using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

// The main game hub for HubScene, built entirely in code (no prefabs, no Inspector wiring).
// Layout (mobile landscape, 16:9 / 1280x720 reference):
//   • full-screen main-page background
//   • top bar: club avatar + name + XP bar + level badge, settings gear, diamond/gold counters
//   • left column: RANKING / SHOP / TEAM square buttons
//   • top-right: season-countdown panel
//   • bottom bar: season-pass (locked), missions (with badge), welcome panel, PLAY → SampleScene
// RANKING and SHOP open "COMING SOON" slide-in overlays; TEAM opens the existing TeamScreenUI as a
// slide-in overlay. No bottom navigation tabs. Sprites load from Assets/Resources/Sprites/.
public class NavigationManager : MonoBehaviour
{
    [SerializeField] private float fadeSeconds = 0.3f; // slide / fade duration for overlays + hub fade-in

    private static readonly Color DarkBar = new Color(0.04f, 0.06f, 0.13f, 0.86f);
    private static readonly Color DarkPanel = new Color(0.03f, 0.05f, 0.11f, 0.92f);
    private static readonly Color OverlayDark = new Color(0.02f, 0.03f, 0.08f, 0.92f);
    private static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    private static readonly Color Cyan = new Color(0f, 0.85f, 1f);
    private static readonly Color Blue = new Color(0.18f, 0.5f, 1f);
    private static readonly Color Bronze = new Color(0.72f, 0.45f, 0.2f);
    private static readonly Color Green = new Color(0.2f, 0.72f, 0.32f);
    private static readonly Color Red = new Color(0.85f, 0.2f, 0.2f);
    private static readonly Color GreyAvatar = new Color(0.5f, 0.53f, 0.6f);

    private static Sprite roundedSprite;  // cached; regenerated after a domain reload
    private static Sprite circleSprite;   // white, tintable
    private static Sprite lockSprite;     // procedural padlock

    private Transform canvasRoot;
    private CanvasGroup hubFade;
    private TextMeshProUGUI goldLabel, diamondLabel; // top-bar currencies, fed by RosterManager

    private GameObject rankingOverlay, shopOverlay, teamOverlay;
    private Coroutine slideRoutine;

    void Start()
    {
        EnsureEventSystem();
        BuildRoot();
        BuildTopBar();
        BuildLeftColumn();
        BuildSeasonTimer();
        BuildBottomBar();
        BuildOverlays();
        RefreshCurrency();
        StartCoroutine(FadeInHub());
    }

    // ------------------------------------------------------------------ shell

    void BuildRoot()
    {
        GameObject canvasGo = new GameObject("HubCanvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        hubFade = canvasGo.AddComponent<CanvasGroup>();
        hubFade.alpha = 0f; // hub fades in on load
        canvasRoot = canvasGo.transform;

        // Full-screen background — scaled to fill.
        Image bg = NewImage(canvasRoot, "Background");
        bg.sprite = LoadSprite("Sprites/main-page-background");
        bg.raycastTarget = false;
        if (bg.sprite == null) bg.color = new Color(0.02f, 0.15f, 0.3f); // pool-blue fallback
        Stretch(bg.rectTransform);
    }

    // ----------------------------------------------------------------- top bar

    void BuildTopBar()
    {
        Image bar = MakePanel(canvasRoot, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 80f), DarkBar);
        bar.gameObject.name = "TopBar";
        bar.raycastTarget = true; // blocks clicks bleeding through
        RectTransform rt = bar.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 80f);

        // Left: grey avatar circle.
        Image avatar = NewImage(bar.transform, "Avatar");
        avatar.sprite = Circle();
        avatar.color = GreyAvatar;
        avatar.raycastTarget = false;
        SetRect(avatar.rectTransform, new Vector2(0f, 0.5f), new Vector2(48f, 0f), new Vector2(60f, 60f));

        // Club name + XP bar + bronze level badge.
        MakeText(bar.transform, "My Club", 20f, new Vector2(0f, 0.5f), new Vector2(150f, 12f),
                 new Vector2(140f, 26f), Color.white, TextAlignmentOptions.Left);

