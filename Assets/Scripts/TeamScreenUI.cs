using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// The hub TEAM management screen (B12), rebuilt as a full-screen overlay panel and built entirely
// in code (no prefabs, no Inspector wiring) in the same style as NavigationManager. NavigationManager
// hosts it on a full-canvas sheet and calls Build(); everything dynamic is driven by RosterManager +
// RosterManager. The data layer is untouched — this only paints a new layout over the same
// SetStarter / GetStarters / GetOwnedPlayers calls.
//
// Layout: a background image, an 80px top bar (back / TEAM / team-strength / currencies), a 220px
// left tactics panel (TACTIC + stars + 3 sprite buttons), a centre pool with the 7-slot 2-3-1
// formation, and a 300px right panel (4 position tabs + a scrollable BENCH list).
public class TeamScreenUI : MonoBehaviour
{
    static readonly Color Navy     = new Color(0.05f, 0.1f, 0.25f, 0.92f);
    static readonly Color DarkBar  = new Color(0.04f, 0.06f, 0.13f, 0.86f);
    static readonly Color PanelDk  = new Color(0.03f, 0.05f, 0.12f, 0.82f);
    static readonly Color PoolBlue = new Color(0.07f, 0.20f, 0.36f, 0.95f);
    static readonly Color Gold     = new Color(1f, 0.82f, 0.2f);
    static readonly Color Cyan     = new Color(0f, 0.85f, 1f);
    static readonly Color Grey     = new Color(0.55f, 0.55f, 0.58f);
    static readonly Color SellRed  = new Color(0.82f, 0.28f, 0.28f);

    static Sprite roundedSprite, silhouetteSprite, circleSprite, starSprite, backSprite, backButtonSprite;

    // Normalised slot positions inside the pool (x: 0=left..1=right, y: 0=bottom..1=top). Index ==
    // starter slot == (int)position. Visual 2-3-1: CF top, LF/RF, LW/RW, CB, GK bottom near own goal.
    static readonly Vector2[] SlotFrac =
    {
        new Vector2(0.50f, 0.10f), // 0 GK  (bottom, own goal)
        new Vector2(0.50f, 0.28f), // 1 CB
        new Vector2(0.30f, 0.46f), // 2 LW
        new Vector2(0.70f, 0.46f), // 3 RW
        new Vector2(0.50f, 0.86f), // 4 CF  (top, enemy goal)
        new Vector2(0.22f, 0.66f), // 5 LF
        new Vector2(0.78f, 0.66f), // 6 RF
    };
    // Extra per-slot pixel offset layered on top of the fractional anchor to spread the cards apart:
    // +30px wider on the LW/RW and LF/RF wings (X). Whole formation shifted up 50px (Y) so GK clears
    // the bottom, plus per-slot nudges: GK +30, CB +20, LW/RW +15, LF/RF +10, CF 0.
    static readonly Vector2[] SlotOffset =
    {
        new Vector2(  0f,  -80f), // 0 GK  (bottom row)
        new Vector2(  0f,  -30f), // 1 CB
        new Vector2(-30f,   10f), // 2 LW
        new Vector2( 30f,   10f), // 3 RW
        new Vector2(  0f,   20f), // 4 CF  (top row)
        new Vector2(-30f,   40f), // 5 LF
        new Vector2( 30f,   40f), // 6 RF
    };
    static readonly string[] PosName = { "GK", "CB", "LW", "RW", "CF", "LF", "RF" };
    static readonly string[] TabLabel = { "WINGS", "CENTER", "DEFENSE", "GK" };
    static readonly string[] TabSprite =
        { "Sprites/wings-button", "Sprites/center-button", "Sprites/defender-button", "Sprites/goalkeeper-button" };

    private Transform root;
    private NavigationManager nav;
    private TextMeshProUGUI ovrText, goldText, diamondText;
    private RectTransform formationContainer; // holds the 7 formation cards
    private RectTransform listContent;        // scroll content: BENCH rows
    private GameObject comingSoonPanel;
    private readonly Image[] tabFaces = new Image[4]; // position tab Images; selected = gold tint, else faded

