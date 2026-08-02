using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum MissionCategory { Newcomer, Daily, Weekly, GlobalCup }
public enum MissionRewardType { Coins, Gems, Pack }

// Missions — plain C# singleton (no scene object), own JSON save (missions.json in
// persistentDataPath). Tracks 3 real stats only (matches_played / matches_won / packs_opened),
// each in four period scopes:
//   Newcomer  → lifetime, never resets
//   Daily     → resets each UTC day  (same day-number pattern as AdWatchCap / Daily Deals)
//   Weekly    → resets every 7 UTC days from a stored week-start day
//   GlobalCup → scoped to the CURRENT SEASON (SeasonPassManager's epoch — resets when it rolls)
// Stat hooks live at the existing event points: MatchTimer.EndMatch (matches) and
// CardPack.OpenTierPack (packs — covers shop buys, reward slots, and pack rewards alike).
public class MissionManager
{
    public class MissionDef
    {
        public string id;
        public MissionCategory category;
        public string description;
        public string statKey;   // "matches_played" | "matches_won" | "packs_opened"
        public int target;
        public MissionRewardType rewardType;
        public int rewardAmount; // coins/gems count, or (int)CardTier for packs
    }

    public static readonly MissionDef[] Defs =
    {
        // Newcomer — one-time starter set.
        new MissionDef { id = "n_play1", category = MissionCategory.Newcomer, description = "Play your first match",
            statKey = "matches_played", target = 1, rewardType = MissionRewardType.Coins, rewardAmount = 200 },
        new MissionDef { id = "n_win1", category = MissionCategory.Newcomer, description = "Win your first match",
            statKey = "matches_won", target = 1, rewardType = MissionRewardType.Gems, rewardAmount = 10 },
        new MissionDef { id = "n_open1", category = MissionCategory.Newcomer, description = "Open your first pack",
            statKey = "packs_opened", target = 1, rewardType = MissionRewardType.Coins, rewardAmount = 200 },
        new MissionDef { id = "n_play5", category = MissionCategory.Newcomer, description = "Play 5 matches",
            statKey = "matches_played", target = 5, rewardType = MissionRewardType.Coins, rewardAmount = 400 },
        new MissionDef { id = "n_win3", category = MissionCategory.Newcomer, description = "Win 3 matches",
            statKey = "matches_won", target = 3, rewardType = MissionRewardType.Gems, rewardAmount = 20 },
        new MissionDef { id = "n_open5", category = MissionCategory.Newcomer, description = "Open 5 packs",
            statKey = "packs_opened", target = 5, rewardType = MissionRewardType.Pack, rewardAmount = (int)CardTier.Common },

        // Daily.
        new MissionDef { id = "d_play1", category = MissionCategory.Daily, description = "Play a match today",
            statKey = "matches_played", target = 1, rewardType = MissionRewardType.Coins, rewardAmount = 150 },
        new MissionDef { id = "d_win1", category = MissionCategory.Daily, description = "Win a match today",
            statKey = "matches_won", target = 1, rewardType = MissionRewardType.Gems, rewardAmount = 5 },
        new MissionDef { id = "d_open1", category = MissionCategory.Daily, description = "Open a pack today",
            statKey = "packs_opened", target = 1, rewardType = MissionRewardType.Coins, rewardAmount = 150 },

        // Weekly.
        new MissionDef { id = "w_play5", category = MissionCategory.Weekly, description = "Play 5 matches this week",
            statKey = "matches_played", target = 5, rewardType = MissionRewardType.Coins, rewardAmount = 500 },
        new MissionDef { id = "w_win3", category = MissionCategory.Weekly, description = "Win 3 matches this week",
            statKey = "matches_won", target = 3, rewardType = MissionRewardType.Gems, rewardAmount = 25 },
        new MissionDef { id = "w_open3", category = MissionCategory.Weekly, description = "Open 3 packs this week",
            statKey = "packs_opened", target = 3, rewardType = MissionRewardType.Pack, rewardAmount = (int)CardTier.Rare },

        // Global Cup — season-long (resets when the Season Pass season rolls).
        new MissionDef { id = "g_play10", category = MissionCategory.GlobalCup, description = "Play 10 matches this season",
            statKey = "matches_played", target = 10, rewardType = MissionRewardType.Gems, rewardAmount = 30 },
        new MissionDef { id = "g_win5", category = MissionCategory.GlobalCup, description = "Win 5 matches this season",
            statKey = "matches_won", target = 5, rewardType = MissionRewardType.Pack, rewardAmount = (int)CardTier.Epic },
        new MissionDef { id = "g_open10", category = MissionCategory.GlobalCup, description = "Open 10 packs this season",
            statKey = "packs_opened", target = 10, rewardType = MissionRewardType.Coins, rewardAmount = 1000 },
    };

