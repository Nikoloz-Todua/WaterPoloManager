#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Editor-only setup helper. It creates direct Sprite references, so all crests remain bundled and
// usable offline; no AssetDatabase call exists in runtime code.
public static class ClubCatalogBuilder
{
    const string CatalogPath = "Assets/Resources/ClubCatalog.asset";
    const string GeneratedPrefix = "ClubTrim_";
    const int BuildRevision = 2;
    static bool rebuilding;
    static readonly (string id, string level, string assetName)[] Clubs =
    {
        ("Alnguard", "MID", "Alnguard"), ("Apollon", "MID-TOP", "Apollon"), ("Arenna", "LOW", "Arenna"),
        ("Astinna", "LOW-MID", "Astinna"), ("Aurelio-Posillipo", "MID", "Aurelio-Posillipo"), ("Barcelona", "MID", "Barcelona"),
        ("Crab", "TOP", "Crab"), ("Dabrovnik", "MID-TOP", "Dabrovnik"), ("Didi-Orod", "LOW", "Didi-Orod"),
        ("Dinamo", "LOW-MID", "Dinamo"), ("Ineri", "LOW", "Ineri"), ("Jordani", "TOP", "Jordani"),
        ("Locomoco", "LOW", "Locomoco"), ("Marselo", "MID-TOP", "Marselo"), ("Matador", "MID-TOP", "Matador"),
        ("mlodest", "MID-TOP", "mlodest"), ("Mularis-Dubonic", "MID", "Mularis-Dubonic"), ("New-Grand", "TOP", "New-Grand"),
        ("Olimpi", "TOP", "Olimpi"), ("Piranias", "MID", "Piranias"), ("Poseidon", "LOW-MID", "Poseidon"),
        ("Prianik", "MID-TOP", "Prianik"), ("Pru-Rico", "TOP", "Pru-Rico"), ("Radni", "MID-TOP", "Radni"),
        ("Randolla", "MID", "Randolla"), ("Red-Star", "MID", "Red-Star"), ("Saas-Planka", "MID-TOP", "Saas-Planka"),
        ("Sebedel", "TOP", "Sebedel"), ("Spartakus", "MID", "Spartakus"), ("Stu-Bucha", "MID", "Stua-Bucha"),
        ("Tbili", "LOW", "Tbili"), ("Vipa-Pospo", "MID-TOP", "Vipa-Pospo"), ("WP-Lions", "TOP", "WP-Lions"), ("WTC", "TOP", "WTC")
    };

    [InitializeOnLoadMethod]
    static void EnsureOnImport()
    {
        EditorApplication.delayCall += () =>
        {
            ClubCatalog catalog = AssetDatabase.LoadAssetAtPath<ClubCatalog>(CatalogPath);
            if (catalog == null || catalog.BuildRevision != BuildRevision) Rebuild();
        };
    }

