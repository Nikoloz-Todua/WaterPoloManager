using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// League leaderboard — HONESTLY SIMULATED (this game has no multiplayer backend). The ~24 rival
// entries are generated once per season (deterministic from the season epoch, gamer-tag name
// pool — same spirit as LeagueSeason's simulated opponents). Only the PLAYER's points are real:
// +20 per match win / +5 per loss, awarded from the shared MatchTimer.EndMatch hook.
// Season rollover follows SeasonPassManager's epoch (ONE season concept): at rollover the
// previous rank is recorded ("last week result"), top 5 promote a tier, bottom 5 demote.
// Plain C# singleton, own JSON (leaderboard.json). Elite/World/Friends/Country tabs are
// deliberate COMING SOON stubs — they need real accounts.
public class LeaderboardManager
{
    public static readonly string[] TierNames = { "IRON", "BRONZE", "SILVER", "GOLD", "DIAMOND" };
    public const int PointsPerWin = 20;
    public const int PointsPerLoss = 5;
    public const int PromoteRank = 5;   // top 5 → up a tier
    public const int DemoteRank = 20;   // rank 20+ → down a tier
    const int RivalCount = 24;

    static readonly string[] RivalPool =
    {
        "AquaKing", "SplashMaster", "DeepBlue77", "WaveRider", "PoloShark", "TritonX",
        "HydroNik", "MarinFC", "BlueWhale", "TorpedoTom", "SeaWolf88", "CoachDima",
        "GoalMachine", "WetCap", "PoseidonJr", "LaneSix", "EggBeater", "CapNumber1",
        "SwimDragon", "CurrentKing", "TideTurner", "OrcaOne", "PoloPetre", "Bakuri22",
        "WaterWizz", "SplashZone", "DeepEnd", "CenterFwd", "BrineBaron", "FlatTwo"
    };

    static LeaderboardManager instance;
    public static LeaderboardManager Instance => instance ?? (instance = new LeaderboardManager());

    [Serializable] class Rival { public string name; public int points; }

    [Serializable]
    class SaveData
    {
        public long seasonTicks;
        public int playerPoints;
        public int tier;                       // index into TierNames, starts IRON
        public List<Rival> rivals = new List<Rival>();
        public bool hasPrev;
        public int prevRank;
        public int prevPoints;
        public string prevTier = "";
    }

    SaveData data;
    bool isEnsuringSeason;
    string SavePath => Path.Combine(Application.persistentDataPath, "leaderboard.json");

    LeaderboardManager()
    {
        Load();
        EnsureSeason();
    }