    public const int ClaimBonusXp = 10; // season XP granted on top of every mission reward

    static MissionManager instance;
    public static MissionManager Instance => instance ?? (instance = new MissionManager());

    [Serializable] class StatEntry { public string key; public int value; }

    [Serializable]
    class SaveData
    {
        public long dailyDay = long.MinValue;
        public long weekStartDay = long.MinValue;
        public long seasonTicks;
        public List<StatEntry> life = new List<StatEntry>();
        public List<StatEntry> daily = new List<StatEntry>();
        public List<StatEntry> week = new List<StatEntry>();
        public List<StatEntry> season = new List<StatEntry>();
        public List<string> claimedLife = new List<string>();
        public List<string> claimedDaily = new List<string>();
        public List<string> claimedWeek = new List<string>();
        public List<string> claimedSeason = new List<string>();
    }

    SaveData data;
    string SavePath => Path.Combine(Application.persistentDataPath, "missions.json");

    MissionManager()
    {
        Load();
        EnsurePeriods();
    }

    void Load()
    {
        data = null;
        if (File.Exists(SavePath))
        {
            try { data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)); }
            catch (Exception e) { Debug.LogWarning("MissionManager: save unreadable, recreating. " + e.Message); }
        }
        if (data == null) data = new SaveData();
    }

    void Save()
    {
        try { File.WriteAllText(SavePath, JsonUtility.ToJson(data, true)); }
        catch (Exception e) { Debug.LogWarning("MissionManager: could not save. " + e.Message); }
    }

    // Same UTC-day-number pattern as AdWatchCap / Daily Deals.
    static long UtcDay() => (long)(DateTime.UtcNow - new DateTime(2026, 1, 1)).TotalDays;

    void EnsurePeriods()
    {
        bool dirty = false;
        long day = UtcDay();
        if (data.dailyDay != day)
        {
            data.dailyDay = day;
            data.daily.Clear();
            data.claimedDaily.Clear();
            dirty = true;
        }
        if (data.weekStartDay == long.MinValue) { data.weekStartDay = day; dirty = true; }
        else if (day - data.weekStartDay >= 7)
        {
            data.weekStartDay += (day - data.weekStartDay) / 7 * 7;
            data.week.Clear();
            data.claimedWeek.Clear();
            dirty = true;
        }
        long seasonTicks = SeasonPassManager.Instance.SeasonStartTicks;
        if (data.seasonTicks != seasonTicks)
        {
            data.seasonTicks = seasonTicks;
            data.season.Clear();
            data.claimedSeason.Clear();
            dirty = true;
        }
        if (dirty) Save();
    }

    static int GetStat(List<StatEntry> list, string key)
    {
        foreach (StatEntry e in list) if (e.key == key) return e.value;
        return 0;
    }

    static void BumpStat(List<StatEntry> list, string key, int amount)
    {
        foreach (StatEntry e in list) if (e.key == key) { e.value += amount; return; }
        list.Add(new StatEntry { key = key, value = amount });
    }

    List<StatEntry> StatsFor(MissionCategory c) => c switch
    {
        MissionCategory.Daily => data.daily,
        MissionCategory.Weekly => data.week,
        MissionCategory.GlobalCup => data.season,
        _ => data.life,
    };

    List<string> ClaimedFor(MissionCategory c) => c switch
    {
        MissionCategory.Daily => data.claimedDaily,
        MissionCategory.Weekly => data.claimedWeek,
        MissionCategory.GlobalCup => data.claimedSeason,
        _ => data.claimedLife,
    };

    // ------------------------------------------------------------------ API

    public void RecordStat(string key, int amount = 1)
    {
        EnsurePeriods();
        BumpStat(data.life, key, amount);
        BumpStat(data.daily, key, amount);
        BumpStat(data.week, key, amount);
        BumpStat(data.season, key, amount);
        Save();
    }

    public int Progress(MissionDef def)
    {
        EnsurePeriods();
        return Mathf.Min(def.target, GetStat(StatsFor(def.category), def.statKey));
    }

    public bool IsClaimed(MissionDef def) { EnsurePeriods(); return ClaimedFor(def.category).Contains(def.id); }
    public bool IsClaimable(MissionDef def) => !IsClaimed(def) && Progress(def) >= def.target;

    public int ClaimReadyCount()
    {
        int n = 0;
        foreach (MissionDef d in Defs) if (IsClaimable(d)) n++;
        return n;
    }

    // Claim: grants through RosterManager (via GrantReward) + a small season-XP bonus.
    // Returns pack grant results for the reveal overlay (null for coin/gem rewards).
    public List<CardPack.GrantResult> Claim(MissionDef def, out bool claimed)
    {
        claimed = false;
        if (!IsClaimable(def)) return null;
        ClaimedFor(def.category).Add(def.id);
        Save();
        claimed = true;
        List<CardPack.GrantResult> results = GrantReward(def.rewardType, def.rewardAmount);
        SeasonPassManager.Instance.AddXP(ClaimBonusXp);
        return results;
    }

    // The ONE reward-granting funnel for missions AND the season pass — always through
    // RosterManager's existing currency methods, no parallel tracking.
    public static List<CardPack.GrantResult> GrantReward(MissionRewardType type, int amount)
    {
        switch (type)
        {
            case MissionRewardType.Coins: RosterManager.Instance.AddCoins(amount); return null;
            case MissionRewardType.Gems: RosterManager.Instance.AddDiamonds(amount); return null;
            default: return CardPack.GrantAll(CardPack.OpenTierPack((CardTier)amount));
        }
    }

    // Countdown label for a category's reset ("" for Newcomer — it never resets).
    public string ResetLabel(MissionCategory c)
    {
        EnsurePeriods();
        switch (c)
        {
            case MissionCategory.Daily:
            {
                TimeSpan left = DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;
                return "RESETS IN " + (int)left.TotalHours + "H " + left.Minutes + "M";
            }
            case MissionCategory.Weekly:
            {
                long daysLeft = data.weekStartDay + 7 - UtcDay();
                return "RESETS IN " + Math.Max(1, daysLeft) + "D";
            }
            case MissionCategory.GlobalCup:
                return "SEASON ENDS IN " + SeasonPassManager.Instance.CountdownLabel();
            default:
                return "";
        }
    }
}

