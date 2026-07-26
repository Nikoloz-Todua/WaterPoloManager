using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Generates one bundled CountryCatalog asset, mirroring ClubCatalogBuilder's direct-reference pattern.
[InitializeOnLoad]
public static class CountryCatalogBuilder
{
    const int BuildRevision = 1;
    const string CatalogPath = "Assets/Resources/CountryCatalog.asset";
    const string FlagFolder = "Assets/Sprites/Countries";

    static readonly (string country, int rate)[] Data =
    {
        ("Georgia", 69), ("Croatia", 85), ("Hungary", 90), ("Japan", 63),
        ("Canada", 60), ("UK", 48), ("China", 59), ("Austria", 42),
        ("Spain", 93), ("Serbia", 86), ("USA", 76), ("Australia", 70),
        ("Israel", 51), ("Malta", 45), ("Sweden", 40), ("Latvia", 38),
        ("Italy", 82), ("Montenegro", 78), ("Russia", 57), ("Netherlands", 68),
        ("Kazakhstan", 55), ("Slovenia", 52), ("Iran", 53), ("Azerbaijan", 36),
        ("Armenia", 34), ("France", 74), ("Greece", 88), ("Romania", 72),
        ("Germany", 65), ("Turkey", 50), ("Poland", 44), ("Ukraine", 47),
        ("Lithuania", 41), ("Slovakia", 54), ("Mexico", 35), ("Portugal", 39)
    };

    static bool rebuilding;

    static CountryCatalogBuilder()
    {
        EditorApplication.delayCall += EnsureBuilt;
    }

    [MenuItem("Tools/Water Polo/Rebuild Country Catalog")]
    public static void Rebuild()
    {
        if (rebuilding) return;
        rebuilding = true;
        try
        {
            EnsureFolder("Assets/Resources");
            CountryCatalog catalog = AssetDatabase.LoadAssetAtPath<CountryCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CountryCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            SerializedObject serialized = new SerializedObject(catalog);
            serialized.FindProperty("buildRevision").intValue = BuildRevision;
            SerializedProperty countries = serialized.FindProperty("countries");
            countries.arraySize = Data.Length;

            List<string> missing = new List<string>();
            for (int i = 0; i < Data.Length; i++)
            {
                SerializedProperty entry = countries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("country").stringValue = Data[i].country;
                entry.FindPropertyRelative("winRate").intValue = Data[i].rate;
                Sprite flag = FindFlag(Data[i].country);
                entry.FindPropertyRelative("flag").objectReferenceValue = flag;
                if (flag == null) missing.Add(Data[i].country);
            }

            serialized.FindProperty("worldCupTrophy").objectReferenceValue =
                LoadSprite("Assets/Sprites/Trophies/WorldCup.png");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            if (missing.Count == 0)
                Debug.Log("CountryCatalogBuilder: 36/36 countries and World Cup trophy linked.");
            else
                Debug.LogWarning("CountryCatalogBuilder missing flags: " + string.Join(", ", missing));
        }
        finally { rebuilding = false; }
    }

    static void EnsureBuilt()
    {
        CountryCatalog catalog = AssetDatabase.LoadAssetAtPath<CountryCatalog>(CatalogPath);
        if (catalog == null || catalog.BuildRevision != BuildRevision) Rebuild();
    }

    static Sprite FindFlag(string country)
    {
        // The supplied Sweden source is currently misspelled "Swedan"; prefer the exact filename
        // whenever it appears later, with the current typo as a compatibility fallback.
        string[] basenames = country == "Sweden" ? new[] { "Sweden", "Swedan" } : new[] { country };
        foreach (string basename in basenames)
        foreach (string extension in new[] { ".png", ".jpg", ".jpeg" })
        {
            string path = FlagFolder + "/" + basename + extension;
            if (File.Exists(path)) return LoadSprite(path);
        }
        return null;
    }

    static Sprite LoadSprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite != null) return sprite;
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
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
