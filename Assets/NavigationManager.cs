using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;
using System.Globalization;

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
    private const float CurrencyGoldX = -94f;
    private const float CurrencyDiamondX = -270f;

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
    private WorldCupUI worldCupUI;
    private ShopUI shopUI;                           // the Shop screen component (for tab-jump shortcuts)
    private GameObject standingsOverlay, preMatchOverlay; // built lazily, content rebuilt on each open
    private GameObject restartChampionshipOverlay;
    private GameObject clubOverlay, settingsOverlay, messagesOverlay, giftsOverlay;
    private GameObject missionsOverlay, seasonPassOverlay;
    private GameObject friendsOverlay, clubsOverlay; // coming-soon stubs (no online backend yet)

    // THE match scene. The old SampleScene (Pool A) and the pre-match SELECT POOL step are
    // retired — every match-load path (pre-match PLAY here, PLAY AGAIN in MatchResultUI) goes
    // straight to this scene. SampleScene.unity still exists on disk but nothing loads it.
    public const string MatchScene = "SampleScene_PoolB";
    private GameObject missionsBadgeGo;              // red claim-ready counter on the missions button
    private TextMeshProUGUI missionsBadgeLabel;
    private Coroutine slideRoutine;
    private GameObject slideTarget;                  // overlay the running slideRoutine animates
    private bool slideShowing;                       // its direction (see FinishSlide)

    // Top-left profile cluster (avatar + flag + name), refreshed from RosterManager.Club.
    private Image avatarCircle, flagDot;
    private CrestTemplateView avatarClubCrest;
    private TextMeshProUGUI clubNameLabel;

    // Competition display names, shared by the cards, standings and pre-match screens.
    private static readonly string[] CompNames =
        { "DIVISION 1", "PREMIUM LEAGUE", "CONTINENTAL CUP", "WORLD CHAMPIONS LEAGUE" };

    // Competition-screen view state (reset on each open; rebuilt in place on tab/expand taps).
    private int compTab;                                     // 0 = GROUP STAGE, 1 = KNOCKOUT
    private readonly bool[] compGroupExpanded = new bool[2]; // per-group expanded table flag
    private int competitionViewIndex = -1;

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
        BuildRightColumn();
        BuildSeasonTimer();
        BuildBottomBar();
        BuildOverlays();
        RefreshCurrency();
        RefreshClubProfile();
        RefreshMissionsBadge();
        StartCoroutine(FadeInHub());

        // A match just dropped a new pack into a slot → announce it with a scale-in.
        int newSlot = PostMatchRewardManager.Instance.ConsumeNewRewardSlot();
        if (newSlot >= 0) StartCoroutine(RevealNewRewardSlot(newSlot));
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

        // Left: profile cluster — avatar + flag badge (opens My Club), name + XP + level (also
        // opens My Club), gear (settings stub), envelope/gift (coming soon), FREE +100 ad pill.
        // No shop button or currency display here — those live on the right side / left column.
        GameObject avGo = new GameObject("BtnAvatar");
        avGo.transform.SetParent(bar.transform, false);
        SetRect(avGo.AddComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(48f, 0f), new Vector2(60f, 60f));
        avatarCircle = avGo.AddComponent<Image>();
        avatarCircle.sprite = Circle();
        // Keep the full clickable avatar hit area, but render the saved crest directly.
        avatarCircle.color = Color.clear;
        Button avBtn = avGo.AddComponent<Button>();
        avBtn.targetGraphic = avatarCircle;
        avBtn.onClick.AddListener(OpenClubScreen);
        AddHover(avGo);
        avatarClubCrest = CrestTemplateView.Create(avGo.transform, "SavedClubCrest",
            new Vector2(58f, 58f), new Vector2(0.5f, 0.5f), Vector2.zero);
        // Country "flag" badge: a colored dot until real flag art exists (grey = no country picked).
        flagDot = NewImage(avGo.transform, "Flag");
        flagDot.sprite = Circle();
        flagDot.raycastTarget = false;
        SetRect(flagDot.rectTransform, new Vector2(1f, 0f), new Vector2(-4f, 6f), new Vector2(20f, 20f));

        // Club name + XP bar + bronze level badge.
        clubNameLabel = MakeText(bar.transform, "My Club", 20f, new Vector2(0f, 0.5f), new Vector2(150f, 12f),
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
        fr.anchorMax = new Vector2(0.7f, 1f);
        fr.offsetMin = new Vector2(1f, 1f);
        fr.offsetMax = new Vector2(-1f, -1f);
        // No player-level XP system exists yet (RosterManager tracks none) — show an HONEST
        // empty bar instead of the old fake 70%. Re-enable the fill when real XP lands.
        xpFill.gameObject.SetActive(false);

        Image badge = NewImage(bar.transform, "LevelBadge");
        badge.sprite = Circle();
        badge.color = Bronze;
        badge.raycastTarget = false;
        SetRect(badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(210f, -14f), new Vector2(22f, 22f));
        MakeText(badge.transform, "1", 13f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 22f),
                 Color.white, TextAlignmentOptions.Center);

        // Invisible click area over the name/XP/level cluster → My Club (same as the avatar).
        GameObject nameBtnGo = new GameObject("BtnClubName");
        nameBtnGo.transform.SetParent(bar.transform, false);
        SetRect(nameBtnGo.AddComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(160f, 0f), new Vector2(160f, 64f));
        Image nameHit = nameBtnGo.AddComponent<Image>();
        nameHit.color = new Color(0f, 0f, 0f, 0f);
        Button nameBtn = nameBtnGo.AddComponent<Button>();
        nameBtn.targetGraphic = nameHit;
        nameBtn.transition = Selectable.Transition.None;
        nameBtn.onClick.AddListener(OpenClubScreen);

        // Settings / Inbox / Gifts — real button art (settings/message/gifts-button.png), one
        // group between the profile block and the pill. 42px icons (dev sized these down from
        // an 84px experiment) at 95px pitch.
        MakeCaptionedIconButton(bar.transform, "BtnSettings", "Sprites/settings-button", "Settings",
                                new Vector2(330f, 0f), () => ShowOverlay(settingsOverlay));
        MakeCaptionedIconButton(bar.transform, "BtnMessages", "Sprites/message-button", "Inbox",
                                new Vector2(425f, 0f), () => ShowOverlay(messagesOverlay));
        MakeCaptionedIconButton(bar.transform, "BtnGifts", "Sprites/gifts-button", "Gifts",
                                new Vector2(520f, 0f), () => ShowOverlay(giftsOverlay));

        // FREE +100: watch an ad (stub) for 100 coins — same AdWatchCap 3/day system as the
        // shop. Moved right (590→660) to stay clear of the enlarged icon group.
        BuildFree100Button(bar.transform, new Vector2(660f, 0f));

        // Premium currency chips: icon well, strong count hierarchy and an integrated shop shortcut.
        goldLabel = MakeCurrencyChip(bar.transform, "GoldChip", "Sprites/gold-coin", Gold,
                                     new Vector2(CurrencyGoldX, 0f), () => OpenShopTab(5));
        diamondLabel = MakeCurrencyChip(bar.transform, "DiamondChip", "Sprites/diamond-coin", Cyan,
                                        new Vector2(CurrencyDiamondX, 0f), () => OpenShopTab(6));
    }

    // Re-read the player's balances into the top bar. Called on build and by TeamScreenUI after a
    // buy / sell / upgrade so the persistent bar never drifts from the real roster.
    public void RefreshCurrency()
    {
        RosterManager rm = RosterManager.Instance;
        if (goldLabel != null) goldLabel.text = FormatCurrency(rm.Coins);
        if (diamondLabel != null) diamondLabel.text = FormatCurrency(rm.Diamonds);
        if (gmGoldLabel != null) gmGoldLabel.text = FormatCurrency(rm.Coins);
        if (gmDiamondLabel != null) gmDiamondLabel.text = FormatCurrency(rm.Diamonds);
    }

    // ------------------------------------------------------------- left column

    // All five column buttons use trimArt: the source PNGs carry very different transparent
    // margins (see LoadTrimmedSprite), so equal RectTransform sizes only LOOK equal after the
    // alpha-trim. Ranking/Team 135, Shop back at its original 140 (per dev). Both columns sit
    // yOff 40px below screen centre — centred they read "too high" against the hub background.
    void BuildLeftColumn()
    {
        const float x = 150f, step = 140f, yOff = -40f;
        MakeImageButton(canvasRoot, "BtnRanking", "Sprites/ranking-button", new Vector2(0f, 0.5f),
                        new Vector2(x, yOff + step), new Vector2(135f, 135f), () => ShowOverlay(rankingOverlay),
                        trimArt: true);
        MakeImageButton(canvasRoot, "BtnShop", "Sprites/shop-button", new Vector2(0f, 0.5f),
                        new Vector2(x, yOff), new Vector2(140f, 140f), () => ShowOverlay(shopOverlay),
                        trimArt: true);
        MakeImageButton(canvasRoot, "BtnTeam", "Sprites/team-button", new Vector2(0f, 0.5f),
                        new Vector2(x, yOff - step), new Vector2(135f, 135f), () => OpenTeamScreen("HUB"),
                        trimArt: true);
    }

    // ------------------------------------------------------------ right column

    // Friends / Clubs — mirrors the left column on the right edge. No online backend exists
    // (accounts, unique IDs, club membership are all deferred — see the master plan roadmap),
    // so both open the same honest COMING SOON stub the other unbuilt features use.
    void BuildRightColumn()
    {
        const float x = -150f, step = 140f, yOff = -40f; // rows/offset match the left column
        worldCupUI = gameObject.AddComponent<WorldCupUI>();
        worldCupUI.Initialize(canvasRoot);
        MakeImageButton(canvasRoot, "BtnFriends", "Sprites/friends-button", new Vector2(1f, 0.5f),
                        new Vector2(x, yOff + step), new Vector2(135f, 135f), () => ShowOverlay(friendsOverlay),
                        trimArt: true);
        // Clubs gets a bigger box (150 vs 135): its trimmed art is intrinsically wide (~1.8:1),
        // so at equal box sizes it reads smaller than the near-square art around it.
        MakeImageButton(canvasRoot, "BtnClubs", "Sprites/clubs-button", new Vector2(1f, 0.5f),
                        new Vector2(x, yOff), new Vector2(150f, 150f), () => ShowOverlay(clubsOverlay),
                        trimArt: true);
        CountryCatalog countryCatalog = CountryCatalog.Instance;
        MakeDirectImageButton(canvasRoot, "BtnWorldCup",
            countryCatalog != null ? countryCatalog.WorldCupTrophy : null,
            new Vector2(1f, 0.5f), new Vector2(x, yOff - step),
            new Vector2(135f, 135f), worldCupUI.Open);
    }

    // ------------------------------------------------------------ season timer

    void BuildSeasonTimer()
    {
        // Live countdown from SeasonPassManager's stored epoch (no longer decorative);
        // tapping it opens the Season Pass screen.
        Image panel = MakePanel(canvasRoot, new Vector2(1f, 1f), new Vector2(-118f, -126f),
                                new Vector2(200f, 80f), DarkPanel);
        panel.raycastTarget = true;
        Button b = panel.gameObject.AddComponent<Button>();
        b.targetGraphic = panel;
        b.onClick.AddListener(() => ShowOverlay(seasonPassOverlay));
        AddHover(panel.gameObject);
        MakeText(panel.transform, "SEASON ENDS IN:", 13f, new Vector2(0.5f, 1f), new Vector2(0f, -20f),
                 new Vector2(188f, 20f), new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center);
        MakeText(panel.transform, SeasonPassManager.Instance.CountdownLabel(), 30f, new Vector2(0.5f, 0f),
                 new Vector2(0f, 16f), new Vector2(188f, 40f), Gold, TextAlignmentOptions.Center);
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

        // Season pass (left): opens the Season Pass screen — the SAME destination as the "SEASON
        // ENDS IN" panel (two entry points to one screen, intentionally). The old dark overlay +
        // padlock + "UNLOCKED AT LEVEL 4" was a pre-Season-Pass placeholder for a player-level
        // system that never existed, so it's removed entirely (no lock, no disabled state).
        Button sp = MakeImageButton(barGo.transform, "BtnSeasonPass", "Sprites/season-pass-button",
                                    new Vector2(0f, 0.5f), new Vector2(195f, 0f), new Vector2(260f, 80f),
                                    () => ShowOverlay(seasonPassOverlay));
        sp.image.preserveAspect = false; // stretch/fill the 220x110 rect (Image Type stays Simple)

        // Missions (centre-left): opens the Missions screen; the red badge shows the live
        // claim-ready count (hidden at 0 — see RefreshMissionsBadge).
        Button ms = MakeImageButton(barGo.transform, "BtnMissions", "Sprites/missions-button",
                                    new Vector2(0f, 0.5f), new Vector2(455f, 0f), new Vector2(90f, 90f),
                                    () => ShowOverlay(missionsOverlay));
        Image dot = NewImage(ms.transform, "Badge");
        dot.sprite = Circle();
        dot.color = Red;
        dot.raycastTarget = false;
        SetRect(dot.rectTransform, new Vector2(1f, 1f), new Vector2(-6f, -6f), new Vector2(26f, 26f));
        missionsBadgeGo = dot.gameObject;
        missionsBadgeLabel = MakeText(dot.transform, "0", 15f, new Vector2(0.5f, 0.5f), Vector2.zero,
                 new Vector2(26f, 26f), Color.white, TextAlignmentOptions.Center);

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
        // Pitch 84 (was 78) leaves room for active slots to scale up 18% without overlapping
        // each other or the PLAY button.
        const float w = 70f, h = 90f, pitch = 84f;

        for (int i = 0; i < PostMatchRewardManager.SlotCount; i++)
        {
            int idx = i;
            PostMatchRewardManager.Slot slot = mgr.GetSlot(i);
            bool ready = mgr.IsReady(i);
            rewardTimeLabels[i] = null;
            rewardShownReady[i] = ready;
            float sx = (i - 1.5f) * pitch;

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
            icon.sprite = CardPack.TierArtSprite(slot.Tier);
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

            // A genuinely active pack (Ready or counting down) pops 18% larger and gets the
            // idle float + shine. Locked/empty slots stay static with the normal hover.
            bool active = ready || slot.State == PostMatchRewardManager.SlotState.Unlocking;
            if (active)
            {
                frame.transform.localScale = Vector3.one * 1.18f;
                PackCardFX.Attach(frame.rectTransform, 4f);
            }
            else AddHover(frame.gameObject); // hover-scale would fight the enlarged scale
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
        art.sprite = CardPack.TierArtSprite(slot.Tier);
        art.preserveAspect = true;
        art.raycastTarget = false;
        if (art.sprite == null) art.color = CardPack.TierColor(slot.Tier);
        SetRect(art.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(130f, 130f));

        MakeText(sheet.transform, def.name, 28f, new Vector2(0.5f, 1f), new Vector2(0f, -178f),
                 new Vector2(500f, 36f), CardPack.TierColor(slot.Tier), TextAlignmentOptions.Center);
        MakeText(sheet.transform, "UP TO " + def.maxCards + " PLAYERS", 18f, new Vector2(0.5f, 1f),
                 new Vector2(0f, -212f), new Vector2(500f, 26f), Color.white, TextAlignmentOptions.Center);

        // Drop-rate table: the shared PackInfoPopup rows, identical to the shop's "i" popup.
        PackInfoPopup.BuildOddsRows(sheet.transform, def, -248f);

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
        messagesOverlay = BuildComingSoonOverlay("MESSAGES");
        giftsOverlay = BuildComingSoonOverlay("GIFTS");
        friendsOverlay = BuildComingSoonOverlay("FRIENDS");
        clubsOverlay = BuildComingSoonOverlay("CLUBS");
        settingsOverlay = BuildSettingsOverlay();
        shopOverlay = BuildShopOverlay();
        teamOverlay = BuildTeamOverlay();
        clubOverlay = BuildClubOverlay();
        rankingOverlay = BuildRankingOverlay();
        missionsOverlay = BuildMissionsOverlay();
        seasonPassOverlay = BuildSeasonPassOverlay();
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

        shopUI = sheetGo.AddComponent<ShopUI>();
        shopUI.Build(sheetGo.transform, this);

        ov.SetActive(false);
        return ov;
    }

    // Called by ShopUI's back arrow.
    public void CloseShopScreen() => HideOverlay(shopOverlay);

    // Open the Shop already scrolled to a specific section (ShopUI tab index: 5 = COINS, 6 = GEMS).
    // Used by the hub top-bar currency [+] buttons — reuses the shop's own SelectTab glide-scroll,
    // the same mechanism its internal tab bar and top-bar [+] shortcuts use.
    public void OpenShopTab(int tab)
    {
        ShowOverlay(shopOverlay);           // activates the overlay (so ShopUI can run its scroll)
        if (shopUI != null) shopUI.SelectTab(tab);
    }

    // Open the shared (minimal) settings overlay from anywhere (hub gear already uses settingsOverlay
    // directly; this lets hosted screens like the Shop route their gear to the same destination).
    public void OpenSettingsScreen() => ShowOverlay(settingsOverlay);

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

    // ------------------------------------------------------- club profile / my club

    // Dark full-screen overlay hosting ClubCustomizationUI (same shell as the team overlay).
    GameObject BuildClubOverlay()
    {
        GameObject ov = new GameObject("Overlay_CLUB");
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

        ClubCustomizationUI club = sheetGo.AddComponent<ClubCustomizationUI>();
        club.Build(sheetGo.transform, this);

        ov.SetActive(false);
        return ov;
    }

    public void OpenClubScreen() => ShowOverlay(clubOverlay);
    public void CloseClubScreen() => HideOverlay(clubOverlay);

    // ------------------------------------------- missions / ranking / season pass

    // Same full-canvas hosted-overlay shell as the team/shop/club screens.
    GameObject BuildMissionsOverlay()
    {
        GameObject ov = BuildHostShell("Overlay_MISSIONS", out Transform sheet);
        sheet.gameObject.AddComponent<MissionsUI>().Build(sheet, this);
        return ov;
    }

    GameObject BuildRankingOverlay()
    {
        GameObject ov = BuildHostShell("Overlay_RANKING", out Transform sheet);
        sheet.gameObject.AddComponent<RankingUI>().Build(sheet, this);
        return ov;
    }

    GameObject BuildSeasonPassOverlay()
    {
        GameObject ov = BuildHostShell("Overlay_SEASONPASS", out Transform sheet);
        sheet.gameObject.AddComponent<SeasonPassUI>().Build(sheet, this);
        return ov;
    }

    GameObject BuildHostShell(string name, out Transform sheet)
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
        sheet = sheetGo.transform;

        ov.SetActive(false);
        return ov;
    }

    public void CloseMissionsScreen() => HideOverlay(missionsOverlay);
    public void CloseRankingScreen() => HideOverlay(rankingOverlay);
    public void CloseSeasonPassScreen() => HideOverlay(seasonPassOverlay);

    // Recompute the missions button's claim-ready counter (hidden at 0). Called on hub build
    // and by MissionsUI after every claim.
    public void RefreshMissionsBadge()
    {
        if (missionsBadgeGo == null) return;
        int n = MissionManager.Instance.ClaimReadyCount();
        missionsBadgeGo.SetActive(n > 0);
        if (missionsBadgeLabel != null) missionsBadgeLabel.text = n.ToString();
    }

    // Re-read the saved club identity into the top-left cluster. Called on hub build and by
    // ClubCustomizationUI's APPLY so the bar updates the moment the profile changes.
    public void RefreshClubProfile()
    {
        ClubProfile club = RosterManager.Instance.Club;
        if (clubNameLabel != null) clubNameLabel.text = club.clubName;
        if (avatarClubCrest != null) avatarClubCrest.SetIdentity(club);
        if (flagDot != null)
            flagDot.color = string.IsNullOrEmpty(club.countryId)
                ? new Color(0.4f, 0.44f, 0.5f, 1f) // placeholder: no country picked yet
                : ClubCustomizationUI.CountryColor(club.countryId);
    }

    // Minimal stub settings panel — just states itself and closes. Real options come later.
    GameObject BuildSettingsOverlay()
    {
        GameObject ov = new GameObject("Overlay_SETTINGS");
        ov.transform.SetParent(canvasRoot, false);
        Stretch(ov.AddComponent<RectTransform>());
        Image backdrop = ov.AddComponent<Image>();
        backdrop.color = OverlayDark;
        backdrop.raycastTarget = true;
        ov.AddComponent<CanvasGroup>();

        Image sheet = NewImage(ov.transform, "Sheet");
        sheet.sprite = GetRoundedSprite();
        sheet.type = Image.Type.Sliced;
        sheet.color = DarkPanel;
        SetRect(sheet.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 300f));

        MakeText(sheet.transform, "SETTINGS", 24f, new Vector2(0.5f, 1f), new Vector2(0f, -52f),
                 new Vector2(560f, 32f), Cyan, TextAlignmentOptions.Center);
        MakeText(sheet.transform, "Sound, language and account options will live here.\nNothing to configure yet.",
                 18f, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(560f, 60f),
                 Color.white, TextAlignmentOptions.Center);

        GameObject self = ov;
        MakeActionButton(sheet.transform, "OK", new Vector2(0.5f, 0f), new Vector2(0f, 46f),
                         new Vector2(180f, 56f), Green, () => HideOverlay(self));
        MakeCloseButton(sheet.transform, () => HideOverlay(self));

        ov.SetActive(false);
        return ov;
    }

    // Top-bar icon button with real sprite art (alpha-trimmed — the raw PNGs are ~60% margin).
    // The label is a tooltip, not a permanent caption: hidden by default, shown on hover
    // (desktop) or after a ~0.4s press-and-hold (touch), on a dark pill backing.
    Button MakeCaptionedIconButton(Transform parent, string name, string spritePath, string caption,
                                   Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        Button btn = MakeImageButton(parent, name, spritePath, new Vector2(0f, 0.5f),
                                     pos, new Vector2(42f, 42f), onClick, trimArt: true);
        Image tipBg = NewImage(btn.transform, "Tooltip");
        tipBg.sprite = GetRoundedSprite();
        tipBg.type = Image.Type.Sliced;
        tipBg.color = DarkPanel;
        tipBg.raycastTarget = false;
        SetRect(tipBg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -16f), new Vector2(94f, 26f));
        TextMeshProUGUI tip = MakeText(tipBg.transform, caption, 13f, new Vector2(0.5f, 0.5f),
                 Vector2.zero, new Vector2(94f, 26f), Color.white, TextAlignmentOptions.Center);
        tip.raycastTarget = false;
        tipBg.gameObject.SetActive(false);
        btn.gameObject.AddComponent<IconTooltip>().tooltip = tipBg.gameObject;
        return btn;
    }

    // "FREE +100" pill: fake-ad (0.8s) → +100 coins, individually capped at 3/day via AdWatchCap.
    void BuildFree100Button(Transform parent, Vector2 pos)
    {
        const string capId = "hub_free100";
        bool capped = AdWatchCap.Used(capId) >= AdWatchCap.DailyCap;

        GameObject go = new GameObject("BtnFree100");
        go.transform.SetParent(parent, false);
        SetRect(go.AddComponent<RectTransform>(), new Vector2(0f, 0.5f), pos, new Vector2(146f, 40f));
        Image img = go.AddComponent<Image>();
        img.sprite = GetRoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = capped ? new Color(0.3f, 0.34f, 0.4f, 1f) : Green;
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable = !capped;

        TextMeshProUGUI txt = MakeText(go.transform, capped ? AdWatchCap.ResetLabel() : "FREE +100",
                                       capped ? 12f : 15f, new Vector2(0.5f, 0.5f), new Vector2(-8f, 0f),
                                       new Vector2(116f, 40f), Color.white, TextAlignmentOptions.Center);
        Image tri = NewImage(go.transform, "Play");
        tri.sprite = TriangleSprite();
        tri.color = Color.white;
        tri.raycastTarget = false;
        SetRect(tri.rectTransform, new Vector2(1f, 0.5f), new Vector2(-15f, 0f), new Vector2(12f, 14f));
        tri.gameObject.SetActive(!capped);

        if (!capped) btn.onClick.AddListener(() =>
        {
            btn.interactable = false;
            txt.text = "LOADING...";
            tri.gameObject.SetActive(false);
            StartCoroutine(HubFakeAd(() =>
            {
                AdWatchCap.Record(capId);
                RosterManager.Instance.AddCoins(100);
                RefreshCurrency();
                if (btn == null) return;
                if (AdWatchCap.Used(capId) >= AdWatchCap.DailyCap)
                {
                    img.color = new Color(0.3f, 0.34f, 0.4f, 1f);
                    txt.fontSize = 12f;
                    txt.text = AdWatchCap.ResetLabel();
                }
                else
                {
                    btn.interactable = true;
                    txt.text = "FREE +100";
                    tri.gameObject.SetActive(true);
                }
            }));
        });
        AddHover(go);
    }

    // Fake rewarded ad: ~0.8s pause then the grant. TODO(ads): swap for the real ad SDK.
    IEnumerator HubFakeAd(System.Action grant)
    {
        yield return new WaitForSecondsRealtime(0.8f);
        grant?.Invoke();
    }

    // Scale-in-with-overshoot on the slot that just received a pack after a match. (Chosen over
    // a genuine fly-in-from-offscreen: the slot row is rebuilt wholesale on every state change,
    // so animating a temporary flying icon across the screen adds fragility for little payoff.)
    IEnumerator RevealNewRewardSlot(int slotIndex)
    {
        yield return null; // let the first layout pass land
        if (rewardSlotRow == null) yield break;
        Transform slot = rewardSlotRow.Find("RewardSlot" + (slotIndex + 1));
        if (slot == null) yield break;
        Vector3 target = slot.localScale; // 1.0 — a fresh drop is Locked, never enlarged
        slot.localScale = Vector3.zero;
        float t = 0f;
        const float dur = 0.5f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            if (slot == null) yield break;
            slot.localScale = target * EaseOutBack(Mathf.Clamp01(t / dur));
            yield return null;
        }
        if (slot != null) slot.localScale = target;
    }

    // Small right-pointing triangle (play glyph / envelope flap). Same procedural pattern as
    // ShopUI's watch-button icon.
    static Sprite triangleSprite;
    static Sprite TriangleSprite()
    {
        if (triangleSprite != null) return triangleSprite;
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float progress = x / (float)(size - 1);
                float halfH = (1f - progress) * (size * 0.5f - 1f);
                float dy = Mathf.Abs(y - (size * 0.5f - 0.5f));
                float a = Mathf.Clamp01(halfH - dy + 1f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        triangleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return triangleSprite;
    }

    // ------------------------------------------------------------- game mode

    // Full-screen "GAME MODE" overlay (built in code, no prefab — same slide-in shell as the team
    // overlay). Four competition cards in a row; each unlocks after the previous is won (PlayerPrefs:
    // div1_won / pl_won / cc_won). Tapping an unlocked card starts the match (SampleScene_PoolB — all
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

        gmGoldLabel = MakeCurrencyChip(bar.transform, "GoldChip", "Sprites/gold-coin", Gold,
                                       new Vector2(CurrencyGoldX, 0f), () => OpenShopTab(5));
        gmDiamondLabel = MakeCurrencyChip(bar.transform, "DiamondChip", "Sprites/diamond-coin", Cyan,
                                          new Vector2(CurrencyDiamondX, 0f), () => OpenShopTab(6));

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
        string[] names = { "DIVISION 1", "PREMIUM LEAGUE", "CONTINENTAL CUP", "WORLD CHAMPIONS LEAGUE" };
        string[] sprites = { "Sprites/division1-card", "Sprites/premier-league-card",
                             "Sprites/continental-cup-card", "Sprites/world-champions-league-card" };
        Color[] tierColors = { new Color(0.180f, 0.800f, 0.251f),   // #2ECC40 green
                               new Color(0.608f, 0.349f, 0.714f),   // #9B59B6 purple
                               new Color(0.161f, 0.502f, 0.725f),   // #2980B9 blue
                               new Color(0.953f, 0.612f, 0.071f) }; // #F39C12 gold
        string[] lockText = { "", "WIN DIVISION 1 TO UNLOCK", "WIN PREMIUM LEAGUE TO UNLOCK",
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

            // Locked divisions are deliberately inspectable. The transparent hit target lives above
            // the veil so a player can see clubs/rewards, while the information screen keeps START disabled.
            Image hit = NewImage(cardGo.transform, "Hit");
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            Stretch(hit.rectTransform);
            Button inspect = hit.gameObject.AddComponent<Button>();
            inspect.targetGraphic = hit;
            inspect.onClick.AddListener(() => OpenStandings(index));
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

    // =============================================================== competition screen

    // Flow: Game Mode card → Competition screen (GROUP STAGE / KNOCKOUT tabs) → NEXT MATCH →
    // Pre-Match → PLAY → SampleScene_PoolB. The competition and pre-match overlays are built lazily and
    // their content is rebuilt on every open (the tournament moves on between visits), then slid in
    // via the shared SlideOverlay. Tab switches and group expand/collapse taps rebuild the sheet in
    // place (no slide).

    public void OpenStandings(int competitionIndex)
    {
        competitionViewIndex = competitionIndex;
        if (IsCompetitionUnlocked(competitionIndex))
            LeagueSeason.Ensure(competitionIndex, PlayerTeamName());
        else
            LeagueSeason.ClearCurrentSelection();
        // Fresh view state each open: group tab during the group stage, knockout once it starts.
        compTab = LeagueSeason.Current == null || LeagueSeason.Current.phase == LeagueSeason.Phase.GroupStage ? 0 : 1;
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

    // Information-only screen used before a championship begins and for locked divisions. It gives
    // players the requested visibility into both fixed groups without leaking fixtures early.
    void BuildCompetitionOverview(Transform sheet, int comp)
    {
        bool unlocked = IsCompetitionUnlocked(comp);
        RectTransform content = MakeCompScroll(sheet);
        float y = BuildCompetitionHero(content, comp, unlocked, 6f) + 14f;
        y = BuildRewardsCard(content, comp, y) + 14f;
        const float groupsH = 348f;
        BuildClubGroupOverview(content, comp, 0, -286f, y, groupsH);
        BuildClubGroupOverview(content, comp, 1, 286f, y, groupsH);
        content.sizeDelta = new Vector2(0f, y + groupsH + 14f);

        if (unlocked)
            MakeActionButton(sheet, "START CHAMPIONSHIP", new Vector2(0.5f, 0f), new Vector2(0f, 46f),
                             new Vector2(440f, 68f), Green, () => StartChampionship(comp));
    }

    float BuildCompetitionHero(RectTransform content, int comp, bool unlocked, float yTop)
    {
        const float h = 150f;
        GameObject card = new GameObject("CompetitionHero");
        card.transform.SetParent(content, false);
        Image frame = card.AddComponent<Image>();
        frame.sprite = GetRoundedSprite();
        frame.type = Image.Type.Sliced;
        frame.color = unlocked ? Gold : new Color(0.45f, 0.52f, 0.62f, 1f);
        SetRect(frame.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -(yTop + h * 0.5f)),
                new Vector2(1140f, h));

        Image fill = NewImage(card.transform, "Fill");
        fill.sprite = GetRoundedSprite();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.045f, 0.09f, 0.16f, 0.98f);
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f);
        frt.offsetMax = new Vector2(-3f, -3f);

        ClubCatalog catalog = ClubCatalog.Instance;
        AddCompetitionArt(card.transform, catalog != null ? catalog.TrophyFor(comp) : null,
                          new Vector2(75f, 0f), 112f, new Vector2(0f, 0.5f));

        Image status = MakePanel(card.transform, new Vector2(0f, 1f), new Vector2(250f, -30f),
                                 new Vector2(196f, 34f), unlocked ? Green : new Color(0.38f, 0.43f, 0.52f, 1f));
        status.raycastTarget = false;
        MakeText(status.transform, unlocked ? "AVAILABLE NOW" : "LOCKED", 16f,
                 new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(188f, 30f),
                 Color.white, TextAlignmentOptions.Center);
        TextMeshProUGUI heroTitle = MakeText(card.transform,
                 unlocked ? "YOUR CLUB ENTERS AUTOMATICALLY" : UnlockMessage(comp), 22f,
                 new Vector2(0f, 0.5f), new Vector2(378f, 10f), new Vector2(440f, 34f),
                 unlocked ? Color.white : new Color(1f, 0.77f, 0.42f, 1f), TextAlignmentOptions.Left);
        heroTitle.enableAutoSizing = true;
        heroTitle.fontSizeMin = 14f;
        heroTitle.fontSizeMax = 22f;
        MakeText(card.transform,
                 unlocked ? "One saved club. Nine AI rivals. A fresh draw begins when you press Start."
                          : "You can inspect every club and reward now. Match fixtures remain hidden until unlocked.",
                 14f, new Vector2(0f, 0.5f), new Vector2(378f, -26f), new Vector2(440f, 40f),
                 new Color(0.68f, 0.78f, 0.9f, 1f), TextAlignmentOptions.Left);

        Image myClub = MakePanel(card.transform, new Vector2(1f, 0.5f), new Vector2(-210f, 0f),
                                 new Vector2(382f, 112f), new Color(0.10f, 0.18f, 0.28f, 1f));
        myClub.raycastTarget = false;
        AddClubLogo(myClub.transform, PlayerTeamName(), new Vector2(62f, 0f), 86f,
                    new Vector2(0f, 0.5f), true);
        MakeText(myClub.transform, "MY CLUB", 13f, new Vector2(0f, 0.5f), new Vector2(234f, 21f),
                 new Vector2(232f, 22f), Gold, TextAlignmentOptions.Left);
        TextMeshProUGUI playerName = MakeText(myClub.transform, PlayerTeamName(), 22f,
                 new Vector2(0f, 0.5f), new Vector2(234f, -9f), new Vector2(232f, 42f),
                 Color.white, TextAlignmentOptions.Left);
        playerName.enableAutoSizing = true;
        playerName.fontSizeMin = 15f;
        playerName.fontSizeMax = 22f;
        return yTop + h;
    }

    void BuildClubGroupOverview(RectTransform content, int comp, int group, float x, float yTop, float h)
    {
        GameObject card = new GameObject(group == 0 ? "GroupAOverview" : "GroupBOverview");
        card.transform.SetParent(content, false);
        Image frame = card.AddComponent<Image>();
        frame.sprite = GetRoundedSprite();
        frame.type = Image.Type.Sliced;
        frame.color = group == 0 ? Blue : new Color(0.72f, 0.25f, 0.30f, 1f);
        SetRect(frame.rectTransform, new Vector2(0.5f, 1f), new Vector2(x, -(yTop + h * 0.5f)),
                new Vector2(558f, h));

        Image fill = NewImage(card.transform, "Fill");
        fill.sprite = GetRoundedSprite();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.06f, 0.115f, 0.19f, 0.98f);
        fill.raycastTarget = false;
        RectTransform fillRt = fill.rectTransform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(3f, 3f);
        fillRt.offsetMax = new Vector2(-3f, -3f);

        MakeText(card.transform, group == 0 ? "GROUP A" : "GROUP B", 23f, new Vector2(0f, 1f),
                 new Vector2(166f, -28f), new Vector2(260f, 34f), Color.white, TextAlignmentOptions.Left);
        MakeText(card.transform, "5 CLUBS", 13f, new Vector2(1f, 1f),
                 new Vector2(-94f, -28f), new Vector2(140f, 26f),
                 new Color(0.64f, 0.76f, 0.88f, 1f), TextAlignmentOptions.Right);

        IReadOnlyList<string> clubs = LeagueSeason.ClubsForGroup(comp, group);
        for (int i = 0; i < clubs.Count; i++)
        {
            bool playerClub = LeagueSeason.IsPlayerClubSlot(clubs[i]);
            string displayName = playerClub ? PlayerTeamName() : clubs[i];
            Image row = NewImage(card.transform, "Club_" + displayName);
            row.sprite = GetRoundedSprite();
            row.type = Image.Type.Sliced;
            row.color = playerClub ? new Color(0.56f, 0.42f, 0.08f, 0.96f)
                                   : new Color(0.025f, 0.06f, 0.115f, 0.88f);
            row.raycastTarget = false;
            SetRect(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -(82f + i * 52f)),
                    new Vector2(520f, 48f));

            if (playerClub)
            {
                Image stripe = NewImage(row.transform, "PlayerStripe");
                stripe.color = Gold;
                stripe.raycastTarget = false;
                SetRect(stripe.rectTransform, new Vector2(0f, 0.5f), new Vector2(3f, 0f),
                        new Vector2(6f, 38f));
            }
            AddClubLogo(row.transform, displayName, new Vector2(36f, 0f), 43f,
                        new Vector2(0f, 0.5f), playerClub);
            TextMeshProUGUI name = MakeText(row.transform, displayName, 18f,
                 new Vector2(0f, 0.5f), new Vector2(214f, 0f), new Vector2(300f, 38f),
                 Color.white, TextAlignmentOptions.Left);
            name.enableAutoSizing = true;
            name.fontSizeMin = 14f;
            name.fontSizeMax = 18f;
            if (playerClub)
            {
                Image tag = MakePanel(row.transform, new Vector2(1f, 0.5f), new Vector2(-60f, 0f),
                                      new Vector2(102f, 28f), Gold);
                tag.raycastTarget = false;
                MakeText(tag.transform, "YOUR CLUB", 12f, new Vector2(0.5f, 0.5f), Vector2.zero,
                         new Vector2(98f, 26f), new Color(0.08f, 0.10f, 0.14f, 1f),
                         TextAlignmentOptions.Center);
            }
        }
    }

    static string UnlockMessage(int comp)
    {
        if (comp == 1) return "LOCKED — FINISH 1ST IN DIVISION 1 TO UNLOCK";
        if (comp == 2) return "LOCKED — FINISH 1ST IN PREMIUM LEAGUE TO UNLOCK";
        if (comp == 3) return "LOCKED — FINISH 1ST IN CONTINENTAL CUP TO UNLOCK";
        return "AVAILABLE";
    }

    void StartChampionship(int competition)
    {
        if (!IsCompetitionUnlocked(competition)) return;
        LeagueSeason.StartNew(competition, PlayerTeamName());
        if (standingsOverlay == null) standingsOverlay = BuildScreenOverlay("Overlay_STANDINGS");
        RectTransform sheet = standingsOverlay.transform.Find("Sheet") as RectTransform;
        ClearChildren(sheet);
        compTab = 0;
        BuildStandingsContent(sheet);
        ShowOverlay(standingsOverlay);
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

    // =============================================================== match start

    // Pre-match PLAY: record the placeholder result and load the one match scene. (The SELECT
    // POOL step that used to sit between pre-match and the match is retired.)
    void StartMatch()
    {
        LeagueSeason s = LeagueSeason.Current;
        if (s == null || s.IsComplete || s.NextOpponent < 0 || !IsCompetitionUnlocked(s.competitionIndex)) return;

        MatchPresentationContext.SetFixture(s.competitionIndex, s.teams[s.PlayerIndex], s.teams[s.NextOpponent]);

        SceneManager.LoadScene(MatchScene);
    }

    void BuildStandingsContent(Transform sheet)
    {
        LeagueSeason s = LeagueSeason.Current;
        int comp = s != null ? Mathf.Clamp(s.competitionIndex, 0, CompNames.Length - 1) : Mathf.Clamp(competitionViewIndex, 0, CompNames.Length - 1);

        // Completion processing is idempotent and normally happens at the final whistle. Repeating
        // it here also self-heals a save made exactly between final-order creation and reward grant.
        if (s != null && s.IsComplete) s.TryGrantCompletionRewards();

        AddScreenBackground(sheet, 0.85f);
        MakeTopBar(sheet, CompNames[comp], () => HideOverlay(standingsOverlay));
        if (s == null)
        {
            BuildCompetitionOverview(sheet, comp);
            return;
        }
        if (s.IsComplete)
        {
            BuildFinalStandings(sheet, s, comp);
            BuildCompBottomBar(sheet, s, comp);
            return;
        }
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
            y += BuildGroupCard(content, s, g, y) + 10f; // tighter gap between Group A / Group B (was 16)
        y = BuildRoundSummary(content, s, y + 2f) + 10f;
        content.sizeDelta = new Vector2(0f, y);
    }

    // Shows the four scores simulated/played with the player's most recent round. Before round one,
    // the same card previews the opening fixtures without scores. Locked competitions never reach
    // this view, so their information-only screens still reveal no pairings.
    float BuildRoundSummary(RectTransform content, LeagueSeason s, float yTop)
    {
        int round = Mathf.Clamp(s.groupRound > 0 ? s.groupRound - 1 : 0, 0, LeagueSeason.GroupRounds - 1);
        bool results = s.groupRound > 0;
        const float h = 158f;

        Image card = NewImage(content, "RoundSummary");
        card.sprite = GetRoundedSprite();
        card.type = Image.Type.Sliced;
        card.color = new Color(0.08f, 0.15f, 0.24f, 0.96f);
        SetRect(card.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -(yTop + h * 0.5f)), new Vector2(1150f, h));

        MakeText(card.transform, "ROUND " + (round + 1) + (results ? " RESULTS" : " FIXTURES"), 19f,
                 new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(420f, 28f),
                 Gold, TextAlignmentOptions.Center);

        for (int group = 0; group < 2; group++)
        {
            float x = group == 0 ? -285f : 285f;
            MakeText(card.transform, group == 0 ? "GROUP A" : "GROUP B", 15f,
                     new Vector2(0.5f, 1f), new Vector2(x, -47f), new Vector2(520f, 22f),
                     Cyan, TextAlignmentOptions.Center);

            int row = 0;
            bool[] appears = new bool[LeagueSeason.GroupSize];
            foreach (LeagueSeason.Fixture fixture in s.groupFixtures)
            {
                if (fixture == null || fixture.group != group || fixture.round != round) continue;
                int localA = fixture.teamA - group * LeagueSeason.GroupSize;
                int localB = fixture.teamB - group * LeagueSeason.GroupSize;
                if (localA >= 0 && localA < appears.Length) appears[localA] = true;
                if (localB >= 0 && localB < appears.Length) appears[localB] = true;
                BuildRoundFixtureLine(card.transform, s, fixture, x, 80f + row * 30f);
                row++;
            }
            for (int i = 0; i < appears.Length; i++)
                if (!appears[i])
                {
                    int team = group * LeagueSeason.GroupSize + i;
                    BuildRoundByeLine(card.transform, s, team, x, 80f + row * 30f);
                    break;
                }
        }
        return yTop + h;
    }

    void BuildRoundFixtureLine(Transform parent, LeagueSeason s, LeagueSeason.Fixture fixture,
                               float x, float yFromTop)
    {
        Image row = NewImage(parent, "FixtureLine");
        row.sprite = GetRoundedSprite();
        row.type = Image.Type.Sliced;
        row.color = fixture.Has(s.PlayerIndex)
            ? new Color(0.45f, 0.34f, 0.08f, 0.90f)
            : new Color(0.025f, 0.065f, 0.12f, 0.86f);
        row.raycastTarget = false;
        SetRect(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(x, -yFromTop),
                new Vector2(522f, 27f));

        string left = s.teams[fixture.teamA];
        string right = s.teams[fixture.teamB];
        AddClubLogo(row.transform, left, new Vector2(22f, 0f), 23f,
                    new Vector2(0f, 0.5f), fixture.teamA == s.PlayerIndex);
        AddClubLogo(row.transform, right, new Vector2(-22f, 0f), 23f,
                    new Vector2(1f, 0.5f), fixture.teamB == s.PlayerIndex);
        TextMeshProUGUI leftText = MakeText(row.transform, left, 14f, new Vector2(0f, 0.5f),
                 new Vector2(124f, 0f), new Vector2(172f, 25f), Color.white,
                 TextAlignmentOptions.Left);
        TextMeshProUGUI rightText = MakeText(row.transform, right, 14f, new Vector2(1f, 0.5f),
                 new Vector2(-124f, 0f), new Vector2(172f, 25f), Color.white,
                 TextAlignmentOptions.Right);
        leftText.enableAutoSizing = rightText.enableAutoSizing = true;
        leftText.fontSizeMin = rightText.fontSizeMin = 10f;
        leftText.fontSizeMax = rightText.fontSizeMax = 14f;
        MakeText(row.transform,
                 fixture.played ? fixture.scoreA + "  –  " + fixture.scoreB : "VS",
                 fixture.played ? 16f : 13f, new Vector2(0.5f, 0.5f), Vector2.zero,
                 new Vector2(100f, 25f), fixture.played ? Gold : new Color(0.7f, 0.8f, 0.9f, 1f),
                 TextAlignmentOptions.Center);
    }

    void BuildRoundByeLine(Transform parent, LeagueSeason s, int team, float x, float yFromTop)
    {
        Image row = NewImage(parent, "ByeLine");
        row.sprite = GetRoundedSprite();
        row.type = Image.Type.Sliced;
        row.color = new Color(0.035f, 0.07f, 0.12f, 0.62f);
        row.raycastTarget = false;
        SetRect(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(x, -yFromTop),
                new Vector2(522f, 27f));
        AddClubLogo(row.transform, s.teams[team], new Vector2(160f, 0f), 23f,
                    new Vector2(0f, 0.5f), team == s.PlayerIndex);
        MakeText(row.transform, "BYE  •  " + s.teams[team], 13f, new Vector2(0.5f, 0.5f),
                 new Vector2(10f, 0f), new Vector2(310f, 24f),
                 new Color(0.66f, 0.76f, 0.86f, 1f), TextAlignmentOptions.Center);
    }

    void BuildFinalStandings(Transform sheet, LeagueSeason s, int comp)
    {
        MakeText(sheet, "FINAL STANDINGS", 28f, new Vector2(0.5f, 1f), new Vector2(0f, -112f),
                 new Vector2(700f, 40f), Gold, TextAlignmentOptions.Center);
        RectTransform content = MakeCompScroll(sheet);
        float yStart = BuildRewardsCard(content, comp, 6f) + 12f;
        yStart = BuildCompletionSummary(content, s, comp, yStart) + 18f;
        const float rowH = 60f, rowPitch = 66f;
        for (int rank = 0; rank < LeagueSeason.TeamCount; rank++)
        {
            int team = s.finalOrder != null && rank < s.finalOrder.Length ? s.finalOrder[rank] : -1;
            if (team < 0 || team >= s.teams.Length) continue;
            Image row = NewImage(content, "Final_" + (rank + 1));
            row.sprite = GetRoundedSprite(); row.type = Image.Type.Sliced;
            bool player = team == s.PlayerIndex;
            row.color = player ? new Color(0.58f, 0.43f, 0.08f, 0.96f)
                      : rank == 0 ? new Color(0.26f, 0.22f, 0.08f, 0.94f)
                      : new Color(0.045f, 0.09f, 0.16f, 0.94f);
            SetRect(row.rectTransform, new Vector2(0.5f, 1f),
                    new Vector2(0f, -(yStart + rowH * 0.5f + rank * rowPitch)),
                    new Vector2(1040f, rowH));
            if (player)
            {
                Image stripe = NewImage(row.transform, "PlayerStripe");
                stripe.color = Gold;
                stripe.raycastTarget = false;
                SetRect(stripe.rectTransform, new Vector2(0f, 0.5f), new Vector2(4f, 0f),
                        new Vector2(8f, 50f));
            }
            MakeText(row.transform, (rank + 1).ToString(), 23f, new Vector2(0f, 0.5f),
                     new Vector2(38f, 0f), new Vector2(54f, 42f),
                     rank < 3 ? Gold : Color.white, TextAlignmentOptions.Center);
            if (rank < 3)
                AddCompetitionArt(row.transform, ClubCatalog.Instance != null
                    ? ClubCatalog.Instance.MedalFor(rank + 1) : null,
                    new Vector2(82f, 0f), 38f, new Vector2(0f, 0.5f));
            AddClubLogo(row.transform, s.teams[team], new Vector2(130f, 0f), 50f,
                        new Vector2(0f, 0.5f), team == s.PlayerIndex);
            TextMeshProUGUI finalName = MakeText(row.transform, s.teams[team], 20f,
                     new Vector2(0f, 0.5f), new Vector2(465f, 0f), new Vector2(600f, 42f),
                     Color.white, TextAlignmentOptions.Left);
            finalName.enableAutoSizing = true;
            finalName.fontSizeMin = 15f;
            finalName.fontSizeMax = 20f;

            string note = rank == 0 && comp < 3 ? "PROMOTED"
                        : rank == 0 ? "CHAMPIONS"
                        : player ? "YOUR CLUB" : "";
            if (!string.IsNullOrEmpty(note))
            {
                Image tag = MakePanel(row.transform, new Vector2(1f, 0.5f), new Vector2(-116f, 0f),
                                      new Vector2(198f, 34f), rank == 0 ? Gold : Cyan);
                tag.raycastTarget = false;
                MakeText(tag.transform, note, 14f, new Vector2(0.5f, 0.5f), Vector2.zero,
                         new Vector2(190f, 30f), new Color(0.035f, 0.065f, 0.12f, 1f),
                         TextAlignmentOptions.Center);
            }
        }
        content.sizeDelta = new Vector2(0f, yStart + LeagueSeason.TeamCount * rowPitch + 12f);
    }

    float BuildCompletionSummary(RectTransform content, LeagueSeason s, int comp, float yTop)
    {
        const float h = 94f;
        int rank = System.Array.IndexOf(s.finalOrder, s.PlayerIndex) + 1;
        LeagueSeason.GetRewardForRank(comp, rank, out int gold, out int diamonds);

        Image card = NewImage(content, "EarnedReward");
        card.sprite = GetRoundedSprite();
        card.type = Image.Type.Sliced;
        card.color = rank == 1 ? new Color(0.32f, 0.25f, 0.07f, 0.98f)
                               : new Color(0.055f, 0.17f, 0.18f, 0.98f);
        SetRect(card.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -(yTop + h * 0.5f)), new Vector2(900f, h));

        string promotion = rank == 1 && comp < 3
            ? "UNLOCKED " + CompNames[comp + 1]
            : rank == 1 ? "WORLD CHAMPION"
            : "FINISH 1ST TO ADVANCE";

        AddClubLogo(card.transform, s.teams[s.PlayerIndex], new Vector2(50f, 0f), 68f,
                    new Vector2(0f, 0.5f), true);
        MakeText(card.transform, "YOUR RESULT", 12f, new Vector2(0f, 0.5f), new Vector2(173f, 20f),
                 new Vector2(150f, 20f), new Color(0.66f, 0.78f, 0.9f, 1f),
                 TextAlignmentOptions.Left);
        MakeText(card.transform, rank + GetOrdinal(rank) + " PLACE", 25f,
                 new Vector2(0f, 0.5f), new Vector2(188f, -10f), new Vector2(180f, 38f),
                 rank <= 3 ? Gold : Color.white, TextAlignmentOptions.Left);

        if (gold > 0)
        {
            MakeIcon(card.transform, "Sprites/gold-coin", new Vector2(0f, 0.5f),
                     new Vector2(324f, 8f), 32f);
            MakeText(card.transform, FormatCurrency(gold), 21f, new Vector2(0f, 0.5f),
                     new Vector2(392f, 8f), new Vector2(96f, 34f), Color.white,
                     TextAlignmentOptions.Left);
            MakeIcon(card.transform, "Sprites/diamond-coin", new Vector2(0f, 0.5f),
                     new Vector2(464f, 8f), 30f);
            MakeText(card.transform, FormatCurrency(diamonds), 21f, new Vector2(0f, 0.5f),
                     new Vector2(529f, 8f), new Vector2(82f, 34f), Color.white,
                     TextAlignmentOptions.Left);
            MakeText(card.transform, "REWARD EARNED", 11f, new Vector2(0f, 0.5f),
                     new Vector2(449f, -24f), new Vector2(250f, 20f),
                     new Color(0.66f, 0.78f, 0.9f, 1f), TextAlignmentOptions.Left);
        }
        else
        {
            MakeText(card.transform, "NO TOP-3 REWARD", 16f, new Vector2(0f, 0.5f),
                     new Vector2(449f, 0f), new Vector2(250f, 30f),
                     new Color(0.72f, 0.78f, 0.86f, 1f), TextAlignmentOptions.Left);
        }

        Image status = MakePanel(card.transform, new Vector2(1f, 0.5f), new Vector2(-145f, 0f),
                                 new Vector2(260f, 46f), rank == 1 ? Gold : new Color(0.10f, 0.48f, 0.60f, 1f));
        status.raycastTarget = false;
        TextMeshProUGUI statusText = MakeText(status.transform, promotion, 13f,
                 new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(246f, 40f),
                 rank == 1 ? new Color(0.04f, 0.07f, 0.12f, 1f) : Color.white,
                 TextAlignmentOptions.Center);
        statusText.enableAutoSizing = true;
        statusText.fontSizeMin = 10f;
        statusText.fontSizeMax = 13f;
        return yTop + h;
    }

    float BuildRewardsCard(RectTransform content, int comp, float yTop)
    {
        const float h = 174f;
        GameObject card = new GameObject("Rewards");
        card.transform.SetParent(content, false);
        Image bg = card.AddComponent<Image>();
        bg.sprite = GetRoundedSprite();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.085f, 0.16f, 0.25f, 0.98f);
        SetRect(bg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -(yTop + h * 0.5f)),
                new Vector2(1140f, h));

        Image topAccent = NewImage(card.transform, "TopAccent");
        topAccent.sprite = GetRoundedSprite();
        topAccent.type = Image.Type.Sliced;
        topAccent.color = Gold;
        topAccent.raycastTarget = false;
        SetRect(topAccent.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -3f),
                new Vector2(1100f, 6f));

        ClubCatalog catalog = ClubCatalog.Instance;
        AddCompetitionArt(card.transform, catalog != null ? catalog.TrophyFor(comp) : null,
                          new Vector2(72f, 12f), 104f, new Vector2(0f, 0.5f));
        MakeText(card.transform, "PRIZE", 13f, new Vector2(0f, 1f), new Vector2(186f, -31f),
                 new Vector2(120f, 22f), new Color(0.66f, 0.77f, 0.9f, 1f), TextAlignmentOptions.Left);
        MakeText(card.transform, "POOL", 25f, new Vector2(0f, 1f), new Vector2(186f, -58f),
                 new Vector2(120f, 34f), Color.white, TextAlignmentOptions.Left);

        Color[] rankColors =
        {
            Gold,
            new Color(0.78f, 0.84f, 0.92f, 1f),
            new Color(0.76f, 0.43f, 0.22f, 1f)
        };
        for (int i = 0; i < 3; i++)
        {
            int rank = i + 1;
            LeagueSeason.GetRewardForRank(comp, rank, out int gold, out int diamonds);
            GameObject tile = new GameObject("Reward_" + rank);
            tile.transform.SetParent(card.transform, false);
            Image tileFrame = tile.AddComponent<Image>();
            tileFrame.sprite = GetRoundedSprite();
            tileFrame.type = Image.Type.Sliced;
            tileFrame.color = rankColors[i];
            SetRect(tileFrame.rectTransform, new Vector2(0f, 0.5f),
                    new Vector2(338f + i * 270f, 8f), new Vector2(254f, 126f));

            Image tileFill = NewImage(tile.transform, "Fill");
            tileFill.sprite = GetRoundedSprite();
            tileFill.type = Image.Type.Sliced;
            tileFill.color = new Color(0.03f, 0.07f, 0.13f, 0.98f);
            tileFill.raycastTarget = false;
            RectTransform tfr = tileFill.rectTransform;
            tfr.anchorMin = Vector2.zero;
            tfr.anchorMax = Vector2.one;
            tfr.offsetMin = new Vector2(3f, 3f);
            tfr.offsetMax = new Vector2(-3f, -3f);

            AddCompetitionArt(tile.transform, catalog != null ? catalog.MedalFor(rank) : null,
                              new Vector2(35f, 32f), 48f, new Vector2(0f, 0.5f));
            MakeText(tile.transform, rank + GetOrdinal(rank) + " PLACE", 17f,
                     new Vector2(0f, 0.5f), new Vector2(150f, 38f), new Vector2(165f, 28f),
                     rankColors[i], TextAlignmentOptions.Left);

            MakeIcon(tile.transform, "Sprites/gold-coin", new Vector2(0f, 0.5f),
                     new Vector2(30f, -4f), 25f);
            MakeText(tile.transform, FormatCurrency(gold), 17f, new Vector2(0f, 0.5f),
                     new Vector2(88f, -4f), new Vector2(72f, 28f), Color.white,
                     TextAlignmentOptions.Left);
            MakeIcon(tile.transform, "Sprites/diamond-coin", new Vector2(0f, 0.5f),
                     new Vector2(142f, -4f), 24f);
            MakeText(tile.transform, FormatCurrency(diamonds), 17f, new Vector2(0f, 0.5f),
                     new Vector2(195f, -4f), new Vector2(62f, 28f), Color.white,
                     TextAlignmentOptions.Left);

            if (rank == 1 && comp < 3)
            {
                TextMeshProUGUI unlock = MakeText(tile.transform, "UNLOCKS " + CompNames[comp + 1], 11f,
                     new Vector2(0.5f, 0f), new Vector2(0f, 15f), new Vector2(224f, 24f),
                     Cyan, TextAlignmentOptions.Center);
                unlock.enableAutoSizing = true;
                unlock.fontSizeMin = 9f;
                unlock.fontSizeMax = 11f;
            }
        }
        return yTop + h;
    }

    // One framed five-club group table. Compact mode shows Pos | Team | Pts; details mode shows
    // the same five clubs with all statistical columns. Tapping the card toggles the presentation.
    // Returns the card height so the caller can stack the next one below.
    float BuildGroupCard(RectTransform content, LeagueSeason s, int g, float yTop)
    {
        bool expanded = compGroupExpanded[g];
        const float width = 1150f, headerH = 48f;
        float rowH = expanded ? 42f : 44f;
        float colHeadH = expanded ? 30f : 0f;
        int rows = expanded ? LeagueSeason.GroupSize : 5;
        float h = headerH + colHeadH + rows * rowH + 10f;

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
        MakeText(head.transform, g == 0 ? "GROUP A" : "GROUP B", 22f, new Vector2(0f, 0.5f),
                 new Vector2(160f, 0f), new Vector2(300f, 30f), Cyan, TextAlignmentOptions.Left);
        MakeText(head.transform, expanded ? "COMPACT VIEW" : "FULL TABLE", 14f, new Vector2(1f, 0.5f),
                 new Vector2(-95f, 0f), new Vector2(170f, 24f), Gold, TextAlignmentOptions.Right);

        List<int> order = s.GroupStandings(g);
        if (expanded)
            MakeGroupFullRow(cardGo.transform, -(headerH + colHeadH * 0.5f), rowH, true, false,
                             "POS", "TEAM", "P", "W", "D", "L", "GD", "PTS");
        for (int r = 0; r < rows; r++)
        {
            int ti = order[r];
            bool player = ti == s.PlayerIndex;
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
        float fs = header ? 13f : 18f;
        Vector2 box = new Vector2(60f, rowH);
        MakeText(row.transform, pos, fs, new Vector2(0.5f, 0.5f), new Vector2(-500f, 0f), box, col, TextAlignmentOptions.Center);
        if (!header) AddClubLogo(row.transform, name, new Vector2(-432f, 0f), rowH - 8f,
                                 new Vector2(0.5f, 0.5f), player);
        MakeText(row.transform, name, fs, new Vector2(0.5f, 0.5f), new Vector2(header ? -290f : -266f, 0f),
                 new Vector2(header ? 330f : 278f, rowH), col, TextAlignmentOptions.Left);
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
        MakeText(row.transform, pos, 18f, new Vector2(0.5f, 0.5f), new Vector2(-500f, 0f), box, Color.white, TextAlignmentOptions.Center);
        AddClubLogo(row.transform, name, new Vector2(-432f, 0f), rowH - 8f,
                    new Vector2(0.5f, 0.5f), player);
        TextMeshProUGUI clubName = MakeText(row.transform, name, 18f, new Vector2(0.5f, 0.5f),
                 new Vector2(-202f, 0f), new Vector2(394f, rowH), Color.white,
                 TextAlignmentOptions.Left);
        clubName.enableAutoSizing = true;
        clubName.fontSizeMin = 14f;
        clubName.fontSizeMax = 18f;
        MakeText(row.transform, pts, 19f, new Vector2(0.5f, 0.5f), new Vector2(485f, 0f), box,
                 player ? new Color(0.08f, 0.10f, 0.14f, 1f) : Gold,
                 TextAlignmentOptions.Center);
    }

    // ---- KNOCKOUT tab ----

    void BuildKnockoutTab(Transform sheet, LeagueSeason s)
    {
        RectTransform content = MakeCompScroll(sheet);
        float y = 2f;

        y = AddBracketLabel(content, "SEMIFINALS", y);
        for (int i = 0; i < 2; i++)
            BuildBracketCard(content, s, s.semifinals[i], new Vector2(i == 0 ? -297f : 297f, -(y + 36f)),
                             new Vector2(576f, 72f));
        y += 82f;

        y = AddBracketLabel(content, "PLACEMENT MATCHES", y);
        BuildBracketCard(content, s, s.placement5, new Vector2(-297f, -(y + 34f)), new Vector2(576f, 64f));
        BuildBracketCard(content, s, s.placement7, new Vector2(297f, -(y + 34f)), new Vector2(576f, 64f));
        BuildBracketCard(content, s, s.placement9, new Vector2(0f, -(y + 108f)), new Vector2(576f, 64f));
        y += 150f;

        y = AddBracketLabel(content, "THIRD PLACE", y);
        BuildBracketCard(content, s, s.thirdPlace, new Vector2(0f, -(y + 36f)), new Vector2(640f, 72f));
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
    void BuildBracketCard(RectTransform content, LeagueSeason s, LeagueSeason.Fixture m,
                          Vector2 center, Vector2 size)
    {
        if (m == null) return;
        bool mine = m.Has(s.PlayerIndex);
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

        float wing = size.x * 0.5f - 78f;
        float logoSize = Mathf.Min(42f, size.y - 16f);
        if (m.teamA >= 0)
            AddClubLogo(fill.transform, nameA, new Vector2(30f, 0f), logoSize,
                        new Vector2(0f, 0.5f), m.teamA == s.PlayerIndex);
        if (m.teamB >= 0)
            AddClubLogo(fill.transform, nameB, new Vector2(-30f, 0f), logoSize,
                        new Vector2(1f, 0.5f), m.teamB == s.PlayerIndex);

        float nameWidth = Mathf.Max(100f, wing - 50f);
        TextMeshProUGUI textA = MakeText(fill.transform, nameA, 17f, new Vector2(0f, 0.5f),
                 new Vector2(56f + nameWidth * 0.5f, 0f), new Vector2(nameWidth, size.y),
                 colA, TextAlignmentOptions.Left);
        TextMeshProUGUI textB = MakeText(fill.transform, nameB, 17f, new Vector2(1f, 0.5f),
                 new Vector2(-(56f + nameWidth * 0.5f), 0f), new Vector2(nameWidth, size.y),
                 colB, TextAlignmentOptions.Right);
        textA.enableAutoSizing = textB.enableAutoSizing = true;
        textA.fontSizeMin = textB.fontSizeMin = 12f;
        textA.fontSizeMax = textB.fontSizeMax = 17f;
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
            bool canRestart = s.PlayerMatchWins > 0;
            Button restart = MakeActionButton(
                bar.transform,
                canRestart ? "RESTART RUN" : "RESTART (WIN 1)",
                new Vector2(0f, 0.5f),
                new Vector2(130f, 0f),
                new Vector2(230f, 52f),
                canRestart ? new Color(0.80f, 0.22f, 0.20f, 1f)
                           : new Color(0.25f, 0.30f, 0.38f, 1f),
                canRestart ? () => OpenRestartChampionshipConfirmation(comp) : null);
            restart.interactable = canRestart;

            if (s.PlayerHasBye)
                MakeActionButton(bar.transform, "SIMULATE BYE ROUND", new Vector2(0.5f, 0.5f), new Vector2(-40f, 0f),
                                 new Vector2(560f, 64f), Green, () => { s.SimulateByeRound(); RebuildStandings(); });
            else
                MakeActionButton(bar.transform, "NEXT MATCH    vs. " + s.NextOpponentName,
                                 new Vector2(0.5f, 0.5f), new Vector2(-40f, 0f), new Vector2(560f, 64f),
                                 Green, OpenPreMatch);
        }
        else
        {
            int rank = System.Array.IndexOf(s.finalOrder, s.PlayerIndex) + 1;
            string status = rank == 1 && comp < 3 ? "PROMOTED TO " + CompNames[comp + 1] : rank == 1 ? "WORLD CHAMPIONS!" : "FINISHED " + rank + GetOrdinal(rank);
            MakeText(bar.transform, status, 20f, new Vector2(0.5f, 0.5f), new Vector2(-220f, 0f), new Vector2(450f, 60f), Gold, TextAlignmentOptions.Center);
            MakeActionButton(bar.transform, "PLAY CHAMPIONSHIP AGAIN", new Vector2(0.5f, 0.5f), new Vector2(270f, 0f),
                             new Vector2(360f, 60f), Green, () => ReplayChampionship(comp));
        }
    }

    void OpenRestartChampionshipConfirmation(int competition)
    {
        LeagueSeason s = LeagueSeason.Current;
        if (s == null || s.IsComplete || s.competitionIndex != competition || s.PlayerMatchWins < 1)
            return;

        if (restartChampionshipOverlay == null)
            restartChampionshipOverlay = BuildScreenOverlay("Overlay_RESTART_CHAMPIONSHIP");
        RectTransform sheet = restartChampionshipOverlay.transform.Find("Sheet") as RectTransform;
        ClearChildren(sheet);

        Image panel = MakePanel(sheet, new Vector2(0.5f, 0.5f), Vector2.zero,
                                new Vector2(700f, 340f), new Color(0.04f, 0.075f, 0.14f, 1f));
        panel.raycastTarget = true;

        Image accent = NewImage(panel.transform, "WarningAccent");
        accent.sprite = GetRoundedSprite();
        accent.type = Image.Type.Sliced;
        accent.color = new Color(0.86f, 0.25f, 0.20f, 1f);
        accent.raycastTarget = false;
        SetRect(accent.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -4f),
                new Vector2(650f, 8f));

        AddClubLogo(panel.transform, PlayerTeamName(), new Vector2(70f, -70f), 92f,
                    new Vector2(0f, 1f), true);
        MakeText(panel.transform, "RESTART CHAMPIONSHIP?", 30f,
                 new Vector2(0.5f, 1f), new Vector2(45f, -52f), new Vector2(520f, 44f),
                 Color.white, TextAlignmentOptions.Center);
        MakeText(panel.transform,
                 "All matches, scores and standings in " + CompNames[competition] + " will return to zero.",
                 17f, new Vector2(0.5f, 0.5f), new Vector2(0f, 34f),
                 new Vector2(610f, 52f), new Color(0.74f, 0.82f, 0.92f, 1f),
                 TextAlignmentOptions.Center);
        MakeText(panel.transform,
                 "Your Gold, Diamonds, unlocked competitions and previous rewards are kept. A fresh fixture calendar will be drawn.",
                 15f, new Vector2(0.5f, 0.5f), new Vector2(0f, -24f),
                 new Vector2(610f, 58f), Gold, TextAlignmentOptions.Center);

        MakeActionButton(panel.transform, "CANCEL", new Vector2(0.5f, 0f),
                         new Vector2(-166f, 52f), new Vector2(270f, 60f),
                         new Color(0.28f, 0.36f, 0.48f, 1f),
                         () => HideOverlay(restartChampionshipOverlay));
        MakeActionButton(panel.transform, "RESET TO 0 MATCHES", new Vector2(0.5f, 0f),
                         new Vector2(166f, 52f), new Vector2(310f, 60f),
                         new Color(0.82f, 0.22f, 0.18f, 1f),
                         () => ConfirmRestartChampionship(competition));
        ShowOverlay(restartChampionshipOverlay);
    }

    void ConfirmRestartChampionship(int competition)
    {
        if (!LeagueSeason.RestartCurrent(competition, PlayerTeamName())) return;
        HideOverlay(restartChampionshipOverlay);
        compTab = 0;
        compGroupExpanded[0] = compGroupExpanded[1] = false;
        RebuildStandings();
    }

    void ReplayChampionship(int competition)
    {
        if (!IsCompetitionUnlocked(competition)) return;
        LeagueSeason.ResetCompletedRun(competition);
        StartChampionship(competition);
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
        string playerName = s.teams[s.PlayerIndex];
        string oppName = opp >= 0 ? s.teams[opp] : "TBD";
        int oppStars = opp >= 0 ? s.stars[opp] : 3;

        const float poolW = 450f, poolH = 246f, poolY = 43f, poolX = 320f;
        BuildPreMatchSideCard(sheet, new Vector2(-poolX, -16f), new Vector2(492f, 492f),
                              Blue, "MY CLUB");
        BuildPreMatchSideCard(sheet, new Vector2(poolX, -16f), new Vector2(492f, 492f),
                              Red, "OPPONENT");
        BuildPreMatchPool(sheet, new Vector2(-poolX, poolY), new Vector2(poolW, poolH), true, Blue);
        BuildPreMatchPool(sheet, new Vector2(poolX, poolY), new Vector2(poolW, poolH), false, Red);

        // Center column: compact competition context, deliberate divider, VS badge and primary CTA.
        MakeText(sheet, CompNames[comp], 20f, new Vector2(0.5f, 0.5f), new Vector2(0f, 196f),
                 new Vector2(200f, 48f), Gold, TextAlignmentOptions.Center);
        MakeText(sheet, s.MatchLabel, 18f,
                 new Vector2(0.5f, 0.5f), new Vector2(0f, 157f), new Vector2(250f, 26f),
                 new Color(0.72f, 0.80f, 0.90f, 1f),
                 TextAlignmentOptions.Center);

        Image divider = NewImage(sheet, "FixtureDivider");
        divider.color = new Color(1f, 1f, 1f, 0.14f);
        divider.raycastTarget = false;
        SetRect(divider.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 42f), new Vector2(2f, 170f));

        Image vsFrame = MakePanel(sheet, new Vector2(0.5f, 0.5f), new Vector2(0f, 67f),
                                  new Vector2(94f, 70f), Gold);
        vsFrame.gameObject.name = "VsBadge";
        vsFrame.raycastTarget = false;
        Image vsFill = MakePanel(vsFrame.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                                 new Vector2(86f, 62f), new Color(0.035f, 0.07f, 0.13f, 1f));
        vsFill.raycastTarget = false;
        TextMeshProUGUI vsLabel = MakeText(sheet, "VS", 44f, new Vector2(0.5f, 0.5f),
                 new Vector2(0f, 67f), new Vector2(90f, 62f),
                 Color.white, TextAlignmentOptions.Center);
        MakeActionButton(sheet, "PLAY MATCH", new Vector2(0.5f, 0.5f),
                         new Vector2(0f, -68f), new Vector2(210f, 68f),
                         Green, StartMatch);

        // Below each pool: logo + name + star rating.
        RectTransform playerPanel = BuildTeamInfo(sheet, new Vector2(-poolX, -166f), playerName,
                                                  s.stars[s.PlayerIndex], Blue, true);
        RectTransform opponentPanel = BuildTeamInfo(sheet, new Vector2(poolX, -166f), oppName,
                                                    oppStars, Red, false);

        // The requested horizontal fixture beat: both real club identities slide in from opposite
        // sides, settle around VS, then remain as the normal pre-match display.
        FixtureIntroFX fixtureFx = sheet.GetComponent<FixtureIntroFX>();
        if (fixtureFx == null) fixtureFx = sheet.gameObject.AddComponent<FixtureIntroFX>();
        fixtureFx.Configure(playerPanel, opponentPanel, vsLabel.rectTransform);
    }

    void BuildPreMatchSideCard(Transform sheet, Vector2 center, Vector2 size, Color accentColor,
                               string label)
    {
        Image card = MakePanel(sheet, new Vector2(0.5f, 0.5f), center, size,
                               new Color(0.035f, 0.075f, 0.14f, 0.96f));
        card.gameObject.name = label == "MY CLUB" ? "PlayerFixtureCard" : "OpponentFixtureCard";
        card.raycastTarget = false;

        Image accent = NewImage(card.transform, "Accent");
        accent.sprite = GetRoundedSprite(); accent.type = Image.Type.Sliced;
        accent.color = accentColor; accent.raycastTarget = false;
        SetRect(accent.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, -5f), new Vector2(size.x - 24f, 7f));
        MakeText(card.transform, label, 13f, new Vector2(0.5f, 1f),
                 new Vector2(0f, -25f), new Vector2(220f, 22f),
                 new Color(accentColor.r, accentColor.g, accentColor.b, 1f),
                 TextAlignmentOptions.Center);
    }

    // Tactical position markers are functional; compact broadcast-style dots replace the old
    // large white debug-looking rectangles. Opponent formations mirror vertically.
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

        BuildFormationMarkers(pool.transform, size, isPlayer, color);
    }

    void BuildFormationMarkers(Transform pool, Vector2 size, bool isPlayer, Color teamColor)
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
            Image m = NewImage(pool, "PositionMarker_" + f.label);
            m.sprite = Circle();
            m.color = f.label == "GK" ? Gold : teamColor;
            m.raycastTarget = false;
            SetRect(m.rectTransform, new Vector2(0.5f, 0.5f),
                    new Vector2(f.fx * size.x, my * size.y), new Vector2(36f, 36f));
            Shadow markerShadow = m.gameObject.AddComponent<Shadow>();
            markerShadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            markerShadow.effectDistance = new Vector2(0f, -2f);

            Image inner = NewImage(m.transform, "Fill");
            inner.sprite = Circle();
            inner.color = new Color(0.035f, 0.08f, 0.14f, 0.94f);
            inner.raycastTarget = false;
            SetRect(inner.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(29f, 29f));
            MakeText(m.transform, f.label, 11f, new Vector2(0.5f, 0.5f), Vector2.zero,
                     new Vector2(34f, 30f), Color.white, TextAlignmentOptions.Center);
        }
    }

    RectTransform BuildTeamInfo(Transform sheet, Vector2 center, string name, int stars, Color color,
                                bool playerClub)
    {
        GameObject panel = new GameObject("FixtureClub_" + name);
        panel.transform.SetParent(sheet, false);
        RectTransform rt = panel.AddComponent<RectTransform>();
        SetRect(rt, new Vector2(0.5f, 0.5f), center, new Vector2(390f, 138f));
        panel.AddComponent<CanvasGroup>();

        AddClubLogo(panel.transform, name, new Vector2(0f, 31f), 78f,
                    new Vector2(0.5f, 0.5f), playerClub, true);
        TextMeshProUGUI teamName = MakeText(panel.transform, name, 22f, new Vector2(0.5f, 0.5f),
                 new Vector2(0f, -22f), new Vector2(360f, 32f), Color.white,
                 TextAlignmentOptions.Center);
        teamName.enableAutoSizing = true;
        teamName.fontSizeMin = 16f;
        teamName.fontSizeMax = 22f;
        MakeText(panel.transform, StarString(stars), 21f, new Vector2(0.5f, 0.5f), new Vector2(0f, -52f),
                 new Vector2(180f, 26f), Gold, TextAlignmentOptions.Center);
        return rt;
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
        TextMeshProUGUI coinText = MakeCurrencyChip(bar, "GoldChip", "Sprites/gold-coin", Gold,
                                                     new Vector2(CurrencyGoldX, 0f), () => OpenShopTab(5));
        TextMeshProUGUI diamondText = MakeCurrencyChip(bar, "DiamondChip", "Sprites/diamond-coin", Cyan,
                                                        new Vector2(CurrencyDiamondX, 0f), () => OpenShopTab(6));
        coinText.text = FormatCurrency(coins);
        diamondText.text = FormatCurrency(diamonds);
    }

    // A compact mobile-game currency component. The count lives inside a high-contrast pill rather
    // than floating beside an icon, and the plus button is part of the component's silhouette.
    TextMeshProUGUI MakeCurrencyChip(Transform parent, string name, string iconPath, Color accent,
                                    Vector2 position, UnityEngine.Events.UnityAction onPlus)
    {
        const float width = 164f, height = 50f;
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        RectTransform rt = root.AddComponent<RectTransform>();
        SetRect(rt, new Vector2(1f, 0.5f), position, new Vector2(width, height));

        Image shadow = NewImage(root.transform, "Shadow");
        shadow.sprite = GetRoundedSprite();
        shadow.type = Image.Type.Sliced;
        shadow.color = new Color(0f, 0f, 0f, 0.42f);
        shadow.raycastTarget = false;
        SetRect(shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -3f),
                new Vector2(width + 2f, height));

        Image frame = NewImage(root.transform, "Frame");
        frame.sprite = GetRoundedSprite();
        frame.type = Image.Type.Sliced;
        frame.color = accent;
        frame.raycastTarget = false;
        Stretch(frame.rectTransform);

        Image fill = NewImage(root.transform, "Fill");
        fill.sprite = GetRoundedSprite();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.035f, 0.065f, 0.12f, 0.98f);
        fill.raycastTarget = false;
        RectTransform fillRt = fill.rectTransform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(3f, 3f);
        fillRt.offsetMax = new Vector2(-3f, -3f);

        Image iconWell = NewImage(root.transform, "IconWell");
        // Keep the original 32px currency art, but remove the coloured circle/halo that made the
        // coin and diamond look as if they had an oversized yellow/blue outer layer.
        iconWell.color = Color.clear;
        iconWell.raycastTarget = false;
        SetRect(iconWell.rectTransform, new Vector2(0f, 0.5f), new Vector2(25f, 0f), new Vector2(40f, 40f));
        MakeIcon(iconWell.transform, iconPath, new Vector2(0.5f, 0.5f), Vector2.zero, 32f);

        TextMeshProUGUI value = MakeText(root.transform, "0", 19f, new Vector2(0.5f, 0.5f),
                                         new Vector2(6f, 0f), new Vector2(82f, 40f),
                                         Color.white, TextAlignmentOptions.Center);
        value.enableAutoSizing = true;
        value.fontSizeMin = 14f;
        value.fontSizeMax = 19f;

        GameObject plusGo = new GameObject("Plus");
        plusGo.transform.SetParent(root.transform, false);
        RectTransform plusRt = plusGo.AddComponent<RectTransform>();
        SetRect(plusRt, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(34f, 34f));
        Image plus = plusGo.AddComponent<Image>();
        plus.sprite = GetRoundedSprite();
        plus.type = Image.Type.Sliced;
        plus.color = accent;
        Button button = plusGo.AddComponent<Button>();
        button.targetGraphic = plus;
        if (onPlus != null) button.onClick.AddListener(onPlus);
        MakeText(plusGo.transform, "+", 25f, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f),
                 new Vector2(34f, 34f), new Color(0.035f, 0.065f, 0.12f, 1f), TextAlignmentOptions.Center);
        AddHover(plusGo);
        return value;
    }

    static string FormatCurrency(int value) =>
        Mathf.Max(0, value).ToString("N0", CultureInfo.InvariantCulture);

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
        img.color = new Color(0f, 0f, 0f, 0.52f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);

        Image face = NewImage(go.transform, "Face");
        face.sprite = GetRoundedSprite();
        face.type = Image.Type.Sliced;
        face.color = color;
        face.raycastTarget = false;
        SetRect(face.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 3f),
                new Vector2(size.x - 4f, size.y - 8f));

        Image shine = NewImage(face.transform, "TopShine");
        shine.sprite = GetRoundedSprite();
        shine.type = Image.Type.Sliced;
        shine.color = new Color(1f, 1f, 1f, 0.22f);
        shine.raycastTarget = false;
        SetRect(shine.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -4f),
                new Vector2(size.x - 24f, 5f));

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

    // The player's saved My Club identity is the human-controlled championship entry. TeamSide only
    // exists inside PoolB, so the hub reads the durable ClubProfile directly.
    static string PlayerTeamName()
    {
        ClubProfile club = RosterManager.Instance.Club;
        return club != null && !string.IsNullOrWhiteSpace(club.clubName) ? club.clubName.Trim() : "MY CLUB";
    }

    static string StarString(int n)
    {
        n = Mathf.Clamp(n, 0, 5);
        return new string('★', n) + new string('☆', 5 - n);
    }

    static string Signed(int v) => v > 0 ? "+" + v : v.ToString();
    static string GetOrdinal(int rank) => rank % 100 is 11 or 12 or 13 ? "TH" : rank % 10 == 1 ? "ST" : rank % 10 == 2 ? "ND" : rank % 10 == 3 ? "RD" : "TH";

    // `playerClub` is always supplied from the actual tournament slot; never infer it from the club
    // name because a saved name can collide with an official club (for example Dinamo).
    Image AddClubLogo(Transform parent, string club, Vector2 position, float size, Vector2 anchor,
                      bool playerClub = false, bool bare = false)
    {
        GameObject holder = new GameObject("Logo_" + club);
        holder.transform.SetParent(parent, false);
        RectTransform holderRt = holder.AddComponent<RectTransform>();
        SetRect(holderRt, anchor, position, new Vector2(size, size));

        // My Club renders bare everywhere. Official clubs retain the legacy fallback treatment
        // except where a screen explicitly requests direct/bare crests (for example pre-match).
        if (!playerClub && !bare)
        {
            Image shadow = NewImage(holder.transform, "Shadow");
            shadow.sprite = Circle();
            shadow.color = new Color(0f, 0f, 0f, 0.38f);
            shadow.raycastTarget = false;
            SetRect(shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -size * 0.045f),
                    new Vector2(size * 0.96f, size * 0.96f));

            Image rim = NewImage(holder.transform, "Rim");
            rim.sprite = Circle();
            rim.color = new Color(0.30f, 0.47f, 0.64f, 1f);
            rim.raycastTarget = false;
            SetRect(rim.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(size * 0.92f, size * 0.92f));

            Image plate = NewImage(holder.transform, "Plate");
            plate.sprite = Circle();
            plate.color = new Color(0.97f, 0.98f, 1f, 1f);
            plate.raycastTarget = false;
            SetRect(plate.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(size * 0.82f, size * 0.82f));
        }

        if (playerClub)
        {
            CrestTemplateView saved = CrestTemplateView.Create(holder.transform, "SavedClubCrest",
                new Vector2(size, size), new Vector2(0.5f, 0.5f), Vector2.zero);
            saved.SetIdentity(RosterManager.Instance.Club);
            return saved.MaskImage;
        }

        Image logo = NewImage(holder.transform, "Crest");
        ClubCatalog catalog = ClubCatalog.Instance;
        logo.sprite = catalog != null ? catalog.LogoFor(club) : null;
        logo.color = logo.sprite != null ? Color.white : new Color(0.15f, 0.23f, 0.32f, 1f);
        logo.preserveAspect = true;
        logo.raycastTarget = false;
        SetRect(logo.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(size * 0.94f, size * 0.94f));
        if (logo.sprite == null)
        {
            logo.gameObject.SetActive(false);
            MakeText(holder.transform, ClubInitials(club), size * 0.28f,
                     new Vector2(0.5f, 0.5f), Vector2.zero,
                     new Vector2(size * 0.70f, size * 0.70f),
                     new Color(0.10f, 0.17f, 0.25f, 1f), TextAlignmentOptions.Center);
        }
        return logo;
    }

    static string ClubInitials(string club)
    {
        if (string.IsNullOrWhiteSpace(club)) return "?";
        string compact = club.Replace("-", "").Replace(" ", "");
        return compact.Substring(0, Mathf.Min(2, compact.Length)).ToUpperInvariant();
    }

    Image AddCompetitionArt(Transform parent, Sprite sprite, Vector2 position, float size, Vector2 anchor)
    {
        GameObject holder = new GameObject("CompetitionArt");
        holder.transform.SetParent(parent, false);
        Image backing = holder.AddComponent<Image>();
        backing.sprite = Circle(); backing.color = Color.white; backing.raycastTarget = false;
        SetRect(backing.rectTransform, anchor, position, new Vector2(size, size));
        Image art = NewImage(holder.transform, "Sprite");
        art.sprite = sprite; art.preserveAspect = true; art.raycastTarget = false;
        art.color = sprite != null ? Color.white : new Color(0.15f, 0.23f, 0.32f, 1f);
        SetRect(art.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.86f, size * 0.86f));
        return art;
    }

    void ShowOverlay(GameObject overlay)
    {
        if (overlay == null) return;
        FinishSlide();
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        slideTarget = overlay;
        slideShowing = true;
        slideRoutine = overlay == gameModeOverlay
            ? StartCoroutine(RevealGameMode(true))   // fade backdrop + stagger the cards in
            : StartCoroutine(SlideOverlay(overlay, true));
    }

    void HideOverlay(GameObject overlay)
    {
        if (overlay == null) return;
        FinishSlide();
        slideTarget = overlay;
        slideShowing = false;
        slideRoutine = overlay == gameModeOverlay
            ? StartCoroutine(RevealGameMode(false))
            : StartCoroutine(SlideOverlay(overlay, false));
    }

    // Snap any in-flight overlay transition to its END state before starting a new one.
    // Previously Show/Hide just stopped the shared coroutine: interrupting a CLOSE this way
    // left that overlay active at near-zero alpha with a full-screen raycastTarget backdrop —
    // an invisible blocker that silently ate every hub tap (the "SEASON ENDS IN does nothing"
    // bug). Finalizing the old transition makes that impossible.
    void FinishSlide()
    {
        if (slideRoutine != null) { StopCoroutine(slideRoutine); slideRoutine = null; }
        if (slideTarget == null) return;
        CanvasGroup cg = slideTarget.GetComponent<CanvasGroup>();
        RectTransform sheet = slideTarget.transform.Find("Sheet") as RectTransform;
        if (slideShowing)
        {
            if (cg != null) cg.alpha = 1f;
            if (sheet != null) sheet.anchoredPosition = Vector2.zero;
        }
        else
        {
            if (cg != null) cg.alpha = 0f;
            slideTarget.SetActive(false);
        }
        slideTarget = null;
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
                           Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick,
                           bool trimArt = false)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, pos, size);

        Image img = go.AddComponent<Image>();
        img.sprite = trimArt ? LoadTrimmedSprite(spritePath) : LoadSprite(spritePath);
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

    Button MakeDirectImageButton(Transform parent, string name, Sprite sprite, Vector2 anchor,
                                 Vector2 pos, Vector2 size,
                                 UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, pos, size);

        Image image = go.AddComponent<Image>();
        image.sprite = sprite != null ? sprite : GetRoundedSprite();
        image.preserveAspect = true;
        if (sprite == null)
        {
            image.type = Image.Type.Sliced;
            image.color = DarkPanel;
        }
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        if (onClick != null) button.onClick.AddListener(onClick);
        AddHover(go);
        return button;
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

    // Minimal show/hide tooltip for the top-bar icons. Desktop: visible while the mouse hovers
    // (mouse pointerIds are negative). Touch: visible after a 0.4s press-and-hold, hidden again
    // on release/exit. Coexists with AddHover's EventTrigger — Unity delivers pointer events to
    // every handler component on the object.
    class IconTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                        IPointerDownHandler, IPointerUpHandler
    {
        public GameObject tooltip;
        const float HoldDelay = 0.4f;
        float pressedAt = -1f; // unscaled time of the current touch press; -1 = not pressed

        public void OnPointerEnter(PointerEventData e)
        {
            if (e.pointerId < 0 && tooltip != null) tooltip.SetActive(true);
        }

        public void OnPointerExit(PointerEventData e) => Hide();

        public void OnPointerDown(PointerEventData e)
        {
            if (e.pointerId >= 0) pressedAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData e) => Hide();

        void Update()
        {
            if (pressedAt >= 0f && Time.unscaledTime - pressedAt >= HoldDelay
                && tooltip != null && !tooltip.activeSelf)
                tooltip.SetActive(true);
        }

        void Hide()
        {
            pressedAt = -1f;
            if (tooltip != null) tooltip.SetActive(false);
        }
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

    // Alpha-trimmed sprite loader for hub button art — same approach as CardPack.TierArtSprite.
    // Loads the raw Texture2D (immune to sprite-slicing import modes) and crops the sprite rect
    // to the visible alpha bounding box, because the source PNGs carry wildly different amounts
    // of transparent padding (friends/clubs/settings art is only ~40-60% content; shop/ranking/
    // team ~75-90%) — untrimmed, the same RectTransform size renders visibly different buttons.
    // Requires isReadable: 1 in the .png.meta; an unreadable texture falls back untrimmed.
    static readonly Dictionary<string, Sprite> trimmedSpriteCache = new Dictionary<string, Sprite>();
    static Sprite LoadTrimmedSprite(string path)
    {
        if (trimmedSpriteCache.TryGetValue(path, out Sprite cached) && cached != null) return cached;
        Texture2D tex = Resources.Load<Texture2D>(path);
        if (tex == null) return LoadSprite(path); // missing entirely → the plain loader logs it
        Rect rect = new Rect(0f, 0f, tex.width, tex.height);
        try
        {
            Color32[] px = tex.GetPixels32();
            const byte cut = 40; // same threshold as CardPack: cuts faint outer glow, keeps art
            int minX = tex.width, minY = tex.height, maxX = -1, maxY = -1;
            for (int y = 0; y < tex.height; y++)
                for (int x = 0; x < tex.width; x++)
                    if (px[y * tex.width + x].a > cut)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            if (maxX > minX && maxY > minY)
                rect = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
        catch { /* texture not readable → keep the full frame (correct, just untrimmed) */ }
        Sprite sp = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        trimmedSpriteCache[path] = sp;
        return sp;
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
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
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
    public static Sprite MakeLockSprite() // public: SeasonPassUI/RankingUI reuse the padlock
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

// Short unscaled-time fixture presentation used by the procedural pre-match screen. It owns no
// tournament state and leaves the panels at their authored positions after the animation.
sealed class FixtureIntroFX : MonoBehaviour
{
    RectTransform playerPanel;
    RectTransform opponentPanel;
    RectTransform vsLabel;
    CanvasGroup playerGroup;
    CanvasGroup opponentGroup;
    CanvasGroup vsGroup;
    Vector2 playerTarget;
    Vector2 opponentTarget;
    Coroutine routine;
    bool configured;

    public void Configure(RectTransform player, RectTransform opponent, RectTransform vs)
    {
        playerPanel = player;
        opponentPanel = opponent;
        vsLabel = vs;
        playerGroup = player != null ? player.GetComponent<CanvasGroup>() : null;
        opponentGroup = opponent != null ? opponent.GetComponent<CanvasGroup>() : null;
        if (vs != null)
        {
            vsGroup = vs.GetComponent<CanvasGroup>();
            if (vsGroup == null) vsGroup = vs.gameObject.AddComponent<CanvasGroup>();
        }
        playerTarget = player != null ? player.anchoredPosition : Vector2.zero;
        opponentTarget = opponent != null ? opponent.anchoredPosition : Vector2.zero;
        configured = playerPanel != null && opponentPanel != null && vsLabel != null;
        if (isActiveAndEnabled) Begin();
    }

    void OnEnable()
    {
        if (configured) Begin();
    }

    void Begin()
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Play());
    }

    IEnumerator Play()
    {
        const float travel = 440f;
        const float delay = 0.08f;
        const float duration = 0.52f;

        playerPanel.anchoredPosition = playerTarget + Vector2.left * travel;
        opponentPanel.anchoredPosition = opponentTarget + Vector2.right * travel;
        playerGroup.alpha = opponentGroup.alpha = vsGroup.alpha = 0f;
        vsLabel.localScale = Vector3.one * 0.65f;

        float wait = 0f;
        while (wait < delay)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            playerPanel.anchoredPosition = Vector2.LerpUnclamped(playerTarget + Vector2.left * travel, playerTarget, ease);
            opponentPanel.anchoredPosition = Vector2.LerpUnclamped(opponentTarget + Vector2.right * travel, opponentTarget, ease);
            playerGroup.alpha = opponentGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.35f));
            float vsT = Mathf.Clamp01((t - 0.42f) / 0.40f);
            vsGroup.alpha = vsT;
            vsLabel.localScale = Vector3.one * Mathf.Lerp(0.65f, 1f, 1f - Mathf.Pow(1f - vsT, 3f));
            yield return null;
        }

        playerPanel.anchoredPosition = playerTarget;
        opponentPanel.anchoredPosition = opponentTarget;
        playerGroup.alpha = opponentGroup.alpha = vsGroup.alpha = 1f;
        vsLabel.localScale = Vector3.one;
        routine = null;
    }
}
