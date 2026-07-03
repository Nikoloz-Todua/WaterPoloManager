using System.Collections.Generic;
using UnityEngine;

// Card-pack data model + open logic — plain static C# (no MonoBehaviour), same pattern as
// LeagueSeason. ONE pack identity system: CardTier (Common/Rare/Epic/Legendary "cards").
// The same 4 packs are sold in the shop (instant open, gem/ad/cash prices) and earned as
// post-match reward slots (timed unlock). Each tier has ONE odds table — the per-card rarity
// weights — used for every open and shown by PackInfoPopup everywhere an "i" button exists.
// Opening returns catalog PlayerData (never clones) — callers grant ids via RosterManager.
public enum CardTier { Common, Rare, Epic, Legendary }

public static class CardPack
{
    public class TierPackDef
    {
        public CardTier tier;
        public string name;
        public float unlockHours;        // reward-slot unlock duration
        public int maxCards;
        public int priceGems;            // shop gem price (instant open)
        public string realMoney;         // e.g. "$2.99"; null = no cash option
        public bool watchAdOption;       // Common card can be opened via an ad instead of gems
        public bool guaranteedLegendary; // Legendary card: every open contains a Legendary
        public (Rarity rarity, float weight)[] odds; // per-card rarity distribution, weights sum to 1
        public string SpritePath => TierSprite(tier);
        public string UnlockLabel => unlockHours >= 1f ? Mathf.RoundToInt(unlockHours) + "H"
                                                       : Mathf.RoundToInt(unlockHours * 60f) + "M";
    }

    static readonly TierPackDef[] TierPacks =
    {
        new TierPackDef { tier = CardTier.Common, name = "COMMON CARD", unlockHours = 3f, maxCards = 2,
            priceGems = 100, watchAdOption = true,
            odds = new[] { (Rarity.Common, 0.90f), (Rarity.Rare, 0.10f) } },
        new TierPackDef { tier = CardTier.Rare, name = "RARE CARD", unlockHours = 7f, maxCards = 2,
            priceGems = 100,
            odds = new[] { (Rarity.Common, 0.40f), (Rarity.Rare, 0.55f), (Rarity.Epic, 0.05f) } },
        new TierPackDef { tier = CardTier.Epic, name = "EPIC CARD", unlockHours = 12f, maxCards = 3,
            priceGems = 250,
            odds = new[] { (Rarity.Common, 0.10f), (Rarity.Rare, 0.40f),
                           (Rarity.Epic, 0.45f), (Rarity.Legendary, 0.05f) } },
        new TierPackDef { tier = CardTier.Legendary, name = "LEGENDARY CARD", unlockHours = 24f, maxCards = 4,
            priceGems = 400, realMoney = "$2.99", guaranteedLegendary = true,
            odds = new[] { (Rarity.Rare, 0.20f), (Rarity.Epic, 0.40f), (Rarity.Legendary, 0.40f) } },
    };

    public static TierPackDef GetTierPack(CardTier t) => TierPacks[(int)t];

    // First card always drops; each extra slot drops 60% of the time ("up to N players").
    public static List<PlayerData> OpenTierPack(CardTier t)
    {
        TierPackDef def = GetTierPack(t);
        List<PlayerData> result = new List<PlayerData>();
        bool gotLegendary = false;
        for (int i = 0; i < def.maxCards; i++)
        {
            if (i > 0 && Random.value > 0.6f) continue;
            PlayerData p = DrawOfRarity(RollWeighted(def.odds));
            if (p != null)
            {
                result.Add(p);
                if (p.rarity == Rarity.Legendary) gotLegendary = true;
            }
        }
        // Legendary card promise: if the rolls came up short, force a Legendary into the pack.
        if (def.guaranteedLegendary && !gotLegendary)
        {
            PlayerData p = DrawOfRarity(Rarity.Legendary);
            if (p != null)
            {
                if (result.Count >= def.maxCards) result[0] = p;
                else result.Insert(0, p);
            }
        }

        // The ONE pack-open completion point (shop buys, reward slots, mission/pass rewards all
        // come through here) → mission stat.
        MissionManager.Instance.RecordStat("packs_opened");
        return result;
    }

