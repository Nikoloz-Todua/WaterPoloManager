# Player Data Audit

Audit date: 2026-08-17

Scope: current repository state only. This document describes the existing country, World Cup,
player-card, roster, pack, match-stat, and portrait architecture. It does not add national players,
Firebase fields, remote portraits, or new balancing.

## A. Country source of truth

### Authoring and runtime ownership

- The editor authoring list is `CountryCatalogBuilder.Data` in
  `Assets/Editor/CountryCatalogBuilder.cs`. It is a private static tuple array with fields
  `(string country, int rate)` and currently contains exactly 36 entries.
- `CountryCatalogBuilder.Rebuild()` writes those entries into
  `Assets/Resources/CountryCatalog.asset`. Its serialized runtime fields are
  `buildRevision`, `countries`, and `worldCupTrophy`; each `CountryCatalog.Entry` has
  `country`, `winRate`, and `flag`.
- Runtime code reads `CountryCatalog.Instance`, which calls
  `Resources.Load<CountryCatalog>("CountryCatalog")`. Runtime therefore consumes the generated
  asset, while the builder's `Data` array is the editable source used to regenerate it.
- The checked-in asset has `buildRevision: 1`, 36 entries, and the same names/rates as the builder.
- `ClubCustomizationUI.CountryNames` in `Assets/Scripts/ClubCustomizationUI.cs` is a second,
  duplicated 36-name list used by the My Club country picker. It does not contain strengths.
- There is no separate canonical country-ID field. The exact `country` display string is also the
  lookup ID used by `CountryCatalog.Get`, `WorldCupSeason.selectedCountry`, and current club data.
- There is no three-letter field in `CountryCatalog.Entry`. The tags below are legacy inputs accepted
  by `ClubCustomizationUI.NormalizeCountryName`; they are not stored in the catalog. The score HUD's
  `MatchPresentationContext.ClubAbbreviation` independently takes the first three alphanumeric
  characters, so it is not a canonical country-code system.
- Flags are resolved by filename under `Assets/Sprites/Countries`. The builder accepts `.png`,
  `.jpg`, then `.jpeg`; Sweden also has a `Swedan` compatibility fallback. Every current country has
  a real local flag. The World Cup trophy is `Assets/Sprites/Trophies/WorldCup.png`.

### Current 36-country catalog