// ---------------------------------------------------------------------------- UI

// The Missions screen — code-built, hosted in NavigationManager's overlay (hub MISSIONS
// button). Left: 4 category tabs. Right: scrollable mission list with progress bar, reward
// and CLAIM state per mission.
public class MissionsUI : MonoBehaviour
{
    static readonly Color CardFill = new Color(0.07f, 0.12f, 0.19f, 0.97f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    static readonly Color Cyan = new Color(0f, 0.85f, 1f);
    static readonly Color Green = new Color(0.2f, 0.72f, 0.32f);
    static readonly Color Grey = new Color(0.55f, 0.6f, 0.68f);

    static readonly string[] TabNames = { "NEWCOMER", "DAILY", "WEEKLY", "GLOBAL CUP" };

    Transform root;
    NavigationManager nav;
    MissionCategory tab = MissionCategory.Newcomer;
    readonly List<TextMeshProUGUI> tabLabels = new List<TextMeshProUGUI>();
    RectTransform listContent;
    TextMeshProUGUI resetLabel;

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

        Sprite back = ButtonSpriteCatalog.SpriteFor("Back-Button");
        GameObject bgo = new GameObject("BtnBack");
        bgo.transform.SetParent(bar.transform, false);
        SetRect(bgo.AddComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(64f, 64f));
        Image bimg = bgo.AddComponent<Image>();
        if (back != null) { bimg.sprite = back; bimg.preserveAspect = true; }
        else { bimg.sprite = Rounded(); bimg.type = Image.Type.Sliced; bimg.color = new Color(0.16f, 0.2f, 0.28f, 1f); }
        Button bbtn = bgo.AddComponent<Button>();
        bbtn.targetGraphic = bimg;
        bbtn.onClick.AddListener(() => { if (nav != null) nav.CloseMissionsScreen(); });

        MakeText(bar.transform, "MISSIONS", 34f, new Vector2(0.5f, 0.5f), Vector2.zero,
                 new Vector2(300f, 50f), Color.white, TextAlignmentOptions.Center);

        // Left tab column.
        tabLabels.Clear();
        for (int i = 0; i < TabNames.Length; i++)
        {
            int idx = i;
            GameObject go = new GameObject("Tab_" + TabNames[i]);
            go.transform.SetParent(root, false);
            SetRect(go.AddComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                    new Vector2(-520f, 170f - i * 84f), new Vector2(220f, 68f));
            Image face = go.AddComponent<Image>();
            face.sprite = ButtonSpriteCatalog.SpriteFor("Button1");
            if (face.sprite == null) { face.sprite = Rounded(); face.type = Image.Type.Sliced; }
            face.color = new Color(0.06f, 0.1f, 0.16f, 1f);
            Button b = go.AddComponent<Button>();
            b.targetGraphic = face;
            b.onClick.AddListener(() => { tab = (MissionCategory)idx; SyncTabs(); RebuildList(); });
            TextMeshProUGUI t = LocalizedButtonStyler.AddLabel(go.transform, TabNames[i], 17f,
                new Vector2(220f, 68f), maxWidthMultiplier: 1f);
            t.color = Grey;
            tabLabels.Add(t);
        }

        resetLabel = MakeText(root, "", 16f, new Vector2(0.5f, 0.5f), new Vector2(110f, 240f),
                              new Vector2(700f, 24f), Cyan, TextAlignmentOptions.Center);

        // Mission list (vertical scroll).
        GameObject vp = new GameObject("ListViewport");
        vp.transform.SetParent(root, false);
        RectTransform vrt = vp.AddComponent<RectTransform>();
        SetRect(vrt, new Vector2(0.5f, 0.5f), new Vector2(110f, -50f), new Vector2(960f, 520f));
        Image vbg = vp.AddComponent<Image>();
        vbg.color = new Color(0f, 0f, 0f, 0f);
        vp.AddComponent<RectMask2D>();

        GameObject ct = new GameObject("ListContent");
        ct.transform.SetParent(vp.transform, false);
        listContent = ct.AddComponent<RectTransform>();
        listContent.anchorMin = new Vector2(0f, 1f);
        listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.anchoredPosition = Vector2.zero;

        ScrollRect scroll = vp.AddComponent<ScrollRect>();
        scroll.viewport = vrt;
        scroll.content = listContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;

        SyncTabs();
        RebuildList();
    }

