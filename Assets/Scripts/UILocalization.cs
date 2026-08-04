using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Small offline localization foundation for code-built UI. Existing callers may continue passing
// English source labels; LocalizedButtonText resolves them from the saved language and keeps text
// fitted if the language changes while a screen is alive.
public static class UILocalization
{
    public const string PreferenceKey = "ui_language";
    public const string English = "en";
    public const string Georgian = "ka";
    public const string Russian = "ru";
    const string GeorgianFontResource = "Fonts/GeorgianFallback SDF";

    static TMP_FontAsset georgianFont;

    static readonly Dictionary<string, string> GeorgianText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "PLAY", "თამაში" }, { "RANKING", "რეიტინგი" }, { "SEASON PASS", "სეზონური საშვი" },
        { "SHOP", "მაღაზია" }, { "TEAM", "გუნდი" }, { "MISSIONS", "მისიები" },
        { "LEAGUE", "ლიგა" }, { "ELITE LEAGUE", "ელიტური ლიგა" }, { "WORLD", "მსოფლიო" },
        { "FRIENDS", "მეგობრები" }, { "COUNTRY", "ქვეყანა" }, { "LAST WEEK", "გასული კვირა" },
        { "FORMATIONS", "ფორმაციები" }, { "PLAYERS", "მოთამაშეები" },
        { "SUBSTITUTIONS", "ცვლილებები" }, { "SAVE CLUB", "კლუბის შენახვა" },
        { "CLUBS", "კლუბები" }, { "WINGS", "ფლანგები" }, { "CENTER", "ცენტრი" },
        { "DEFENSE", "დაცვა" }, { "GK", "მეკარე" }, { "RESTART", "თავიდან" },
        { "RESUME", "გაგრძელება" }, { "QUIT", "გასვლა" }, { "CANCEL", "გაუქმება" },
        { "CONTINUE", "გაგრძელება" }, { "MAIN MENU", "მთავარი მენიუ" },
        { "TEAM MANAGEMENT", "გუნდის მართვა" }, { "CLAIM", "მიღება" },
        { "ACTIVATE", "გააქტიურება" }, { "OK", "კარგი" }
    };

    static readonly Dictionary<string, string> RussianText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "PLAY", "ИГРАТЬ" }, { "RANKING", "РЕЙТИНГ" }, { "SEASON PASS", "СЕЗОННЫЙ ПРОПУСК" },
        { "SHOP", "МАГАЗИН" }, { "TEAM", "КОМАНДА" }, { "MISSIONS", "МИССИИ" },
        { "LEAGUE", "ЛИГА" }, { "ELITE LEAGUE", "ЭЛИТНАЯ ЛИГА" }, { "WORLD", "МИР" },
        { "FRIENDS", "ДРУЗЬЯ" }, { "COUNTRY", "СТРАНА" }, { "LAST WEEK", "ПРОШЛАЯ НЕДЕЛЯ" },
        { "FORMATIONS", "СХЕМЫ" }, { "PLAYERS", "ИГРОКИ" }, { "SUBSTITUTIONS", "ЗАМЕНЫ" },
        { "SAVE CLUB", "СОХРАНИТЬ КЛУБ" }, { "RESUME", "ПРОДОЛЖИТЬ" }, { "QUIT", "ВЫЙТИ" },
        { "CLUBS", "КЛУБЫ" }, { "WINGS", "ФЛАНГИ" }, { "CENTER", "ЦЕНТР" },
        { "DEFENSE", "ЗАЩИТА" }, { "GK", "ВРАТАРЬ" }, { "RESTART", "ПЕРЕЗАПУСК" },
        { "CANCEL", "ОТМЕНА" }, { "CONTINUE", "ПРОДОЛЖИТЬ" }, { "MAIN MENU", "ГЛАВНОЕ МЕНЮ" },
        { "TEAM MANAGEMENT", "УПРАВЛЕНИЕ КОМАНДОЙ" }, { "CLAIM", "ЗАБРАТЬ" },
        { "ACTIVATE", "АКТИВИРОВАТЬ" }, { "OK", "ОК" }
    };

    public static string CurrentLanguage => PlayerPrefs.GetString(PreferenceKey, English);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BootstrapFontFallback() => EnsureGeorgianFallbackInstalled();

    public static void SetLanguage(string language)
    {
        string normalized = language == Georgian || language == Russian ? language : English;
        PlayerPrefs.SetString(PreferenceKey, normalized);
        PlayerPrefs.Save();
        EnsureGeorgianFallbackInstalled();
    }

    public static void ApplyLocalizedFont(TMP_Text label)
    {
        if (label == null) return;
        EnsureGeorgianFallbackInstalled();
        TMP_FontAsset desired = CurrentLanguage == Georgian ? georgianFont : TMP_Settings.defaultFontAsset;
        if (desired != null && label.font != desired)
        {
            label.font = desired;
            label.fontSharedMaterial = desired.material;
        }
        if (label.fontSharedMaterial == null && label.font != null && label.font.material != null)
            label.fontSharedMaterial = label.font.material;
    }

    static void EnsureGeorgianFallbackInstalled()
    {
        if (georgianFont == null)
            georgianFont = Resources.Load<TMP_FontAsset>(GeorgianFontResource);
        if (georgianFont == null) return;

        List<TMP_FontAsset> globalFallbacks = TMP_Settings.fallbackFontAssets;
        if (globalFallbacks == null)
        {
            globalFallbacks = new List<TMP_FontAsset>();
            TMP_Settings.fallbackFontAssets = globalFallbacks;
        }
        if (!globalFallbacks.Contains(georgianFont))
            globalFallbacks.Insert(0, georgianFont);

        TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;
        if (defaultFont == null) return;
        if (defaultFont.fallbackFontAssetTable == null)
            defaultFont.fallbackFontAssetTable = new List<TMP_FontAsset>();
        if (!defaultFont.fallbackFontAssetTable.Contains(georgianFont))
            defaultFont.fallbackFontAssetTable.Insert(0, georgianFont);
    }

    public static string Text(string englishSource)
    {
        if (string.IsNullOrEmpty(englishSource)) return "";
        Dictionary<string, string> table = CurrentLanguage == Georgian ? GeorgianText
            : CurrentLanguage == Russian ? RussianText : null;
        return table != null && table.TryGetValue(englishSource, out string translated)
            ? translated : englishSource;
    }
}