| ID / display name | winRate | Legacy tag accepted | Current flag asset |
|---|---:|---|---|
| Georgia | 69 | GEO | `Assets/Sprites/Countries/Georgia.png` |
| Croatia | 85 | CRO | `Assets/Sprites/Countries/Croatia.png` |
| Hungary | 90 | HUN | `Assets/Sprites/Countries/Hungary.png` |
| Japan | 63 | JPN | `Assets/Sprites/Countries/Japan.png` |
| Canada | 60 | CAN | `Assets/Sprites/Countries/Canada.png` |
| UK | 48 | GBR (`UK` also accepted) | `Assets/Sprites/Countries/UK.png` |
| China | 59 | CHN | `Assets/Sprites/Countries/China.png` |
| Austria | 42 | AUT | `Assets/Sprites/Countries/Austria.jpeg` |
| Spain | 93 | ESP | `Assets/Sprites/Countries/Spain.png` |
| Serbia | 86 | SRB | `Assets/Sprites/Countries/Serbia.png` |
| USA | 76 | USA | `Assets/Sprites/Countries/USA.png` |
| Australia | 70 | AUS | `Assets/Sprites/Countries/Australia.png` |
| Israel | 51 | ISR | `Assets/Sprites/Countries/Israel.png` |
| Malta | 45 | MLT | `Assets/Sprites/Countries/Malta.png` |
| Sweden | 40 | SWE | `Assets/Sprites/Countries/Sweden.png` |
| Latvia | 38 | LVA | `Assets/Sprites/Countries/Latvia.png` |
| Italy | 82 | ITA | `Assets/Sprites/Countries/Italy.png` |
| Montenegro | 78 | MNE | `Assets/Sprites/Countries/Montenegro.png` |
| Russia | 57 | RUS | `Assets/Sprites/Countries/Russia.png` |
| Netherlands | 68 | NED | `Assets/Sprites/Countries/Netherlands.png` |
| Kazakhstan | 55 | KAZ | `Assets/Sprites/Countries/Kazakhstan.png` |
| Slovenia | 52 | SVN | `Assets/Sprites/Countries/Slovenia.png` |
| Iran | 53 | IRN | `Assets/Sprites/Countries/Iran.png` |
| Azerbaijan | 36 | AZE | `Assets/Sprites/Countries/Azerbaijan.png` |
| Armenia | 34 | ARM | `Assets/Sprites/Countries/Armenia.png` |
| France | 74 | FRA | `Assets/Sprites/Countries/France.png` |
| Greece | 88 | GRE | `Assets/Sprites/Countries/Greece.png` |
| Romania | 72 | ROU | `Assets/Sprites/Countries/Romania.png` |
| Germany | 65 | GER | `Assets/Sprites/Countries/Germany.png` |
| Turkey | 50 | TUR | `Assets/Sprites/Countries/Turkey.png` |
| Poland | 44 | POL | `Assets/Sprites/Countries/Poland.png` |
| Ukraine | 47 | UKR | `Assets/Sprites/Countries/Ukraine.png` |
| Lithuania | 41 | LTU | `Assets/Sprites/Countries/Lithuania.png` |
| Slovakia | 54 | SVK | `Assets/Sprites/Countries/Slovakia.png` |
| Mexico | 35 | MEX | `Assets/Sprites/Countries/Mexico.jpeg` |
| Portugal | 39 | POR | `Assets/Sprites/Countries/Portugal.png` |

## B. World Cup strength algorithm

The implementation is in `Assets/Scripts/WorldCupSeason.cs` and
`Assets/Scripts/TournamentCore.cs`.

- `WorldCupSeason.Simulate(LeagueSeason.Fixture fixture, bool knockout)` passes the two serialized
  `winRates` to `TournamentCore.SimulateBiased(...)` and supplies `WorldCupSeason.Next01` as the
  random-number source.
- Relative win probability is `rateA / max(1, rateA + rateB)`. A rate is not used as a direct
  percentage. For example, 90 versus 45 gives A a 90 / 135 = 66.67% winner-selection share after
  the separate draw decision.
- Group simulations first roll a fixed 12% draw chance. A simulated knockout has no draw roll.
- `baseGoals = 3 + floor(next01 * 6)`, giving 3 through 8 goals to the loser or both sides in a draw.
- For a decisive result, the winner is rolled from the relative rate formula. The margin is
  `1 + floor(next01 * 4)`, giving 1 through 4. Winner goals are
  `min(15, baseGoals + margin)`; with current ranges, generated scores are effectively 3 through 12.
- Strength changes winner probability only. It does not currently change base score, margin,
  shooting volume, or defensive concession rate.
- A player-supplied knockout draw is resolved in `WorldCupSeason.ApplyKnockout` by a separate
  50/50 `Next01() < 0.5` roll and one added goal. Simulated knockout matches already arrive
  non-drawn from `SimulateBiased`.
- Group results use `TournamentCore.ApplyGroupResult`: 3 points for a win, 1 per team for a draw.
  `TournamentCore.CompareTable` orders by points, goal difference, goals for, then ordinal country
  name.
- The persisted deterministic PRNG is an LCG in `WorldCupSeason.Next01`:
  `rngState = rngState * 1664525 + 1013904223`, then the low 24 bits divided by 16,777,216.
  `rngState` is saved in `worldcup.json`, so a saved run continues its own deterministic stream.
- A new run seeds from `DateTime.UtcNow.Ticks`, an incrementing process-local `seedCounter`, and the
  draw attempt. A zero seed is replaced by `0xA341316C`.