        Image xpBg = MakePanel(bar.transform, new Vector2(0f, 0.5f), new Vector2(150f, -14f),
                               new Vector2(120f, 12f), new Color(0f, 0f, 0f, 0.5f));
        xpBg.raycastTarget = false;
        Image xpFill = NewImage(xpBg.transform, "Fill");
        xpFill.sprite = GetRoundedSprite();
        xpFill.type = Image.Type.Sliced;
        xpFill.color = Blue;
        xpFill.raycastTarget = false;
        RectTransform fr = xpFill.rectTransform;
        fr.anchorMin = new Vector2(0f, 0f);
        fr.anchorMax = new Vector2(0.7f, 1f); // ~70% filled
        fr.offsetMin = new Vector2(1f, 1f);
        fr.offsetMax = new Vector2(-1f, -1f);

        Image badge = NewImage(bar.transform, "LevelBadge");
        badge.sprite = Circle();
        badge.color = Bronze;
        badge.raycastTarget = false;
        SetRect(badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(210f, -14f), new Vector2(22f, 22f));
        MakeText(badge.transform, "1", 13f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 22f),
                 Color.white, TextAlignmentOptions.Center);

        // Settings gear (plain circle placeholder — TMP's default font lacks a gear glyph).
        MakeGearButton(bar.transform, new Vector2(0f, 0.5f), new Vector2(272f, 0f), 48f,
                       () => Debug.Log("Settings coming soon"));

        // Right side, right-to-left: gold [+], gold count, gold icon, diamond [+], diamond count, diamond icon.
        MakePlusButton(bar.transform, new Vector2(1f, 0.5f), new Vector2(-32f, 0f), 30f,
                       () => Debug.Log("Store coming soon"));
        goldLabel = MakeText(bar.transform, "0", 18f, new Vector2(1f, 0.5f), new Vector2(-92f, 0f),
                             new Vector2(66f, 30f), Color.white, TextAlignmentOptions.Right);
        MakeIcon(bar.transform, "Sprites/gold-coin", new Vector2(1f, 0.5f), new Vector2(-145f, 0f), 34f);

        MakePlusButton(bar.transform, new Vector2(1f, 0.5f), new Vector2(-192f, 0f), 30f,
                       () => Debug.Log("Store coming soon"));
        diamondLabel = MakeText(bar.transform, "0", 18f, new Vector2(1f, 0.5f), new Vector2(-246f, 0f),
                                new Vector2(54f, 30f), Color.white, TextAlignmentOptions.Right);
        MakeIcon(bar.transform, "Sprites/diamond-coin", new Vector2(1f, 0.5f), new Vector2(-292f, 0f), 34f);
    }

    // Re-read the player's balances into the top bar. Called on build and by TeamScreenUI after a
    // buy / sell / upgrade so the persistent bar never drifts from the real roster.
    public void RefreshCurrency()
    {
        RosterManager rm = RosterManager.Instance;
        if (goldLabel != null) goldLabel.text = rm.Coins.ToString();
        if (diamondLabel != null) diamondLabel.text = rm.Diamonds.ToString();
    }

    // ------------------------------------------------------------- left column

    void BuildLeftColumn()
    {
        const float x = 150f, step = 125f; // centres 125px apart
        MakeImageButton(canvasRoot, "BtnRanking", "Sprites/ranking-button", new Vector2(0f, 0.5f),
                        new Vector2(x, step), new Vector2(115f, 115f), () => ShowOverlay(rankingOverlay));
        MakeImageButton(canvasRoot, "BtnShop", "Sprites/shop-button", new Vector2(0f, 0.5f),
                        new Vector2(x, 0f), new Vector2(140f, 140f), () => ShowOverlay(shopOverlay));
        MakeImageButton(canvasRoot, "BtnTeam", "Sprites/team-button", new Vector2(0f, 0.5f),
                        new Vector2(x, -step), new Vector2(115f, 115f), () => ShowOverlay(teamOverlay));
    }

    // ------------------------------------------------------------ season timer

    void BuildSeasonTimer()
    {
        Image panel = MakePanel(canvasRoot, new Vector2(1f, 1f), new Vector2(-118f, -126f),
                                new Vector2(200f, 80f), DarkPanel);
        panel.raycastTarget = false;
        MakeText(panel.transform, "SEASON ENDS IN:", 13f, new Vector2(0.5f, 1f), new Vector2(0f, -20f),
                 new Vector2(188f, 20f), new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center);
        MakeText(panel.transform, "2D 10H", 30f, new Vector2(0.5f, 0f), new Vector2(0f, 16f),
                 new Vector2(188f, 40f), Gold, TextAlignmentOptions.Center);
    }

    // -------------------------------------------------------------- bottom bar

    void BuildBottomBar()
    {
        GameObject barGo = new GameObject("BottomBar");
        barGo.transform.SetParent(canvasRoot, false);
        RectTransform bar = barGo.AddComponent<RectTransform>();
        bar.anchorMin = new Vector2(0f, 0f);
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = Vector2.zero;
        bar.sizeDelta = new Vector2(0f, 130f);

        // Season pass (left, locked): art + dark overlay + lock + label.
        Button sp = MakeImageButton(barGo.transform, "BtnSeasonPass", "Sprites/season-pass-button",
                                    new Vector2(0f, 0.5f), new Vector2(195f, 0f), new Vector2(260f, 80f),
                                    () => Debug.Log("Season Pass coming soon"));
        sp.image.preserveAspect = false; // stretch/fill the 220x110 rect (Image Type stays Simple)
        Image ovl = NewImage(sp.transform, "LockOverlay");
        ovl.sprite = GetRoundedSprite();
        ovl.type = Image.Type.Sliced;
        ovl.color = new Color(0f, 0f, 0f, 0.55f);
        ovl.raycastTarget = false;
        Stretch(ovl.rectTransform);
        Image lk = NewImage(ovl.transform, "Lock");
        lk.sprite = MakeLockSprite();
        lk.color = new Color(0.85f, 0.87f, 0.92f, 1f);
        lk.raycastTarget = false;
        SetRect(lk.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 18f), new Vector2(46f, 46f));
        MakeText(ovl.transform, "UNLOCKED AT LEVEL 4", 16f, new Vector2(0.5f, 0f), new Vector2(0f, 20f),
                 new Vector2(260f, 24f), Color.white, TextAlignmentOptions.Center);

        // Missions (centre-left): art + red notification badge.
        Button ms = MakeImageButton(barGo.transform, "BtnMissions", "Sprites/missions-button",
                                    new Vector2(0f, 0.5f), new Vector2(455f, 0f), new Vector2(90f, 90f),
                                    () => Debug.Log("Missions coming soon"));
        Image dot = NewImage(ms.transform, "Badge");
        dot.sprite = Circle();
        dot.color = Red;
        dot.raycastTarget = false;
        SetRect(dot.rectTransform, new Vector2(1f, 1f), new Vector2(-6f, -6f), new Vector2(26f, 26f));
        MakeText(dot.transform, "1", 15f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f),
                 Color.white, TextAlignmentOptions.Center);

        // Card slots (visual placeholders) between the missions button and PLAY.
        BuildCardSlots(barGo.transform);

        // Play (right) → start a match.
        MakeImageButton(barGo.transform, "BtnPlay", "Sprites/play-button", new Vector2(1f, 0.5f),
                        new Vector2(-160f, 0f), new Vector2(320f, 120f), // centre shifted so the wider button stays flush-right on screen
                        () => SceneManager.LoadScene("SampleScene"));
    }

    // Four visual-only "card slot" placeholders between the missions button and PLAY: a dark
    // rounded panel with a rounded outline, a small white-circle lock placeholder, and a timer label.
    void BuildCardSlots(Transform parent)
    {
        string[] times = { "3H", "7H", "12H", "24H" };
        Color slotFill = new Color(0.102f, 0.165f, 0.227f, 0.784f); // #1A2A3A @ alpha 200
        Color slotOutline = new Color(0.165f, 0.290f, 0.416f, 1f);  // #2A4A6A
        const float w = 70f, h = 90f, gap = 8f, rowCenterX = 90f; // centred in the gap between missions and PLAY

        for (int i = 0; i < times.Length; i++)
        {
            float sx = rowCenterX + (i - 1.5f) * (w + gap);

            // outline frame
            Image frame = NewImage(parent, "CardSlot" + (i + 1));
            frame.sprite = GetRoundedSprite();
            frame.type = Image.Type.Sliced;
            frame.color = slotOutline;
            frame.raycastTarget = false;
            SetRect(frame.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(sx, 0f), new Vector2(w, h));

            // dark fill inset 2px (leaves the outline as a thin border)
            Image fill = NewImage(frame.transform, "Fill");
            fill.sprite = GetRoundedSprite();
            fill.type = Image.Type.Sliced;
            fill.color = slotFill;
            fill.raycastTarget = false;
            RectTransform frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);

            // lock placeholder (plain white circle, 20px), centred and nudged up
            Image lockImg = NewImage(frame.transform, "Lock");
            lockImg.sprite = Circle();
            lockImg.color = Color.white;
            lockImg.raycastTarget = false;
            SetRect(lockImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(20f, 20f));

            // timer label below the lock
            MakeText(frame.transform, times[i], 12f, new Vector2(0.5f, 0.5f), new Vector2(0f, -22f),
                     new Vector2(64f, 18f), Color.white, TextAlignmentOptions.Center);
        }
    }

    // ---------------------------------------------------------------- overlays

    void BuildOverlays()
    {
        rankingOverlay = BuildComingSoonOverlay("RANKING");
        shopOverlay = BuildComingSoonOverlay("SHOP");
        teamOverlay = BuildTeamOverlay();
    }

    // Dark full-screen overlay with a centred sheet: "COMING SOON" + close [X]. Starts hidden.
    GameObject BuildComingSoonOverlay(string title)
    {
        GameObject ov = new GameObject("Overlay_" + title);
        ov.transform.SetParent(canvasRoot, false);
        RectTransform ort = ov.AddComponent<RectTransform>();
        Stretch(ort);
        Image backdrop = ov.AddComponent<Image>();
        backdrop.color = OverlayDark;
        backdrop.raycastTarget = true; // swallow clicks to the hub behind
        ov.AddComponent<CanvasGroup>();

        Image sheet = NewImage(ov.transform, "Sheet");
        sheet.sprite = GetRoundedSprite();
        sheet.type = Image.Type.Sliced;
        sheet.color = DarkPanel;
        SetRect(sheet.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 300f));

        MakeText(sheet.transform, title, 24f, new Vector2(0.5f, 1f), new Vector2(0f, -52f),
                 new Vector2(560f, 32f), Cyan, TextAlignmentOptions.Center);
        MakeText(sheet.transform, "COMING SOON", 46f, new Vector2(0.5f, 0.5f), new Vector2(0f, 6f),
                 new Vector2(560f, 70f), Gold, TextAlignmentOptions.Center);
        MakeText(sheet.transform, "This feature is on the way.", 18f, new Vector2(0.5f, 0f),
                 new Vector2(0f, 46f), new Vector2(560f, 28f), Color.white, TextAlignmentOptions.Center);

        GameObject self = ov;
        MakeCloseButton(sheet.transform, () => HideOverlay(self));

        ov.SetActive(false);
        return ov;
    }

    // Dark full-screen overlay hosting the existing TeamScreenUI on a full-canvas sheet + close [X].
    GameObject BuildTeamOverlay()
    {
        GameObject ov = new GameObject("Overlay_TEAM");
        ov.transform.SetParent(canvasRoot, false);
        RectTransform ort = ov.AddComponent<RectTransform>();
        Stretch(ort);
        Image backdrop = ov.AddComponent<Image>();
        backdrop.color = OverlayDark;
        backdrop.raycastTarget = true;
        ov.AddComponent<CanvasGroup>();

        GameObject sheetGo = new GameObject("Sheet");
        sheetGo.transform.SetParent(ov.transform, false);
        RectTransform srt = sheetGo.AddComponent<RectTransform>();
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.pivot = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = Vector2.zero;       // fills the canvas (slid via anchoredPosition)
        srt.anchoredPosition = Vector2.zero;

        TeamScreenUI team = sheetGo.AddComponent<TeamScreenUI>();
        team.Build(sheetGo.transform, this); // passes 'this' so its buys/sells refresh our top bar

        GameObject self = ov;
        MakeCloseButton(ov.transform, () => HideOverlay(self)); // X above the sheet, screen top-right

        ov.SetActive(false);
        return ov;
    }

    void ShowOverlay(GameObject overlay)
    {
        if (overlay == null) return;
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        if (slideRoutine != null) StopCoroutine(slideRoutine);
        slideRoutine = StartCoroutine(SlideOverlay(overlay, true));
    }

    void HideOverlay(GameObject overlay)
    {
        if (overlay == null) return;
        if (slideRoutine != null) StopCoroutine(slideRoutine);
        slideRoutine = StartCoroutine(SlideOverlay(overlay, false));
    }

    // Slide the sheet in from / out to the right while the backdrop fades.
    IEnumerator SlideOverlay(GameObject overlay, bool show)
    {
        CanvasGroup cg = overlay.GetComponent<CanvasGroup>();
        RectTransform sheet = overlay.transform.Find("Sheet") as RectTransform;
        const float off = 1200f; // off-screen-right slide distance (> reference width)
        float dur = Mathf.Max(0.01f, fadeSeconds);
        float t = 0f;

        if (show)
        {
            if (cg != null) cg.alpha = 0f;
            if (sheet != null) sheet.anchoredPosition = new Vector2(off, 0f);
        }

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            float e = show ? k : 1f - k; // 1 = fully in view, 0 = off-screen
            if (cg != null) cg.alpha = e;
            if (sheet != null) sheet.anchoredPosition = new Vector2(Mathf.Lerp(off, 0f, e), 0f);
            yield return null;
        }

        if (cg != null) cg.alpha = show ? 1f : 0f;
        if (sheet != null) sheet.anchoredPosition = new Vector2(show ? 0f : off, 0f);
        if (!show) overlay.SetActive(false);
        slideRoutine = null;
    }

    IEnumerator FadeInHub()
    {
        float dur = Mathf.Max(0.01f, fadeSeconds);
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            hubFade.alpha = Mathf.Clamp01(t / dur);
            yield return null;
        }
        hubFade.alpha = 1f;
    }

    // ------------------------------------------------------------ UI helpers

    Image NewImage(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<Image>();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    Image MakePanel(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
    {
        Image img = NewImage(parent, "Panel");
        img.sprite = GetRoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = color;
        SetRect(img.rectTransform, anchor, pos, size);
        return img;
    }

    Image MakeIcon(Transform parent, string spritePath, Vector2 anchor, Vector2 pos, float size)
    {
        Image img = NewImage(parent, "Icon");
        img.sprite = LoadSprite(spritePath);
        img.preserveAspect = true;
        img.raycastTarget = false;
        if (img.sprite == null) img.color = Gold; // visible square fallback
        SetRect(img.rectTransform, anchor, pos, new Vector2(size, size));
        return img;
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

    // A button whose whole face is a sprite (left column, season pass, missions, play).
    Button MakeImageButton(Transform parent, string name, string spritePath, Vector2 anchor,
                           Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, pos, size);

        Image img = go.AddComponent<Image>();
        img.sprite = LoadSprite(spritePath);
        img.preserveAspect = true;
        if (img.sprite == null) // visible rounded fallback so a missing sprite still shows a button
        {
            img.sprite = GetRoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = DarkPanel;
        }

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);
        AddHover(go);
        return btn;
    }

    // Small green rounded [+] button.
    Button MakePlusButton(Transform parent, Vector2 anchor, Vector2 pos, float size,
                          UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("BtnPlus");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, pos, new Vector2(size, size));

        Image img = go.AddComponent<Image>();
        img.sprite = GetRoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = Green;

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);

        TextMeshProUGUI t = MakeText(go.transform, "+", size * 0.72f, new Vector2(0.5f, 0.5f),
                                     Vector2.zero, new Vector2(size, size), Color.white,
                                     TextAlignmentOptions.Center);
        Stretch(t.rectTransform);
        AddHover(go);
        return btn;
    }

    // Plain circular settings button with a lighter inner hub (gear placeholder).
    Button MakeGearButton(Transform parent, Vector2 anchor, Vector2 pos, float size,
                          UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("BtnSettings");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, pos, new Vector2(size, size));

        Image img = go.AddComponent<Image>();
        img.sprite = Circle();
        img.color = new Color(0.25f, 0.28f, 0.36f, 1f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);

        Image inner = NewImage(go.transform, "Inner");
        inner.sprite = Circle();
        inner.color = new Color(0.6f, 0.63f, 0.7f, 1f);
        inner.raycastTarget = false;
        SetRect(inner.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(size * 0.42f, size * 0.42f));

        AddHover(go);
        return btn;
    }

    // Red rounded [X] close button, pinned to the parent's top-right corner.
    Button MakeCloseButton(Transform parent, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("BtnClose");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(44f, 44f));

        Image img = go.AddComponent<Image>();
        img.sprite = GetRoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = new Color(0.7f, 0.2f, 0.2f, 1f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);

        TextMeshProUGUI t = MakeText(go.transform, "X", 24f, new Vector2(0.5f, 0.5f), Vector2.zero,
                                     new Vector2(44f, 44f), Color.white, TextAlignmentOptions.Center);
        Stretch(t.rectTransform);
        AddHover(go);
        return btn;
    }

    static void AddHover(GameObject go)
    {
        EventTrigger trigger = go.AddComponent<EventTrigger>();
        EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => { if (go != null) go.transform.localScale = Vector3.one * 1.05f; });
        trigger.triggers.Add(enter);
        EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => { if (go != null) go.transform.localScale = Vector3.one; });
        trigger.triggers.Add(exit);
    }

    // Single funnel for Resources sprites so a missing/misimported one names itself in the Console.
    static Sprite LoadSprite(string path)
    {
        Sprite s = Resources.Load<Sprite>(path);
        if (s == null)
            Debug.LogWarning("NavigationManager: sprite not found at Resources/" + path +
                             " — check the file exists there and its Texture Type is 'Sprite (2D and UI)'.");
        return s;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    // ---------------------------------------------------------- generated sprites

    static Sprite GetRoundedSprite()
    {
        if (roundedSprite != null) return roundedSprite;
        const int size = 128, corner = 20;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        float half = size * 0.5f - 0.5f;
        float inner = half - corner;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Max(Mathf.Abs(x - half) - inner, 0f);
                float qy = Mathf.Max(Mathf.Abs(y - half) - inner, 0f);
                float d = Mathf.Sqrt(qx * qx + qy * qy);
                byte a = (byte)(Mathf.Clamp01(corner - d) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                      100f, 0, SpriteMeshType.FullRect,
                                      new Vector4(corner + 2, corner + 2, corner + 2, corner + 2));
        return roundedSprite;
    }

    // White, tintable filled circle (avatars, badges, gear).
    static Sprite Circle()
    {
        if (circleSprite != null) return circleSprite;
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
        circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return circleSprite;
    }

    // White, tintable padlock (rounded body + shackle ring + keyhole) for the locked season pass.
    static Sprite MakeLockSprite()
    {
        if (lockSprite != null) return lockSprite;
        const int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[s * s];
        Color32 on = new Color32(255, 255, 255, 255);
        Color32 clear = new Color32(0, 0, 0, 0);
        Vector2 shackle = new Vector2(s * 0.5f, 40f);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool fill = false;

                // rounded body
                if (x >= 16 && x <= 48 && y >= 10 && y <= 38)
                {
                    float cx = Mathf.Clamp(x, 20f, 44f);
                    float cy = Mathf.Clamp(y, 14f, 34f);
                    if (new Vector2(x - cx, y - cy).sqrMagnitude <= 16f) fill = true;
                }

                // shackle (upper half ring)
                float d = Vector2.Distance(new Vector2(x, y), shackle);
                if (y >= 36 && d >= 9f && d <= 13f) fill = true;

                // keyhole
                if (Vector2.Distance(new Vector2(x, y), new Vector2(s * 0.5f, 24f)) <= 3.5f) fill = false;

                px[y * s + x] = fill ? on : clear;
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        lockSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        return lockSprite;
    }
}
