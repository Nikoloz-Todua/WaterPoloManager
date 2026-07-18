using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The "My Club" customization screen — code-built (no prefabs), hosted in NavigationManager's
// club overlay (opened by tapping the hub's avatar or club name). Includes crest/club colors,
// simple previous/next player palette selectors, country, name and APPLY.
//
// No crest/flag art exists yet, so the 8 crests are procedural shapes (shield/circle/star/…)
// and a selected country shows as a colored dot on the hub's flag badge. Persistence is the
// ClubProfile inside Roster (RosterManager.Club / SaveClub) — same JSON save as everything else.
public class ClubCustomizationUI : MonoBehaviour
{
    static readonly Color Panel = new Color(0.03f, 0.05f, 0.11f, 0.92f);
    static readonly Color CardFill = new Color(0.07f, 0.12f, 0.19f, 0.97f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    static readonly Color Green = new Color(0.2f, 0.72f, 0.32f);
    static readonly Color Grey = new Color(0.55f, 0.6f, 0.68f);
    static readonly Color TileDark = new Color(0.1f, 0.14f, 0.2f, 1f);

    public const int CrestCount = 8;

    const int DefaultCapPaletteIndex = 1;
    const int DefaultSwimwearPaletteIndex = 9;

    // Fourteen common colors drive club swatches and the human-player cap/swimwear selectors.
    // The saved ClubProfile hex values are the sole match-time source; PlayerAnimator has no
    // independent Inspector override.
    public static readonly Color[] Palette =
    {
        Hex("1E90FF"), Hex("C62828"), Hex("2E7D32"), Hex("F9A825"), Hex("6A1B9A"),
        Hex("00838F"), Hex("E64A19"), Hex("283593"), Hex("37474F"), Hex("FFFFFF"),
        Hex("EC407A"), Hex("00BCD4"), Hex("111111"), Hex("7CB342"),
    };

    static readonly string[] PaletteNames =
    {
        "BLUE", "RED", "GREEN", "GOLD", "PURPLE", "TEAL", "ORANGE",
        "NAVY", "CHARCOAL", "WHITE", "PINK", "CYAN", "BLACK", "LIME"
    };

    // Water-polo nations, text-only (no flag art yet).
    public static readonly string[] CountryIds =
        { "GEO", "USA", "ESP", "ITA", "GER", "FRA", "GRE", "HUN", "SRB", "CRO", "AUS", "JPN" };

    Transform root;
    NavigationManager nav;

    // Working selections (committed to the profile only on APPLY).
    int selCrest, selPrimary, selSecondary = DefaultSwimwearPaletteIndex, selCountry = -1;
    int selCap = DefaultCapPaletteIndex, selSwimwear = DefaultSwimwearPaletteIndex;

    TMP_InputField nameField;
    Image previewCircle, previewCrest;
    Image capColorPreview, swimwearColorPreview;
    TextMeshProUGUI capColorName, swimwearColorName;
    TextMeshProUGUI savedFlash;
    readonly List<Image> crestFrames = new List<Image>();
    readonly List<Image> primFrames = new List<Image>();
    readonly List<Image> secFrames = new List<Image>();
    readonly List<Image> countryFrames = new List<Image>();
    Coroutine flashRoutine;

    public void Build(Transform parent, NavigationManager navigation)
    {
        root = parent;
        nav = navigation;

        Image bg = NewImage("Background", root);
        bg.color = new Color(0.03f, 0.07f, 0.13f, 1f);
        bg.raycastTarget = true;
        Stretch(bg.rectTransform);

        // Top bar: back + title.
        Image bar = NewImage("TopBar", root);
        bar.sprite = Rounded(); bar.type = Image.Type.Sliced;
        bar.color = new Color(0.04f, 0.06f, 0.13f, 0.86f);
        RectTransform brt = bar.rectTransform;
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(0f, 80f);

        Sprite back = Resources.Load<Sprite>("Sprites/back-button");
        GameObject bgo = new GameObject("BtnBack");
        bgo.transform.SetParent(bar.transform, false);
        SetRect(bgo.AddComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(64f, 64f));
        Image bimg = bgo.AddComponent<Image>();
        if (back != null) { bimg.sprite = back; bimg.preserveAspect = true; }
        else { bimg.sprite = Rounded(); bimg.type = Image.Type.Sliced; bimg.color = new Color(0.16f, 0.2f, 0.28f, 1f); }
        Button bbtn = bgo.AddComponent<Button>();
        bbtn.targetGraphic = bimg;
        bbtn.onClick.AddListener(() => { if (nav != null) nav.CloseClubScreen(); });

        MakeText(bar.transform, "MY CLUB", 34f, new Vector2(0.5f, 0.5f), Vector2.zero,
                 new Vector2(300f, 50f), Color.white, TextAlignmentOptions.Center);

        BuildPreviewColumn();
        BuildCrestGrid();
        BuildColorRows();
        BuildCountryGrid();

        MakeButton(root, "APPLY", 22f, new Vector2(0.5f, 0.5f), new Vector2(438f, -240f),
                   new Vector2(240f, 62f), Green, Apply);
        savedFlash = MakeText(root, "", 18f, new Vector2(0.5f, 0.5f), new Vector2(438f, -290f),
                              new Vector2(260f, 26f), Gold, TextAlignmentOptions.Center);

        SyncFromProfile();
    }

    void OnEnable() { SyncFromProfile(); } // re-opened → show what's actually saved

    // ------------------------------------------------------------------ sections

    void BuildPreviewColumn()
    {
        Image panel = MakeCard(root, new Vector2(-480f, -20f), new Vector2(300f, 460f), new Color(0.227f, 0.353f, 0.478f, 1f));
        MakeText(panel.transform, "PREVIEW", 18f, new Vector2(0.5f, 1f), new Vector2(0f, -30f),
                 new Vector2(260f, 24f), Grey, TextAlignmentOptions.Center);

        previewCircle = NewImage("PreviewCircle", panel.transform);
        previewCircle.sprite = Circle();
        previewCircle.raycastTarget = false;
        SetRect(previewCircle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -125f), new Vector2(130f, 130f));
        previewCrest = NewImage("PreviewCrest", previewCircle.transform);
        previewCrest.raycastTarget = false;
        previewCrest.preserveAspect = true;
        SetRect(previewCrest.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(76f, 76f));

        MakeText(panel.transform, "CLUB NAME (max 16)", 14f, new Vector2(0.5f, 1f), new Vector2(0f, -224f),
                 new Vector2(260f, 20f), Grey, TextAlignmentOptions.Center);
        nameField = MakeInputField(panel.transform, new Vector2(0f, -262f), new Vector2(250f, 52f));
    }

