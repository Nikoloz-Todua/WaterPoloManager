using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// THE canonical season system. One 14-day season (epoch stored in seasonpass.json) drives:
//   • this screen's XP / reward track / Gold Pass,
//   • the hub's "SEASON ENDS IN" countdown (NavigationManager reads CountdownLabel),
//   • MissionManager's Global Cup mission scope,
//   • LeaderboardManager's league season rollover.
// Plain C# singleton (no scene object), same JSON-in-persistentDataPath pattern as
// PostMatchRewardManager. XP arrives via AddXP from MatchTimer.EndMatch (the one shared
// post-match hook) and from mission claims.
public class SeasonPassManager
{
    public const int TierCount = 16;
    public const int XpPerTier = 100;         // flat — tier N unlocks at N*100 XP
    public const int SeasonDays = 14;
    public const int GoldPassPriceGems = 500; // PLACEHOLDER price — tune before release

    public struct PassReward
    {
        public MissionRewardType type;
        public int amount; // coins/gems count, or (int)CardTier for packs
        public PassReward(MissionRewardType t, int a) { type = t; amount = a; }
    }

    // Tier 1 → index 0. Free track: modest but always collectible.
    public static readonly PassReward[] FreeTrack =
    {
        new PassReward(MissionRewardType.Coins, 150), new PassReward(MissionRewardType.Gems, 10),
        new PassReward(MissionRewardType.Coins, 200), new PassReward(MissionRewardType.Pack, (int)CardTier.Common),
        new PassReward(MissionRewardType.Coins, 250), new PassReward(MissionRewardType.Gems, 15),
        new PassReward(MissionRewardType.Coins, 300), new PassReward(MissionRewardType.Pack, (int)CardTier.Rare),
        new PassReward(MissionRewardType.Coins, 350), new PassReward(MissionRewardType.Gems, 20),
        new PassReward(MissionRewardType.Coins, 400), new PassReward(MissionRewardType.Pack, (int)CardTier.Rare),
        new PassReward(MissionRewardType.Coins, 500), new PassReward(MissionRewardType.Gems, 25),
        new PassReward(MissionRewardType.Coins, 600), new PassReward(MissionRewardType.Pack, (int)CardTier.Epic),
    };

    // Gold track: same tiers, better rewards; locked behind ACTIVATE.
    public static readonly PassReward[] GoldTrack =
    {
        new PassReward(MissionRewardType.Coins, 400), new PassReward(MissionRewardType.Gems, 30),
        new PassReward(MissionRewardType.Pack, (int)CardTier.Rare), new PassReward(MissionRewardType.Coins, 600),
        new PassReward(MissionRewardType.Gems, 40), new PassReward(MissionRewardType.Pack, (int)CardTier.Epic),
        new PassReward(MissionRewardType.Coins, 800), new PassReward(MissionRewardType.Gems, 50),
        new PassReward(MissionRewardType.Pack, (int)CardTier.Epic), new PassReward(MissionRewardType.Coins, 1000),
        new PassReward(MissionRewardType.Gems, 60), new PassReward(MissionRewardType.Pack, (int)CardTier.Epic),
        new PassReward(MissionRewardType.Coins, 1500), new PassReward(MissionRewardType.Gems, 80),
        new PassReward(MissionRewardType.Pack, (int)CardTier.Legendary), new PassReward(MissionRewardType.Pack, (int)CardTier.Legendary),
    };

    static SeasonPassManager instance;
    public static SeasonPassManager Instance => instance ?? (instance = new SeasonPassManager());

    [Serializable]
    class SaveData
    {
        public long seasonStartTicks;
        public int seasonNumber = 1;
        public int xp;
        public bool goldPass;
        public bool[] freeClaimed = new bool[TierCount];
        public bool[] goldClaimed = new bool[TierCount];
    }

    SaveData data;
    string SavePath => Path.Combine(Application.persistentDataPath, "seasonpass.json");

