using UnityEngine;

// One player "card" — a ScriptableObject asset (Assets → Create → Water Polo → Player,
// or generated in bulk by Tools → Generate Sample Players). This is the data foundation
// for B9 (Currencies), B12 (Team Screen) and B13 (Transfers). It is PURELY data — nothing
// here touches the live 6v6 match.
//
// Field positions (per the master plan's "Player card structure"):
//   GK goalkeeper, CB centre-back, LW/RW wings, CF centre-forward, LF/RF flats.
// The enum ORDER is also the starter-slot order in Roster.starterSlots (slot i == position i),
// so a player's natural starting slot is simply (int)position.
public enum PlayerPosition { GK, CB, LW, RW, CF, LF, RF }

// NOTE: serialized as ints in the .asset files — inserting Epic shifted Legendary from 2 to 3,
// and every existing legendary .asset was migrated (rarity: 2 → 3) when Epic was added.
public enum Rarity { Common, Rare, Epic, Legendary }

[CreateAssetMenu(fileName = "NewPlayer", menuName = "Water Polo/Player", order = 0)]
public class PlayerData : ScriptableObject
{
    [Tooltip("Unique id — also the .asset file name and the PlayerDatabase dictionary key.")]
    public string id;
    public string fullName = "New Player";
    public string nation = "—";
    public PlayerPosition position = PlayerPosition.CF;

    [Range(0, 100)] public int overall = 60;
    public Stats stats = new Stats();

    public Rarity rarity = Rarity.Common;

    [Tooltip("Card art — loaded from Firebase Storage later. Leave null for now (the UI draws a silhouette).")]
    public Sprite portrait;

    public int priceGold = 100;
    [Tooltip("True = a baked-in national-team player; false = a buyable human-roster player.")]
    public bool isBot = false;

    // The six rated attributes, each 0-100.
    [System.Serializable]
    public struct Stats
    {
        [Range(0, 100)] public int speed;
        [Range(0, 100)] public int shooting;
        [Range(0, 100)] public int passing;
        [Range(0, 100)] public int defense;
        [Range(0, 100)] public int stamina;
        [Range(0, 100)] public int goalKeeping;
    }

    // Single source of truth for the overall rating, so the sample generator and
    // RosterManager.UpgradePlayer agree. GK leans on goalkeeping; field players average
    // their outfield stats.
    public static int ComputeOverall(Stats s, PlayerPosition pos)
    {
        float v = pos == PlayerPosition.GK
            ? s.goalKeeping * 0.6f + s.defense * 0.15f + s.passing * 0.15f + s.stamina * 0.1f
            : (s.speed + s.shooting + s.passing + s.defense + s.stamina) / 5f;
        return Mathf.Clamp(Mathf.RoundToInt(v), 0, 100);
    }

    // A detached runtime copy — RosterManager owns clones of the player's OWNED cards so an
    // in-match upgrade can never write back into the source .asset (a real Unity gotcha:
    // ScriptableObject edits during Play persist in the Editor). The catalog stays pristine.
    public PlayerData Clone() => Instantiate(this);

    // Border tint for the card frame (Common grey, Rare blue, Epic purple, Legendary gold).
    public Color RarityColor => RarityTint(rarity);

    public static Color RarityTint(Rarity r) => r switch
    {
        Rarity.Legendary => new Color(1f, 0.82f, 0.2f),
        Rarity.Epic => new Color(0.61f, 0.35f, 0.71f),
        Rarity.Rare => new Color(0.25f, 0.5f, 1f),
        _ => new Color(0.6f, 0.6f, 0.62f),
    };
}
