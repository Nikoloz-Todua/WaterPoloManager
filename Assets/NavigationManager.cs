using System.Collections;
using System.Collections.Generic;
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
//   • bottom bar: season-pass (locked), missions (with badge), card slots, PLAY → Game Mode overlay
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
    private static readonly Color GameModeBg = new Color(0.039f, 0.086f, 0.157f, 1f); // #0A1628 game-mode backdrop
    private static readonly Color CardGold = new Color(1f, 0.843f, 0f);               // #FFD700 unlocked-card frame

    private static Sprite roundedSprite;  // cached; regenerated after a domain reload
    private static Sprite circleSprite;   // white, tintable
    private static Sprite lockSprite;     // procedural padlock
    private static Sprite gradientSprite; // bottom-up black gradient for card name legibility
    private static Sprite lockSignSprite; // lock-sign art, cropped to its content box
    private static Sprite vignetteSprite; // radial edge-darkening overlay

    // Full-frame sprites wrapped straight from their Texture2D (works regardless of the PNG's sprite
    // import mode). Keyed by Resources path so pool-screen / back-button / competition bg share one cache.
    private static readonly Dictionary<string, Sprite> textureSpriteCache = new Dictionary<string, Sprite>();

    // Game-mode cards, captured at build so the overlay can replay a staggered entry each time it opens.
    private readonly List<RectTransform> gmCardRects = new List<RectTransform>();
    private readonly List<CanvasGroup> gmCardGroups = new List<CanvasGroup>();
    private readonly List<Vector2> gmCardBasePos = new List<Vector2>();
    private readonly List<bool> gmCardSelected = new List<bool>();

    private Transform canvasRoot;
    private CanvasGroup hubFade;
    private TextMeshProUGUI goldLabel, diamondLabel;     // hub top-bar currencies, fed by RosterManager
    private TextMeshProUGUI gmGoldLabel, gmDiamondLabel; // game-mode top-bar currencies, fed by RosterManager

    private GameObject rankingOverlay, shopOverlay, teamOverlay, gameModeOverlay;
    private GameObject standingsOverlay, preMatchOverlay; // built lazily, content rebuilt on each open
    private Coroutine slideRoutine;

    // Competition display names, shared by the cards, standings and pre-match screens.
    private static readonly string[] CompNames =
        { "DIVISION 1", "PREMIER LEAGUE", "CONTINENTAL CUP", "WORLD CHAMPIONS LEAGUE" };

    // Competition-screen view state (reset on each open; rebuilt in place on tab/expand taps).
    private int compTab;                                     // 0 = GROUP STAGE, 1 = KNOCKOUT
    private readonly bool[] compGroupExpanded = new bool[2]; // per-group expanded table flag

    // Where the TEAM overlay returns when closed: the hub ("HUB") or the competition screen
    // ("COMPETITION"). Minimal navigation context — the underlying overlay usually just stays
    // active beneath, this flag covers the case where it isn't.
    private string teamReturnTo = "HUB";

    // Post-match reward slots (bottom bar): container rebuilt on any state change, countdown
    // labels ticked from Update, ready-flags so a finished countdown triggers one rebuild.
    private Transform rewardSlotRow;
    private readonly TextMeshProUGUI[] rewardTimeLabels = new TextMeshProUGUI[PostMatchRewardManager.SlotCount];
    private readonly bool[] rewardShownReady = new bool[PostMatchRewardManager.SlotCount];
    private GameObject rewardPopup; // built per open, destroyed on close
    private float rewardTickTimer;

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
        if (gmGoldLabel != null) gmGoldLabel.text = rm.Coins.ToString();
        if (gmDiamondLabel != null) gmDiamondLabel.text = rm.Diamonds.ToString();
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
                        new Vector2(x, -step), new Vector2(115f, 115f), () => OpenTeamScreen("HUB"));
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

        // Post-match reward slots (live) between the missions button and PLAY.
        BuildRewardSlots(barGo.transform);

        // Play (right) → open the Game Mode overlay (competition picker), not the match directly.
        MakeImageButton(barGo.transform, "BtnPlay", "Sprites/play-button", new Vector2(1f, 0.5f),
                        new Vector2(-160f, 0f), new Vector2(320f, 120f), // centre shifted so the wider button stays flush-right on screen
                        () => ShowOverlay(gameModeOverlay));
    }

    // ------------------------------------------------------ post-match reward slots

    // Live Clash-style reward slots between the missions button and PLAY. State comes from
    // PostMatchRewardManager (persistent JSON); the row is rebuilt on any state change, and
    // Update ticks the countdown labels of Unlocking slots.
    void BuildRewardSlots(Transform parent)
    {
        GameObject rowGo = new GameObject("RewardSlots");
        rowGo.transform.SetParent(parent, false);
        RectTransform rrt = rowGo.AddComponent<RectTransform>();
        SetRect(rrt, new Vector2(0.5f, 0.5f), new Vector2(90f, 0f), new Vector2(320f, 100f));
        rewardSlotRow = rowGo.transform;
        RebuildRewardSlots();
    }

    void RebuildRewardSlots()
    {
        if (rewardSlotRow == null) return;
        ClearChildren(rewardSlotRow);
        PostMatchRewardManager mgr = PostMatchRewardManager.Instance;
        const float w = 70f, h = 90f, gap = 8f;

        for (int i = 0; i < PostMatchRewardManager.SlotCount; i++)
        {
            int idx = i;
            PostMatchRewardManager.Slot slot = mgr.GetSlot(i);
            bool ready = mgr.IsReady(i);
            rewardTimeLabels[i] = null;
            rewardShownReady[i] = ready;
            float sx = (i - 1.5f) * (w + gap);

            // outline frame + dark inset fill (same look as the old placeholders)
            Image frame = NewImage(rewardSlotRow, "RewardSlot" + (i + 1));
            frame.sprite = GetRoundedSprite();
            frame.type = Image.Type.Sliced;
            frame.color = ready ? CardGold : new Color(0.165f, 0.290f, 0.416f, 1f);
            frame.raycastTarget = true;
            SetRect(frame.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(sx, 0f), new Vector2(w, h));

            Image fill = NewImage(frame.transform, "Fill");
            fill.sprite = GetRoundedSprite();
            fill.type = Image.Type.Sliced;
            fill.color = slot.State == PostMatchRewardManager.SlotState.Empty
                ? new Color(0.05f, 0.08f, 0.12f, 0.55f)                 // greyed empty slot
                : new Color(0.102f, 0.165f, 0.227f, 0.9f);              // #1A2A3A
            fill.raycastTarget = false;
            RectTransform frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);

            if (slot.State == PostMatchRewardManager.SlotState.Empty)
            {
                MakeText(frame.transform, "EMPTY", 11f, new Vector2(0.5f, 0.5f), Vector2.zero,
                         new Vector2(64f, 18f), new Color(1f, 1f, 1f, 0.35f), TextAlignmentOptions.Center);
                continue;
            }

            // Tier pack art + status label.
            CardPack.TierPackDef def = CardPack.GetTierPack(slot.Tier);
            Image icon = NewImage(frame.transform, "PackIcon");
            icon.sprite = LoadSprite(def.SpritePath);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            if (icon.sprite == null) icon.color = CardPack.TierColor(slot.Tier);
            SetRect(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(54f, 54f));

            string label;
            Color labelCol = Color.white;
            if (ready) { label = "OPEN!"; labelCol = Gold; }
            else if (slot.State == PostMatchRewardManager.SlotState.Unlocking)
                label = PostMatchRewardManager.FormatRemaining(mgr.SecondsRemaining(i));
            else label = def.UnlockLabel; // Locked: shows the tier's unlock duration (3H/7H/12H/24H)

            TextMeshProUGUI lbl = MakeText(frame.transform, label, 12f, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -26f), new Vector2(66f, 20f), labelCol, TextAlignmentOptions.Center);
            if (!ready && slot.State == PostMatchRewardManager.SlotState.Unlocking) rewardTimeLabels[i] = lbl;

            Button btn = frame.gameObject.AddComponent<Button>();
            btn.targetGraphic = frame;
            if (ready) btn.onClick.AddListener(() => OpenReadyRewardSlot(idx));
            else if (slot.State == PostMatchRewardManager.SlotState.Locked)
                btn.onClick.AddListener(() => OpenRewardPopup(idx));
            AddHover(frame.gameObject);
        }
    }

    // Ticks the Unlocking countdowns twice a second; a countdown that hits zero rebuilds the row
    // so the slot flips to the gold OPEN! state.
    void Update()
    {
        if (rewardSlotRow == null) return;
        rewardTickTimer -= Time.unscaledDeltaTime;
        if (rewardTickTimer > 0f) return;
        rewardTickTimer = 0.5f;

        PostMatchRewardManager mgr = PostMatchRewardManager.Instance;
        for (int i = 0; i < PostMatchRewardManager.SlotCount; i++)
        {
            if (mgr.GetSlot(i).State != PostMatchRewardManager.SlotState.Unlocking) continue;
            if (mgr.IsReady(i))
            {
                if (!rewardShownReady[i]) { RebuildRewardSlots(); return; }
            }
            else if (rewardTimeLabels[i] != null)
                rewardTimeLabels[i].text = PostMatchRewardManager.FormatRemaining(mgr.SecondsRemaining(i));
        }
    }

    // "TAP TO UNLOCK" popup for a Locked slot: pack art, tier name, "UP TO N PLAYERS", the tier's
    // internal drop-rate rows, and START UNLOCKING. Built fresh each open, destroyed on close.
    void OpenRewardPopup(int slotIndex)
    {
        PostMatchRewardManager mgr = PostMatchRewardManager.Instance;
        PostMatchRewardManager.Slot slot = mgr.GetSlot(slotIndex);
        if (slot == null || slot.State != PostMatchRewardManager.SlotState.Locked) return;
        CardPack.TierPackDef def = CardPack.GetTierPack(slot.Tier);

        if (rewardPopup != null) Destroy(rewardPopup);
        GameObject ov = new GameObject("RewardPopup");
        ov.transform.SetParent(canvasRoot, false);
        ov.transform.SetAsLastSibling();
        Stretch(ov.AddComponent<RectTransform>());
        Image dark = ov.AddComponent<Image>();
        dark.color = OverlayDark;
        dark.raycastTarget = true;
        rewardPopup = ov;

        Image sheet = NewImage(ov.transform, "Sheet");
        sheet.sprite = GetRoundedSprite();
        sheet.type = Image.Type.Sliced;
        sheet.color = DarkPanel;
        SetRect(sheet.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(660f, 520f));

        Image art = NewImage(sheet.transform, "PackArt");
        art.sprite = LoadSprite(def.SpritePath);
        art.preserveAspect = true;
        art.raycastTarget = false;
        if (art.sprite == null) art.color = CardPack.TierColor(slot.Tier);
        SetRect(art.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(130f, 130f));

        MakeText(sheet.transform, def.name, 28f, new Vector2(0.5f, 1f), new Vector2(0f, -178f),
                 new Vector2(500f, 36f), CardPack.TierColor(slot.Tier), TextAlignmentOptions.Center);
        MakeText(sheet.transform, "UP TO " + def.maxCards + " PLAYERS", 18f, new Vector2(0.5f, 1f),
                 new Vector2(0f, -212f), new Vector2(500f, 26f), Color.white, TextAlignmentOptions.Center);

        // Drop-rate rows: rarity dot + rarity name + percentage.
        float rowY = -252f;
        foreach (var (rarity, weight) in def.odds)
        {
            Image dot = NewImage(sheet.transform, "Dot");
            dot.sprite = Circle();
            dot.color = PlayerData.RarityTint(rarity);
            dot.raycastTarget = false;
            SetRect(dot.rectTransform, new Vector2(0.5f, 1f), new Vector2(-160f, rowY), new Vector2(20f, 20f));
            MakeText(sheet.transform, rarity.ToString().ToUpper(), 17f, new Vector2(0.5f, 1f),
                     new Vector2(-30f, rowY), new Vector2(220f, 24f), Color.white, TextAlignmentOptions.Left);
            MakeText(sheet.transform, (weight * 100f).ToString("0.#") + "%", 17f, new Vector2(0.5f, 1f),
                     new Vector2(130f, rowY), new Vector2(120f, 24f), Gold, TextAlignmentOptions.Right);
            rowY -= 32f;
        }

        // One unlock at a time (Clash rule): the button greys out while another slot counts down.
        bool busy = mgr.AnyUnlocking();
        Button start = MakeActionButton(sheet.transform, busy ? "ANOTHER PACK IS UNLOCKING" : "START UNLOCKING",
            new Vector2(0.5f, 0f), new Vector2(0f, 52f), new Vector2(400f, 64f),
            busy ? new Color(0.3f, 0.34f, 0.4f, 1f) : Green, () =>
            {
                if (PostMatchRewardManager.Instance.AnyUnlocking()) return;
                PostMatchRewardManager.Instance.StartUnlock(slotIndex);
                Destroy(rewardPopup);
                rewardPopup = null;
                RebuildRewardSlots();
            });
        start.interactable = !busy;

        MakeCloseButton(sheet.transform, () => { Destroy(rewardPopup); rewardPopup = null; });
    }

    // Tap on a Ready slot: open the pack, grant cards (duplicates → coins), show the reveal.
    void OpenReadyRewardSlot(int slotIndex)
    {
        List<CardPack.GrantResult> results = PostMatchRewardManager.Instance.OpenSlot(slotIndex);
        if (results == null) return;
        RefreshCurrency();
        RebuildRewardSlots();
        PackRevealUI.Show(canvasRoot, results, RefreshCurrency);
    }

    // ---------------------------------------------------------------- overlays

    void BuildOverlays()
    {
        rankingOverlay = BuildComingSoonOverlay("RANKING");
        shopOverlay = BuildShopOverlay();
        teamOverlay = BuildTeamOverlay();
        gameModeOverlay = BuildGameModeOverlay();
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

    // Dark full-screen overlay hosting TeamScreenUI on a full-canvas sheet. The team screen owns its
    // own back arrow (→ CloseTeamScreen), so this overlay adds no [X] of its own.
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
        // No overlay [X] here: TeamScreenUI draws its own back arrow in its top bar, which calls
        // CloseTeamScreen() below — a single, unambiguous close affordance.

        ov.SetActive(false);
        return ov;
    }

    // Opens the TEAM overlay, remembering where its back arrow should land.
    void OpenTeamScreen(string returnTo)
    {
        teamReturnTo = returnTo;
        ShowOverlay(teamOverlay);
    }

    // Dark full-screen overlay hosting ShopUI on a full-canvas sheet (same shell as the team
    // overlay). ShopUI owns its own back arrow → CloseShopScreen.
    GameObject BuildShopOverlay()
    {
        GameObject ov = new GameObject("Overlay_SHOP");
        ov.transform.SetParent(canvasRoot, false);
        Stretch(ov.AddComponent<RectTransform>());
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
        srt.sizeDelta = Vector2.zero;
        srt.anchoredPosition = Vector2.zero;

        ShopUI shop = sheetGo.AddComponent<ShopUI>();
        shop.Build(sheetGo.transform, this);

        ov.SetActive(false);
        return ov;
    }

    // Called by ShopUI's back arrow.
    public void CloseShopScreen() => HideOverlay(shopOverlay);

    // Called by TeamScreenUI's back arrow. The overlay we came from normally stays active beneath the
    // team sheet, so sliding it closed reveals the right screen on its own; the COMPETITION branch is
    // a safety net that re-shows the competition screen if it was somehow deactivated. No coroutine
    // there — starting a second slide would cancel the team overlay's closing animation.
    public void CloseTeamScreen()
    {
        HideOverlay(teamOverlay);
        if (teamReturnTo == "COMPETITION" && standingsOverlay != null && !standingsOverlay.activeSelf)
        {
            standingsOverlay.SetActive(true);
            standingsOverlay.transform.SetSiblingIndex(teamOverlay.transform.GetSiblingIndex());
            if (standingsOverlay.transform.Find("Sheet") is RectTransform sh) sh.anchoredPosition = Vector2.zero;
            CanvasGroup cg = standingsOverlay.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;
        }
    }

    // ------------------------------------------------------------- game mode

    // Full-screen "GAME MODE" overlay (built in code, no prefab — same slide-in shell as the team
    // overlay). Four competition cards in a row; each unlocks after the previous is won (PlayerPrefs:
    // div1_won / pl_won / cc_won). Tapping an unlocked card starts the match (SampleScene for now — all
    // competitions share the one scene until the per-competition pools + simulation are built).
    GameObject BuildGameModeOverlay()
    {
        GameObject ov = new GameObject("Overlay_GAMEMODE");
        ov.transform.SetParent(canvasRoot, false);
        RectTransform ort = ov.AddComponent<RectTransform>();
        Stretch(ort);
        Image backdrop = ov.AddComponent<Image>();
        backdrop.color = OverlayDark;
        backdrop.raycastTarget = true;
        ov.AddComponent<CanvasGroup>();

        // Full-canvas sheet — slid in from the right by SlideOverlay, which finds the "Sheet" child.
        GameObject sheetGo = new GameObject("Sheet");
        sheetGo.transform.SetParent(ov.transform, false);
        RectTransform srt = sheetGo.AddComponent<RectTransform>();
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.pivot = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = Vector2.zero;
        srt.anchoredPosition = Vector2.zero;

        // Dark-blue background (#0A1628); also swallows clicks that miss the cards. Sits behind the
        // pool render below and shows through as a fallback if the art fails to load.
        Image bg = sheetGo.AddComponent<Image>();
        bg.color = GameModeBg;
        bg.raycastTarget = true;

        // Animated pool-screen backdrop (Ken-Burns drift + breathing vignette + drifting specks).
        BuildGameModeBackground(sheetGo.transform);

        // ---- top bar (80px): back arrow | "GAME MODE" | diamond + gold currencies ----
        Image bar = MakePanel(sheetGo.transform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 80f), DarkBar);
        bar.gameObject.name = "GMTopBar";
        bar.raycastTarget = true;
        RectTransform brt = bar.rectTransform;
        brt.anchorMin = new Vector2(0f, 1f);
        brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(0f, 80f);

        // Back arrow → close (universal back-button sprite).
        MakeBackButton(bar.transform, () => HideOverlay(gameModeOverlay));

        // Title.
        MakeText(bar.transform, "GAME MODE", 36f, new Vector2(0.5f, 0.5f), Vector2.zero,
                 new Vector2(420f, 50f), Color.white, TextAlignmentOptions.Center);

        // Currencies, right side, right-to-left: gold count, gold icon, diamond count, diamond icon.
        gmGoldLabel = MakeText(bar.transform, "0", 18f, new Vector2(1f, 0.5f), new Vector2(-40f, 0f),
                               new Vector2(66f, 30f), Color.white, TextAlignmentOptions.Right);
        MakeIcon(bar.transform, "Sprites/gold-coin", new Vector2(1f, 0.5f), new Vector2(-95f, 0f), 34f);
        gmDiamondLabel = MakeText(bar.transform, "0", 18f, new Vector2(1f, 0.5f), new Vector2(-150f, 0f),
                                  new Vector2(54f, 30f), Color.white, TextAlignmentOptions.Right);
        MakeIcon(bar.transform, "Sprites/diamond-coin", new Vector2(1f, 0.5f), new Vector2(-200f, 0f), 34f);

        // ---- card row: 4 cards, 30px side margins, equal gaps, centred in the area below the bar ----
        const float margin = 30f, cardH = 480f, gap = 24f, cardY = -40f; // cardY drops the row into the 640px main area
        float rowW = 1280f - 2f * margin;     // 1220 usable width
        float cardW = (rowW - 3f * gap) / 4f; // ~287 each (320 spec width can't fit 4 + margins at 1280)
        gmCardRects.Clear(); gmCardGroups.Clear(); gmCardBasePos.Clear(); gmCardSelected.Clear();
        for (int i = 0; i < 4; i++)
        {
            float cx = -rowW * 0.5f + cardW * 0.5f + i * (cardW + gap);
            BuildGameModeCard(sheetGo.transform, i, new Vector2(cx, cardY), new Vector2(cardW, cardH));
        }

        ov.SetActive(false);
        return ov;
    }

    // Builds the three background layers behind the cards and wires them to GameModeBackgroundFX,
    // which animates them only while the overlay is open. Called before the top bar and cards so
    // everything here renders behind them. Draw-call budget: backdrop + vignette + specks ≈ 3.
    void BuildGameModeBackground(Transform sheet)
    {
        // Competition backdrop — oversized (stretch + 90/70px margin) so the slow pan/zoom never
        // exposes an edge. The animation (Ken-Burns drift) plays over whatever image is here.
        Image backdrop = NewImage(sheet, "GMBackdrop");
        backdrop.sprite = CompetitionBgSprite();
        backdrop.raycastTarget = false;
        backdrop.preserveAspect = false;                 // fill the oversized rect on any aspect
        backdrop.color = backdrop.sprite != null
            ? new Color(0.82f, 0.85f, 0.92f, 1f)         // slight dim so the cards stay the focal point
            : GameModeBg;                                // solid fallback if the art is missing
        RectTransform prt = backdrop.rectTransform;
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.offsetMin = new Vector2(-90f, -70f);
        prt.offsetMax = new Vector2(90f, 70f);

        // Soft radial vignette that gently breathes (alpha pulsed by the FX component).
        Image vig = NewImage(sheet, "GMVignette");
        vig.sprite = Vignette();
        vig.raycastTarget = false;
        vig.color = new Color(0f, 0f, 0f, 0.35f);
        Stretch(vig.rectTransform);

        // Empty full-screen container the specks live under (kept behind the cards).
        GameObject specksGo = new GameObject("GMSpecks");
        specksGo.transform.SetParent(sheet, false);
        Stretch(specksGo.AddComponent<RectTransform>());

        GameModeBackgroundFX fx = sheet.gameObject.GetComponent<GameModeBackgroundFX>();
        if (fx == null) fx = sheet.gameObject.AddComponent<GameModeBackgroundFX>();
        fx.Init(prt, vig, specksGo.transform, Circle(), 14, 1280f, 720f);
    }

    // One competition card. Rounded (via Mask) art fill + bottom-gradient name. Locked cards get a
    // dark veil + the lock-sign badge + "WIN … TO UNLOCK" and shake on tap; unlocked cards get a gold
    // frame and open the league standings on tap. Interaction/animation lives in GameModeCardFX.
    void BuildGameModeCard(Transform parent, int index, Vector2 pos, Vector2 size)
    {
        string[] names = { "DIVISION 1", "PREMIER LEAGUE", "CONTINENTAL CUP", "WORLD CHAMPIONS LEAGUE" };
        string[] sprites = { "Sprites/division1-card", "Sprites/premier-league-card",
                             "Sprites/continental-cup-card", "Sprites/world-champions-league-card" };
        Color[] tierColors = { new Color(0.180f, 0.800f, 0.251f),   // #2ECC40 green
                               new Color(0.608f, 0.349f, 0.714f),   // #9B59B6 purple
                               new Color(0.161f, 0.502f, 0.725f),   // #2980B9 blue
                               new Color(0.953f, 0.612f, 0.071f) }; // #F39C12 gold
        string[] lockText = { "", "WIN DIVISION 1 TO UNLOCK", "WIN PREMIER LEAGUE TO UNLOCK",
                              "WIN CONTINENTAL CUP TO UNLOCK" };

        bool unlocked = IsCompetitionUnlocked(index);
        bool selected = unlocked && index == 0; // Division 1 is the default-highlighted card
        float w = size.x;

        // Card root — the outer rounded rect shows through as the gold frame on unlocked cards.
        GameObject cardGo = new GameObject("Card_" + index);
        cardGo.transform.SetParent(parent, false);
        RectTransform cardRt = cardGo.AddComponent<RectTransform>();
        SetRect(cardRt, new Vector2(0.5f, 0.5f), pos, size);
        CanvasGroup cardCg = cardGo.AddComponent<CanvasGroup>(); // drives the staggered fade-in on open
        Image frame = cardGo.AddComponent<Image>();
        frame.sprite = GetRoundedSprite();
        frame.type = Image.Type.Sliced;
        frame.color = unlocked ? CardGold : new Color(0.08f, 0.12f, 0.2f, 1f);
        frame.raycastTarget = false;

        // Inner masked container rounds the art + every overlay to radius 20.
        float border = unlocked ? 3f : 0f; // gold frame thickness shows around the inner card
        GameObject innerGo = new GameObject("Inner");
        innerGo.transform.SetParent(cardGo.transform, false);
        RectTransform innerRt = innerGo.AddComponent<RectTransform>();
        innerRt.anchorMin = Vector2.zero;
        innerRt.anchorMax = Vector2.one;
        innerRt.offsetMin = new Vector2(border, border);
        innerRt.offsetMax = new Vector2(-border, -border);
        Image innerImg = innerGo.AddComponent<Image>();
        innerImg.sprite = GetRoundedSprite();
        innerImg.type = Image.Type.Sliced;
        innerImg.color = new Color(0.05f, 0.09f, 0.16f, 1f); // card fallback bg (shows if art is missing)
        Mask mask = innerGo.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        // Art — fills the card; the mask supplies the rounded corners.
        Image art = NewImage(innerGo.transform, "Art");
        art.sprite = LoadSprite(sprites[index]);
        art.preserveAspect = false;
        art.raycastTarget = false;
        if (art.sprite == null) art.color = tierColors[index];
        Stretch(art.rectTransform);

        // Bottom gradient for name legibility (black, fades up).
        Image grad = NewImage(innerGo.transform, "Gradient");
        grad.sprite = BottomGradient();
        grad.color = new Color(0f, 0f, 0f, 0.85f);
        grad.raycastTarget = false;
        RectTransform gradRt = grad.rectTransform;
        gradRt.anchorMin = new Vector2(0f, 0f);
        gradRt.anchorMax = new Vector2(1f, 0f);
        gradRt.pivot = new Vector2(0.5f, 0f);
        gradRt.anchoredPosition = Vector2.zero;
        gradRt.sizeDelta = new Vector2(0f, 120f);

        // Competition name (bottom-centre, wraps for long names).
        MakeText(innerGo.transform, names[index], 22f, new Vector2(0.5f, 0f), new Vector2(0f, 30f),
                 new Vector2(w - 16f, 60f), Color.white, TextAlignmentOptions.Center);

        if (!unlocked)
        {
            // Dark veil over the whole card.
            Image veil = NewImage(innerGo.transform, "LockVeil");
            veil.sprite = GetRoundedSprite();
            veil.type = Image.Type.Sliced;
            veil.color = new Color(0f, 0f, 0f, 0.7f);
            veil.raycastTarget = true; // locked → swallow taps (GameModeCardFX turns them into a bounce)
            Stretch(veil.rectTransform);

            // The user's glossy red lock-sign, cropped to its content and shown at native (square) aspect.
            Image lockImg = NewImage(veil.transform, "LockSign");
            lockImg.sprite = LockSignSprite();
            lockImg.preserveAspect = true;
            lockImg.raycastTarget = false;
            lockImg.color = new Color(0.88f, 0.88f, 0.92f, 1f); // slightly dimmed; the tap flash brightens it
            if (lockImg.sprite == null) { lockImg.sprite = MakeLockSprite(); lockImg.color = Color.white; }
            SetRect(lockImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(84f, 84f));

            // Unlock instruction below the lock.
            TextMeshProUGUI unlockLabel = MakeText(veil.transform, lockText[index], 14f,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -34f), new Vector2(w - 24f, 44f),
                new Color(1f, 1f, 1f, 0.92f), TextAlignmentOptions.Center);

            cardGo.AddComponent<GameModeCardFX>().InitLocked(lockImg, unlockLabel);
        }
        else
        {
            // Transparent full-card hit target on top → fully tappable; the whole card scales on hover.
            Image hit = NewImage(cardGo.transform, "Hit");
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            Stretch(hit.rectTransform);
            cardGo.AddComponent<GameModeCardFX>().InitUnlocked(frame, CardGold, selected,
                () => OpenStandings(index)); // open the league standings instead of the match directly
        }

        gmCardRects.Add(cardRt);
        gmCardGroups.Add(cardCg);
        gmCardBasePos.Add(pos);
        gmCardSelected.Add(selected);
    }

    // Unlock gates: Division 1 is always open; each higher tier needs the previous competition won.
    static bool IsCompetitionUnlocked(int index)
    {
        switch (index)
        {
            case 0: return true;                                       // Division 1
            case 1: return PlayerPrefs.GetInt("div1_won", 0) == 1;     // Premier League
            case 2: return PlayerPrefs.GetInt("pl_won", 0) == 1;       // Continental Cup
            case 3: return PlayerPrefs.GetInt("cc_won", 0) == 1;       // World Champions League
            default: return false;
        }
    }

    // Player won a division's Final → persist the unlock for the next tier (read back by
    // IsCompetitionUnlocked; the Game Mode cards pick it up next time the hub loads).
    static void MarkCompetitionWon(int competitionIndex)
    {
        string[] keys = { "div1_won", "pl_won", "cc_won", "wcl_won" };
        if (competitionIndex < 0 || competitionIndex >= keys.Length) return;
        PlayerPrefs.SetInt(keys[competitionIndex], 1);
        PlayerPrefs.Save();
    }

    // =============================================================== competition screen

    // Flow: Game Mode card → Competition screen (GROUP STAGE / KNOCKOUT tabs) → NEXT MATCH →
    // Pre-Match → PLAY → SampleScene. The competition and pre-match overlays are built lazily and
    // their content is rebuilt on every open (the tournament moves on between visits), then slid in
    // via the shared SlideOverlay. Tab switches and group expand/collapse taps rebuild the sheet in
    // place (no slide).

    public void OpenStandings(int competitionIndex)
    {
        LeagueSeason.Ensure(competitionIndex, PlayerTeamName());
        // Fresh view state each open: group tab during the group stage, knockout once it starts.
        compTab = LeagueSeason.Current.phase == LeagueSeason.Phase.GroupStage ? 0 : 1;
        compGroupExpanded[0] = compGroupExpanded[1] = false;
        if (standingsOverlay == null) standingsOverlay = BuildScreenOverlay("Overlay_STANDINGS");
        RectTransform sheet = standingsOverlay.transform.Find("Sheet") as RectTransform;
        ClearChildren(sheet);
        BuildStandingsContent(sheet);
        ShowOverlay(standingsOverlay);
    }

    // Rebuild the competition sheet in place — used by the tabs and the group expand/collapse taps
    // while the overlay is already open.
    void RebuildStandings()
    {
        if (standingsOverlay == null) return;
        RectTransform sheet = standingsOverlay.transform.Find("Sheet") as RectTransform;
        ClearChildren(sheet);
        BuildStandingsContent(sheet);
    }

    void OpenPreMatch()
    {
        if (LeagueSeason.Current == null || LeagueSeason.Current.IsComplete) return;
        if (preMatchOverlay == null) preMatchOverlay = BuildScreenOverlay("Overlay_PREMATCH");
        RectTransform sheet = preMatchOverlay.transform.Find("Sheet") as RectTransform;
        ClearChildren(sheet);
        BuildPreMatchContent(sheet);
        ShowOverlay(preMatchOverlay);
    }

    // Empty slide-in overlay shell (dark backdrop + CanvasGroup + full-canvas "Sheet"). Content is
    // added into the Sheet by the callers and cleared/rebuilt on each open.
    GameObject BuildScreenOverlay(string name)
    {
        GameObject ov = new GameObject(name);
        ov.transform.SetParent(canvasRoot, false);
        Stretch(ov.AddComponent<RectTransform>());
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
        srt.sizeDelta = Vector2.zero;
        srt.anchoredPosition = Vector2.zero;

        ov.SetActive(false);
        return ov;
    }

    void BuildStandingsContent(Transform sheet)
    {
        LeagueSeason s = LeagueSeason.Current;
        if (s == null) return;
        int comp = Mathf.Clamp(s.competitionIndex, 0, CompNames.Length - 1);

        AddScreenBackground(sheet, 0.85f);
        MakeTopBar(sheet, CompNames[comp], () => HideOverlay(standingsOverlay));
        BuildCompTabs(sheet, s);

        if (compTab == 0) BuildGroupStageTab(sheet, s);
        else BuildKnockoutTab(sheet, s);

        BuildCompBottomBar(sheet, s, comp);
    }

    // ---- tabs ----

    void BuildCompTabs(Transform sheet, LeagueSeason s)
    {
        bool koOpen = s.phase != LeagueSeason.Phase.GroupStage; // knockout tab locked until the groups end
        MakeCompTab(sheet, "GROUP STAGE", new Vector2(-148f, -104f), compTab == 0, true,
            () => { if (compTab != 0) { compTab = 0; RebuildStandings(); } });
        MakeCompTab(sheet, "KNOCKOUT", new Vector2(148f, -104f), compTab == 1, koOpen,
            () => { if (koOpen && compTab != 1) { compTab = 1; RebuildStandings(); } });
    }

    void MakeCompTab(Transform sheet, string label, Vector2 pos, bool selected, bool unlocked,
                     UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Tab_" + label);
        go.transform.SetParent(sheet, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, new Vector2(0.5f, 1f), pos, new Vector2(280f, 44f));

        Image img = go.AddComponent<Image>();
        img.sprite = GetRoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = selected ? new Color(0.13f, 0.24f, 0.36f, 0.98f) : new Color(0.05f, 0.09f, 0.15f, 0.9f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        if (unlocked) AddHover(go);

        Color txt = !unlocked ? new Color(1f, 1f, 1f, 0.35f)
                  : selected ? Color.white : new Color(0.7f, 0.78f, 0.88f, 1f);
        MakeText(go.transform, label, 19f, new Vector2(0.5f, 0.5f), Vector2.zero,
                 new Vector2(276f, 44f), txt, TextAlignmentOptions.Center);

        if (selected) // gold underline marks the active tab (plain quad — no rounded slicing at 4px)
        {
            Image u = NewImage(go.transform, "Underline");
            u.color = Gold;
            u.raycastTarget = false;
            SetRect(u.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(200f, 4f));
        }
    }

    // Vertical ScrollRect filling the area between the tabs and the bottom bar. Returns the content
    // RectTransform; callers lay children out top-down and must set content.sizeDelta.y to the total.
    RectTransform MakeCompScroll(Transform sheet)
    {
        const float top = 134f, bottom = 98f, width = 1180f;
        GameObject go = new GameObject("Scroll");
        go.transform.SetParent(sheet, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, -(top + bottom));
        rt.anchoredPosition = new Vector2(0f, (bottom - top) * 0.5f);

        Image img = go.AddComponent<Image>(); // invisible drag-catcher for the whole viewport
        img.color = new Color(0f, 0f, 0f, 0f);
        go.AddComponent<RectMask2D>();

        GameObject contentGo = new GameObject("Content");
        contentGo.transform.SetParent(go.transform, false);
        RectTransform content = contentGo.AddComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 10f);

        ScrollRect scroll = go.AddComponent<ScrollRect>();
        scroll.viewport = rt;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        return content;
    }

    // ---- GROUP STAGE tab ----

    void BuildGroupStageTab(Transform sheet, LeagueSeason s)
    {
        RectTransform content = MakeCompScroll(sheet);
        float y = 4f;
        for (int g = 0; g < 2; g++)
            y += BuildGroupCard(content, s, g, y) + 16f;
        content.sizeDelta = new Vector2(0f, y);
    }

    // One framed group table. Collapsed: top 5 in Pos | Team | Pts. Expanded: all 8 with full
    // columns. Tapping anywhere on the card toggles (ScrollRect drags don't trigger the click).
    // Returns the card height so the caller can stack the next one below.
    float BuildGroupCard(RectTransform content, LeagueSeason s, int g, float yTop)
    {
        bool expanded = compGroupExpanded[g];
        const float width = 1150f, headerH = 46f;
        float rowH = expanded ? 34f : 30f;
        float colHeadH = expanded ? 28f : 0f;
        int rows = expanded ? LeagueSeason.GroupSize : 5;
        float h = headerH + colHeadH + rows * rowH + 12f;

        // Outer border + inset fill — same two-image framing as the hub card slots.
        GameObject cardGo = new GameObject(g == 0 ? "GroupA" : "GroupB");
        cardGo.transform.SetParent(content, false);
        RectTransform rt = cardGo.AddComponent<RectTransform>();
        SetRect(rt, new Vector2(0.5f, 1f), new Vector2(0f, -(yTop + h * 0.5f)), new Vector2(width, h));
        Image frame = cardGo.AddComponent<Image>();
        frame.sprite = GetRoundedSprite();
        frame.type = Image.Type.Sliced;
        frame.color = new Color(0.227f, 0.353f, 0.478f, 1f); // #3A5A7A border

        Image fill = NewImage(cardGo.transform, "Fill");
        fill.sprite = GetRoundedSprite();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.102f, 0.165f, 0.227f, 0.96f); // #1A2A3A body
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(2f, 2f);
        frt.offsetMax = new Vector2(-2f, -2f);

        Button btn = cardGo.AddComponent<Button>();
        btn.targetGraphic = frame;
        int gi = g;
        btn.onClick.AddListener(() => { compGroupExpanded[gi] = !compGroupExpanded[gi]; RebuildStandings(); });

        // Header bar: group name left, expand/collapse hint right.
        Image head = NewImage(cardGo.transform, "Header");
        head.sprite = GetRoundedSprite();
        head.type = Image.Type.Sliced;
        head.color = new Color(0.13f, 0.24f, 0.36f, 1f);
        head.raycastTarget = false;
        SetRect(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -(headerH * 0.5f + 2f)),
                new Vector2(width - 8f, headerH - 4f));
        MakeText(head.transform, g == 0 ? "GROUP A" : "GROUP B", 20f, new Vector2(0f, 0.5f),
                 new Vector2(160f, 0f), new Vector2(300f, 30f), Cyan, TextAlignmentOptions.Left);
        MakeText(head.transform, expanded ? "COLLAPSE" : "VIEW ALL", 15f, new Vector2(1f, 0.5f),
                 new Vector2(-95f, 0f), new Vector2(170f, 24f), Gold, TextAlignmentOptions.Right);

        List<int> order = s.GroupStandings(g);
        if (expanded)
            MakeGroupFullRow(cardGo.transform, -(headerH + colHeadH * 0.5f), rowH, true, false,
                             "POS", "TEAM", "P", "W", "D", "L", "GD", "PTS");
        for (int r = 0; r < rows; r++)
        {
            int ti = order[r];
            bool player = ti == LeagueSeason.PlayerIndex;
            float cy = -(headerH + colHeadH + rowH * 0.5f + r * rowH);
            if (expanded)
                MakeGroupFullRow(cardGo.transform, cy, rowH, false, player,
                    (r + 1).ToString(), s.teams[ti], s.played[ti].ToString(), s.won[ti].ToString(),
                    s.drawn[ti].ToString(), s.lost[ti].ToString(), Signed(s.GoalDiff(ti)),
                    s.Points(ti).ToString());
            else
                MakeGroupCompactRow(cardGo.transform, cy, rowH, player,
                                    (r + 1).ToString(), s.teams[ti], s.Points(ti).ToString());
        }
        return h;
    }

    static Color GroupRowColor(bool header, bool player) =>
        header ? new Color(0.10f, 0.16f, 0.28f, 0.98f)
      : player ? new Color(0.90f, 0.72f, 0.14f, 0.60f)   // gold highlight = the player's club
               : new Color(0.06f, 0.10f, 0.18f, 0.55f);

    Image MakeGroupRowStrip(Transform card, float centerY, float rowH, bool header, bool player)
    {
        Image row = NewImage(card, header ? "ColHeader" : "Row");
        row.sprite = GetRoundedSprite();
        row.type = Image.Type.Sliced;
        row.color = GroupRowColor(header, player);
        row.raycastTarget = false;
        SetRect(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, centerY),
                new Vector2(1120f, rowH - 4f));
        return row;
    }

    // Full 8-column row for an expanded group (also draws the column header when `header`).
    void MakeGroupFullRow(Transform card, float centerY, float rowH, bool header, bool player,
                          string pos, string name, string p, string w, string d, string l,
                          string gd, string pts)
    {
        Image row = MakeGroupRowStrip(card, centerY, rowH, header, player);
        Color col = header ? new Color(0.72f, 0.85f, 1f, 1f) : Color.white;
        float fs = header ? 14f : 17f;
        Vector2 box = new Vector2(60f, rowH);
        MakeText(row.transform, pos, fs, new Vector2(0.5f, 0.5f), new Vector2(-500f, 0f), box, col, TextAlignmentOptions.Center);
        MakeText(row.transform, name, fs, new Vector2(0.5f, 0.5f), new Vector2(-290f, 0f), new Vector2(330f, rowH), col, TextAlignmentOptions.Left);
        MakeText(row.transform, p, fs, new Vector2(0.5f, 0.5f), new Vector2(-55f, 0f), box, col, TextAlignmentOptions.Center);
        MakeText(row.transform, w, fs, new Vector2(0.5f, 0.5f), new Vector2(25f, 0f), box, col, TextAlignmentOptions.Center);
        MakeText(row.transform, d, fs, new Vector2(0.5f, 0.5f), new Vector2(105f, 0f), box, col, TextAlignmentOptions.Center);
        MakeText(row.transform, l, fs, new Vector2(0.5f, 0.5f), new Vector2(185f, 0f), box, col, TextAlignmentOptions.Center);
        MakeText(row.transform, gd, fs, new Vector2(0.5f, 0.5f), new Vector2(285f, 0f), new Vector2(90f, rowH), col, TextAlignmentOptions.Center);
        MakeText(row.transform, pts, fs, new Vector2(0.5f, 0.5f), new Vector2(415f, 0f), new Vector2(100f, rowH), col, TextAlignmentOptions.Center);
    }

    // Compact 3-column row (Pos | Team | Pts) for a collapsed group.
    void MakeGroupCompactRow(Transform card, float centerY, float rowH, bool player,
                             string pos, string name, string pts)
    {
        Image row = MakeGroupRowStrip(card, centerY, rowH, false, player);
        Vector2 box = new Vector2(60f, rowH);
        MakeText(row.transform, pos, 16f, new Vector2(0.5f, 0.5f), new Vector2(-500f, 0f), box, Color.white, TextAlignmentOptions.Center);
        MakeText(row.transform, name, 16f, new Vector2(0.5f, 0.5f), new Vector2(-230f, 0f), new Vector2(450f, rowH), Color.white, TextAlignmentOptions.Left);
        MakeText(row.transform, pts, 16f, new Vector2(0.5f, 0.5f), new Vector2(485f, 0f), box, Color.white, TextAlignmentOptions.Center);
    }

    // ---- KNOCKOUT tab ----

    void BuildKnockoutTab(Transform sheet, LeagueSeason s)
    {
        RectTransform content = MakeCompScroll(sheet);
        float y = 2f;

        y = AddBracketLabel(content, "QUARTERFINALS", y);
        for (int i = 0; i < 4; i++)
        {
            float cx = (i % 2 == 0) ? -297f : 297f;                 // 2x2 grid
            float cy = y + 38f + (i / 2) * 84f;
            BuildBracketCard(content, s, s.quarterfinals[i], new Vector2(cx, -cy), new Vector2(576f, 72f));
        }
        y += 36f + 84f + 36f + 10f;

        y = AddBracketLabel(content, "SEMIFINALS", y);
        for (int i = 0; i < 2; i++)
            BuildBracketCard(content, s, s.semifinals[i], new Vector2(i == 0 ? -297f : 297f, -(y + 36f)),
                             new Vector2(576f, 72f));
        y += 82f;

        y = AddBracketLabel(content, "FINAL", y);
        BuildBracketCard(content, s, s.Final, new Vector2(0f, -(y + 40f)), new Vector2(640f, 80f));
        y += 90f;

        content.sizeDelta = new Vector2(0f, y);
    }

    float AddBracketLabel(RectTransform content, string label, float y)
    {
        MakeText(content, label, 20f, new Vector2(0.5f, 1f), new Vector2(0f, -(y + 16f)),
                 new Vector2(400f, 28f), Gold, TextAlignmentOptions.Center);
        return y + 34f;
    }

    // One bracket match card: Team A | score - score | Team B ("vs" while unplayed, "TBD" for
    // undecided slots). The player's tie gets a gold frame; a decided tie dims the loser.
    void BuildBracketCard(RectTransform content, LeagueSeason s, LeagueSeason.KnockoutMatch m,
                          Vector2 center, Vector2 size)
    {
        bool mine = m.Has(LeagueSeason.PlayerIndex);
        GameObject go = new GameObject("Bracket");
        go.transform.SetParent(content, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, new Vector2(0.5f, 1f), center, size);
        Image frame = go.AddComponent<Image>();
        frame.sprite = GetRoundedSprite();
        frame.type = Image.Type.Sliced;
        frame.color = mine ? CardGold : new Color(0.227f, 0.353f, 0.478f, 1f);
        frame.raycastTarget = false;

        Image fill = NewImage(go.transform, "Fill");
        fill.sprite = GetRoundedSprite();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.08f, 0.13f, 0.18f, 0.97f);
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(2f, 2f);
        frt.offsetMax = new Vector2(-2f, -2f);

        Color tbd = new Color(0.5f, 0.56f, 0.64f, 1f);
        Color dim = new Color(0.55f, 0.6f, 0.68f, 1f);
        string nameA = m.teamA >= 0 ? s.teams[m.teamA] : "TBD";
        string nameB = m.teamB >= 0 ? s.teams[m.teamB] : "TBD";
        Color colA = m.teamA < 0 ? tbd : !m.played ? Color.white : m.Winner == m.teamA ? Color.white : dim;
        Color colB = m.teamB < 0 ? tbd : !m.played ? Color.white : m.Winner == m.teamB ? Color.white : dim;

        float wing = size.x * 0.5f - 78f; // name box width each side, leaving the middle for the score
        MakeText(fill.transform, nameA, 17f, new Vector2(0f, 0.5f), new Vector2(wing * 0.5f + 14f, 0f),
                 new Vector2(wing, size.y), colA, TextAlignmentOptions.Left);
        MakeText(fill.transform, nameB, 17f, new Vector2(1f, 0.5f), new Vector2(-(wing * 0.5f + 14f), 0f),
                 new Vector2(wing, size.y), colB, TextAlignmentOptions.Right);
        string mid = m.played ? m.scoreA + " - " + m.scoreB : "vs";
        MakeText(fill.transform, mid, m.played ? 21f : 17f, new Vector2(0.5f, 0.5f), Vector2.zero,
                 new Vector2(130f, size.y), m.played ? Color.white : dim, TextAlignmentOptions.Center);
    }

    // ---- bottom bar (always visible): NEXT MATCH / result state + TEAM shortcut ----

    void BuildCompBottomBar(Transform sheet, LeagueSeason s, int comp)
    {
        Image bar = MakePanel(sheet, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 92f), DarkBar);
        bar.gameObject.name = "CompBottomBar";
        bar.raycastTarget = true;
        RectTransform brt = bar.rectTransform;
        brt.anchorMin = new Vector2(0f, 0f);
        brt.anchorMax = new Vector2(1f, 0f);
        brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(0f, 92f);

        // TEAM shortcut — back from the team screen returns here, not to the hub.
        MakeImageButton(bar.transform, "BtnTeam", "Sprites/team-button", new Vector2(1f, 0.5f),
                        new Vector2(-70f, 0f), new Vector2(78f, 78f), () => OpenTeamScreen("COMPETITION"));

        if (!s.IsComplete)
        {
            MakeActionButton(bar.transform, "NEXT MATCH    vs. " + s.NextOpponentName,
                             new Vector2(0.5f, 0.5f), new Vector2(-40f, 0f), new Vector2(560f, 64f),
                             Green, OpenPreMatch);
        }
        else if (s.PlayerIsChampion)
        {
            MakeActionButton(bar.transform, "CHAMPIONS!  CLAIM REWARDS", new Vector2(0.5f, 0.5f),
                             new Vector2(-40f, 0f), new Vector2(560f, 64f), Gold,
                () => Debug.Log("CLAIM REWARDS (placeholder) — " + CompNames[comp] + " won."));
        }
        else
        {
            string champ = s.Champion >= 0 ? s.teams[s.Champion] : "?";
            MakeText(bar.transform, "ELIMINATED IN THE " + s.eliminatedIn + "  -  " + champ + " ARE CHAMPIONS",
                     19f, new Vector2(0.5f, 0.5f), new Vector2(-40f, 0f), new Vector2(900f, 60f),
                     new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.Center);
        }
    }

    // =============================================================== pre-match

    void BuildPreMatchContent(Transform sheet)
    {
        LeagueSeason s = LeagueSeason.Current;
        if (s == null) return;
        int comp = Mathf.Clamp(s.competitionIndex, 0, CompNames.Length - 1);

        AddScreenBackground(sheet, 0.55f); // dimmed
        MakeTopBar(sheet, CompNames[comp], () => HideOverlay(preMatchOverlay));

        int opp = s.NextOpponent;
        string playerName = s.teams[LeagueSeason.PlayerIndex];
        string oppName = opp >= 0 ? s.teams[opp] : "TBD";
        int oppStars = opp >= 0 ? s.stars[opp] : 3;

        const float poolW = 470f, poolH = 264f, poolY = 34f, poolX = 322f;
        BuildPreMatchPool(sheet, new Vector2(-poolX, poolY), new Vector2(poolW, poolH), true, Blue);
        BuildPreMatchPool(sheet, new Vector2(poolX, poolY), new Vector2(poolW, poolH), false, Red);

        // Center column: league badge (text), phase/match label, VS, PLAY.
        MakeText(sheet, CompNames[comp], 20f, new Vector2(0.5f, 0.5f), new Vector2(0f, 196f),
                 new Vector2(200f, 48f), Gold, TextAlignmentOptions.Center);
        MakeText(sheet, s.MatchLabel, 18f,
                 new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(300f, 26f), Color.white,
                 TextAlignmentOptions.Center);
        MakeText(sheet, "VS", 44f, new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(160f, 56f),
                 new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.Center);
        MakeActionButton(sheet, "PLAY", new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(180f, 72f),
            Green, () =>
            {
                // Placeholder result until real match reporting is wired: simulate the player's score,
                // then load the match. The tournament reflects it next time the screen is opened.
                s.RecordPlayerResult(Random.Range(0, 13), Random.Range(0, 13));
                if (s.PlayerIsChampion) MarkCompetitionWon(s.competitionIndex);
                SceneManager.LoadScene("SampleScene");
            });

        // Below each pool: logo + name + star rating.
        BuildTeamInfo(sheet, new Vector2(-poolX, -150f), playerName, s.stars[LeagueSeason.PlayerIndex], Blue);
        BuildTeamInfo(sheet, new Vector2(poolX, -150f), oppName, oppStars, Red);
    }

    // A pool render with a coloured frame and 6 formation markers. Opponent formations mirror vertically.
    void BuildPreMatchPool(Transform sheet, Vector2 center, Vector2 size, bool isPlayer, Color color)
    {
        Image frame = NewImage(sheet, isPlayer ? "PlayerPoolFrame" : "OpponentPoolFrame");
        frame.sprite = GetRoundedSprite();
        frame.type = Image.Type.Sliced;
        frame.color = new Color(color.r, color.g, color.b, 0.95f);
        frame.raycastTarget = false;
        SetRect(frame.rectTransform, new Vector2(0.5f, 0.5f), center, size + new Vector2(8f, 8f));

        Image pool = NewImage(sheet, isPlayer ? "PlayerPool" : "OpponentPool"); // drawn after → on top of frame
        pool.sprite = PoolScreenSprite();
        pool.preserveAspect = false;
        pool.raycastTarget = false;
        pool.color = pool.sprite != null ? Color.white : new Color(0.10f, 0.35f, 0.60f, 1f);
        SetRect(pool.rectTransform, new Vector2(0.5f, 0.5f), center, size);

        BuildFormationMarkers(pool.transform, size, isPlayer);
    }

    void BuildFormationMarkers(Transform pool, Vector2 size, bool isPlayer)
    {
        // Labels mirror TeamSide's field roles (+ a GK marker); positions are fractions of the pool rect.
        (string label, float fx, float fy)[] form =
        {
            ("GK", 0f, -0.40f), ("CB", 0f, -0.16f),
            ("LW", -0.30f, 0.02f), ("RW", 0.30f, 0.02f),
            ("LF", -0.16f, 0.24f), ("RF", 0.16f, 0.24f)
        };
        foreach (var f in form)
        {
            float my = isPlayer ? f.fy : -f.fy; // opponent attacks the other way → mirror
            Image m = NewImage(pool, "Pos_" + f.label);
            m.sprite = GetRoundedSprite();
            m.type = Image.Type.Sliced;
            m.color = new Color(0.96f, 0.97f, 1f, 0.95f);
            m.raycastTarget = false;
            SetRect(m.rectTransform, new Vector2(0.5f, 0.5f),
                    new Vector2(f.fx * size.x, my * size.y), new Vector2(42f, 50f));
            MakeText(m.transform, f.label, 15f, new Vector2(0.5f, 0.5f), Vector2.zero,
                     new Vector2(42f, 50f), new Color(0.06f, 0.10f, 0.18f, 1f), TextAlignmentOptions.Center);
        }
    }

    void BuildTeamInfo(Transform sheet, Vector2 center, string name, int stars, Color color)
    {
        Image logo = NewImage(sheet, "TeamLogo");
        logo.sprite = Circle();
        logo.color = color;
        logo.raycastTarget = false;
        SetRect(logo.rectTransform, new Vector2(0.5f, 0.5f), center + new Vector2(0f, 28f), new Vector2(54f, 54f));
        MakeText(sheet, name, 20f, new Vector2(0.5f, 0.5f), center + new Vector2(0f, -12f),
                 new Vector2(320f, 28f), Color.white, TextAlignmentOptions.Center);
        MakeText(sheet, StarString(stars), 22f, new Vector2(0.5f, 0.5f), center + new Vector2(0f, -42f),
                 new Vector2(180f, 26f), Gold, TextAlignmentOptions.Center);
    }

    // ---- shared screen helpers (top bar / back button / currency / backdrop / buttons) ----

    // Full-width 80px dark top bar with a universal back button, centred title, and currency readout.
    Image MakeTopBar(Transform sheet, string title, UnityEngine.Events.UnityAction onBack)
    {
        Image bar = MakePanel(sheet, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 80f), DarkBar);
        bar.gameObject.name = "TopBar";
        bar.raycastTarget = true;
        RectTransform brt = bar.rectTransform;
        brt.anchorMin = new Vector2(0f, 1f);
        brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(0f, 80f);

        MakeBackButton(bar.transform, onBack);
        MakeText(bar.transform, title, 34f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 50f),
                 Color.white, TextAlignmentOptions.Center);
        AddCurrencyDisplay(bar.transform);
        return bar;
    }

    void AddCurrencyDisplay(Transform bar)
    {
        RosterManager rm = RosterManager.Instance;
        int coins = rm != null ? rm.Coins : 0;
        int diamonds = rm != null ? rm.Diamonds : 0;
        MakeText(bar, coins.ToString(), 18f, new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(66f, 30f),
                 Color.white, TextAlignmentOptions.Right);
        MakeIcon(bar, "Sprites/gold-coin", new Vector2(1f, 0.5f), new Vector2(-95f, 0f), 34f);
        MakeText(bar, diamonds.ToString(), 18f, new Vector2(1f, 0.5f), new Vector2(-150f, 0f), new Vector2(54f, 30f),
                 Color.white, TextAlignmentOptions.Right);
        MakeIcon(bar, "Sprites/diamond-coin", new Vector2(1f, 0.5f), new Vector2(-200f, 0f), 34f);
    }

    // Solid dark base (swallows clicks) + competition-page-background dimmed by `brightness`.
    void AddScreenBackground(Transform sheet, float brightness)
    {
        Image baseImg = NewImage(sheet, "BaseBG");
        baseImg.color = GameModeBg;
        baseImg.raycastTarget = true;
        Stretch(baseImg.rectTransform);

        Image bg = NewImage(sheet, "CompetitionBG");
        bg.sprite = CompetitionBgSprite();
        bg.raycastTarget = false;
        bg.preserveAspect = false;
        bg.color = bg.sprite != null ? new Color(brightness, brightness, brightness, 1f) : GameModeBg;
        Stretch(bg.rectTransform);
    }

    // The universal back button — the back-button sprite at native aspect, anchored top-left of a bar.
    Button MakeBackButton(Transform parent, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("BtnBack");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(64f, 64f));

        Image img = go.AddComponent<Image>();
        img.sprite = BackButtonSprite();
        img.preserveAspect = true;
        if (img.sprite == null) // rounded fallback with a "<" glyph if the sprite is missing
        {
            img.sprite = GetRoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = new Color(0.16f, 0.2f, 0.28f, 1f);
            TextMeshProUGUI t = MakeText(go.transform, "<", 34f, new Vector2(0.5f, 0.5f), Vector2.zero,
                                         new Vector2(64f, 64f), Color.white, TextAlignmentOptions.Center);
            Stretch(t.rectTransform);
        }

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);
        AddHover(go);
        return btn;
    }

    // A prominent rounded, labelled action button (NEXT MATCH / PLAY / CLAIM REWARDS).
    Button MakeActionButton(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size,
                            Color color, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("BtnAction");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, pos, size);

        Image img = go.AddComponent<Image>();
        img.sprite = GetRoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = color;

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);

        TextMeshProUGUI t = MakeText(go.transform, label, Mathf.Min(26f, size.y * 0.38f),
                                     new Vector2(0.5f, 0.5f), Vector2.zero, size, Color.white,
                                     TextAlignmentOptions.Center);
        Stretch(t.rectTransform);
        AddHover(go);
        return btn;
    }

    static void ClearChildren(Transform t)
    {
        if (t == null) return;
        for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
    }

    // The player's club name. TeamSide only exists in the match scene, so in the hub this falls back
    // to "MY TEAM" (per spec).
    static string PlayerTeamName()
    {
        TeamSide ts = FindFirstObjectByType<TeamSide>();
        if (ts != null && !string.IsNullOrEmpty(ts.teamName) && ts.teamName != "Team") return ts.teamName;
        return "MY TEAM";
    }

    static string StarString(int n)
    {
        n = Mathf.Clamp(n, 0, 5);
        return new string('★', n) + new string('☆', 5 - n);
    }

    static string Signed(int v) => v > 0 ? "+" + v : v.ToString();

    void ShowOverlay(GameObject overlay)
    {
        if (overlay == null) return;
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        if (slideRoutine != null) StopCoroutine(slideRoutine);
        slideRoutine = overlay == gameModeOverlay
            ? StartCoroutine(RevealGameMode(true))   // fade backdrop + stagger the cards in
            : StartCoroutine(SlideOverlay(overlay, true));
    }

    void HideOverlay(GameObject overlay)
    {
        if (overlay == null) return;
        if (slideRoutine != null) StopCoroutine(slideRoutine);
        slideRoutine = overlay == gameModeOverlay
            ? StartCoroutine(RevealGameMode(false))
            : StartCoroutine(SlideOverlay(overlay, false));
    }

    // Game-mode open/close: fade the backdrop, then stagger the cards in (fade + scale 0.9→1.0,
    // rising from just below their resting spot), each 0.08s after the previous, left → right.
    IEnumerator RevealGameMode(bool show)
    {
        CanvasGroup cg = gameModeOverlay.GetComponent<CanvasGroup>();
        if (gameModeOverlay.transform.Find("Sheet") is RectTransform sheet)
            sheet.anchoredPosition = Vector2.zero; // this overlay fades rather than slides
        float dur = Mathf.Max(0.01f, fadeSeconds);
        float t = 0f;

        if (show)
        {
            // Reset every card to its hidden pose before the reveal.
            for (int i = 0; i < gmCardRects.Count; i++)
            {
                if (gmCardGroups[i] != null) gmCardGroups[i].alpha = 0f;
                if (gmCardRects[i] != null)
                {
                    gmCardRects[i].localScale = Vector3.one * 0.9f;
                    gmCardRects[i].anchoredPosition = gmCardBasePos[i] + new Vector2(0f, -40f);
                }
                StartCoroutine(RevealCard(i, 0.28f, i * 0.08f));
            }

            if (cg != null) cg.alpha = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                if (cg != null) cg.alpha = Mathf.Clamp01(t / dur);
                yield return null;
            }
            if (cg != null) cg.alpha = 1f;
        }
        else
        {
            float a0 = cg != null ? cg.alpha : 1f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                if (cg != null) cg.alpha = Mathf.Lerp(a0, 0f, Mathf.Clamp01(t / dur));
                yield return null;
            }
            if (cg != null) cg.alpha = 0f;
            gameModeOverlay.SetActive(false);
        }
        slideRoutine = null;
    }

    // Animates one card from its hidden pose to resting after `delay`. The default-selected card
    // gets a slight overshoot (ease-out-back) so its gold selection state visibly pops in.
    IEnumerator RevealCard(int i, float dur, float delay)
    {
        if (i >= gmCardRects.Count) yield break;
        RectTransform rt = gmCardRects[i];
        CanvasGroup cg = gmCardGroups[i];
        Vector2 basePos = gmCardBasePos[i];
        bool overshoot = gmCardSelected[i];
        if (rt == null) yield break;

        float t = 0f;
        while (t < delay) { t += Time.unscaledDeltaTime; yield return null; }

        t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            float fade = EaseOutCubic(k);
            if (cg != null) cg.alpha = fade;
            float s = Mathf.LerpUnclamped(0.9f, 1f, overshoot ? EaseOutBack(k) : fade);
            rt.localScale = Vector3.one * s;
            rt.anchoredPosition = basePos + new Vector2(0f, Mathf.Lerp(-40f, 0f, fade));
            yield return null;
        }
        if (cg != null) cg.alpha = 1f;
        rt.localScale = Vector3.one;
        rt.anchoredPosition = basePos;
    }

    static float EaseOutCubic(float k) { float p = 1f - k; return 1f - p * p * p; }
    static float EaseOutBack(float k)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float p = k - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
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

    // Vertical gradient: opaque (white, tintable) at the bottom → transparent at the top. Tinted black
    // per card for the name strip. One column is enough — it stretches horizontally.
    static Sprite BottomGradient()
    {
        if (gradientSprite != null) return gradientSprite;
        const int w = 4, h = 128;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++) // texture y=0 is the bottom row → most opaque
        {
            byte a = (byte)((1f - y / (float)(h - 1)) * 255f);
            for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        gradientSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        return gradientSprite;
    }

    // A full-frame sprite wrapped from a Resources texture (not Resources.Load<Sprite>) so it works no
    // matter how the PNG's sprite import mode is set. Cached per path.
    static Sprite TextureSprite(string path)
    {
        if (textureSpriteCache.TryGetValue(path, out Sprite cached) && cached != null) return cached;
        Texture2D tex = Resources.Load<Texture2D>(path);
        if (tex == null)
        {
            Debug.LogWarning("NavigationManager: texture not found at Resources/" + path +
                             " — check the file exists there and its Texture Type is 'Sprite (2D and UI)'.");
            return null;
        }
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        textureSpriteCache[path] = sp;
        return sp;
    }

    static Sprite PoolScreenSprite() => TextureSprite("Sprites/pool-screen");
    static Sprite BackButtonSprite() => TextureSprite("Sprites/back-button");
    static Sprite CompetitionBgSprite() => TextureSprite("Sprites/competition-page-background");

    // lock-sign art, cropped to just the red padlock button. The source PNG has wide transparent
    // margins, so a full-frame sprite would render tiny; the content box (measured as fractions of the
    // texture, so it survives a re-import at a different size) is cut out here via Sprite.Create.
    static Sprite LockSignSprite()
    {
        if (lockSignSprite != null) return lockSignSprite;
        Texture2D tex = Resources.Load<Texture2D>("Sprites/lock-sign");
        if (tex == null)
        {
            Debug.LogWarning("NavigationManager: Resources/Sprites/lock-sign not found — using procedural lock.");
            return null;
        }
        // Content box as texture fractions (x0,x1 from left; yTop,yBot from top) with a little padding.
        const float x0 = 0.298f, x1 = 0.702f, yTop = 0.187f, yBot = 0.775f;
        float rx = x0 * tex.width;
        float ry = (1f - yBot) * tex.height;          // Unity texture space has y=0 at the bottom
        float rw = (x1 - x0) * tex.width;
        float rh = (yBot - yTop) * tex.height;
        lockSignSprite = Sprite.Create(tex, new Rect(rx, ry, rw, rh), new Vector2(0.5f, 0.5f), 100f);
        return lockSignSprite;
    }

    // Radial vignette: transparent centre → opaque toward the edges (white, tinted dark by the Image).
    // Stretched over the screen it gives a soft, breathing edge-darkening.
    static Sprite Vignette()
    {
        if (vignetteSprite != null) return vignetteSprite;
        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        Vector2 c = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxD = c.magnitude;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / maxD; // 0 centre → 1 corner
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, d));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        vignetteSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return vignetteSprite;
    }
}