Implication for the next design: country player ratings can be made monotonic with each country's
`winRate` (for example, via squad-average OVR bands), but those player ratings do not feed the
current World Cup simulator unless a later task explicitly adds that connection. The existing
tournament rate algorithm does not need to change to make the two systems narratively correlate.

## C. World Cup draw algorithm

- Constants in `WorldCupSeason`: 36 teams, 6 groups, 6 teams per group, and 5 group rounds.
- `WorldCupSeason.DrawGroups` sorts all `CountryCatalog.Entry` values descending by `winRate`.
- The sorted list is divided into six consecutive pots of six teams. Each pot is independently
  shuffled by the run's seeded `WorldCupSeason.Shuffle` (Fisher-Yates using `NextInt`/`Next01`).
- Each group receives exactly one team from each pot. Storage index is
  `group * GroupSize + pot`, so the serialized `teams` array is group-major.
- `drawSignature` is `string.Join("|", teams)`. `StartNew` remembers the previous run's signature
  and makes up to 12 seeded attempts to avoid repeating it.
- `BuildGroupFixtures` uses `TournamentCore.BuildEvenRoundRobin(6)`, producing five rounds and
  three fixtures per group per round. Recording the player's group fixture simulates every other
  unplayed fixture in that same global group round.
- Qualification is the top two from each group (12) plus the best four third-place teams (4), using
  the same table comparator, for a 16-team knockout.
- `SetupRoundOf16` shuffles the six group winners and ten lower seeds. Each group winner is paired
  with a random lower seed from a different group where possible. The four remaining lower seeds
  are paired with each other, again avoiding same-group pairings where possible.
- Later knockout rounds are bracket-fed in adjacent pairs by `AdvanceRound`. If the human is
  eliminated or fails to qualify, `SimulateToEnd` completes the remaining bracket and records a
  champion.

## D. Existing player data

### `PlayerData`

Defined in `Assets/Scripts/PlayerData.cs` as a `ScriptableObject` with these actual fields:

- `string id`
- `string fullName`
- `string nation`
- `PlayerPosition position`
- `int overall` (0-100 inspector range)
- `PlayerData.Stats stats`
- `Rarity rarity`
- `Sprite portrait`
- `int priceGold`
- `bool isBot`

`PlayerData.Stats` contains exactly six integer attributes, each 0-100:
`speed`, `shooting`, `passing`, `defense`, `stamina`, and `goalKeeping`.

`PlayerPosition` is `GK, CB, LW, RW, CF, LF, RF`. Its enum order is also the seven-slot permanent
starter order in `Roster.starterSlots`.

`PlayerData.ComputeOverall` uses:

- GK: 60% goalKeeping, 15% defense, 15% passing, 10% stamina.
- Outfield: the arithmetic mean of speed, shooting, passing, defense, and stamina.

`PlayerData.Clone()` creates a detached runtime `ScriptableObject` copy so play-mode upgrades do
not write into the source asset.

### Current catalog content and loading

- `PlayerDatabase` (`Assets/Scripts/PlayerDatabase.cs`) is a lazy plain-C# singleton.
- `Reload()` loads every `PlayerData` under `Resources/Players`, keys it by `id`, and lets the last
  loaded duplicate ID win. `AllPlayers()` returns a new list; catalog order is undefined.
- The checked-in `Assets/Resources/Players` directory currently has 27 sample assets:
  5 GK, 5 CB, 4 LW, 3 RW, 5 CF, 2 LF, and 3 RF.
- Rarity distribution is 11 Common, 6 Rare, 6 Epic, and 4 Legendary.
- All 27 currently have `isBot: false` and `portrait: {fileID: 0}`.
- `Assets/Editor/SamplePlayerGenerator.cs` owns the deterministic sample `Specs`, positional stat
  profile, price formula, and idempotent asset-generation command. These are generic club cards,
  not the requested 36-country national database.

