using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

// The whole hub-navigation shell for HubScene, built entirely in code (no prefabs):
// persistent top bar (club + currencies + settings) and bottom nav (CAREER / TEAM /
// TRANSFERS / MY CLUB / CHALLENGES), plus the five screens themselves with placeholder
// design-only content — no real data or economy yet. Screens cross-fade in 0.3s.
// CAREER's PLAY button loads SampleScene; MainMenu's PLAY loads this scene.
public class NavigationManager : MonoBehaviour
{
    [SerializeField] private float fadeSeconds = 0.3f;

    private static readonly Color NavyBar = new Color(0.05f, 0.1f, 0.25f, 0.95f);
    private static readonly Color NavyPanel = new Color(0.05f, 0.1f, 0.25f, 0.85f);
    private static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    private static readonly Color CyanHi = Color.cyan;

    private static Sprite roundedSprite; // cached; regenerated after domain reload

    private Transform canvasRoot;
    private readonly GameObject[] screens = new GameObject[5];
    private readonly CanvasGroup[] screenGroups = new CanvasGroup[5];
    private readonly TextMeshProUGUI[] navLabels = new TextMeshProUGUI[5];
    private static readonly string[] NavNames = { "CAREER", "TEAM", "TRANSFERS", "MY CLUB", "CHALLENGES" };
    private int currentScreen = -1;
    private Coroutine fadeRoutine;

    void Start()
    {
        EnsureEventSystem();
        BuildRoot();
        BuildTopBar();
        BuildScreens();
        BuildBottomNav();
        SwitchScreen(0, instant: true); // Career is the landing screen
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
        canvasRoot = canvasGo.transform;

        // Full-screen background — same background.png as the main menu.
        Image bg = NewImage(canvasRoot, "Background");
        bg.sprite = LoadSprite("Sprites/background");
        if (bg.sprite == null) bg.color = new Color(0.02f, 0.15f, 0.3f); // pool-blue fallback
        Stretch(bg.rectTransform);
    }

    void BuildTopBar()
    {
        Image bar = NewImage(canvasRoot, "TopBar");
        bar.color = NavyBar;
        bar.raycastTarget = true; // blocks clicks bleeding through to screens below
        RectTransform rt = bar.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 80f);

        // Left: club logo placeholder (white circle) + team name.
        Image logo = NewImage(bar.transform, "ClubLogo");
        logo.sprite = MakeCircleSprite(64);
        SetRect(logo.rectTransform, new Vector2(0f, 0.5f), new Vector2(45f, 0f), new Vector2(50f, 50f));
        MakeText(bar.transform, "My Club", 18f, new Vector2(0f, 0.5f), new Vector2(150f, 0f),
                 new Vector2(160f, 40f), Color.white, TextAlignmentOptions.Left);

        // Right side, laid right-to-left: settings, diamond count, diamond, gold count, gold.
        // ("SET" instead of a ⚙ glyph — the default TMP font doesn't have that character.)
        Button settings = MakeBareButton(bar.transform, "BtnSettings", new Vector2(1f, 0.5f),
                                         new Vector2(-40f, 0f), new Vector2(50f, 50f),
                                         () => Debug.Log("Settings coming soon"));
        if (settings.image != null) settings.image.color = Color.clear;
        MakeText(settings.transform, "SET", 20f, new Vector2(0.5f, 0.5f), Vector2.zero,
                 new Vector2(50f, 50f), Color.white, TextAlignmentOptions.Center);