    void Load()
    {
        data = null;
        if (File.Exists(SavePath))
        {
            try { data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)); }
            catch (Exception e) { Debug.LogWarning("LeaderboardManager: save unreadable, recreating. " + e.Message); }
        }
        if (data == null) data = new SaveData();
        if (data.rivals == null) data.rivals = new List<Rival>();
    }

    void Save()
    {
        try { File.WriteAllText(SavePath, JsonUtility.ToJson(data, true)); }
        catch (Exception e) { Debug.LogWarning("LeaderboardManager: could not save. " + e.Message); }
    }

    void EnsureSeason()
    {
        // Defensive re-entry guard: rollover now uses unchecked helpers below, but this prevents a
        // future query added inside rollover from ever recreating the old stack-overflow cycle.
        if (isEnsuringSeason) return;
        isEnsuringSeason = true;
        try
        {
            long seasonTicks = SeasonPassManager.Instance.SeasonStartTicks;
            if (data.seasonTicks == seasonTicks && data.rivals.Count > 0) return;

            if (data.seasonTicks != 0 && data.rivals.Count > 0)
            {
                // Close out the old season: record the result, then promote/demote.
                // Do not call the public Rank() here: Rank -> Standings -> EnsureSeason would re-enter
                // this rollover indefinitely. The current data still represents the OLD season at
                // this point, so calculate its final rank directly before replacing the rivals.
                data.prevRank = RankUnchecked();
                data.prevPoints = data.playerPoints;
                data.prevTier = TierNames[Mathf.Clamp(data.tier, 0, TierNames.Length - 1)];
                data.hasPrev = true;
                if (data.prevRank <= PromoteRank) data.tier = Mathf.Min(data.tier + 1, TierNames.Length - 1);
                else if (data.prevRank >= DemoteRank) data.tier = Mathf.Max(data.tier - 1, 0);
            }

            data.seasonTicks = seasonTicks;
            data.playerPoints = 0;
            GenerateRivals(seasonTicks);
            Save();
        }
        finally
        {
            isEnsuringSeason = false;
        }
    }

    // Deterministic per season: same rivals all season, fresh set next season. Point spread
    // scales a little with tier so higher leagues feel harder.
    void GenerateRivals(long seed)
    {
        System.Random rng = new System.Random((int)(seed % int.MaxValue) ^ data.tier * 977);
        List<string> pool = new List<string>(RivalPool);
        data.rivals.Clear();
        int max = 300 + data.tier * 150;
        for (int i = 0; i < RivalCount && pool.Count > 0; i++)
        {
            int pick = rng.Next(pool.Count);
            data.rivals.Add(new Rival { name = pool[pick], points = rng.Next(10, max) });
            pool.RemoveAt(pick);
        }
    }

    // ------------------------------------------------------------------ API

    public int TierIndex { get { EnsureSeason(); return Mathf.Clamp(data.tier, 0, TierNames.Length - 1); } }
    public string TierName => TierNames[TierIndex];
    public int PlayerPoints { get { EnsureSeason(); return data.playerPoints; } }
    public bool HasPreviousSeason => data.hasPrev;
    public int PrevRank => data.prevRank;
    public int PrevPoints => data.prevPoints;
    public string PrevTier => data.prevTier;

    public void AddLeaguePoints(int n)
    {
        EnsureSeason();
        data.playerPoints += n;
        Save();
    }

    public struct Row { public string name; public int points; public bool isPlayer; }

    // Rivals + the player's real row, sorted by points (desc). Player name = the club name.
    public List<Row> Standings()
    {
        EnsureSeason();
        return StandingsUnchecked();
    }

    // Builds rows from the data currently in memory without trying to roll the season. This is
    // required while EnsureSeason is closing the old season and recording its final result.
    List<Row> StandingsUnchecked()
    {
        List<Row> rows = new List<Row>(data.rivals.Count + 1);
        foreach (Rival r in data.rivals) rows.Add(new Row { name = r.name, points = r.points });
        rows.Add(new Row { name = RosterManager.Instance.Club.clubName, points = data.playerPoints, isPlayer = true });
        rows.Sort((a, b) =>
        {
            int c = b.points.CompareTo(a.points);
            return c != 0 ? c : string.CompareOrdinal(a.name, b.name);
        });
        return rows;
    }

    public int Rank()
    {
        EnsureSeason();
        return RankUnchecked();
    }

    int RankUnchecked()
    {
        List<Row> rows = StandingsUnchecked();
        for (int i = 0; i < rows.Count; i++) if (rows[i].isPlayer) return i + 1;
        return rows.Count;
    }
}

// ---------------------------------------------------------------------------- UI