### Team UI and match squad

- `TeamScreenUI` reads owned runtime clones from `RosterManager`, shows the seven starters in
  formation, and defines the bench as owned cards not present in a starter slot. Tabs filter by
  position family; it does not own another squad save.
- `MatchPlayerState` in `Assets/MatchPlayerRuntime.cs` stores match-only identity and rules state:
  `PlayerId`, `DisplayName`, `Position`, `Overall`, `CapNumber`, `RosterData`, `Team`, `HumanTeam`,
  `RoleSlot`, `Status`, `PersonalFouls`, `PermanentlyDisqualified`, `SubstitutionPending`,
  `LegalOnField`, scripted move purpose/target/arrival/velocity, and live stamina through the body's
  existing `StaminaSystem`.
- Actual statuses are `OnField`, `Bench`, `SubstitutingOut`, `WaitingForExchange`,
  `SubstitutingIn`, `ExclusionExit`, `ExclusionWaiting`, `ExclusionReplacementApproach`,
  `ExclusionReplacementWaiting`, `ExcludedReplacedBench`, and `PermanentlyOut`.
- `MatchSquadManager` binds the six `TeamSide.members` field bodies to human starter cards, creates
  human bench bodies from owned non-starters, and creates six deterministic generic bot bench
  bodies. The bot match bench does not currently come from `PlayerDatabase`.
- Goalkeepers are separate scene `Goalkeeper` bodies, not members of the six-entry
  `TeamSide.members` field array. The current match squad builder excludes GK cards from its bench,
  so the match-only substitution system presently manages field-player swaps, not goalkeeper card
  swaps. This is an important design constraint for the next national-squad pass.
- Match substitutions never call `RosterManager.SetStarter`; match stamina, fouls, bench state, and
  removals reset with the scene/new match.

## E. Existing card rarity and pack system

There are two distinct but integer-aligned enums:

- `Rarity { Common, Rare, Epic, Legendary }` in `PlayerData.cs` is stored on player cards.
- `CardTier { Common, Rare, Epic, Legendary }` in `CardPack.cs` identifies the pack/reward tier.

`CardPack.TierColor` casts `CardTier` to `Rarity`, so changing either enum order independently would
break the visual mapping. `CardPack.TierPackDef` is the canonical pack definition.

| Pack | Unlock | Max cards | Gem price | Other purchase | Per-card rarity weights | Guarantee |
|---|---:|---:|---:|---|---|---|
| Common | 3h | 2 | 100 | Rewarded ad | 90% Common, 10% Rare | None |
| Rare | 7h | 2 | 100 | None | 40% Common, 55% Rare, 5% Epic | None |
| Epic | 12h | 3 | 250 | None | 10% Common, 40% Rare, 45% Epic, 5% Legendary | None |
| Legendary | 24h | 4 | 400 | `$2.99` | 20% Rare, 40% Epic, 40% Legendary | At least one Legendary if the catalog has one |

Pack behavior in `CardPack.OpenTierPack`:

- Slot 1 always exists. Every later slot independently appears with 60% probability, hence
  "up to N players."
- Each present slot rolls the tier's explicit rarity weights.
- `DrawOfRarity` excludes `isBot` cards. If the requested rarity has no catalog entries, it steps
  downward one rarity at a time instead of returning an empty pack.
- A Legendary pack without a rolled Legendary forces one into the first/result slot if a
  Legendary catalog card exists.
- `GrantAll` calls `RosterManager.GrantPlayer`. A duplicate grants
  `max(10, priceGold / 2)` coins instead.

Shop behavior in `Assets/Scripts/ShopUI.cs`:

- The normal pack cards use the gem/cash/ad values from `TierPackDef` above.
- Coach's Choice costs 250 gems and grants one Epic-pack opening plus 500 coins (the UI displays
  "was 400").
