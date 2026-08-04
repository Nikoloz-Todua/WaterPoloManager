using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Events;

// Third-pass presentation rules shared by every code-built screen. The project creates most UI at
// runtime, so a small persistent auditor catches both scene-authored labels and labels created later
// by popups without requiring every screen to remember typography setup.
public static class UniversalUIStyle
{
    static Sprite circle;
    static readonly Dictionary<string, Sprite> backgroundCache = new Dictionary<string, Sprite>();

    // The supplied backgrounds are imported as one-sprite sheets. LoadAll handles that importer
    // mode reliably while keeping every screen independent of the generated sub-sprite suffix.
    public static Sprite LoadBackground(string assetName)
    {
        string path = assetName.StartsWith("Sprites/") ? assetName : "Sprites/" + assetName;
        if (backgroundCache.TryGetValue(path, out Sprite cached)) return cached;

        Sprite sprite = Resources.Load<Sprite>(path);
        if (sprite == null)
        {
            Sprite[] sprites = Resources.LoadAll<Sprite>(path);
            if (sprites != null && sprites.Length > 0) sprite = sprites[0];
        }

        backgroundCache[path] = sprite;
        return sprite;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Object.FindAnyObjectByType<UniversalTypographyAuditor>() != null) return;
        GameObject go = new GameObject("Universal UI Style");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<UniversalTypographyAuditor>();
    }

    public static void ApplyTypography(TextMeshProUGUI label)
    {
        if (label == null) return;
        UILocalization.ApplyLocalizedFont(label);

        // extraPadding asks TMP to reserve material/mesh space around glyphs. This is the safest
        // global clipping fix because it does not shrink the authored RectTransform text bounds.
        label.extraPadding = true;
        if (label.textWrappingMode == TextWrappingModes.NoWrap &&
            label.overflowMode == TextOverflowModes.Overflow)
            label.overflowMode = TextOverflowModes.Ellipsis;

        Shadow shadow = null;
        Shadow[] effects = label.GetComponents<Shadow>();
        for (int i = 0; i < effects.Length; i++)
            if (!(effects[i] is Outline)) { shadow = effects[i]; break; }
        if (shadow == null) shadow = label.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.002f, 0.01f, 0.03f, 0.96f);
        shadow.effectDistance = new Vector2(1.75f, -2.75f);
        shadow.useGraphicAlpha = true;

        Outline outline = label.GetComponent<Outline>();
        if (outline == null) outline = label.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.002f, 0.008f, 0.025f, 0.88f);
        outline.effectDistance = new Vector2(1.15f, -1.15f);
        outline.useGraphicAlpha = true;
    }

    // Country-selector close treatment: bright circular rim, inset dark-red face, crisp vector X.
    public static Button MakeCloseButton(Transform parent, Vector2 anchor, Vector2 position,
                                         Vector2 size, UnityAction onClick)
    {
        GameObject go = new GameObject("BtnClose");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        Image outer = go.AddComponent<Image>();
        outer.sprite = Circle();
        outer.preserveAspect = true;
        outer.color = new Color(0.92f, 0.18f, 0.23f, 1f);
        Shadow shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.48f);
        shadow.effectDistance = new Vector2(0f, -3f);
        shadow.useGraphicAlpha = true;

        float diameter = Mathf.Min(size.x, size.y);
        Image inner = ChildImage(go.transform, "Inner", Circle(),
            new Color(0.30f, 0.035f, 0.055f, 0.94f), new Vector2(diameter - 8f, diameter - 8f));
        inner.preserveAspect = true;

        float slashLength = diameter * 0.45f;
        float slashWidth = Mathf.Max(4f, diameter * 0.085f);
        Image slashA = ChildImage(go.transform, "CloseSlashA", CrestUITheme.Rounded(), Color.white,
                                  new Vector2(slashWidth, slashLength));
        slashA.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Image slashB = ChildImage(go.transform, "CloseSlashB", CrestUITheme.Rounded(), Color.white,
                                  new Vector2(slashWidth, slashLength));
        slashB.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = outer;
        if (onClick != null) button.onClick.AddListener(onClick);
        AddCloseInteraction(go);
        return button;
    }

    static void AddCloseInteraction(GameObject go)
    {
        EventTrigger trigger = go.AddComponent<EventTrigger>();
        EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => { if (go != null) go.transform.localScale = Vector3.one * 1.06f; });
        trigger.triggers.Add(enter);
        EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => { if (go != null) go.transform.localScale = Vector3.one; });
        trigger.triggers.Add(exit);
        EventTrigger.Entry down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        down.callback.AddListener(_ => { if (go != null) go.transform.localScale = Vector3.one * 0.94f; });
        trigger.triggers.Add(down);
        EventTrigger.Entry up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener(_ => { if (go != null) go.transform.localScale = Vector3.one * 1.06f; });
        trigger.triggers.Add(up);
    }

    static Image ChildImage(Transform parent, string name, Sprite sprite, Color color, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite == CrestUITheme.Rounded() ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        RectTransform rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        return image;
    }

    static Sprite Circle()
    {
        if (circle != null) return circle;
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "UniversalUI_CloseCircle";
        Color32[] pixels = new Color32[size * size];
        float radius = size * 0.5f - 1f;
        Vector2 center = new Vector2((size - 1f) * 0.5f, (size - 1f) * 0.5f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float alpha = Mathf.Clamp01(radius - Vector2.Distance(new Vector2(x, y), center));
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        circle = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        circle.name = "UniversalUI_CloseCircle";
        return circle;
    }
}

public sealed class UniversalTypographyAuditor : MonoBehaviour
{
    readonly HashSet<TextMeshProUGUI> styled = new HashSet<TextMeshProUGUI>();
    float nextAudit;

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Audit();
    }

    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) { styled.Clear(); nextAudit = 0f; }

    void Update()
    {
        if (Time.unscaledTime < nextAudit) return;
        Audit();
        nextAudit = Time.unscaledTime + 0.5f;
    }

    void Audit()
    {
        TextMeshProUGUI[] labels = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include);
        for (int i = 0; i < labels.Length; i++)
        {
            TextMeshProUGUI label = labels[i];
            if (label == null || !styled.Add(label)) continue;
            UniversalUIStyle.ApplyTypography(label);
        }
    }
}