// The Ranking screen — code-built, hosted in NavigationManager's overlay (hub RANKING button).
// LEAGUE tab is fully built (simulated list + the player's real row, pinned at the bottom).
// ELITE LEAGUE / WORLD / FRIENDS / COUNTRY are honest COMING SOON stubs (no backend exists).
public class RankingUI : MonoBehaviour
{
    static readonly Color CardFill = new Color(0.07f, 0.12f, 0.19f, 0.97f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    static readonly Color Silver = new Color(0.75f, 0.78f, 0.82f);
    static readonly Color BronzeCol = new Color(0.72f, 0.45f, 0.2f);
    static readonly Color Cyan = new Color(0f, 0.85f, 1f);
    static readonly Color Green = new Color(0.2f, 0.72f, 0.32f);
    static readonly Color Grey = new Color(0.55f, 0.6f, 0.68f);

    static readonly string[] TabNames = { "LEAGUE", "ELITE LEAGUE", "WORLD", "FRIENDS", "COUNTRY" };

    Transform root;
    NavigationManager nav;
    int tab;
    readonly List<TextMeshProUGUI> tabLabels = new List<TextMeshProUGUI>();
    RectTransform panelArea;
    GameObject popup;

    public void Build(Transform parent, NavigationManager navigation)
    {
        root = parent;
        nav = navigation;

        Image bg = NewImage("Background", root);
        bg.color = new Color(0.03f, 0.07f, 0.13f, 1f);
        bg.raycastTarget = true;
        Stretch(bg.rectTransform);

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
        bbtn.onClick.AddListener(() => { if (nav != null) nav.CloseRankingScreen(); });

        MakeText(bar.transform, "RANKING", 34f, new Vector2(0.5f, 0.5f), Vector2.zero,
                 new Vector2(300f, 50f), Color.white, TextAlignmentOptions.Center);

        // Left tab column.
        tabLabels.Clear();
        for (int i = 0; i < TabNames.Length; i++)
        {
            int idx = i;
            GameObject go = new GameObject("Tab_" + TabNames[i]);
            go.transform.SetParent(root, false);
            SetRect(go.AddComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                    new Vector2(-520f, 190f - i * 78f), new Vector2(220f, 62f));
            Image face = go.AddComponent<Image>();
            face.sprite = Rounded(); face.type = Image.Type.Sliced;
            face.color = new Color(0.06f, 0.1f, 0.16f, 1f);
            Button b = go.AddComponent<Button>();
            b.targetGraphic = face;
            b.onClick.AddListener(() => { tab = idx; SyncTabs(); RebuildPanel(); });
            TextMeshProUGUI t = MakeText(go.transform, TabNames[i], 16f, new Vector2(0.5f, 0.5f),
                                         Vector2.zero, new Vector2(210f, 54f), Grey, TextAlignmentOptions.Center);
            Stretch(t.rectTransform);
            tabLabels.Add(t);
        }

        GameObject area = new GameObject("PanelArea");
        area.transform.SetParent(root, false);
        panelArea = area.AddComponent<RectTransform>();
        SetRect(panelArea, new Vector2(0.5f, 0.5f), new Vector2(110f, -40f), new Vector2(980f, 600f));

        SyncTabs();
        RebuildPanel();
    }

    void OnEnable() { if (panelArea != null) { SyncTabs(); RebuildPanel(); } }

    void SyncTabs()
    {
        for (int i = 0; i < tabLabels.Count; i++)
            tabLabels[i].color = i == tab ? Gold : Grey;
    }

    void RebuildPanel()
    {
        for (int i = panelArea.childCount - 1; i >= 0; i--) Destroy(panelArea.GetChild(i).gameObject);
        if (tab == 0) BuildLeaguePanel();
        else BuildStubPanel(TabNames[tab]);
    }

    // ------------------------------------------------------------------ LEAGUE

    void BuildLeaguePanel()
    {
        LeaderboardManager lb = LeaderboardManager.Instance;

        // Header: tier + countdown + bonus stat + "i" + last week result.
        Image header = NewImage("Header", panelArea);
        header.sprite = Rounded(); header.type = Image.Type.Sliced;
        header.color = new Color(0.227f, 0.353f, 0.478f, 1f);
        SetRect(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(960f, 80f));
        Image hfill = NewImage("Fill", header.transform);
        hfill.sprite = Rounded(); hfill.type = Image.Type.Sliced;
        hfill.color = CardFill;
        hfill.raycastTarget = false;
        RectTransform hfr = hfill.rectTransform;
        hfr.anchorMin = Vector2.zero; hfr.anchorMax = Vector2.one;
        hfr.offsetMin = new Vector2(3f, 3f); hfr.offsetMax = new Vector2(-3f, -3f);

        MakeText(header.transform, lb.TierName + " LEAGUE", 24f, new Vector2(0f, 0.5f), new Vector2(130f, 14f),
                 new Vector2(240f, 30f), Gold, TextAlignmentOptions.Left);
        MakeText(header.transform, "ENDS IN " + SeasonPassManager.Instance.CountdownLabel(), 15f,
                 new Vector2(0f, 0.5f), new Vector2(130f, -16f), new Vector2(240f, 22f), Cyan,
                 TextAlignmentOptions.Left);
        MakeText(header.transform, "WIN = +" + LeaderboardManager.PointsPerWin + " PTS", 15f,
                 new Vector2(0.5f, 0.5f), new Vector2(30f, 0f), new Vector2(160f, 24f), Grey,
                 TextAlignmentOptions.Center);

        // "i" info popup (promotion rules).
        GameObject info = new GameObject("BtnInfo");
        info.transform.SetParent(header.transform, false);
        SetRect(info.AddComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-250f, 0f), new Vector2(40f, 40f));
        Image iimg = info.AddComponent<Image>();
        Sprite isp = Resources.Load<Sprite>("Sprites/i-button");
        if (isp != null) { iimg.sprite = isp; iimg.preserveAspect = true; }
        else { iimg.sprite = Circle(); iimg.color = Cyan; }
        Button ibtn = info.AddComponent<Button>();
        ibtn.targetGraphic = iimg;
        ibtn.onClick.AddListener(() => ShowPopup("LEAGUE RULES",
            "Win matches to earn league points\n(+" + LeaderboardManager.PointsPerWin + " win / +" +
            LeaderboardManager.PointsPerLoss + " loss).\n\nAt season's end: top " + LeaderboardManager.PromoteRank +
            " promote a league,\nrank " + LeaderboardManager.DemoteRank + "+ demotes.\n\nLadder: " +
            string.Join(" → ", LeaderboardManager.TierNames) + "\n\nRivals are SIMULATED until online play exists."));

        MakeButton(header.transform, "LAST WEEK", 14f, new Vector2(1f, 0.5f), new Vector2(-120f, 0f),
                   new Vector2(190f, 46f), new Color(0.16f, 0.2f, 0.28f, 1f), () =>
        {
            if (LeaderboardManager.Instance.HasPreviousSeason)
                ShowPopup("LAST SEASON RESULT",
                    LeaderboardManager.Instance.PrevTier + " LEAGUE\nRANK " + LeaderboardManager.Instance.PrevRank +
                    "  —  " + LeaderboardManager.Instance.PrevPoints + " PTS");
            else
                ShowPopup("LAST SEASON RESULT", "No previous season yet —\nthis is your first one. Good luck!");
        });

        // Ranked list.
        GameObject vp = new GameObject("ListViewport");
        vp.transform.SetParent(panelArea, false);
        RectTransform vrt = vp.AddComponent<RectTransform>();
        SetRect(vrt, new Vector2(0.5f, 0.5f), new Vector2(0f, -55f), new Vector2(960f, 390f));
        Image vbg = vp.AddComponent<Image>();
        vbg.color = new Color(0f, 0f, 0f, 0f);
        vp.AddComponent<RectMask2D>();

        GameObject ct = new GameObject("ListContent");
        ct.transform.SetParent(vp.transform, false);
        RectTransform crt = ct.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero;

        ScrollRect scroll = vp.AddComponent<ScrollRect>();
        scroll.viewport = vrt;
        scroll.content = crt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;

        List<LeaderboardManager.Row> rows = lb.Standings();
        float y = -4f;
        for (int i = 0; i < rows.Count; i++)
        {
            BuildRankRow(crt, rows[i], i + 1, false, y);
            y -= 58f;
        }
        crt.sizeDelta = new Vector2(0f, -y + 4f);

        // Pinned player row (always visible, reference style).
        int rank = lb.Rank();
        LeaderboardManager.Row me = new LeaderboardManager.Row
        { name = RosterManager.Instance.Club.clubName, points = lb.PlayerPoints, isPlayer = true };
        GameObject pin = new GameObject("PinnedRow");
        pin.transform.SetParent(panelArea, false);
        RectTransform prt = pin.AddComponent<RectTransform>();
        SetRect(prt, new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(960f, 54f));
        BuildRankRow(prt, me, rank, true, 0f);
    }