    private int selectedSlot = -1; // formation slot chosen for a swap, -1 = none
    private int activeTab = 0;     // 0 wings, 1 center, 2 defense, 3 goalkeeper

    public void Build(Transform parent, NavigationManager navigation)
    {
        root = parent;
        nav = navigation;

        BuildBackground();
        BuildTopBar();
        BuildLeftPanel();
        BuildCenterPool();
        BuildRightPanel();
        BuildComingSoon();

        SyncTabHighlight();
        Refresh();
    }

    // ----------------------------------------------------------------- background

    void BuildBackground()
    {
        Image bg = NewImage("Background", root);
        bg.sprite = LoadSprite("Sprites/team-page-backround"); // note: asset filename is misspelled
        bg.raycastTarget = true;                               // blocks clicks bleeding to the hub
        if (bg.sprite == null) bg.color = new Color(0.03f, 0.12f, 0.24f); // pool-blue fallback
        Stretch(bg.rectTransform);
    }

    // ----------------------------------------------------------------- top bar

    void BuildTopBar()
    {
        Image bar = PanelStretch(root, new Vector2(0f, 1f), new Vector2(1f, 1f),
                                 new Vector2(0f, -80f), Vector2.zero, DarkBar);
        bar.gameObject.name = "TopBar";
        Transform t = bar.transform;

        // Left: universal back-button sprite → close the team screen, return to hub.
        MakeBackButton(t, new Vector2(0f, 0.5f), new Vector2(46f, 0f), new Vector2(60f, 60f),
                       () => { if (nav != null) nav.CloseTeamScreen(); });

        // Centre: TEAM title.
        MakeText(t, "TEAM", 32f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 44f),
                 Color.white, TextAlignmentOptions.Center);

        // Right-of-currencies: TEAM STRENGTH label + OVR number.
        MakeText(t, "TEAM STRENGTH", 13f, new Vector2(1f, 0.5f), new Vector2(-300f, 13f),
                 new Vector2(170f, 18f), new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Right);
        ovrText = MakeText(t, "OVR 0", 20f, new Vector2(1f, 0.5f), new Vector2(-300f, -11f),
                           new Vector2(170f, 26f), Gold, TextAlignmentOptions.Right);