    SeasonPassManager()
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
            catch (Exception e) { Debug.LogWarning("SeasonPassManager: save unreadable, recreating. " + e.Message); }
        }
        if (data == null) data = new SaveData();
        if (data.freeClaimed == null || data.freeClaimed.Length != TierCount) data.freeClaimed = new bool[TierCount];
        if (data.goldClaimed == null || data.goldClaimed.Length != TierCount) data.goldClaimed = new bool[TierCount];
        if (data.seasonStartTicks == 0) { data.seasonStartTicks = DateTime.UtcNow.Ticks; Save(); } // first launch
    }

    void Save()
    {
        try { File.WriteAllText(SavePath, JsonUtility.ToJson(data, true)); }
        catch (Exception e) { Debug.LogWarning("SeasonPassManager: could not save. " + e.Message); }
    }

    // Roll the season forward when 14 days elapse (handles multi-season absences in one step).
    void EnsureSeason()
    {
        DateTime start = new DateTime(data.seasonStartTicks, DateTimeKind.Utc);
        double elapsed = (DateTime.UtcNow - start).TotalDays;
        if (elapsed < SeasonDays) return;
        long periods = (long)(elapsed / SeasonDays);
        data.seasonStartTicks = start.AddDays(periods * SeasonDays).Ticks;
        data.seasonNumber += (int)periods;
        data.xp = 0;
        data.goldPass = false;
        data.freeClaimed = new bool[TierCount];
        data.goldClaimed = new bool[TierCount];
        Save();
    }

    // ------------------------------------------------------------------ queries

    public long SeasonStartTicks { get { EnsureSeason(); return data.seasonStartTicks; } }
    public int SeasonNumber { get { EnsureSeason(); return data.seasonNumber; } }
    public int Xp { get { EnsureSeason(); return data.xp; } }
    public bool GoldPassActive { get { EnsureSeason(); return data.goldPass; } }
    public int TierReached => Mathf.Min(TierCount, Xp / XpPerTier);

    public DateTime SeasonEndUtc
        => new DateTime(SeasonStartTicks, DateTimeKind.Utc).AddDays(SeasonDays);

    public string CountdownLabel()
    {
        TimeSpan left = SeasonEndUtc - DateTime.UtcNow;
        if (left.TotalDays >= 1) return (int)left.TotalDays + "D " + left.Hours + "H";
        if (left.TotalHours >= 1) return (int)left.TotalHours + "H " + left.Minutes + "M";
        return Math.Max(0, left.Minutes) + "M";
    }

    public bool IsClaimed(bool gold, int tierIdx)
        => gold ? data.goldClaimed[tierIdx] : data.freeClaimed[tierIdx];

    public bool CanCollect(bool gold, int tierIdx)
    {
        EnsureSeason();
        if (tierIdx < 0 || tierIdx >= TierCount) return false;
        if (tierIdx >= TierReached) return false;        // tier N = index N-1 → reached when TierReached > idx
        if (IsClaimed(gold, tierIdx)) return false;
        return !gold || data.goldPass;
    }

    // ------------------------------------------------------------------ mutations

    public void AddXP(int n)
    {
        EnsureSeason();
        data.xp += n;
        Save();
    }

    public bool ActivateGoldPass()
    {
        EnsureSeason();
        if (data.goldPass) return false;
        if (!RosterManager.Instance.SpendDiamonds(GoldPassPriceGems)) return false;
        data.goldPass = true;
        Save();
        return true;
    }

    // Collect a reachable, unclaimed tier reward. Returns pack grant results (for the reveal
    // overlay) or null for coin/gem rewards; null also when the collect wasn't allowed.
    public List<CardPack.GrantResult> Collect(bool gold, int tierIdx, out bool collected)
    {
        collected = false;
        if (!CanCollect(gold, tierIdx)) return null;
        if (gold) data.goldClaimed[tierIdx] = true; else data.freeClaimed[tierIdx] = true;
        Save();
        collected = true;
        PassReward r = gold ? GoldTrack[tierIdx] : FreeTrack[tierIdx];
        return MissionManager.GrantReward(r.type, r.amount);
    }
}

// ---------------------------------------------------------------------------- UI