// Auto-fit + bounded horizontal expansion. The root grows only when the translated label needs it;
// the text still auto-sizes down as a final safety net, so no language can clip.
public sealed class LocalizedButtonText : MonoBehaviour
{
    public TextMeshProUGUI label;
    public RectTransform buttonRect;
    public string englishSource;
    public Vector2 baseSize;
    public float horizontalPadding = 44f;
    public float maxWidthMultiplier = 1.55f;
    public bool lockVisualStructure;

    string appliedLanguage;
    string appliedText;
    bool measurementPending;
    bool measurementWarningLogged;
    bool exactMeasurementDisabled;

    void OnEnable() => Refresh(true, false);
    void LateUpdate() => Refresh(measurementPending, true);

    public void Configure(TextMeshProUGUI target, string source, Vector2 size,
                          float padding = 44f, float maxMultiplier = 1.55f,
                          bool lockStructure = false)
    {
        label = target;
        buttonRect = transform as RectTransform;
        englishSource = source;
        baseSize = size;
        horizontalPadding = padding;
        maxWidthMultiplier = maxMultiplier;
        lockVisualStructure = lockStructure;
        Refresh(true, false);
    }

    void Refresh(bool force, bool allowExactMeasurement)
    {
        if (label == null || buttonRect == null) return;
        string language = UILocalization.CurrentLanguage;
        string value = UILocalization.Text(englishSource);
        if (!force && !measurementPending && language == appliedLanguage && value == appliedText) return;
        appliedLanguage = language;
        appliedText = value;
        UILocalization.ApplyLocalizedFont(label);
        label.text = value;

        label.enableAutoSizing = true;
        label.fontSizeMax = Mathf.Max(12f, label.fontSize);
        label.fontSizeMin = Mathf.Min(12f, label.fontSizeMax);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;

        // Authored image buttons (most importantly PLAY) keep the exact same RectTransform,
        // Image and layout contract in every language. Only the TMP string/font may change.
        if (lockVisualStructure)
        {
            measurementPending = false;
            return;
        }

        // A label can be configured while its overlay is inactive. In that state TMP has not always
        // assigned its default font/material yet, and GetPreferredValues would throw inside
        // MaterialReference. Use a safe estimate immediately, then replace it with TMP's exact
        // measurement on the first frame where the font and shared material are ready.
        float preferred = EstimateWidth(value) + horizontalPadding;
        if (allowExactMeasurement && !exactMeasurementDisabled && EnsureFontAndMaterial())
        {
            try
            {
                preferred = label.GetPreferredValues(value, 10000f, baseSize.y).x + horizontalPadding;
                measurementPending = false;
            }
            catch (Exception exception)
            {
                // Exact measurement is a visual enhancement. If this TMP/package combination still
                // rejects it, retain the estimate + auto-sizing and never let it interrupt the UI.
                exactMeasurementDisabled = true;
                measurementPending = false;
                if (!measurementWarningLogged)
                {
                    measurementWarningLogged = true;
                    Debug.LogWarning("LocalizedButtonText: TMP measurement was unavailable; using safe auto-fit sizing. " +
                                     exception.GetType().Name);
                }
            }
        }
        else
        {
            measurementPending = !exactMeasurementDisabled;
        }

        float maxWidth = Mathf.Max(baseSize.x, baseSize.x * Mathf.Max(1f, maxWidthMultiplier));
        float fittedWidth = Mathf.Clamp(preferred, baseSize.x, maxWidth);
        LayoutElement layout = buttonRect.GetComponent<LayoutElement>();
        if (layout != null)
        {
            // Layout groups own the RectTransform. Feed the measured dimensions into the layout
            // contract instead of fighting it by rewriting sizeDelta every frame.
            layout.minWidth = Mathf.Max(layout.minWidth, baseSize.x);
            layout.preferredWidth = fittedWidth;
            layout.minHeight = Mathf.Max(layout.minHeight, baseSize.y);
            layout.preferredHeight = Mathf.Max(layout.preferredHeight, baseSize.y);
        }
        else
        {
            buttonRect.sizeDelta = new Vector2(fittedWidth, baseSize.y);
        }
    }

