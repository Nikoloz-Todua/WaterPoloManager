using System.Collections.Generic;
using UnityEngine;

// The read-only player CATALOG: every PlayerData asset under a Resources/Players/ folder,
// loaded once and keyed by id. Plain C# singleton (no scene object) — it lazily loads the
// first time anything touches PlayerDatabase.Instance. Both buyable human cards and (future)
// baked-in bot players live here; RosterManager works on CLONES so the catalog stays pristine.
public class PlayerDatabase
{
    private static PlayerDatabase instance;
    public static PlayerDatabase Instance => instance ??= new PlayerDatabase();

    private readonly Dictionary<string, PlayerData> byId = new Dictionary<string, PlayerData>();

    public int Count => byId.Count;

    private PlayerDatabase() { Reload(); }

    // (Re)load all PlayerData assets from Resources/Players/.
    public void Reload()
    {
        byId.Clear();
        PlayerData[] all = Resources.LoadAll<PlayerData>("Players");
        foreach (PlayerData p in all)
        {
            if (p == null || string.IsNullOrEmpty(p.id)) continue;
            byId[p.id] = p; // last one wins on a duplicate id
        }
        if (all.Length == 0)
            Debug.LogWarning("PlayerDatabase: no players in Resources/Players/ — run Tools → Generate Sample Players.");
    }

    public PlayerData Get(string id)
        => (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out PlayerData p)) ? p : null;

    public bool Has(string id) => !string.IsNullOrEmpty(id) && byId.ContainsKey(id);

    // A fresh list of every catalog card (catalog order is undefined; callers that care sort).
    public List<PlayerData> AllPlayers() => new List<PlayerData>(byId.Values);

    // First catalog card of a given position, or null — used to seed the default roster.
    public PlayerData FirstOfPosition(PlayerPosition pos)
    {
        foreach (PlayerData p in byId.Values)
            if (p != null && p.position == pos) return p;
        return null;
    }
}