    void BuildRankRow(RectTransform parent, LeaderboardManager.Row row, int rank, bool pinned, float yTop)
    {
        Image face = NewImage("Row" + rank, parent);
        face.sprite = Rounded(); face.type = Image.Type.Sliced;
        face.color = row.isPlayer ? new Color(0.45f, 0.38f, 0.12f, 1f) : new Color(0.07f, 0.11f, 0.17f, 0.95f);
        RectTransform rrt = face.rectTransform;
        if (pinned) { Stretch(rrt); }
        else
        {
            rrt.anchorMin = new Vector2(0.5f, 1f);
            rrt.anchorMax = new Vector2(0.5f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0f, yTop);
            rrt.sizeDelta = new Vector2(940f, 52f);
        }

        // Rank / medal: top 3 get colored medal circles.
        if (rank <= 3)
        {
            Image medal = NewImage("Medal", face.transform);
            medal.sprite = Circle();
            medal.color = rank == 1 ? Gold : rank == 2 ? Silver : BronzeCol;
            medal.raycastTarget = false;
            SetRect(medal.rectTransform, new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(34f, 34f));
            TextMeshProUGUI mt = MakeText(medal.transform, rank.ToString(), 17f, new Vector2(0.5f, 0.5f),
                     Vector2.zero, new Vector2(34f, 34f), new Color(0.1f, 0.1f, 0.1f), TextAlignmentOptions.Center);
            Stretch(mt.rectTransform);
        }
        else
            MakeText(face.transform, "#" + rank, 17f, new Vector2(0f, 0.5f), new Vector2(40f, 0f),
                     new Vector2(60f, 26f), row.isPlayer ? Gold : Grey, TextAlignmentOptions.Center);

        // Placeholder avatar dot (real avatars need accounts/art).
        Image dot = NewImage("Avatar", face.transform);
        dot.sprite = Circle();
        dot.color = AvatarColor(row.name);
        dot.raycastTarget = false;
        SetRect(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(90f, 0f), new Vector2(30f, 30f));

        MakeText(face.transform, row.name + (row.isPlayer ? "  (YOU)" : ""), 17f, new Vector2(0f, 0.5f),
                 new Vector2(320f, 0f), new Vector2(400f, 26f), Color.white, TextAlignmentOptions.Left);
        MakeText(face.transform, row.points.ToString("N0") + " PTS", 17f, new Vector2(1f, 0.5f),
                 new Vector2(-70f, 0f), new Vector2(140f, 26f), row.isPlayer ? Gold : Cyan,
                 TextAlignmentOptions.Right);
    }