    static Rarity RollWeighted((Rarity rarity, float weight)[] odds)
    {
        float roll = Random.value, acc = 0f;
        foreach (var (rarity, weight) in odds)
        {
            acc += weight;
            if (roll <= acc) return rarity;
        }
        return odds[odds.Length - 1].rarity;
    }

    // Random catalog player of the given rarity (bots excluded). If the catalog has none of that
    // rarity yet (e.g. Epic before the sample players are regenerated), steps DOWN a tier so a
    // pack never comes up empty.
    static PlayerData DrawOfRarity(Rarity r)
    {
        for (int rr = (int)r; rr >= 0; rr--)
        {
            List<PlayerData> pool = new List<PlayerData>();
            foreach (PlayerData p in PlayerDatabase.Instance.AllPlayers())
                if (p != null && !p.isBot && p.rarity == (Rarity)rr) pool.Add(p);
            if (pool.Count > 0) return pool[Random.Range(0, pool.Count)];
        }
        return null;
    }

    // Grant a pack's cards to the roster. Duplicates (already owned) convert to coins instead —
    // the reveal UI shows "NEW" vs "+N coins" per card from the returned results.
    public struct GrantResult { public PlayerData player; public bool isNew; public int dupCoins; }

    public static List<GrantResult> GrantAll(List<PlayerData> cards)
    {
        RosterManager rm = RosterManager.Instance;
        List<GrantResult> results = new List<GrantResult>();
        foreach (PlayerData p in cards)
        {
            if (p == null) continue;
            bool added = rm.GrantPlayer(p.id);
            int coins = 0;
            if (!added) { coins = Mathf.Max(10, p.priceGold / 2); rm.AddCoins(coins); }
            results.Add(new GrantResult { player = p, isNew = added, dupCoins = coins });
        }
        return results;
    }

    public static string TierSprite(CardTier t) => t switch
    {
        CardTier.Legendary => "Sprites/legendary-card",
        CardTier.Epic => "Sprites/epic-card",
        CardTier.Rare => "Sprites/rare-card",
        _ => "Sprites/common-card",
    };

    // Pack-art sprite, trimmed to its visible content. The ONE loader every pack-art Image
    // should use. Loads the raw Texture2D (immune to the texture's sprite-slicing import mode —
    // a Multiple-mode auto-slice once made rare-card load as a tiny fragment) and crops the
    // sprite rect to the alpha bounding box, so source PNGs with different padding/aspect all
    // fill a fixed UI box consistently. Cached per tier.
    static readonly Dictionary<CardTier, Sprite> artCache = new Dictionary<CardTier, Sprite>();
    public static Sprite TierArtSprite(CardTier t)
    {
        if (artCache.TryGetValue(t, out Sprite cached) && cached != null) return cached;
        string path = TierSprite(t);
        Texture2D tex = Resources.Load<Texture2D>(path);
        if (tex == null)
        {
            Sprite direct = Resources.Load<Sprite>(path); // last resort: whatever the importer made
            if (direct != null) artCache[t] = direct;
            else Debug.LogWarning("CardPack: pack art not found at Resources/" + path);
            return direct;
        }

        Rect rect = new Rect(0f, 0f, tex.width, tex.height);
        try
        {
            // Tight alpha bounds (threshold cuts the faint outer glow, keeps the bag).
            Color32[] px = tex.GetPixels32();
            const byte cut = 40;
            int minX = tex.width, minY = tex.height, maxX = -1, maxY = -1;
            for (int y = 0; y < tex.height; y++)
                for (int x = 0; x < tex.width; x++)
                    if (px[y * tex.width + x].a > cut)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            if (maxX > minX && maxY > minY)
                rect = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
        catch { /* texture not readable → keep the full frame (still correct, just untrimmed) */ }

        Sprite sp = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        artCache[t] = sp;
        return sp;
    }

    public static Color TierColor(CardTier t) => PlayerData.RarityTint((Rarity)(int)t);
}