// The Season Pass screen — code-built, hosted in NavigationManager's overlay (opened from the
// hub's SEASON ENDS IN panel). Left: Gold Pass card (ACTIVATE) + tier/XP progress + Free Pass
// note. Right: horizontal 16-tier reward track, gold row on top (padlocked until activated),
// free row below; COLLECT by tapping a glowing cell.
public class SeasonPassUI : MonoBehaviour
{
    static readonly Color Panel = new Color(0.03f, 0.05f, 0.11f, 0.92f);
    static readonly Color CardFill = new Color(0.07f, 0.12f, 0.19f, 0.97f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    static readonly Color Cyan = new Color(0f, 0.85f, 1f);
    static readonly Color Green = new Color(0.2f, 0.72f, 0.32f);
    static readonly Color Grey = new Color(0.55f, 0.6f, 0.68f);

    Transform root;
    NavigationManager nav;
    RectTransform leftColumn, trackContent;
    TextMeshProUGUI countdownLabel;

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
        bbtn.onClick.AddListener(() => { if (nav != null) nav.CloseSeasonPassScreen(); });

        MakeText(bar.transform, "SEASON PASS", 32f, new Vector2(0.5f, 0.5f), new Vector2(-60f, 0f),
                 new Vector2(340f, 50f), Color.white, TextAlignmentOptions.Center);
        countdownLabel = MakeText(bar.transform, "", 16f, new Vector2(0.5f, 0.5f), new Vector2(245f, 0f),
                                  new Vector2(250f, 30f), Cyan, TextAlignmentOptions.Center);
        if (nav != null) nav.AddCurrencyDisplay(bar.transform);

        // Static shells; live content is rebuilt on every open.
        GameObject left = new GameObject("LeftColumn");
        left.transform.SetParent(root, false);
        leftColumn = left.AddComponent<RectTransform>();
        SetRect(leftColumn, new Vector2(0.5f, 0.5f), new Vector2(-465f, -40f), new Vector2(320f, 560f));

        GameObject vp = new GameObject("TrackViewport");
        vp.transform.SetParent(root, false);
        RectTransform vrt = vp.AddComponent<RectTransform>();
        SetRect(vrt, new Vector2(0.5f, 0.5f), new Vector2(170f, -40f), new Vector2(920f, 540f));
        Image vbg = vp.AddComponent<Image>();
        vbg.color = new Color(0f, 0f, 0f, 0f);
        vp.AddComponent<RectMask2D>();

        GameObject ct = new GameObject("TrackContent");
        ct.transform.SetParent(vp.transform, false);
        trackContent = ct.AddComponent<RectTransform>();
        trackContent.anchorMin = new Vector2(0f, 0f);
        trackContent.anchorMax = new Vector2(0f, 1f);
        trackContent.pivot = new Vector2(0f, 0.5f);
        trackContent.anchoredPosition = Vector2.zero;
        trackContent.sizeDelta = new Vector2(SeasonPassManager.TierCount * 140f + 20f, 0f);

        ScrollRect scroll = vp.AddComponent<ScrollRect>();
        scroll.viewport = vrt;
        scroll.content = trackContent;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;

        Rebuild();
    }

    void OnEnable() { if (trackContent != null) Rebuild(); }

    void Rebuild()
    {
        SeasonPassManager sp = SeasonPassManager.Instance;
        countdownLabel.text = "SEASON " + sp.SeasonNumber + "  —  ENDS IN " + sp.CountdownLabel();
        for (int i = leftColumn.childCount - 1; i >= 0; i--) Destroy(leftColumn.GetChild(i).gameObject);
        for (int i = trackContent.childCount - 1; i >= 0; i--) Destroy(trackContent.GetChild(i).gameObject);
        BuildLeftColumn(sp);
        BuildTrack(sp);
    }