    void BuildCrestGrid()
    {
        MakeText(root, "LOGO", 20f, new Vector2(0.5f, 0.5f), new Vector2(-60f, 250f),
                 new Vector2(200f, 26f), Gold, TextAlignmentOptions.Center);
        crestFrames.Clear();
        for (int i = 0; i < CrestCount; i++)
        {
            int idx = i;
            float x = -60f + (i % 4 - 1.5f) * 100f;
            float y = i < 4 ? 180f : 80f;
            Image frame = MakeTile(new Vector2(x, y), new Vector2(86f, 86f), () => { selCrest = idx; SyncSelectionVisuals(); });
            crestFrames.Add(frame);
            Image crest = NewImage("Crest", frame.transform);
            crest.sprite = CrestSprite(i);
            crest.color = Color.white;
            crest.raycastTarget = false;
            crest.preserveAspect = true;
            SetRect(crest.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(58f, 58f));
        }
    }

    void BuildColorRows()
    {
        MakeText(root, "CLUB PRIMARY", 16f, new Vector2(0.5f, 0.5f), new Vector2(-60f, 10f),
                 new Vector2(300f, 22f), Gold, TextAlignmentOptions.Center);
        primFrames.Clear();
        for (int i = 0; i < Palette.Length; i++)
        {
            int idx = i;
            primFrames.Add(MakeSwatch(new Vector2(
                                      -60f + (i - (Palette.Length - 1) * 0.5f) * 36f, -30f), Palette[i],
                                      () => { selPrimary = idx; SyncSelectionVisuals(); }));
        }

        MakeText(root, "CLUB SECONDARY", 16f, new Vector2(0.5f, 0.5f), new Vector2(-60f, -80f),
                 new Vector2(300f, 22f), Gold, TextAlignmentOptions.Center);
        secFrames.Clear();
        for (int i = 0; i < Palette.Length; i++)
        {
            int idx = i;
            secFrames.Add(MakeSwatch(new Vector2(
                                     -60f + (i - (Palette.Length - 1) * 0.5f) * 36f, -120f), Palette[i],
                                     () => { selSecondary = idx; SyncSelectionVisuals(); }));
        }

        BuildPlayerColorSelector("PLAYER CAP COLOR", -185f, true,
                                 out capColorPreview, out capColorName);
        BuildPlayerColorSelector("PLAYER SWIMWEAR COLOR", -275f, false,
                                 out swimwearColorPreview, out swimwearColorName);
    }

