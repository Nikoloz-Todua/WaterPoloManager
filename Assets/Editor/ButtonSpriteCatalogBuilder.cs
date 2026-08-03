using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Generates the runtime direct-reference catalog for Assets/Sprites/Buttons. The source art stays in
// that folder; only this tiny ScriptableObject lives in Resources.
[InitializeOnLoad]
public static class ButtonSpriteCatalogBuilder
{
    const int BuildRevision = 3;
    const string CatalogPath = "Assets/Resources/ButtonSpriteCatalog.asset";
    const string ButtonFolder = "Assets/Sprites/Buttons";

    static readonly string[] Keys =
    {
        "Ad-Button", "Back-Button", "Center-Button", "Clubs-Button",
        "Defend-Button", "Defender-Button", "Friends-Button", "Gifts-Button", "I-Button",
        "Keeper-Button", "Lock-Button", "Message-Button", "Missions-Button", "Pass-Button",
        "Pause-Button", "Play-Button", "Ranking-Button", "Season-Pass", "Settings-Button",
        "Shoot-Button", "Shop-Button", "Sprint-Button", "Switch-Button", "Team-Button",
        "Wing-Button"
    };

    static bool rebuilding;

    static ButtonSpriteCatalogBuilder() => EditorApplication.delayCall += EnsureBuilt;

    // The delayed bootstrap can otherwise race a very quick Play click after script reload. Validate
    // synchronously at the Play Mode boundary so runtime code always sees the completed direct refs.
    [InitializeOnEnterPlayMode]
    static void EnsureBuiltBeforePlay(EnterPlayModeOptions options) => EnsureBuilt();

    [MenuItem("Tools/Water Polo/Rebuild Button Sprite Catalog")]
    public static void Rebuild()
    {
        if (rebuilding) return;
        rebuilding = true;
        try
        {
            EnsureFolder("Assets/Resources");
            ButtonSpriteCatalog catalog = AssetDatabase.LoadAssetAtPath<ButtonSpriteCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ButtonSpriteCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            SerializedObject serialized = new SerializedObject(catalog);
            serialized.FindProperty("buildRevision").intValue = BuildRevision;
            SerializedProperty buttons = serialized.FindProperty("buttons");
            buttons.arraySize = Keys.Length;
            List<string> missing = new List<string>();

            for (int i = 0; i < Keys.Length; i++)
            {
                string assetPath = ButtonFolder + "/" + Keys[i] + ".png";
                Rect visibleRect01 = MeasureVisibleRect01(assetPath);
                Sprite sprite = LoadSingleSprite(assetPath, Keys[i], visibleRect01);
                SerializedProperty entry = buttons.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("key").stringValue = Keys[i];
                entry.FindPropertyRelative("sprite").objectReferenceValue = sprite;
                entry.FindPropertyRelative("visibleRect01").rectValue = visibleRect01;
                if (sprite == null) missing.Add(Keys[i]);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            if (missing.Count == 0)
                Debug.Log("ButtonSpriteCatalogBuilder: " + Keys.Length + "/" + Keys.Length +
                          " button sprites linked from Assets/Sprites/Buttons.");
            else
                Debug.LogWarning("ButtonSpriteCatalogBuilder missing: " + string.Join(", ", missing));
        }
        finally { rebuilding = false; }
    }

    static void EnsureBuilt()
    {
        ButtonSpriteCatalog catalog = AssetDatabase.LoadAssetAtPath<ButtonSpriteCatalog>(CatalogPath);
        if (catalog == null || catalog.BuildRevision != BuildRevision || CatalogIsIncomplete(catalog))
            Rebuild();
    }

    static bool CatalogIsIncomplete(ButtonSpriteCatalog catalog)
    {
        if (catalog.Buttons == null || catalog.Buttons.Count != Keys.Length) return true;
        for (int i = 0; i < Keys.Length; i++)
        {
            ButtonSpriteCatalog.Entry entry = catalog.Buttons[i];
            if (entry == null || entry.key != Keys[i] || entry.sprite == null) return true;
        }
        return false;
    }

    static Sprite LoadSingleSprite(string path, string key, Rect visibleRect01)
    {
        if (!File.Exists(path)) return null;
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            bool changed = importer.textureType != TextureImporterType.Sprite ||
                           importer.spriteImportMode != SpriteImportMode.Single ||
                           !importer.alphaIsTransparency || importer.mipmapEnabled;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;

            if (changed) importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Rect MeasureVisibleRect01(string path)
    {
        if (!File.Exists(path)) return new Rect(0f, 0f, 1f, 1f);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(path), false)) return new Rect(0f, 0f, 1f, 1f);
            Color32[] pixels = texture.GetPixels32();
            int minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;
            const byte alphaCutoff = 18;
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                if (pixels[y * texture.width + x].a <= alphaCutoff) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            if (maxX < minX || maxY < minY) return new Rect(0f, 0f, 1f, 1f);

            // Keep a small glow-safe gutter so protruding symbols and shine never get shaved off.
            int padX = Mathf.Max(2, Mathf.RoundToInt(texture.width * 0.008f));
            int padY = Mathf.Max(2, Mathf.RoundToInt(texture.height * 0.008f));
            minX = Mathf.Max(0, minX - padX); minY = Mathf.Max(0, minY - padY);
            maxX = Mathf.Min(texture.width - 1, maxX + padX);
            maxY = Mathf.Min(texture.height - 1, maxY + padY);
            return new Rect(minX / (float)texture.width, minY / (float)texture.height,
                (maxX - minX + 1) / (float)texture.width,
                (maxY - minY + 1) / (float)texture.height);
        }
        finally { Object.DestroyImmediate(texture); }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