- Daily Deals show three of the four pack tiers, selected from UTC day plus watched refresh count,
  with 30%, 40%, or 50% discounts. Discounted gem prices are rounded to the nearest 10 with a
  minimum of 10.
- Rewarded-ad actions use `AdWatchCap.DailyCap = 3` per action ID per UTC day. The current ad and IAP
  bridges are test/stub flows, not Firebase-backed services.

Post-match reward slots in `Assets/Scripts/PostMatchRewardManager.cs`:

- Four slots are persisted separately in `rewardSlots.json`.
- A full-time match places one pack in the first empty slot as `Locked`; all-full silently skips.
- Tier roll: 80% Common, 16% Rare, 3.5% Epic, 0.5% Legendary.
- `SlotState` is `Empty`, `Locked`, or `Unlocking`; Ready is derived from Unlocking plus elapsed UTC
  time. `AnyUnlocking` enforces the UI rule that only one unfinished timer runs at once.
- Opening delegates to the same `CardPack.OpenTierPack` odds/guarantees as shop packs.

## F. Club ownership model

- `RosterManager` saves `Roster` as
  `Path.Combine(Application.persistentDataPath, "roster.json")` using `JsonUtility`.
- `Roster.ownedPlayerIds` is the complete ordered list of club-owned card IDs, including starters
  and bench.
- `Roster.starterSlots` is a seven-string array: index 0 GK, then positions in `PlayerPosition`
  enum order. A null entry is an empty starter slot.
- There is no persisted bench array. "Bench" means an owned ID not currently present in
  `starterSlots`.
- The same JSON also stores `coins`, `diamonds`, and `ClubProfile` (`clubName`, `logoId`, crest
  colors, cap/swimwear colors, and exact-name `countryId`).
- At load, `RosterManager.RebuildOwnedRuntime` clones each owned catalog asset into the
  `ownedRuntime` dictionary. Queries and UI use those clones.
- New installs seed one catalog player per position, three additional owned non-starters,
  2,000 coins, and 75 diamonds.
- `SetStarter` prevents a duplicate starter and requires ownership, but the manager itself does not
  verify that the ID's position matches the target slot; `TeamScreenUI` presents compatible choices.
- `UpgradePlayer` spends `100 + overall * 5` coins, adds 2 to every stat (clamped to 100), and
  recomputes OVR. However, upgrade levels/stat deltas are not fields in `Roster`. `Save()` therefore
  writes no upgrade data, and the runtime clone returns to catalog stats on a fresh launch. This is
  explicitly acknowledged in current source comments and must be addressed before relying on
  persistent national/card development.
- Live match substitutions, stamina, personal fouls, exclusions, and match statuses exist only on
  `MatchPlayerState`/scene bodies and never modify `roster.json`.

For National Team mode, national-player IDs should therefore live in a separate competition squad
selection/save model, not be appended to `Roster.ownedPlayerIds`, unless a later design explicitly
awards a separate club card.

## G. Match stat connections

### Current card-to-PoolB connection

No individual `PlayerData.Stats` field currently affects PoolB gameplay. Repository-wide runtime
search finds card-stat reads only in `RosterManager.UpgradePlayer`; match code does not read
`RosterData.stats.speed`, `.shooting`, `.passing`, `.defense`, `.stamina`, or `.goalKeeping`.

The card data that currently reaches a match is:

- ID/name/cap/position for identity and UI.
- `Position` for field role assignment, goalkeeper-versus-outfield compatibility, and substitution
  role-family preference.
- `Overall` only as a weighted tie/quality term in `MatchSquadManager.BestBenchReplacement`.
- The card's `stamina` stat does not seed `StaminaSystem`; every body starts its match at that
  component's `maxStamina` (default 100).

### Actual live gameplay tunings

- Human movement/shoot/pass/steal are serialized values on `PlayerMovement` (`moveSpeed`,
  `holdMoveSpeed`, shoot powers/bonuses, pass speeds, `stealChance`, and related ranges).