    void BuildLeftColumn(SeasonPassManager sp)
    {
        // Gold Pass card.
        Image goldCard = MakeCard(leftColumn, new Vector2(0f, 155f), new Vector2(310f, 240f), Gold);
        MakeText(goldCard.transform, "GOLD PASS", 26f, new Vector2(0.5f, 1f), new Vector2(0f, -36f),
                 new Vector2(280f, 34f), Gold, TextAlignmentOptions.Center);
        MakeText(goldCard.transform, "Unlocks the top reward row:\nbigger coins, gems and epic/legendary packs.",
                 14f, new Vector2(0.5f, 0.5f), new Vector2(0f, 14f), new Vector2(280f, 60f),
                 Color.white, TextAlignmentOptions.Center);
        if (sp.GoldPassActive)
            MakeText(goldCard.transform, "ACTIVE", 22f, new Vector2(0.5f, 0f), new Vector2(0f, 42f),
                     new Vector2(200f, 30f), Green, TextAlignmentOptions.Center);
        else
            MakeButton(goldCard.transform, "ACTIVATE  " + SeasonPassManager.GoldPassPriceGems + " GEMS", 16f,
                       new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(260f, 52f), Green, () =>
            {
                if (!SeasonPassManager.Instance.ActivateGoldPass()) { if (nav != null) nav.RefreshCurrency(); return; }
                if (nav != null) nav.RefreshCurrency();
                Rebuild();
            });

        // Tier / XP progress.
        Image prog = MakeCard(leftColumn, new Vector2(0f, -35f), new Vector2(310f, 110f),
                              new Color(0.227f, 0.353f, 0.478f, 1f));
        MakeText(prog.transform, "TIER " + sp.TierReached + " / " + SeasonPassManager.TierCount, 22f,
                 new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(280f, 28f), Color.white,
                 TextAlignmentOptions.Center);
        Image barBg = NewImage("XpBg", prog.transform);
        barBg.sprite = Rounded(); barBg.type = Image.Type.Sliced;
        barBg.color = new Color(0f, 0f, 0f, 0.5f);
        barBg.raycastTarget = false;
        SetRect(barBg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(260f, 16f));
        float frac = sp.TierReached >= SeasonPassManager.TierCount ? 1f
            : (sp.Xp - sp.TierReached * SeasonPassManager.XpPerTier) / (float)SeasonPassManager.XpPerTier;
        Image barFill = NewImage("XpFill", barBg.transform);
        barFill.sprite = Rounded(); barFill.type = Image.Type.Sliced;
        barFill.color = Cyan;
        barFill.raycastTarget = false;
        RectTransform frt = barFill.rectTransform;
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(Mathf.Clamp01(frac), 1f);
        frt.offsetMin = new Vector2(2f, 2f); frt.offsetMax = new Vector2(-2f, -2f);
        MakeText(prog.transform, sp.Xp + " XP", 14f, new Vector2(0.5f, 0f), new Vector2(0f, 14f),
                 new Vector2(200f, 20f), Grey, TextAlignmentOptions.Center);

        // Free Pass note. (The reference's separate Free Pass card is just the free row of the
        // same track — no second reward list; see summary.)
        Image free = MakeCard(leftColumn, new Vector2(0f, -175f), new Vector2(310f, 120f), Cyan);
        MakeText(free.transform, "FREE PASS", 20f, new Vector2(0.5f, 1f), new Vector2(0f, -28f),
                 new Vector2(280f, 26f), Cyan, TextAlignmentOptions.Center);
        MakeText(free.transform, "Always active — the bottom row of\nthe track is free for everyone.",
                 14f, new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(280f, 44f),
                 Color.white, TextAlignmentOptions.Center);
    }

    void BuildTrack(SeasonPassManager sp)
    {
        for (int i = 0; i < SeasonPassManager.TierCount; i++)
        {
            float x = 20f + 65f + i * 140f;

            MakeText(trackContent, (i + 1).ToString(), 20f, new Vector2(0f, 0.5f),
                     new Vector2(x, 245f), new Vector2(60f, 26f),
                     i < sp.TierReached ? Gold : Grey, TextAlignmentOptions.Center); // gold = reached

            BuildRewardCell(sp, true, i, new Vector2(x, 105f));   // gold row (top)
            BuildRewardCell(sp, false, i, new Vector2(x, -105f)); // free row (bottom)
        }
        MakeText(trackContent, "GOLD", 15f, new Vector2(0f, 0.5f), new Vector2(12f, 105f),
                 new Vector2(50f, 22f), Gold, TextAlignmentOptions.Center).rectTransform
                 .localRotation = Quaternion.Euler(0f, 0f, 90f);
        MakeText(trackContent, "FREE", 15f, new Vector2(0f, 0.5f), new Vector2(12f, -105f),
                 new Vector2(50f, 22f), Cyan, TextAlignmentOptions.Center).rectTransform
                 .localRotation = Quaternion.Euler(0f, 0f, 90f);
    }

