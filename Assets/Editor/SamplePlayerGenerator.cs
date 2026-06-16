using UnityEditor;
using UnityEngine;

// Editor tool: generates a spread of sample PlayerData .asset files into Resources/Players/
// so the Team / Transfers screens have real data to show. Same pattern as
// AnimationClipBuilder / GoalkeeperAnimationBuilder. Idempotent — re-running refreshes the
// existing assets in place (stats are derived deterministically, so values never drift).
//   Tools/Generate Sample Players
public static class SamplePlayerGenerator
{
    const string Dir = "Assets/Resources/Players";

    struct Spec
    {
        public string id, name, nation;
        public PlayerPosition pos;
        public Rarity rarity;
        public int tier; // base stat level before the position profile
        public Spec(string id, string name, string nation, PlayerPosition pos, Rarity rarity, int tier)
        { this.id = id; this.name = name; this.nation = nation; this.pos = pos; this.rarity = rarity; this.tier = tier; }
    }

    static readonly Spec[] Specs =
    {
        // GK
        new Spec("gk_silva",   "R. Silva",    "Brazil",     PlayerPosition.GK, Rarity.Common,    66),
        new Spec("gk_muller",  "K. Mueller",  "Germany",    PlayerPosition.GK, Rarity.Common,    69),
        new Spec("gk_volkov",  "A. Volkov",   "Russia",     PlayerPosition.GK, Rarity.Rare,      74),
        new Spec("gk_kovac",   "I. Kovac",    "Croatia",    PlayerPosition.GK, Rarity.Legendary, 86),
        // CB
        new Spec("cb_lopez",   "F. Lopez",    "Spain",      PlayerPosition.CB, Rarity.Common,    62),
        new Spec("cb_horvat",  "M. Horvat",   "Croatia",    PlayerPosition.CB, Rarity.Common,    64),
        new Spec("cb_garcia",  "L. Garcia",   "Spain",      PlayerPosition.CB, Rarity.Rare,      72),
        new Spec("cb_njegus",  "S. Njegus",   "Serbia",     PlayerPosition.CB, Rarity.Legendary, 84),
        // LW
        new Spec("lw_kim",     "J. Kim",      "Korea",      PlayerPosition.LW, Rarity.Common,    64),
        new Spec("lw_costa",   "M. Costa",    "Portugal",   PlayerPosition.LW, Rarity.Common,    68),
        new Spec("lw_rossi",   "G. Rossi",    "Italy",      PlayerPosition.LW, Rarity.Rare,      75),
        // RW
        new Spec("rw_smith",   "J. Smith",    "USA",        PlayerPosition.RW, Rarity.Common,    65),
        new Spec("rw_takeda",  "H. Takeda",   "Japan",      PlayerPosition.RW, Rarity.Rare,      73),
        // CF
        new Spec("cf_oconnor", "S. O'Connor", "Ireland",    PlayerPosition.CF, Rarity.Common,    63),
        new Spec("cf_petrov",  "D. Petrov",   "Russia",     PlayerPosition.CF, Rarity.Common,    71),
        new Spec("cf_milos",   "N. Milos",    "Montenegro", PlayerPosition.CF, Rarity.Rare,      78),
        new Spec("cf_tanaka",  "K. Tanaka",   "Japan",      PlayerPosition.CF, Rarity.Legendary, 88),
        // LF
        new Spec("lf_dubois",  "P. Dubois",   "France",     PlayerPosition.LF, Rarity.Common,    67),
        new Spec("lf_weber",   "T. Weber",    "Germany",    PlayerPosition.LF, Rarity.Rare,      74),
        // RF
        new Spec("rf_ivanov",  "V. Ivanov",   "Russia",     PlayerPosition.RF, Rarity.Common,    66),
        new Spec("rf_nagy",    "B. Nagy",     "Hungary",    PlayerPosition.RF, Rarity.Legendary, 85),
    };

    [MenuItem("Tools/Generate Sample Players")]
    public static void Generate()
    {
        EnsureFolder(Dir);
        foreach (Spec s in Specs) CreateOrUpdate(s);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SamplePlayerGenerator] {Specs.Length} sample players written to {Dir}.");
    }

    static void CreateOrUpdate(Spec spec)
    {
        string path = $"{Dir}/{spec.id}.asset";
        PlayerData asset = AssetDatabase.LoadAssetAtPath<PlayerData>(path);
        bool isNew = asset == null;
        if (isNew) asset = ScriptableObject.CreateInstance<PlayerData>();

        asset.id = spec.id;
        asset.fullName = spec.name;
        asset.nation = spec.nation;
        asset.position = spec.pos;
        asset.rarity = spec.rarity;
        asset.stats = Profile(spec.pos, spec.tier);
        asset.overall = PlayerData.ComputeOverall(asset.stats, spec.pos);
        asset.priceGold = Price(asset.overall, spec.rarity);
        asset.isBot = false;
        asset.portrait = null;

        if (isNew) AssetDatabase.CreateAsset(asset, path);
        else EditorUtility.SetDirty(asset);
    }

    // Stats derived from a base tier + a per-position emphasis (deterministic → idempotent).
    static PlayerData.Stats Profile(PlayerPosition pos, int b)
    {
        PlayerData.Stats s = new PlayerData.Stats
        {
            speed = b, shooting = b, passing = b, defense = b, stamina = b, goalKeeping = Clamp(b - 25)
        };
        switch (pos)
        {
            case PlayerPosition.GK:
                s.goalKeeping = Clamp(b + 8); s.defense = Clamp(b); s.passing = Clamp(b - 8);
                s.speed = Clamp(b - 10); s.shooting = Clamp(b - 15); s.stamina = Clamp(b - 2); break;
            case PlayerPosition.CF:
                s.shooting = Clamp(b + 8); s.passing = Clamp(b + 4); s.speed = Clamp(b + 2); break;
            case PlayerPosition.LW:
            case PlayerPosition.RW:
                s.speed = Clamp(b + 8); s.shooting = Clamp(b + 3); break;
            case PlayerPosition.LF:
            case PlayerPosition.RF:
                s.passing = Clamp(b + 6); s.defense = Clamp(b + 3); break;
            case PlayerPosition.CB:
                s.defense = Clamp(b + 10); s.stamina = Clamp(b + 4); s.shooting = Clamp(b - 6); break;
        }
        return s;
    }

    static int Price(int overall, Rarity r)
    {
        float mult = r == Rarity.Legendary ? 2.6f : r == Rarity.Rare ? 1.6f : 1.0f;
        return Mathf.RoundToInt(overall * 8 * mult / 10f) * 10; // nearest 10 gold
    }

    static int Clamp(int x) => Mathf.Clamp(x, 0, 100);

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        string leaf = path.Substring(path.LastIndexOf('/') + 1);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