    void BuildPlayerColorSelector(string label, float labelY, bool cap,
                                  out Image preview, out TextMeshProUGUI colorName)
    {
        MakeText(root, label, 15f, new Vector2(0.5f, 0.5f), new Vector2(-60f, labelY),
                 new Vector2(330f, 22f), Gold, TextAlignmentOptions.Center);

        Color arrowColor = new Color(0.16f, 0.2f, 0.28f, 1f);
        MakeButton(root, "<", 24f, new Vector2(0.5f, 0.5f), new Vector2(-190f, labelY - 40f),
                   new Vector2(54f, 42f), arrowColor, () => CyclePlayerColor(cap, -1));
        MakeButton(root, ">", 24f, new Vector2(0.5f, 0.5f), new Vector2(70f, labelY - 40f),
                   new Vector2(54f, 42f), arrowColor, () => CyclePlayerColor(cap, 1));

        preview = NewImage(cap ? "CapColorPreview" : "SwimwearColorPreview", root);
        preview.sprite = Rounded();
        preview.type = Image.Type.Sliced;
        preview.raycastTarget = false;
        SetRect(preview.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(-60f, labelY - 40f), new Vector2(180f, 42f));
        colorName = MakeText(preview.transform, "", 16f, new Vector2(0.5f, 0.5f), Vector2.zero,
                             new Vector2(170f, 38f), Color.white, TextAlignmentOptions.Center);
        Stretch(colorName.rectTransform);
    }

    void CyclePlayerColor(bool cap, int direction)
    {
        if (cap)
            selCap = (selCap + direction + Palette.Length) % Palette.Length;
        else
            selSwimwear = (selSwimwear + direction + Palette.Length) % Palette.Length;
        SyncPlayerColorPreviews();
    }

    void BuildCountryGrid()
    {
        MakeText(root, "COUNTRY", 20f, new Vector2(0.5f, 0.5f), new Vector2(438f, 250f),
                 new Vector2(200f, 26f), Gold, TextAlignmentOptions.Center);
        countryFrames.Clear();
        for (int i = 0; i < CountryIds.Length; i++)
        {
            int idx = i;
            float x = 438f + (i % 2 == 0 ? -78f : 78f);
            float y = 195f - (i / 2) * 54f;
            Image frame = MakeTile(new Vector2(x, y), new Vector2(146f, 46f),
                                   () => { selCountry = idx; SyncSelectionVisuals(); });
            countryFrames.Add(frame);
            Image dot = NewImage("Dot", frame.transform);
            dot.sprite = Circle();
            dot.color = CountryColor(CountryIds[i]);
            dot.raycastTarget = false;
            SetRect(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(18f, 18f));
            MakeText(frame.transform, CountryIds[i], 17f, new Vector2(0.5f, 0.5f), new Vector2(12f, 0f),
                     new Vector2(100f, 24f), Color.white, TextAlignmentOptions.Center);
        }
    }

    // ------------------------------------------------------------------ state