    bool EnsureFontAndMaterial()
    {
        UILocalization.ApplyLocalizedFont(label);
        if (label.fontSharedMaterial == null && label.font != null && label.font.material != null)
            label.fontSharedMaterial = label.font.material;
        return label.font != null && label.font.material != null && label.fontSharedMaterial != null;
    }

    float EstimateWidth(string value)
    {
        float units = 0f;
        foreach (char character in value ?? string.Empty)
        {
            if (char.IsWhiteSpace(character)) units += 0.34f;
            else if (character < 128 && char.IsPunctuation(character)) units += 0.42f;
            else if (character < 128) units += 0.62f;
            else units += 0.78f;
        }
        return units * Mathf.Max(12f, label.fontSizeMax);
    }
}

// Shared code-built visual language taken from the My Club Crest screen. Authored bitmap buttons
// bypass this helper and retain their native aspect ratio.
public static class CrestUITheme
{
    public static readonly Color Surface = new Color(0.055f, 0.095f, 0.15f, 0.98f);
    public static readonly Color SurfaceRaised = new Color(0.075f, 0.125f, 0.19f, 0.98f);
    public static readonly Color Frame = new Color(0.23f, 0.35f, 0.48f, 1f);

    static Sprite rounded;

    public static void ApplyButton(Image frame, Color accent)
    {
        if (frame == null) return;
        ApplyFrame(frame, accent, SurfaceRaised, 2f);
        Shadow shadow = frame.GetComponent<Shadow>();
        if (shadow == null) shadow = frame.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.38f);
        shadow.effectDistance = new Vector2(0f, -3f);
        shadow.useGraphicAlpha = true;
    }

    public static Image ApplyFrame(Image frame, Color accent, Color surface, float inset = 3f)
    {
        if (frame == null) return null;
        frame.sprite = Rounded();
        frame.type = Image.Type.Sliced;
        frame.preserveAspect = false;
        frame.color = accent;

        Transform existing = frame.transform.Find("Surface");
        Image inner;
        if (existing != null) inner = existing.GetComponent<Image>();
        else
        {
            GameObject go = new GameObject("Surface");
            go.transform.SetParent(frame.transform, false);
            inner = go.AddComponent<Image>();
        }
        inner.sprite = Rounded();
        inner.type = Image.Type.Sliced;
        inner.preserveAspect = false;
        inner.color = surface;
        inner.raycastTarget = false;
        RectTransform rt = inner.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        return inner;
    }

    public static Sprite Rounded()
    {
        if (rounded != null) return rounded;
        const int size = 128, corner = 20;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f - 0.5f, inner = half - corner;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float qx = Mathf.Max(Mathf.Abs(x - half) - inner, 0f);
            float qy = Mathf.Max(Mathf.Abs(y - half) - inner, 0f);
            float distance = Mathf.Sqrt(qx * qx + qy * qy);
            pixels[y * size + x] = new Color32(255, 255, 255,
                (byte)(Mathf.Clamp01(corner - distance) * 255f));
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.name = "CrestUI_Rounded";
        rounded = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect,
            new Vector4(corner + 2, corner + 2, corner + 2, corner + 2));
        rounded.name = "CrestUI_Rounded";
        return rounded;
    }
}