    void OnEnable() { if (listContent != null) { SyncTabs(); RebuildList(); } }

    void SyncTabs()
    {
        for (int i = 0; i < tabLabels.Count; i++)
            tabLabels[i].color = i == (int)tab ? Gold : Grey;
    }

    void RebuildList()
    {
        for (int i = listContent.childCount - 1; i >= 0; i--) Destroy(listContent.GetChild(i).gameObject);
        MissionManager mm = MissionManager.Instance;
        resetLabel.text = mm.ResetLabel(tab);

        float y = -12f;
        foreach (MissionManager.MissionDef def in MissionManager.Defs)
        {
            if (def.category != tab) continue;
            BuildRow(mm, def, y);
            y -= 108f;
        }
        listContent.sizeDelta = new Vector2(0f, -y + 12f);
    }

    void BuildRow(MissionManager mm, MissionManager.MissionDef def, float yTop)
    {
        int progress = mm.Progress(def);
        bool claimed = mm.IsClaimed(def);
        bool claimable = mm.IsClaimable(def);

        Image row = NewImage("Row_" + def.id, listContent);
        row.sprite = Rounded(); row.type = Image.Type.Sliced;
        row.color = claimable ? Green : new Color(0.227f, 0.353f, 0.478f, 1f);
        RectTransform rrt = row.rectTransform;
        rrt.anchorMin = new Vector2(0.5f, 1f);
        rrt.anchorMax = new Vector2(0.5f, 1f);
        rrt.pivot = new Vector2(0.5f, 1f);
        rrt.anchoredPosition = new Vector2(0f, yTop);
        rrt.sizeDelta = new Vector2(940f, 96f);
        Image fill = NewImage("Fill", row.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced;
        fill.color = CardFill;
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);

        MakeText(row.transform, def.description, 18f, new Vector2(0f, 0.5f), new Vector2(220f, 18f),
                 new Vector2(400f, 26f), Color.white, TextAlignmentOptions.Left);

        // Progress bar + n/target.
        Image barBg = NewImage("BarBg", row.transform);
        barBg.sprite = Rounded(); barBg.type = Image.Type.Sliced;
        barBg.color = new Color(0f, 0f, 0f, 0.5f);
        barBg.raycastTarget = false;
        SetRect(barBg.rectTransform, new Vector2(0f, 0.5f), new Vector2(170f, -18f), new Vector2(280f, 14f));
        Image barFill = NewImage("BarFill", barBg.transform);
        barFill.sprite = Rounded(); barFill.type = Image.Type.Sliced;
        barFill.color = claimable || claimed ? Green : Cyan;
        barFill.raycastTarget = false;
        RectTransform bfr = barFill.rectTransform;
        bfr.anchorMin = new Vector2(0f, 0f);
        bfr.anchorMax = new Vector2(Mathf.Clamp01(progress / (float)def.target), 1f);
        bfr.offsetMin = new Vector2(1f, 1f); bfr.offsetMax = new Vector2(-1f, -1f);
        MakeText(row.transform, progress + " / " + def.target, 14f, new Vector2(0f, 0.5f),
                 new Vector2(360f, -18f), new Vector2(90f, 20f), Grey, TextAlignmentOptions.Left);

        // Reward: icon + label.
        BuildRewardIcon(row.transform, def, new Vector2(560f, 0f));

        // State: CLAIM button / CLAIMED / nothing (bar shows progress).
        if (claimable)
        {
            MakeButton(row.transform, "CLAIM", 18f, new Vector2(1f, 0.5f), new Vector2(-90f, 0f),
                       new Vector2(140f, 52f), Green, () =>
            {
                var results = MissionManager.Instance.Claim(def, out bool ok);
                if (!ok) return;
                if (nav != null) { nav.RefreshCurrency(); nav.RefreshMissionsBadge(); }
                if (results != null && results.Count > 0) PackRevealUI.Show(root, results, null);
                RebuildList();
            });
        }
        else if (claimed)
            MakeText(row.transform, "CLAIMED", 16f, new Vector2(1f, 0.5f), new Vector2(-90f, 0f),
                     new Vector2(140f, 24f), Grey, TextAlignmentOptions.Center);
    }

