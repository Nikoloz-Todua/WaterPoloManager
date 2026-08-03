using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// Builds a project-local TMP fallback from Sylfaen, which contains the Georgian Mkhedruli glyphs
// used by UILocalization. The generated font remains dynamic, but the complete requested ranges are
// baked into its atlas up front so player builds never display TMP's missing-glyph squares.
[InitializeOnLoad]
public static class GeorgianFontAssetBuilder
{
    const string SourceAssetPath = "Assets/Fonts/Sylfaen.ttf";
    const string OutputAssetPath = "Assets/Resources/Fonts/GeorgianFallback SDF.asset";
    const string ModernGeorgianAlphabet = "აბგდევზთიკლმნოპჟრსტუფქღყშჩცძწჭხჯჰ";
    static bool building;

    static GeorgianFontAssetBuilder() => EditorApplication.delayCall += EnsureBuilt;

    [InitializeOnEnterPlayMode]
    static void EnsureBuiltBeforePlay(EnterPlayModeOptions options) => EnsureBuilt();

    [MenuItem("Tools/Water Polo/Rebuild Georgian TMP Font")]
    public static void Rebuild()
    {
        if (building) return;
        building = true;
        try
        {
            EnsureSourceFont();
            Font source = AssetDatabase.LoadAssetAtPath<Font>(SourceAssetPath);
            if (source == null)
            {
                Debug.LogError("GeorgianFontAssetBuilder: Sylfaen.ttf could not be imported.");
                return;
            }

            EnsureFolder("Assets/Resources/Fonts");
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputAssetPath) != null)
                AssetDatabase.DeleteAsset(OutputAssetPath);

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(source, 64, 6,
                GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (fontAsset == null)
            {
                Debug.LogError("GeorgianFontAssetBuilder: TMP could not create the Sylfaen font asset. " +
                               "Verify Include Font Data is enabled on Assets/Fonts/Sylfaen.ttf.");
                return;
            }

            fontAsset.name = "GeorgianFallback SDF";
            fontAsset.atlasTextures[0].name = "GeorgianFallback Atlas";
            fontAsset.material.name = "GeorgianFallback Material";
            AssetDatabase.CreateAsset(fontAsset, OutputAssetPath);
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

            List<uint> characters = new List<uint>();
            AddRange(characters, 0x20, 0x7E);      // labels may mix Georgian with numbers/punctuation
            AddRange(characters, 0x10A0, 0x10FF);  // Georgian
            AddRange(characters, 0x2D00, 0x2D2F);  // Georgian Supplement
            fontAsset.TryAddCharacters(characters.ToArray(), out uint[] missing);

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(OutputAssetPath, ImportAssetOptions.ForceUpdate);

            if (!fontAsset.HasCharacters(ModernGeorgianAlphabet, out List<char> missingModern))
                Debug.LogError("GeorgianFontAssetBuilder: source font is missing required modern Georgian: " +
                               new string(missingModern.ToArray()));
            else
                Debug.Log("GeorgianFontAssetBuilder: generated GeorgianFallback SDF from Sylfaen (" +
                          (missing == null ? 0 : missing.Length) +
                          " unsupported/unassigned range code points skipped).");
        }
        finally { building = false; }
    }

    static void EnsureBuilt()
    {
        EnsureSourceFont();
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputAssetPath);
        if (existing == null || !existing.HasCharacters(ModernGeorgianAlphabet)) Rebuild();
    }

    static void EnsureSourceFont()
    {
        if (File.Exists(SourceAssetPath)) return;
        EnsureFolder("Assets/Fonts");
        string windowsFont = Path.Combine(System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.Fonts), "sylfaen.ttf");
        if (!File.Exists(windowsFont))
        {
            Debug.LogError("GeorgianFontAssetBuilder: Windows Sylfaen font was not found. Copy a " +
                           "Georgian-capable TTF to " + SourceAssetPath + " and rebuild.");
            return;
        }
        File.Copy(windowsFont, SourceAssetPath, false);
        AssetDatabase.ImportAsset(SourceAssetPath, ImportAssetOptions.ForceSynchronousImport);
    }

    static void AddRange(List<uint> values, uint first, uint last)
    {
        for (uint value = first; value <= last; value++) values.Add(value);
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