public static class LocalizedButtonStyler
{
    // Icon-led hub art reserves a dedicated caption strip below the illustration. Keeping the
    // localized label out of the icon area prevents collisions in English, Russian and Georgian.
    public enum TextZone { Center, NativeCenter, LowerPlate, PlayPlate, SeasonPass }

    const float VisualCenterBottomPadding = 14f;

    public static TextMeshProUGUI AddLabel(Transform parent, string englishSource, float fontSize,
                                            Vector2 baseSize, TextZone zone = TextZone.Center,
                                            float maxWidthMultiplier = 1.55f,
                                            bool lockVisualStructure = false)
    {
        GameObject go = new GameObject("LocalizedLabel");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        UILocalization.ApplyLocalizedFont(text);
        text.text = UILocalization.Text(englishSource);
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.enableAutoSizing = true;
        text.fontSizeMax = fontSize;
        text.fontSizeMin = Mathf.Min(11f, fontSize);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        // Do not set TMP outlineWidth/outlineColor here. Freshly added TextMeshProUGUI components
        // can have no material yet, and TMP would throw ArgumentNullException while cloning it.
        Shadow shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0.08f, 0.20f, 0.82f);
        shadow.effectDistance = new Vector2(0f, -2f);

        RectTransform rt = text.rectTransform;
        if (zone == TextZone.NativeCenter)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
        }
        else if (zone == TextZone.SeasonPass)
        {
            rt.anchorMin = new Vector2(0.38f, 0.10f);
            rt.anchorMax = new Vector2(0.92f, 0.36f);
        }
        else if (zone == TextZone.LowerPlate)
        {
            rt.anchorMin = new Vector2(0.10f, 0.08f);
            rt.anchorMax = new Vector2(0.90f, 0.34f);
        }
        else if (zone == TextZone.PlayPlate)
        {
            rt.anchorMin = new Vector2(0.08f, 0.12f);
            rt.anchorMax = new Vector2(0.57f, 0.44f);
        }
        else
        {
            rt.anchorMin = new Vector2(0.10f, 0.18f);
            rt.anchorMax = new Vector2(0.90f, 0.84f);
        }
        bool dedicatedCaptionPlate = zone == TextZone.LowerPlate || zone == TextZone.PlayPlate ||
                                     zone == TextZone.SeasonPass;
        rt.offsetMin = zone == TextZone.NativeCenter ? new Vector2(10f, 4f)
            : new Vector2(0f, dedicatedCaptionPlate ? 0f : VisualCenterBottomPadding);
        rt.offsetMax = zone == TextZone.NativeCenter ? new Vector2(-10f, -4f) : Vector2.zero;

        LocalizedButtonText localized = parent.gameObject.AddComponent<LocalizedButtonText>();
        localized.Configure(text, englishSource, baseSize,
                            zone == TextZone.NativeCenter ? 24f : zone == TextZone.Center ? 48f : 72f,
                            maxWidthMultiplier, lockVisualStructure);
        return text;
    }

}