    void BuildRewardIcon(Transform row, MissionManager.MissionDef def, Vector2 pos)
    {
        Image icon = NewImage("RewardIcon", row);
        icon.raycastTarget = false;
        string label;
        if (def.rewardType == MissionRewardType.Pack)
        {
            icon.sprite = CardPack.TierArtSprite((CardTier)def.rewardAmount);
            icon.preserveAspect = true;
            SetRect(icon.rectTransform, new Vector2(0f, 0.5f), pos, new Vector2(52f, 52f));
            label = CardPack.GetTierPack((CardTier)def.rewardAmount).name;
        }
        else if (def.rewardType == MissionRewardType.Coins)
        {
            Sprite coin = Resources.Load<Sprite>("Sprites/gold-coin");
            if (coin != null) { icon.sprite = coin; icon.preserveAspect = true; }
            else { icon.sprite = Circle(); icon.color = Gold; }
            SetRect(icon.rectTransform, new Vector2(0f, 0.5f), pos, new Vector2(40f, 40f));
            label = def.rewardAmount.ToString("N0");
        }
        else
        {
            Sprite gem = Resources.Load<Sprite>("Sprites/diamond-coin");
            if (gem != null) { icon.sprite = gem; icon.preserveAspect = true; }
            else
            {
                icon.sprite = Rounded(); icon.type = Image.Type.Sliced;
                icon.color = Cyan;
                icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }
            SetRect(icon.rectTransform, new Vector2(0f, 0.5f), pos, new Vector2(38f, 38f));
            label = def.rewardAmount.ToString();
        }
        MakeText(row, label, 16f, new Vector2(0f, 0.5f), new Vector2(pos.x + 90f, 0f),
                 new Vector2(140f, 24f), Gold, TextAlignmentOptions.Left);
    }

    // ------------------------------------------------------------------ helpers

    Button MakeButton(Transform parent, string label, float fontSize, Vector2 anchor, Vector2 pos,
                      Vector2 size, Color color, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        SetRect(go.AddComponent<RectTransform>(), anchor, pos, size);
        Image img = go.AddComponent<Image>();
        img.sprite = LocalizedButtonStyler.UniversalSprite();
        if (img.sprite == null) { img.sprite = Rounded(); img.type = Image.Type.Sliced; }
        img.color = color;
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);
        LocalizedButtonStyler.AddLabel(go.transform, label, fontSize, size, maxWidthMultiplier: 1.3f);
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
