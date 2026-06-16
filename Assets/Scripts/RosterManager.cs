using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Owns the player's Roster: guest-mode local JSON save/load in Application.persistentDataPath,
// plus the buy / sell / upgrade / set-starter economy. Self-bootstrapping singleton (no scene
// object, no Inspector wiring) that survives scene loads. Auto-saves after every mutation.
//
// Owned cards are kept as runtime CLONES of the catalog assets, so an upgrade bumps the clone
// and never corrupts the source .asset. (Upgrades therefore reset on a fresh launch — by
// design: Roster intentionally stores only IDs. Add an upgrade-levels map to Roster later to
// persist them.)
public class RosterManager : MonoBehaviour
{
    private const int DefaultCoins = 2000;
    private const int DefaultDiamonds = 75;
    private const int DefaultBenchExtras = 3;   // owned-but-not-starting players seeded at first run
    private const int StatBumpPerUpgrade = 2;   // each stat gains this per upgrade
    private const string SaveFileName = "roster.json";

    private static RosterManager instance;
    public static RosterManager Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("RosterManager");
                instance = go.AddComponent<RosterManager>(); // Awake runs now → loads the roster
            }
            return instance;
        }
    }

    private Roster roster;
    private readonly Dictionary<string, PlayerData> ownedRuntime = new Dictionary<string, PlayerData>();

    private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    public int Coins => roster != null ? roster.coins : 0;
    public int Diamonds => roster != null ? roster.diamonds : 0;

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    // ----------------------------------------------------------------- load / save

    void Load()
    {
        bool fileExisted = File.Exists(SavePath);
        roster = null;

        if (fileExisted)
        {
            try { roster = JsonUtility.FromJson<Roster>(File.ReadAllText(SavePath)); }
            catch (System.Exception e) { Debug.LogWarning("RosterManager: save unreadable, recreating. " + e.Message); }
        }

        if (roster == null) roster = new Roster();
        if (roster.ownedPlayerIds == null) roster.ownedPlayerIds = new List<string>();
        if (roster.starterSlots == null || roster.starterSlots.Length != 7) roster.starterSlots = new string[7];

        // Self-heal: an empty roster + a non-empty catalog (e.g. the game was played once before
        // the sample players existed) re-seeds the default squad instead of staying empty forever.
        bool seeded = false;
        if (roster.ownedPlayerIds.Count == 0 && PlayerDatabase.Instance.Count > 0) { SeedDefaultRoster(); seeded = true; }

        RebuildOwnedRuntime();
        if (seeded || !fileExisted) Save();
    }

    void Save()
    {
        try { File.WriteAllText(SavePath, JsonUtility.ToJson(roster, true)); }
        catch (System.Exception e) { Debug.LogWarning("RosterManager: could not save roster. " + e.Message); }
    }

    // A starting seven (one owned player per position) + a few bench players + starting funds.
    void SeedDefaultRoster()
    {
        PlayerDatabase db = PlayerDatabase.Instance;
        roster.ownedPlayerIds.Clear();
        roster.starterSlots = new string[7];

        for (int slot = 0; slot < 7; slot++)
        {
            PlayerData p = db.FirstOfPosition((PlayerPosition)slot);
            if (p == null) continue;
            if (!roster.ownedPlayerIds.Contains(p.id)) roster.ownedPlayerIds.Add(p.id);
            roster.starterSlots[slot] = p.id;
        }

        int extras = 0;
        foreach (PlayerData p in db.AllPlayers())
        {
            if (extras >= DefaultBenchExtras) break;
            if (p == null || roster.ownedPlayerIds.Contains(p.id)) continue;
            roster.ownedPlayerIds.Add(p.id);
            extras++;
        }

        roster.coins = DefaultCoins;
        roster.diamonds = DefaultDiamonds;
    }

    // Rebuild the id → clone map from ownedPlayerIds (skips ids no longer in the catalog).
    void RebuildOwnedRuntime()
    {
        ownedRuntime.Clear();
        PlayerDatabase db = PlayerDatabase.Instance;
        foreach (string id in roster.ownedPlayerIds)
        {
            if (ownedRuntime.ContainsKey(id)) continue;
            PlayerData original = db.Get(id);
            if (original != null) ownedRuntime[id] = original.Clone();
        }
    }

    // ----------------------------------------------------------------- queries

    public bool IsOwned(string id) => !string.IsNullOrEmpty(id) && roster.ownedPlayerIds.Contains(id);
    public bool IsStarter(string id) => !string.IsNullOrEmpty(id) && System.Array.IndexOf(roster.starterSlots, id) >= 0;

    // The runtime (clone) card for an owned id, or null.
    public PlayerData GetOwned(string id)
        => (id != null && ownedRuntime.TryGetValue(id, out PlayerData p)) ? p : null;

    // Owned cards in owned order (clones).
    public List<PlayerData> GetOwnedPlayers()
    {
        List<PlayerData> list = new List<PlayerData>();
        foreach (string id in roster.ownedPlayerIds)
            if (ownedRuntime.TryGetValue(id, out PlayerData p) && p != null) list.Add(p);
        return list;
    }

    // The seven starters by slot (clones; null for an empty/invalid slot).
    public PlayerData[] GetStarters()
    {
        PlayerData[] starters = new PlayerData[7];
        for (int i = 0; i < 7; i++)
        {
            string id = roster.starterSlots[i];
            if (id != null && ownedRuntime.TryGetValue(id, out PlayerData p)) starters[i] = p;
        }
        return starters;
    }

    // Average overall of the filled starter slots (0 if none).
    public int TeamOverall()
    {
        int sum = 0, n = 0;
        foreach (PlayerData p in GetStarters())
            if (p != null) { sum += p.overall; n++; }
        return n > 0 ? Mathf.RoundToInt((float)sum / n) : 0;
    }

    public int UpgradeCost(PlayerData p) => p == null ? 0 : 100 + p.overall * 5;
    public int SellValue(PlayerData p) => p == null ? 0 : p.priceGold / 2;

    // ----------------------------------------------------------------- mutations (auto-save)

    // Buy a catalog player into the roster (added to the bench). False if already owned,
    // not in the catalog, or too few coins.
    public bool BuyPlayer(string id)
    {
        if (IsOwned(id)) return false;
        PlayerData original = PlayerDatabase.Instance.Get(id);
        if (original == null || roster.coins < original.priceGold) return false;

        roster.coins -= original.priceGold;
        roster.ownedPlayerIds.Add(id);
        ownedRuntime[id] = original.Clone();
        Save();
        return true;
    }

    // Sell an owned player for SellValue; clears it from any starter slot first.
    public bool SellPlayer(string id)
    {
        if (!IsOwned(id)) return false;

        for (int i = 0; i < roster.starterSlots.Length; i++)
            if (roster.starterSlots[i] == id) roster.starterSlots[i] = null;

        roster.coins += SellValue(GetOwned(id) ?? PlayerDatabase.Instance.Get(id));
        roster.ownedPlayerIds.Remove(id);
        ownedRuntime.Remove(id);
        Save();
        return true;
    }

    // Bump every stat (clamped to 100), recompute overall, spend gold. False if not owned
    // or too few coins.
    public bool UpgradePlayer(string id)
    {
        if (!IsOwned(id)) return false;
        PlayerData p = GetOwned(id);
        if (p == null) return false;

        int cost = UpgradeCost(p);
        if (roster.coins < cost) return false;

        p.stats.speed = Mathf.Min(100, p.stats.speed + StatBumpPerUpgrade);
        p.stats.shooting = Mathf.Min(100, p.stats.shooting + StatBumpPerUpgrade);
        p.stats.passing = Mathf.Min(100, p.stats.passing + StatBumpPerUpgrade);
        p.stats.defense = Mathf.Min(100, p.stats.defense + StatBumpPerUpgrade);
        p.stats.stamina = Mathf.Min(100, p.stats.stamina + StatBumpPerUpgrade);
        p.stats.goalKeeping = Mathf.Min(100, p.stats.goalKeeping + StatBumpPerUpgrade);
        p.overall = PlayerData.ComputeOverall(p.stats, p.position);

        roster.coins -= cost;
        Save();
        return true;
    }

    // Put an owned player into a starter slot (id == null clears the slot). Removes the player
    // from any other slot first so it can't start twice. False on a bad slot or unowned id.
    public bool SetStarter(int slot, string id)
    {
        if (slot < 0 || slot >= roster.starterSlots.Length) return false;
        if (id != null && !IsOwned(id)) return false;

        if (id != null)
            for (int i = 0; i < roster.starterSlots.Length; i++)
                if (roster.starterSlots[i] == id) roster.starterSlots[i] = null;

        roster.starterSlots[slot] = id;
        Save();
        return true;
    }
}
