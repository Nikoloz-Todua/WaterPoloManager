using System.Collections.Generic;

// The player's saved state — exactly what gets written to JSON in persistentDataPath
// (guest-mode local save; Firebase sync comes later). Stores only IDs + currencies, so it
// stays tiny and human-readable. RosterManager turns these IDs into runtime PlayerData clones.
[System.Serializable]
public class Roster
{
    // Every player the user owns (starters + bench), by PlayerData.id.
    public List<string> ownedPlayerIds = new List<string>();

    // The starting seven, indexed by slot: 0 = GK, 1..6 field by position
    // (slot i holds a player whose position == (PlayerPosition)i). null = empty slot.
    public string[] starterSlots = new string[7];

    public int coins;
    public int diamonds;

    // Club identity, edited on the My Club screen. Rides in the same JSON save.
    public ClubProfile club = new ClubProfile();
}

// The player's club identity: shown in the hub's top-left profile cluster.
// clubName defaults to "Guest_XXXXXX" (RosterManager generates the suffix once, on first load).
[System.Serializable]
public class ClubProfile
{
    public string clubName = "";
    public int logoId;                          // index into ClubCustomizationUI's crest set
    public string primaryColorHex = "1E90FF";   // RRGGBB, no '#'
    public string secondaryColorHex = "FFFFFF";
    public string countryId = "";               // e.g. "GEO"; "" = not chosen yet
}