        // Far right: diamond icon + count | gold icon + count.
        MakeIcon(t, "Sprites/diamond-coin", new Vector2(1f, 0.5f), new Vector2(-178f, 0f), 26f);
        diamondText = MakeText(t, "0", 16f, new Vector2(1f, 0.5f), new Vector2(-132f, 0f),
                               new Vector2(56f, 28f), Cyan, TextAlignmentOptions.Right);
        MakeText(t, "|", 18f, new Vector2(1f, 0.5f), new Vector2(-100f, 0f), new Vector2(12f, 28f),
                 new Color(1f, 1f, 1f, 0.4f), TextAlignmentOptions.Center);
        MakeIcon(t, "Sprites/gold-coin", new Vector2(1f, 0.5f), new Vector2(-78f, 0f), 26f);
        goldText = MakeText(t, "0", 16f, new Vector2(1f, 0.5f), new Vector2(-34f, 0f),
                            new Vector2(60f, 28f), Gold, TextAlignmentOptions.Right);
    }

    // ----------------------------------------------------------------- left tactics panel

    void BuildLeftPanel()
    {
        Image panel = PanelStretch(root, new Vector2(0f, 0f), new Vector2(0f, 1f),
                                   Vector2.zero, new Vector2(220f, -80f), PanelDk);
        Transform p = panel.transform;

        MakeText(p, "TACTIC", 18f, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(200f, 22f),
                 new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center);
        MakeText(p, "2-3-1", 30f, new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(200f, 38f),
                 Gold, TextAlignmentOptions.Center);

        // Star rating: 1 filled + 4 empty, 24px each, centred.
        const float starSize = 24f, gap = 6f;
        float rowW = 5f * starSize + 4f * gap;
        for (int i = 0; i < 5; i++)
        {
            Image star = NewImage("Star", p);
            star.sprite = Star();
            star.raycastTarget = false;
            star.color = i == 0 ? Gold : new Color(1f, 1f, 1f, 0.18f);
            float sx = -rowW * 0.5f + starSize * 0.5f + i * (starSize + gap);
            SetRect(star.rectTransform, new Vector2(0.5f, 1f), new Vector2(sx, -104f),
                    new Vector2(starSize, starSize));
        }

        // Three stacked sprite buttons, lowered to leave empty space above for future content.
        // FORMATIONS is bigger (220x80); the other two stay 200x70. All → COMING SOON.
        MakeSpriteLabelButton(p, "Sprites/formations-button", "FORMATIONS", new Vector2(0f, 20f), new Vector2(220f, 80f), ShowComingSoon);
        MakeSpriteLabelButton(p, "Sprites/players-button", "PLAYERS", new Vector2(0f, -55f), new Vector2(200f, 70f), ShowComingSoon);
        MakeSpriteLabelButton(p, "Sprites/substitutions-button", "SUBSTITUTIONS", new Vector2(0f, -130f), new Vector2(200f, 70f), ShowComingSoon);
    }

    void MakeSpriteLabelButton(Transform parent, string spritePath, string label, Vector2 pos,
                               Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, new Vector2(0.5f, 0.5f), pos, size);

        Image img = go.AddComponent<Image>();
        img.sprite = LoadSprite(spritePath);
        img.preserveAspect = true;
        if (img.sprite == null) { img.sprite = Rounded(); img.type = Image.Type.Sliced; img.color = Navy; }

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);

        // No text label drawn on top — the button sprites already have their text baked in.
        AddHover(go);
    }

    // ----------------------------------------------------------------- centre pool + formation

    void BuildCenterPool()
    {
        Image pool = PanelStretch(root, new Vector2(0f, 0f), new Vector2(1f, 1f),
                                  new Vector2(232f, 12f), new Vector2(-312f, -92f), PoolBlue);
        Transform p = pool.transform;

        MakeGoalNet(p, top: true);   // enemy goal (top)
        MakeGoalNet(p, top: false);  // own goal (bottom)

        formationContainer = NewRect("Formation", p);
        formationContainer.anchorMin = Vector2.zero;
        formationContainer.anchorMax = Vector2.one;
        formationContainer.offsetMin = new Vector2(8f, 40f);  // inset so cards clear the goals
        formationContainer.offsetMax = new Vector2(-8f, -40f);
    }

    void MakeGoalNet(Transform parent, bool top)
    {
        Image frame = NewImage("Goal", parent);
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced;
        frame.color = new Color(1f, 1f, 1f, 0.5f);
        frame.raycastTarget = false;
        SetRect(frame.rectTransform, new Vector2(0.5f, top ? 1f : 0f),
                new Vector2(0f, top ? -16f : 16f), new Vector2(150f, 24f));

        Image fill = NewImage("Mouth", frame.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced;
        fill.color = new Color(0.04f, 0.10f, 0.20f, 0.7f);
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(2f, 2f); frt.offsetMax = new Vector2(-2f, -2f);
    }

    void RefreshFormation()
    {
        ClearChildren(formationContainer);
        PlayerData[] starters = RosterManager.Instance.GetStarters();
        for (int slot = 0; slot < 7; slot++) BuildFormationCard(slot, starters[slot]);
    }

    void BuildFormationCard(int slot, PlayerData player)
    {
        bool selected = slot == selectedSlot;
        Color border = selected ? Gold : (player != null ? player.RarityColor : Grey);

        Image frame = NewImage("Slot" + slot, formationContainer);
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced; frame.color = border;
        RectTransform frt = frame.rectTransform;
        frt.anchorMin = frt.anchorMax = SlotFrac[slot];
        frt.pivot = new Vector2(0.5f, 0.5f);
        frt.anchoredPosition = SlotOffset[slot];
        frt.sizeDelta = new Vector2(75f, 90f);

        Image fill = NewImage("Fill", frame.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced; fill.color = Navy;
        fill.raycastTarget = false;
        RectTransform fillRt = fill.rectTransform;
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
        float inset = selected ? 4f : 3f;
        fillRt.offsetMin = new Vector2(inset, inset); fillRt.offsetMax = new Vector2(-inset, -inset);

        MakeText(frame.transform, PosName[slot], 14f, new Vector2(0.5f, 1f), new Vector2(0f, -12f),
                 new Vector2(86f, 18f), Cyan, TextAlignmentOptions.Center);

        MakePortrait(frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), 38f, player);

        if (player == null)
        {
            MakeText(frame.transform, "EMPTY", 13f, new Vector2(0.5f, 0f), new Vector2(0f, 18f),
                     new Vector2(86f, 18f), Grey, TextAlignmentOptions.Center);
        }
        else
        {
            MakeText(frame.transform, Short(player.fullName, 11), 10f, new Vector2(0.5f, 0f),
                     new Vector2(0f, 26f), new Vector2(86f, 16f), Color.white, TextAlignmentOptions.Center);
            MakeText(frame.transform, "OVR " + player.overall, 10f, new Vector2(0.5f, 0f),
                     new Vector2(0f, 10f), new Vector2(86f, 16f), Gold, TextAlignmentOptions.Center);
        }

        int captured = slot;
        Button btn = frame.gameObject.AddComponent<Button>();
        btn.targetGraphic = frame;
        btn.onClick.AddListener(() => OnSlotClicked(captured));
        AddHover(frame.gameObject);
    }

    void OnSlotClicked(int slot)
    {
        selectedSlot = slot;
        activeTab = CategoryOf((PlayerPosition)slot); // jump to the tab that can fill this slot
        SyncTabHighlight();
        RefreshFormation();
        RefreshList();
    }

    // ----------------------------------------------------------------- right panel (tabs + list)

    void BuildRightPanel()
    {
        Image panel = PanelStretch(root, new Vector2(1f, 0f), new Vector2(1f, 1f),
                                   new Vector2(-300f, 0f), new Vector2(0f, -80f), PanelDk);
        Transform p = panel.transform;

        // Four position tabs in a fixed-size row anchored to the panel's top-right.
        for (int i = 0; i < 4; i++)
            MakeTabButton(p, i);

        // Scrollable BENCH list filling the rest of the panel.
        listContent = BuildScroll(p);
    }

    void MakeTabButton(Transform parent, int index)
    {
        // Fixed 70x45 tabs (80x50 would overflow: 4*80+3*8=344 > 300px panel), in a horizontal
        // row anchored to the panel's top-right with an 8px gap. NO localScale on these — ever.
        const float w = 70f, h = 45f, gap = 8f, rightPad = 8f, topPad = 8f;

        GameObject go = new GameObject("Tab_" + TabLabel[index]);
        go.transform.SetParent(parent, false); // child of the right panel, not the root canvas
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        int columnFromRight = (TabLabel.Length - 1) - index; // index 0 = leftmost, 3 = rightmost
        rt.anchoredPosition = new Vector2(-(rightPad + columnFromRight * (w + gap)), -topPad);
        rt.sizeDelta = new Vector2(w, h);

        // The Image component IS the button — no background panel, no outline behind it.
        Image face = go.AddComponent<Image>();
        face.sprite = LoadSprite(TabSprite[index]);
        face.preserveAspect = true;
        if (face.sprite == null) // labelled fallback if a tab sprite is missing
        {
            face.sprite = Rounded(); face.type = Image.Type.Sliced;
            MakeText(go.transform, TabLabel[index], 11f, new Vector2(0.5f, 0.5f), Vector2.zero,
                     Vector2.zero, Color.white, TextAlignmentOptions.Center);
        }
        tabFaces[index] = face;

        int captured = index;
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = face;
        btn.onClick.AddListener(() => OnTabClicked(captured));
    }

    void OnTabClicked(int index)
    {
        activeTab = index;
        SyncTabHighlight();
        RefreshList();
    }

    void SyncTabHighlight()
    {
        for (int i = 0; i < tabFaces.Length; i++)
        {
            Image face = tabFaces[i];
            if (face == null) continue;
            // Color tint only — no scale, no border, no background panel.
            face.color = (i == activeTab) ? new Color(1f, 0.85f, 0.2f, 1f)  // selected: gold tint
                                          : new Color(1f, 1f, 1f, 0.6f);     // unselected: faded white
        }
    }

    // ----------------------------------------------------------------- refresh

    // Rebuild every dynamic part from the current roster + catalog.
    void Refresh()
    {
        RosterManager rm = RosterManager.Instance;
        ovrText.text = "OVR " + rm.TeamOverall();
        goldText.text = rm.Coins.ToString();
        diamondText.text = rm.Diamonds.ToString();

        RefreshFormation();
        RefreshList();

        if (nav != null) nav.RefreshCurrency();
    }

    void RefreshList()
    {
        ClearChildren(listContent);
        RosterManager rm = RosterManager.Instance;

        // BENCH: owned players not currently in a starter slot, filtered to the active tab.
        AddSectionHeader("BENCH");
        int benchShown = 0;
        HashSet<string> seen = new HashSet<string>();
        foreach (PlayerData p in rm.GetOwnedPlayers())
        {
            if (p == null || !seen.Add(p.id)) continue;
            if (rm.IsStarter(p.id) || CategoryOf(p.position) != activeTab) continue;
            BuildBenchRow(p);
            benchShown++;
        }
        if (benchShown == 0) AddEmptyNote("No bench players for this tab.");
    }

    void BuildBenchRow(PlayerData player)
    {
        string id = player.id; // local copy for the button closures
        RosterManager rm = RosterManager.Instance;

        RectTransform row = MakeRowCard(player.RarityColor, 74f);
        MakePortrait(row, new Vector2(0f, 0.5f), new Vector2(28f, 0f), 42f, player);
        MakeText(row, Short(player.fullName, 14), 14f, new Vector2(0f, 0.5f), new Vector2(52f, 16f),
                 new Vector2(118f, 18f), Color.white, TextAlignmentOptions.Left);
        MakeText(row, PosName[(int)player.position] + "  ·  OVR " + player.overall, 11f,
                 new Vector2(0f, 0.5f), new Vector2(52f, -6f), new Vector2(118f, 16f),
                 new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.Left);
        MakeRarityDot(row, player);

        // START → put this owned player into the selected slot (or its natural slot if none chosen).
        MakeButton(row, "START", new Vector2(80f, 32f), new Vector2(1f, 0.5f), new Vector2(-46f, 0f),
                   14f, () => { rm.SetStarter(StartSlotFor(player), id); Refresh(); }, Cyan);
    }

    // Where a START click sends an owned player: the slot the user highlighted, else its natural slot.
    int StartSlotFor(PlayerData player)
        => selectedSlot >= 0 ? selectedSlot : (int)player.position;

    void MakeRarityDot(Transform parent, PlayerData player)
    {
        Image dot = NewImage("Rarity", parent);
        dot.sprite = Circle(); dot.color = player.RarityColor; dot.raycastTarget = false;
        SetRect(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(176f, 0f), new Vector2(12f, 12f));
    }

    // ----------------------------------------------------------------- scroll list scaffolding

    RectTransform BuildScroll(Transform parent)
    {
        GameObject scrollGo = new GameObject("BenchMarketScroll");
        scrollGo.transform.SetParent(parent, false);
        RectTransform srt = scrollGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 1f);
        srt.offsetMin = new Vector2(8f, 8f); srt.offsetMax = new Vector2(-8f, -72f); // below the tabs
        Image bg = scrollGo.AddComponent<Image>();
        bg.sprite = Rounded(); bg.type = Image.Type.Sliced; bg.color = new Color(0f, 0f, 0f, 0.25f);

        ScrollRect sr = scrollGo.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true; sr.scrollSensitivity = 24f;
        sr.movementType = ScrollRect.MovementType.Clamped;

        GameObject vp = new GameObject("Viewport");
        vp.transform.SetParent(scrollGo.transform, false);
        RectTransform vrt = vp.AddComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(4f, 4f); vrt.offsetMax = new Vector2(-4f, -4f);
        vrt.pivot = new Vector2(0.5f, 1f);
        Image vpImg = vp.AddComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.01f); // near-invisible raycast target so empty space scrolls
        vp.AddComponent<RectMask2D>();
        sr.viewport = vrt;

        GameObject content = new GameObject("Content");
        content.transform.SetParent(vp.transform, false);
        RectTransform crt = content.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.offsetMin = crt.offsetMax = Vector2.zero;
        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f; vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        sr.content = crt;

        return crt;
    }

    void AddSectionHeader(string label)
    {
        GameObject go = new GameObject("Header_" + label);
        go.transform.SetParent(listContent, false);
        go.AddComponent<RectTransform>();
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minHeight = 26f; le.preferredHeight = 26f; le.flexibleWidth = 1f;
        MakeText(go.transform, label, 16f, new Vector2(0f, 0.5f), new Vector2(10f, 0f),
                 new Vector2(240f, 22f), Cyan, TextAlignmentOptions.Left);
    }

    void AddEmptyNote(string text)
    {
        GameObject go = new GameObject("Empty");
        go.transform.SetParent(listContent, false);
        go.AddComponent<RectTransform>();
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minHeight = 30f; le.preferredHeight = 30f; le.flexibleWidth = 1f;
        MakeText(go.transform, text, 12f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260f, 26f),
                 new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.Center);
    }

    // A rarity-bordered list row (border frame + navy fill) sized by the vertical layout group.
    RectTransform MakeRowCard(Color border, float height)
    {
        Image frame = NewImage("Row", listContent);
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced; frame.color = border;
        SetRect(frame.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, height));
        LayoutElement le = frame.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height; le.preferredHeight = height; le.flexibleWidth = 1f;

        Image fill = NewImage("Fill", frame.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced; fill.color = Navy;
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);

        return frame.rectTransform;
    }

    // ----------------------------------------------------------------- COMING SOON placeholder

    void BuildComingSoon()
    {
        comingSoonPanel = new GameObject("ComingSoon");
        comingSoonPanel.transform.SetParent(root, false);
        RectTransform rt = comingSoonPanel.AddComponent<RectTransform>();
        Stretch(rt);
        Image backdrop = comingSoonPanel.AddComponent<Image>();
        backdrop.color = new Color(0.01f, 0.02f, 0.05f, 0.8f);
        backdrop.raycastTarget = true;

        Image sheet = NewImage("Sheet", comingSoonPanel.transform);
        sheet.sprite = Rounded(); sheet.type = Image.Type.Sliced; sheet.color = PanelDk;
        SetRect(sheet.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 240f));
        MakeText(sheet.transform, "COMING SOON", 40f, new Vector2(0.5f, 0.5f), new Vector2(0f, 8f),
                 new Vector2(480f, 60f), Gold, TextAlignmentOptions.Center);
        MakeText(sheet.transform, "This feature is on the way.", 16f, new Vector2(0.5f, 0f),
                 new Vector2(0f, 40f), new Vector2(480f, 24f), Color.white, TextAlignmentOptions.Center);
        MakeButton(sheet.transform, "X", new Vector2(44f, 44f), new Vector2(1f, 1f), new Vector2(-28f, -28f),
                   22f, HideComingSoon, SellRed);

        comingSoonPanel.SetActive(false);
    }

    void ShowComingSoon()
    {
        if (comingSoonPanel == null) return;
        comingSoonPanel.SetActive(true);
        comingSoonPanel.transform.SetAsLastSibling();
    }

    void HideComingSoon()
    {
        if (comingSoonPanel != null) comingSoonPanel.SetActive(false);
    }

    // ----------------------------------------------------------------- shared UI helpers

    static int CategoryOf(PlayerPosition pos) => pos switch
    {
        PlayerPosition.GK => 3,
        PlayerPosition.CB => 2,
        PlayerPosition.CF => 1,
        _ => 0, // LW, RW, LF, RF → attack
    };

    static void ClearChildren(Transform t)
    {
        if (t == null) return;
        for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    Image NewImage(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<Image>();
    }

    // An anchored rounded panel (stretches between aMin/aMax with the given edge offsets).
    Image PanelStretch(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color color)
    {
        Image img = NewImage("Panel", parent);
        img.sprite = Rounded(); img.type = Image.Type.Sliced; img.color = color;
        RectTransform rt = img.rectTransform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
        return img;
    }

    Image MakeIcon(Transform parent, string spritePath, Vector2 anchor, Vector2 pos, float size)
    {
        Image img = NewImage("Icon", parent);
        img.sprite = LoadSprite(spritePath);
        img.preserveAspect = true; img.raycastTarget = false;
        if (img.sprite == null) img.color = Gold;
        SetRect(img.rectTransform, anchor, pos, new Vector2(size, size));
        return img;
    }

    // A grey circle portrait placeholder with a head-and-shoulders silhouette (or real art if present).
    void MakePortrait(Transform parent, Vector2 anchor, Vector2 pos, float size, PlayerData player)
    {
        Image circle = NewImage("Portrait", parent);
        circle.sprite = Circle(); circle.color = new Color(0.30f, 0.34f, 0.42f, 1f);
        circle.raycastTarget = false;
        SetRect(circle.rectTransform, anchor, pos, new Vector2(size, size));

        Image silo = NewImage("Silo", circle.transform);
        silo.sprite = (player != null && player.portrait != null) ? player.portrait : Silhouette();
        silo.preserveAspect = true; silo.raycastTarget = false;
        RectTransform s = silo.rectTransform;
        s.anchorMin = Vector2.zero; s.anchorMax = Vector2.one;
        s.offsetMin = new Vector2(4f, 2f); s.offsetMax = new Vector2(-4f, -2f);
    }

    TextMeshProUGUI MakeText(Transform parent, string content, float size, Vector2 anchor,
                             Vector2 pos, Vector2 box, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = content; txt.fontSize = size; txt.fontStyle = FontStyles.Bold;
        txt.color = color; txt.alignment = align; txt.raycastTarget = false;
        txt.overflowMode = TextOverflowModes.Ellipsis;
        SetRect(txt.rectTransform, anchor, pos, box);
        return txt;
    }

    // Cyan-bordered navy button (border tint overridable) with a 1.05x hover.
    Button MakeButton(Transform parent, string label, Vector2 size, Vector2 anchor, Vector2 pos,
                      float fontSize, UnityEngine.Events.UnityAction onClick, Color border)
    {
        GameObject go = new GameObject("Btn");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, pos, size);

        Image frame = go.AddComponent<Image>();
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced; frame.color = border;

        Image fill = NewImage("Fill", go.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced; fill.color = new Color(0.05f, 0.1f, 0.25f, 1f);
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = frame;
        if (onClick != null) btn.onClick.AddListener(onClick);

        TextMeshProUGUI txt = MakeText(go.transform, label, fontSize, new Vector2(0.5f, 0.5f),
                                       Vector2.zero, Vector2.zero, Color.white, TextAlignmentOptions.Center);
        Stretch(txt.rectTransform);

        AddHover(go);
        return btn;
    }

    // A rounded button whose face is a generated/loaded icon sprite (e.g. the back arrow).
    Button MakeIconButton(Transform parent, Sprite icon, Vector2 anchor, Vector2 pos, Vector2 size,
                          UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("BtnIcon");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, pos, size);

        Image frame = go.AddComponent<Image>();
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced; frame.color = Cyan;

        Image fill = NewImage("Fill", go.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced; fill.color = new Color(0.05f, 0.1f, 0.25f, 1f);
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);

        Image glyph = NewImage("Glyph", go.transform);
        glyph.sprite = icon; glyph.color = Color.white; glyph.raycastTarget = false; glyph.preserveAspect = true;
        RectTransform grt = glyph.rectTransform;
        grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
        grt.offsetMin = new Vector2(12f, 12f); grt.offsetMax = new Vector2(-12f, -12f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = frame;
        if (onClick != null) btn.onClick.AddListener(onClick);
        AddHover(go);
        return btn;
    }

    // The universal back button: the shared back-button sprite at native aspect (no frame). Falls back
    // to the procedural arrow if the sprite is missing.
    Button MakeBackButton(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                          UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("BtnBack");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, pos, size);

        Image img = go.AddComponent<Image>();
        img.sprite = BackButtonSprite();
        img.preserveAspect = true;
        if (img.sprite == null) img.sprite = BackArrow(); // procedural fallback

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);
        AddHover(go);
        return btn;
    }

    // back-button art wrapped from its Texture2D so it loads regardless of the PNG's sprite import mode.
    static Sprite BackButtonSprite()
    {
        if (backButtonSprite != null) return backButtonSprite;
        Texture2D tex = Resources.Load<Texture2D>("Sprites/back-button");
        if (tex == null) return null;
        backButtonSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        return backButtonSprite;
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

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static string Short(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max));

    static Sprite LoadSprite(string path)
    {
        Sprite s = Resources.Load<Sprite>(path);
        if (s == null)
            Debug.LogWarning("TeamScreenUI: sprite not found at Resources/" + path +
                             " — check the file exists there and its Texture Type is 'Sprite (2D and UI)'.");
        return s;
    }

    // ----------------------------------------------------------------- generated sprites

    static Sprite Rounded()
    {
        if (roundedSprite != null) return roundedSprite;
        const int size = 64, corner = 14;
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
        tex.SetPixels32(px); tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
        roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                      100f, 0, SpriteMeshType.FullRect,
                                      new Vector4(corner + 2, corner + 2, corner + 2, corner + 2));
        return roundedSprite;
    }

    static Sprite Circle()
    {
        if (circleSprite != null) return circleSprite;
        const int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[s * s];
        float r = s * 0.5f - 1f;
        Vector2 c = new Vector2(s * 0.5f - 0.5f, s * 0.5f - 0.5f);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                px[y * s + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255f));
            }
        tex.SetPixels32(px); tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
        circleSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        return circleSprite;
    }

    // A white, tintable five-point star (point-in-polygon fill).
    static Sprite Star()
    {
        if (starSprite != null) return starSprite;
        const int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[s * s];
        Vector2 c = new Vector2(s * 0.5f, s * 0.5f);
        float outer = s * 0.47f, inner = outer * 0.42f;
        Vector2[] pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float ang = Mathf.PI * 0.5f + i * Mathf.PI / 5f; // first point straight up
            float rad = (i % 2 == 0) ? outer : inner;
            pts[i] = c + new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
        }
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool inside = PointInPoly(new Vector2(x + 0.5f, y + 0.5f), pts);
                px[y * s + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
        tex.SetPixels32(px); tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
        starSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        return starSprite;
    }

    // A white, tintable left-pointing arrow (triangle head + shaft) for the back button.
    static Sprite BackArrow()
    {
        if (backSprite != null) return backSprite;
        const int s = 64;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[s * s];
        Vector2[] head =
        {
            new Vector2(0.22f * s, 0.50f * s),
            new Vector2(0.56f * s, 0.24f * s),
            new Vector2(0.56f * s, 0.76f * s),
        };
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                bool inHead = PointInPoly(p, head);
                bool inShaft = x >= 0.50f * s && x <= 0.82f * s && y >= 0.43f * s && y <= 0.57f * s;
                px[y * s + x] = (inHead || inShaft) ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
        tex.SetPixels32(px); tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
        backSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        return backSprite;
    }

    static bool PointInPoly(Vector2 p, Vector2[] v)
    {
        bool inside = false;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            if (((v[i].y > p.y) != (v[j].y > p.y)) &&
                (p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x))
                inside = !inside;
        return inside;
    }

    // A neutral head-and-shoulders silhouette placeholder (until real portraits exist).
    static Sprite Silhouette()
    {
        if (silhouetteSprite != null) return silhouetteSprite;
        const int s = 96;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[s * s];
        Color32 body = new Color32(180, 190, 208, 255);
        Color32 clear = new Color32(0, 0, 0, 0);
        Vector2 head = new Vector2(s * 0.5f, s * 0.64f);
        float headR = s * 0.19f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool inHead = Vector2.Distance(new Vector2(x, y), head) <= headR;
                float bx = Mathf.Abs(x - s * 0.5f);
                float tNeck = Mathf.InverseLerp(0.46f * s, 0.10f * s, y);
                float halfW = Mathf.Lerp(s * 0.20f, s * 0.34f, Mathf.Clamp01(tNeck));
                bool inBody = y >= 0.10f * s && y <= 0.46f * s && bx <= halfW;
                px[y * s + x] = (inHead || inBody) ? body : clear;
            }
        tex.SetPixels32(px); tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
        silhouetteSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        return silhouetteSprite;
    }
}