    [MenuItem("Tools/Water Polo/Rebuild Club Catalog")]
    public static void Rebuild()
    {
        if (rebuilding) return;
        rebuilding = true;
        try
        {
        ClubCatalog catalog = AssetDatabase.LoadAssetAtPath<ClubCatalog>(CatalogPath);
        if (catalog == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            catalog = ScriptableObject.CreateInstance<ClubCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        foreach (Object child in AssetDatabase.LoadAllAssetsAtPath(CatalogPath))
        {
            if (child != null && child != catalog && child.name.StartsWith(GeneratedPrefix, System.StringComparison.Ordinal))
                Object.DestroyImmediate(child, true);
        }

        SerializedObject so = new SerializedObject(catalog);
        so.FindProperty("buildRevision").intValue = BuildRevision;
        SerializedProperty list = so.FindProperty("clubs");
        list.arraySize = Clubs.Length;
        for (int i = 0; i < Clubs.Length; i++)
        {
            SerializedProperty item = list.GetArrayElementAtIndex(i);
            item.FindPropertyRelative("id").stringValue = Clubs[i].id;
            item.FindPropertyRelative("displayName").stringValue = Clubs[i].id;
            item.FindPropertyRelative("level").stringValue = Clubs[i].level;
            string[] matches = AssetDatabase.FindAssets(Clubs[i].assetName + " t:Sprite", new[] { "Assets/Sprites/Clubs" });
            Sprite sprite = null;
            foreach (string guid in matches)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == Clubs[i].assetName) { sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path); break; }
            }
            item.FindPropertyRelative("logo").objectReferenceValue = CreateTightLogo(sprite, Clubs[i].id, catalog);
            if (sprite == null) Debug.LogWarning("ClubCatalogBuilder: missing logo for " + Clubs[i].id);
        }
        so.FindProperty("division1Trophy").objectReferenceValue = FindSprite("Division1", "Assets/Sprites/Trophies");
        so.FindProperty("premierLeagueTrophy").objectReferenceValue = FindSprite("Premier-League", "Assets/Sprites/Trophies");
        so.FindProperty("continentalCupTrophy").objectReferenceValue = FindSprite("Continental-Cup", "Assets/Sprites/Trophies");
        so.FindProperty("championsLeagueTrophy").objectReferenceValue = FindSprite("Champions-League", "Assets/Sprites/Trophies");
        so.FindProperty("goldMedal").objectReferenceValue = FindSprite("Gold-Medal", "Assets/Sprites/Trophies");
        so.FindProperty("silverMedal").objectReferenceValue = FindSprite("Silver-Medal", "Assets/Sprites/Trophies");
        so.FindProperty("bronzeMedal").objectReferenceValue = FindSprite("Bronze-Medal", "Assets/Sprites/Trophies");
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        }
        finally
        {
            rebuilding = false;
        }
    }

    // Club PNGs have wildly different transparent margins. Baking a tightly cropped copy makes
    // every crest fill the same UI badge without modifying the artist's source asset.
    static Sprite CreateTightLogo(Sprite source, string id, ClubCatalog catalog)
    {
        if (source == null) return null;

        string assetPath = AssetDatabase.GetAssetPath(source);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        bool restoreReadable = importer != null && !importer.isReadable;
        if (restoreReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
            source = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        try
        {
            Texture2D texture = source.texture;
            Rect sourceRect = source.textureRect;
            int left = Mathf.Clamp(Mathf.FloorToInt(sourceRect.x), 0, texture.width - 1);
            int bottom = Mathf.Clamp(Mathf.FloorToInt(sourceRect.y), 0, texture.height - 1);
            int right = Mathf.Clamp(Mathf.CeilToInt(sourceRect.xMax) - 1, left, texture.width - 1);
            int top = Mathf.Clamp(Mathf.CeilToInt(sourceRect.yMax) - 1, bottom, texture.height - 1);
            Color32[] pixels = texture.GetPixels32();
            int minX = right;
            int minY = top;
            int maxX = left;
            int maxY = bottom;
            bool found = false;

            for (int y = bottom; y <= top; y++)
            for (int x = left; x <= right; x++)
            {
                if (pixels[y * texture.width + x].a <= 18) continue;
                found = true;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }

            if (!found)
            {
                minX = left;
                minY = bottom;
                maxX = right;
                maxY = top;
            }

            int padding = Mathf.Max(3, Mathf.RoundToInt(Mathf.Max(maxX - minX + 1, maxY - minY + 1) * 0.035f));
            minX = Mathf.Max(left, minX - padding);
            minY = Mathf.Max(bottom, minY - padding);
            maxX = Mathf.Min(right, maxX + padding);
            maxY = Mathf.Min(top, maxY + padding);
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;

            Texture2D cropped = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = GeneratedPrefix + id + "_Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideInHierarchy
            };
            cropped.SetPixels(texture.GetPixels(minX, minY, width, height));
            cropped.Apply(false, true);
            AssetDatabase.AddObjectToAsset(cropped, catalog);

            Sprite result = Sprite.Create(
                cropped,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                source.pixelsPerUnit,
                0,
                SpriteMeshType.FullRect);
            result.name = GeneratedPrefix + id;
            result.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(result, catalog);
            return result;
        }
        finally
        {
            if (restoreReadable)
            {
                importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
            }
        }
    }

    static Sprite FindSprite(string name, string folder)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Sprite", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        Debug.LogWarning("ClubCatalogBuilder: missing art " + name);
        return null;
    }
}
#endif
