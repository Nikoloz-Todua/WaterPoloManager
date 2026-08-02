using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Small offline localization foundation for code-built UI. Existing callers may continue passing
// English source labels; LocalizedButtonText resolves them from the saved language and keeps text
// fitted if the language changes while a screen is alive.
public static class UILocalization
{
    public const string PreferenceKey = "ui_language";
    public const string English = "en";
    public const string Georgian = "ka";
    public const string Russian = "ru";

    static readonly Dictionary<string, string> GeorgianText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "PLAY", "თამაში" }, { "RANKING", "რეიტინგი" }, { "SEASON PASS", "სეზონური საშვი" },
        { "SHOP", "მაღაზია" }, { "TEAM", "გუნდი" }, { "MISSIONS", "მისიები" },
        { "LEAGUE", "ლიგა" }, { "ELITE LEAGUE", "ელიტური ლიგა" }, { "WORLD", "მსოფლიო" },
        { "FRIENDS", "მეგობრები" }, { "COUNTRY", "ქვეყანა" }, { "LAST WEEK", "გასული კვირა" },
        { "FORMATIONS", "ფორმაციები" }, { "PLAYERS", "მოთამაშეები" },
        { "SUBSTITUTIONS", "ცვლილებები" }, { "SAVE CLUB", "კლუბის შენახვა" },
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
        { "CANCEL", "ОТМЕНА" }, { "CONTINUE", "ПРОДОЛЖИТЬ" }, { "MAIN MENU", "ГЛАВНОЕ МЕНЮ" },
        { "TEAM MANAGEMENT", "УПРАВЛЕНИЕ КОМАНДОЙ" }, { "CLAIM", "ЗАБРАТЬ" },
        { "ACTIVATE", "АКТИВИРОВАТЬ" }, { "OK", "ОК" }
    };

    public static string CurrentLanguage => PlayerPrefs.GetString(PreferenceKey, English);

    public static void SetLanguage(string language)
    {
        string normalized = language == Georgian || language == Russian ? language : English;
        PlayerPrefs.SetString(PreferenceKey, normalized);
        PlayerPrefs.Save();
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

    string appliedLanguage;
    string appliedText;

    void OnEnable() => Refresh(true);
    void LateUpdate() => Refresh(false);

    public void Configure(TextMeshProUGUI target, string source, Vector2 size,
                          float padding = 44f, float maxMultiplier = 1.55f)
    {
        label = target;
        buttonRect = transform as RectTransform;
        englishSource = source;
        baseSize = size;
        horizontalPadding = padding;
        maxWidthMultiplier = maxMultiplier;
        Refresh(true);
    }

    void Refresh(bool force)
    {
        if (label == null || buttonRect == null) return;
        string language = UILocalization.CurrentLanguage;
        string value = UILocalization.Text(englishSource);
        if (!force && language == appliedLanguage && value == appliedText) return;
        appliedLanguage = language;
        appliedText = value;
        label.text = value;

        label.enableAutoSizing = true;
        label.fontSizeMax = Mathf.Max(12f, label.fontSize);
        label.fontSizeMin = Mathf.Min(12f, label.fontSizeMax);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;

        float preferred = label.GetPreferredValues(value, 10000f, baseSize.y).x + horizontalPadding;
        float maxWidth = Mathf.Max(baseSize.x, baseSize.x * Mathf.Max(1f, maxWidthMultiplier));
        buttonRect.sizeDelta = new Vector2(Mathf.Clamp(preferred, baseSize.x, maxWidth), baseSize.y);
    }
}

// The supplied Play-Button source still contains the English PLAY lettering. Keep its authored art
// for English, but automatically swap to the clean universal plate for every translated language so
// an old English word can never show behind Georgian/Russian text.
public sealed class LocalizedPlayButtonBackground : MonoBehaviour
{
    public UnityEngine.UI.Image background;
    public TextMeshProUGUI label;
    string appliedLanguage;

    public void Configure(UnityEngine.UI.Image image, TextMeshProUGUI text)
    {
        background = image;
        label = text;
        Refresh(true);
    }

    void OnEnable() => Refresh(true);
    void LateUpdate() => Refresh(false);

    void Refresh(bool force)
    {
        string language = UILocalization.CurrentLanguage;
        if (!force && language == appliedLanguage) return;
        appliedLanguage = language;
        bool english = language == UILocalization.English;
        if (background != null)
            background.sprite = english ? ButtonSpriteCatalog.SpriteFor("Play-Button")
                                        : ButtonSpriteCatalog.SpriteFor("Button1");
        if (label != null)
        {
            RectTransform rt = label.rectTransform;
            rt.anchorMin = english ? new Vector2(0.08f, 0.18f) : new Vector2(0.10f, 0.16f);
            rt.anchorMax = english ? new Vector2(0.57f, 0.82f) : new Vector2(0.90f, 0.82f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}

public static class LocalizedButtonStyler
{
    public enum TextZone { Center, LowerPlate, PlayPlate }

    public static TextMeshProUGUI AddLabel(Transform parent, string englishSource, float fontSize,
                                            Vector2 baseSize, TextZone zone = TextZone.Center,
                                            float maxWidthMultiplier = 1.55f)
    {
        GameObject go = new GameObject("LocalizedLabel");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
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
        text.outlineWidth = 0.08f;
        text.outlineColor = new Color32(0, 35, 82, 220);

        RectTransform rt = text.rectTransform;
        if (zone == TextZone.LowerPlate)
        {
            rt.anchorMin = new Vector2(0.10f, 0.10f);
            rt.anchorMax = new Vector2(0.90f, 0.54f);
        }
        else if (zone == TextZone.PlayPlate)
        {
            rt.anchorMin = new Vector2(0.08f, 0.18f);
            rt.anchorMax = new Vector2(0.57f, 0.82f);
        }
        else
        {
            rt.anchorMin = new Vector2(0.10f, 0.16f);
            rt.anchorMax = new Vector2(0.90f, 0.82f);
        }
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        LocalizedButtonText localized = parent.gameObject.AddComponent<LocalizedButtonText>();
        localized.Configure(text, englishSource, baseSize, zone == TextZone.Center ? 48f : 72f,
                            maxWidthMultiplier);
        return text;
    }

    public static Sprite UniversalSprite() => ButtonSpriteCatalog.SpriteFor("Button1");
}
