using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Code-built My Club editor. Template/colors/name are previewed live and committed to the existing
// ClubProfile in roster.json only when SAVE is pressed.
public class ClubCustomizationUI : MonoBehaviour
{
    static readonly Color CardFill = new Color(0.07f, 0.12f, 0.19f, 0.97f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    static readonly Color Green = new Color(0.2f, 0.72f, 0.32f);
    static readonly Color Grey = new Color(0.55f, 0.6f, 0.68f);
    static readonly Color TileDark = new Color(0.1f, 0.14f, 0.2f, 1f);
    static readonly Color Arrow = new Color(0.16f, 0.2f, 0.28f, 1f);

    public const int CrestCount = 20;
    const int MaxClubNameLength = 9;
    const int DefaultCapPaletteIndex = 1;
    const int DefaultSwimwearPaletteIndex = 9;

    // The exact same 14-color palette is reused by all three crest regions.
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

    public static readonly string[] CountryNames =
    {
        "Armenia", "Australia", "Austria", "Azerbaijan", "Canada", "China", "Croatia",
        "France", "Georgia", "Germany", "Greece", "Hungary", "Iran", "Israel", "Italy",
        "Japan", "Kazakhstan", "Latvia", "Lithuania", "Malta", "Mexico", "Montenegro",
        "Netherlands", "Poland", "Portugal", "Romania", "Russia", "Serbia", "Slovakia",
        "Slovenia", "Spain", "Sweden", "Turkey", "UK", "Ukraine", "USA"
    };

    Transform root;
    NavigationManager nav;
    int selTemplate, selPrimary, selSecondary = DefaultSwimwearPaletteIndex, selTertiary = 3;
    int selCountry = -1, selCap = DefaultCapPaletteIndex, selSwimwear = DefaultSwimwearPaletteIndex;

    CrestTemplateView preview;
    TMP_InputField nameField;
    TextMeshProUGUI templateLabel, capColorName, swimwearColorName, savedFlash, countryNameLabel;
    Image capColorPreview, swimwearColorPreview, currentCountryFlag;
    GameObject countryOverlay;
    sealed class SwatchVisual
    {
        public Image frame;
        public Image fill;
        public RectTransform rect;
        public GameObject checkBadge;
        public Shadow glow;
    }

    readonly List<SwatchVisual> primaryFrames = new List<SwatchVisual>();
    readonly List<SwatchVisual> secondaryFrames = new List<SwatchVisual>();
    readonly List<SwatchVisual> tertiaryFrames = new List<SwatchVisual>();
    readonly List<Image> countryFrames = new List<Image>();
    readonly List<GameObject> countryChecks = new List<GameObject>();
    Coroutine flashRoutine;

    public void Build(Transform parent, NavigationManager navigation)
    {
        root = parent;
        nav = navigation;

        Image bg = NewImage("Background", root);
        bg.color = new Color(0.03f, 0.07f, 0.13f, 1f);
        bg.raycastTarget = true;
        Stretch(bg.rectTransform);
        BuildTopBar();
        BuildPreviewPanel();
        BuildTemplateBrowser();
        BuildCrestColorRows();
        BuildPlayerColorSelectors();
        BuildCountrySelector();
        SyncFromProfile();
    }

    void OnEnable() { SyncFromProfile(); }

    void BuildTopBar()
    {
        Image bar = NewImage("TopBar", root);
        bar.sprite = Rounded(); bar.type = Image.Type.Sliced;
        bar.color = new Color(0.04f, 0.06f, 0.13f, 0.86f);
        RectTransform brt = bar.rectTransform;
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(0f, 80f);

        Sprite back = ButtonSpriteCatalog.SpriteFor("Back-Button");
        GameObject backGo = new GameObject("BtnBack");
        backGo.transform.SetParent(bar.transform, false);
        SetRect(backGo.AddComponent<RectTransform>(), new Vector2(0f, 0.5f),
                new Vector2(52f, 0f), new Vector2(64f, 64f));
        Image backImage = backGo.AddComponent<Image>();
        if (back != null) { backImage.sprite = back; backImage.preserveAspect = true; }
        else { backImage.sprite = Rounded(); backImage.type = Image.Type.Sliced; backImage.color = Arrow; }
        Button button = backGo.AddComponent<Button>();
        button.targetGraphic = backImage;
        button.onClick.AddListener(() => { if (nav != null) nav.CloseClubScreen(); });

        MakeText(bar.transform, "MY CLUB CREST", 34f, new Vector2(0.5f, 0.5f), Vector2.zero,
                 new Vector2(420f, 50f), Color.white, TextAlignmentOptions.Center);
    }

    void BuildPreviewPanel()
    {
        Image panel = MakeCard(root, new Vector2(-440f, -14f), new Vector2(340f, 540f),
                               new Color(0.23f, 0.35f, 0.48f, 1f));
        MakeText(panel.transform, "LIVE PREVIEW", 18f, new Vector2(0.5f, 1f), new Vector2(0f, -28f),
                 new Vector2(280f, 24f), Grey, TextAlignmentOptions.Center);

        preview = CrestTemplateView.Create(panel.transform, "LiveCrest",
            new Vector2(260f, 260f), new Vector2(0.5f, 0.5f), new Vector2(0f, 78f));

        MakeText(panel.transform, "CLUB NAME · MAX 9", 14f, new Vector2(0.5f, 0.5f),
                 new Vector2(0f, -92f), new Vector2(280f, 22f), Grey, TextAlignmentOptions.Center);
        nameField = MakeInputField(panel.transform, new Vector2(0f, -132f), new Vector2(274f, 52f));
        nameField.onValueChanged.AddListener(_ => SyncPreview());

        MakeButton(panel.transform, "SAVE CLUB", 22f, new Vector2(0.5f, 0.5f),
                   new Vector2(0f, -207f), new Vector2(250f, 62f), Green, Apply);
        savedFlash = MakeText(panel.transform, "", 17f, new Vector2(0.5f, 0.5f),
                              new Vector2(0f, -251f), new Vector2(280f, 24f),
                              Gold, TextAlignmentOptions.Center);
    }

    void BuildTemplateBrowser()
    {
        MakeText(root, "CREST TEMPLATE", 18f, new Vector2(0.5f, 0.5f), new Vector2(55f, 242f),
                 new Vector2(300f, 26f), Gold, TextAlignmentOptions.Center);
        MakeButton(root, "‹", 34f, new Vector2(0.5f, 0.5f), new Vector2(-135f, 202f),
                   new Vector2(58f, 48f), Arrow, () => CycleTemplate(-1));
        templateLabel = MakeText(root, "TEMPLATE 01 / 20", 20f, new Vector2(0.5f, 0.5f),
                                 new Vector2(55f, 202f), new Vector2(280f, 42f),
                                 Color.white, TextAlignmentOptions.Center);
        MakeButton(root, "›", 34f, new Vector2(0.5f, 0.5f), new Vector2(245f, 202f),
                   new Vector2(58f, 48f), Arrow, () => CycleTemplate(1));
    }

    void BuildCrestColorRows()
    {
        BuildPaletteGroup("PRIMARY", 126f, Palette[0], primaryFrames, index =>
        {
            selPrimary = index; SyncSelectionVisuals();
        });
        BuildPaletteGroup("SECONDARY", 12f, new Color(0.52f, 0.31f, 0.82f, 1f),
                          secondaryFrames, index =>
        {
            selSecondary = index; SyncSelectionVisuals();
        });
        BuildPaletteGroup("TERTIARY", -102f, Gold, tertiaryFrames, index =>
        {
            selTertiary = index; SyncSelectionVisuals();
        });
    }

    void BuildPaletteGroup(string label, float y, Color accent, List<SwatchVisual> frames,
                           System.Action<int> select)
    {
        Image card = MakeCard(root, new Vector2(55f, y), new Vector2(500f, 102f),
                              new Color(accent.r, accent.g, accent.b, 0.86f));
        card.gameObject.name = label + "_Palette";

        Image accentBar = NewImage("Accent", card.transform);
        accentBar.sprite = Rounded(); accentBar.type = Image.Type.Sliced;
        accentBar.color = accent; accentBar.raycastTarget = false;
        SetRect(accentBar.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(7f, 0f), new Vector2(7f, 76f));

        MakeText(card.transform, label, 14f, new Vector2(0f, 0.5f),
                 new Vector2(61f, 10f), new Vector2(100f, 24f),
                 Color.white, TextAlignmentOptions.Center);
        MakeText(card.transform, "COLOR", 10f, new Vector2(0f, 0.5f),
                 new Vector2(61f, -12f), new Vector2(100f, 18f),
                 new Color(accent.r, accent.g, accent.b, 0.95f),
                 TextAlignmentOptions.Center);

        Image divider = NewImage("Divider", card.transform);
        divider.color = new Color(1f, 1f, 1f, 0.10f); divider.raycastTarget = false;
        SetRect(divider.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(116f, 0f), new Vector2(2f, 70f));

        frames.Clear();
        for (int i = 0; i < Palette.Length; i++)
        {
            int index = i;
            int column = i % 7;
            int row = i / 7;
            float x = 145f + column * 47f;
            float swatchY = row == 0 ? 24f : -24f;
            frames.Add(MakeSwatch(card.transform, new Vector2(x, swatchY), Palette[i],
                                  () => select(index)));
        }
    }

    void BuildPlayerColorSelectors()
    {
        BuildPlayerColorSelector("PLAYER CAP", -188f, true, out capColorPreview, out capColorName);
        BuildPlayerColorSelector("PLAYER SWIMWEAR", -265f, false,
                                 out swimwearColorPreview, out swimwearColorName);
    }

    void BuildPlayerColorSelector(string label, float y, bool cap,
                                  out Image previewImage, out TextMeshProUGUI colorName)
    {
        MakeText(root, label, 14f, new Vector2(0.5f, 0.5f), new Vector2(-35f, y),
                 new Vector2(220f, 22f), Gold, TextAlignmentOptions.Center);
        MakeButton(root, "‹", 25f, new Vector2(0.5f, 0.5f), new Vector2(-150f, y - 35f),
                   new Vector2(48f, 38f), Arrow, () => CyclePlayerColor(cap, -1));
        MakeButton(root, "›", 25f, new Vector2(0.5f, 0.5f), new Vector2(80f, y - 35f),
                   new Vector2(48f, 38f), Arrow, () => CyclePlayerColor(cap, 1));

        previewImage = NewImage(cap ? "CapColorPreview" : "SwimwearColorPreview", root);
        previewImage.sprite = Rounded(); previewImage.type = Image.Type.Sliced;
        previewImage.raycastTarget = false;
        SetRect(previewImage.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(-35f, y - 35f), new Vector2(170f, 38f));
        colorName = MakeText(previewImage.transform, "", 14f, new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(160f, 34f), Color.white,
                             TextAlignmentOptions.Center);
        Stretch(colorName.rectTransform);
    }

    void BuildCountrySelector()
    {
        const float centerX = 420f;
        MakeText(root, "COUNTRY", 18f, new Vector2(0.5f, 0.5f), new Vector2(centerX, 242f),
                 new Vector2(310f, 26f), Gold, TextAlignmentOptions.Center);

        Image selector = MakeCard(root, new Vector2(centerX, 184f), new Vector2(270f, 70f),
                                  new Color(0.12f, 0.66f, 1f, 1f));
        selector.gameObject.name = "CountryInlineSelector";
        Button selectorButton = selector.gameObject.AddComponent<Button>();
        selectorButton.targetGraphic = selector;
        selectorButton.onClick.AddListener(OpenCountryOverlay);

        MakeButton(selector.transform, "<", 26f, new Vector2(0f, 0.5f), new Vector2(26f, 0f),
                   new Vector2(42f, 48f), Arrow, () => CycleCountry(-1));
        MakeButton(selector.transform, ">", 26f, new Vector2(1f, 0.5f), new Vector2(-26f, 0f),
                   new Vector2(42f, 48f), Arrow, () => CycleCountry(1));

        currentCountryFlag = NewImage("CurrentFlag", selector.transform);
        currentCountryFlag.preserveAspect = true;
        currentCountryFlag.raycastTarget = false;
        SetRect(currentCountryFlag.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(-65f, 0f), new Vector2(42f, 30f));
        countryNameLabel = MakeText(selector.transform, "SELECT COUNTRY", 18f,
            new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(120f, 40f),
            Color.white, TextAlignmentOptions.Center);
        countryNameLabel.enableAutoSizing = true;
        countryNameLabel.fontSizeMin = 12f;
        countryNameLabel.fontSizeMax = 18f;

        MakeButton(root, "v", 25f, new Vector2(0.5f, 0.5f), new Vector2(586f, 184f),
                   new Vector2(50f, 56f), new Color(0.05f, 0.32f, 0.68f, 1f), OpenCountryOverlay);
        BuildCountryOverlay();
    }

    void BuildCountryOverlay()
    {
        countryOverlay = new GameObject("CountrySelectionOverlay");
        countryOverlay.transform.SetParent(root, false);
        Stretch(countryOverlay.AddComponent<RectTransform>());
        Image dim = countryOverlay.AddComponent<Image>();
        dim.color = new Color(0.005f, 0.015f, 0.04f, 0.90f);
        dim.raycastTarget = true;
        Button dismiss = countryOverlay.AddComponent<Button>();
        dismiss.targetGraphic = dim;
        dismiss.onClick.AddListener(CloseCountryOverlay);

        Image modal = MakeCard(countryOverlay.transform, new Vector2(0f, -4f),
                               new Vector2(940f, 590f), new Color(0.08f, 0.70f, 1f, 1f));
        modal.gameObject.name = "CountryModal";
        modal.raycastTarget = true;
        MakeText(modal.transform, "SELECT YOUR COUNTRY", 30f, new Vector2(0.5f, 1f),
                 new Vector2(0f, -34f), new Vector2(620f, 42f), Color.white,
                 TextAlignmentOptions.Center);
        MakeText(modal.transform, "Choose a flag — your profile updates immediately", 15f,
                 new Vector2(0.5f, 1f), new Vector2(0f, -69f), new Vector2(620f, 24f),
                 Grey, TextAlignmentOptions.Center);
        MakeButton(modal.transform, "X", 18f, new Vector2(1f, 1f), new Vector2(-34f, -34f),
                   new Vector2(46f, 46f), new Color(0.55f, 0.12f, 0.18f, 1f), CloseCountryOverlay);

        GameObject viewportGo = new GameObject("CountryViewport");
        viewportGo.transform.SetParent(modal.transform, false);
        RectTransform viewport = viewportGo.AddComponent<RectTransform>();
        SetRect(viewport, new Vector2(0.5f, 0.5f), new Vector2(0f, -45f), new Vector2(870f, 440f));
        Image viewportImage = viewportGo.AddComponent<Image>();
        viewportImage.color = new Color(0.015f, 0.035f, 0.07f, 0.64f);
        viewportGo.AddComponent<RectMask2D>();

        GameObject contentGo = new GameObject("CountryGrid");
        contentGo.transform.SetParent(viewportGo.transform, false);
        RectTransform content = contentGo.AddComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 12f * 76f + 12f);
        GridLayoutGroup grid = contentGo.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(18, 18, 14, 14);
        grid.cellSize = new Vector2(268f, 62f);
        grid.spacing = new Vector2(14f, 14f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperCenter;

        ScrollRect scroll = viewportGo.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;

        countryFrames.Clear();
        countryChecks.Clear();
        CountryCatalog catalog = CountryCatalog.Instance;
        for (int i = 0; i < CountryNames.Length; i++)
        {
            int index = i;
            GameObject row = new GameObject("Country_" + CountryNames[i]);
            row.transform.SetParent(content.transform, false);
            Image frame = row.AddComponent<Image>();
            frame.sprite = Rounded();
            frame.type = Image.Type.Sliced;
            frame.color = TileDark;
            Button button = row.AddComponent<Button>();
            button.targetGraphic = frame;
            button.onClick.AddListener(() => SelectCountry(index, true));
            countryFrames.Add(frame);

            Image flag = NewImage("Flag", row.transform);
            flag.sprite = catalog != null ? catalog.FlagFor(CountryNames[i]) : null;
            flag.preserveAspect = true;
            flag.raycastTarget = false;
            if (flag.sprite == null) { flag.sprite = Circle(); flag.color = CountryColor(CountryNames[i]); }
            SetRect(flag.rectTransform, new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(48f, 34f));

            TextMeshProUGUI country = MakeText(row.transform, CountryNames[i], 16f,
                new Vector2(0f, 0.5f), new Vector2(142f, 0f), new Vector2(150f, 34f),
                Color.white, TextAlignmentOptions.Left);
            country.enableAutoSizing = true;
            country.fontSizeMin = 12f;
            country.fontSizeMax = 16f;

            GameObject check = MakeGreenCheck(row.transform, new Vector2(1f, 0.5f),
                                               new Vector2(-28f, 0f), 26f);
            check.SetActive(false);
            countryChecks.Add(check);
        }
        countryOverlay.SetActive(false);
    }

    void SyncFromProfile()
    {
        if (nameField == null) return;
        ClubProfile club = RosterManager.Instance.Club;
        CrestTemplateCatalog catalog = CrestTemplateCatalog.Instance;
        int candidate = Mathf.Clamp(club.logoId, 0, CrestCount - 1);
        CrestTemplateCatalog.Entry entry = catalog != null ? catalog.Get(candidate) : null;
        selTemplate = entry != null && entry.valid && entry.mask != null
            ? candidate
            : catalog != null ? catalog.FirstValidIndex() : candidate;
        selPrimary = PaletteIndex(club.primaryColorHex, 0);
        selSecondary = PaletteIndex(club.secondaryColorHex, DefaultSwimwearPaletteIndex);
        selTertiary = PaletteIndex(club.tertiaryColorHex, 3);
        selCap = PaletteIndex(club.capColorHex, DefaultCapPaletteIndex);
        selSwimwear = PaletteIndex(club.swimwearColorHex, DefaultSwimwearPaletteIndex);
        selCountry = System.Array.IndexOf(CountryNames, NormalizeCountryName(club.countryId));
        nameField.SetTextWithoutNotify(ClampClubName(club.clubName));
        SyncSelectionVisuals();
    }

    void CycleTemplate(int direction)
    {
        CrestTemplateCatalog catalog = CrestTemplateCatalog.Instance;
        selTemplate = catalog != null
            ? catalog.NextValidIndex(selTemplate, direction)
            : (selTemplate + (direction < 0 ? -1 : 1) + CrestCount) % CrestCount;
        SyncSelectionVisuals();
    }

    void CyclePlayerColor(bool cap, int direction)
    {
        if (cap) selCap = (selCap + direction + Palette.Length) % Palette.Length;
        else selSwimwear = (selSwimwear + direction + Palette.Length) % Palette.Length;
        SyncPlayerColorPreviews();
    }

    void CycleCountry(int direction)
    {
        int start = selCountry >= 0 ? selCountry : (direction > 0 ? -1 : 0);
        SelectCountry((start + direction + CountryNames.Length) % CountryNames.Length, true);
    }

    void OpenCountryOverlay()
    {
        if (countryOverlay == null) return;
        SyncCountryVisuals();
        countryOverlay.SetActive(true);
        countryOverlay.transform.SetAsLastSibling();
    }

    void CloseCountryOverlay()
    {
        if (countryOverlay != null) countryOverlay.SetActive(false);
    }

    void SelectCountry(int index, bool persistImmediately)
    {
        selCountry = Mathf.Clamp(index, 0, CountryNames.Length - 1);
        SyncCountryVisuals();
        if (persistImmediately)
        {
            ClubProfile club = RosterManager.Instance.Club;
            club.countryId = CountryNames[selCountry];
            RosterManager.Instance.SaveClub();
            if (nav != null) nav.RefreshClubProfile();
            if (savedFlash != null)
            {
                savedFlash.text = "COUNTRY UPDATED";
                savedFlash.alpha = 1f;
                if (flashRoutine != null) StopCoroutine(flashRoutine);
                flashRoutine = StartCoroutine(FadeFlash());
            }
            CloseCountryOverlay();
        }
    }

    void SyncSelectionVisuals()
    {
        SyncSwatchGroup(primaryFrames, selPrimary);
        SyncSwatchGroup(secondaryFrames, selSecondary);
        SyncSwatchGroup(tertiaryFrames, selTertiary);
        SyncCountryVisuals();
        if (templateLabel != null)
            templateLabel.text = "TEMPLATE " + (selTemplate + 1).ToString("00") + " / " + CrestCount;
        SyncPreview();
        SyncPlayerColorPreviews();
    }

    void SyncCountryVisuals()
    {
        string country = selCountry >= 0 && selCountry < CountryNames.Length
            ? CountryNames[selCountry] : "";
        if (countryNameLabel != null) countryNameLabel.text = string.IsNullOrEmpty(country) ? "SELECT COUNTRY" : country;
        if (currentCountryFlag != null)
        {
            Sprite flag = !string.IsNullOrEmpty(country) && CountryCatalog.Instance != null
                ? CountryCatalog.Instance.FlagFor(country) : null;
            currentCountryFlag.sprite = flag != null ? flag : Circle();
            currentCountryFlag.color = flag != null ? Color.white
                : string.IsNullOrEmpty(country) ? new Color(0.38f, 0.42f, 0.48f, 1f) : CountryColor(country);
        }
        for (int i = 0; i < countryFrames.Count; i++)
        {
            bool selected = i == selCountry;
            countryFrames[i].color = selected ? new Color(0.12f, 0.72f, 0.32f, 1f) : TileDark;
            if (i < countryChecks.Count) countryChecks[i].SetActive(selected);
        }
    }

    static void SyncSwatchGroup(List<SwatchVisual> swatches, int selected)
    {
        for (int i = 0; i < swatches.Count; i++)
        {
            bool active = i == selected;
            SwatchVisual swatch = swatches[i];
            swatch.frame.color = active ? Gold : new Color(0.20f, 0.27f, 0.36f, 1f);
            swatch.rect.localScale = active ? Vector3.one * 1.12f : Vector3.one;
            swatch.checkBadge.SetActive(active);
            swatch.glow.enabled = active;
        }
    }

    void SyncPreview()
    {
        if (preview == null) return;
        preview.SetIdentity(selTemplate, Palette[selPrimary], Palette[selSecondary],
                            Palette[selTertiary]);
    }

    void SyncPlayerColorPreviews()
    {
        if (capColorPreview != null) capColorPreview.color = Palette[selCap];
        if (capColorName != null)
        {
            capColorName.text = PaletteNames[selCap];
            capColorName.color = ReadableTextColor(Palette[selCap]);
        }
        if (swimwearColorPreview != null) swimwearColorPreview.color = Palette[selSwimwear];
        if (swimwearColorName != null)
        {
            swimwearColorName.text = PaletteNames[selSwimwear];
            swimwearColorName.color = ReadableTextColor(Palette[selSwimwear]);
        }
    }

    void Apply()
    {
        ClubProfile club = RosterManager.Instance.Club;
        string cleanName = ClampClubName(nameField.text);
        if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "MY CLUB";
        club.clubName = cleanName;
        club.logoId = selTemplate;
        club.primaryColorHex = ColorUtility.ToHtmlStringRGB(Palette[selPrimary]);
        club.secondaryColorHex = ColorUtility.ToHtmlStringRGB(Palette[selSecondary]);
        club.tertiaryColorHex = ColorUtility.ToHtmlStringRGB(Palette[selTertiary]);
        club.capColorHex = ColorUtility.ToHtmlStringRGB(Palette[selCap]);
        club.swimwearColorHex = ColorUtility.ToHtmlStringRGB(Palette[selSwimwear]);
        club.countryId = selCountry >= 0 ? CountryNames[selCountry] : "";
        RosterManager.Instance.SaveClub();
        nameField.SetTextWithoutNotify(club.clubName);
        SyncPreview();
        if (nav != null) nav.RefreshClubProfile();

        savedFlash.text = "CLUB SAVED";
        savedFlash.alpha = 1f;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FadeFlash());
    }