- Bot and teammate movement, shooting, and stealing are serialized on `BotMovement` and
  `TeammateAI` (`chaseSpeed`, `carrySpeed`, `supportSpeed`, `shootRange`, `shootPower`,
  `stealChance`, and distances). `WaterPoloBrain` consumes the `IAgentBody` values.
- Goalkeeper movement/save behavior is serialized directly on `Goalkeeper` (`trackSpeed`,
  `baseSaveChance`, shot penalties, grab distances, and other keeper tunings).
- `StaminaSystem` is a real match-runtime energy model. It begins at 100, drains/replenishes from
  movement/activity, and applies speed/steal multipliers: below 40% speed is 0.8; below 20% speed is
  0.6 and steal is 0.8; at 0 sprint is blocked. Bench/rest states recover at 15 per real second.
  Goalkeeper exhaustion applies save penalties in `Goalkeeper`. This state remains on a body through
  match substitutions but is unrelated to the card's `stats.stamina` value.

The next player-data design therefore needs an explicit, centralized match-stat application layer
if card attributes are intended to affect PoolB. It should map card data once when binding a match
participant rather than sprinkling card reads through movement/AI scripts.

## H. Portrait and visual architecture

- The only current portrait field is the local Unity `Sprite PlayerData.portrait`; there is no URL,
  version, cache key, or Firebase metadata field.
- All 27 current cards have a null portrait.
- `TeamScreenUI.MakePortrait` uses the sprite when present; otherwise `TeamScreenUI.Silhouette()`
  procedurally creates and caches a neutral 96x96 head-and-shoulders sprite inside a grey circle.
- In-water visuals are shared. `PlayerAnimator` and `BotAnimator` load the common
  `Assets/Resources/PlayerFlipbookSet.asset` when no override is assigned. The set contains six-frame
  idle, horizontal swim, optional up/down swim, holding, and throwing arrays; directional swimming
  is currently opt-in and disabled in the asset.
- `PlayerAnimator`/`BotAnimator` create shared visual renderers and palette-material instances.
  Team/club cap and swimwear colors are palette swaps; individual athletes do not currently have
  unique in-water faces, bodies, portraits, or animation sheets.

A future remote portrait can be added as card/presentation metadata (for example URL plus content
version/cache key) and resolved into UI sprite caching without changing `PlayerFlipbookSet`,
`PlayerAnimator`, `BotAnimator`, or the shared swimmer sprites. That separation should be retained.

## I. Files to upload for player database design

Upload this audit plus the following minimum source set:

1. `PLAYER_DATA_AUDIT.md`
2. `Assets/Editor/CountryCatalogBuilder.cs`
3. `Assets/Scripts/CountryCatalog.cs`
4. `Assets/Scripts/WorldCupSeason.cs`
5. `Assets/Scripts/TournamentCore.cs`
6. `Assets/Scripts/WorldCupUI.cs`
7. `Assets/Scripts/PlayerData.cs`
8. `Assets/Scripts/PlayerDatabase.cs`
9. `Assets/Editor/SamplePlayerGenerator.cs`
10. `Assets/Scripts/Roster.cs`
11. `Assets/Scripts/RosterManager.cs`
12. `Assets/Scripts/TeamScreenUI.cs`
13. `Assets/Scripts/CardPack.cs`
14. `Assets/Scripts/PostMatchRewardManager.cs`
15. `Assets/Scripts/ShopUI.cs`
16. `Assets/MatchPlayerRuntime.cs`
17. `Assets/StaminaSystem.cs`

`Assets/NavigationManager.cs` is not required for the data-model/rating design: it instantiates the
World Cup and Team screens and renders reward slots, but it does not own country rates, player
fields, pack odds, or roster persistence. Include it only if the next conversation also designs the
hub navigation/wiring implementation. Likewise, the generated `CountryCatalog.asset` and all 27
sample `.asset` files are unnecessary because the exact catalog and generator rules are captured by
the builder/source files and this audit.
