#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Splits the 20 flat crest sources into packed tint masks:
// R = primary, G = secondary, B = tertiary, A = fixed outline/details.
public static class CrestTemplateBuilder
{
    const string SourceFolder = "Assets/Sprites/CrestTemplates";
    const string OutputFolder = "Assets/Resources/CrestMasks";
    const string CatalogPath = "Assets/Resources/CrestTemplateCatalog.asset";
    const string MaterialPath = "Assets/Resources/Materials/CrestMaskTint.mat";
    const string QcPath = OutputFolder + "/CrestTemplate_QC.txt";
    const int TemplateCount = 20;
    const int BuildRevision = 3;
    const float RegionDistanceTolerance = 0.30f;
    const float MinimumCoverage = 0.010f;
    const int FeatherRadius = 2;
    static bool rebuilding;

    sealed class Bin
    {
        public Vector3 color;
        public int count;
    }

    sealed class Cluster
    {
        public Vector3 center;
        public double weight;
    }

    sealed class BuildResult
    {
        public bool valid;
        public Texture2D mask;
        public float[] coverage = new float[3];
        public Vector3[] colors = new Vector3[3];
        public string note;
    }

    [InitializeOnLoadMethod]
    static void EnsureOnImport()
    {
        EditorApplication.delayCall += () =>
        {
            if (rebuilding) return;
            CrestTemplateCatalog catalog = AssetDatabase.LoadAssetAtPath<CrestTemplateCatalog>(CatalogPath);
            string signature = SourceSignature();
            if (catalog == null || catalog.BuildRevision != BuildRevision ||
                catalog.SourceSignature != signature || catalog.Count != TemplateCount)
                Rebuild();
        };
    }