    void SyncFromProfile()
    {
        if (nameField == null) return; // OnEnable before Build
        ClubProfile club = RosterManager.Instance.Club;
        selCrest = Mathf.Clamp(club.logoId, 0, CrestCount - 1);
        selPrimary = PaletteIndex(club.primaryColorHex, 0);
        selSecondary = PaletteIndex(club.secondaryColorHex, DefaultSwimwearPaletteIndex);
        selCap = PaletteIndex(club.capColorHex, DefaultCapPaletteIndex);
        selSwimwear = PaletteIndex(club.swimwearColorHex, DefaultSwimwearPaletteIndex);
        selCountry = System.Array.IndexOf(CountryIds, club.countryId);
        nameField.text = club.clubName;
        SyncSelectionVisuals();
    }

    static int PaletteIndex(string hex, int fallback)
    {
        fallback = Mathf.Clamp(fallback, 0, Palette.Length - 1);
        Color c = ParseHex(hex, Palette[fallback]);
        int nearest = fallback;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < Palette.Length; i++)
        {
            if (ColorUtility.ToHtmlStringRGB(Palette[i]) == ColorUtility.ToHtmlStringRGB(c)) return i;
            float dr = Palette[i].r - c.r;
            float dg = Palette[i].g - c.g;
            float db = Palette[i].b - c.b;
            float distance = dr * dr + dg * dg + db * db;
            if (distance < nearestDistance) { nearestDistance = distance; nearest = i; }
        }
        return nearest;
    }

    void SyncSelectionVisuals()
    {
        for (int i = 0; i < crestFrames.Count; i++) crestFrames[i].color = i == selCrest ? Gold : TileDark;
        for (int i = 0; i < primFrames.Count; i++) primFrames[i].color = i == selPrimary ? Gold : TileDark;
        for (int i = 0; i < secFrames.Count; i++) secFrames[i].color = i == selSecondary ? Gold : TileDark;
        for (int i = 0; i < countryFrames.Count; i++) countryFrames[i].color = i == selCountry ? Gold : TileDark;
        if (previewCircle != null) previewCircle.color = Palette[selPrimary];
        if (previewCrest != null) { previewCrest.sprite = CrestSprite(selCrest); previewCrest.color = Palette[selSecondary]; }
        SyncPlayerColorPreviews();
    }

    void SyncPlayerColorPreviews()
    {
        if (capColorPreview != null)
            capColorPreview.color = Palette[selCap];
        if (capColorName != null)
        {
            capColorName.text = PaletteNames[selCap];
            capColorName.color = ReadableTextColor(Palette[selCap]);
        }
        if (swimwearColorPreview != null)
            swimwearColorPreview.color = Palette[selSwimwear];
        if (swimwearColorName != null)
        {
            swimwearColorName.text = PaletteNames[selSwimwear];
            swimwearColorName.color = ReadableTextColor(Palette[selSwimwear]);
        }
    }

    static Color ReadableTextColor(Color background)
    {
        float luminance = background.r * 0.2126f + background.g * 0.7152f + background.b * 0.0722f;
        return luminance > 0.58f ? new Color(0.05f, 0.06f, 0.08f) : Color.white;
    }

    void Apply()
    {
        ClubProfile club = RosterManager.Instance.Club;
        string name = nameField.text.Trim();
        if (name.Length > 16) name = name.Substring(0, 16);
        if (!string.IsNullOrEmpty(name)) club.clubName = name; // empty → keep the old name
        club.logoId = selCrest;
        club.primaryColorHex = ColorUtility.ToHtmlStringRGB(Palette[selPrimary]);
        club.secondaryColorHex = ColorUtility.ToHtmlStringRGB(Palette[selSecondary]);
        club.capColorHex = ColorUtility.ToHtmlStringRGB(Palette[selCap]);
        club.swimwearColorHex = ColorUtility.ToHtmlStringRGB(Palette[selSwimwear]);
        club.countryId = selCountry >= 0 ? CountryIds[selCountry] : "";
        RosterManager.Instance.SaveClub();
        if (nav != null) nav.RefreshClubProfile(); // hub cluster updates immediately
        nameField.text = club.clubName;            // show what was actually kept
        SyncPlayerColorPreviews();

        savedFlash.text = "SAVED!";
        savedFlash.alpha = 1f;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FadeFlash());
    }

    System.Collections.IEnumerator FadeFlash()
    {
        yield return new WaitForSecondsRealtime(1f);
        float t = 0f;
        while (t < 0.4f)
        {
            t += Time.unscaledDeltaTime;
            if (savedFlash != null) savedFlash.alpha = 1f - t / 0.4f;
            yield return null;
        }
    }

    // ------------------------------------------------------------------ shared club visuals

    // Procedural crest shapes (placeholder art — swap for real crest sprites later).
    static readonly Sprite[] crestCache = new Sprite[CrestCount];
    public static Sprite CrestSprite(int id)
    {
        id = Mathf.Clamp(id, 0, CrestCount - 1);
        if (crestCache[id] != null) return crestCache[id];
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = (x - size * 0.5f + 0.5f) / (size * 0.5f);
                float ny = (y - size * 0.5f + 0.5f) / (size * 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, CrestShape(id, nx, ny) ? (byte)255 : (byte)0);
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        crestCache[id] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect);
        return crestCache[id];
    }

    // x/y in -1..1, +y up. Each id is a distinct silhouette.
    static bool CrestShape(int id, float x, float y)
    {
        float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
        float d = Mathf.Sqrt(x * x + y * y);
        switch (id)
        {
            case 0: // shield: straight top, tapering point at the bottom
                if (y > 0.7f || y < -0.85f) return false;
                if (y >= 0f) return ax < 0.7f;
                return ax < 0.7f * (1f + y / 0.85f);
            case 1: return d < 0.8f;                                  // solid circle
            case 2: // 6-point star = up triangle + down triangle
                bool up = y > -0.7f && y < 0.8f && ax < 0.8f * (0.8f - y) / 1.5f;
                bool down = y < 0.7f && y > -0.8f && ax < 0.8f * (0.8f + y) / 1.5f;
                return up || down;
            case 3: return ax + ay < 0.85f;                           // diamond
            case 4: return Mathf.Max(ax * 0.866025f + ay * 0.5f, ay) < 0.78f; // hexagon
            case 5: return y > -0.7f && y < 0.8f && ax < 0.8f * (0.8f - y) / 1.5f; // triangle
            case 6: return d > 0.5f && d < 0.8f;                      // ring
            default: return (ax < 0.26f && ay < 0.85f) || (ay < 0.26f && ax < 0.85f); // cross
        }
    }

    public static Color ParseHex(string hex, Color fallback)
        => !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString("#" + hex, out Color c) ? c : fallback;

    // Stable placeholder color for a country's "flag dot" until real flag art exists.
    public static Color CountryColor(string id)
    {
        if (string.IsNullOrEmpty(id)) return new Color(0.5f, 0.53f, 0.6f);
        int h = 0;
        foreach (char c in id) h = h * 31 + c;
        int index = Mathf.Abs(h) % (Palette.Length - 1);
        if (index >= DefaultSwimwearPaletteIndex) index++; // skip white (invisible dot)
        return Palette[index];
    }

    // ------------------------------------------------------------------ UI helpers

    Image MakeTile(Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        Image frame = NewImage("Tile", root);
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced;
        frame.color = TileDark;
        SetRect(frame.rectTransform, new Vector2(0.5f, 0.5f), pos, size);
        Image fill = NewImage("Fill", frame.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced;
        fill.color = CardFill;
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);
        Button b = frame.gameObject.AddComponent<Button>();
        b.targetGraphic = frame;
        b.onClick.AddListener(onClick);
        return frame;
    }

    Image MakeSwatch(Vector2 pos, Color color, UnityEngine.Events.UnityAction onClick)
    {
        Image frame = MakeTile(pos, new Vector2(34f, 34f), onClick);
        Image sw = NewImage("Swatch", frame.transform);
        sw.sprite = Rounded(); sw.type = Image.Type.Sliced;
        sw.color = color;
        sw.raycastTarget = false;
        RectTransform srt = sw.rectTransform;
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(5f, 5f); srt.offsetMax = new Vector2(-5f, -5f);
        return frame;
    }

    TMP_InputField MakeInputField(Transform parent, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject("NameField");
        go.transform.SetParent(parent, false);
        SetRect(go.AddComponent<RectTransform>(), new Vector2(0.5f, 1f), pos, size);
        Image bg = go.AddComponent<Image>();
        bg.sprite = Rounded(); bg.type = Image.Type.Sliced;
        bg.color = new Color(0.1f, 0.14f, 0.2f, 1f);
        TMP_InputField field = go.AddComponent<TMP_InputField>();
        field.targetGraphic = bg;

        GameObject area = new GameObject("TextArea");
        area.transform.SetParent(go.transform, false);
        RectTransform art = area.AddComponent<RectTransform>();
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(12f, 6f); art.offsetMax = new Vector2(-12f, -6f);
        area.AddComponent<RectMask2D>();

        TextMeshProUGUI txt = new GameObject("Text").AddComponent<TextMeshProUGUI>();
        txt.transform.SetParent(area.transform, false);
        txt.fontSize = 20f;
        txt.fontStyle = FontStyles.Bold;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(txt.rectTransform);

        TextMeshProUGUI ph = new GameObject("Placeholder").AddComponent<TextMeshProUGUI>();
        ph.transform.SetParent(area.transform, false);
        ph.fontSize = 20f;
        ph.fontStyle = FontStyles.Italic;
        ph.color = new Color(1f, 1f, 1f, 0.35f);
        ph.alignment = TextAlignmentOptions.MidlineLeft;
        ph.text = "Club name";
        Stretch(ph.rectTransform);

        field.textViewport = art;
        field.textComponent = txt;
        field.placeholder = ph;
        field.characterLimit = 16;
        field.lineType = TMP_InputField.LineType.SingleLine;
        return field;
    }

    Image MakeCard(Transform parent, Vector2 pos, Vector2 size, Color border)
    {
        Image frame = NewImage("Card", parent);
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced;
        frame.color = border;
        SetRect(frame.rectTransform, new Vector2(0.5f, 0.5f), pos, size);
        Image fill = NewImage("Fill", frame.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced;
        fill.color = CardFill;
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);
        return frame;
    }

    Button MakeButton(Transform parent, string label, float fontSize, Vector2 anchor, Vector2 pos,
                      Vector2 size, Color color, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        SetRect(go.AddComponent<RectTransform>(), anchor, pos, size);
        Image img = go.AddComponent<Image>();
        img.sprite = Rounded(); img.type = Image.Type.Sliced;
        img.color = color;
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);
        TextMeshProUGUI t = MakeText(go.transform, label, fontSize, new Vector2(0.5f, 0.5f), Vector2.zero,
                                     size, Color.white, TextAlignmentOptions.Center);
        Stretch(t.rectTransform);
        return btn;
    }

    Image NewImage(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<Image>();
    }

    TextMeshProUGUI MakeText(Transform parent, string content, float size, Vector2 anchor,
                             Vector2 pos, Vector2 box, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = content;
        txt.fontSize = size;
        txt.fontStyle = FontStyles.Bold;
        txt.color = color;
        txt.alignment = align;
        txt.raycastTarget = false;
        SetRect(txt.rectTransform, anchor, pos, box);
        return txt;
    }

    static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Color Hex(string hex)
        => ColorUtility.TryParseHtmlString("#" + hex, out Color c) ? c : Color.magenta;

    static Sprite roundedLocal, circleLocal;
    static Sprite Rounded()
    {
        if (roundedLocal != null) return roundedLocal;
        const int size = 128, corner = 20;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        float half = size * 0.5f - 0.5f, inner = half - corner;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Max(Mathf.Abs(x - half) - inner, 0f);
                float qy = Mathf.Max(Mathf.Abs(y - half) - inner, 0f);
                float d = Mathf.Sqrt(qx * qx + qy * qy);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(corner - d) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        roundedLocal = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                     SpriteMeshType.FullRect, new Vector4(corner + 2, corner + 2, corner + 2, corner + 2));
        return roundedLocal;
    }

    static Sprite Circle()
    {
        if (circleLocal != null) return circleLocal;
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        float r = size * 0.5f - 1f;
        Vector2 c = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        circleLocal = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return circleLocal;
    }
}