    void BuildRewardCell(SeasonPassManager sp, bool gold, int tierIdx, Vector2 pos)
    {
        SeasonPassManager.PassReward r = gold ? SeasonPassManager.GoldTrack[tierIdx]
                                              : SeasonPassManager.FreeTrack[tierIdx];
        bool claimed = sp.IsClaimed(gold, tierIdx);
        bool collectable = sp.CanCollect(gold, tierIdx);
        bool lockedByPass = gold && !sp.GoldPassActive;
        bool future = tierIdx >= sp.TierReached;

        Color frameCol = collectable ? Green
                       : claimed ? new Color(0.2f, 0.24f, 0.3f, 1f)
                       : gold ? Gold : new Color(0.227f, 0.353f, 0.478f, 1f);

        Image frame = NewImage("Cell", trackContent);
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced;
        frame.color = frameCol;
        SetRect(frame.rectTransform, new Vector2(0f, 0.5f), pos, new Vector2(124f, 190f));
        Image fill = NewImage("Fill", frame.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced;
        fill.color = CardFill;
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);

        BuildRewardIcon(frame.transform, r, new Vector2(0f, 26f), claimed || future || lockedByPass ? 0.45f : 1f);

        string label = r.type == MissionRewardType.Coins ? r.amount.ToString("N0") + " COINS"
                     : r.type == MissionRewardType.Gems ? r.amount + " GEMS"
                     : CardPack.GetTierPack((CardTier)r.amount).name;
        MakeText(frame.transform, label, 13f, new Vector2(0.5f, 0f), new Vector2(0f, 46f),
                 new Vector2(114f, 34f), Color.white, TextAlignmentOptions.Center);

        if (claimed)
            MakeText(frame.transform, "CLAIMED", 13f, new Vector2(0.5f, 0f), new Vector2(0f, 18f),
                     new Vector2(110f, 20f), Grey, TextAlignmentOptions.Center);
        else if (collectable)
        {
            Button b = frame.gameObject.AddComponent<Button>();
            b.targetGraphic = frame;
            bool g = gold; int idx = tierIdx;
            b.onClick.AddListener(() =>
            {
                var results = SeasonPassManager.Instance.Collect(g, idx, out bool ok);
                if (!ok) return;
                if (nav != null) nav.RefreshCurrency();
                if (results != null && results.Count > 0) PackRevealUI.Show(root, results, null);
                Rebuild();
            });
            MakeText(frame.transform, "COLLECT", 14f, new Vector2(0.5f, 0f), new Vector2(0f, 18f),
                     new Vector2(110f, 20f), Green, TextAlignmentOptions.Center);
        }
        else if (lockedByPass)
        {
            Image lockIcon = NewImage("Lock", frame.transform);
            lockIcon.sprite = NavigationManager.MakeLockSprite();
            lockIcon.color = new Color(0.9f, 0.9f, 0.95f, 0.9f);
            lockIcon.raycastTarget = false;
            SetRect(lockIcon.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(26f, 26f));
        }
    }

    // Small procedural reward icon: coin = gold circle, gems = cyan diamond, pack = its art.
    void BuildRewardIcon(Transform parent, SeasonPassManager.PassReward r, Vector2 pos, float alpha)
    {
        Image icon = NewImage("RewardIcon", parent);
        icon.raycastTarget = false;
        if (r.type == MissionRewardType.Pack)
        {
            icon.sprite = CardPack.TierArtSprite((CardTier)r.amount);
            icon.preserveAspect = true;
            icon.color = new Color(1f, 1f, 1f, alpha);
            SetRect(icon.rectTransform, new Vector2(0.5f, 0.5f), pos, new Vector2(72f, 72f));
        }
        else if (r.type == MissionRewardType.Coins)
        {
            Sprite coin = Resources.Load<Sprite>("Sprites/gold-coin");
            if (coin != null) { icon.sprite = coin; icon.preserveAspect = true; icon.color = new Color(1f, 1f, 1f, alpha); }
            else { icon.sprite = Circle(); icon.color = new Color(1f, 0.82f, 0.2f, alpha); }
            SetRect(icon.rectTransform, new Vector2(0.5f, 0.5f), pos, new Vector2(54f, 54f));
        }
        else
        {
            Sprite gem = Resources.Load<Sprite>("Sprites/diamond-coin");
            if (gem != null) { icon.sprite = gem; icon.preserveAspect = true; icon.color = new Color(1f, 1f, 1f, alpha); }
            else
            {
                icon.sprite = Rounded(); icon.type = Image.Type.Sliced;
                icon.color = new Color(0f, 0.85f, 1f, alpha);
                icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }
            SetRect(icon.rectTransform, new Vector2(0.5f, 0.5f), pos, new Vector2(50f, 50f));
        }
    }

    // ------------------------------------------------------------------ helpers

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
        CrestUITheme.ApplyButton(img, color);
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);
        LocalizedButtonStyler.AddLabel(go.transform, label, fontSize, size,
            LocalizedButtonStyler.TextZone.NativeCenter, 1.3f);
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