    [MenuItem("Tools/Water Polo/Rebuild Crest Templates")]
    public static void Rebuild()
    {
        if (rebuilding) return;
        rebuilding = true;
        List<string> importerPaths = new List<string>();
        StringBuilder qc = new StringBuilder();
        qc.AppendLine("CREST TEMPLATE QC");
        qc.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        qc.AppendLine("Meaningful region threshold: " + (MinimumCoverage * 100f).ToString("0.0") + "% of opaque coverage");
        qc.AppendLine();

        try
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder(OutputFolder);
            EnsureFolder("Assets/Resources/Materials");

            Material material = EnsureMaterial();
            CrestTemplateCatalog catalog = AssetDatabase.LoadAssetAtPath<CrestTemplateCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CrestTemplateCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            SerializedObject serialized = new SerializedObject(catalog);
            serialized.FindProperty("buildRevision").intValue = BuildRevision;
            serialized.FindProperty("sourceSignature").stringValue = SourceSignature();
            serialized.FindProperty("tintMaterial").objectReferenceValue = material;
            SerializedProperty entries = serialized.FindProperty("templates");
            entries.arraySize = TemplateCount;

            int clean = 0;
            List<string> failed = new List<string>();
            for (int index = 0; index < TemplateCount; index++)
            {
                string id = "Template" + (index + 1).ToString("00");
                string sourcePath = SourceFolder + "/" + id + ".png";
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("id").stringValue = id;
                entry.FindPropertyRelative("valid").boolValue = false;
                entry.FindPropertyRelative("mask").objectReferenceValue = null;
                entry.FindPropertyRelative("primaryCoverage").floatValue = 0f;
                entry.FindPropertyRelative("secondaryCoverage").floatValue = 0f;
                entry.FindPropertyRelative("tertiaryCoverage").floatValue = 0f;

                Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
                if (source == null)
                {
                    string missing = "MISSING SOURCE: " + sourcePath;
                    entry.FindPropertyRelative("qcNote").stringValue = missing;
                    qc.AppendLine("[FAIL] " + id + " — " + missing);
                    failed.Add(id);
                    continue;
                }

                TextureImporter sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
                if (sourceImporter != null && !sourceImporter.isReadable)
                {
                    sourceImporter.isReadable = true;
                    sourceImporter.SaveAndReimport();
                    importerPaths.Add(sourcePath);
                    source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
                }

                BuildResult result;
                try { result = Split(source); }
                catch (Exception exception)
                {
                    result = new BuildResult { valid = false, note = exception.Message };
                }

                entry.FindPropertyRelative("qcNote").stringValue = result.note ?? "";
                if (!result.valid || result.mask == null)
                {
                    qc.AppendLine("[FAIL] " + id + " — " + result.note);
                    failed.Add(id);
                    if (result.mask != null) UnityEngine.Object.DestroyImmediate(result.mask);
                    continue;
                }

                string maskPath = OutputFolder + "/" + id + "_Mask.png";
                File.WriteAllBytes(maskPath, result.mask.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(result.mask);
                AssetDatabase.ImportAsset(maskPath, ImportAssetOptions.ForceUpdate);
                ConfigureMaskImporter(maskPath);
                Sprite maskSprite = AssetDatabase.LoadAssetAtPath<Sprite>(maskPath);

                bool validAsset = maskSprite != null;
                entry.FindPropertyRelative("valid").boolValue = validAsset;
                entry.FindPropertyRelative("mask").objectReferenceValue = maskSprite;
                entry.FindPropertyRelative("primaryCoverage").floatValue = result.coverage[0];
                entry.FindPropertyRelative("secondaryCoverage").floatValue = result.coverage[1];
                entry.FindPropertyRelative("tertiaryCoverage").floatValue = result.coverage[2];

                string colors = "RGB centers " + Html(result.colors[0]) + " / " +
                                Html(result.colors[1]) + " / " + Html(result.colors[2]);
                string coverage = "coverage " + Percent(result.coverage[0]) + " / " +
                                  Percent(result.coverage[1]) + " / " + Percent(result.coverage[2]);
                if (validAsset)
                {
                    clean++;
                    qc.AppendLine("[PASS] " + id + " — " + coverage + "; " + colors);
                }
                else
                {
                    failed.Add(id);
                    entry.FindPropertyRelative("qcNote").stringValue = "Mask asset failed to import.";
                    qc.AppendLine("[FAIL] " + id + " — mask asset failed to import.");
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            qc.AppendLine();
            qc.AppendLine("SUMMARY: " + clean + "/" + TemplateCount + " split cleanly.");
            qc.AppendLine(failed.Count == 0
                ? "Manual look: none."
                : "Manual look required: " + string.Join(", ", failed));
            File.WriteAllText(QcPath, qc.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(QcPath, ImportAssetOptions.ForceUpdate);

            string summary = "CrestTemplateBuilder: " + clean + "/" + TemplateCount +
                             " templates passed QC. Report: " + QcPath;
            if (failed.Count == 0) Debug.Log(summary);
            else Debug.LogWarning(summary + ". Manual look: " + string.Join(", ", failed));
        }
        finally
        {
            foreach (string path in importerPaths)
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
            rebuilding = false;
        }
    }

    static BuildResult Split(Texture2D source)
    {
        BuildResult result = new BuildResult();
        if (source.width != 1024 || source.height != 1024)
        {
            result.note = "Expected 1024x1024, got " + source.width + "x" + source.height + ".";
            return result;
        }

        Color32[] pixels = source.GetPixels32();
        List<Bin> bins = BuildHistogram(pixels);
        if (bins.Count < 3)
        {
            result.note = "Fewer than three non-black opaque color clusters were detected.";
            return result;
        }

        List<Cluster> clusters = KMeans3(bins);
        if (clusters.Count != 3)
        {
            result.note = "Could not initialize three distinct fill colors.";
            return result;
        }
        clusters.Sort((a, b) => b.weight.CompareTo(a.weight));
        for (int i = 0; i < 3; i++) result.colors[i] = clusters[i].center;

        float minSeparation = Mathf.Min(
            Vector3.Distance(clusters[0].center, clusters[1].center),
            Mathf.Min(Vector3.Distance(clusters[0].center, clusters[2].center),
                      Vector3.Distance(clusters[1].center, clusters[2].center)));
        if (minSeparation < 0.10f)
        {
            result.note = "Detected fill colors are not distinct enough (minimum distance " +
                          minSeparation.ToString("0.000") + ").";
            return result;
        }

        int width = source.width;
        int height = source.height;
        float[][] channels =
        {
            new float[pixels.Length], new float[pixels.Length],
            new float[pixels.Length], new float[pixels.Length]
        };
        double opaqueWeight = 0d;
        double[] regionWeight = new double[3];

        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 pixel = pixels[i];
            float alpha = pixel.a / 255f;
            if (alpha <= 0.01f) continue;
            opaqueWeight += alpha;
            Vector3 color = ToVector(pixel);

            int category = 3;
            if (!IsBlackish(color))
            {
                int nearest = 0;
                float nearestDistance = Vector3.Distance(color, clusters[0].center);
                for (int c = 1; c < 3; c++)
                {
                    float distance = Vector3.Distance(color, clusters[c].center);
                    if (distance < nearestDistance) { nearest = c; nearestDistance = distance; }
                }
                if (nearestDistance <= RegionDistanceTolerance) category = nearest;
            }

            channels[category][i] = alpha;
            if (category < 3) regionWeight[category] += alpha;
        }

        if (opaqueWeight <= 1d)
        {
            result.note = "No meaningful opaque pixels.";
            return result;
        }

        List<string> weak = new List<string>();
        string[] names = { "primary", "secondary", "tertiary" };
        for (int i = 0; i < 3; i++)
        {
            result.coverage[i] = (float)(regionWeight[i] / opaqueWeight);
            if (result.coverage[i] < MinimumCoverage) weak.Add(names[i] + " " + Percent(result.coverage[i]));
        }
        if (weak.Count > 0)
        {
            result.note = "Meaningful coverage validation failed: " + string.Join(", ", weak) +
                          ". Check for a transparent/absent fill region.";
            return result;
        }

        for (int i = 0; i < 4; i++) channels[i] = BoxBlur(channels[i], width, height, FeatherRadius);
        Color32[] packed = new Color32[pixels.Length];
        for (int i = 0; i < packed.Length; i++)
            packed[i] = new Color32(ToByte(channels[0][i]), ToByte(channels[1][i]),
                                    ToByte(channels[2][i]), ToByte(channels[3][i]));

        Texture2D mask = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = source.name + "_Mask",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        mask.SetPixels32(packed);
        mask.Apply(false, false);
        result.valid = true;
        result.mask = mask;
        result.note = "Clean split.";
        return result;
    }

    static List<Bin> BuildHistogram(Color32[] pixels)
    {
        Dictionary<int, int> counts = new Dictionary<int, int>();
        foreach (Color32 pixel in pixels)
        {
            if (pixel.a < 160) continue;
            Vector3 color = ToVector(pixel);
            if (IsBlackish(color)) continue;
            int r = pixel.r >> 3;
            int g = pixel.g >> 3;
            int b = pixel.b >> 3;
            int key = (r << 10) | (g << 5) | b;
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        List<Bin> bins = new List<Bin>(counts.Count);
        foreach (KeyValuePair<int, int> pair in counts)
        {
            int r = (pair.Key >> 10) & 31;
            int g = (pair.Key >> 5) & 31;
            int b = pair.Key & 31;
            bins.Add(new Bin
            {
                color = new Vector3((r + 0.5f) / 32f, (g + 0.5f) / 32f, (b + 0.5f) / 32f),
                count = pair.Value
            });
        }
        bins.Sort((a, b) => b.count.CompareTo(a.count));
        return bins;
    }

    static List<Cluster> KMeans3(List<Bin> bins)
    {
        List<Cluster> clusters = new List<Cluster>();
        clusters.Add(new Cluster { center = bins[0].color });
        while (clusters.Count < 3)
        {
            Bin best = null;
            double bestScore = -1d;
            foreach (Bin bin in bins)
            {
                float nearest = float.MaxValue;
                foreach (Cluster cluster in clusters)
                    nearest = Mathf.Min(nearest, Vector3.SqrMagnitude(bin.color - cluster.center));
                double score = nearest * Math.Sqrt(bin.count);
                if (score > bestScore) { bestScore = score; best = bin; }
            }
            if (best == null || bestScore <= 0d) break;
            clusters.Add(new Cluster { center = best.color });
        }
        if (clusters.Count != 3) return clusters;

        for (int iteration = 0; iteration < 14; iteration++)
        {
            Vector3[] sums = new Vector3[3];
            double[] weights = new double[3];
            foreach (Bin bin in bins)
            {
                int nearest = 0;
                float distance = Vector3.SqrMagnitude(bin.color - clusters[0].center);
                for (int c = 1; c < 3; c++)
                {
                    float candidate = Vector3.SqrMagnitude(bin.color - clusters[c].center);
                    if (candidate < distance) { nearest = c; distance = candidate; }
                }
                sums[nearest] += bin.color * bin.count;
                weights[nearest] += bin.count;
            }
            for (int c = 0; c < 3; c++)
            {
                clusters[c].weight = weights[c];
                if (weights[c] > 0d) clusters[c].center = sums[c] / (float)weights[c];
            }
        }
        return clusters;
    }

    static float[] BoxBlur(float[] source, int width, int height, int radius)
    {
        float[] horizontal = new float[source.Length];
        float[] output = new float[source.Length];
        int diameter = radius * 2 + 1;
        for (int y = 0; y < height; y++)
        {
            float sum = 0f;
            for (int x = -radius; x <= radius; x++)
                sum += source[y * width + Mathf.Clamp(x, 0, width - 1)];
            for (int x = 0; x < width; x++)
            {
                horizontal[y * width + x] = sum / diameter;
                int remove = Mathf.Clamp(x - radius, 0, width - 1);
                int add = Mathf.Clamp(x + radius + 1, 0, width - 1);
                sum += source[y * width + add] - source[y * width + remove];
            }
        }
        for (int x = 0; x < width; x++)
        {
            float sum = 0f;
            for (int y = -radius; y <= radius; y++)
                sum += horizontal[Mathf.Clamp(y, 0, height - 1) * width + x];
            for (int y = 0; y < height; y++)
            {
                output[y * width + x] = Mathf.Clamp01(sum / diameter);
                int remove = Mathf.Clamp(y - radius, 0, height - 1);
                int add = Mathf.Clamp(y + radius + 1, 0, height - 1);
                sum += horizontal[add * width + x] - horizontal[remove * width + x];
            }
        }
        return output;
    }

    static Material EnsureMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Shader shader = Shader.Find("UI/Crest Mask Tint");
        if (shader == null) throw new InvalidOperationException("Shader 'UI/Crest Mask Tint' was not found.");
        if (material == null)
        {
            material = new Material(shader) { name = "CrestMaskTint" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
            EditorUtility.SetDirty(material);
        }
        return material;
    }

    static void ConfigureMaskImporter(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.sRGBTexture = false;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    static string SourceSignature()
    {
        StringBuilder value = new StringBuilder(BuildRevision.ToString());
        for (int i = 1; i <= TemplateCount; i++)
        {
            string path = SourceFolder + "/Template" + i.ToString("00") + ".png";
            value.Append('|').Append(AssetDatabase.AssetPathExists(path)
                ? AssetDatabase.GetAssetDependencyHash(path).ToString()
                : "missing");
        }
        return Hash128.Compute(value.ToString()).ToString();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    static Vector3 ToVector(Color32 c) => new Vector3(c.r / 255f, c.g / 255f, c.b / 255f);
    static bool IsBlackish(Vector3 c)
    {
        float maximum = Mathf.Max(c.x, Mathf.Max(c.y, c.z));
        // Only true/near-neutral black is fixed outline. Several supplied templates deliberately
        // use dark maroon, navy or charcoal as their third tintable fill.
        return maximum < 0.055f;
    }
    static byte ToByte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
    static string Percent(float value) => (value * 100f).ToString("0.00") + "%";
    static string Html(Vector3 value) => "#" + ColorUtility.ToHtmlStringRGB(new Color(value.x, value.y, value.z));
}
#endif