    static Color AvatarColor(string name)
    {
        int h = 0;
        foreach (char c in name) h = h * 31 + c;
        UnityEngine.Random.State s = UnityEngine.Random.state;
        UnityEngine.Random.InitState(h);
        Color col = Color.HSVToRGB(UnityEngine.Random.value, 0.55f, 0.85f);
        UnityEngine.Random.state = s;
        return col;
    }

    // ------------------------------------------------------------------ stub tabs

    void BuildStubPanel(string title)
    {
        Image card = NewImage("Stub", panelArea);
        card.sprite = Rounded(); card.type = Image.Type.Sliced;
        card.color = new Color(0.227f, 0.353f, 0.478f, 1f);
        SetRect(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(640f, 320f));
        Image fill = NewImage("Fill", card.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced;
        fill.color = CardFill;
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);

        Image lockIcon = NewImage("Lock", card.transform);
        lockIcon.sprite = NavigationManager.MakeLockSprite();
        lockIcon.color = new Color(0.85f, 0.87f, 0.92f, 1f);
        lockIcon.raycastTarget = false;
        SetRect(lockIcon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(52f, 52f));

        MakeText(card.transform, title, 26f, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f),
                 new Vector2(560f, 34f), Cyan, TextAlignmentOptions.Center);
        MakeText(card.transform,
                 "COMING SOON — this leaderboard needs real online\naccounts, which aren't built yet. No fake data here.",
                 17f, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(560f, 60f),
                 Color.white, TextAlignmentOptions.Center);
    }

    // ------------------------------------------------------------------ popup

    void ShowPopup(string title, string body)
    {
        ClosePopup();
        popup = new GameObject("RankPopup");
        popup.transform.SetParent(root, false);
        popup.transform.SetAsLastSibling();
        Stretch(popup.AddComponent<RectTransform>());
        Image dark = popup.AddComponent<Image>();
        dark.color = new Color(0.02f, 0.03f, 0.08f, 0.9f);
        Button db = popup.AddComponent<Button>();
        db.targetGraphic = dark;
        db.onClick.AddListener(ClosePopup);

        Image sheet = NewImage("Sheet", popup.transform);
        sheet.sprite = Rounded(); sheet.type = Image.Type.Sliced;
        sheet.color = CardFill;
        SetRect(sheet.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 400f));

        MakeText(sheet.transform, title, 24f, new Vector2(0.5f, 1f), new Vector2(0f, -44f),
                 new Vector2(500f, 32f), Gold, TextAlignmentOptions.Center);
        MakeText(sheet.transform, body, 18f, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f),
                 new Vector2(500f, 240f), Color.white, TextAlignmentOptions.Center);
        MakeButton(sheet.transform, "OK", 20f, new Vector2(0.5f, 0f), new Vector2(0f, 44f),
                   new Vector2(180f, 52f), Green, ClosePopup);
    }

    void ClosePopup() { if (popup != null) { Destroy(popup); popup = null; } }

    // ------------------------------------------------------------------ helpers

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