        MakeText(bar.transform, "50", 18f, new Vector2(1f, 0.5f), new Vector2(-95f, 0f),
                 new Vector2(60f, 40f), Color.white, TextAlignmentOptions.Left);
        MakeIcon(bar.transform, "Sprites/diamond-coin", new Vector2(1f, 0.5f), new Vector2(-145f, 0f), 40f);
        MakeText(bar.transform, "1000", 18f, new Vector2(1f, 0.5f), new Vector2(-225f, 0f),
                 new Vector2(70f, 40f), Color.white, TextAlignmentOptions.Left);
        MakeIcon(bar.transform, "Sprites/gold-coin", new Vector2(1f, 0.5f), new Vector2(-275f, 0f), 40f);
    }

    void BuildBottomNav()
    {
        Image bar = NewImage(canvasRoot, "BottomNav");
        bar.color = NavyBar;
        bar.raycastTarget = true;
        RectTransform rt = bar.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 70f);

        for (int i = 0; i < 5; i++)
        {
            int idx = i; // captured by the click closure
            GameObject go = new GameObject("Nav" + NavNames[i]);
            go.transform.SetParent(bar.transform, false);
            RectTransform brt = go.AddComponent<RectTransform>();
            brt.anchorMin = new Vector2(i / 5f, 0f);
            brt.anchorMax = new Vector2((i + 1) / 5f, 1f);
            brt.offsetMin = brt.offsetMax = Vector2.zero;

            Image img = go.AddComponent<Image>();
            img.color = Color.clear; // bar provides the navy; this is just the hit area

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => SwitchScreen(idx));

            navLabels[i] = MakeText(go.transform, NavNames[i], 18f, new Vector2(0.5f, 0.5f),
                                    Vector2.zero, new Vector2(0f, 0f), Color.white,
                                    TextAlignmentOptions.Center);
            RectTransform lrt = navLabels[i].rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        }
    }

    void BuildScreens()
    {
        for (int i = 0; i < 5; i++)
        {
            GameObject screen = new GameObject("Screen" + NavNames[i]);
            screen.transform.SetParent(canvasRoot, false);
            RectTransform rt = screen.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(0f, 70f);  // clear the bottom nav
            rt.offsetMax = new Vector2(0f, -80f); // clear the top bar
            screenGroups[i] = screen.AddComponent<CanvasGroup>();
            screens[i] = screen;
            screen.SetActive(false);
        }

        BuildCareerScreen(screens[0].transform);
        BuildTeamScreen(screens[1].transform);
        BuildTransfersScreen(screens[2].transform);
        BuildMyClubScreen(screens[3].transform);
        BuildChallengesScreen(screens[4].transform);
    }

    void SwitchScreen(int idx, bool instant = false)
    {
        if (idx == currentScreen) return;

        for (int i = 0; i < navLabels.Length; i++)
            navLabels[i].color = i == idx ? CyanHi : Color.white;

        if (currentScreen >= 0) screens[currentScreen].SetActive(false);
        currentScreen = idx;
        screens[idx].SetActive(true);

        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        if (instant) { screenGroups[idx].alpha = 1f; return; }
        fadeRoutine = StartCoroutine(FadeInScreen(screenGroups[idx]));
    }

    IEnumerator FadeInScreen(CanvasGroup group)
    {
        group.alpha = 0f;
        float t = 0f;
        while (t < fadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(t / fadeSeconds);
            yield return null;
        }
        group.alpha = 1f;
    }

    // -------------------------------------------------------------- 1. CAREER

    void BuildCareerScreen(Transform root)
    {
        MakeTitle(root, "CAREER");

        // Division badge.
        Image badge = MakePanel(root, new Vector2(320f, 70f), new Vector2(0f, 195f));
        MakeText(badge.transform, "DIVISION 3", 34f, new Vector2(0.5f, 0.5f), Vector2.zero,
                 new Vector2(300f, 60f), Gold, TextAlignmentOptions.Center);

        // Standings: fake records, My Club highlighted.
        Image table = MakePanel(root, new Vector2(560f, 240f), new Vector2(0f, 25f));
        string[] rows =
        {
            "1   Sharks FC      W3  D0  L0   9",
            "2   My Club        W2  D1  L0   7",
            "3   Neptune        W1  D1  L1   4",
            "4   Aqua Bulls     W1  D0  L2   3",
            "5   Wave Riders    W0  D0  L3   0",
        };
        for (int i = 0; i < rows.Length; i++)
        {
            Color c = rows[i].Contains("My Club") ? CyanHi : Color.white;
            MakeText(table.transform, rows[i], 20f, new Vector2(0.5f, 1f),
                     new Vector2(0f, -30f - i * 42f), new Vector2(520f, 36f), c,
                     TextAlignmentOptions.Left);
        }

        MakeText(root, "Season: 3 matches played, 12 remaining", 18f, new Vector2(0.5f, 0.5f),
                 new Vector2(0f, -125f), new Vector2(600f, 30f), Color.white,
                 TextAlignmentOptions.Center);

        MakeButton(root, "PLAY", new Vector2(300f, 70f), new Vector2(0f, -200f), 28f,
                   () => SceneManager.LoadScene("SampleScene"));
    }

    // ---------------------------------------------------------------- 2. TEAM

    void BuildTeamScreen(Transform root)
    {
        MakeTitle(root, "TEAM");

        MakeText(root, "OVR 72", 30f, new Vector2(0.5f, 0.5f), new Vector2(330f, 170f),
                 new Vector2(160f, 44f), Gold, TextAlignmentOptions.Center);
        MakeText(root, "Formation: 2-3-2", 18f, new Vector2(0.5f, 0.5f), new Vector2(-330f, 170f),
                 new Vector2(200f, 30f), Color.white, TextAlignmentOptions.Center);

        // 2-3-2 formation, attacking upward: wings high, mid three, backs + keeper low.
        (string pos, Vector2 at)[] slots =
        {
            ("LW", new Vector2(-110f, 110f)), ("RW", new Vector2(110f, 110f)),
            ("LF", new Vector2(-180f, -15f)), ("CF", new Vector2(0f, -15f)), ("RF", new Vector2(180f, -15f)),
            ("CB", new Vector2(-110f, -150f)), ("GK", new Vector2(110f, -150f)),
        };
        foreach (var s in slots)
        {
            Image card = MakePanel(root, new Vector2(80f, 110f), s.at);
            Outline border = card.gameObject.AddComponent<Outline>();
            border.effectColor = Color.white;
            border.effectDistance = new Vector2(2f, 2f);
            MakeText(card.transform, s.pos, 20f, new Vector2(0.5f, 1f), new Vector2(0f, -24f),
                     new Vector2(76f, 26f), Color.white, TextAlignmentOptions.Center);
            MakeText(card.transform, "TAP TO\nADD", 11f, new Vector2(0.5f, 0.5f), new Vector2(0f, -14f),
                     new Vector2(76f, 40f), new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Center);
        }
    }

    // ----------------------------------------------------------- 3. TRANSFERS

    void BuildTransfersScreen(Transform root)
    {
        MakeTitle(root, "TRANSFERS");

        // AGENTS row.
        MakeText(root, "AGENTS", 20f, new Vector2(0.5f, 0.5f), new Vector2(-470f, 180f),
                 new Vector2(140f, 30f), Color.white, TextAlignmentOptions.Center);
        MakeAgentButton(root, "COMMON 40", new Vector2(-240f, 180f));
        MakeAgentButton(root, "RARE 150", new Vector2(0f, 180f));
        MakeAgentButton(root, "GOLDEN 375", new Vector2(240f, 180f));

        MakeText(root, "Daily refresh in: 23:45:12", 18f, new Vector2(0.5f, 0.5f),
                 new Vector2(440f, 180f), new Vector2(280f, 30f), Color.white,
                 TextAlignmentOptions.Center);

        // 6 player cards, 2 rows of 3.
        (string name, string pos, int ovr, int price)[] cards =
        {
            ("D. Petrov", "CF", 71, 450), ("M. Costa", "LW", 68, 300), ("A. Volkov", "GK", 74, 600),
            ("J. Smith", "RF", 65, 250), ("L. Garcia", "CB", 70, 400), ("K. Tanaka", "CF", 77, 800),
        };
        for (int i = 0; i < cards.Length; i++)
        {
            var c = cards[i];
            float x = (i % 3 - 1) * 250f;
            float y = i < 3 ? 10f : -165f;
            Image card = MakePanel(root, new Vector2(230f, 160f), new Vector2(x, y));

            MakeText(card.transform, c.name, 20f, new Vector2(0.5f, 1f), new Vector2(0f, -22f),
                     new Vector2(210f, 28f), Color.white, TextAlignmentOptions.Center);
            MakeText(card.transform, c.pos + "   OVR " + c.ovr, 17f, new Vector2(0.5f, 1f),
                     new Vector2(0f, -52f), new Vector2(210f, 24f), CyanHi, TextAlignmentOptions.Center);
            MakeIcon(card.transform, "Sprites/gold-coin", new Vector2(0.5f, 1f), new Vector2(-45f, -82f), 26f);
            MakeText(card.transform, c.price.ToString(), 17f, new Vector2(0.5f, 1f), new Vector2(10f, -82f),
                     new Vector2(90f, 24f), Gold, TextAlignmentOptions.Left);
            MakeButton(card.transform, "BUY", new Vector2(120f, 36f), new Vector2(0f, -135f), 17f,
                       () => Debug.Log("Purchase coming soon"));
        }
    }

    void MakeAgentButton(Transform root, string label, Vector2 pos)
    {
        Button btn = MakeButton(root, label, new Vector2(200f, 52f), pos, 19f,
                                () => Debug.Log("Agents coming soon"));
        MakeIcon(btn.transform, "Sprites/diamond-coin", new Vector2(1f, 0.5f), new Vector2(-22f, 0f), 28f);
    }

    // ------------------------------------------------------------- 4. MY CLUB

    void BuildMyClubScreen(Transform root)
    {
        MakeTitle(root, "MY CLUB");

        BuildUpgradeCard(root, new Vector2(-170f, 70f), "STADIUM", "Lv 1",
                         "Capacity: 500 fans", "500");
        BuildUpgradeCard(root, new Vector2(170f, 70f), "POOL", "Lv 1",
                         "Post-match bonus: +10 gold", "300");

        MakeText(root, "CUSTOMIZE", 22f, new Vector2(0.5f, 0.5f), new Vector2(0f, -115f),
                 new Vector2(220f, 32f), Color.white, TextAlignmentOptions.Center);
        MakeButton(root, "CAP COLOR", new Vector2(220f, 56f), new Vector2(-130f, -180f), 20f,
                   () => Debug.Log("Customization coming soon"));
        MakeButton(root, "SWIMWEAR", new Vector2(220f, 56f), new Vector2(130f, -180f), 20f,
                   () => Debug.Log("Customization coming soon"));
    }

    void BuildUpgradeCard(Transform root, Vector2 pos, string title, string level,
                          string statLine, string cost)
    {
        Image card = MakePanel(root, new Vector2(300f, 230f), pos);
        MakeText(card.transform, title, 26f, new Vector2(0.5f, 1f), new Vector2(0f, -28f),
                 new Vector2(280f, 32f), Color.white, TextAlignmentOptions.Center);
        MakeText(card.transform, level, 20f, new Vector2(0.5f, 1f), new Vector2(0f, -62f),
                 new Vector2(280f, 26f), CyanHi, TextAlignmentOptions.Center);
        MakeText(card.transform, statLine, 16f, new Vector2(0.5f, 1f), new Vector2(0f, -92f),
                 new Vector2(280f, 24f), Color.white, TextAlignmentOptions.Center);
        MakeIcon(card.transform, "Sprites/gold-coin", new Vector2(0.5f, 1f), new Vector2(-40f, -125f), 26f);
        MakeText(card.transform, cost, 18f, new Vector2(0.5f, 1f), new Vector2(15f, -125f),
                 new Vector2(100f, 26f), Gold, TextAlignmentOptions.Left);
        MakeButton(card.transform, "UPGRADE", new Vector2(180f, 46f), new Vector2(0f, -190f), 19f,
                   () => Debug.Log("Upgrade coming soon"));
    }

    // ---------------------------------------------------------- 5. CHALLENGES

    void BuildChallengesScreen(Transform root)
    {
        MakeTitle(root, "CHALLENGES");

        (string name, string progress, string gold, string diamonds)[] daily =
        {
            ("Score 3 Goals", "0/3", "50", "5"),
            ("Win 2 Matches", "0/2", "50", "5"),
            ("Make 10 Passes", "0/10", "50", "5"),
        };
        for (int i = 0; i < daily.Length; i++)
        {
            var d = daily[i];
            Image card = MakePanel(root, new Vector2(640f, 86f), new Vector2(0f, 115f - i * 105f));

            MakeText(card.transform, d.name, 22f, new Vector2(0f, 0.5f), new Vector2(30f, 0f),
                     new Vector2(240f, 40f), Color.white, TextAlignmentOptions.Left);
            MakeText(card.transform, d.progress, 20f, new Vector2(0f, 0.5f), new Vector2(280f, 0f),
                     new Vector2(80f, 36f), CyanHi, TextAlignmentOptions.Center);
            MakeIcon(card.transform, "Sprites/gold-coin", new Vector2(0f, 0.5f), new Vector2(370f, 0f), 28f);
            MakeText(card.transform, d.gold, 18f, new Vector2(0f, 0.5f), new Vector2(405f, 0f),
                     new Vector2(50f, 32f), Gold, TextAlignmentOptions.Left);
            MakeIcon(card.transform, "Sprites/diamond-coin", new Vector2(0f, 0.5f), new Vector2(450f, 0f), 28f);
            MakeText(card.transform, d.diamonds, 18f, new Vector2(0f, 0.5f), new Vector2(485f, 0f),
                     new Vector2(40f, 32f), Gold, TextAlignmentOptions.Left);

            // Greyed-out claim button — challenges aren't completable yet.
            // Grey both layers (cyan border + navy fill) of the bordered button.
            Button claim = MakeButton(card.transform, "CLAIM", new Vector2(100f, 40f),
                                      new Vector2(265f, 0f), 17f, null);
            claim.interactable = false;
            claim.image.color = new Color(0.4f, 0.4f, 0.45f, 0.7f);
            Transform fillT = claim.transform.Find("Fill");
            if (fillT != null) fillT.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
        }

        MakeText(root, "Resets in: 23:45:12", 18f, new Vector2(0.5f, 0.5f), new Vector2(0f, -215f),
                 new Vector2(300f, 30f), Color.white, TextAlignmentOptions.Center);
    }

    // ------------------------------------------------------------ UI helpers

    void MakeTitle(Transform root, string title)
    {
        MakeText(root, title, 40f, new Vector2(0.5f, 1f), new Vector2(0f, -36f),
                 new Vector2(500f, 50f), Color.white, TextAlignmentOptions.Center);
    }

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

    Image MakePanel(Transform parent, Vector2 size, Vector2 pos)
    {
        Image img = NewImage(parent, "Panel");
        img.sprite = GetRoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = NavyPanel;
        SetRect(img.rectTransform, new Vector2(0.5f, 0.5f), pos, size);
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

    // Single funnel for Resources sprites so a missing/misimported one names itself
    // in the Console instead of failing silently (or worse, throwing downstream).
    static Sprite LoadSprite(string path)
    {
        Sprite s = Resources.Load<Sprite>(path);
        if (s == null)
            Debug.LogWarning("NavigationManager: sprite not found at Resources/" + path +
                             " — check the file exists there and its Texture Type is 'Sprite (2D and UI)'.");
        return s;
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

    // Bare clickable area (no panel styling) — used for the settings gear.
    Button MakeBareButton(Transform parent, string name, Vector2 anchor, Vector2 pos,
                          Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, pos, size);
        Image img = go.AddComponent<Image>();
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);
        AddHover(go);
        return btn;
    }

    // Standard navy button with a cyan border + 1.05x hover. The border is a second
    // image underneath (cyan root, navy fill inset 3px) — NOT a TMP outline:
    // TMP_Text.set_outlineWidth crashes when the font material isn't initialized yet.
    Button MakeButton(Transform parent, string label, Vector2 size, Vector2 pos,
                      float fontSize, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn" + label);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, new Vector2(0.5f, 0.5f), pos, size);

        Image border = go.AddComponent<Image>();
        border.sprite = GetRoundedSprite();
        border.type = Image.Type.Sliced;
        border.color = new Color(0f, 0.8f, 1f, 1f);

        Image fill = NewImage(go.transform, "Fill");
        fill.sprite = GetRoundedSprite();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.05f, 0.1f, 0.25f, 1f); // opaque so the cyan doesn't bleed through
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f);
        frt.offsetMax = new Vector2(-3f, -3f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = border;
        if (onClick != null) btn.onClick.AddListener(onClick);

        TextMeshProUGUI txt = MakeText(go.transform, label, fontSize, new Vector2(0.5f, 0.5f),
                                       Vector2.zero, Vector2.zero, Color.white,
                                       TextAlignmentOptions.Center);
        RectTransform trt = txt.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        AddHover(go);
        return btn;
    }

    static void AddHover(GameObject go)
    {
        EventTrigger trigger = go.AddComponent<EventTrigger>();
        EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => go.transform.localScale = Vector3.one * 1.05f);
        trigger.triggers.Add(enter);
        EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => go.transform.localScale = Vector3.one);
        trigger.triggers.Add(exit);
    }

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
        roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                      100f, 0, SpriteMeshType.FullRect,
                                      new Vector4(corner + 2, corner + 2, corner + 2, corner + 2));
        return roundedSprite;
    }

    static Sprite MakeCircleSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        float r = size * 0.5f - 1f;
        Vector2 c = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }
}
