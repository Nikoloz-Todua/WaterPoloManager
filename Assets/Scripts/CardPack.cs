using System.Collections.Generic;
using UnityEngine;

// Card-pack data model + open logic — plain static C# (no MonoBehaviour), same pattern as
// LeagueSeason. Two separate pack concepts share this file:
//   • ShopPackType — the 4 purchasable shop packs (Basic/Super/Gold/Legendary). Each has a price
//     and a drop table of "chance of at least one card of X rarity" rows; opening rolls each row.
//   • CardTier — the 4 post-match reward packs (Common/Rare/Epic/Legendary "cards"). Each has an
//     unlock time and internal per-card rarity odds; opening rolls `maxCards` slots against them.
// Opening returns catalog PlayerData (never clones) — callers grant ids via RosterManager.
public enum CardTier { Common, Rare, Epic, Legendary }
public enum ShopPackType { Basic, Super, Gold, Legendary }

public static class CardPack
{
    // ---- shop packs ------------------------------------------------------------

    public class ShopPackDef
    {
        public ShopPackType type;
        public string name;
        public int priceGems;         // 0 = not buyable with gems
        public string realMoney;      // e.g. "$2.99"; null = no cash option
        public bool watchAdOption;    // Basic pack can be opened via an ad instead of gems
        public int maxCards;
        public (Rarity rarity, float chance)[] dropTable; // "at least one of X" rows, 0..1
        public string SpritePath => TierSprite(TierForArt);
        public CardTier TierForArt => (CardTier)(int)type; // Basic→Common art … Legendary→Legendary art
    }

    static readonly ShopPackDef[] ShopPacks =
    {
        new ShopPackDef { type = ShopPackType.Basic, name = "BASIC PACK", priceGems = 100,
            watchAdOption = true, maxCards = 2,
            dropTable = new[] { (Rarity.Common, 1f), (Rarity.Rare, 0.30f) } },
        new ShopPackDef { type = ShopPackType.Super, name = "SUPER PACK", priceGems = 100,
            maxCards = 3,
            dropTable = new[] { (Rarity.Common, 1f), (Rarity.Rare, 0.72f), (Rarity.Epic, 0.18f) } },
        new ShopPackDef { type = ShopPackType.Gold, name = "GOLD PACK", priceGems = 250,
            maxCards = 3,
            dropTable = new[] { (Rarity.Rare, 1f), (Rarity.Epic, 0.45f), (Rarity.Legendary, 0.09f) } },
        new ShopPackDef { type = ShopPackType.Legendary, name = "LEGENDARY PACK", priceGems = 400,
            realMoney = "$2.99", maxCards = 4,
            dropTable = new[] { (Rarity.Common, 1f), (Rarity.Rare, 0.973f),
                                (Rarity.Epic, 0.657f), (Rarity.Legendary, 1f) } },
    };

    public static ShopPackDef GetShopPack(ShopPackType t) => ShopPacks[(int)t];

    // Roll every "at least one of X" row; each hit adds one card of that rarity (capped at maxCards).
    public static List<PlayerData> OpenShopPack(ShopPackType t)
    {
        ShopPackDef def = GetShopPack(t);
        List<PlayerData> result = new List<PlayerData>();
        foreach (var (rarity, chance) in def.dropTable)
        {
            if (result.Count >= def.maxCards) break;
            if (Random.value <= chance)
            {
                PlayerData p = DrawOfRarity(rarity);
                if (p != null) result.Add(p);
            }
        }
        if (result.Count == 0) { PlayerData p = DrawOfRarity(Rarity.Common); if (p != null) result.Add(p); }
        return result;
    }

    // ---- post-match reward (tier) packs -----------------------------------------

    public class TierPackDef
    {
        public CardTier tier;
        public string name;
        public float unlockHours;
        public int maxCards;
        public (Rarity rarity, float weight)[] odds; // per-card rarity distribution, weights sum to 1
        public string SpritePath => TierSprite(tier);
        public string UnlockLabel => unlockHours >= 1f ? Mathf.RoundToInt(unlockHours) + "H"
                                                       : Mathf.RoundToInt(unlockHours * 60f) + "M";
    }

    static readonly TierPackDef[] TierPacks =
    {
        new TierPackDef { tier = CardTier.Common, name = "COMMON CARD", unlockHours = 3f, maxCards = 2,
            odds = new[] { (Rarity.Common, 0.90f), (Rarity.Rare, 0.10f) } },
        new TierPackDef { tier = CardTier.Rare, name = "RARE CARD", unlockHours = 7f, maxCards = 2,
            odds = new[] { (Rarity.Common, 0.40f), (Rarity.Rare, 0.55f), (Rarity.Epic, 0.05f) } },
        new TierPackDef { tier = CardTier.Epic, name = "EPIC CARD", unlockHours = 12f, maxCards = 3,
            odds = new[] { (Rarity.Common, 0.10f), (Rarity.Rare, 0.40f),
                           (Rarity.Epic, 0.45f), (Rarity.Legendary, 0.05f) } },
        new TierPackDef { tier = CardTier.Legendary, name = "LEGENDARY CARD", unlockHours = 24f, maxCards = 4,
            odds = new[] { (Rarity.Rare, 0.20f), (Rarity.Epic, 0.40f), (Rarity.Legendary, 0.40f) } },
    };

    public static TierPackDef GetTierPack(CardTier t) => TierPacks[(int)t];

    // First card always drops; each extra slot drops 60% of the time ("up to N players").
    public static List<PlayerData> OpenTierPack(CardTier t)
    {
        TierPackDef def = GetTierPack(t);
        List<PlayerData> result = new List<PlayerData>();
        for (int i = 0; i < def.maxCards; i++)
        {
            if (i > 0 && Random.value > 0.6f) continue;
            PlayerData p = DrawOfRarity(RollWeighted(def.odds));
            if (p != null) result.Add(p);
        }
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

    public static Color TierColor(CardTier t) => PlayerData.RarityTint((Rarity)(int)t);
}