    IEnumerator FadeFlash()
    {
        yield return new WaitForSecondsRealtime(1f);
        float time = 0f;
        while (time < 0.4f)
        {
            time += Time.unscaledDeltaTime;
            if (savedFlash != null) savedFlash.alpha = 1f - time / 0.4f;
            yield return null;
        }
    }

    static string ClampClubName(string value)
    {
        string clean = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        return clean.Length > MaxClubNameLength ? clean.Substring(0, MaxClubNameLength) : clean;
    }

    static int PaletteIndex(string hex, int fallback)
    {
        fallback = Mathf.Clamp(fallback, 0, Palette.Length - 1);
        Color color = ParseHex(hex, Palette[fallback]);
        int nearest = fallback;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < Palette.Length; i++)
        {
            Color delta = Palette[i] - color;
            float distance = delta.r * delta.r + delta.g * delta.g + delta.b * delta.b;
            if (distance < nearestDistance) { nearestDistance = distance; nearest = i; }
        }
        return nearest;
    }

    static Color ReadableTextColor(Color background)
    {
        float luminance = background.r * 0.2126f + background.g * 0.7152f + background.b * 0.0722f;
        return luminance > 0.58f ? new Color(0.05f, 0.06f, 0.08f) : Color.white;
    }

    public static Color ParseHex(string hex, Color fallback)
        => !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString("#" + hex, out Color color)
            ? color : fallback;

    public static string NormalizeCountryName(string saved)
    {
        if (string.IsNullOrWhiteSpace(saved)) return "";
        switch (saved.Trim().ToUpperInvariant())
        {
            case "ARM": return "Armenia"; case "AUS": return "Australia";
            case "AUT": return "Austria"; case "AZE": return "Azerbaijan";
            case "CAN": return "Canada"; case "CHN": return "China";
            case "CRO": return "Croatia"; case "FRA": return "France";
            case "GEO": return "Georgia"; case "GER": return "Germany";
            case "GRE": return "Greece"; case "HUN": return "Hungary";
            case "IRN": return "Iran"; case "ISR": return "Israel";
            case "ITA": return "Italy"; case "JPN": return "Japan";
            case "KAZ": return "Kazakhstan"; case "LVA": return "Latvia";
            case "LTU": return "Lithuania"; case "MLT": return "Malta";
            case "MEX": return "Mexico"; case "MNE": return "Montenegro";
            case "NED": return "Netherlands"; case "POL": return "Poland";
            case "POR": return "Portugal"; case "ROU": return "Romania";
            case "RUS": return "Russia"; case "SRB": return "Serbia";
            case "SVK": return "Slovakia"; case "SVN": return "Slovenia";
            case "ESP": return "Spain"; case "SWE": return "Sweden";
            case "TUR": return "Turkey"; case "GBR": return "UK";
            case "UK": return "UK"; case "UKR": return "Ukraine";
            case "USA": return "USA";
            default: return saved.Trim();
        }
    }

    public static Sprite ClubBadgeBackgroundSprite() => Rounded();

    public static Color CountryColor(string id)
    {
        if (string.IsNullOrEmpty(id)) return new Color(0.5f, 0.53f, 0.6f);
        int hash = 0;
        foreach (char c in id) hash = hash * 31 + c;
        int index = Mathf.Abs(hash) % (Palette.Length - 1);
        if (index >= DefaultSwimwearPaletteIndex) index++;
        return Palette[index];
    }

    Image MakeTile(Vector2 position, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        Image frame = NewImage("Tile", root);
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced; frame.color = TileDark;
        SetRect(frame.rectTransform, new Vector2(0.5f, 0.5f), position, size);
        Image fill = NewImage("Fill", frame.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced; fill.color = CardFill; fill.raycastTarget = false;
        RectTransform fillRt = fill.rectTransform;
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(3f, 3f); fillRt.offsetMax = new Vector2(-3f, -3f);
        Button button = frame.gameObject.AddComponent<Button>();
        button.targetGraphic = frame;
        button.onClick.AddListener(onClick);
        return frame;
    }

    GameObject MakeGreenCheck(Transform parent, Vector2 anchor, Vector2 position, float size)
    {
        Image badge = NewImage("SelectedCheck", parent);
        badge.sprite = Circle();
        badge.color = new Color(0.15f, 0.88f, 0.38f, 1f);
        badge.raycastTarget = false;
        SetRect(badge.rectTransform, anchor, position, new Vector2(size, size));

        Image shortStroke = NewImage("Short", badge.transform);
        shortStroke.color = Color.white; shortStroke.raycastTarget = false;
        SetRect(shortStroke.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-4f, -1f),
                new Vector2(3.5f, size * 0.32f));
        shortStroke.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -42f);
        Image longStroke = NewImage("Long", badge.transform);
        longStroke.color = Color.white; longStroke.raycastTarget = false;
        SetRect(longStroke.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(3.5f, 0.5f),
                new Vector2(3.5f, size * 0.48f));
        longStroke.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 42f);
        return badge.gameObject;
    }

    SwatchVisual MakeSwatch(Transform parent, Vector2 position, Color color,
                            UnityEngine.Events.UnityAction onClick)
    {
        Image frame = NewImage("SwatchFrame", parent);
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced;
        frame.color = new Color(0.20f, 0.27f, 0.36f, 1f);
        SetRect(frame.rectTransform, new Vector2(0f, 0.5f), position, new Vector2(40f, 34f));
        Button button = frame.gameObject.AddComponent<Button>();
        button.targetGraphic = frame;
        button.onClick.AddListener(onClick);
        Shadow glow = frame.gameObject.AddComponent<Shadow>();
        glow.effectColor = new Color(1f, 0.82f, 0.2f, 0.68f);
        glow.effectDistance = new Vector2(0f, -3f);
        glow.useGraphicAlpha = true;
        glow.enabled = false;

        Image swatch = NewImage("Swatch", frame.transform);
        swatch.sprite = Rounded(); swatch.type = Image.Type.Sliced;
        swatch.color = color; swatch.raycastTarget = false;
        RectTransform rt = swatch.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(4f, 4f); rt.offsetMax = new Vector2(-4f, -4f);

        Image badge = NewImage("SelectedBadge", frame.transform);
        badge.sprite = Circle(); badge.color = Gold; badge.raycastTarget = false;
        SetRect(badge.rectTransform, new Vector2(1f, 1f), new Vector2(-2f, -2f),
                new Vector2(16f, 16f));
        Image checkShort = NewImage("CheckShort", badge.transform);
        checkShort.color = new Color(0.06f, 0.09f, 0.14f, 1f); checkShort.raycastTarget = false;
        SetRect(checkShort.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(-2.5f, 0f), new Vector2(2.5f, 7f));
        checkShort.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -42f);
        Image checkLong = NewImage("CheckLong", badge.transform);
        checkLong.color = new Color(0.06f, 0.09f, 0.14f, 1f); checkLong.raycastTarget = false;
        SetRect(checkLong.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(2.5f, -0.5f), new Vector2(2.5f, 10f));
        checkLong.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 42f);
        badge.gameObject.SetActive(false);

        return new SwatchVisual
        {
            frame = frame,
            fill = swatch,
            rect = frame.rectTransform,
            checkBadge = badge.gameObject,
            glow = glow
        };
    }

    TMP_InputField MakeInputField(Transform parent, Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject("NameField");
        go.transform.SetParent(parent, false);
        SetRect(go.AddComponent<RectTransform>(), new Vector2(0.5f, 0.5f), position, size);
        Image background = go.AddComponent<Image>();
        background.sprite = Rounded(); background.type = Image.Type.Sliced;
        background.color = new Color(0.1f, 0.14f, 0.2f, 1f);
        TMP_InputField field = go.AddComponent<TMP_InputField>();
        field.targetGraphic = background;

        GameObject area = new GameObject("TextArea");
        area.transform.SetParent(go.transform, false);
        RectTransform areaRt = area.AddComponent<RectTransform>();
        areaRt.anchorMin = Vector2.zero; areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = new Vector2(12f, 6f); areaRt.offsetMax = new Vector2(-12f, -6f);
        area.AddComponent<RectMask2D>();

        TextMeshProUGUI text = new GameObject("Text").AddComponent<TextMeshProUGUI>();
        text.transform.SetParent(area.transform, false);
        text.fontSize = 20f; text.fontStyle = FontStyles.Bold; text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(text.rectTransform);

        TextMeshProUGUI placeholder = new GameObject("Placeholder").AddComponent<TextMeshProUGUI>();
        placeholder.transform.SetParent(area.transform, false);
        placeholder.fontSize = 20f; placeholder.fontStyle = FontStyles.Italic;
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.text = "Club name";
        Stretch(placeholder.rectTransform);

        field.textViewport = areaRt;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.characterLimit = MaxClubNameLength;
        field.lineType = TMP_InputField.LineType.SingleLine;
        return field;
    }

    Image MakeCard(Transform parent, Vector2 position, Vector2 size, Color border)
    {
        Image frame = NewImage("Card", parent);
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced; frame.color = border;
        SetRect(frame.rectTransform, new Vector2(0.5f, 0.5f), position, size);
        Image fill = NewImage("Fill", frame.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced; fill.color = CardFill; fill.raycastTarget = false;
        RectTransform rt = fill.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(3f, 3f); rt.offsetMax = new Vector2(-3f, -3f);
        return frame;
    }

    Button MakeButton(Transform parent, string label, float fontSize, Vector2 anchor,
                      Vector2 position, Vector2 size, Color color,
                      UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        SetRect(go.AddComponent<RectTransform>(), anchor, position, size);
        Image image = go.AddComponent<Image>();
        image.sprite = ButtonSpriteCatalog.SpriteFor("Button1");
        if (image.sprite == null) { image.sprite = Rounded(); image.type = Image.Type.Sliced; }
        image.color = color;
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        if (onClick != null) button.onClick.AddListener(onClick);
        LocalizedButtonStyler.AddLabel(go.transform, label, fontSize, size, maxWidthMultiplier: 1.3f);
        return button;
    }

    Image NewImage(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<Image>();
    }

    TextMeshProUGUI MakeText(Transform parent, string value, float fontSize, Vector2 anchor,
                             Vector2 position, Vector2 size, Color color, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = value; text.fontSize = fontSize; text.fontStyle = FontStyles.Bold;
        text.color = color; text.alignment = alignment; text.raycastTarget = false;
        SetRect(text.rectTransform, anchor, position, size);
        return text;
    }

    static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static Color Hex(string hex)
        => ColorUtility.TryParseHtmlString("#" + hex, out Color color) ? color : Color.magenta;

    static Sprite roundedLocal, circleLocal;
    static Sprite Rounded()
    {
        if (roundedLocal != null) return roundedLocal;
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
        texture.SetPixels32(pixels); texture.Apply(); texture.wrapMode = TextureWrapMode.Clamp;
        roundedLocal = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect,
            new Vector4(corner + 2, corner + 2, corner + 2, corner + 2));
        return roundedLocal;
    }

    static Sprite Circle()
    {
        if (circleLocal != null) return circleLocal;
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[size * size];
        float radius = size * 0.5f - 1f;
        Vector2 center = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            pixels[y * size + x] = new Color32(255, 255, 255,
                (byte)(Mathf.Clamp01(radius - Vector2.Distance(new Vector2(x, y), center)) * 255f));
        texture.SetPixels32(pixels); texture.Apply(); texture.wrapMode = TextureWrapMode.Clamp;
        circleLocal = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return circleLocal;
    }
}
