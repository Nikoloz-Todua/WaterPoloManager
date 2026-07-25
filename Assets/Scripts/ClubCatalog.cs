using System;
using System.Collections.Generic;
using UnityEngine;

// Bundled club data is the offline source of truth. A future Firebase patch may override metadata,
// but matches never depend on a network image request.
[CreateAssetMenu(fileName = "ClubCatalog", menuName = "Water Polo/Club Catalog")]
public class ClubCatalog : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string id;
        public string displayName;
        public string level;
        public Sprite logo;
    }

    [SerializeField] private int buildRevision;
    [SerializeField] private List<Entry> clubs = new List<Entry>();
    [SerializeField] private Sprite division1Trophy;
    [SerializeField] private Sprite premierLeagueTrophy;
    [SerializeField] private Sprite continentalCupTrophy;
    [SerializeField] private Sprite championsLeagueTrophy;
    [SerializeField] private Sprite goldMedal;
    [SerializeField] private Sprite silverMedal;
    [SerializeField] private Sprite bronzeMedal;
    private Dictionary<string, Entry> byId;
    private static ClubCatalog instance;

    public int BuildRevision => buildRevision;

    public static ClubCatalog Instance
    {
        get
        {
            if (instance == null) instance = Resources.Load<ClubCatalog>("ClubCatalog");
            return instance;
        }
    }

    public Sprite LogoFor(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (byId == null)
        {
            byId = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (Entry entry in clubs)
                if (entry != null && !string.IsNullOrEmpty(entry.id)) byId[entry.id] = entry;
        }
        return byId.TryGetValue(id, out Entry club) ? club.logo : null;
    }

    public Sprite TrophyFor(int competition) => competition == 0 ? division1Trophy : competition == 1 ? premierLeagueTrophy : competition == 2 ? continentalCupTrophy : championsLeagueTrophy;
    public Sprite MedalFor(int rank) => rank == 1 ? goldMedal : rank == 2 ? silverMedal : rank == 3 ? bronzeMedal : null;
}
