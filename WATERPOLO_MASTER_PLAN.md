# Water Polo Manager — MASTER PLAN & HANDOFF (Unity 2D)

> **This is the single source of truth for the project.** It merges the full feature vision with the current build reality.
> Drop this whole file into any new Claude/AI chat and it will have complete context. Read top to bottom.
> ⚠️ This is a **Unity 6 / C#** game. An earlier version of this plan said "Swift / SDL2 / SceneKit / LLM bots" — **that tech is DEAD and abandoned.** Ignore any Swift/SDL2/SceneKit/LLM-bot references anywhere. The real engine is Unity, the real AI is C# state machines.

---

## ⭐ HOW TO READ THIS FILE

- **PART A** = current reality: tech, dev, environment, Git, what's built, scene wiring, what's next. Pinned to the truth.
- **PART B** = the full feature vision (the whole game design). Each section is tagged:
  - ✅ **DONE** — built and working
  - 🟡 **PARTIAL** — some of it exists
  - ⬜ **NOT STARTED** — future work
- Features in Part B are the destination. Part A is where we actually are. They don't contradict; Part B is "the dream," Part A is "today."

---

# PART A — CURRENT REALITY

## A1. What this is

A **2D top-down water polo game** in **Unity 6 (6000.4.7f1), C#**, targeting **Android + iOS** (later). Originally conceived as a 3D Dream-League-style game; **retargeted to 2D** for solo-dev scope and hardware. Built brick-by-brick with step-by-step guidance.

**Planned backend (not yet integrated):** **Firebase** — Auth (Google / Apple / Email + Guest mode), Firestore (player data + remote config + admin control), Storage (player card images), cloud sync of roster/currencies/career on login with local-JSON-first saves. See **Player System Architecture** (end of Part A) for the full design. Payments stay Apple/Google in-app billing (A3 rule 5).

**Current state: a working 6v6 match with full defensive AI and a complete Visual Pass 1 animation system.** Core gameplay works (role-based positioning, marking, dynamic mark-switching, press/zone toggle, sprint mechanic, steal animation, proximity-based defend animation); menus/economy/career not built yet. Animated pool water background working via a URP Shader Graph material (ShaderWaterMaterial / WaterGraph.shadergraph) on the PoolWater object — see A8.

## A2. Developer & environment

- **Nikoloz Todua** — solo dev. Strong C#, intermediate Unity. **Needs beginner-level, step-by-step Unity navigation** ("top menu → GameObject → 2D Object → Sprites → Square"; "Hierarchy = left list, Inspector = right panel, Project = bottom").
- **Machine:** HP EliteBook, Windows 10, VS Code with **PowerShell** terminal.
- **GitHub:** https://github.com/Nikoloz-Todua
- **Repo:** https://github.com/Nikoloz-Todua/WaterPoloManager (private)
- **Project folder:** `C:\Users\PC\Desktop\WaterPoloManager`
- **Claude Code:** installed in VS Code, usable any time from the terminal. Use it for **multi-file / whole-system AI refactors** (reads all scripts at once). Use chat for **guidance, planning, single-file features, and explaining what Claude Code did.**

## A3. Critical rules (dev preferences)

1. **Never use PowerShell `echo >` to create files** — UTF-16 BOM breaks things. Use VS Code "New File" or `Set-Content -Encoding ascii`.
2. Default terminal = **PowerShell**.
3. Explain Unity steps like a beginner; name panels and exact menu paths.
4. **After replacing any full script, remind him to re-check drag-and-drop slots** — they can silently empty on a refactor. This has bitten us repeatedly. Tell him exactly which object + which slot.
5. Mobile payments later = **Apple/Google in-app billing** (store policy). NOT BOG/Stripe/PayPal. (BOG is only for his separate web projects.)
6. Tone: direct, blunt, intermediate-level.

## A4. Saving / pushing to GitHub

VS Code **PowerShell** terminal, from project root:
```powershell
git add .
git commit -m "describe what changed"
git push
```
Auth is set up (Git Credential Manager). `.gitignore` excludes `Library/`, `Temp/`, `Logs/`, etc.; only `Assets/`, `Packages/`, `ProjectSettings/` are tracked.

## A5. Scripts (all in `Assets/`)

| File | Role |
|---|---|
| `PlayerMovement.cs` | Human control of the active player: move, grab (E), **charged shoot** (hold Space; time-based `shotChargeTime` 0.7s, min-speed floor so a tap never drops), aim chevron + **power bar** (world-unit `powerBarWidth` 1.2 — >2× the keeper bar, grows left→right), **directional charged pass** (hold B — fires where the facing triangle/joystick points with a tunable `passAssist`, NOT auto-homed; `FindPassAssistTarget` scores teammates by dot with `lastDirection`). **Shot height** (`shotHeight` 0..1, charges in lock-step with power: low 0–0.3 / mid / high 0.7–1; read by Goalkeeper + GoalkeeperAnimator for the dive tier; **charge >0.7 releases as the untouchable ASYMMETRIC shot arc** landing 1.5u before the aimed goal line — `HighShotLandPoint`, raw aim, no assist). **Shots ×1.35 code-side speed** (`ShotSpeedMult` — shots always outpace passes; serialized `maxShootPower` 12 untouched). **Skip shot** (hold Q while charging Space → fast LOW bounce shot via `BallFlight`). **Every B pass arcs** (`ArcKind.Pass` small hop to the assist target, else a charge-scaled 3.5–6.5u spot along the aim); **F+B = the big high LOB** (`ArcKind.Lob`, ×0.7 speed) — both untouchable mid-flight (nobody intercepts an airborne ball; contests happen at the landing). **Charge bar reads shot-vs-pass:** pass = cool blue→cyan; shot = green→yellow→red strobing white past 0.7 (the high-shot zone). Ball held via **parenting**; reports possession to MatchContext. `TakeOverHeldBall()` for clean control transfer; `TouchBlockSteal()` (Block button — half steal chance, 50% foul-on-miss). **Stamina hooks** (`StaminaSpeedMult`/`StaminaSprintMult`/`StaminaSprintBlocked`/`StaminaStealMult`/`StaminaPercent01`, neutral 1 by default). **Steal feedback (2026-07-09f):** the 09c whiff puff is REMOVED (dev read it as a phantom ball) — an out-of-range Space/BLOCK press keeps only the pre-existing snatch-anim lunge; in-range attempts still always play the snatch anim before the roll. `stealDistance` **1.5** / `stealChance` **0.4** (scene + code aligned; the scene's serialized 1.2/0.2 were why real presses whiffed by range and misses dominated). Both steal paths skip a **`MatchContext.IsFoulProtected`** carrier (the 5s post-foul shield). |
| `TeammateAI.cs` | Thin component on each player. When NOT human-controlled, runs the shared `WaterPoloBrain`. Implements `IAgentBody`. |
| `BotMovement.cs` | Thin component on each bot. Always runs `WaterPoloBrain`. Implements `IAgentBody`. |
| `WaterPoloAI.cs` | **The shared brain** + `IAgentBody` interface. All AI decisions live here once: carrier (shoot/pass/**drive**/dribble), support (get open), presser (nearest chases), defender (hold shape). 🟡 New: **drives** (beaten marker + clear lane → burst to 2m, shoot/kick-out/abort) and **picks/screens** (nominated screener plants on the carrier's marker; rubbing past = short "beaten" boost). Works, needs tuning. **This is C# state-machine AI — NOT an LLM.** **2026-07-09g:** positioning RETENTION (for shape only, a loose ball still "belongs" to `LastTouchTeam` — the attacking team holds its spread through pass flights while its closest member chases the reception; no more whole-team defensive collapse on every pass) + shared positional catch rule **`CanCatchLooseBall`** (flying ball >2.5 u/s → 0.6u reach + must face it; settled ball keeps the full grab radius) + `MinTeammateSeparation` 1.5. |
| `TeamSide.cs` | One per team. Holds goals + roster (`members`), formation math (auto-spreads ANY number of players), passing/positioning logic, **attacking-spacing + tactics tunables (center-feed, counter, shot-quality threshold, free-throw clearance), shot-quality + pass-risk scoring, and 4 defense modes — Press/Zone/Drop/MPress — incl. man-up 4-2 umbrella + man-down zone shapes**. 🟡 New: **dynamic Centre** (fights for inside water goal-side of its guard at 2m), wider lanes + weak-side wing drift, receiver-shot-quality pass bonus, drive/screen helpers (`DrivePoint`, `GetScreenSpot`, `FindScreenerForCarrier`), and **bot adaptive defense** (`EvaluateDefenseMode`, auto-detected `isAI`: Drop when man-down / protecting a late lead / Centre conceded 2+; Press otherwise). Scales 2v2 → 6v6 with no code change. **2026-07-09g:** `BestPassTarget` gains a PRESSURED least-bad-outlet fallback — with nobody formally open, the carrier offloads to the most-open clear-lane mate instead of dribbling into the press. |
| `MatchContext.cs` | Singleton "match truth": ball position, possession + last toucher (`NoteTouch` for deflections), post-release grab cooldown (`releaseGrabDelay` 0.5s), freeze flag, shot-clock grab-ban, kickoff-pass flag, **free-throw state, keeper-hold flag, counterattack window, player goal-line clamp (`playerLimitX`)**, halftime `SwapEnds()`, `GiveBallTo()` / `ForceDropHeldBall()`, `EnemyOf()`, **`IsProtectedKeeper(carrier)`** (the keeper-steal safe-zone rule — true while a keeper carries the ball inside its safe zone, Task 5), **`StartFoulProtection`/`IsFoulProtected(carrier)`** (2026-07-09f: 5s post-foul steal shield on the fouled carrier; lapses early the moment they release the ball; honoured by TrySteal / TouchBlockSteal / TryStealAI / keeper snatch + the AI stand-off). |
| `TeamManager.cs` | On `GameManager`. Auto-switches control to the ball-holder after `autoSwitchDelay` (0.5s — so you keep control to chase your own loose ball); manual **C** / touch SWITCH (skips excluded); **Z** cycles defense (Press/Zone/Drop/MPress); never auto-activates excluded players. Exposes static **`ActivePlayer`** + **`ActivePlayerIndex`** (read by `CameraFollow` and the stamina HUD). |
| `Goalkeeper.cs` | Kinematic keeper sliding along its physical goal line tracking ball Y (stays on its goal after the halftime swap). **Save % system:** a fast shot reaching its hands rolls `baseSaveChance` 0.65 minus penalties for HIGH (height >0.7), POWER (>9 u/s) and SKIP shots, plus a stamina penalty when tired; a slow ball is auto-collected. **Snatch:** an enemy carrier within `keeperSnatchDistance` (0.8u) is stripped with 100% success, no roll (`TrySnatchFromCarrier`; respects free throws, not vs another keeper). **Player keeper = full control:** while your own keeper holds the ball it plays like a field swimmer — free **2D movement** at `keeperMoveSpeed` (4), sprint, a charged shot fired in the **joystick/aim** direction (never auto-aimed at goal), and a **directional pass** (`FindKeeperPassTarget` scores ALL teammates by dot(aim,dir)−dist×0.05, no cone; reads the live `TouchControls.Instance` joystick, else `lastDir`). **No auto-pass** — fully manual; it SWIMS back to its line (never teleports) after you shoot/pass. **Safe zone (Task 5):** within `KeeperSafeZoneRadius` (1.5u) of the goal line the carrying keeper is unstealable; carry it OUTSIDE and `keeperLeftSafeZone` latches → enemies steal normally (exposed via `MatchContext.IsProtectedKeeper`; `OnBallStolen()` clears the hold on a successful strip). **Organic idle** (not holding, ball far): small random X drift 0.1–0.3u every 2–4s (≤0.4u off the line) + a subtle ±0.05u Y sine micro-bob. **Bot keeper** auto-distributes after `keeperHoldSeconds` (0.8s) OR immediately if crowded within `keeperPanicDistance` (2.5u) — UNCHANGED. **Stamina-aware** (tired = worse saves, no sprint at 0%). A keeper hold is NOT a possession change — the shot clock keeps ticking until the pass-out. **Distribution arcs (2026-07-04b):** `PassOut` throws the same untouchable BallFlight arc as every other pass (`ArcKind.Pass`; the forced DEEP outlet = the big `Lob`); point-blank falls back flat. **Freeze gate:** the keeper now fully freezes during `PlayFrozen` like every swimmer (needed so it can't fish the dead ball out of its own net during the goal hang-time). |
| `Goal.cs` | Trigger on each net; reports `goalSide` ("Left"/"Right") + its own transform (for the net-pulse reaction) to ScoreManager. |
| `ScoreManager.cs` | Team-based score (credits the team attacking that net → survives the halftime swap) shown on **separate `playerScoreText` + `botScoreText`** TMP fields; **ignores held-ball goals**; exposes `HomeScore`/`AwayScore` (read by the camera's goal-shake). **FRAME-ACCURACY GATE (2026-07-04b):** touching the goal trigger is NOT a goal — the ball's real velocity is projected onto the goal line and the crossing must land inside the mouth (|y| ≤ 1.5, moving INTO the net; consts mirror GoalLineOut) → skims/corner-clips/sideways drifts no longer score, badly-aimed shots miss. **NET REACTION:** on every goal the net sprite gets a damped-spring squash/bulge pulse (0.45s, scale + outward nudge, originals restored) + an expanding white impact ring at the ball. **Goal restart (NOT a quarter start → NO sprint duel):** 5 phases — (0) **HANG TIME (`goalHangSeconds` 3.5, NEW):** play freezes THE INSTANT the ball hits the net; ball stays IN the net (velocity cut ×0.15, fully stopped after 0.15s), everyone holds position, camera keeps following the action + goal shake — the reset only starts after this hold; (1) ball loose at exact (0,0), touch UI hidden + `ctx.ResetBallTouch()` (camera → overview), a `goalFreezeSeconds` (1s) celebration; (2) both teams snap into the **natural restart spread** (`TeamSide.SnapToRestartFormation(hasBall)`), the **conceding team** is given the ball at exact centre (`ctx.GiveBallTo`) + `ctx.ResetBallTouch()` again; (3) a **`postGoalPauseSeconds` (3s) silent pause**; (4) `Unfreeze` + `SetKickoffPass(conceding)` + `ctx.MarkBallTouched()` + restore UI + reset shot clock. |
| `MatchTimer.cs` | Quarters (**displays 8:00 draining over 90s real** — `CompressedTimer`, 2026-07-09) + win/lose/draw; pauses the clock during freezes; triggers the sprint duel each quarter; halftime swap; `ForfeitMatch()`. At full time / forfeit it calls `MatchResultUI.Show()` (falls back to the bare `resultText` if no MatchResultUI in the scene). |
| `ShotClock.cs` | Per-possession clock (**displays 30 draining over 15s real** — `CompressedTimer`, 2026-07-09; singleton): resets on possession change / goal / defensive exclusion; turnover + grab-ban at 0; pauses when frozen, **during a free throw**, or match over; **a keeper hold does NOT reset it (keeps ticking until the keeper distributes)**. |
| `ExclusionManager.cs` | Fouls + exclusions (singleton): failed steal = foul → **free throw** to the fouled team; 2 fouls in 10s → exclusion (**HUD displays 20 draining over 7.5s of real live play** — `CompressedTimer`, 2026-07-09; roster slot nulled → AI auto-adapts) **or a PENALTY if the victim was in the 2m zone**; 3rd → removal; forfeit < 4 players; HUD countdowns. 🟡 **virtual foul** when the victim is an inside-water Centre (Centres draw exclusions/penalties faster; toggle `centerFoulBoost` — may be too hot, watch in testing). **2026-07-09f — fouls are now VISIBLE:** ordinary foul = 0.7s referee-whistle freeze (`foulWhistleFreezeSeconds`) + floating world-space **"FOUL!"** popup at the victim + a **`foulProtectSeconds` (5s real) steal-proof window** on the fouled carrier (`MatchContext.StartFoulProtection`; AI defenders stand off `freeThrowClearance` the whole window, presser stands down). Excluded players **dim to 45% alpha** at the pen (visibly benched, not a corner defender) and un-dim on return; **re-entry drops onto a live `DefendSpot` again** (the 07-05 behavior — the 07-06 pen-clamp drop-in left returners looking stuck at the pen) + clears stale AI intent (mark/drive/screen). Pen sides verified NOT mirrored: each team's pen = its **defending-half** corner (real WP re-entry corner), matched by x-sign so it survives `SwapEnds`. |
| `SprintDuel.cs` | Quarter-start duel (singleton), fully rebuilt. Builds its OWN screen-space UI in code (no wiring): a big centred **"5 → 4 → 3 → 2 → 1 → GO!" countdown** (1s each, scale-pulse per number; `countdownStart` 5) + a "TAP SPACE / TAP SPRINT FOR SPEED" hint, then a tall **vertical SPEED bar on the left** (red→orange→green, fills with the human's speed) under a pulsing "TAP FASTER!". Ball is pinned to EXACT (0,0,0) with physics OFF during the countdown, goes live at GO. At GO! the two sprinters race (bot fixed speed; human base speed + each **Space / LeftShift tap OR a tap anywhere on screen** boosts toward a cap, decays) AND **every other swimmer immediately jogs into formation at ~60% speed** (`formationMoveSpeed`, both teams alike — `RestartFormationSpot`, position-based so it ignores the freeze; no statues, no waiting for possession). The designated sprinter starts slightly ahead of its line (`sprinterForwardOffset`) so it's clearly the sprinter, not the keeper, and is made the **active player**. Runs at **quarter starts ONLY** (Q1 via `MatchTimer.Start`, Q2–Q4 via `AdvanceToNextQuarter`) — **never after goals/penalties/turnovers** (a goal restart is a separate, duel-free system in `ScoreManager`). `StartDuel` calls `ctx.ResetBallTouch()` so the camera holds the wide overview until a sprinter grabs. First within grabDistance wins → grabs → un-freeze → kickoff pass; the rest transition straight into normal AI from wherever they jogged to. **Hides the gameplay touch UI** (`TouchControls.SetGameplayVisible(false)`) for the duel's duration and restores it on finish. The TAP-for-speed mechanic lives ONLY here — regular play is hold-to-sprint. |
| `EventFeed.cs` | Rolling last-5 event log (singleton): goals, exclusions, turnovers, out-of-bounds, forfeit, halftime. |
| `BallOutOfBounds.cs` | Top/bottom-wall out rule: a loose ball at the edge → possession to the nearest player of the team that didn't touch it last. **Full-escape recovery (2026-07-09c):** a ball past the walls entirely (|x|>8.2 / |y|>4.7 — previously it just sat outside forever) bounces/settles on the deck, pauses ~0.8s, then the awarded (defending) team's KEEPER restarts play — ball dropped at the keeper, its normal collect logic takes it; enemy grab-banned for the beat. |
| `PenaltyManager.cs` | Penalty shot (singleton, B16.11): on an exclusion-level foul inside the 2m zone, freezes play, puts the fouled shooter on the penalty spot (|x|≈2.47) facing the open corner, lines everyone else up **behind the shooter**. Human charges with **Space** within an aim cone; AI auto-fires after a delay (with a miss chance). The freeze lifts on the shot; a goal flows through the normal `Goal` path. |
| `GoalLineOut.cs` | Goal-line out rule (B16.11): a LOOSE ball crossing a goal line outside the mouth → re-enter just inside, nearest opponent gets it; a CARRIER pressing the end line → **corner restart** (ball + receiver placed at that corner). Awards to the team that didn't touch it last (deflection-aware via `LastTouchTeam`). |
| `BallTouchTracker.cs` | Sits on the **Ball**. Records the last team to physically touch a LOOSE ball, so a shot/pass that deflects off an opponent and goes out is awarded correctly. Ignores keeper touches and held-ball contacts. |
| `PlayerAnimator.cs` | Drives Animator for the human player. Reads speed from Rigidbody2D, IsHolding from PlayerMovement. Fires IsShooting trigger on fast release, IsStealing trigger on every grab attempt. Flips SpriteRenderer horizontally based on velocity.x. Defend animation triggers only when enemy carrier is within 1.5 units. |
| `BotAnimator.cs` | Drives Animator for AI bots. Reads state via IAgentBody. Same steal/defend/flip logic as PlayerAnimator. Reads isBlueTeam from BotMovement and swaps Animator controller to BlueAnimation.controller at Awake() if true. |
| `AnimationClipBuilder.cs` | Editor tool (Tools menu). Builds 7 animation clips (idle/swim/sprint/hold/throw/defend/steal) from sliced sprite sheets, assigns them to the Animator controller states, and wires all transitions. Two menu items: Tools/Build Water Polo Animations (red) and Tools/Build Blue Team Animations (blue). Creates BlueAnimation.controller programmatically if missing. |
| `GoalkeeperAnimator.cs` | Drives the Animator on KeeperLeft/KeeperRight. Reads ball velocity from MatchContext + the shot's **height** (`PlayerMovement.ShotHeight` via `MatchContext.LastReleaser`; AI shots → 0.5 mid) to compute **DiveState** (0–7): idle, dive left/right, dive bottom-left/right, dive top-left/right, save. Low shot → bottom dive, high → top, mid → side; save when this keeper has caught the ball. A **`BallFlight.KeeperFooled`** skip shot pins it to the mid (side) dive — no reaction. Single int param `DiveState`; SpriteRenderer flipX set in Awake by keeper side. |
| `GoalkeeperAnimationBuilder.cs` | Editor tool (Tools → Build Goalkeeper Animations). Builds 8 animation clips from goalkeeper_sheet.png frames, assigns them to GoalkeeperAnimation.controller states, wires DiveState int parameter and Any State transitions. Idempotent. |
| `TouchControls.cs` | Runtime-built mobile touch UI (no prefabs), **singleton** (`Instance` + `JoystickAxis`, read by the keeper for its aim): virtual joystick bottom-left + **3 circular image buttons** bottom-right (`actionButtonSize`/`mainButtonSize` 270) that swap icon + behaviour with possession. **Attack** (we hold / loose): Sprint (top) / Shoot (bottom-right) / Pass (bottom-left). **Defense** (enemy holds): Switch (top) / Defend (bottom-right) / Block (bottom-left). Mode read each frame from `MatchContext.PossessingTeam` (==BotTeam → defense), SmoothStep fade-out→swap→fade-in (0.22s); icons from `Resources/Sprites/` (`sprint/shoot/pass/Defend/switch/block`). Attack actions feed `PlayerMovement.SetTouchInput` (merged with keyboard via `\|\|`); Switch rides `TouchSwitchDown`; Block → `TouchBlockSteal()`; Defend feeds a chase-the-carrier axis. **Player-keeper control:** while your own keeper holds the ball the 3 attack buttons + joystick route to the `Goalkeeper` (Shoot/Pass/Sprint) — the old single **PASS OUT** button is RETIRED. **Stamina HUD panel** above the joystick: `P#` (or "GK") + a green→yellow→red fill bar reading `PlayerMovement`/`Goalkeeper.StaminaPercent01` + `TeamManager.ActivePlayerIndex` (Lerp-smoothed; label pulses red below 20%). Press feedback = scale to 0.9x. Visible on mobile, or in Editor when `showInEditor`. |
| `PoolLineFloat.cs` | Standalone gentle bob (±0.04u) + sway (±1.5°) for the 12 pool lane-line sprites; random phase/speed (0.6–0.9 Hz) per object; offsets from the Start pose so it never drifts. |
| `MainMenuUI.cs` | MainMenu scene. Builds the whole main menu in code at runtime: canvas (1280x720), background + logo from `Assets/Resources/Sprites/`, PLAY/SETTINGS/QUIT buttons with hover scale + cyan-outline TMP labels, 1s fade-in, version footer. PLAY → **HubScene**. |
| `NavigationManager.cs` | HubScene. The whole hub built in code. **Top bar:** left = profile cluster (circular avatar tinted club-primary with the club CREST as its glyph + country "flag" dot + club name/XP/level — avatar OR name opens the My Club screen; settings/inbox/gifts icon buttons with real art (`settings/message/gifts-button.png`, 42px group at 95px pitch; labels are hover / 0.4s press-hold **tooltips** with a dark pill backing via the nested `IconTooltip` — no permanent captions) → stub settings panel + COMING SOON overlays; **FREE +100** watch-ad pill at x 660, 3/day via `AdWatchCap`), right = live gold/diamond with [+]. **Left column:** RANKING (coming soon) / SHOP (`ShopUI` overlay) / TEAM (`TeamScreenUI`) — 135/140/135px, rows at yOff −40 ± 140. **Right column:** FRIENDS 135 / CLUBS 150 (bigger box: its trimmed art is ~1.8:1 wide), same rows/offset as the left column → COMING SOON stubs (no online backend yet). ALL hub button art loads via the nested **`LoadTrimmedSprite`** (alpha-trim, needs `isReadable: 1` metas) because the source PNGs carry 10-60% transparent margins. **Bottom bar:** season pass (locked) + missions + **4 live post-match reward slots** (state from `PostMatchRewardManager`; pitch 84; Ready/Unlocking slots scale 1.18x + get `PackCardFX` float/shine; a slot filled by the last match scale-ins with overshoot on hub load via `ConsumeNewRewardSlot`) + PLAY → Game Mode overlay. Also hosts: Game Mode / Standings / Pre-Match / Club-customization overlays, the reward-slot unlock popup (odds table via shared `PackInfoPopup`), and `RefreshClubProfile()` (re-reads `RosterManager.Club` into the cluster). |
| `PlayerData.cs` | **(Player data foundation, NEW)** ScriptableObject = one player CARD: `id`, `fullName`, `nation`, `position` (enum GK/CB/LW/RW/CF/LF/RF — enum order == starter-slot order), `overall` 0–100, a `Stats` struct (speed/shooting/passing/defense/stamina/goalKeeping 0–100), `rarity` (Common/Rare/Legendary → `RarityColor`), `portrait` (Sprite, null for now → UI draws a silhouette), `priceGold`, `isBot`. `[CreateAssetMenu]` (Create → Water Polo/Player). Static `ComputeOverall(stats,pos)` (GK leans on goalkeeping, field = outfield avg) shared by the generator + UpgradePlayer; `Clone()` so owned cards are mutated as runtime copies, never the source asset. PURELY data — never touched by the match. |
| `PlayerDatabase.cs` | **(NEW)** Read-only player CATALOG: lazy C# singleton that `Resources.LoadAll`s every `PlayerData` under `Resources/Players/` into a dict by id (`Get`/`Has`/`AllPlayers`/`FirstOfPosition`/`Count`). No scene object. |
| `Roster.cs` | `[Serializable]` save payload: `List<string> ownedPlayerIds`, `string[7] starterSlots` (0=GK, 1–6 field by position), `int coins`, `int diamonds`, plus **`ClubProfile club`** (clubName / logoId / primaryColorHex / secondaryColorHex / countryId — the My Club identity). IDs only → tiny JSON. |
| `RosterManager.cs` | Self-bootstrapping singleton MonoBehaviour (DontDestroyOnLoad, no wiring). Loads/saves `Roster` as JSON in `Application.persistentDataPath/roster.json` (guest-mode, no Firebase); seeds a default 7 + bench + coins/diamonds on first run (self-heals if the catalog was empty then). Owned cards held as `Clone()`s so upgrades never corrupt the source asset. API: `BuyPlayer`/`SellPlayer`/`UpgradePlayer`/`SetStarter(slot,id)`/`GetOwnedPlayers`/`GetStarters`/`TeamOverall`; **`Club` + `SaveClub()`** (the ClubProfile; generates a one-time "Guest_XXXXXX" name on first load); auto-saves after every mutation. (Upgrades are in-session only — Roster stores ids only; extend later.) |
| `TeamScreenUI.cs` | **(NEW)** The REAL hub Team screen (B12), built in code in NavigationManager's style (no prefabs/wiring; NavigationManager attaches it + passes itself). Live 2-3-2 formation of the 7 starters, a scrollable owned-bench + buyable-market list, team OVR + gold/diamonds, and working **BUY / SELL / UPGRADE / START** buttons → `RosterManager` (each refreshes the screen + the top-bar currency). Each card: rarity-coloured border (grey/blue/gold) + name/OVR/position + silhouette (or `portrait`). |
| `SamplePlayerGenerator.cs` | **(NEW, Editor — `Assets/Editor/`)** **Tools → Generate Sample Players**: writes 21 sample `PlayerData` assets to `Resources/Players/` (all 7 positions, mixed rarities/ratings/prices; deterministic → idempotent). Run once so the Team screen has data. |
| `MatchResultUI.cs` | Full-time result screen, built in code, hidden until `MatchTimer` calls `Show(title, outcome)`: dark 80% overlay, FULL TIME/FORFEIT title, "YOU n — n BOT" score from ScoreManager, colored winner line (cyan/red/yellow), PLAY AGAIN + MAIN MENU buttons; 0.5s unscaled-time fade-in (timeScale is 0 at match end). Singleton. |
| `QuarterBreakUI.cs` | Between-quarters pause screen (built in code, **self-bootstrapping** via `Get()` — no scene object needed). `MatchTimer` raises it when a quarter ends (but the match isn't over): dimmed overlay + centred dark panel with **"QUARTER N COMPLETE"**, the score, and **RESUME** (→ next quarter's sprint duel) / **QUIT** (→ MainMenu if present, else stop play). Play freezes via `MatchContext.FreezeAll` until RESUME. Singleton. |
| `PauseMenuUI.cs` | Pause system, built in code: 70x70 pause button top-right at (-20,-45) (sprite `Resources/Sprites/pause-button`; pulled down to clear the scoreboard), click → `Time.timeScale = 0` + centered 400x350 rounded panel with PAUSED + RESUME / QUIT / TEAM MANAGEMENT. QUIT opens a confirmation sub-panel ("If you quit, this match counts as a loss.") with YES QUIT (→ MainMenu) / CANCEL. TEAM MANAGEMENT is a placeholder (no functionality yet). Ignores clicks after full time (result screen owns the freeze). Works with mouse + touch. |
| `CameraFollow.cs` | **FIFA-style follow camera** on **Main Camera** — self-contained, no Inspector wiring (pulls `TeamManager.ActivePlayer` + `MatchContext`). **Start/post-goal overview (Task 1):** until the ball is first touched after any reset (game start, after a goal, between quarters — `MatchContext.BallTouchedSinceReset`) it holds the wide pool overview centred on (0,0) at **maxSize 5.0**, no following; the first grab eases it smoothly into the normal follow. Tracks a weighted point between the active player (60%) and the ball (40%) — 70/30 when the ball is loose — via `SmoothDamp` (speeds up to `switchSpeed` 8 for 0.5s on a player switch). **Dynamic orthographic zoom** (`Mathf.Lerp`): 4.2 base → 5.0 (player/ball far) → 4.5 (`SprintHeld`) → 3.8 (you control the keeper). HARD pool-boundary clamps on the camera centre (X ±5.5, Y ±3.2); Z locked −10. **Screen shake** (additive): goal 0.15/0.4s (polls `ScoreManager` total), powerful shot (ball >10 u/s) 0.05/0.15s. Managers missing → parks at (0,0,−10) size 5, no errors. All tunables serialized. |
| `StaminaSystem.cs` | FIFA-style stamina on every field swimmer + keeper. **Auto-installs at runtime** (`RuntimeInitializeOnLoadMethod`) onto any `PlayerMovement`/`IAgentBody`/`Goalkeeper` lacking one → 14 objects (6 players, 6 bots, 2 keepers), zero wiring (the 2 keepers keep a hand-tuned copy). **Field drain/recovery per sec:** idle +8% (×2 after 5s rest), swim −3%, hold+move −5%, sprint −12% (−18% after 3s fatigue), excluded +15%; **second wind** at 0% (ease off sprint 2s → +15% burst). **Effects:** <40% speed ×0.8; <20% speed ×0.6 + steal ×0.8; 0% sprint disabled. **Keeper:** track −2%, hold −1%, idle +10%; tired = worse saves, no sprint at 0%. Writes only neutral hooks (deleting it leaves the game identical); HUD lives in `TouchControls`. |
| `BallFlight.cs` | Ball VFX + **the airborne-arc system**, **auto-added to the Ball at runtime** by `PlayerMovement` (no wiring), singleton. **ALL passes and HIGH shots fly as arcs** (`LaunchHighBall(landPos, speed, height01, ArcKind)`): the rigidbody flies a straight zero-damping constant-speed line with **colliders OFF** (players/keepers/walls/goal trigger can't touch it) while a sprite copy (`BallAirSprite`, sorted over swimmers) rides the height curve above a shrinking oval water shadow; the root sprite hides mid-flight. **Untouchable mid-air:** `MatchContext.BallGrabbable` is false while `HighBallActive` — grabs/steals/keeper saves all wait for the landing (exact at landPos; landings clamped into open water; overlapped swimmers collision-ignored until separated). **Three ArcKinds:** `Pass` (B / every bot pass — small quick SYMMETRIC hop, peak ≈ dist×0.055 clamped 0.18–0.5, swell 1.08, no spin), `Lob` (F+B / bot long-or-blocked ball — the big floaty parabola, peak ≈ dist×0.14 clamped 0.45–1.25, swell 1.2), `Shot` (charge >0.7 — **ASYMMETRIC** hand-built curve: easeOutQuad rise into a peak at 35% of the flight, easeInQuad fall that hangs near the top then drops; peak ≈ dist×0.10 clamped 0.35–0.9, swell 1.15; glows + keeps FULL speed on landing — passes land with a 25% roll). **Release SNAP (shots only, incl. bot/keeper):** raw un-eased squash 0.84 → pop 1.12 → settle over 0.12s at the instant of release. Plus: speed-gated **TrailRenderer** (>5 u/s, suppressed mid-arc); **flat point-blank high-shot** swell+glow fallback; **skip-shot** bounce 1.5u before the goal (Y jitter, squash + water ripple, 35% `KeeperFooled`); **spin** (shots 54°/s, fast loose 18°/s, arcs 9°/s — none on skip or any Pass, only >6 u/s, snaps upright on catch). All scaling uniform, recomputed from a clean base each frame. Exposes `ShotHeight`, `SkipActive`/`SkipBounced`, `HighBallActive`, `KeeperFooled`. **Settle ripples (2026-07-09c):** a fast loose ball slowing below 1 u/s with nobody collecting it splashes ONCE — 3 staggered expanding rings at the contact point, first largest, each fainter (latched via arm-at->2.5 u/s; suppressed while held/airborne/frozen). |
| `GoalColliderFixer.cs` | Editor tool (**Tools → Fix Goal Colliders**). Resizes GoalRight/GoalLeft Box Collider 2D to the visual goal mouth (size (4,15) → world ≈0.8×3.0u at scale 0.2). Idempotent; marks the scene dirty (Ctrl+S to save). |
| `PlayerLabel.cs` | ⬜ **NOT YET BUILT** (planned). Future: world-space player-number labels floating above each swimmer. |
| `LeagueSeason.cs` | Static session-persistent **tournament** state, one per competition. 16 teams in 2 groups of 8 (player = team 0, Group A); 7-round group round-robin (circle method), each player match also simulates that round in both groups; top 4 per group → single-elim knockout (QF: A1vB4/A2vB3/B1vA4/B2vA3 → SF → Final, no draws — sudden-death goal). Player eliminated → rest of bracket simulates instantly. Phase enum (GroupStage/Quarterfinal/Semifinal/Final/Completed), per-group P/W/D/L/GF/GA/Pts, `KnockoutMatch` bracket, 30-club name pool (15 opponents drawn per division). Champion → NavigationManager persists the next-division unlock (PlayerPrefs div1_won/pl_won/cc_won/wcl_won). |
| `GameModeCardFX.cs` | Card hover/select animations, locked-card shake, staggered entry for Game Mode screen. |
| `GameModeBackgroundFX.cs` | Ken Burns + vignette + light specks animated background for Game Mode. |
| `CardPack.cs` | Static pack data model + open logic. **ONE pack identity: `CardTier`** (Common/Rare/Epic/Legendary Card — the old `ShopPackType` Basic/Super/Gold/Legendary was DELETED 2026-07). Each tier def carries BOTH shop pricing (gems 100/100/250/400, Legendary also $2.99, Common also watch-ad) AND reward-slot unlock hours (3/7/12/24), plus ONE per-card rarity odds table used for every open and every "i" popup. Legendary is `guaranteedLegendary` (forces one in if the rolls miss). **`TierArtSprite(tier)`** = the ONE pack-art loader: loads the raw Texture2D (immune to sprite-slicing import modes) + alpha-trims to the content box so all 4 arts render uniform (see sprite-import gotcha in the 2026-07-02 session log). `GrantAll` adds to roster, duplicates convert to coins. |
| `PostMatchRewardManager.cs` | Post-match reward slots (Clash-style). Self-bootstrapping singleton, own JSON save (`rewardSlots.json` — persists across relaunch). 4 slots, states Empty/Locked/Unlocking(+derived Ready); full-time in `MatchTimer` rolls a tier (Common 80/Rare 16/Epic 3.5/Legendary 0.5%) into the first empty slot; one unlock at a time; UTC-tick timing keeps counting while the app is closed. `ConsumeNewRewardSlot()` = one-shot in-memory flag (slot index of the newest drop) that NavigationManager consumes on hub load for the reveal animation. |
| `PackRevealUI.cs` | Shared "PACK OPENED!" overlay: rarity-framed cards scale in staggered, NEW / +coins per card, tap to dismiss. Used by hub reward slots and the shop. Same file also holds **`PackInfoPopup`** — the ONE drop-rate "i" popup (odds table from `CardPack.TierPackDef.odds`), used by shop pack cards (`Show`) and the reward-slot popup (`BuildOddsRows`) so both render identically. |
| `ShopUI.cs` | Full SHOP screen (code-built, hosted in NavigationManager's shop overlay). Content = **ONE continuous horizontal ScrollRect ("shelf")** with 9 fixed-width sections side by side (OFFERS / PACKS / DAILY DEALS / FREE PRIZES / ADS PACK / COIN PACKS / GEM PACKS / DRAFT TICKETS / EVENT); bottom bar = 9 plain-text tabs (active green + underline, FREE pill badges) that glide-scroll (0.4s SmoothStep) to their section, highlight follows free drags. All 4 pack cards are uniform 250x400 (`PackCardFX` float+shine on each); DAILY DEALS rotates 3-of-4 by UTC day + ad-watched REFRESH; FREE PRIZES are watch-to-claim; every WATCH button is compact ("WATCH ▶") and individually capped 3/day. Same file holds **`AdWatchCap`** — the shared PlayerPrefs 3/day watch-ad cap (also used by the hub's FREE +100). |
| `IAPBridge.cs` | Single entry point for ALL real-money buys (`PurchaseProduct(productId, onSuccess)`). Currently a stub: logs + succeeds immediately. Swap this one method body for Unity IAP (Apple/Google billing only) later. |
| `PackCardFX.cs` | **(NEW 2026-07)** Reusable idle-animation component: sine float (~5px, ~2s, random phase) + periodic diagonal shine sweep (one shared procedural gradient sprite, RectMask2D-clipped, raycastTarget off). `PackCardFX.Attach(rect)`. In the SHOP it's attached to the pack ART Image only (text/buttons stay still); on hub reward slots (Ready/Unlocking) it's the whole slot, intentionally. Unscaled time, no per-frame allocation. |
| `MissionManager.cs` | **(NEW 2026-07)** Missions: plain C# singleton + `MissionsUI` (hub MISSIONS button → overlay; left tabs Newcomer/Daily/Weekly/Global Cup, right list with progress bars + CLAIM). Own JSON (missions.json). 3 real stats only — matches_played / matches_won / packs_opened — in 4 scopes: lifetime (Newcomer), UTC-day (Daily), 7-day (Weekly), season (Global Cup, follows SeasonPassManager's epoch). Stat hooks: MatchTimer.EndMatch + CardPack.OpenTierPack — no parallel tracking. Claims grant via `GrantReward` (the ONE reward funnel → RosterManager / CardPack) + 10 season XP. Red claim-ready badge on the hub missions button. |
| `LeaderboardManager.cs` | **(NEW 2026-07)** League leaderboard: plain C# singleton + `RankingUI` (hub RANKING button). HONESTLY SIMULATED — 24 deterministic fake rivals per season (gamer-tag pool); only the PLAYER's points are real (+20 win / +5 loss from the EndMatch hook). 5-tier ladder IRON→DIAMOND; at season rollover (SeasonPassManager epoch) top 5 promote, rank 20+ demotes, prev result stored for "LAST WEEK". Player row always pinned at the bottom. Elite/World/Friends/Country tabs = locked COMING SOON stubs (need real accounts — NOT fake data). Own JSON (leaderboard.json). |
| `SeasonPassManager.cs` | **(NEW 2026-07)** THE canonical season: 14-day epoch in seasonpass.json drives the hub "SEASON ENDS IN" countdown (now real + tappable → this screen), Global Cup mission scope, league rollover, and the shop EVENT badges. 16 tiers × 100 XP; XP from matches (+25 win / +10 loss, EndMatch hook) + mission claims (+10). `SeasonPassUI`: Gold Pass card (ACTIVATE = 500 gems PLACEHOLDER price, via SpendDiamonds), tier/XP progress, horizontal 16-tier track — free row always collectible, gold row padlocked until activated, COLLECT grants via the shared reward funnel. Free Pass "card" is just a note — the free row IS the free pass (no duplicate track). |
| `ClubCustomizationUI.cs` | **(NEW 2026-07)** The "My Club" screen (code-built, hosted in NavigationManager's club overlay; opened from the hub avatar/name). Crest picker (8 PROCEDURAL shapes — real crest art still needed), primary/secondary color swatches (10 preset), country picker (12 text chips, colored-dot placeholder until flag art exists), TMP_InputField rename (max 16 chars), live preview, APPLY → `RosterManager.Club`/`SaveClub()` + `nav.RefreshClubProfile()`. Statics shared with the hub: `CrestSprite(id)`, `ParseHex`, `CountryColor`. |

| `CompressedTimer.cs` | **(NEW 2026-07-09)** Shared compressed-countdown struct: the DISPLAYED number counts down from `displayDuration` while only `realDuration` real seconds pass (FIFA-style fast-ticking big clock). Gameplay reads the REAL scale (`Tick`/`RealRemaining`/`IsComplete`); only printed text uses `DisplayValue`/`DisplayElapsed`. Used by MatchTimer (8:00 / 90s real), ShotClock (30 / 15s real), ExclusionManager (20 / 7.5s real). |

| `PoolTheme.cs` | **(NEW 2026-07)** Data-driven pool theming: `PoolTheme` (id, background sprite path, water tint, ambient-layer defs) + static `PoolThemes` catalog (CardPack-style registry; `Get(id)` / `ForDivision(competitionIndex)`) + `PoolThemeApplier` (self-bootstrapping like StaminaSystem: on scene load, finds "PoolWater" and applies overrides; hub scenes no-op). ONE real theme ("classic") = the current art expressed as no-overrides, all 4 divisions map to it. Future themed pools = register an entry + map the division; ambient ANIMATION plugs into the spawned "PoolThemeAmbient" group later. |

**Architecture rule for any AI:** keep `TeamSide` + `MatchContext` + `WaterPoloBrain`. It is roster-size-agnostic by design. To scale teams: add player/bot objects, drop them into the team `members` arrays + TeamManager arrays; formation & AI scale automatically.

## A6. Scene objects + wiring (the Hierarchy) — current 6v6 scene

> ⚠️ Slots are set by dragging objects from the Hierarchy into the Inspector. After any full-script replace, VERIFY these. The Unity Inspector is the real truth.

**Pool & arena**
- **Pool** — Square, Pos (0,0), Scale (16,9), blue.
- **Walls** (empty parent) → `WallTop`/`WallBottom`/`WallLeft`/`WallRight` — Squares, **Box Collider 2D (Is Trigger OFF)** at pool edges (±8 x, ±4.5 y). Top/bottom also act as out-of-bounds lines (handled in code by `BallOutOfBounds` via the ball's y — no wiring); left/right keep normal bounce physics.
- **PoolLines** — thin decorative strips (2m / 5m / half markings). Visual only, no colliders.

**Camera**
- **Main Camera** — Orthographic, starts at **Size 5** / Pos (0,0,−10). Has **`CameraFollow`** (self-contained, no wiring): on play it eases the zoom to 4.2 base and follows the weighted active-player/ball point with dynamic zoom (3.8–5.0), hard boundary clamps, and goal/shot screen-shake. Z stays −10. (URP camera.)

**Ball**
- **Ball** — Circle (~0.4), yellow, **Tag = "Ball"**, Order 1. Rigidbody2D: Gravity 0, **Linear Damping 2.5** (was 4 — passes were dying mid-flight), Angular Damping 0.05, Collision Detection = Continuous. Circle Collider 2D (trigger OFF). Also has **`BallTouchTracker`** (no refs — pulls from MatchContext; tracks loose-ball deflections for the out rules). Plus **`BallFlight`** is auto-added at runtime (trail / skip / lob / high-shot VFX + spin — no wiring).

**Players (your team, 6) — attack one end / defend the other; sides SWAP at halftime**
- **Player1 … Player6** — Circles (~0.5), red, Order 1. Each has: Rigidbody2D (Gravity 0, Freeze Rotation Z), Circle Collider 2D, a child **AimLine** (Line Renderer).
  - `PlayerMovement`: **Ball = Ball**, **Aim Line = its OWN AimLine child**, speed/grab/shoot/pass/steal tunables.
  - `TeammateAI`: **My Team = PlayerTeam** (+ AI tunables).
  - **Slot index in PlayerTeam.Members = role:** 0 Center, 1 Center-Back, 2/3 Wings, 4/5 Flats.
  - Also Animator + `PlayerAnimator`, and a **`StaminaSystem`** that **auto-installs at runtime** (no slot to wire).

**Bots (enemy team, 6)**
- **Bot1 … Bot6** — Circles (~0.5), magenta. Each: Rigidbody2D + Circle Collider 2D + `BotMovement`: **My Team = BotTeam** (+ tunables). Plus Animator + `BotAnimator` and a runtime-auto-installed **`StaminaSystem`**.

**Goals & keepers**
- **GoalRight** (Pos (7,0)) / **GoalLeft** (Pos (-7,0)) — Squares (0.5,3), **Box Collider 2D Is Trigger ON**, sized to the goal mouth via **Tools → Fix Goal Colliders** (`GoalColliderFixer`: size (4,15) ≈ 0.8×3.0u world at scale 0.2). `Goal`: Goal Side = "Right"/"Left", **Score Manager = ScoreManager**.
- **KeeperRight** (~(6.3,0)) / **KeeperLeft** (~(-6.3,0)) — thin tall Squares. Box Collider 2D (trigger OFF) + Rigidbody2D **Kinematic** (Use Full Kinematic Contacts ON, Gravity 0). `Goalkeeper`: **Ball = Ball**, Track Speed 4, Min/Max Y, and grab-and-control fields: Keeper Grab Distance 1.2, Base Save Chance 0.65, **Keeper Snatch Distance 0.8** (strip a point-blank enemy carrier, 100% no roll), **Keeper Hold 0.8** (bot auto-distribute), **Keeper Panic Distance 2.5** (bot distributes now if crowded), Hold Offset 0.5, **Keeper Move Speed 4** (free-roam while you hold the ball). Keepers guard their physical goal even after the halftime swap. Each keeper also has an **Animator + `GoalkeeperAnimator`** (DiveState, `GoalkeeperAnimation.controller`) and a hand-added **`StaminaSystem`** (tuned keeper drain rates).

**Managers — all components on ONE `GameManager` GameObject:**
- `MatchContext`: **Ball = Ball, Player Team = PlayerTeam, Bot Team = BotTeam**, Release Grab Delay 0.5 (was 0.35 — gives passes/drops time to travel), Free Throw AI Hold 3, Player Limit X 6.9, Counter Window 4.
- `TeamManager`: **Players = [Player1..6]**, **Teammate AIs = [Player1..6] (SAME ORDER)**, **Player Team = PlayerTeam**, **Defense Mode Text = DefenseModeText**.
- `MatchTimer`: **Score Manager = ScoreManager, Timer Text = TimerText, Quarter Text = QuarterText, Result Text = ResultText**, Quarter Length 90 (real), Display Quarter Length 480 (the 8:00 shown), Total Quarters 4.
- `ShotClock`: **Match Timer = (this GameManager's MatchTimer), Shot Clock Text = ShotClockText**, Shot Clock Seconds 30 (displayed), Shot Clock Real Seconds 15.
- `EventFeed`: **Feed Text = EventFeedText, Match Timer = MatchTimer**, Max Lines 5.
- `SprintDuel`: no required refs (pulls teams/ball from MatchContext); optional **Duel Text**; speed/timing tunables.
- `BallOutOfBounds`: no refs (pulls from MatchContext); Out Y Threshold 4.2, Reentry Inset 0.5.
- `PenaltyManager`: optional **Penalty Text = PenaltyText**; Penalty Spot X 2.47, Behind Spot Margin 1, Penalty Aim Cone 70, AI Shoot Delay 1, AI Miss Chance 0.25, AI Miss Offset 1.6, Penalty Shot Speed 13, Max Penalty Seconds 6.
- `GoalLineOut`: no refs (pulls from MatchContext); Goal Line X 7, Goal Mouth Half Height 1.5, Reentry Inset 0.5, Carrier Out X 6.7, Corner Inset X 6.2, Corner Y 3.5.

**Other manager objects (empty GameObjects)**
- **PlayerTeam** — `TeamSide`: Name "Player", **Attack Goal = GoalRight, Defend Goal = GoalLeft**, **Members = [Player1..6]**, formation + AI tunables, plus **attacking-spacing** (Teammate Spacing 2, Support Pass Range 5, Support Blend 0.5, Pass Openness Weight 1.5) and **tactics** (Center Feed Weight 3, Counter Runners 2, Drop Sag 0.5, Shot Quality Threshold 0.30, Free Throw Clearance 2.2) fields. (Defense mode is runtime-only, defaults Press.)
- **BotTeam** — `TeamSide`: Name "Bot", **Attack Goal = GoalLeft, Defend Goal = GoalRight**, **Members = [Bot1..6]**.
- **ScoreManager** — `ScoreManager`: **Ball = Ball, Player Score Text = PlayerScoreText, Bot Score Text = BotScoreText, Player Team = PlayerTeam, Bot Team = BotTeam**, Goal Freeze Seconds 1.
- **ExclusionManager** — `ExclusionManager`: **Match Timer = MatchTimer, Exclusion Text = ExclusionText**; Foul Window 10, Fouls For Exclusion 2, Exclusion Display 20 / Exclusion Real 7.5, Max Exclusions 3, Min Players 4, Foul Steal Lockout 1.5, Penalty Zone X 4.28.

**UI — Canvas (TextMeshPro), + EventSystem (auto)**
- **ScoreboardBG** (Raw Image, `score-tab.png`) holding **PlayerScoreText** + **BotScoreText** (separate score fields) and **PlayerNameText** + **BotNameText**; **TimerText** ("1:30"), **QuarterText** ("Q1"), **ResultText** (hidden until full time), **DefenseModeText** ("DEFENSE: PRESS/ZONE"), **ExclusionText** (exclusion countdowns), **ShotClockText** ("30"), **EventFeedText** (last 5 events), **PenaltyText** ("PENALTY!", hidden until a penalty; wired into `PenaltyManager.Penalty Text`). The **stamina HUD panel** (P#/GK + bar) is built at runtime inside `TouchControls` — not a Canvas object.

**HubScene**
- **HubScene** — all UI procedural via NavigationManager.cs. Sprites from Assets/Resources/Sprites/ at runtime.

## A7. Animation system (Visual Pass 1 — COMPLETE)

**Two Animator controllers:**
- `Assets/Sprites/PlayerAnimation.controller` — red team (Player, Player2–Player6)
- `Assets/Sprites/BlueAnimation.controller` — blue team (Bot, Bot2–Bot6)

**7 animation states per controller:**
idle, swim, sprint, hold, throw, defend, steal

**Animator parameters:**
- Speed (float) — driven by Rigidbody2D.linearVelocity.magnitude
- IsHolding (bool) — from PlayerMovement.IsHolding / IAgentBody.IsHolding
- IsSprinting (bool) — Shift held + speed > 0.1 (player) / IsDriving (bot)
- IsDefending (bool) — enemy carrier within 1.5 units proximity check
- IsExcluded (bool) — from ExclusionManager
- IsShooting (trigger) — fires on fast ball release
- IsStealing (trigger) — fires on EVERY grab/steal attempt, hit or miss

**Sprite sheets — Red team:** `Assets/Sprites/Players/RedTeam/`
**Sprite sheets — Blue team:** `Assets/Sprites/Players/BlueTeam/`
Each sheet: 6 frames, 2048px wide, sliced Grid By Cell Count C:6 R:1,
Filter Mode: Bilinear, Max Size: 4096

**File naming convention:**
- `idle_floating_in_water__gentle_arm_movement[_blue].png`
- `swimming_forward__arms_mid-stroke[_blue].png`
- `sprinting__arms_in_fast_crawl_stroke[_blue].png`
- `holding_ball_raised_in_right_hand[_blue].png`
- `throwing_ball_overhead__arm_extended[_blue].png`
- `defensive_stance__arms_out_wide[_blue].png`
- `steal_snatch_attempt[_blue].png`

**Sprint mechanic (HOLD-to-sprint in regular play — June 2026):**
- Player: HOLD **LEFT SHIFT** (keyboard) or the **Sprint button** (touch) → sprint at
  `moveSpeed * sprintMultiplier` (2× by default) while moving. Release = stop. `SprintHeld` is
  the raw held state on the active player; `SprintCharge` is now just a **0/1 proxy** of it
  (1 = sprinting) so the camera zoom / animator / stamina drain / teammate-hustle keep reading
  one value. `SprintHeld` + ball = IsLooseHold (enemy grab range doubles). **No head sprint
  bar** in regular play (removed — it was for the tap charge). Stamina still drains while
  sprinting and disables sprint at 0%. *(The TAP mechanic now lives ONLY in the sprint duel —
  see `SprintDuel.cs`.)*
- Player-team AI mates move 1.2x faster (keep formation, no sprint of their own) while the
  human holds sprint; the camera zooms out and the swim animation reads as a sprint too.
- Bots: sprint decided by WaterPoloBrain IsDriving logic, unchanged

**SpriteRenderer flipping:**
- velocity.x > 0.1 → flipX = false (faces right, default)
- velocity.x < -0.1 → flipX = true (mirror)
- near zero → hold last value

**Known remaining issues (fix later):**
- Sprint animation not triggering correctly in all cases (IsSprinting threshold tuning needed)
- Idle/swim sprite size inconsistency (swim sprites slightly smaller — art fix needed in ChatGPT)

**Goalkeeper animation (COMPLETE):**
- `Assets/Sprites/Players/GoalkeeperAnimation.controller` — **8 states** driven by a single integer `DiveState` parameter (Any State → state when DiveState == its value): idle (0), dive_left (1), dive_right (2), dive_bottom_left (3), dive_bottom_right (4), dive_top_left (5), dive_top_right (6), save (7).
- Sheet: `Assets/Sprites/Players/goalkeeper_sheet.png` — **8 frames at 2928×352px**, one held frame per clip.
- Built by `GoalkeeperAnimationBuilder` (Tools → Build Goalkeeper Animations, idempotent); driven at runtime by `GoalkeeperAnimator` on both KeeperLeft + KeeperRight (low shot → bottom dive, high → top, mid → side; `BallFlight.KeeperFooled` skip shot pins the mid dive).

## A8. Pool Visual (COMPLETE)

**Water background:**
- Old Pool object (Sprite + WaterScroller.cs + WaterScroll.shader) has been removed and replaced
- New system: a 2D Square GameObject renamed to PoolWater, using a Sprite Renderer with ShaderWaterMaterial (a URP Shader Graph material, `Assets/Sprites/ShaderWaterMaterial.mat`)
- Shader: `Assets/Sprites/WaterGraph.shadergraph` — Voronoi noise-based procedural water, animated via Time node, creates realistic individual ripple/bubble movement across the pool surface. Far more realistic than the old scrolling texture approach
- Known: Sprite Renderer shows a _MainTex warning — cosmetic only, does not affect gameplay. Will be fixed later by swapping to MeshRenderer
- WaterScroller.cs and WaterScroll.shader were unused and have been deleted (June 2026). `Assets/Sprites/Pool/WaterScrollMat.mat` is also now unused (will render pink if applied) — candidate for later cleanup

**Remaining pool visuals (not started):**
- Lane lines (2m, 5m, 7m markings)
- Goal net art
- Poolside/edge tiles
- Player ripple effects

Game Mode screen with 4 competition cards, lock-sign sprite, animated background, card polish. Competition screen (per division): GROUP STAGE / KNOCKOUT tabs — two collapsible framed group tables (collapsed = top 5, Pos|Team|Pts; expanded = all 8 full columns; tap card to toggle; player row gold) in a vertical ScrollRect, plus a bracket view (QF 2x2, SF, Final; player's tie gold-framed, losers dimmed, "vs"/TBD for unplayed). Bottom bar: NEXT MATCH + TEAM shortcut (back from Team returns to the competition screen via `teamReturnTo` context; hub TEAM returns to hub). Pre-Match screen (two pool-screen pools, 6 formation markers, phase-aware match label, PLAY). Full nav flow: Hub → Game Mode → Competition → Pre-Match → SampleScene_PoolB (the one match scene — the old SampleScene + SELECT POOL step are retired, see 2026-07-06 session log). Universal back-button sprite.

## A9. Controls (keyboard — for PC testing; touch comes later)

- **WASD / arrows** — move active player.
- **Hold LeftShift** — **sprint** (2x speed while moving). Sprinting WITH the ball = **loose hold**: you keep the ball but opponents get 2x steal range + a steal-chance bonus (`looseHoldStealBonus` 0.15 on BotMovement/TeammateAI).
- **E** — grab / drop a loose ball.
- **Hold Space** — charge & shoot (release to fire). Charge past 0.7 (bar strobes white) = the HIGH shot: an untouchable asymmetric arc landing 1.5u before the goal line you aimed at. Shots travel ×1.35 faster than passes and punch-snap on release.
- **Hold B** — charge & pass. **DIRECTIONAL (FIFA-style):** the ball goes where you AIM (the facing triangle / joystick / WASD), not auto-homed to a teammate. A gentle `passAssist` (default 0.3) bends it toward a teammate that's roughly along the aim; aim at the keeper or empty water and it goes there. **Every pass now flies a small arc** (untouchable mid-flight; the receiver collects it where it lands — a mis-aimed pass still lands in empty water / with the enemy). Tunables on PlayerMovement: `passAssist`, `passAssistRange`, `passAssistMinDot`, `passAccuracy`, `passInaccuracyDegrees`.
- **Space (when NOT holding)** — attempt steal (chance-based; must be in front of the carrier).
- **C** — manual player switch (mostly redundant: control auto-follows the ball-carrier).
- **Z** — cycle team defense: **Press → Zone → Drop → MPress**.

## A10. What's working today (DONE)

Movement, ball carry (parented), charged shoot, aim line; passing with control hand-off; two AI teammates + two AI bots on ONE shared C# brain (carrier shoots/passes/dribbles, nearest presses, others hold a spread formation, support gets open); AI shoots at the goal CORNER via direct velocity (mass-independent); post-release grab cooldown (0.35s) so shots/passes travel; goalkeepers block shots; pool walls; two team-aware goals; on-screen scoreboard; formation spacing that auto-spreads any roster; **auto-switch control to whoever on your team holds the ball.**

Also now DONE:
- **Human B-pass** to nearest teammate.
- **Charged, direct-velocity shooting** (mass-independent).
- **Human steal on Space** (chance-based, with cooldown) when not holding the ball.
- **AI steal** — pressers strip the carrier (chance-based, with cooldown).
- **AI catch-then-shoot settle delay** — carrier squares up before shooting.
- **4-quarter match timer** (90s/quarter, tunable) with **win/lose/draw at full time**.
- **Scaled from 2v2 → 4v4 → 6v6** (formation + AI scale with no code change).
- **Player/ball/keeper sprites scaled down** to reduce crowding.
- **Role-based positioning** — Center, Center-Back, Wings, Flats assigned by roster index; each role holds a distinct depth + lane.
- **1-to-1 marking** — each non-presser defender marks its counterpart on the enemy team; the single nearest player still presses the ball.
- **Facing-gated steal** — can't strip the ball from behind; the stealer must be in the carrier's frontal arc (both human and AI).
- **Kickoff formation reset** — both teams snap to a spread home shape on every goal and at match start (no more bunching).
- **Idle drift** — players that reach their spot gently float instead of freezing.
- **Shot/pass power bar** for the active player (fills while charging).
- **Chevron aim indicator** under the active player, replacing the old long aim line.
- **Dynamic threat-based mark-switching** — defenders re-pick the most dangerous man with hysteresis (no oscillation); coverage hands off automatically.
- **Charged passing** — hold B to charge, reusing the power bar. **Directional aim** (goes where the facing triangle points, with a tunable assist toward an aligned teammate — see Controls/A9), so passes can miss, be intercepted, or sail out if mis-aimed.
- **Selectable PRESS vs ZONE defense (player team)** — toggle with **Z**, shown on screen as "DEFENSE: PRESS/ZONE". Press = threat-based 1-to-1 marking with dynamic switching; Zone = goal-side spread, no man-chasing. Bots always use Press.
- **Defensive-AI spec COMPLETE:** role-based positioning + 1-to-1 marking + dynamic threat-based mark-switching (with hysteresis) + press/zone toggle.
- **Sprint duel** at every quarter start (incl. Q1): line-up + whistle, two sprinters race (human mashes **Space**), winner grabs → **kickoff pass to deepest teammate**, then play.
- **Goal restart:** the conceding team's centre gets the ball at centre after a short settle freeze.
- **30s shot clock** per possession (resets on possession change / goal / defensive exclusion; turnover + grab-ban at 0) and **halftime side-switch** (teams swap ends; scoring + keepers stay correct).
- **Exclusion system:** failed steal = foul; 2 fouls in 10s → 5s exclusion; 3rd → permanent removal; **forfeit under 4 players**. Man-up/man-down emerges from the roster auto-adapting.
- **Event feed** — rolling last-5 lines (goals, exclusions, turnovers, out-of-bounds, forfeit, halftime).
- **Out-of-bounds** off the top/bottom walls → possession to the nearest player of the team that didn't touch it last.
- **Held balls can't score** — only a released/loose ball (shot, pass, loose) counts.
- **Free throws with clearance** — an ordinary foul gives the fouled team a free throw: the shot clock pauses, the carrier is protected from steals, and enemies must back off `freeThrowClearance` (2.2).
- **Penalties (B16.11)** — an exclusion-level foul inside the attacking **2m zone** → penalty shot from |x|≈2.47 with an **aiming cone** and **everyone lined up behind the shooter**; human charges Space / AI auto-fires with a miss chance. (`PenaltyManager`.)
- **Goal-line out + corner restart** — a loose ball over the goal line (outside the mouth) → nearest opponent re-enters it just inside; a carrier pressing the end line → **corner restart** (ball + receiver placed at that corner). (`GoalLineOut`.)
- **Player goal-line clamp** — swimmers can't cross the goal line (x clamped to ±`playerLimitX` 6.9); ball + keepers excluded.
- **Deflection-aware last touch** — `BallTouchTracker` on the ball credits loose-ball deflections to the right team, so out-of-bounds + goal-line awards are correct after a deflection.
- **Keeper grab-and-control** — the keeper collects a slow loose ball near its net, holds, then distributes to an open teammate (bot auto-passes; player keeper passes out on **B**); the shot clock keeps ticking through the hold.
- **Counterattack** — winning the ball in your own half opens a fast-break window; the top advanced players sprint forward.
- **Man-up 4-2 umbrella / man-down zone** — distinct tactical shapes that emerge automatically when a team is up or down a player.
- **Drop + MPress defense modes** — **Z** now cycles four modes (Press → Zone → Drop → MPress): Drop = help defense fronting the centre with a sagging helper, MPress = press with one centre dropper.
- **Shot-quality + pass-risk decision logic** — the carrier scores shot quality (distance / angle / clear lane / pressure) against a threshold (0.42) to decide shoot-vs-pass, and weighs pass risk by pass distance (longer passes need a wider-open lane).

Also now 🟡 **WORKING (first pass — improve later, not 100% done):**
- **Drives** — a carrier with a beaten marker (or fresh screen boost) and a clear lane bursts to the 2m point at 1.35× carry speed; finishes with a shot, kicks out to the open man if help comes, aborts if the marker recovers. (Effectively bot-only: player-team carriers are human-controlled.)
- **Picks/screens** — a nominated wing/flat plants a screen on the carrier's marker; the carrier rubbing past gets a 0.5s "marker beaten" boost feeding the drive trigger; screener rotates out after the pick. Works for the human carrier as a physical block.
- **Bot adaptive defense** — bots re-pick Press/Drop every ~4s with hysteresis (Drop when man-down / sitting on a late lead / your Centre scored 2+; Press otherwise); changes logged in the event feed.
- **Dynamic Centre + wider offense** — the Centre fights for inside water (goal-side of its guard at the 2m point); wings/flats hold wider lanes, weak-side wing drifts wider; stronger anti-cluster spacing; passes also score the RECEIVER's shot quality + double bonus for an inside-water Centre feed.
- **Centre draws fouls** — steals on an inside-water Centre fail more often + a virtual foul on the offender → exclusions/penalties come faster (tunable/toggleable).
- New plumbing: `MatchContext.LastReleaser` (who released the ball) → `ScoreManager` tracks Centre goals per conceding team; `MatchTimer.RemainingSeconds()` + `MatchTimer`/`ScoreManager` singletons.

## A11. Known issues / tuning notes

- At 2v2, **passing is rare by design** (few open teammates) — comes alive at 6v6.
- Bots can feel **too strong** — lower Chase/Carry/Support speeds to tune.
- Some AI numbers (Shoot Range/Power ≈20) are placeholder/high — TUNABLE.
- Graphics are placeholder circles/squares on purpose — **art is a later phase, after gameplay is locked.**

**KNOWN ISSUES / NEXT:**
- Residual **clustering only when the ball + multiple players + an opponent genuinely converge** on the same spot — acceptable/realistic, not the old "everyone bunches" bug.
- **Done since last update:** drives + picks/screens + bot adaptive Drop + dynamic Centre (inside water) + wider spacing + Centre-draws-fouls — all 🟡 first-pass working, to be improved/tuned later (NOT counted 100% done).
- **Tuning watch-list:** `centerFoulBoost` virtual foul can make the first foul on an inside Centre escalate instantly (usually a penalty) — toggle it off or raise Fouls For Exclusion if too hot; drive trigger/lane radii; screen timings; Centre inside depth (1.2 from goal).
- **Next brick:** **tuning pass** (above + speeds, steal chances, shot quality threshold), then **VISUAL PASS 1** (sprites/caps/names/HUD), then touch controls.
- **Deferred visuals** (secondary per dev priority): crowd/stadium; water-flow effects. (Pool zone lines exist as `PoolLines`; keeper art/animation ✅ done; a FIFA-style follow camera with dynamic zoom ✅ done — `CameraFollow`.)
- **Other deferred:** weak no-hold deflection shot (a ball struck without a settled hold should be weaker than a settled one); corners on KEEPER deflections; referee. (Per-player **stamina system** ✅ now done — `StaminaSystem`.)

## A12. Immediate roadmap (next bricks, rough order)

1. **Scale teams** — 4v4 first (verify formation+AI), then 6v6. Mostly cloning objects + adding to lists. ✅ **DONE** (now 6v6).
2. **Match timer + win condition** — game currently never ends. ✅ **DONE** (4 quarters, 30s each tunable, win/lose/draw at full time).
3. **Steal mechanic** — take ball from a holder (key + success chance); ties into fouls. ✅ **DONE** (human steal on Space + AI pressers strip the carrier; fouls not yet wired).
4. **Keeper grab-and-control** — keeper collects a slow loose ball, distributes to an open teammate (bot auto / player on B); clock keeps ticking through the hold. ✅ **DONE**.
5. Smarter AI: pass backward/around a block, better shot selection. ✅ **DONE** (shot-quality + pass-risk logic, center feed, counterattack, man-up/down shapes, Drop/MPress).
6. Rule systems: shot clock, quarters, exclusions (see Part B §16). ✅ **DONE** (incl. free throws + penalties + goal-line out).
7. Touch controls (virtual joystick + 3 action buttons) for mobile. 🟡 **DONE (first pass)** — 3-button attack/defense scheme + joystick + stamina HUD + keeper control; swipe-evasion / hand-button still planned.
8. Then the whole shell: menus, onboarding, currencies, career/divisions, store (Part B §1–15).
9. Android build/test (Build Support module + phone over USB). iOS needs a Mac later.

**Deferred (documented, not built):** 1v1 keeper close-range mechanic (pressing PASS instead of
SHOOT to trigger a low/chip shot, possibly redirected into a pass if a teammate is in the lane) —
concept discussed but under-specified, needs a clearer design pass before building. Deferred.

### A12.1 NEXT BRICK DESIGN (in order)

**(a) Drives + picks + bot adaptive Drop — 🟡 BUILT (first pass, June 2026; improve later):**
- **Drives.** A perimeter carrier with a step on its marker and a clear lane attacks the cage: a timed burst toward the goal that draws help; if a second defender commits, kick to the now-open man. Hook into the carrier branch of `WaterPoloBrain` — when shot-quality is low but the marker is beaten (carrier has a lateral/forward step + lane toward 2m), set a **drive target** instead of holding the role spot. End the drive on: reaching ~2m (shoot), a help defender stepping in (pass to the vacated man), or losing the step.
- **Picks (screens).** An off-ball attacker sets a screen on the carrier's marker — moves adjacent to that defender on the side the carrier wants to attack, holds, and the carrier rubs off it. Add `ScreenSpot(carrier, marker)` to `TeamSide` plus a "set screen / use screen" role pair in the brain; the pick frees either the driver or a pop-out shooter.
- **Bot adaptive Drop.** Bots currently always Press. Give the bot `TeamSide` an evaluator that re-picks `defenseMode` every few seconds (with hysteresis): **Drop/MPress** when man-down, protecting a late lead, or the player's centre keeps getting deep 2m feeds; **Press** when chasing or man-up. Same `defenseMode` plumbing the player's Z cycle already uses, just automated.

**(b) Tuning pass** — a dedicated balance pass once (a) lands: chase/carry/support speeds, shoot range/power (~20 placeholders), steal chances, shot-quality threshold, free-throw/penalty timings. Goal: bots beatable but not weak.

**(c) VISUAL PASS 1** — first real art/HUD layer: player **sprites** + **caps** (team colours / numbers), **name labels** above each swimmer, directional facing, and a laid-out **HUD** (score with logos, quarter, timer, shot clock, exclusion countdowns). Still 2D, still pre-touch.

**then → touch controls** (virtual joystick + A/B/C + hand button) for mobile.

## Animation Overhaul Plan

**Approach:** Single bone rig per character using Unity Skinning Editor. One assembled character PNG image with bones placed directly on it. No separate body parts needed.

**Current rig location:**
- Scene: Assets/Scenes/CharacterRig.unity
- Object in scene: test_0
- Bones: bone_1 through bone_8 (8 total)
- Sprite Skin component attached to test_0
- Animator Controller: Assets/Sprites/Players/Animations/PlayerBodyAnimation.controller
- WIP animation clip: Assets/Sprites/Players/Animations/idle_body_new.anim

**Base character images:**
- Front view: Assets/Sprites/Players/Parts/denes-varga-front.png
- Back view: Assets/Sprites/Players/Parts/denes-varga-back.png
- Front view handles left/right movement via SpriteRenderer Flip X (already in PlayerAnimator.cs)
- Back view used when player moves up/down on screen

**Animation list — field players (front view):**
1. Idle/floating — gentle arm sway, body bob
2. Swimming — arms alternate stroke
3. Sprinting — faster arm movement
4. Holding ball — ball in right hand
5. Charge shot — arm pulls back
6. Shoot release — explosive forward throw
7. Pass — side arm throw
8. Lob pass — arm fully overhead
9. Skip shot — low fast release
10. Receiving/catching — both arms extend forward
11. Steal attempt — one arm lunges sideways
12. Defending idle — both arms raised
13. Blocking shot — one arm shoots up
14. Foul committed — arms spread slightly
15. Celebration — fist pump
16. Excluded/ejected — arms down swimming to corner

**Animation list — field players (back view):**
1. Idle/floating
2. Swimming
3. Sprinting

**Ball hide/show system:**
- When IsHolding=true: hide physics ball (SpriteRenderer.enabled = false)
- Show ball baked into holding animation frames
- On release: re-enable physics ball at hand position
- Needs code change in PlayerMovement.cs and BotMovement.cs

**Goalkeeper rig:** separate CharacterRig setup needed later, same approach.

**How to continue work:**
1. Open Unity
2. Project panel → Assets/Scenes → double-click CharacterRig
3. Click test_0 in Hierarchy
4. Window → Animation → Animation
5. Continue recording bone animations

## Animation System (Built June 2026)

> **Status: field-player visual animation is DONE and working in-engine** (sprite-swap edition).
> This **supersedes the bone-rig "Animation Overhaul Plan" above** — that single-rig SpriteSkin
> approach was built, hit a SpriteSkin↔sprite-swap incompatibility, and was **abandoned** (the bone
> assets are still in the project but unused; see Known issues). Everything here is automated by
> `Assets/Editor/AnimatorBuilder.cs` and driven at runtime by `Assets/PlayerAnimator.cs`. Applies to
> the **6 human red field players only** — bots (blue) and goalkeepers are untouched.
>
> **⚠️ 2026-06-20 UPDATE — the bone rig is BACK (for floating + holding).** The "abandoned" wording
> below is now only half-true. Flat FrontBody/BackBody sprite-swap is still the base for swimming /
> sprinting / throwing / stealing, but **floating and holding now use real bone-rigged SpriteSkin
> bodies** — `BoneBody` (floating) + `HoldBody` (holding) — added as extra children on each player.
> What made it work this time: each bone body lives on its OWN child with its OWN rig sprite, and only
> its RENDERER is toggled; we never mix a bone clip onto the sprite-swap body. Full detail in the
> "### 2026-06-20 — Bone bodies (BoneBody floating + HoldBody holding)" subsection just below.

### 2026-06-20 — Bone bodies (BoneBody floating + HoldBody holding)
Each red player now has up to FOUR body children: flat `FrontBody` + `BackBody` (sprite-swap, the
base) PLUS two SpriteSkin bone bodies shown only in specific states:
- **`BoneBody`** — instance of `Assets/Sprites/Players/test_0.prefab` (SpriteSkin on the `test` rig),
  scale 0.07/0.07/1, localPos 0, runs `BoneBodyAnimation.controller`. Shown ONLY while **floating**
  (`speed < 0.15 && !isHolding`). Wired to `PlayerAnimator.boneAnimator` + `boneRenderer`.
- **`HoldBody`** — instance of `Assets/Sprites/Players/hold_0.prefab` (SpriteSkin on the `hold` rig),
  scale 0.07/0.07/1, localPos 0, runs `HoldBodyAnimation.controller`. Shown ONLY while **holding the
  ball**. Wired to `PlayerAnimator.holdAnimator` + `holdRenderer`.

**Visibility rule (`PlayerAnimator.Update`):** `showBone = isFloating && boneRenderer && boneAnimator`;
`showHold = isHolding && holdRenderer && holdAnimator`. They're mutually exclusive (floating requires
!isHolding). When either is true the flat FrontBody + BackBody are hidden. Both bone Animators are kept
`.enabled = true` EVERY frame (toggling `.enabled` made the clip stutter/restart); only the renderer's
`.enabled` flips. `BobFloatSpeedMax` was raised 0.05 → 0.15 so slow drift still reads as floating.

**Controllers (`Assets/Sprites/Players/Animations/`):**
- `BoneBodyAnimation.controller` — `floating_body` (default) + an `holding` state whose motion
  currently ALSO points at `floating_body` (that holding state is unused) + an `IsHolding` bool param
  (also unused by the current code — HoldBody handles holding instead).
- `HoldBodyAnimation.controller` — single looping state, `holding_body.anim`, no params.

**Bone clips (recorded in CharacterRig, each on its own rig):**
- `floating_body.anim` (test_0 rig, front idle) — WORKING ✅
- `holding_body.anim` (hold_0 rig, front holding w/ arm motion) — WORKING ✅
- `floating_body_back.anim` (test-back_0 rig) — recorded, NOT wired to any body yet ⬜
- `holding_body_back.anim` — NOT recorded (the `hold-back` sprite's Auto-Weights fail silently) ⬜

**Editor tools (`AnimatorBuilder.cs`):**
- `Tools → Setup BoneBody All Players` — instantiate test_0.prefab as `BoneBody` on Player..Player6,
  wire boneAnimator/boneRenderer.
- `Tools → Setup HoldBody All Players` — instantiate hold_0.prefab as `HoldBody`, wire
  holdAnimator/holdRenderer.
- Each skips a player that already has the child and marks the scene dirty (never auto-saves).

**⚠️ PREREQUISITE — `hold_0.prefab` does NOT exist yet.** The `hold_0` rig lives only inside
`Assets/Scenes/CharacterRig.unity`. Before `Setup HoldBody All Players` does anything: open that
scene, drag `hold_0` into `Assets/Sprites/Players/` to create `hold_0.prefab`, then run the tool (it
logs a clear error and adds nothing until the prefab exists). `test_0.prefab` already exists, so
BoneBody works right now.

**Held-ball hand positioning (`PlayerMovement.cs`, presentation-only):** five Inspector-tuned offsets
— `handOffsetRight` / `handOffsetLeft` / `handOffsetUp` / `handOffsetUpLeft` / `handOffsetDown` —
chosen by `HeldBallHandOffset()` (back/up by velocity.y + aim.x; explicit left/right by aim.x;
down/idle = one fixed offset) and pinned to the hand in `LateUpdate` (world space). The down/idle case
is a SINGLE fixed offset (no flip): an earlier X-mirror-by-last-facing made the ball jump sides on
A→S vs D→S, so that mirror and the `lastHorizontalDir` field were REMOVED 2026-06-20. There is NO
`MirrorForFlip`/`MirrorForFlipBack` method (it never existed) and no `lastHorizontalDir` field anymore.

**Reverted (do NOT re-add blindly):** hiding the real ball's SpriteRenderer while HoldBody shows (so
only the ball baked into `hold.png` shows) was tried and FULLY reverted — it made the held ball
vanish. `PlayerAnimator` must never touch the ball renderer; the real ball stays visible, pinned to
the hand. Ball-facing for inactive teammates was also tried earlier and reverted (idlers read as
swimming).

### Technique — how the clips animate
Plain **SpriteRenderer sprite-swap** — no bones, no SpriteSkin. Each clip animates the
SpriteRenderer's `m_Sprite`, looping.
- **floating / holding / defending / stealing** → STATIC (one sprite held, looping).
- **swimming / sprinting** → 2-frame swap (`swiml` ↔ `swimr`).
- **throwing** → 2-frame swap (`throw-charge` ↔ `throw-release`).
- All **`_back`** clips use the same technique with the back-view sprites.

### ✅ What's built & working
- **Dual body per player:** two child GameObjects, `FrontBody` + `BackBody`, each a plain
  `SpriteRenderer` + `Animator`. Exactly one is visible at a time.
- **All 6 red field players** (`Player`, `Player2`–`Player6`) set up identically and wired.
- **Two controllers**, 7 states each, with AnyState transition priorities (below).
- **Direction switching** (`PlayerAnimator.cs`): `velocity.y > 0.3` → show **BackBody** (swimming
  away); otherwise show **FrontBody**; `FrontBody.flipX` follows `velocity.x` (sheets face right).
- **One-button tooling:** `Tools → Setup All Players`, then `Tools → Wire Animation Clips`.
- **Clean console:** zero errors / zero warnings.

### Player setup in SampleScene (per player)
- **Parent** (`Player`, `Player2`–`Player6`): `Rigidbody2D`, `PlayerMovement`, `CircleCollider2D`,
  `TeammateAI`, `Animator` (`PlayerAnimation`), `PlayerAnimator`. **Parent `SpriteRenderer` is
  DISABLED** (children render the body; the parent Animator is disabled too).
- **Child `FrontBody`:** plain `SpriteRenderer` + `Animator` (`PlayerFrontAnimation`), default sprite
  `test`, **scale 0.07/0.07/1**, position 0/0/0.
- **Child `BackBody`:** plain `SpriteRenderer` + `Animator` (`PlayerBackAnimation`), default sprite
  `test-back`, **scale 0.07/0.07/1**, position 0/0/0.
- **`PlayerAnimator` slots:** `frontAnimator`, `backAnimator`, `frontRenderer`, `backRenderer` — all
  wired by the Setup tool.

### Animator controllers
Both in `Assets/Sprites/Players/Animations/`.
- **`PlayerFrontAnimation.controller`** — 7 states. AnyState transition priority (top-down, first
  match wins; all `hasExitTime=false`, `duration=0.05`):
  `throwing` (IsShooting trigger + !IsHolding) → `stealing` (IsStealing trigger) →
  `holding` (IsHolding) → `defending` (IsDefending + !IsHolding) →
  `sprinting` (IsSprinting + !IsHolding) → `swimming` (Speed>0.1 + !IsHolding + !IsSprinting) →
  `floating` (Speed<0.05 + !IsHolding — fallback).
- **`PlayerBackAnimation.controller`** — identical structure, `_back` clips.
- **Parameters** (driven by `PlayerAnimator.cs`): `Speed` (float); `IsHolding` / `IsSprinting` /
  `IsDefending` / `IsExcluded` (bool); `IsShooting` / `IsStealing` (trigger). Sprint is gated
  `!IsHolding` (a carrier never reads as sprinting). `IsExcluded` has no clip yet.

### 🟡 Partially working
- **Throwing/shooting** transition timing needs tuning (release→throw feels off).
- **Back-view throwing** clip exists but is **untested in gameplay**.
- **Swimming** plays, but its frames are slightly larger than the static floating sprite, so there's
  a small size "pop" when switching floating↔swimming.

### ⬜ Not working / known issues
1. **Players too small** at scale 0.07 — needs a size pass (raise scale, or re-export sprites so a
   larger scale reads correctly).
2. **No hand anchor for the held ball.** Ball *parenting* already works (`PlayerMovement` does
   `ball.transform.SetParent(transform)` on pickup, `SetParent(null)` on release), but the ball sits
   at the body centre, not a hand — there's no `HandPosition` child. Holding visuals currently rely
   on the ball baked into `hold.png`.
3. **Floating + holding are now ANIMATED via bone bodies** (2026-06-20) — `BoneBody` (floating) and
   `HoldBody` (holding) SpriteSkin children play real bone clips (see the 2026-06-20 subsection
   above). The earlier "static / abandoned" problem came from mixing bone + sprite-swap on ONE body;
   the fix was a separate child per technique. Back-view bone floating/holding is still TODO.
4. **Size mismatch:** swimming frames are larger than floating → visible pop (same fix as #1).
5. **Throw/shoot timing** rough (see Partially working).
6. **Bots (blue team)** have no new animations — still the single-body `BotAnimator` + old clips
   (`idle`/`swim`/`sprint`/`hold`/`throw`/`steal`/`defend`.anim) with a red/blue controller swap.
7. **Goalkeeper** has its own animation system (`goalkeeper_*.anim`) — untouched.
8. **Defense debug circles** still drawn around players (existing debug visual).
9. **Back throwing** untested in gameplay (listed again for completeness).
10. **Asset hygiene (cleanup later):** on-disk names differ slightly from the ideal —
    `throw-charge..png` (double dot), back-steal is `steal-back 1.png` (space + "1"); stray dupes
    `test 1.png`, `player_parts_red.png.png`. Orphaned bone-rig leftovers remain unused:
    `idle_body.anim`, `holding_back.anim`, `PlayerBodyAnimation.controller`, `swiml_0.controller`,
    the `test_0` / `test-back_0` prefabs, and `Assets/Scenes/CharacterRig.unity`.

### File locations
- **Sprites** — `Assets/Sprites/Players/Parts/`:
  - Front: `test`, `swiml`, `swimr`, `hold`, `throw-charge..png`, `throw-release`, `defend`, `steal`.
  - Back: `test-back`, `swim-backl`, `swim-backr`, `hold-back`, `throw-charge-back`,
    `throw-release-back`, `defend-back`, `steal-back 1`.
- **Clips** — `Assets/Sprites/Players/Animations/`:
  - Front: `floating`, `swimming`, `sprinting`, `holding`, `throwing`, `defending`, `stealing`.
  - Back: `floating_back`, `swimming_back`, `sprinting_back`, `hold-back`, `throwing_back`,
    `defending_back`, `stealing_back`.
- **Controllers** — `PlayerFrontAnimation.controller`, `PlayerBackAnimation.controller` (same folder).
- **Runtime script** — `Assets/PlayerAnimator.cs` (reads `PlayerMovement` + `Rigidbody2D`, drives
  both bodies, front/back switch + flipX).
- **Editor tooling** — `Assets/Editor/AnimatorBuilder.cs` (menus: Setup All Players, Wire Animation
  Clips, Build Player Animator Controllers, Setup Player GameObjects).
- **Scene** — `Assets/Scenes/SampleScene_PoolB.unity` (the sole match scene since 2026-07-06; the old
  `SampleScene.unity` is retired — still on disk, but nothing loads it and it's out of Build Settings).

### How to add a NEW player (future workflow)
1. Generate front + back images for each pose (idle, swim, hold, throw, defend, steal) at the **same
   proportions** as the existing set (e.g. in GPT).
2. Import them to `Assets/Sprites/Players/Parts/` (Texture Type = **Sprite (2D and UI)**).
3. Create the animation clips referencing the new sprites (or reuse the shared clips if the art is
   shared).
4. Duplicate an existing `Player` GameObject (already has the parent components + `FrontBody`/
   `BackBody` children), or add a parent with `PlayerAnimator` and run `Tools → Setup All Players`.
5. Set the `FrontBody`/`BackBody` default sprites to the new front/back idle.
6. Update the clip sprite references (or give the player its own controllers) and re-check the 4
   `PlayerAnimator` slots.

### How to improve animations (future)
- **Add bone animation properly:** use the **Skinning Editor** on the sprite → place bones → paint
  weights → in the Animation window record the **bone Transforms** (NOT the SpriteRenderer).
  ⚠️ Do **not** mix bone clips and sprite-swap clips on the same body — that incompatibility is
  exactly what sank the first attempt; commit a body fully to one technique.
- **Better swimming:** generate mid-stroke frames and add keyframes to `swimming.anim`.
- **Better sprint:** generate more aggressive arm-position frames; give `sprinting` its own art
  rather than reusing the swim frames.
- **Quick wins:** fix body scale (#1), normalize sprite export sizes so floating/swimming match (#4),
  add a `HandPosition` child and parent the ball to it on pickup (#2).

## Player System Architecture

> Foundation for B9 (Currencies), B12 (Team Screen), B13 (Transfers). Nothing here is implemented yet — this is the agreed design.

**Two types of players exist in this game:**
1. **Bot players** — fixed rosters: each national team has 15 players with set stats, ratings, rarity, position, and name. Bot players ship baked-in as Unity assets (ScriptableObject or JSON) as the offline fallback. When the app starts and Firebase is reachable, Firestore is checked for a bot player patch. If a patch exists it overrides local data for that session and is cached. Bot stats can be updated remotely without an app update but the game never breaks if Firebase is unavailable.
2. **Human roster players** — fully flexible. The user buys, sells, and manages these. Maximum 17 players in a roster. These are stored locally AND synced to Firebase when logged in.

**Player card structure (each player has):**
- Unique ID (string)
- Full name
- Nation / team
- Position (GK, CB, LW, RW, CF, LF, RF)
- Overall rating (0-100)
- Stats: Speed, Shooting, Passing, Defense, Stamina, GoalKeeping
- Rarity: Common / Rare / Legendary
- Image URL (stored in Firebase Storage, loaded remotely — never bundled in app)
- Price in gold coins
- Is bot player: true/false

**Rarity system:**
- Common — white border, grey background
- Rare — blue border, blue gradient background
- Legendary — gold border, gold gradient background, animated shimmer effect

**Remote config rule:**
- All player stats, ratings, rarity, image URLs stored in Firestore
- Developer can change any player's stats, rating, rarity remotely without app update
- Bot players: see the baked-in + Firestore-patch rule under "Two types of players" above
- Unity loads player data from Firestore on app start, caches locally

**Save system architecture:**
- Guest mode: everything stored locally using JSON files in `Application.persistentDataPath`
- Logged in: local JSON is the primary save, Firebase is the sync backup
- On login: merge local progress into Firebase (local wins on conflict — player's progress is never wiped)
- Synced to Firebase: roster, coins, diamonds, career progress, purchased players
- NOT synced: match state, settings (stored local only)
- Payments require login — no purchases without an account
- Ads work for guests — ad revenue does not require login
- Small persistent reminder shown on profile/hub screen when guest: "Log in to save your progress across devices"

**Login methods (future implementation):**
- Google Sign-In
- Apple Sign-In (required for iOS)
- Email + Password
- Guest mode (local only, no Firebase)

**Admin control (via Firebase console):**
- Grant coins/diamonds to specific user by UID
- Grant specific players to user
- Change any player's stats/rating/rarity remotely
- Run events, change shop prices, adjust rewards
- All without any app update

**National teams planned at launch:**
- 16 national teams
- 15 players per team
- 1 goalkeeper per team
- Total: 240 players

**Implementation order (future sessions):**
1. Firebase project setup + Unity SDK integration
2. Player ScriptableObject structure in Unity
3. Firestore player data schema
4. Local JSON save system
5. Login screen (Google + Apple + Guest)
6. Roster management UI
7. Shop / transfers with real player cards
8. Cloud sync on login

---

# PART B — FULL FEATURE VISION (the whole game)

> The complete design. This is the destination. Tech references are Unity/C#. Status tags show what's built.
> (Original 3D ideas like "realistic 3D faces" are reinterpreted as 2D: animated portraits / sprites; detailed portrait art reserved for cards, managers, replays, celebrations.)

## B1. Loading Screen ⬜ NOT STARTED
- Dev's own image centered; spinning water polo ball below it as loading indicator. Background tile swappable.

## B2. First-Time User Flow ⬜ NOT STARTED
- **Intro video:** 10–15s looping when a new user first opens the game.
- **Enter Team Name:** subtext "You can change your team name at a later time." Rules: letters only (no numbers), min 3, max 30. One shared club logo at this stage.

## B3. Choose Your Manager ⬜ NOT STARTED
- Title "Choose Your Manager." Pool background. 3 managers as animated 2D portraits (subtle idle motion). Tap → zoom in. Options: **Name your Manager** (subtext "change later," letters only, min 3 / max 35), **Confirm**, **Change Manager** (zoom back to all 3). Confirm with valid name → next screen.

## B4. Sign Your Captain ⬜ NOT STARTED
- Title "Sign your captain." Pool + cheering fans. 9 FIFA-style rating cards with water polo player images; stats/cards author-defined. Tap card → screen slides, captain walks in. Options: **Change Captain** (back to 9 cards), **Confirm** → short video of chosen manager talking to chosen captain → Main Menu.

## B5. Returning User ✅ DONE-equivalent (concept)
- If onboarding complete, after loading screen go straight to Main Menu. (No onboarding/menu built yet, but the "skip to game" idea is trivial once menus exist.)

## B6. Main Screen Layout 🟡 PARTIAL (main menu + hub navigation shell DONE; real data/economy not)
- ✅ **DONE (June 2026):** `MainMenu` scene with `MainMenuUI.cs` — entire menu built in code at runtime (no prefabs): full-screen canvas (1280x720 scale-with-screen), background + logo from `Assets/Resources/Sprites/` via `Resources.Load<Sprite>`, PLAY / SETTINGS / QUIT buttons (navy, white bold TMP with cyan outline, 1.05x hover scale), 1s fade-in, "Water Polo Manager v0.1" footer. PLAY loads **HubScene**; SETTINGS is a stub (logs "coming soon"); QUIT quits.
- ✅ **DONE (June 2026, shell only):** `HubScene` + `NavigationManager.cs` — full navigation shell for B6–B15: persistent top bar (logo placeholder, team name, gold/diamond displays, settings gear stub) + bottom nav with 5 tabs, Career/Team/Transfers/My Club/Challenges placeholder screens, 0.3s fades. **All numbers are hardcoded placeholders; no economy, saving, or real data.**
- **Still future (the full vision):**
- **Top horizontal tab (always visible):** Settings icon + social link icon; Claim Rewards; Diamond currency (diamond + cyan bg + number); Gold currency (coin + number); Club logo + Team name.
- **Large buttons:** Career; Live ("Coming Soon", inactive).
- **Smaller buttons:** TEAM, TRANSFERS, My Club, Challenges.

## B7. Settings Screen 🟡 PARTIAL (hub gear opens a minimal stub panel — title + "nothing to configure yet" + OK; real options not built)
- Top tab stays; content area swaps; back arrow appears.
- Options: (1) Language `< >` instant — English/Russian/Georgian (+more). (2) Bot difficulty `< >` — Medium/Hard, default Medium. (3) Account — Log In/Out/Sign Up/Delete (Apple or Google Play); progress saved & synced across devices (Firebase planned). (4) Info links — FAQs, Legal Notices, ToS, System Info (external links).

## B8. Claim Rewards ✅ MOSTLY DONE (2026-07: working CLAIM flows in Missions + Season Pass track + reward slots; all grants through RosterManager)
- Popup: Season Pass + Activate Pass. Split horizontally: top = premium (pass) rewards, bottom = free. Rewards = coins/diamonds/items from wins/goals.

## B9. Currencies 🟡 MOSTLY DONE (gold/diamonds LIVE from RosterManager everywhere; earned/spent via shop, packs, rewards, ads; IAP still stubbed through IAPBridge)
> Foundation: **Player System Architecture** (end of Part A) — coins/diamonds stored in local JSON, synced to Firebase on login; payments require login, ads don't.
- **Diamond:** icon + cyan bg + number; rare; buy high-rated random players / upgrade when gold short.
- **Gold:** coin + number; buy normal/good players, upgrade pool, upgrade players, buy caps/swimwear.
- Both have **+** → shop popup (real-money items/players via Apple/Google billing); purchase adds item to game.

## B10. Club Logo / Team Name Popup 🟡 MOSTLY DONE (2026-07: full My Club customization screen — `ClubCustomizationUI` — crest/colors/country/rename, persisted in `Roster.club`, hub cluster updates live; crests + flags are procedural placeholders until real art)
- Manager standing, large club logo, overall team rating, changeable nationality flag, **Highlights** (saved goals), **Records** (games, W/L/D, goals for/against, biggest win/loss, win %, trophies).

## B11. Career Screen 🟡 PARTIAL (standings + pre-match built, career progression pending)

Game Mode screen opens when PLAY tapped on hub.
4 competition tiers — each unlocks after winning the previous.
Unlock state stored in PlayerPrefs (div1_won / pl_won / cc_won / wcl_won); set when the player wins a division's Final.

| Tier | Badge | Competition | Teams | Format | Unlock |
|---|---|---|---|---|---|
| 4 | Green | Division 1 | 16 | 2 groups of 8 + knockouts | Always open |
| 3 | Purple | Premier League | 16 | 2 groups of 8 + knockouts | Win Division 1 |
| 2 | Blue | Continental Cup | 16 | 2 groups of 8 + knockouts | Win Premier League |
| 1 | Gold | World Champions League | 16 | 2 groups of 8 + knockouts | Win Continental Cup |

All four divisions share the same tournament format (see `LeagueSeason.cs` in A5): 7 group matches, top 4 per group → QF → SF → Final.

Pool variants per competition (visual only, same match scene — SampleScene_PoolB):
- Division 1 → current outdoor pool (existing SampleScene_PoolB)
- Premier League → indoor club pool (future art)
- Continental Cup → arena pool with crowd (future art)
- World Champions League → Olympic arena (future art)

Card images in Assets/Resources/Sprites/:
division1-card / premier-league-card / continental-cup-card / world-champions-league-card

National team tournaments (European/World Championship) → v2 only.
This is a club management game.

NavigationManager.cs: PLAY button → GameModeScreen overlay
(not directly to SampleScene anymore).
Competition logic: group standings + knockout simulation built (LeagueSeason.cs);
promotion/relegation and real match-result reporting → not yet built
(pre-match PLAY still records a random placeholder score).

## B12. Team Screen 🟡 PARTIAL (DATA FOUNDATION DONE: real player cards + local-save roster + a working Team screen; drag-swap, captain, portraits, max-17 enforcement still to do)
> Foundation: **Player System Architecture** (end of Part A) — human roster (max 17), player card structure, rarity borders (Common/Rare/Legendary), images from Firebase Storage.
- ✅ **DONE (foundation, 2026-06-17):** `PlayerData` (card SO) + `PlayerDatabase` (Resources catalog) + `Roster`/`RosterManager` (local-JSON guest save, buy/sell/upgrade/set-starter) + `TeamScreenUI` (live formation + bench/market + working buttons) + `SamplePlayerGenerator` (Tools menu, 21 sample cards). Purely additive — the 6v6 match is untouched. Firebase sync, drag-to-swap, portraits, captain, and the max-17 cap are still future.
- Full-screen; pool with 7 positions in water polo formation + subs. Drag to swap, upgrade, set captain, sell, save lineup.

## B13. Transfers Screen 🟡 PARTIAL (shell built: 3 agent buttons with diamond prices, 6 fake player cards with BUY stubs, refresh countdown placeholder; no real market)
> Foundation: **Player System Architecture** (end of Part A) — buyable players come from Firestore (remote-patchable stats/prices), card rarity visuals, gold prices per card.
- Daily random players (mostly low-level; tiered rare/golden chances). **Agents** cost diamonds → secret player by tier (Common 40 / Rare 150 / Golden 375 diamonds). Not enough diamonds → payment popup.

## B14. My Club Screen 🟡 PARTIAL (identity half DONE 2026-07: crest/colors/country/name via `ClubCustomizationUI`; STADIUM/POOL upgrades + CAP COLOR/SWIMWEAR from the old shell were dropped in the hub redesign — pool VARIANTS via division progression is the planned replacement, not purchases)
- Full-screen. (1) Upgrade Stadium/Pool → more fans → more post-match money (win > loss). (2) Customize cap & swimwear (colors/designs).

## B15. Challenges Screen ✅ SUPERSEDED by the Missions system (2026-07: `MissionManager`/`MissionsUI` — real tracked Newcomer/Daily/Weekly/Global Cup missions with working CLAIM; the old Challenges shell is gone with the hub redesign)
- Popup; daily challenges ("Score 3 goals", "Win 5 games") → reward Gold + Diamonds.

## B16. MATCH GAMEPLAY (the core)

### B16.1 Pre-Match Intro 🟡 PARTIAL (pre-match screen built, intro anim not yet)
- Optional skippable intro (≤10s): both teams enter pool and warm up.

### B16.2 Match Start — Sprint Duel ✅ DONE
- At every quarter start (incl. Q1): ball at centre, all players line up on their own goal lines and freeze; after a whistle delay the two sprinters (each team's first available member) race. Bot swims at a fixed speed; the human **mashes Space** to go faster (boost decays). First to the ball grabs it; play + shot clock start. The winning AI centre then makes a **kickoff pass to its deepest teammate** before normal play. (`SprintDuel.cs`.)

### B16.3 Match Controls 🟡 PARTIAL (keyboard shoot/charge/skip/lob/directional-pass/steal + a 3-button mobile touch scheme with attack↔defense mode-switching all built; planned A/B/C + swipe-evasion + hand-button scheme not built)
- **A** — with ball: shoot (hold = power bar, directional arrow); without ball: aggressive defensive press. *(Charged-shot power bar for the active player is built ✅.)*
- **B** — with ball: regular pass (short=slow, long=fast, fast risks bad catch); without ball: pressure (not aggressive).
- **C** — with ball: high/long lob, late-game penalty-style lob (easier for keeper); without ball: manual player switch (auto-switch exists ✅; manual override).
- **Hand button ✋** — tap: pick ball up to hands; hold: water-polo hand movements; then A to shoot; single tap: release.
- **Joystick (bottom-right)** — 360° move; directs pass/shot aim via under-player arrow.
- **Swipes** — up = special evasion (pump fake/shoulder turn); down = different (reverse pivot); success = attacker rating vs defender rating; fail risks losing ball.
- **Shot/pass upgrades:**
  - ✅ **Charged shot** — hold Space = power + height (`shotHeight` 0..1); charge past 0.7 = the HIGH shot: an untouchable ASYMMETRIC arc (steep rise, hang, sharp drop) that lands 1.5u before the aimed goal line; shots are ×1.35 faster than passes and snap-punch on release.
  - ✅ **Skip/bounce shot** — **Q** + Space → fast LOW bounce shot (`BallFlight`; 35% keeper-fool chance).
  - ✅ **Arced passes** — EVERY pass (B, and every bot pass) flies a small untouchable arc; **F** + B = the big high LOB (both immune to interception mid-flight — contests happen at the landing point).
  - ⬜ **Block animation upgrade** — defending pose → one-arm raised block (still the arms-wide defend pose).

### B16.4 Camera & Visibility 🟡 PARTIAL (2D top-down + FIFA-style follow camera w/ dynamic zoom + directional chevron done; player names not yet)
- Dream-League-style overhead angled; faces not clear in play. Name above each player; directional arrow below showing heading. *(A directional chevron under the active player ✅ and a self-contained `CameraFollow` — weighted player/ball tracking, dynamic 3.8–5.0 zoom, hard boundary clamps, goal/shot screen-shake — are built ✅; player-name labels still TODO.)*

### B16.5 Match Structure ✅ DONE (shot clock + halftime side-switch built)
- 4 quarters, **90s each** (tunable), win/lose/draw at full time. **30s shot clock** per possession — resets on possession change / goal / defensive exclusion; at 0 → turnover with a grab-ban on the violating team until the other side touches the ball. **Halftime side-switch** after the middle quarter: attack/defend goals swap, scoring stays correct, keepers keep their physical goal. Each quarter restarts through the sprint duel; the clock pauses during freezes.

### B16.6 HUD 🟡 PARTIAL (split scoreboard + score-tab art ✅, stamina HUD ✅, pause button ✅; logos/full layout pass still to do)
- Split score (PlayerScoreText/BotScoreText) on a `score-tab.png` board ✅; quarter indicator; match timer; **stamina HUD** (P#/GK + bar, in `TouchControls`) ✅; pause button ✅ (`PauseMenuUI`, top-right); exclusion countdown.

### B16.7 Pause Menu 🟡 PARTIAL (core pause DONE June 2026; Team Management not)
- ✅ **DONE:** pause button (top-right, below the scoreboard) → `Time.timeScale = 0` + centered panel with PAUSED + RESUME / QUIT / TEAM MANAGEMENT (`PauseMenuUI.cs`, all built in code). QUIT asks for confirmation first ("If you quit, this match counts as a loss." → YES QUIT / CANCEL); YES QUIT returns to MainMenu (loss recording itself comes with the career system). TEAM MANAGEMENT is a placeholder button. Timer/clock stop automatically (both are `Time.deltaTime`-driven). Full-time result screen with PLAY AGAIN / MAIN MENU also done (`MatchResultUI.cs`, hooked into `MatchTimer`).
- **Still future:** actually recording the quit as a loss (needs career/standings); score / time elapsed / who-scored-when inside the pause panel; Team Management with subs (apply only after a goal/foul stop/exclusion end/quarter break).

### B16.8 In-Game Substitutions ⬜
- Players tap hands at pool edge; outgoing player must fully exit before new one enters; excluded/benched players uncontrollable during transition.

### B16.9 Exclusion System ✅ DONE (man-up/man-down via roster auto-adapt, not special-cased)
- A failed steal = foul (offender locked out, carrier keeps the ball). **2 fouls within 10s → 5s exclusion:** the player leaves its `TeamSide.members` slot (set null → formation + marking auto-adapt to the extra/missing man), parks in its goal corner, fully inert. **3rd exclusion → permanent removal** (GameObject disabled). If a team drops **below 4 players → forfeit** (other team wins, via `MatchTimer.ForfeitMatch`). HUD shows exclusion countdowns; event feed logs each. (`ExclusionManager.cs`.) Tunables: Foul Window 10, Fouls 2, Exclusion 5s, Max 3, Min Players 4.

### B16.10 AI Behaviour 🟡 PARTIAL (full defensive AI DONE in C#; only exclusion-based repositioning NOT yet)
- With ball → attack positions; lose ball → defensive positions; players hold assigned positions; opponent excluded → exploit extra man; own exclusion → shorthanded defense.
- **Built (defensive AI spec COMPLETE):** role-based positioning + 1-to-1 marking (nearest presses, others mark their man); facing-gated steal (no stealing from behind); dynamic threat-based mark-switching with hysteresis (coverage hands off automatically, no oscillation); selectable **Press vs Zone** defense for the player team (toggle **Z**, on-screen label) — Press = man-marking with switching, Zone = goal-side spread; bots always use Press.
- **Man-up / man-down:** now emerges automatically — an excluded player's roster slot is nulled, so formation spacing and marking re-solve for the extra/missing man with no special-case code (B16.9 done).
- **AI is C# state-machine logic (`WaterPoloBrain`), scaled by player stats.** The original "LLM-driven bots (LM Studio/llama.cpp/Claude API)" idea is **ABANDONED** — do not implement it; it's wrong for a real-time game.

### B16.11 Fouls & Rules ✅ DONE (free throws + penalties + goal-line out + corner restart; only keeper-deflection corners + referee left)
- **Done:** failed-steal fouls + exclusions (see B16.9); **free throw** on an ordinary foul (shot clock pauses, the carrier is protected from steals, enemies back off `freeThrowClearance`); **penalty shot** for an exclusion-level foul inside the **2m zone** (`PenaltyManager`: shooter on the penalty spot |x|≈2.47, aim cone, everyone behind the shooter; human charges Space / AI auto-fires with a miss chance); **top/bottom out-of-bounds** (loose ball at the edge → nearest player of the team that didn't touch it last, re-enters just inside, "Out - YOU/BOT" feed); **goal-line out + corner restart** (`GoalLineOut`, deflection-aware via `BallTouchTracker`); **held-ball goals ignored**; **player goal-line clamp**.
- **NOT yet:** corners specifically on KEEPER deflections; poolside referee.

### B16.12 Goals & Replays 🟡 PARTIAL (goal detection + scoring DONE; replays/celebrations/sounds not)
- Goal → auto replay; player can save replay (→ Club highlights); celebrations; specific crowd sounds.

### B16.13 Post-Match ⬜
- Final whistle → earn coins; if enough progress, pass rewards + daily task rewards.

## B17. Art & Character Notes 🟡 (basic sprite animation DONE & working in-engine; full art still a later phase)
- **Visual Pass 1 COMPLETE:** 7-state animation system fully working in-engine for both red and blue teams. Red team: PlayerAnimation.controller on Player1–6. Blue team: BlueAnimation.controller on Bot1–6, blue cap sprites in BlueTeam folder. AnimationClipBuilder editor tool builds and wires everything (Tools menu). Steal animation fires on every grab attempt. Defend animation proximity-gated (1.5 units). Sprint mechanic with loose-hold strip bonus. SpriteRenderer horizontal flipping. **Done since:** goalkeeper animation (8-dive `DiveState` controller) ✅ and ball-flight VFX (`BallFlight`: trail / skip / lob / high-shot / spin) ✅. **Remaining art:** scale consistency between idle and swim/sprint sprites; 15 total field-player states planned (7 done; 8 keeper dives done).
- Believable body types/faces. In live play faces not detailed (Dream-League style). Goal replays use close-up → detailed faces matter there. **2D approach:** small simple sprites in-match; higher-detail 2D portraits for cards/managers/replays/celebrations. Art is deliberately deferred until gameplay is locked. (Old SceneKit/3D-mesh/GLTF notes are obsolete — this is a 2D Unity game.)
- **Skeletal animation** (Unity 2D Animation package, free) planned for goal celebrations, player portrait cards, manager animations, special move sequences. Developer will animate manually for full control. Status: 🟡 planned, not started.

---

## FOR AN AI READING THIS

- Unity 6 / C# **2D** water polo game. Keep `TeamSide` + `MatchContext` + `WaterPoloBrain`. AI is **C# state machines, not an LLM.**
- Explain Unity steps **beginner-level, step-by-step** (name the panel + exact menu path).
- **After any full-script replace, remind Nikoloz to re-check drag-and-drop slots** (Part A6) and say exactly which object/slot.
- Don't suggest: Swift/SDL2/SceneKit, LLM-driven bots, web deployment, Stripe/PayPal, Tailwind. Mobile payments = Apple/Google billing, later.
- Nikoloz has **Claude Code in VS Code** — big multi-file AI work goes there; single-file features + guidance happen in chat.
- Commit routine: `git add . && git commit -m "..." && git push`. GitHub: https://github.com/Nikoloz-Todua
- Current focus: gameplay polish complete (stamina, FIFA follow camera, ball-flight VFX, full keeper control, goalkeeper animation, touch controls). Next priorities:
  (1) player number labels above heads (`PlayerLabel.cs`),
  (2) touch controls tuning on iPhone,
  (3) wall positions aligned to the visual borders,
  (4) event feed + defense-mode text repositioning,
  (5) main menu / game flow (Part B).
  Everything in Part B tagged ⬜ is future.

---

## SESSION LOG — 2026-06-15 (gameplay polish)

- **Ball scale fixed for good** (`BallFlight.cs`): scale recomputed from a clean base every frame,
  carrier scale divided out per-axis → always uniform, never drifts. Root cause was bots being
  non-uniform (`0.2 × 0.25`) — a spinning ball re-parented onto them baked shear that compounded
  each catch. Effects uniform, capped 1.2×, Lerp-smoothed; spin only > 6 u/s, never on a plain
  pass, snaps upright on catch.
- **Player goalkeeper = full player** (`Goalkeeper.cs`): while your own keeper holds the ball it
  moves freely in 2D (clamped to its half, never crosses its goal line), sprints, charges a shot,
  and charges a pass (hold-to-charge, scales speed). **No auto-pass** — you're in charge; it
  returns to its line only after you shoot/pass (shot clock still turns a stalled hold over).
  On-ball HUD: green triangle + facing chevron + power bar. Bot keeper unchanged.
- **Charge / UI** (`PlayerMovement.cs`): shot/pass charge is **time-based** (shotChargeTime 0.7s,
  passChargeTime 0.45s) so it's snappy regardless of the high `maxShootPower`; min shot-speed
  floor so a tap never "drops"; power bar redesigned (dark rounded track + green→yellow→red).
- **Touch** (`TouchControls.cs`): tighter button cluster (~25px gaps at 1.5× size), smoother
  attack↔defense fade (SmoothStep, 0.22s).
- **Bots / "I'm in charge"** (`WaterPoloAI.cs`): a player-team AI carrier never auto-acts (holds
  for the human). Bots: calmer passes (13→11, +0.35s settle), shoot within 3.5u not from anywhere
  (had ShootRange 20), sprint (×1.7) to chase/cover, faster mark switching (0.6→0.35s). Keeper as
  a pass target stays a 10% last resort.

## SESSION LOG — 2026-06-16 (sprint-duel rebuild + quarter break; fixed last session's tap-sprint regressions)

The previous session's tap-charge sprint broke several things; all found + fixed, plus the new
sprint-duel / quarter-break features built.

- **Regressions fixed:**
  - *Sprint duel didn't react to input* — `SprintDuel.cs` only read keyboard Space and had no
    real UI. Rebuilt: reads Space / LeftShift **and** a full-screen tap-catcher (mobile), with a
    visible SPEED bar so taps obviously matter.
  - *Ball not dead-centre at duel start* — the ball is now pinned to **(0,0,0) with physics OFF**
    for the whole countdown and only goes live at "GO!", so nothing can nudge it.
  - *Active player "sprinted by itself"* — at later quarters the auto-swimming duel sprinter was
    `FirstMember(team)`, NOT the active player, so a non-controlled swimmer moved while the
    camera sat on a frozen one. The duel now makes the human sprinter the **active player**
    (`TeamManager.ActivatePlayer`), and regular play is strictly tap-only (never auto-sprints).
  - *Teammates didn't follow during sprint* — `TeammateFollowMult` is applied in `MoveTo`;
    threshold aligned to the spec's **> 0.5** sprint intensity (20% hustle to hold formation).
  - *Sprint bar invisible* — TWO bars now: the regular-play **head bar** (0.6 × 0.08, red→green,
    hides 0.8s after the last tap) and the duel's tall **left-side SPEED bar**.
- **Sprint rebuilt to tap-FREQUENCY** (`PlayerMovement.cs`): taps/sec over a 0.5s rolling window;
  `boost = tps * 0.08 * moveSpeed`, capped at `moveSpeed * 1.8`. `SprintCharge` repurposed as a
  0..1 intensity so `CameraFollow` / `PlayerAnimator` / `StaminaSystem` / `WaterPoloAI` all keep
  working unchanged. Removed `sprintMultiplier` / the old accumulate-decay meter.
- **Quarter-end pause screen** (`QuarterBreakUI.cs` NEW + `MatchTimer.cs`): every quarter end
  (not full time) freezes play and shows "QUARTER N COMPLETE" + score + RESUME / QUIT; RESUME
  rolls into the next quarter's duel, QUIT → MainMenu. Self-bootstrapping (no scene object).
- **UI cleanup during duel/break** (`TouchControls.SetGameplayVisible`): joystick + action
  buttons + stamina HUD hide for the duel and the break, restored instantly afterwards.
- **Clean console:** zero errors, zero warnings (verified via IDE diagnostics on every changed file).
- **Slot re-check:** `SprintDuel`'s old optional **Duel Text** slot is GONE (it builds its own UI).
  `PlayerMovement`'s **Ball** + **Aim Line** slots are untouched — just confirm they're still set on
  Player1–6. New tunables (PlayerMovement sprint window/boost; SprintDuel countdown step) show with
  safe defaults. Nothing new needs wiring (QuarterBreakUI + duel UI build themselves at runtime).

## SESSION LOG — 2026-06-16b (sprint reverted to HOLD; camera overview; 5s countdown; post-goal duel)

Follow-up tuning after testing the tap-sprint rebuild:

- **Sprint is HOLD again in regular play** (`PlayerMovement.cs`, `TouchControls.cs`): hold LEFT
  SHIFT / the Sprint button → `moveSpeed * sprintMultiplier` (2×) while moving; release = stop.
  Removed the tap-frequency model **and the head sprint bar** entirely. `SprintHeld` restored;
  `SprintCharge` kept as a 0/1 proxy so `CameraFollow`/`PlayerAnimator`/`StaminaSystem`/
  `WaterPoloAI` need no changes. The TAP-for-speed mechanic now lives **only in the sprint duel**.
- **Camera overview until first touch** (`CameraFollow.cs` + `MatchContext.BallTouchedSinceReset`):
  at game start, after every goal, and between quarters the camera holds the full-pool overview
  at size 5.0 centred on (0,0) — no following — until a player/bot first grabs the ball, then it
  eases smoothly into the normal follow (baseSize 4.2). Flag flips true on the first `SetPossession`
  to a team, reset by `SprintDuel.StartDuel` + the post-goal restart.
- **Countdown is 5s** (`SprintDuel.cs`): 5 → 4 → 3 → 2 → 1 → GO! (`countdownStart`), 1s each, same
  pulse + hint.
- **Post-goal = celebration + 3s silent pause + sprint duel** (`ScoreManager.cs`): after a goal the
  ball sits loose at (0,0), everyone frozen, no UI, for `goalFreezeSeconds` (1s) + `postGoalPauseSeconds`
  (3s); then `SprintDuel.StartDuel()` runs the 5-count race for possession (replacing the old
  conceding-team kickoff, which remains only as a no-duel fallback).
- **Untouched (as requested):** goalkeeper, exclusions, passing, shooting, AI brain decisions.
- **Clean console:** zero errors/warnings (IDE diagnostics on every changed file).
- **Slot re-check:** `PlayerMovement` on Player1–6 now shows a **Sprint Multiplier** field (default 2)
  in place of the removed tap fields — its **Ball** + **Aim Line** slots are untouched, just confirm
  they're still set. `ScoreManager` (on the ScoreManager object) gains a **Post Goal Pause Seconds**
  field (default 3); its existing slots (Ball / score texts / teams) are unchanged. `SprintDuel` gains
  a **Countdown Start** field (default 5). Nothing new to wire.

## SESSION LOG — 2026-06-17 (real-water-polo flow: duel = quarter starts only; goals = silent restart)

Corrected the game flow so it behaves like real water polo: a **goal is no longer a mini
match restart** — the sprint duel now happens ONLY at quarter starts; goals get a quiet
conceding-team restart. Off-sprinter swimmers also stop freezing during the duel.

- **No sprint duel after a goal** (`ScoreManager.cs`): removed the `SprintDuel.StartDuel()`
  hand-off. The goal restart is now a self-contained 4-phase flow: (1) 1s celebration freeze,
  ball loose at (0,0), camera → overview; (2) both teams snap to a **natural spread** inside
  their own halves (not a rigid goal-line), the **conceding team** takes the ball at exact
  centre; (3) a 3s **silent** restart pause (frozen, no UI, no countdown); (4) un-freeze and
  the team in possession begins the attack (bot relays a kickoff, human is free). `SprintDuel`
  is now triggered ONLY by `MatchTimer` (Q1 + each quarter) — verified there are no other callers.
- **Off-sprinters jog to formation during the duel** (`SprintDuel.cs`): at GO! only the two
  designated sprinters race; **every other swimmer (both teams) immediately swims to its
  formation at ~60% speed** instead of freezing, then transitions straight into normal AI when
  a sprinter grabs (no brain reset). The sprinter now starts **slightly ahead** of its line
  (`sprinterForwardOffset`) so it's not confused with the goalkeeper, and is the active player.
- **Natural restart formations** (`TeamSide.cs` NEW `RestartFormationSpot(member, hasBall)` +
  `SnapToRestartFormation(hasBall)`): per-role distinct depth + lane (attacking spread when you
  have the ball, sat-back defensive spread when you don't), always inside the own half — reused
  by both the goal restart and the duel's formation jog.
- **Camera resume cue** (`MatchContext.cs` NEW `MarkBallTouched()`): the goal restart sets
  possession during the frozen pause, so the normal first-grab camera trigger is consumed; this
  re-arms it on un-freeze so the camera eases from the overview back into the follow.
- **Untouched (verified, as required):** `CameraFollow.cs` (already overview-at-5.0-until-first-
  touch → follow, driven by the `BallTouchedSinceReset` flag the above now sets correctly — no
  code change) and `PlayerMovement.cs` regular sprint (already HOLD LeftShift / Sprint button,
  no tap mechanic — the tap lives only in `SprintDuel`). No changes to passing / shooting /
  goalkeeper / exclusions / shot clock / WaterPoloBrain decisions / touch-control layout.
- **Clean console:** zero errors/warnings on every changed file (IDE diagnostics).
- **Slot re-check:** nothing new to wire. `SprintDuel` (on `GameManager`) shows two new tunables
  — **Sprinter Forward Offset** (1) and **Formation Move Speed** (3) — with safe defaults; its
  existing fields are untouched. `ScoreManager`'s slots (Ball / score texts / teams) are
  unchanged. No new Inspector references on any object.

## SESSION LOG — 2026-06-17b (player data foundation + real Team screen)

Built the **player data foundation** (B9/B12/B13 groundwork). PURELY ADDITIVE — the 6v6 match
is untouched (no edits to PlayerMovement/TeammateAI/BotMovement/WaterPoloAI/TeamSide/MatchContext
or any match-scene object). All new scripts live in `Assets/Scripts/` (+ one Editor tool in
`Assets/Editor/`); the only existing file changed is `NavigationManager.cs` (hub Team tab + live
top-bar currency).

- **Data layer (NEW):** `PlayerData` (ScriptableObject card), `PlayerDatabase` (lazy singleton,
  loads `Resources/Players/`), `Roster` (serializable: owned ids + 7 starter slots + coins +
  diamonds), `RosterManager` (self-bootstrapping singleton; local-JSON save in
  `persistentDataPath/roster.json`; `BuyPlayer`/`SellPlayer`/`UpgradePlayer`/`SetStarter`/
  `GetOwnedPlayers`/`GetStarters`/`TeamOverall`; auto-saves; seeds a default squad + funds on
  first run and self-heals an empty roster once a catalog exists).
- **Team screen (NEW):** `TeamScreenUI` replaces the placeholder Team tab — live 2-3-2 formation
  of the real starters, a scrollable owned-bench + buyable-market list, team OVR + gold/diamonds,
  and working BUY / SELL / UPGRADE / START buttons. Rarity-coloured card borders + silhouette
  placeholders. `NavigationManager.BuildTeamScreen` now attaches it; the top bar's gold/diamond
  read from `RosterManager` and refresh after each transaction.
- **Editor tool (NEW):** Tools → Generate Sample Players → 21 sample cards into
  `Resources/Players/` (all positions, mixed rarities/ratings; idempotent). **Run this once**
  before opening the hub or the Team screen is empty (it shows an on-screen hint if so).
- **Design notes:** owned cards are runtime `Clone()`s so upgrades never corrupt the source
  `.asset` (upgrades are therefore in-session only — Roster stores ids; add an upgrade-levels map
  later to persist them). No Firebase yet (guest-mode local save, per the plan).
- **Clean console:** zero errors / zero warnings on all new + changed files (IDE diagnostics).
- **Slot re-check:** nothing to wire — `RosterManager`/`PlayerDatabase` self-bootstrap and
  `TeamScreenUI` builds itself. No match-scene objects or slots were touched.

## SESSION LOG — 2026-06-19 (field-player sprite animation: bone rig tried, then reverted to sprite-swap)

Built the **field-player visual animation system** for the 6 human red players (full detail under
"## Animation System (Built June 2026)"). **No gameplay scripts were touched** — `PlayerMovement`,
`TeammateAI`, `BotMovement`, `WaterPoloAI`, `TeamSide`, `MatchContext` untouched; only the
animation/editor scripts and animation assets changed.

- **Dual-body setup:** every red player got `FrontBody` + `BackBody` children (plain `SpriteRenderer`
  + `Animator`), parent `SpriteRenderer` disabled, 4 `PlayerAnimator` slots wired — all via
  `Tools → Setup All Players` in `AnimatorBuilder.cs`. Body scale 0.07/0.07/1.
- **Controllers:** rebuilt `PlayerFrontAnimation` / `PlayerBackAnimation` (7 states; AnyState
  priorities throwing→stealing→holding→defending→sprinting→swimming→floating; `hasExitTime=false`,
  `duration=0.05`).
- **Bone-rig detour (ABANDONED):** tried a Unity 2D SpriteSkin bone rig (`test_0`/`test-back_0`
  prefabs + procedurally-generated bone clips). SpriteSkin deformation fights sprite-swap, so it was
  reverted to **plain SpriteRenderer sprite-swap**. Unused bone leftovers remain (`idle_body.anim`,
  `holding_back.anim`, `PlayerBodyAnimation.controller`, the test prefabs, `CharacterRig.unity`).
- **Clips wired** via `Tools → Wire Animation Clips`: floating/holding/defending(+back) = static
  sprite-swap; swimming/sprinting/throwing/stealing(+back) = multi-frame sprite-swap. The old
  `Tools → Generate Bone Clips` menu was removed (it would re-break the sprite-swap clips).
- **`PlayerAnimator.cs`:** no longer nulls the body sprite in `Awake()` (the body keeps its default
  sprite; clips drive `m_Sprite`); sprint is gated `!IsHolding`.
- **Clean console:** zero errors / zero warnings on the changed scripts.
- **Slot re-check:** after running the two tools, verify each of `Player`/`Player2`–`Player6` has its
  4 `PlayerAnimator` slots (frontAnimator/backAnimator/frontRenderer/backRenderer) filled and its
  parent `SpriteRenderer` unchecked.
- **This entry's doc task:** documentation only — added the "Animation System (Built June 2026)"
  section + this log. No code changed; no existing text removed.

## SESSION LOG — 2026-06-20 (bone rig REVIVED the right way: BoneBody float + HoldBody hold; hand-offset ball positioning)

Brought the bone rig back (it was abandoned 2026-06-19) but on SEPARATE children so it co-exists with
the flat sprite-swap bodies. **No AI/gameplay decision logic changed** — `PlayerMovement` only gained
presentation-only hand offsets + a ball-position helper; the match brain, shooting, passing, steal,
shot clock, etc. are untouched. Full detail under "## Animation System (Built June 2026) →
### 2026-06-20 — Bone bodies".

- **Two new bone bodies per red player (Player..Player6):**
  - `BoneBody` = `Assets/Sprites/Players/test_0.prefab` child, scale 0.07/0.07/1, controller
    `BoneBodyAnimation` — shown ONLY while floating; wired to `PlayerAnimator.boneAnimator` +
    `boneRenderer`.
  - `HoldBody` = `Assets/Sprites/Players/hold_0.prefab` child, scale 0.07/0.07/1, controller
    `HoldBodyAnimation` — shown ONLY while holding; wired to `PlayerAnimator.holdAnimator` +
    `holdRenderer`.
  - Both Animators stay `.enabled = true` always (toggling stuttered the clip); only the RENDERER
    toggles. FrontBody + BackBody hidden whenever a bone body shows.
- **New clips:** `floating_body.anim` (front, test_0 rig) ✅ and `holding_body.anim` (front, hold_0
  rig) ✅ both WORKING. `floating_body_back.anim` recorded but NOT wired ⬜. `holding_body_back.anim`
  NOT recorded — `hold-back` Auto-Weights silently fail ⬜.
- **New controllers:** `BoneBodyAnimation.controller` (state `floating_body` default; an unused
  `holding` state that also points at `floating_body`; unused `IsHolding` bool) and
  `HoldBodyAnimation.controller` (one looping `holding_body` state).
- **New editor tools** (`Assets/Editor/AnimatorBuilder.cs`): `Tools → Setup BoneBody All Players`
  and `Tools → Setup HoldBody All Players` (instantiate the prefab as the child on all 6, wire the
  two slots, skip if present, mark scene dirty — never auto-save).
- **PlayerAnimator.cs:** added `boneAnimator/boneRenderer/holdAnimator/holdRenderer`; `showBone =
  isFloating && …`; `showHold = isHolding && …`; both bone Animators forced enabled; front/back hidden
  when showBone||showHold; `BobFloatSpeedMax` 0.05 → 0.15 (slow drift still floats); IdleBob unchanged.
- **PlayerMovement.cs — held-ball hand positioning:** 5 Inspector-tuned offsets (`handOffsetRight/
  Left/Up/UpLeft/Down`) + `HeldBallHandOffset()` + `LateUpdate` world-space pin. **Bug fixed:** the
  down-facing ball jumped sides depending on the last A vs D facing — the down case X-mirrored by
  `lastHorizontalDir`. **Removed `lastHorizontalDir` entirely**; down/idle now returns a single fixed
  `handOffsetDown`. NOTE for the next AI: there is NO `MirrorForFlip`/`MirrorForFlipBack` method and no
  `lastHorizontalDir` field — earlier conversation references to those are obsolete. (Gameplay tunables
  unchanged this session: auto-grab within `grabDistance` 1.6u and pass speeds `minPassSpeed` 9 /
  `maxPassSpeed` 16 are as previously set.)
- **Reverted today (do not blindly re-add):** (a) hiding the real ball's SpriteRenderer while HoldBody
  shows — made the held ball vanish, so it was fully reverted; `PlayerAnimator` must never touch the
  ball renderer. (b) Ball-facing for inactive teammates — made idlers read as swimming.
- **⚠️ MUST DO before HoldBody works:** `hold_0.prefab` does NOT exist yet. Open
  `Assets/Scenes/CharacterRig.unity`, drag `hold_0` into `Assets/Sprites/Players/` to create
  `hold_0.prefab`, THEN run `Tools → Setup HoldBody All Players` (until then it errors and adds
  nothing). `test_0.prefab` exists, so BoneBody already works.
- **Known issues / NOT done:** (1) `holding_body_back.anim` not recorded — `hold-back` Auto-Weights
  broken (`git checkout -- "Assets/Sprites/Players/Parts/hold-back.png"` to restore original, then
  re-rig). (2) `floating_body_back.anim` unwired — needs a `BackBoneBody` (test-back_0) child + back
  controller + velocity.y switching. (3) Back view while holding still shows the flat `hold-back`
  sprite-swap — acceptable for now. (4) Swimming↔floating size pop persists (sprite export — needs
  art). (5) Blue team + goalkeeper animations untouched by design.
- **Works perfectly:** front floating bone anim; front holding bone anim; all 5 hand offsets tuned;
  all 6 players have BoneBody (+ HoldBody once the prefab exists); float threshold 0.15.
- **Clean console:** zero errors / zero warnings on every changed file.
- **Slot re-check:** after running the two Setup tools, verify on each `Player`/`Player2`–`Player6`
  that `boneAnimator` + `boneRenderer` and `holdAnimator` + `holdRenderer` are filled, alongside the
  existing `frontAnimator`/`backAnimator`/`frontRenderer`/`backRenderer`. FrontBody/BackBody slots are
  untouched.

## SESSION LOG — 2026-06-21 (Hub UI redesign + back bone animations)

**ANIMATION SYSTEM:**
- Added BackBoneBody child (test-back_0.prefab, BackBoneBodyAnimation.controller,
  floating_body_back.anim) — shown when floating AND moving toward own goal
- Added BackHoldBody child (back-side_0.prefab, BackHoldBodyAnimation.controller,
  holding_body_back.anim) — shown when holding AND moving toward own goal
- holding_body_back.anim recorded after fixing back-side_0 bone weights
  (Auto Weights failed; bones were outside mesh — manually repositioned via Edit Bone)
- showBack logic: vel.x < -FlipEpsilon (moving left) with lastShowBack latch
  so direction is remembered when stick released; floating idle always resets to front
- P3/P4 hand offsets: 5 new Swapped fields (handOffsetRightSwapped etc) in
  PlayerMovement — completely independent from P1/P2 values, selected via
  defendGoal.position.x > 0
- AnimatorBuilder.cs: Tools > Setup BackBoneBody All Players +
  Tools > Setup BackHoldBody All Players added
- PlayerAnimator.cs: backBoneAnimator/backBoneRenderer/backHoldAnimator/
  backHoldRenderer slots added; anyBone logic hides flat front/back sprites
- Clean console: zero errors, zero warnings

**HUB UI (NavigationManager.cs — full rebuild):**
- Removed all bottom nav tabs (CAREER/TEAM/TRANSFERS/MY CLUB/CHALLENGES)
- Background: main-page-background.png full screen
- Top bar: avatar circle + My Club + XP bar + level badge + gear button +
  diamond/gold currency with [+] buttons
- Left column: ranking-button / shop-button / team-button (140/140/115px)
- Top right: SEASON ENDS IN panel (placeholder 2D 10H)
- Bottom bar: season-pass-button (260x80) + missions-button (90x90, red badge) +
  4 card slots (3H/7H/12H/24H placeholders) + play-button (320x120)
- RANKING/SHOP → COMING SOON overlay panels
- TEAM → existing TeamScreenUI
- PLAY → loads SampleScene
- Removed welcome panel
- Globe overlay: traced — not a code bug; was MainMenu logo sprite
  rendering in editor session only. Not present in saved HubScene.
- Quit routing: MatchResultUI + PauseMenuUI + QuarterBreakUI all now
  load HubScene instead of MainMenu on quit

**TEAM SCREEN — FINAL PASS (same session):**
- Player slot cards reduced to 75x90px, portrait 38px
- Formation Y positions tuned manually across multiple passes — final SlotOffset values:
  GK Y=-80, CB Y=-30, LW/RW Y=10, CF Y=20, LF/RF Y=40
- Position tab buttons (wings/center/defender/goalkeeper) fixed to 70x45px, gold color when
  selected, faded white when unselected, no scale/border/background
- Wings tab replaces Attacker tab: covers LW/RW/LF/RF
- All purchasing removed from team screen (BUY/SELL/UPGRADE)
- Left panel buttons (formations/players/substitutions) text overlays removed — images have
  baked-in text
- Formations button 220x80px, others 200x70px
- Globe overlay on team screen: editor artifact only, not present at runtime
- Zero errors, zero warnings
- NOTE: formation position values may be hand-tweaked further by the user between sessions

**KNOWN REMAINING:**
- Globe white circle still visible in editor (editor artifact, not runtime bug)
- Season pass image needs better sprite
- Pool variants system (replaces pool upgrades — unlock better pools via
  division progression, not purchases) — NOT YET BUILT
- Main menu flow: MainMenu.unity still exists as launch screen (plain);
  consider skipping it and launching HubScene directly
- ~~Card slots are visual only~~ → reward slots are now LIVE (see 2026-07-02 log)

---

## SESSION LOG — 2026-07-02 (shop rebuild + pack unification + hub profile cluster + My Club)

**SHOP REBUILT (ShopUI.cs) — one continuous shelf:**
- The 9 isolated tab panels became ONE horizontal ScrollRect: all sections laid
  side by side, free drag scrolls through the whole shop like a shelf
- Bottom bar: 9 plain-text tabs (active = green + underline, small FREE pills on
  Daily Deals / Free Prizes / Ads Pack / Draft Tickets / Event); tapping glide-scrolls
  (0.4s hand-rolled SmoothStep, no tween package) to the section; while free-dragging
  the highlight follows whichever section's centre is nearest the viewport centre
- All 4 pack cards are uniform 250x400 from a single `BuildPackCard`

**PACK UNIFICATION — ShopPackType DELETED:**
- One pack identity: `CardTier` (Common/Rare/Epic/Legendary Card) used by BOTH shop
  purchases (instant open) and post-match reward slots (timed unlock). Prices live on
  the tier defs (100/100/250/400 gems; Legendary $2.99 + guaranteed-legendary flag;
  Common watch-ad option). ONE odds table per tier; probability numbers unchanged
- `PackInfoPopup` (in PackRevealUI.cs): the ONE "i" drop-rate popup, identical in the
  shop and on reward slots (NavigationManager's popup calls its `BuildOddsRows`)

**WATCH-AD CAPS (`AdWatchCap`, in ShopUI.cs):**
- Every WATCH button (shop packs, deals refresh, free prizes, ads pack, hub FREE +100)
  has its OWN PlayerPrefs counter, 3 uses per UTC day; at cap it greys out showing
  "RESETS IN Xh". Buttons are compact "WATCH ▶" (procedural play-triangle, no camera art)
- Free Prizes changed from 6h cooldowns to watch-to-claim under this cap
- Daily Deals gained an ad-watched REFRESH that rerolls which 3 packs are on sale

**SPRITE IMPORT GOTCHA (the rare-card bug) — READ THIS BEFORE ADDING PNGs:**
- User-dropped PNGs can import as Sprite Mode MULTIPLE and get auto-sliced; then
  `Resources.Load<Sprite>` returns a tiny fragment (rare-card.png sliced into 12 → the
  Rare pack rendered invisible; refresh made cards "disappear" whenever Rare rotated in)
- Fix: `CardPack.TierArtSprite(tier)` loads the raw Texture2D + alpha-trims to the
  content box → all pack art uses IT, never raw Load<Sprite>. The 4 card metas were
  hand-set to spriteMode 1 + isReadable 1 (readable is required for the trim;
  non-readable falls back to untrimmed). Source art sizes differ wildly
  (common/rare 1536x1024 with huge margins, legendary 500x500 tight) — the trim is
  what makes them render uniform

**PACK/SLOT ANIMATIONS (PackCardFX.cs, NEW):**
- Idle sine float (~5px, ~2s, random phase) + diagonal shine sweep every 3-4s
  (one shared procedural gradient sprite, RectMask2D-clipped, raycast-transparent)
- On every shop pack card + Coach's Choice + hub reward slots in Ready/Unlocking state
- Active reward slots also scale 1.18x (slot pitch widened 78→84 so nothing overlaps);
  Locked/empty slots stay static
- New reward from a match → on next hub load that slot scale-ins with overshoot
  (ease-out-back 0.5s; chose in-place scale-in over a fly-in — the slot row rebuilds
  wholesale on every state change, a flying temp icon would be fragile) — one-shot flag
  `PostMatchRewardManager.ConsumeNewRewardSlot()`

**HUB PROFILE CLUSTER + MY CLUB (NavigationManager.cs + ClubCustomizationUI.cs NEW):**
- Top-left cluster: circular avatar (club-primary tint, crest glyph, country dot
  badge) + "Guest_XXXXXX" name (generated once, persisted in roster.json) + XP/level +
  gear (stub settings panel) + envelope/gift (COMING SOON overlays) + FREE +100 ad pill
  (+100 coins, AdWatchCap 3/day). Avatar or name → My Club screen
- My Club: 8 procedural crest shapes, 10-color primary/secondary swatches, 12-country
  text picker, rename field (TMP_InputField, max 16), APPLY persists `ClubProfile`
  in Roster + updates the hub instantly
- PLACEHOLDER ART STILL NEEDED: real avatar art, real crest set, real country flags

**KNOWN REMAINING (this area):**
- All "watch ad" flows fake the ad (0.8s pause) — real rewarded-ad SDK still TODO
- IAPBridge still a stub (grants immediately)
- Envelope/gift/settings destinations are stubs by design
- Gameplay flow of the new pieces NOT fully play-tested (pack odds, cap resets at UTC
  midnight, club profile across relaunch)

---

## SESSION LOG — 2026-07-02b (missions + ranking + season pass; FX scope fix)

**FX SCOPE FIX (Task 0):** shop pack cards' float/shine now attach to the pack ART Image
only — titles, player counts and buy buttons stay still. Hub reward slots unchanged
(whole-slot float/shine is intentional there).

**THE ONE SEASON (SeasonPassManager.cs, NEW):** 14-day season epoch stored in
seasonpass.json — THE single season concept. Drives: hub "SEASON ENDS IN" (now a real
countdown AND a button → Season Pass screen), Global Cup mission scope, league season
rollover, shop EVENT badge/section countdowns. 16 tiers × 100 XP. XP: +25 win / +10 loss
(EndMatch hook) + 10 per mission claim. Gold Pass = 500 gems (PLACEHOLDER price, via
SpendDiamonds — swap to IAPBridge product if it becomes a cash buy). Free row always
collectible; gold row padlocked until ACTIVATE. The reference's separate "Free Pass card"
was folded into the free row (one track, no duplicated reward list).

**MISSIONS (MissionManager.cs, NEW):** Newcomer(6, one-time) / Daily(3, UTC-day reset) /
Weekly(3, 7-day reset) / Global Cup(3, season-scoped). ONLY 3 real stats
(matches_played/matches_won/packs_opened) hooked at TWO existing points:
- MatchTimer.EndMatch — THE combined post-match block (mission stats + league points
  + season XP, one place, don't add more call sites)
- CardPack.OpenTierPack — the one pack-open completion point (covers shop, slots,
  mission/pass pack rewards; those pack rewards legitimately count as packs opened)
"Goals scored" mission deliberately NOT added (no clean accessible per-match goal stat
without new machinery). Hub missions button now opens the screen; its red badge = live
claim-ready count (hidden at 0).

**RANKING (LeaderboardManager.cs, NEW):** LEAGUE tab fully built but SIMULATED — 24
deterministic fake rivals per season from a gamer-tag pool; ONLY the player's points are
real (+20 win/+5 loss). IRON→BRONZE→SILVER→GOLD→DIAMOND; top 5 promote / rank 20+ demote
at season rollover; "LAST WEEK" shows the recorded previous-season result (honest "no
previous season yet" on the first one). Player row pinned at the panel bottom always.
ELITE LEAGUE / WORLD / FRIENDS / COUNTRY = locked COMING SOON stubs — they require real
online accounts; NO fake data pretending to be networked.

**PLACEHOLDER ART in these screens:** medal circles, avatar color-dots, coin/gem icons
(real gold-coin/diamond-coin sprites reused where they exist), padlock (procedural),
all tab bars text-only. Real art wanted eventually: medals, avatars, league emblems,
gold-pass art.

**KNOWN REMAINING:**
- Gold Pass price + XP amounts + mission targets/rewards are first-guess numbers — tune
- Season rollover promote/demote and mission/league season resets not yet play-tested
  across a real 14-day boundary (logic rolls multiple missed seasons in one step)

---

## SESSION LOG — 2026-07-03 (hub right column + icon tooltips; sprite-import fix round 2)

*(Previous session hit the usage limit mid-work; this session verified its edits actually
landed complete — `BuildOverlays()` and the whole NavigationManager compile clean, nothing
was left half-written — then finished the two follow-up fixes.)*

**RIGHT COLUMN (FIX A) — the code was never the bug:**
- `BuildRightColumn()` already had FRIENDS/CLUBS at 115×115 mirroring the left column's
  top two rows (anchor (1,0.5), x −150, rows +125/0). They rendered tiny (~50px) and
  misplaced because **all 5 new button PNGs (friends/clubs/settings/message/gifts-button)
  imported as Sprite Mode MULTIPLE** — the SAME gotcha as the 2026-07-02 rare-card bug:
  `Resources.Load<Sprite>` returns an auto-sliced fragment, not the whole image
- Fix: hand-set `spriteMode: 1` (Single) in all 5 `.png.meta` files. UI buttons load via
  `Load<Sprite>` and need Single mode; only pack art goes through `CardPack.TierArtSprite`
- RULE REINFORCED: after dropping ANY new PNG into Resources/Sprites, check its Inspector
  Sprite Mode is **Single** before wiring it to UI

**ICON TOOLTIPS (FIX B) — captions removed:**
- Settings/Inbox/Gifts permanent caption text under the top-bar icons is gone;
  `MakeCaptionedIconButton` now builds the label as an inactive tooltip text object
- New nested `IconTooltip` (NavigationManager.cs, next to `AddHover`): shows on mouse
  hover (pointerId < 0 = mouse), or after a 0.4s press-and-hold on touch; hides on
  exit/release. Deliberately minimal — no tooltip framework
- Icons re-centred vertically (the +9px caption offset removed)

**KNOWN REMAINING (this area):**
- Friends/Clubs open honest COMING SOON stubs (real feature needs online accounts)
- Unity must reimport the 5 sprites (open the editor once) before the fix is visible

---

## SESSION LOG — 2026-07-03b (button sizing root cause: PNG padding; hub buttons trimmed + enlarged; shine mask inset)

**THE REAL Friends/Clubs SIZE BUG — transparent padding INSIDE the PNGs:**
- The spriteMode fix (07-03) was necessary but not sufficient. Measured alpha bounds:
  friends-button content = 620×464 of a 1536×1024 texture (**40%×45%**), clubs 908×500
  (59%×49%), settings/message/gifts ~576×572 (38%×56%) — vs shop 80%×90%, ranking 73%×83%,
  team 77%×84%. Same 115px RectTransform → friends' glyph rendered ~46px. **Identical
  RectTransform sizes DO NOT mean identical visual sizes when source padding differs**
- Fix: `NavigationManager.LoadTrimmedSprite(path)` (nested, cached) — raw Texture2D +
  alpha-trim to content box, same logic/threshold(40) as `CardPack.TierArtSprite`;
  `MakeImageButton(..., trimArt: true)` opt-in flag. All 8 button metas hand-set
  `isReadable: 1` (required for the trim; unreadable falls back untrimmed)
- NOTE: clubs-button's trimmed content is intrinsically wide (908×500 ≈ 1.8:1) — it fills
  the box width but stays shorter than square art. If it must read taller, re-export the PNG

**HUB BUTTON SIZES (before → after, incl. the dev's same-day corrections):**
- Ranking 115→135, Team 115→135, Friends 115→135, Clubs 115→150 (its trimmed art is ~1.8:1
  wide, so it needs the bigger box to read equal); Shop STAYS 140 (dev: don't touch it)
- Both columns shifted DOWN: rows now yOff −40 ± step 140 (was centred at 0 ± 125) —
  centred columns read "too high" against the hub background
- Top-bar Settings/Inbox/Gifts stayed 42px (an 84px experiment was rejected — too big),
  centres 310/385/460 → 330/425/520; FREE +100 pill moved 590→660
- Tooltip rebuilt as dark pill (94×26, rounded, DarkPanel) + 13pt text under the icon

**SHINE SWEEP CONTAINMENT (PackCardFX.cs):**
- The RectMask2D sized exactly to the art bounds still leaked the sweep past visible art
  edges (alpha-trim threshold + sub-pixel slack). Now `mask.padding` insets ~4% per side
  (clamped 3-10px) — sweep guaranteed contained, slightly smaller by design

---

## SESSION LOG — 2026-07-03c (release self-collision fix; keeper-hold jam + camera; PoolTheme architecture)

**BALL RELEASE vs OWN BODY (the wrong/weak deflection bug) — root cause CONFIRMED as spawn
overlap, in TWO forms:**
- `PlayerMovement.Shoot()` snapped the ball to the player CENTRE before `simulated = true` —
  the ball collider woke up fully INSIDE the shooter's collider; physics depenetration fought
  the assigned velocity. `ChargedPass()` released at the HAND with no protection — a pass aimed
  back across the body (down-left while facing right) flew THROUGH the player's own collider
- Fix: `MatchContext.IgnoreReleaseCollision(releaser)` — ignores ball↔releaser Collider2D
  contacts for 0.3s (`Physics2D.IgnoreCollision`), then re-enables ONLY once they've separated
  (extension capped +1.5s) so a dropped ball at the feet never gets popped away. Called from
  EVERY release path: PlayerMovement Shoot/ChargedPass/DropBall/ReleaseBall, WaterPoloAI
  DetachBall (bot shoot/pass/release), Goalkeeper KeeperShoot/PassOut, PenaltyManager FireAIShot
- Shots now release from the HAND (hold position) — the snap-to-centre was the old workaround
  for exactly this overlap and is gone
- HOTFIX (same day): `IgnoreReleaseCollision` runs BEFORE un-parenting, so
  `GetComponentsInChildren<Collider2D>` on the releaser also returned the still-parented BALL's
  own collider → `Physics2D.Distance(ball, ball)` threw ArgumentException in the window
  coroutine. The ball's collider (and anything on its rigidbody) is now filtered out of the
  releaser list, plus defensive `c != ballCol` guards at every use

**KEEPER-HOLD JAM (game stuck when a keeper caught a shot under pressure):**
- Old loop: enemy crowding the keeper → panic `PassOut()` STRAIGHT INTO the crowder →
  deflection → slow loose ball on the goal line → keeper auto re-collects → forever
- Bot keeper now: after `keeperHoldSeconds` (0.8) it passes only when NOT crowded; while
  crowded it WAITS, and past `maxKeeperHoldSeconds` (3, new Inspector field) it forces the
  DEEP outlet (`PassOut(1f, forceDeep)` → `DeepestMember`) instead of a short pass
- Clearance rule (both sides symmetric): a protected ball-holding keeper gets working room —
  the AI presser holds a standoff spot at KeeperProtectRadius+0.4 (2.9u) instead of the old
  chase/push jitter, and the HUMAN-controlled player is now walked back out of 2.85u
  continuously (PlayerMovement.FixedUpdate) — the old shove fired only on steal ATTEMPTS, so
  standing still on the keeper jammed it. Attack resumes normally after the pass-out
- Keeper's teammates already open up on their own: possession flips to their team on the catch,
  so their formation switches to the attacking spread — no extra code needed

**CAMERA (keeper control):** while YOUR keeper holds the ball, CameraFollow anchored 60% on the
active FIELD player far from goal — the keeper played half off-screen. The follow anchor is now
the ball (pinned to the keeper's hands) whenever `KeeperHolding && KeeperHoldTeam == PlayerTeam`;
the existing 3.8 keeper zoom is unchanged.

**POOL THEME ARCHITECTURE (PoolTheme.cs, NEW — no new art):** see the A5 table row. Division →
theme lookup wired for all 4 divisions to the ONE "classic" theme (current art as no-overrides).
No placeholder recolors for other divisions — the water is a Shader Graph material, not a plain
sprite, so a "cheap recolor" isn't safely cheap. Ambient crowd/bench animation deliberately
deferred until real art exists.

**KNOWN REMAINING (this area):**
- `maxKeeperHoldSeconds` (3s) and the 2.85u human clearance are first-guess numbers — tune in play
- The shot clock keeps ticking through a long crowded keeper hold (by design; a hold is not a
  possession change) — a near-zero clock at the save can still expire during the wait; watch it
- Global Cup missions are season-scoped stats, not tied to a real "Global Cup" event yet

---

## SESSION LOG — 2026-07-04 (camera-keeper fix + the high-ball arc system) — retro-logged

*(This session was committed as `745b560` but never logged here; added retroactively because
the 2026-07-04b session builds directly on it.)*

**CAMERA DIDN'T FOLLOW THE ADVANCING KEEPER — root cause was NOT the camera:**
- A non-simulated Rigidbody2D's pose FREEZES: while any carrier holds the ball
  (`simulated=false`, parented), `ball.position` stays stuck at the CATCH point — the camera's
  keeper anchor read that stale pose, so it framed the catch spot and never moved. Proof the
  codebase already knew rb caches persist: every grab hand-zeroes `linearVelocity`
- Fix: `MatchContext.BallPosition` is now **transform-aware** (transform while held, rb while
  simulated). Same stale-pose family fixed in: PlayerMovement steal ranges (TrySteal /
  TouchBlockSteal) + the Goalkeeper's tracking reads → bots/keepers now track the LIVE carrier
- Deliberately left stale: GoalLineOut's carrier-at-line rule (reviving it = a gameplay change)

**HIGH BALL (BallFlight.LaunchHighBall):** high shots (charge >0.7) + F+B lobs + bot
long/blocked-lane passes fly as untouchable arcs — rigidbody stays simulated on a straight
zero-damping line with colliders OFF, sprite copy rides the parabola over a shrinking shadow,
`BallGrabbable` false mid-flight (one gate blocks every grab/steal/save site), landing exact
(shots keep full speed + land 1.5u before the aimed goal line; passes keep a 25% roll;
overlapped swimmers collision-ignored until separated — mirrors ReleaseCollisionWindow).

---

## SESSION LOG — 2026-07-04b (all passes arc + distinct shot arc/feel; frame-accurate goals; goal hang-time + net reaction)

**TASK 1 — EVERY pass is now an arc (`BallFlight.ArcKind`):**
- `LaunchHighBall` takes an **ArcKind (Pass / Lob / Shot)** instead of a bool; each kind has its
  own peak profile, swell and shadow size (per-kind consts in BallFlight)
- **Pass** = the toned-down default (peak dist×0.055 clamped 0.18–0.5, swell 1.08, no spin,
  full pass speed) — plain B, every bot pass, and the keeper's PassOut all use it
- **Lob (F+B KEPT as a separate input):** distinguishing still makes sense — Pass is a quick
  low hop for ball movement; Lob is the slow floaty ball OVER a press (bots pick it on long
  ≥5.5u or blocked-lane passes; the keeper's forced deep outlet uses it too). Same system,
  different profile — no parallel code path
- Mid-air immunity now covers every pass: NOBODY intercepts an airborne ball; defenders contest
  the LANDING point (lane checks still shape bot pass CHOICE). Point-blank throws (<2u) stay flat
- WaterPoloBrain.Pass, PlayerMovement.ChargedPass and Goalkeeper.PassOut all route through the
  same launch + flat fallback

**TASK 2 — a shot is never confusable with a pass:**
- **Distinct curve:** `ShotArc01` — easeOutQuad rise into an EARLY peak (35% of the flight),
  easeInQuad fall (hangs near the top, then drops hard). Passes/lobs keep the symmetric 4t(1−t)
  parabola. Two separate hand-built functions, not a shared curve
- **Speed:** `ShotSpeedMult = 1.35` (code-side, human shots only — serialized maxShootPower 12
  untouched): full-charge 16.2, high ≈18.6, tap floor 10.8 — always above the 9–16 pass band.
  Bot shot speed untouched (their `ShootPower` already exceeds their 5–11 pass cap)
- **Release snap:** 0.12s raw squash(0.84)→pop(1.12)→settle scale punch on EVERY shot release
  (human/bot/keeper, arc or flat — triggered inside NoteShot + Shot-kind launches), never on
  passes. Applied outside the eased scale so it stays crisp
- **Charge bar:** pass charge = cool blue→cyan; shot charge = hot green→yellow→red, strobing
  white past 0.7 fill (doubles as the "high shot armed" cue)
- Bot shots now call `NoteShot(0.5)` — gives them the snap AND fixes a real bug: the keeper was
  judging bot shots with a STALE ShotHeight left over from a previous human high shot

**TASK 3 — frame-accurate goals (the "scored without aiming at the frame" bug):**
- AUDIT: no goal-seeking assist exists in any shot path — human shots fly the raw aim vector
  (`lastDirection`) start to finish; `HighShotLandPoint` + landing velocity stay on that exact
  line; bot `ShotAimPoint` is the bot CHOOSING a corner (aiming, not assist). The hole was in
  SCORING: `Goal.OnTriggerEnter2D` counted ANY loose-ball contact with the trigger box (~0.8u
  deep + ball radius ⇒ an effective mouth ~±1.7 vs the real ±1.5, plus front-face skims)
- FIX (ScoreManager.BallEnteredGoal): project the ball's REAL velocity onto the goal line —
  must be moving INTO the net and the crossing y must be inside the mouth (consts mirror
  GoalLineOut's 7 / 1.5). Skims, corner clips, sideways drifts and behind-goal pinballs no
  longer score; a badly-aimed hard shot now misses exactly like a badly-aimed pass

**TASK 4 — goal hang-time + net reaction (NO player celebration clips, per spec):**
- **Phase 0 HANG (`goalHangSeconds` 3.5, new serialized field):** play freezes THE INSTANT the
  ball hits the net; the ball STAYS in the net (velocity cut ×0.15 at impact, fully zeroed
  0.15s in), everyone holds position, the camera keeps its normal follow + goal shake on the
  net. Only then does the untouched original sequence run (overview → formations → 3s silent
  pause → resume). Total post-goal ≈ 7.5s — tune `goalHangSeconds` / `postGoalPauseSeconds`
  in the Inspector if it feels long
- **Net reaction:** `Goal.cs` now passes its transform; ScoreManager plays a 0.45s damped-spring
  squash/bulge on the net sprite (scale + outward nudge, originals cached/restored, survives
  interruption) + an expanding white impact ring at the ball. Chose transform-pulse + ring over
  segment physics: reads as "ball hit the net" with zero new art/objects on placeholder squares
- **Goalkeeper freeze gate (required by the hang):** Goalkeeper.FixedUpdate now returns while
  `PlayFrozen` — without it the keeper fished the dead ball out of its own net mid-celebration
  (stale keeper-hold through the restart). Side effect: keepers hold position during duel
  countdowns / penalty setup like everyone else (they used to keep tracking; cosmetic)
- MatchTimer needed NO changes: the quarter clock already pauses while frozen, and goals can't
  fire EndMatch directly (it's time-driven) — verified

**DEFERRED (documented in A12, not built):** the 1v1 keeper close-range PASS-button chip-shot
mechanic — under-specified, needs a design pass.

**Build:** `dotnet build Assembly-CSharp.csproj` → 0 errors (21 pre-existing warnings in
untouched files).
**Slot re-check:** nothing NEW to wire. Verify after the script changes: **ScoreManager** object
→ ScoreManager component still has **Ball / Player+Bot Score Text / Player+Bot Team** set and
shows the new **Goal Hang Seconds** (3.5) field; **GoalRight/GoalLeft** → Goal component still
has **Score Manager** set; **Player1–6** PlayerMovement **Ball + Aim Line** slots; **KeeperLeft/
KeeperRight** Goalkeeper **Ball** slot. Also worth re-running **Tools → Fix Goal Colliders** once
so the trigger boxes match the visual mouth (the new gate makes scoring exact even if they don't).

---

## SESSION LOG — 2026-07-04c (visible pass arc floor, every-shot arc, in-net bob, touch LOB button)

Builds directly on 2026-07-04b. Four polish tasks, no new systems — tuning + one new touch button.

**TASK 1 — the default Pass arc is now unmistakable, and NO pass is ever flat:**
- The Pass ArcKind already existed but its peak (`dist×0.055` clamped **0.18–0.5u**) read as flat at
  short range. Raised the floor: **`PassPeakMin` 0.18 → 0.4u**, `PassPeakMax` 0.5 → 0.6u,
  `PassPeakPerUnit` 0.055 → 0.08 (so mid-range passes visibly scale above the floor before capping).
  Net: every pass now hops a clearly-visible ~0.4u minimum with a real shadow, still well under the
  Lob (0.45–1.25u), still scaling with distance/charge.
- **Removed the `<2u` point-blank flat exception entirely.** `MinHighBallDistance` 2.0 → **0.05u**
  (now only a degenerate zero-distance 0/0 throw is refused). Every pass — human B, bot, keeper
  PassOut, however short/weak — arcs.
- Added **`MinFlightTime` 0.32s** for Pass/Lob only: a very short pass is SLOWED (speed = dist/time,
  still lands exactly at landPos) so its arc stays airborne long enough to read instead of being a
  2-frame blip. Shots keep full pace (unaffected).
- **F+B Lob unchanged** — now purely an optional "bigger lob" on top of an already-good Pass, not a
  requirement to get any arc.

**TASK 2 — every shot gets the asymmetric shot arc (the flat quick-tap is gone):**
- `PlayerMovement.Shoot`: dropped the `shotHeight > 0.7f` gate — **every** non-skip shot now routes
  through `LaunchHighBall(..., ArcKind.Shot)` at any charge. Only a skip shot (deliberate LOW
  bounce, Q+Space) or a degenerate launch takes the flat path.
- Shot arc peak now scales with **charge as well as distance**: `dist×0.10 × Lerp(ShotChargeMinScale
  0.3, 1, height01)`, clamped **0.2–0.9u**. A barely-tapped shot still visibly hops (floored 0.2u,
  never a straight line); a full charge arcs high. `ShotPeakMin` lowered 0.35 → 0.2 so weak charges
  have room to scale down toward (but never reaching) zero.
- **Shot-vs-pass distinction preserved:** shots still use the asymmetric `ShotArc01` curve (steep
  rise → early peak → hang → sharp drop), still ×1.35 faster (`ShotSpeedMult`), still fire the
  release snap + glow + full-speed landing; passes stay symmetric, slower, no snap, roll-landing.
- Keeper manual shot (`KeeperShoot`) and bot shots (`WaterPoloBrain.Shoot`) intentionally left flat
  — distinct manual/AI mechanics, out of this task's scope.

**TASK 3 — the netted ball floats instead of freezing solid during the goal hang:**
- `ScoreManager.ResumeAfterGoal` Phase 0: after the 0.15s settle-and-zero, the remaining hang runs a
  new **`BallNetBob`** coroutine — a gentle buoyancy float around where the ball settled (vertical
  bob `NetBobAmpY` 0.07u + tinier sway `NetBobAmpX` 0.035u at `NetBobRate` 2.6 rad/s, random phase,
  amplitude easing 1.25→0.85 as it settles). Reads as "resting in the net in water," not a paused
  screenshot.
- Safe by construction: play is frozen, the ball's velocity is zero and BallFlight runs no flight,
  so driving `ball.position` directly is uncontested; the frame-accuracy goal gate ignores it
  (velocity 0 ⇒ no re-score). Everything else about the hang (duration, player freeze, camera hold,
  net squash) is byte-for-byte unchanged.

**TASK 4 — touch LOB button (mobile had no F-equivalent):**
- New attack-only **LOB toggle** in `TouchControls`, left of the PASS button (`LobPos` -785,160, same
  bottom-right-anchored cluster, own full-stretch group + "LOB" caption). Tap to ARM ("next pass =
  lob", button brightens), tap PASS to throw it; it auto-disarms after that pass (one-shot, matching
  how holding F behaves). Hidden + disarmed in defense and while controlling the keeper.
- Wiring: `PlayerMovement.SetLobModifier(bool)` sets a new `touchLobHeld`, merged into `ChargedPass`'s
  `lob` with `Input.GetKey(KeyCode.F)`. TouchControls (exec order −100) sets it the same frame the
  pass releases, so the lob applies, then disarms. Optional `Resources/Sprites/lob` art is used if
  present, else it reuses the pass icon.

**Before/after (Task 1 floor, the explicit numbers asked for):** Pass minimum peak **0.18u → 0.4u**
(≈2.2× taller — a modest short pass now clears roughly one ball-diameter, with a visible shadow);
`PassPeakMax` 0.5→0.6u, `PassPeakPerUnit` 0.055→0.08. Shot floor **0.35u → 0.2u** (so weak charges
scale down), full-charge shots still peak up to 0.9u.

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (21 pre-existing warnings, untouched files).
**Slot re-check (nothing NEW to wire — all runtime/procedural):** verify the usual slots survived the
full-script replaces — **ScoreManager** → Ball / Player+Bot Score Text / Player+Bot Team (+ Goal Hang
Seconds 3.5); **Player1–6** PlayerMovement **Ball + Aim Line**; **KeeperLeft/Right** Goalkeeper **Ball**;
**GameManager** MatchContext **Ball / Player Team / Bot Team**. The LOB button, its icon and the net-bob
are all built/driven in code — no Inspector fields to set. (Optional: drop a `Resources/Sprites/lob.png`
to give the LOB button dedicated art instead of the reused pass icon.)

---

## SESSION LOG — 2026-07-05 (reliable exclusion re-entry + position-aware net reaction)

Two targeted fixes, no new systems. Only `ExclusionManager.cs` and `ScoreManager.cs` changed.

**TASK 1 — excluded players now re-enter reliably (was "sometimes stuck outside play"):**
- ROOT CAUSE: the re-entry only nulled the roster slot back in and left the player's BODY dumped
  in the goal corner (`PlaceAtCorner` → |x| = 7, which is PAST the `playerLimitX` 6.9 clamp),
  relying entirely on the AI brain to swim it all the way back across the pool. Combined with a
  countdown that ran off an absolute `Time.time` deadline — which keeps advancing during soft
  `MatchContext.PlayFrozen` freezes (goal restart / penalty / sprint duel) — an exclusion could be
  wholly "served" and silently restored DURING a goal celebration the player couldn't be seen
  re-entering from, leaving it marooned in the corner.
- FIX (all in `ExclusionManager`):
  - `Exclusion.endTime` (absolute) → **`remaining`** (seconds of LIVE play left). `Update()` now
    only decrements it when **not** `PlayFrozen` (hard `timeScale = 0` pauses already zero
    `deltaTime`), so an exclusion is a true `exclusionSeconds` of gameplay and never bleeds away
    during a stoppage. HUD reads `remaining` directly.
  - New **`ReturnToPlay(e)`**: restores the roster slot (with a **`SnapshotIndex`** fallback to the
    `Start()` roster snapshot so a stale/out-of-range index can NEVER silently drop a player out of
    the roster for good), clears the excluded flag, then **actively teleports the player onto a live
    goal-side `TeamSide.DefendSpot`** (ClampToField-bounded) with zero velocity — it re-enters IN
    position instead of swimming back from behind its own goal.
- **Behaviour change to note:** a returning player now appears at a sensible defensive spot rather
  than swimming in from the corner (water-polo-purists' corner re-entry is traded for reliability,
  which is what was asked). Exclusion duration now excludes freeze time (feels slightly longer in
  wall-clock across a goal, but is a correct 5 s of actual play).

**TASK 2 — net reaction now reacts at the ACTUAL impact point:**
- `ScoreManager.BallEnteredGoal` reuses the frame-accuracy gate's projected line-crossing (`yAtLine`)
  as the true impact point `(netSign·GoalLineX, yAtLine)`, then **`NormalizedImpact`** maps it to a
  0..1 coordinate inside the goal **Collider2D's real `bounds`** (x left→right, y bottom→top) — read
  live from the collider, so NO pixel/world size is baked in and it survives a goal art/collider swap
  (falls back to a centred 0.5,0.5 hit if the goal has no collider).
- `PlayNetReaction` / `NetPulse` are now driven by that impact:
  - **Vertical lean:** the net is nudged toward the struck height (`iy · 0.10·goalHeight · bulge`) —
    a **top-corner** goal kicks the net **up-and-out**, a **bottom-corner** goal **down-and-out**, a
    **centre** goal **straight out**. The throw is a fraction of the goal's REAL height, not a baked
    distance.
  - **Corner intensity:** `intensity = 1 + 0.6·|iy|` scales the outward stretch + x-nudge, and a
    corner hit folds a little MORE on the y-squash (`0.10 + 0.06·|iy|`) — cornered goals visibly
    punch harder than dead-centre ones.
  - **Ripple** spawns at the exact crossing point (`impactWorld`) instead of the ball's current pose.

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (21 pre-existing warnings, untouched files).
**Slot re-check (nothing NEW to wire — both changes are pure code):** verify the usual slots survived
the full-script replaces — **ScoreManager** → Ball / Player+Bot Score Text / Player+Bot Team / Goal
Hang Seconds; **ExclusionManager** → Match Timer / Exclusion Text; **GoalRight/GoalLeft** must each
still have a **Box Collider 2D** (the net reaction reads its bounds — run **Tools → Fix Goal Colliders**
if unsure) + their Goal **Score Manager** ref.

**How to test — TASK 1 (re-entry):** Play. Provoke exclusions (spam Space/Block steals on a carrier —
2 failed fouls in 10 s → 5 s exclusion), watch the offender sit in its goal corner, and confirm at the
end of the HUD countdown it snaps back onto a defensive spot and immediately rejoins play. Repeat while
a GOAL happens mid-exclusion (score right after excluding someone) — the countdown should PAUSE through
the celebration and the player should still return cleanly afterward, never stranded in the corner.
**How to test — TASK 2 (net reaction):** Score into the TOP corner (high shot aimed high) → the net
kicks up-and-out and the white ring appears high in the mouth; score into the BOTTOM corner → net kicks
down-and-out, ring low; score dead-centre → straight-out bulge. Corner goals should look punchier than
centre goals. All three should differ clearly.

---

## SESSION LOG — 2026-07-05b (pool selection screen before a match)

Adds a Pool A / Pool B choice to the pre-match flow. Two match scenes now exist: **Pool A =
`Assets/Scenes/SampleScene.unity`**, **Pool B = `Assets/Scenes/SampleScene_PoolB.unity`** (Pool B is
currently an art-duplicate — this change is purely "let the player pick which scene loads").

**Where it hooks in:** the ONLY gameplay-scene load trigger is `NavigationManager.BuildPreMatchContent`'s
**PLAY** button (was `SceneManager.LoadScene("SampleScene")` inline). That PLAY button now opens the new
pool-select overlay instead of loading directly; the scene load (and the placeholder result recording)
moved into the overlay's confirm. (`MainMenuUI` → HubScene and `QuarterBreakUI`/`PauseMenuUI` → HubScene
are menu nav, not match starts, and were left alone.)

**New in `NavigationManager.cs` (code-built UI, no prefabs, same lazy-overlay pattern as pre-match):**
- Fields: `poolSelectOverlay`, `selectedPool` (0/1), `poolCardFrames[]`, and static tables
  `PoolScenes = { "SampleScene", "SampleScene_PoolB" }`, `PoolLabels = { "POOL A", "POOL B" }`,
  `PoolAccents = { Blue, Red }`, `PoolPrefKey = "selected_pool"`.
- `OpenPoolSelect()` — lazy-builds `Overlay_POOLSELECT` via the shared `BuildScreenOverlay`, clears +
  rebuilds the sheet, preselects from `PlayerPrefs["selected_pool"]`.
- `BuildPoolSelectContent` / `BuildPoolOption` — top bar "SELECT POOL", two clickable option cards
  (accent frame + `pool-screen` thumbnail, or a plain tinted rectangle if that art is missing, + a big
  A/B letter so the two read as distinct while Pool B is still a duplicate + a label), and a green
  **START MATCH** button.
- `RefreshPoolHighlight()` — brightens the selected card, dims the other (re-run on each tap).
- `ConfirmPoolAndStart()` — persists the choice to PlayerPrefs, then runs the SAME placeholder
  `RecordPlayerResult` + `MarkCompetitionWon` the old PLAY handler did, and finally
  `SceneManager.LoadScene(PoolScenes[selectedPool])` — Pool A → SampleScene, Pool B → SampleScene_PoolB.
- Same choice is offered for EVERY match (all divisions/tournaments) for now — no per-division wiring.

**`MatchResultUI.cs`:** the post-match **PLAY AGAIN** button (also a match-scene load trigger) now
reloads the SAME pool via `LastPoolScene()` reading `PlayerPrefs["selected_pool"]` (was hardcoded
"SampleScene"), so "play again" stays on the pool you picked. Nothing else touched.

**Build Settings fix (REQUIRED — the task's premise was off):** `SampleScene_PoolB.unity` existed on
disk but was **NOT** registered in `ProjectSettings/EditorBuildSettings.asset` (only MainMenu / HubScene /
SampleScene were) — so `LoadScene("SampleScene_PoolB")` would have thrown at runtime. Added it as the
4th enabled scene (guid `bcddd9b6…`). Pool B is now loadable.

**Untouched:** `LeagueSeason.cs` and all tournament logic; everything INSIDE the match scenes.

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (21 pre-existing warnings, untouched files).

**Slot re-check (nothing NEW to wire — all runtime/procedural):** the pool-select overlay, its cards and
the letter/label are all code-built in `NavigationManager` at runtime; no Inspector fields. Just confirm
**HubScene** still has its `NavigationManager` (unchanged) and that **Build Settings** now lists all four
scenes (MainMenu, HubScene, SampleScene, SampleScene_PoolB — File → Build Settings to eyeball). Optional:
drop a real `Resources/Sprites/pool-b-preview`-style art later and point `BuildPoolOption`'s thumbnail at
it per index to visually distinguish the pools.

**How to test:** From the hub → PLAY → competition → **NEXT MATCH** → pre-match → **PLAY**. The SELECT POOL
screen appears with two cards. (1) Tap **POOL A** (left, highlights blue) → **START MATCH** → confirm
`SampleScene` loads. (2) From the result screen or a fresh pre-match, PLAY again → tap **POOL B** (right,
highlights red) → **START MATCH** → confirm `SampleScene_PoolB` loads. (3) Tap the back arrow on SELECT
POOL → returns to the pre-match screen without loading anything. (4) Pick Pool B, play, then hit **PLAY
AGAIN** on the result screen → confirm it reloads `SampleScene_PoolB` (the remembered choice).

---

## SESSION LOG — 2026-07-05c (hub dead-end cleanup: level-4 lock → Season Pass, currency [+] → Shop, gear → Settings)

Wiring-only pass — no new screens, no gameplay/tournament/pool-select changes. Touched
`NavigationManager.cs` and `ShopUI.cs`.

**TASK 1 — killed the fake "UNLOCKED AT LEVEL 4" lock on the hub Season Pass button:**
- `BuildBottomBar`'s `BtnSeasonPass` was a pre-Season-Pass placeholder: it drew a dark `LockOverlay`
  + padlock (`MakeLockSprite`) + "UNLOCKED AT LEVEL 4" text and its handler was
  `Debug.Log("Season Pass coming soon")`. No player-level system ever existed to unlock it.
- Removed the whole lock overlay/icon/label + disabled look; the button now
  `() => ShowOverlay(seasonPassOverlay)` — the SAME destination as the "SEASON ENDS IN" panel
  (`BuildSeasonTimer`). Two entry points to one screen, intentionally (per the task).
- `MakeLockSprite` is KEPT — still used by the reward-veil, `SeasonPassUI` and `LeaderboardManager`.

**TASK 2 — hub currency [+] buttons now open the Shop on the right buy tab:**
- Both `MakePlusButton`s in `BuildHubTopBar` were `Debug.Log("Store coming soon")` stubs.
- The Shop already had the exact mechanism: `ShopUI.SelectTab(int)` is public and glide-scrolls the
  horizontal shelf to a section (COINS = tab 5, GEMS = tab 6 — the shop's OWN top-bar [+] shortcuts
  use `SelectTab(5)`/`SelectTab(6)`).
- Stored the shop component in a new `shopUI` field (set in `BuildShopOverlay`) and added
  `public void OpenShopTab(int tab)` = `ShowOverlay(shopOverlay)` then `shopUI.SelectTab(tab)`.
  Gold [+] → `OpenShopTab(5)` (COINS), diamond [+] → `OpenShopTab(6)` (GEMS).

**TASK 3 — audit for other dead-end buttons/stubs. Wired the one with an obvious destination; the
rest are flagged below (see the summary for the full list). Wired:**
- **Shop settings gear** (`ShopUI.BuildTopBar`, was `Debug.Log("Shop settings coming soon")`) → new
  `public void OpenSettingsScreen()` = `ShowOverlay(settingsOverlay)`, the same (minimal) settings
  overlay the hub gear already opens. Routed via the existing `nav` reference.

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (21 pre-existing warnings, untouched files).

**Slot re-check (nothing NEW to wire — all runtime/procedural):** everything is code-built in
`NavigationManager`/`ShopUI` on the HubScene canvas; no Inspector fields added or changed. Just confirm
**HubScene** still hosts its `NavigationManager`. (`shopUI` is assigned in code at overlay-build time.)

**How to test — TASK 1:** Hub → the bottom-left **SEASON PASS** button no longer shows the padlock or
"UNLOCKED AT LEVEL 4"; tapping it opens the Season Pass screen (identical to tapping "SEASON ENDS IN"
top-right).
**How to test — TASK 2:** Hub top bar → tap the **[+]** next to the GOLD count → Shop opens scrolled to
**COIN PACKS**; close, tap the **[+]** next to the GEM count → Shop opens scrolled to **GEM PACKS**.
(Bonus: the Shop's own gear icon now opens the Settings panel instead of logging.)

**Task 3 — found but NOT wired (need a decision), for the record:**
- **MainMenu "SETTINGS"** (`MainMenuUI.cs:72`, `Debug.Log("Settings coming soon"`) — the MainMenu is a
  separate scene with no settings UI and no NavigationManager; wiring it would mean building a settings
  panel there or loading HubScene into settings (a new behaviour). Left as-is.
- **Competition "CLAIM REWARDS"** (`NavigationManager.cs`, `Debug.Log("CLAIM REWARDS (placeholder)…")`)
  — shown when the player wins a competition; there's no championship-reward payout defined (amount/tier
  is a design call), so no obvious existing destination. Left as-is.
- **Intentional COMING SOON stubs (correct as-is, not dead-ends — they show a real feedback overlay):**
  hub FRIENDS / CLUBS / INBOX / GIFTS, RANKING's ELITE/WORLD/FRIENDS/COUNTRY tabs, TeamScreen's
  FORMATIONS/etc., Shop DRAFT TICKETS + EVENT sections. All need real backends and honestly say so.

---

## SESSION LOG — 2026-07-05d (tighter Group Stage standings layout)

Pure spacing/sizing pass on the competition **Group Stage** cards (`NavigationManager.BuildGroupCard` /
`BuildGroupStageTab` / `MakeGroupRowStrip`). No logic touched — group standings, knockout, VIEW ALL
expand, the gold MY-TEAM row highlight and tap-to-expand all behave exactly as before.

**What changed (constants only):**
- `headerH` **46 → 40** (less space around the GROUP A/B + VIEW ALL header bar).
- `rowH` **34/30 → 28/26** (expanded / collapsed) — shorter rows, less padding around each row's text.
- `colHeadH` (expanded column-header strip) **28 → 22**.
- Card bottom padding **+12 → +8**.
- Inter-card gap (Group A → Group B) **+16 → +10** in `BuildGroupStageTab`.
- Row-strip inset in `MakeGroupRowStrip` **rowH−4 → rowH−3** (3px inter-row gap, a touch less dead space
  while rows stay visually separated).

**Net:** a collapsed 5-row card drops ~208px → ~178px tall; an expanded 8-row card ~358px → ~294px, so
both groups fit far more compactly with the same fonts/columns/colours. All text boxes still clear the
shorter rows (compact 16pt in a 26px row, full 17pt in 28px, col-header 14pt in 22px, GROUP name 20pt in
the 40px header).

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (21 pre-existing warnings, untouched files).

**Slot re-check:** none — all runtime/procedural in `NavigationManager`, no Inspector fields.

**How to test:** Hub → PLAY → a competition in the **GROUP STAGE** phase → the Standings screen opens on
the GROUP STAGE tab. (1) Both **GROUP A** and **GROUP B** cards should read noticeably tighter — shorter
rows, less gap around the header and between the two cards — with all 5 collapsed rows (Pos | Team | Pts)
still legible and your club's row still gold. (2) Tap a card → **VIEW ALL** expands to all 8 teams with the
full POS/TEAM/P/W/D/L/GD/PTS columns, also compact and aligned; the column header + rows fit without
overlap. (3) Tap again → **COLLAPSE** back to 5 rows. (4) Scroll the list — both cards stack with the
smaller gap and nothing clips.

---

## SESSION LOG — 2026-07-06 (SampleScene retired + PoolB-only; exclusion pen markers; crowd fans; cameraman flashes)

**1) SampleScene retired, pool-select removed (reverses 2026-07-05b).** `SampleScene_PoolB` is now the
ONLY match scene; the SELECT POOL overlay is deleted outright (dead code removed, not hidden).
- `NavigationManager.cs`: removed `poolSelectOverlay`, `PoolScenes`/`PoolLabels`/`PoolAccents`/
  `PoolPrefKey`/`selectedPool`/`poolCardFrames` and `OpenPoolSelect`/`BuildPoolSelectContent`/
  `BuildPoolOption`/`RefreshPoolHighlight`/`ConfirmPoolAndStart`. New `public const string MatchScene =
  "SampleScene_PoolB"` + `StartMatch()` (records the placeholder result, loads MatchScene); the pre-match
  **PLAY** button calls it directly again.
- `MatchResultUI.cs`: PLAY AGAIN → `NavigationManager.MatchScene` (the `LastPoolScene()` /
  `PlayerPrefs["selected_pool"]` reader is deleted; the old pref key is simply orphaned).
- `ProjectSettings/EditorBuildSettings.asset`: **SampleScene removed from Build Settings** (edited
  directly — verify in File → Build Settings: MainMenu, HubScene, SampleScene_PoolB only).
- `Assets/Editor/AnimatorBuilder.cs`: its "open SampleScene" warning now names SampleScene_PoolB.
- `SampleScene.unity` still exists on disk untouched — nothing loads it. (`ProjectSettings.asset`
  `templateDefaultScene` still points at it; editor-only, harmless.)

**2) Exclusion pen markers (ExclusionManager).** Two empty marker objects added to SampleScene_PoolB:
**`ExclusionSpot_Home` at (−7.2, −4.1)** and **`ExclusionSpot_Away` at (7.2, −4.1)** (bottom pool
corners — NUDGE THEM onto the exclusion-pen art). `ExclusionManager` gained serialized
`exclusionSpotHome/Away` Transform slots that auto-find those names at Start and SELF-HEAL (create at
the same defaults + warn) if a scene lacks them. An excluded player is now parked AT its team's pen
(replacing the hardcoded (±7, −4) corner) and re-enters FROM the pen, clamped just inside live water
(x ±6.4 / y ±3.9) so the 2026-07-05 softlock fix can't regress; the pen is matched to a team by which
half it sits in (x sign vs the team's CURRENT defend goal), so halftime SwapEnds stays correct.

**3) CrowdSpawner.cs (new, Assets/).** At Start, finds every GameObject tagged **FanSeat** and spawns
one fan sprite per seat (random pick from `[SerializeField] Sprite[] fanVariants` — the art in
`Assets/Sprites/Pool/fans/` is NOT under Resources, so it must be Inspector-wired), scaled to
`fanWorldHeight` (0.6u), sorted just above the seat's SpriteRenderer, random flipX, each with a nested
`FanIdle` sine bob (±0.035u) + sway (±2.5°) at random speed/phase (PackCardFX/PoolLineFloat pattern).
Empty `fanVariants` or zero tagged seats → a clear console warning, no spawn, no crash. A
**CrowdSpawner object was added to SampleScene_PoolB** (scene YAML + pre-made script .meta GUID) with
an EMPTY fanVariants array. ⚠️ The **FanSeat tag is registered but applied to NOTHING yet** — fans
will not appear until seats are tagged (see the manual steps in the session summary).

**4) CameramanFX.cs (new, Assets/).** Self-bootstrapping (StaminaSystem pattern — no scene object, no
wiring): on every scene load finds all GameObjects tagged **Cameraman** and runs an independent flash
loop per cameraman — every 4–10s (re-rolled each flash) a small procedural white radial glow pops at
the sprite's upper-centre: 0.04s fade-in, 0.18s fade-out with a slight expand. Purely cosmetic. The
**Cameraman tag did NOT exist** (despite being believed applied): it was registered in
`TagManager.asset` and applied via scene-YAML edit to the 5 cameraman objects in SampleScene_PoolB
(cameraman1_0, cameraman2_0, cameraman3_0, cameraman3_0 (1), cameraman4_0).

**Project tags now:** `Ball` (the ball object, read by CameraFollow etc.), `FanSeat` (CrowdSpawner —
applied to nothing yet), `Cameraman` (CameramanFX — 5 objects in PoolB).

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (22 pre-existing warnings, untouched
files; the two new scripts were also added to the csproj so the check compiles them — Unity will
regenerate it anyway).

**Slot re-check:** Build Settings scene list (above); `ExclusionManager` object → two new optional
`Exclusion Spot Home/Away` slots (leave empty = auto-find by name); **CrowdSpawner object in
SampleScene_PoolB → drag fan1..fan8 into `Fan Variants` (REQUIRED — it warns + skips otherwise)**.
Standard reminder: ExclusionManager's existing `Match Timer` + `Exclusion Text` slots should still be
filled (script was replaced).

**How to test:** see the 2026-07-06 session summary (per-task steps: hub PLAY flow + PLAY AGAIN both
land in PoolB with no SELECT POOL; exclusion parks at / re-enters from the markers; fans spawn on
tagged seats after wiring sprites; cameramen flash every 4–10s).

---

## SESSION LOG — 2026-07-06b (crowd pass 2: multi-fan benches, true seat alignment, breathing idle)

`CrowdSpawner.cs` reworked (nothing else touched — CameramanFX / ExclusionManager / gameplay intact).
Context discovered first: the 10 FanSeat-tagged objects (bench1–bench10, tagged + sprite-wired by the
dev since 2026-07-06) render **bench.jpg — a 7-row × ~19-seat grandstand block** (~6.7×3.3u at their
0.6 scale), and the fan art is a **full seated figure** whose seat-contact point (butt) sits ~⅓ up the
sprite — both facts drive the new placement math.

**1) Multi-fan distribution.** One fan per bench pivot → `fansPerBench` (new int field, default 4)
fans spread evenly across the bench SpriteRenderer's rendered `bounds` width (even slots, each fan
jittered ±15% of its slot in x / ±6% of a row pitch in y — organic, still reads as one row). Benches
with no SpriteRenderer or ~zero bounds log a warning and are skipped, never crash.

**2) Seat alignment derived from BOTH sprites' bounds (no fixed offsets).** Fans floated because they
were centred on the bench pivot. Now: the bench art paints `rowsInBenchArt` (7) seat rows, so one
row's pitch = bounds.height / rows; the seat-surface line is `seatSurface01` (0.48 = middle row) of
the bounds height; and the fan's `fanSeatAnchor01` (0.34) butt-point — not its feet — is what lands
on that line (pivot-agnostic: computed via sprite.bounds.min.y), so the legs hang over the bench
front under the fan's +1 sorting order like a real seated person. Rescale a bench or swap its art →
everything recomputes.

**3) Proportional scale + breathing idle.** The flat `fanWorldHeight` (0.6u) is GONE; fan height =
`fanHeightInRows` (1.5) × one row pitch (≈0.71u on the current benches — the visible figure ends up
≈0.29u wide vs the art's ≈0.35u seats), capped by slot width so a high fansPerBench packs tighter
instead of overlapping. FanIdle: bob 0.035u fixed → **1.5% of the fan's own height** (≈0.011u), sway
2.5° → **0.6°**, speed 0.6–0.9Hz → **0.25–0.45Hz** (resting-breath rate). Reads as a living twitch,
not a hover.

**Scene:** the CrowdSpawner component's stale serialized `fanWorldHeight`/`sortingOrderFallback`
values were replaced in SampleScene_PoolB.unity with the new defaults; the 8 wired fanVariants
sprites were preserved untouched.

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (22 pre-existing warnings).

---

## SESSION LOG — 2026-07-20b (BallDropRipple invisibility root cause + generic ripple removal)

Investigation began by rereading the complete master plan and complete current `BallFlight.cs` before
touching code. This was traced from the live `SampleScene_PoolB` YAML and a project-wide search of
every ripple/splash renderer owner, rather than treating the previous implementation as proven.

**Observed root causes:**

1. **The supplied sprite sheet was being made effectively invisible by inherited Ball scale.** The
   live Ball transform is authored at **0.04 × 0.04**, while every `BallDropRipple.png` slice is
   176 pixels at 100 PPU (**1.76 world units** at scale 1). The previous `BuildSettleRipple()` made
   `BallDropRipple` a child of Ball, so its actual world size was only **1.76 × 0.04 = 0.0704u**.
   That is why no visible sheet ripple appeared despite a valid frame set: it was rendered as a
   tiny 7%-of-a-unit sprite under the ball.
2. **The unrelated "weird symbols" came from the active legacy generic water-effect owner.**
   `WaterEffectsSystem` was serialized directly on the live Ball and constructed five runtime
   ParticleSystem GameObjects: `Water Ripples (Pooled)`, `Water Foam (Pooled)`, `Water Bubbles
   (Pooled)`, `Water Splashes (Pooled)`, and `Water Side Displacement (Pooled)`. Its procedural
   ring/soft placeholder textures were still emitted by ball, swimmer, skip/landing, and goalkeeper
   paths, so they could render independently of the new sheet. This was a real parallel renderer,
   not a disabled leftover.

**Changes:**

- `BallDropRipple` is now a world-space renderer at **1 × 1** scale instead of a Ball child, placed
  at the settle contact point and drawn at sorting order **2** (the live Ball is order **1**).
- The old `WaterEffectsSystem` script, its Ball scene component, all five generic particle-system
  construction paths, and every Player/Bot/Goalkeeper/BallFlight call site were removed. A complete
  post-removal source search leaves only `ScoreManager`'s goal-only `NetRippleRoutine`, which runs
  solely after a confirmed goal and cannot render for a loose ball settling on open water.
- Added one-shot `[BallDropRipple VERIFY]` logs. On the first real settle they print the actual
  Resource frame-set, frame-1/frame-9 sprite and texture names, trigger speed/position, renderer
  enabled state, world scale, and sorting layer/order. A missing frame set produces an explicit
  error instead of silently falling back to a generic impact effect.

**Verification status:** the root-cause values above are observed from the live scene and active
renderer inventory. An isolated batch-mode Unity Play probe was also attempted, but Unity's copied
project failed while compiling package source (`ShaderGraph` missing `GUID` type) before it could
enter Play mode; no false runtime-trigger result is recorded. The in-project runtime logs remain
the required final confirmation: make one fast loose ball settle and look for the first
`[BallDropRipple VERIFY] TRIGGER` line. Do not call the visual verification complete until that line
reports the expected `BallDropRipple_8` / `BallDropRipple.png`, enabled renderer, `worldScale=(1,1,1)`,
and sorting order 2.

---

## SESSION LOG — 2026-07-20c (BallDropRipple animation disabled)

Per direct dev feedback, the BallDropRipple two-phase settle animation is disabled in
`BallFlight.cs` (`settleRippleEnabled = false`). `BallFlight` no longer creates the ripple renderer,
loads the frame sheet, or evaluates the settle-ripple trigger. The supplied `BallDropRipple.png`
and its frame-set asset are retained untouched for a future art pass, but no loose-ball settling
ripple can render now. The generic `WaterEffectsSystem` removal from 2026-07-20b remains in effect.

---

## SESSION LOG — 2026-07-20d (field-player sprite animation disabled)

Per direct dev request, the old field-player sprite/flipbook animation presentation is disabled for
both sides. `PlayerAnimator` and `BotAnimator` now default `playerSpriteAnimationsEnabled` to
**false**: no runtime flipbook body is created, no old `Assets/Sprites/Players/Parts` animation frame
is played, and legacy Animator controllers are disabled for the human field players. Each swimmer
keeps one static default scene sprite visible so the match does not lose player bodies entirely.

This is presentation-only. Keyboard and mobile action-button input, movement, shooting, passing,
stealing, possession, AI, and all button press feedback remain unchanged. The Part PNGs are retained
on disk (not destructively deleted) for a future art decision; they are simply no longer animated at
runtime. Re-enable either component's **Player Sprite Animations Enabled** Inspector checkbox only
if this visual system is deliberately wanted again.

---

## SESSION LOG — 2026-07-20e (field-player animation disable reverted)

The 2026-07-20d field-player animation disable was immediately reverted on direct dev feedback.
`PlayerAnimator` and `BotAnimator` again run their existing flipbook and legacy presentation paths
exactly as before; no static-only override or new Inspector toggle remains. PC/mobile controls were
never changed. The Parts/flipbook assets remain enabled and working.

**Slot re-check (CrowdSpawner object in SampleScene_PoolB):** `Fan Variants` should still show the 8
sprites (verify — the script was replaced!); new tunables `Fans Per Bench` 4, `Rows In Bench Art` 7,
`Fan Height In Rows` 1.5, `Seat Surface 01` 0.48, `Fan Seat Anchor 01` 0.34. To seat fans on a
different row: seat-surface ≈ (rowIndexFromBottom + 0.35) / 7.

**How to test:** enter a match → each tagged bench shows 4 fans in one line across its width, slightly
uneven spacing, sized so a fan roughly fills one painted seat; fans' bottoms sit ON a seat row (legs
over the bench front), not floating above/below; watch 10s — movement is a subtle breath, neighbours
out of sync. Bump `Fans Per Bench` to 12 → tighter row, no overlaps. Un-tag one bench → that bench
empty, no errors; temporarily tag an empty GameObject → console warns "no SpriteRenderer with usable
bounds", play continues.

---

## SESSION LOG — 2026-07-07 (crowd bug hunt: side-array "wrong sprites" was a misdiagnosis; back-fan size = inconsistent art + a tuning knob)

Two reported bugs in the three-stand `CrowdSpawner` (front/back/side) from 2026-07-06/07.
Investigation only for BUG 1 (no fix needed); one additive code field for BUG 2. **Only
`CrowdSpawner.cs` changed** — no scene YAML, no import settings, no other files.

**BUG 1 — "wrong sprites in `fanVariantsSide` (Element 6 = `fan9_0`, Element 7 = `fam8left_0`)" was a MISDIAGNOSIS. Nothing was wired wrong.**
- On disk `Assets/Sprites/Pool/fans/side/` holds EXACTLY 8 correctly-named files, `fanSideL1.png`
  … `fanSideL8.png`, no typos. A project-wide search found **no** `fan9`, `fam8`, `fan8left`
  file anywhere — the names the Inspector showed do not exist as assets.
- Root cause: **every** fan PNG (front, back AND side) imports as **`spriteMode: 2` (Multiple)**
  with a single auto-generated sub-sprite, and the Inspector shows that sub-sprite's *internal
  name*, not the file name. Those names are inconsistent leftovers from the source art:
  `fanSideL1..L6` → `fan1left_0..fan6left_0`, but **`fanSideL7` → `fan9_0`** and **`fanSideL8`
  → `fam8left_0`** (a stray index and a `fam`/`fan` typo baked into the meta's sprite name).
  So Element 6/7 correctly reference `fanSideL7.png`/`fanSideL8.png` — only their display
  labels look wrong. (The array is even in the right order, L1→L8.)
- **Content verified by eye:** opened `fanSideL7.png` (yellow shirt) and `fanSideL8.png` (blue
  shirt) — both are correct left-profile seated fans, identical framing/orientation to the
  known-good `fanSideL1.png`. Not the "old back-facing test sprite" that was feared.
- **No rewire, no code change.** Optional tidy-up (NOT done, would break the current drag refs
  → user re-drags): reimport those PNGs as Sprite Mode = Single, or rename the sub-sprites in
  the Sprite Editor, so the Inspector shows sane names.
- **⚠️ Discovered while looking: the `fanVariantsBack` / `fanVariantsSide` wiring is NOT saved to
  `SampleScene_PoolB.unity`.** The `CrowdSpawner` MonoBehaviour on disk still serializes only
  the OLD field set (`fanVariants` + pre-rework `rowsInBenchArt/fanHeightInRows/seatSurface01/
  fanSeatAnchor01`, plus the hand-edited `fansPerBench: 70`) — no `fanVariantsBack`,
  `fanVariantsSide`, `columnsInBenchArt`, `seatLineInRow01`. The back/side arrays exist only in
  the live (unsaved) Unity session, which is why the back fans were visible in-game but absent
  from the file. **Action required: Ctrl+S the scene** so the wiring + new fields persist.

**BUG 2 — inconsistent apparent size across `fanVariantsBack` fans is an ART problem, not a code problem. Root cause CONFIRMED with measurements:**
- CrowdSpawner scales every fan so its sub-sprite RECT maps to one fixed world height
  (`fanHeightInRows × rowPitch`), i.e. it normalizes on `sprite.bounds.size.y`. That is only
  uniform if each PNG frames its character at the same scale/aspect. The **front** stand does
  (all 8 are 1254² canvases, rects ~540×~1130, aspect ~0.48 → uniform, which is why front
  looked right). The **back** stand does NOT:
  - Canvas sizes are mixed: `fanBack1` 1254², `fanBack3` 1024², the other six 500².
  - `fanBack1` is a **legless upper-body crop** (character bbox 843×864, aspect ~0.98) → renders
    as an oversized broad torso.
  - `fanBack3` is a full cross-legged figure **wrapped in a glow/aura** that inflates its alpha
    bounds (bbox 669×648 inside a 939-tall rect, fill ~0.69) → the person renders too small AND
    carries a visible halo.
  - The six 500² fans (`fanBack2,4,5,6,7,8`) are a fairly consistent seated cluster (fill
    ~0.72–0.83, aspect ~0.69–0.92).
  A single array-wide factor can't reconcile a legless crop, an aura'd figure and clean seated
  figures — the differences are per-sprite pose/framing, not a uniform offset.
- **Fix shipped (additive, zero behavior change by default):** new `[SerializeField] float[]
  backScaleOverride` — an optional per-element size multiplier for the **back stand only**
  (same index as `fanVariantsBack`: element 0 = fanBack1 … element 7 = fanBack8). Empty, or any
  element ≤ 0, means 1. Applied after the width cap and before the seat anchor so a tuned value
  visibly resizes the fan and it still sits correctly. Front/side pass `null` (untouched).
- **Real recommendation: regenerate the two outliers** `fanBack1` (frame the FULL seated figure
  like the others, not a legless crop) and `fanBack3` (drop the glow aura; a 500²-ish clean
  canvas) to match the six-fan cluster. The override is the stop-gap if regenerating isn't
  practical. Measured starting values (equalising fill to the ~0.77 cluster median; expect to
  tune by eye): fanBack1 ≈ 0.75, fanBack3 ≈ 1.15, fanBack7 ≈ 1.05, the rest ≈ 0.95–1.0. These
  correct HEIGHT only — they can't fix fanBack1's missing legs or fanBack3's halo.
- **Side stand has a milder version of the same issue** (`fanSideL1/L2/L7` are 1024² with
  ~900–930 rects vs the ~1150 cluster) — left alone this session (BUG 2 was scoped to back). Say
  the word and I'll add a matching `sideScaleOverride`.

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (22 pre-existing warnings, untouched files).

**Slot re-check (CrowdSpawner object in SampleScene_PoolB):** the script was edited, so confirm
`Fan Variants` still holds the 8 front sprites and `Fan Variants Back` / `Fan Variants Side` still
hold their 8 each. A new **`Back Scale Override`** array appears (leave EMPTY = every back fan at
size 1; fill 8 elements only when tuning). **Then Ctrl+S the scene** — per BUG 1 above, the
back/side arrays and the renamed fields are not yet on disk.

**How to test:** enter a match. BUG 2 knob — set `Back Scale Override` size 8, put `fanBack1` = 0.75
and `fanBack3` = 1.15 (rest 1), Play → those two back fans should read closer in size to the seated
cluster (fanBack1 no longer a giant torso; fanBack3 less tiny, though its halo remains until the art
is regenerated). Set the array back to empty → identical to before (default all-1). BUG 1 — the side
stand's fans are correct; if their Inspector names still read `fan9_0`/`fam8left_0`, that's cosmetic.

---

## SESSION LOG — 2026-07-07b (side fans faced OUTWARD: art-direction assumption was backwards; per-stand fan-height fields)

Two changes to `CrowdSpawner.cs` only — nothing else touched.

**BUG — side-stand fans didn't face the pool. ROOT CAUSE = a backwards art-direction assumption,
NOT a comparison bug.** Investigated before touching code:
- **Art direction confirmed by eye:** opened `fanSideL1/L3/L7/L8.png` — every side fan is a
  profile figure that **faces RIGHT** when rendered unflipped. The 2026-07-06b code (and its
  comment) assumed the side art was **LEFT-facing** ("mirrors the left-facing art to face right").
  That single wrong assumption inverted the whole flip.
- **The comparison was NOT the problem — it correctly distinguished the two benches.** Measured
  from the scene: `PoolWater` centre ≈ **x +0.53** (transform x 0.53, has a SpriteRenderer so
  `PoolCenterX()` resolves), `bench5` (left side stand) at **x −11.35**, `bench9` (right side
  stand) at **x +11.87** — they straddle the pool centre cleanly. So `world.center.x < PoolCenterX()`
  gave bench5 = true, bench9 = false (two different values, per-bench, never overwritten).
- **Net effect of the old code:** art faces right, bench5 got flipX=**true** → faced LEFT (outward,
  pool is to its right); bench9 got flipX=**false** → faced RIGHT (outward, pool is to its left).
  Both stands faced AWAY from the water (opposite directions, both wrong).
- **FIX (one operator):** `bool mirror = faceThePool && world.center.x > PoolCenterX();` (was `<`) +
  the comment rewritten to state the art faces right. Now bench5 (left, x<centre) → flipX=false →
  faces right/inward; bench9 (right, x>centre) → flipX=true → faces left/inward. Both look at the pool.
- **Debug-log step:** the requested temporary `Debug.Log` of (bench name, center.x, poolCenterX,
  flipX) was NOT left in the shipped code (kept clean). Diagnosis was done statically from the
  actual art + scene coordinates, which is conclusive here. Drop-in snippet for the dev to confirm
  in Play mode is in this session's chat summary; predicted output: `bench5 center.x=-11.35
  poolCenterX=0.53 flipX=false`, `bench9 center.x=11.87 poolCenterX=0.53 flipX=true` (post-fix).

**SEPARATE — back fans read too small; added PER-STAND base height (so it isn't 8 manual overrides).**
- Replaced the single shared `fanHeightInRows` (1.5) with three fields: **`frontFanHeightInRows` 1.5**,
  **`backFanHeightInRows` 1.75** (bumped ~17%), **`sideFanHeightInRows` 1.5**. Front/side behaviour is
  unchanged (same 1.5); only the back stand grows. Threaded per-tag through `SpawnCrowd`/`SpawnBenchFans`
  as `heightInRows`.
- The 2026-07-07 `backScaleOverride` array is UNTOUCHED and still layers on top per-sprite (for the
  `fanBack1`/`fanBack3` art outliers). So back sizing is now: `backFanHeightInRows` (whole stand) ×
  `backScaleOverride[i]` (per sprite, default 1).

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (22 pre-existing warnings, untouched files).

**Slot re-check (CrowdSpawner object in SampleScene_PoolB) — the script was replaced:**
- Confirm `Fan Variants` (8 front), `Fan Variants Back` (8), `Fan Variants Side` (8) survived, and
  `Back Scale Override` is still whatever you set (empty by default).
- The old single **`Fan Height In Rows`** field is GONE; three new fields appear —
  **`Front Fan Height In Rows` = 1.5**, **`Back Fan Height In Rows` = 1.75**, **`Side Fan Height In Rows`
  = 1.5**. They take these code defaults automatically (the scene never stored the new names). If the
  back fans end up too big/small, tune `Back Fan Height In Rows` here.
- ⚠️ Same standing note as 2026-07-07: the `Fan Variants Back`/`Side` arrays are STILL not saved to
  `SampleScene_PoolB.unity` on disk (the component there still serialises only the old field set) —
  **Ctrl+S the scene** so the wiring + new fields persist.

**How to test:** enter a match. (1) FACING — the LEFT side stand's fans now look RIGHT (toward the
water) and the RIGHT side stand's fans look LEFT (toward the water); the two stands mirror each other
instead of both facing outward. (2) SIZE — back-stand fans are ~17% taller than before; adjust `Back
Fan Height In Rows` up/down to taste, with `Back Scale Override` still available for the fanBack1/3
outliers.

---

## SESSION LOG — 2026-07-07c (crowd polish: per-stand seat line, back position-override, taller back fans, breathing idle)

Four tuning/authoring changes to `CrowdSpawner.cs` only — nothing else touched. All additive or
Inspector-tunable; front sizing/position untouched.

**TASK 1 — per-stand seat line.** The single `seatLineInRow01` (0.35) is split into
**`frontSeatLineInRow01` 0.35 / `backSeatLineInRow01` 0.35 / `sideSeatLineInRow01` 0.42**, threaded
per-tag like the height fields. Front/back keep 0.35 (unchanged); side is raised to 0.42 so side fans
seat deeper into their row band instead of floating near its edge. Starting point — tune in Inspector.

**TASK 2 — per-fan position nudge for the back stand.** New **`Vector2[] backOffsetOverride`**,
index-matched to `Fan Variants Back` (element 0 = fanBack1 … 7 = fanBack8), default all (0,0) = no
change. Applied as an extra local nudge after normal grid placement: `pos += (off.x, off.y) × the
fan's render scale` on screen X/Y (fans are world-upright), so a value stays proportional if the back
height is retuned. Same optional/layered pattern as `backScaleOverride`. Intended use: set element 3
(fanBack4) to something like (0, -0.15) after eyeballing it in-game.

**TASK 3 — back fans taller.** `backFanHeightInRows` default **1.75 → 1.95** (back art frames the
figure small; still a starting point, Inspector-tunable). `backScaleOverride` + `backOffsetOverride`
still layer per-sprite on top.

**TASK 4 — FanIdle reads as breathing, not floating.** The old idle was a position-only Y sine bob +
sway. Now one breath cycle drives BOTH the vertical bob (kept, 1.5% of height) AND a **Y-scale pulse
(±1.5% of base Y scale)** — the torso lengthening/shortening reads as a chest rising/falling instead of
levitation. **Desync investigation (as asked):** the per-fan phase was ALREADY fully random (0..2π), so
phase was never the sync culprit — the crowd read as one collective pulse because every fan did the
identical position-only motion in a narrow **0.25-0.45 Hz** band. Fix diversifies the *character*: adds
the scale breath, widens the rate band to **~0.18-0.55 Hz**, and gives the sway its **own** random phase
(was a fixed +1.3 offset). Neighbours now visibly breathe out of step. Amplitudes stay deliberately
subtle (a living twitch, not a bounce).

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (22 pre-existing warnings, untouched files).

**New / changed Inspector fields (CrowdSpawner object in SampleScene_PoolB) — the script was replaced, re-check:**
- **`Back Fan Height In Rows`** default **1.95** (was 1.75). `Front`/`Side Fan Height In Rows` unchanged at **1.5**.
- **`Front Seat Line In Row 01`** = **0.35**, **`Back Seat Line In Row 01`** = **0.35**, **`Side Seat Line
  In Row 01`** = **0.42** (these replace the old single `Seat Line In Row 01`, which is GONE).
- **`Back Offset Override`** = new `Vector2[]`, **empty by default** (all (0,0)). Fill 8 elements only when
  nudging a specific back fan (e.g. element 3 = fanBack4).
- Unchanged and should survive: `Fan Variants` / `Fan Variants Back` / `Fan Variants Side` (8 each),
  `Back Scale Override`, `Fans Per Bench` 70, `Rows/Columns In Bench Art` 7/20, `Fan Seat Anchor 01` 0.34.
- All new fields take these code defaults automatically (the scene never serialized the new names).
- ⚠️ Same standing note: the `Fan Variants Back`/`Side` arrays are STILL not saved to
  `SampleScene_PoolB.unity` on disk — **Ctrl+S the scene** so the wiring + all the new fields persist.

**How to test:** enter a match. (1) Side fans should sit a touch lower/deeper in their seats (tune
`Side Seat Line In Row 01`). (2) Back fans are noticeably taller (~1.95). (3) Watch the crowd ~10s — it
should read as many people breathing (subtle chest rise/fall, out of sync), not a synchronized float; if
one back fan (e.g. fanBack4) sits wrong, set its `Back Offset Override` element to nudge it. (4) With
`Back Offset Override` empty and side seat line back at 0.35, behaviour matches the previous session.

---

## SESSION LOG — 2026-07-08 (goal-celebration poses; breathing idle take 2; back-size measurement; two serialization gotchas surfaced)

Big session — one new feature + three fixes, all in `CrowdSpawner.cs`, plus one safe scene-YAML
float edit. Also surfaced two important truths about scene state (below).

**⚠️ SCENE-STATE TRUTHS found while investigating (read these — they explain a lot):**
- The dev DID finally save the scene since 2026-07-07c, so `SampleScene_PoolB.unity` now serializes
  the back/side arrays + all the per-stand fields. Good.
- BUT the scene serializes **`backFanHeightInRows: 1.75`, NOT the 1.95** I set as the code default in
  2026-07-07c — the [[serialization gotcha]]: the field was already materialised at 1.75 (from
  2026-07-07b) in the dev's session, so the 1.95 code default never applied, and the save wrote 1.75.
  **Last session's height bump effectively never happened** — that's a big part of why back fans "still
  read too small." To actually get 1.95 (or bigger), set **`Back Fan Height In Rows`** in the Inspector.
- The back-fan PNGs are **byte-identical to last session** — `fanBack1` was NOT regenerated (still the
  legless upper-body crop) and `fanBack3` NOT regenerated (still the glow/aura figure). `backScaleOverride`
  was still `[]`. So the "already-fixed fanBack1/fanBack3" issue was never actually fixed in the art.

**TASK 1 — GOAL CELEBRATION POSES (new feature).** Three new `Sprite[]` arrays —
`fanVariantsFrontCele` / `fanVariantsBackCele` / `fanVariantsSideCele` (empty; dev drags in the 24
`*Cele1..8` sprites) — plus `celebrateSeconds` (3.5). Every spawned fan is registered per-stand with
its seated sprite + its SAME-INDEX cheer sprite. Goal detection is a **poll of
`ScoreManager.Instance.HomeScore + AwayScore`** each frame (rises = a goal) — chosen over editing
ScoreManager so the scoring/goal code is untouched and everything stays inside CrowdSpawner. On a goal,
**each stand independently** flips ~50% of ITS OWN fans (per-fan coin flip) to the same-index cheer
sprite (identity/colour preserved, only the SpriteRenderer's sprite swaps — flipX/scale/position kept),
then reverts after `celebrateSeconds` (unscaled time, so the goal-hang freeze can't strand a pose).
**Overlapping goals = CLEAN RESTART (my choice):** a second goal seats everyone still cheering, re-picks
a fresh ~half of each stand, and resets the timer — a flurry of goals can never leave fans stuck or
double-swapped. A stand whose cele array is empty simply never cheers (no crash).

**TASK 2 — breathing idle, take 2 (still floated last time).** Root of the "floating on water" read =
whole-body vertical translation. Cut it hard and leaned on the scale breath (numbers as asked):
- **`BobFraction` (position bob) 0.015 → 0.004** (= 26.7% of the old value — bottom of the "25-30%" ask).
- **`ScalePulseFrac` (Y-scale breath) 0.015 → 0.025** (up 67%, now carries the "alive" read).
The chest now visibly rises/falls in place while the body barely translates. Sway (0.6°, own random
phase) and the 0.18-0.55 Hz band are unchanged from 2026-07-07c.

**TASK 3 — back-size outliers, MEASURED (not eyeballed).** Method as before: sub-sprite rect height
(import) vs the character's alpha-bbox height → fill = charH/rectH = apparent rendered size (the spawner
normalises every fan to a constant rect height, so fill IS the on-screen size). Current 8:

| fan | canvas | rect h | char h | fill (apparent size) |
|---|---|---|---|---|
| fanBack1 | 1254² | 869 | 864 | **0.994 — LARGEST** (legless crop, huge bbox) |
| fanBack2 | 500² | 500 | 396 | 0.792 |
| fanBack3 | 1024² | 939 | 648 | **0.690 — smallest** (aura inflates bounds) |
| fanBack4 | 500² | 499 | 360 | 0.721 |
| fanBack5 | 500² | 487 | 386 | 0.793 |
| fanBack6 | 500² | 480 | 400 | 0.833 (cluster top) |
| fanBack7 | 500² | 488 | 360 | 0.738 |
| fanBack8 | 500² | 496 | 380 | 0.766 |

**The measurement CONTRADICTS the perception on fanBack1:** it is the BIGGEST by geometry, not too small —
scaling it up makes a bigger legless torso. Its "reads wrong/small" is the missing-legs framing → needs
art REGEN, not a scale bump; I left its override at 1.0. Genuinely undersized (below the ~0.78 median,
excluding the two framing outliers) are **fanBack4, fanBack7, fanBack8**; I set `backScaleOverride` to
lift them to ~the cluster top (0.833): **fanBack4 → 1.15, fanBack7 → 1.13, fanBack8 → 1.09** (measurement
flagged fanBack4 too, even though the dev listed it only for position — see Task 4). `fanBack3` left at
1.0 (scaling the aura'd figure just enlarges the halo → regen). Set in the scene YAML directly
(`backScaleOverride` is a float array — safe to hand-edit, unlike sprite object-refs).

**TASK 4 — fanBack4 position nudge: LEFT FOR THE DEV (needs eyes I don't have).** `backOffsetOverride`
stays `[]` (untouched). The dev sets it: CrowdSpawner Inspector → **`Back Offset Override`** → Size **8**
→ **Element 3** (fanBack4) → try around **(0.1, -0.1)** — POSITIVE X = right, NEGATIVE Y = down/back —
and tune by eye. (Element 3 also now has a 1.15 size override from Task 3; size and position are
independent — zero either if you disagree.)

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (22 pre-existing warnings, untouched files).

**Inspector / scene checklist (CrowdSpawner in SampleScene_PoolB — script replaced, re-check):**
- **DRAG IN 24 CELEBRATE SPRITES** (empty until you do → fans just don't cheer):
  - `Fan Variants Front Cele` ← `fanFrontCele1..8` from `Assets/Sprites/Pool/fans/front/celebrate/`
  - `Fan Variants Back Cele` ← `fanBackCele1..8` from `.../back/celebrate/`
  - `Fan Variants Side Cele` ← `fanSideLCele1..8` from `.../side/celebrate/`
  - order MUST match the base arrays (element 0 = the fanFront1/fanBack1/fanSideL1 person).
- **`Celebrate Seconds`** = 3.5 (new, code default).
- **`Back Scale Override`** now = `[1, 1, 1, 1.15, 1, 1, 1.13, 1.09]` (set in the scene this session). If
  your open Unity session shows it EMPTY, reload the scene (File → revert) OR type those 8 values in.
- **`Back Fan Height In Rows`** currently **1.75** in the scene — NOT the 1.95 intended last session
  (gotcha above). If the whole back stand still reads small, bump this in the Inspector (~1.95+).
- **`Back Offset Override`** stays empty — set Element 3 per Task 4.
- Bob-amplitude change is in code (const), nothing to wire.

**How to test:** (1) CELEBRATION — score a goal: ~half of each stand (front/back/side, independently)
pops the cheer pose for 3.5s, then sits back down; identity/colour unchanged, side fans still face the
pool. Score two goals fast → it cleanly restarts (fresh half, no stuck poses). (2) BREATHING — watch a
stand ~10s: chests rise/fall roughly in place, almost no whole-body float, out of sync. (3) SIZE —
fanBack4/7/8 read a bit bigger; fanBack1 is unchanged (it needs art regen, not scaling). (4) fanBack4
position — after you set `Back Offset Override[3]`, it shifts right + down/back.

---

## SESSION LOG — 2026-07-08b (back-size cap ROOT-CAUSED + fixed; energetic celebration anim; subtle ball-tracking tilt)

Three changes, `CrowdSpawner.cs` only. No scene edits this session.

**TASK 1 — WHY raising `Back Fan Height In Rows` did nothing, and the fix. PLAINLY: the width cap was
eating it.** The spawner sizes each fan to `heightInRows × rowPitch` tall, THEN (from the single-row era)
clamped the scale so the fan is never wider than one seat cell:
`if (rowsRunVertical) scale = Mathf.Min(scale, seatPitch / sprite.bounds.size.x)`. `rowsRunVertical` is
true for BOTH front and back (only side is false), so **the cap applied to the back stand**. The back
PNGs have ~square sub-sprite bounds (rect aspect ≈ 1.0 — measured last session) sitting in ~square seat
cells, so "no wider than one cell" ≈ "no taller than one row": the cap silently clamped every back fan
back down to ~1 row tall NO MATTER what `backFanHeightInRows` was (1.75, 1.95, anything). Front was
unaffected because front art is TALL (rect aspect ≈ 0.48) so its 1.5× height stays within the one-cell
width budget. **Fix:** the width cap is now per-stand via a new `capWidth` arg — **true only for FRONT**;
BACK and SIDE pass false (side already had no cap because its seats run vertically). Back fans now honour
`backFanHeightInRows` in full. Trade-off (documented, expected): because scale is uniform, a taller back
fan is also wider, so back fans may now overlap horizontally — reads as a *packed* stand; dial
`Back Fan Height In Rows` (or `Fans Per Bench`) down if it's too dense. Chose "extend the side exemption
to back" over a tunable cap knob, per the dev's steer + the repeated "bigger back fans" ask.

**TASK 2 — celebration is now genuinely energetic, not "idle but bigger".** `FanIdle` got a second mood.
While a fan holds its cheer pose (`celebrating`, set/cleared by CrowdSpawner's PickHalf/RevertActive) it
switches to ENERGETIC constants and reverts to calm when the window ends:
| param | calm idle | celebrating |
|---|---|---|
| Y-scale bounce | 0.025 | **0.10** |
| rotation wobble | 0.6° | **7°** |
| position hop | 0.004 | **0.03** |
| oscillation rate | ×1 | **×5** |
Reads as an excited bounce + wobble for the 3.5s, then settles. (Celebration trigger/array wiring
untouched, as instructed.)

**TASK 3 — subtle ball-tracking tilt (FRONT + BACK only; NOT side).** Each front/back fan adds a small
Z-tilt LEANING toward the ball's side, layered on top of the idle sway/breath: `dx = ballX − fanX`,
`tilt = −MaxTiltDeg · dx / sqrt(dx² + TiltRef²)` — a smooth (not hard-clamped) saturation, `MaxTiltDeg
= 6°` (dev's 5-8° range), `TiltRef = 6u` (a ball ~6u off-centre → ~0.71 of max). Reads as "the crowd
follows the game," subtle. Side fans set `trackBall = false` (they're ~90°-rotated art — different math,
out of scope). **Performance (as asked):** the ball position is read from `MatchContext.Instance.BallPosition`
**once per frame** in `CrowdSpawner.Update` and cached in a static (`sBallX`); the ~700 `FanIdle.Update`
calls each do just a subtract + one `sqrt` + a couple multiplies off that cached float — no per-fan
`MatchContext.Instance`, no allocation. Ball X source is the same transform-aware `BallPosition` the
camera/keeper use (correct while the ball is held, not just simulated).

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (22 pre-existing warnings, untouched files).

**Inspector / scene:** NO new serialized fields this session — the tuning lives in `FanIdle` consts
(celebration energy, tilt) and one internal `capWidth` arg. Nothing new to wire. (Still outstanding from
2026-07-08: drag in the 24 `*Cele` sprites for celebrations to show; `Back Scale Override` =
`[1,1,1,1.15,1,1,1.13,1.09]`; set `Back Offset Override[3]` for fanBack4.) If the celebration bounce /
tilt magnitudes want tuning, they're the `Cele*` / `MaxTiltDeg` / `TiltRef` consts in `FanIdle`.

**How to test:** (1) BACK SIZE — with `Back Fan Height In Rows` at 1.95, back fans are now visibly taller
(they were stuck ~1 row before); raise/lower it and the back stand actually responds. (2) CELEBRATION —
score: ~half of each stand bounces + wobbles energetically for 3.5s, then calms. (3) TILT — move the ball
across the pool and watch a front/back stand: fans lean slightly toward the ball's side (max ~6°); side
stands don't tilt. If the lean direction feels inverted, flip the sign on `MaxTiltDeg` in `FanIdle`.

---

## SESSION LOG — 2026-07-08c (celebration wobble dialled back)

One-line tune per dev feedback: the celebration ROTATION wobble was too much at 7°. **`FanIdle.CeleSwayDegrees`
7f → 3.5f.** Everything else confirmed-good and untouched — scale bounce (`CeleScalePulseFrac` 0.10),
position hop (`CeleBobFraction` 0.03), oscillation rate (`CeleRateMult` 5), and the front/back ball-tracking
tilt (`MaxTiltDeg` 6°) all unchanged.

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (22 pre-existing warnings, untouched files).
Nothing to wire (const in `FanIdle`).

---

## SESSION LOG — 2026-07-09 (foul visibility root causes fixed; compressed FIFA-style clocks)

Follows the 2026-07-08/09 diagnosis: fouls/exclusions were NOT broken — the trigger chain was intact
but steal attempts were structurally near-impossible in normal play. Two targeted gameplay fixes +
one new shared display utility.

**TASK 1 — Player3 steal reach fixed (scene YAML):** Player3's `PlayerMovement.stealDistance` was
serialized at **0.2** in `SampleScene_PoolB.unity` (every other player: 1.2) — its Space-steals
silently whiffed by range. Now **1.2**, matching the rest. (Latent quirk, predated the "fouls
missing" report; found during the diagnosis.)

**TASK 2 — `pressDistance` 1.8 → 1.0 (the structural fix):** an AI carrier passes the moment any
enemy is within `TeamSide.pressDistance` (`WaterPoloAI.Carry`, "pressured"). At 1.8u that is FARTHER
than every steal reach (bots `grabDistance` 1.0, players `stealDistance` 1.2), so a carrier dumped
the ball **before a legal steal attempt was even possible** — failed steals are the ONLY foul source,
so fouls/exclusions were starved regardless of `stealChance`. **Chose 1.0**: exactly AT the bots'
steal reach and inside the players' 1.2, so pressers enter legal steal range at the same moment the
carrier starts reacting — steals become possible, carriers still pass under real pressure (that
branch is unchanged). Changed in BOTH places (the serialized-default gotcha): `TeamSide.cs` default
AND the two serialized `pressDistance` values in `SampleScene_PoolB.unity` (PlayerTeam + BotTeam).
Side note: `pressDistance` also feeds `TeamSide.ShotQuality`'s pressure term, so AI shooters now
count themselves "pressured" only inside 1.0u — slightly more willing to shoot with a defender at
1.0–1.8u; watch in testing. NOT touched (confirmed correct in diagnosis): `stealChance`,
`foulWindowSeconds`, `foulsForExclusion`, `maxExclusionsPerPlayer`, escalation/penalty logic.
(Retired `SampleScene.unity` still carries the old 1.8/0.2 values — nothing loads it.)

**TASK 3 — compressed clocks (`CompressedTimer.cs`, NEW in `Assets/`):** one shared struct — the
HUD counts down `displayDuration` while only `realDuration` real seconds pass. Gameplay keys off the
REAL scale (`Tick`/`RealRemaining`/`IsComplete`); only printed text uses `DisplayValue`/`DisplayElapsed`.
All three timers now run through it (no hand-rolled scale factors):
- **Quarter clock (`MatchTimer`):** displays **8:00 → 0:00 over 90 real seconds** (real quarter
  length UNCHANGED — new `displayQuarterLength` 480). `RemainingSeconds()` (bot late-lead defense)
  stays REAL. `MatchTimeStamp()` (event-feed stamps) now uses the DISPLAY scale so feed times agree
  with the on-screen clock.
- **Shot clock (`ShotClock`):** displays **30 → 0 over 15 real seconds** (new `shotClockRealSeconds`
  15; `shotClockSeconds` 30 is now explicitly the displayed number). Real possession length is
  therefore HALVED, as specified. Turnover-at-zero logic untouched; `warningThreshold` 5 is
  display-scale (red for the last 2.5 real seconds).
- **Exclusion timer (`ExclusionManager`):** displays **20 → 0 over 7.5 real seconds** of live play
  (new `exclusionDisplaySeconds` 20 / `exclusionRealSeconds` 7.5 — task's 7–8s range). ⚠️ **Premise
  correction:** the exclusion was NOT "20s real" before — it was `exclusionSeconds` **5s** (code +
  scene). Real sit-out time therefore got LONGER, 5 → 7.5s (a stronger man-up, and easier to actually
  see). The old `exclusionSeconds` field is deleted; the scene's orphaned `exclusionSeconds: 5` line
  is ignored by Unity and will drop on the next scene save. Freeze-pausing (2026-07-05 fix) preserved:
  the timer still only ticks during live play.

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (22 pre-existing warnings, untouched files).

**Slot re-check (scripts edited in place, but per the standing rule):** GameManager → **MatchTimer**
(Score Manager / Timer / Quarter / Result Text), **ShotClock** (Match Timer / Shot Clock Text),
**ExclusionManager** (Match Timer / Exclusion Text) slots should all still be filled. New Inspector
fields appear with correct code defaults (nothing to wire): MatchTimer **Display Quarter Length 480**,
ShotClock **Shot Clock Real Seconds 15**, ExclusionManager **Exclusion Display Seconds 20 / Exclusion
Real Seconds 7.5**. ⚠️ Serialized-default gotcha: once the scene is saved in Unity these bake into the
YAML — future re-tuning must happen in the Inspector/scene, not just in code defaults.

**How to test:** (1) FOULS — play normally; when a bot presser reaches you (or your presser reaches a
bot carrier) steal attempts now actually roll: expect "Foul - free throw YOU/BOT" feed lines within a
possession or two, and an exclusion (player parked at the pen, HUD "EXC: 20.0" counting fast) when the
same offender fouls twice in 10s. Player3 Space-steals now work like everyone else's. (2) QUARTER
CLOCK — shows 8:00 and drains to 0:00 in 90 real seconds (~5.3 displayed seconds per real second);
quarter/halftime flow unchanged. (3) SHOT CLOCK — shows 30, hits 0 (turnover) after 15 real seconds
of possession; red at displayed 5. (4) EXCLUSION — HUD counts 20 → 0 while the player sits out 7.5
real seconds of LIVE play (still pauses through goal celebrations). (5) Event-feed timestamps march
in step with the big clock.

---

## SESSION LOG — 2026-07-09c (steal whiff cue; loose-ball settle splash; escaped-ball keeper restart)

Three independent additions. Touched: `PlayerMovement.cs`, `BallFlight.cs`, `BallOutOfBounds.cs`.
Untouched by explicit instruction: the shot clock's pause-during-loose-ball design, `stealDistance`,
`pressDistance`, all foul/exclusion values.

**TASK 1 — whiff feedback on silent steal exits (`PlayerMovement`).** Space (and the touch BLOCK)
used to exit with zero feedback on the three "legal press, no attempt" gates — a dead-feeling key
(diagnosed 2026-07-09b). Each now plays a cheap cue via a new `StealWhiff(bool playAnimation)`:
- **Out of range** (carrier beyond `stealDistance` / `BlockStealRange`) → the snatch ANIMATION (a
  lunge at open water) + a small water-swipe puff.
- **Wrong side** (failed the ~70° front-facing gate; the snatch anim already fires before that gate)
  → puff only.
- **Cooldown** (0.6s between attempts / 1.5s post-foul lockout) → puff only, no anim spam.
The puff = a short-lived world-space sprite (the ball's own circle drawn white) expanding 0.15→0.45
and fading over 0.25s at the hand (`lastDirection` × 0.45u), sorted just above the player, throttled
to one per 0.15s so mashing can't stack them. Purely visual — no gameplay branch changed, input never
consumed/blocked, and the always-loud outcomes (real roll → steal or foul) are untouched. The other
silent exits (ball loose / mid-arc / free throw / protected keeper) intentionally stay silent — those
are "there is nothing to steal" states, not whiffs.

**TASK 2 — settle splash for a loose floating ball (`BallFlight`).** New `UpdateSettleRipples()` +
`RippleWave(pos, delay, maxScale, alpha, seconds)` (a parameterised sibling of the skip-shot ripple):
when a LOOSE, simulated ball that was recently moving fast (armed at >2.5 u/s) decelerates below
1 u/s with nobody having collected it, **three staggered expanding rings** radiate once from the
contact point — (0s, 0.9 scale, 0.6 alpha), (0.18s, 0.6, 0.45), (0.36s, 0.38, 0.3): first wave
largest, each successive one smaller/fainter. Latched (`settleArmed`): fires ONCE per settle, never
re-triggers while the ball sits there, re-arms only after it moves fast again. Suppressed while held,
mid-arc (it settles at the landing instead), during a pre-bounce skip shot, and while `PlayFrozen`
(no splash from the ball dying in the net during goal hang-time).

**TASK 3 — escaped-ball recovery → keeper restart (`BallOutOfBounds`).** The dev-reported "a very
hard shot leaves the pool and just disappears": nothing owned a ball fully OUTSIDE the walls (the
y-rule stops at the wall face, `GoalLineOut` owns the goal lines, and a violent ball can jump past
both between physics steps). Extended the existing out-rule owner — no parallel system:
- **Detection (FixedUpdate, checked BEFORE the wall rule):** a LOOSE ball at |x| > 8.2 or |y| > 4.7
  (beyond the ±8 / ±4.5 walls; both serialized) = escaped. Skipped while a `HighBallActive` arc flies
  (its rigidbody line is always in-pool and its landing is clamped).
- **Sequence (`RecoverEscapedBall` coroutine):** physics OFF; possession is claimed for the awarded
  team (the team that did NOT touch it last — same ruling as the wall rule) which fences the parked
  ball off from every other rule (they all early-out on a non-loose ball) and from all grabs; feed
  line "Out - keeper ball YOU/BOT"; **two decaying deck hops** (~0.6s total, sliding ~0.6u further
  out, capped near the pool so the clamped camera still shows it); a **0.8s dead-ball pause**; then
  the ball is dropped 0.5u in FRONT of the awarded team's keeper, physics back ON, possession
  cleared, and the ENEMY team grab-banned — the keeper's own save/collect logic picks it up within
  its normal release-cooldown beat and distributes exactly as it always does (bot: auto pass-out;
  your team: full keeper control). The grab ban lifts automatically when the keeper takes possession
  (`SetPossession` clears it). Shot clock reset on the award.
- **Abort-safety:** every phase (incl. mid-hop) checks `Claimed()` — ball re-parented, re-simulated,
  or `PlayFrozen` (quarter break → sprint duel pins the ball with physics off) → the sequence stands
  down instantly and defers to the claiming system.
- Keeper lookup = x-sign of the awarded team's `defendGoal` vs each `Goalkeeper`'s half (the same
  rule `Goalkeeper.KeeperTeam` uses), so it survives the halftime `SwapEnds`.

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (22 pre-existing warnings; the new
code adds none).

**Slot re-check (scripts edited in place; per the standing rule):** GameManager → **BallOutOfBounds**
has NEW Inspector fields (Escape X/Y Threshold 8.2/4.7, Settle Seconds 0.6, Throw In Delay 0.8 — code
defaults apply, nothing to wire) and its existing Out Y Threshold 4.2 / Reentry Inset 0.5 should still
read those values. `PlayerMovement`/`BallFlight` gained no serialized fields. Usual check that
Player1–6 → Player Movement still has **Ball + Aim Line** wired.

**⚠️ NEEDS DEV VERIFICATION (Task 3 — the keeper handoff rides existing AI logic):**
1. Force a ball out (easiest: from your own half, aim a full-charge HIGH shot at the top wall corner,
   or nudge the Ball's position past x=8.3 in the Inspector during Play). Confirm: feed shows
   "Out - keeper ball …", the ball hops/settles on the deck, pauses, then appears at the correct
   (defending) keeper and is collected within ~1s.
2. Confirm the BOT keeper then passes out normally (its `keeperHoldSeconds` 0.8 auto-distribute), and
   — separately — that when YOUR keeper is awarded, you get normal player-keeper control (Task 5 flow).
3. Watch the shot clock across the restart: reset on the award, keeps ticking through the keeper hold
   (keeper hold ≠ possession change — unchanged design).
4. Edge: let a quarter expire while the ball is parked on the deck — the quarter break/sprint duel
   must take the ball cleanly (the recovery aborts itself; nothing should teleport mid-duel).
5. Cosmetic: during the ~2s recovery the enemy presser may swim to the wall nearest the dead ball
   (it reads "possession = awarded team, no carrier" and presses the spot) — looks like fetching the
   ball; flag if it reads wrong.

---

## SESSION LOG — 2026-07-09d (REGRESSION FIX for 09c: goal-spam guards; whiff/splash visuals de-ball-ified)

Regression report after 09c: (1) spurious repeated "Goal - BOT" feed lines + an "oversized/duplicate
ball sprite stuck in the goal net" (screenshot), (2) Space while defending "triggers a goal-score
animation", (3) no fouls at all any more. Investigated, root-caused, fixed. Touched:
`PlayerMovement.cs`, `BallFlight.cs`, `BallOutOfBounds.cs`, `ScoreManager.cs`. Nothing new added.

**Investigation answers (as asked):**
- The whiff puff IS a fully separate cosmetic object — `new GameObject` with only a SpriteRenderer,
  untagged, NO collider, NO rigidbody; it never references, moves or clones the real Ball object.
  `Goal.OnTriggerEnter2D` requires a collider AND the "Ball" tag, so the puff/ripples physically
  CANNOT fire the goal trigger.
- `TrySteal`'s control flow is intact: the 09c whiffs only replaced three previously-SILENT
  `return`s (cooldown / out-of-range / wrong-facing). The traced path for Space against an in-range,
  correctly-facing carrier still reaches `lastStealTime = …` → the `stealChance` roll →
  `ExclusionManager.ReportFoul` on a miss, unchanged. Bots' `TryStealAI` was never touched.

**What actually broke, plainly:**
1. **The 09c visuals were ball look-alikes.** Both the whiff puff and the settle ripples drew the
   ball's OWN sprite in white at fixed world scales (puff to 0.45, ripples to 0.9) — the ball itself
   is authored at ~0.1 scale, so they rendered as **4.5x–9x "giant white balls"**, visually identical
   to ScoreManager's goal impact ring (`NetRipple`: same sprite, white, ~1.1). Symptom 2 = the puff
   (Space on defense) being read as the goal animation; the screenshot's "oversized duplicate ball in
   the net" = settle ripples firing where a saved/slowed shot dies at the goal mouth. **Fix:** both
   effects are now sub-ball-scale (puff 0.08→0.22, waves 0.3/0.2/0.13), fainter (alpha 0.35 /
   0.4-0.2), tinted **pale water-cyan** (never ball-white), and settle ripples are suppressed
   entirely within 6u of a goal line (`SettleMaxX`) — nothing splashy ever renders in the net again.
2. **The 09c escape rule could misfire on parked balls.** `BallOutOfBounds.FixedUpdate` read
   `ctx.Ball.position` — the **stale rigidbody pose** ([[waterpolo-rb-frozen-pose]]) — with **no
   `PlayFrozen` and no `simulated` guard**. Any physics-off loose-ball state (sprint-duel pin, goal
   hang-time/restart, its own recovery) leaves rb.position frozen at wherever play last was; if that
   stale pose sat past a threshold the rule fired repeatedly mid-freeze — claiming possession during
   restarts, spamming the feed, and fighting the restart systems for the ball (the "no real play
   between events" chaos). **Fix:** both rules now bail while `PlayFrozen`, bail when the ball is
   not `simulated` (physics-off = some system is managing it), and read the transform-aware
   `ctx.BallPosition`.
3. **`ScoreManager.BallEnteredGoal` had NO re-entrancy guard.** During the ~7.5s goal restart the
   ball is parked loose IN the net (bobbing), reset, handed out — and nothing stopped the goal
   trigger from scoring AGAIN mid-restart. **Fix:** a `restartInProgress` latch — set the moment a
   goal counts, cleared when Phase 4 actually resumes play; `BallEnteredGoal` early-outs while set.
   One goal per restart, guaranteed, whatever re-enters the trigger.
4. **"No fouls at all" was a consequence, not a separate break:** with the game spending most of its
   time frozen inside stacked goal restarts (7.5s each), live play — the only place steal rolls
   happen — barely existed. The steal/foul path itself is verified unchanged (see above).

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors, 22 warnings** (exact pre-existing
baseline — the 2 warnings 09c had added are also gone).

**Slot re-check:** no new serialized fields this session (consts only); the usual GameManager
(ScoreManager refs, BallOutOfBounds thresholds from 09c) and Player1–6 slots should be untouched —
in-place edits, not full replaces.

**How to test:** (1) Play a full quarter: goals only appear in the feed for real shots, one per
restart, never stacked seconds apart; nothing large/white ever sits in a net. (2) Mash Space on
defense: a small faint CYAN swipe puff at the hand — clearly not the goal ring; out-of-range presses
also lunge. (3) Fouls are back: press a carrier from the front → "Foul - free throw" lines,
exclusions on repeat offenders (the 2026-07-09 tuning — pressDistance 1.0 — is what makes them
possible; it was never the problem). (4) A pass/shot dying in OPEN water still gives the 3 small
fading rings; a ball dying at the goal mouth gives none. (5) Quarter break with a loose ball: no
"Out - keeper ball" spam during the duel (the stale-pose misfire is gone).

---

## SESSION LOG — 2026-07-09f (EMPIRICAL steal diagnosis from a real playtest log; foul visibility overhaul; whiff removed; exclusion pen verified + re-entry fixed)

Method change after two wrong static-trace sessions: temporary `[STEAL-DBG]` Debug.Logs were added
at every TrySteal/TryStealAI gate + roll + ReportFoul, the dev played ~60s attempting steals, and
the diagnosis below comes from the **676 captured log lines** (read from Editor.log), not from code
reading. All debug logs are removed again. Touched: `PlayerMovement.cs`, `WaterPoloAI.cs`,
`ExclusionManager.cs`, `MatchContext.cs`, `Goalkeeper.cs`, `SampleScene_PoolB.unity`.

**WHAT THE LOG ACTUALLY SHOWED (diagnosis):**
1. **The foul→exclusion chain WORKS end-to-end.** The playtest produced ~10 `ReportFoul`s, several
   successful steals, and TWO escalations (both by the dev's own team — the "YOU EXC" screenshot was
   the dev's own player serving one). "Zero fouls/exclusions" was a rate + visibility problem, never
   a broken trigger chain.
2. **Human Space-steals DID roll — 6 times** (5 miss → foul, 1 success). The "only the puff happens"
   perception had three real causes: (a) genuine in-close presses measured **1.33–1.75u to the ball**
   (it sits at the carrier's far-side hand) vs `stealDistance` **1.2** → OUT OF RANGE whiffs; (b) many
   presses landed while the ball was mid-flight between bots (possession=none — correctly nothing to
   steal); (c) the scene's serialized `stealChance` **0.2** (code default 0.4 — the gotcha) made 4 of
   5 rolls a self-foul, so "nothing visible" was often a silent-looking foul.
3. **The REAL starvation bug (bots + AI teammates): scene `grabDistance` 0.5.** Dozens of
   "presser close but OUT OF REACH: dist=1.0x > reach=0.50" lines. The 2026-07-09 fix set
   `pressDistance` 1.0 against an ASSUMED steal reach of 1.0 — but the scene serialized **0.5** on
   all 12 TeammateAI/BotMovement components (code default 1.2). Pressers therefore parked at 1.0
   and never legally reached the carrier; the only bot steals in the log came vs a sprinting
   (loose-hold) carrier, where reach doubles to exactly 1.0. **The serialized-default gotcha, third
   strike.**

**FIXES (this session):**
- **Scene tuning (YAML edited directly):** `grabDistance` 0.5 → **1** (×12 AI agents — restores the
  09 design: press range == steal reach); `stealDistance` 1.2 → **1.5** (×6 players + code default;
  matches the touch-BLOCK reach 1.5 and the 1.5 defend-anim proximity); `stealChance` 0.2 → **0.4**
  (×6 players; code default was already 0.4).
- **(Item 1) Exclusion pen sides — VERIFIED, NOT SWAPPED, unchanged.** Evidence: bots pressed/stole
  from KeeperLeft → bots attack GoalLeft; scene YAML: Player `defendGoal`=GoalLeft (1781417090),
  Bot `defendGoal`=GoalRight (773787453); markers at (−7.2,−4.1)/(7.2,−4.1). `PenFor` parks each
  team at its **defending-half** corner = real water polo's re-entry corner (own goal line). What
  made it READ mirrored: a full-opacity benched player parked exactly where the enemy man-up plays.
  Fix = presentation, not sides: **excluded players dim to 45% alpha** while benched, restored on
  return. (If the pen is WANTED on the attacking side as a design choice, it's a one-line sign flip
  in `PenFor` — deliberately not done.)
- **(Item 2) Post-exclusion reintegration.** Found a quiet regression: the 07-06 pen-marker session
  replaced the 07-05 "re-enter onto a live DefendSpot" with "pen position clamped 0.8u inside" —
  the returner re-entered basically AT the pen and the whole rejoin depended on the brain, reading
  as "sat inert, never came back" (its own header comment still promised the DefendSpot behavior).
  **Restored:** `ReturnToPlay` drops the returner onto `team.DefendSpot(...)` (pen-clamp kept only
  as the no-team fallback), clears stale AI intent (`CurrentMark`/`IsDriving`/`IsSettingScreen`),
  un-dims, zeroes velocity. Roster-slot restore + excludedNow removal unchanged (verified correct;
  no exceptions in the playtest log).
- **(Item 3) Ordinary fouls are now VISIBLE (they used to be an event-feed line only):**
  `ExclusionManager.FreeThrow` now (a) fires a **0.7s referee-whistle freeze**
  (`foulWhistleFreezeSeconds`; safe: steal rolls only happen in live play, so no other freeze owner
  can be active/started during it), (b) spawns a rising/fading world-space **"FOUL!"** TextMesh at
  the victim, and (c) starts **`foulProtectSeconds` = 5 REAL seconds** of `MatchContext`
  **foul protection**: `StartFoulProtection`/`IsFoulProtected(carrier)` — nobody may steal from the
  fouled carrier (gated in `TrySteal`, `TouchBlockSteal`, `TryStealAI`, and the keeper snatch), and
  the AI stand-off that used to end the instant the carrier moved now holds for the whole window
  (brain: `enemyFreeThrow` → `enemyShielded`, covering free throw OR protection; presser stands
  down, defenders keep `freeThrowClearance`). Protection lapses EARLY the moment the carrier
  releases the ball (it checks they still carry), so it never shields a receiver. Shot clock: paused
  during the whistle freeze + free throw as before; it RUNS during the remaining protection window
  (protected but burning clock — flag if it feels wrong).
- **(Item 4) Whiff puff REMOVED everywhere** (dev: "random ball appearing/disappearing"). The 09c/09d
  `StealWhiff`/`WhiffRoutine` + consts are deleted; cooldown and wrong-side exits are silent again;
  the out-of-range lunge keeps ONLY the pre-existing snatch animation. `System.Collections` using
  dropped from PlayerMovement (no coroutines left there).

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors, 22 warnings** (exact pre-existing baseline).

**Slot re-check (in-place edits, no full script replaces — slots should be intact):** GameManager →
ExclusionManager gets NEW Inspector fields **Foul Protect Seconds 5 / Foul Whistle Freeze Seconds
0.7** (code defaults apply, nothing to wire). Scene YAML was edited directly — in Unity, if
SampleScene_PoolB is open, use File → Open Scene to reload it (do NOT save an open stale copy over
the fix). ⚠️ Serialized-default gotcha note for the future: the live tuning values are now scene
`grabDistance 1` / `stealDistance 1.5` / `stealChance 0.4` — retune in the Inspector, not in code.

**⚠️ VERIFICATION PENDING (same 60s playtest, dev must CONFIRM before this is called done):**
1. Press a bot carrier from the front and mash Space: visible snatch lunges, and within a few
   attempts a **whistle pause + "FOUL!" popup** + feed line; defenders (and you) can't touch the
   fouled carrier for ~5s (watch the bots visibly back off).
2. Foul twice quickly with the same player → exclusion: the benched player is **dimmed** at the
   defending-corner pen, HUD counts down, and at 0 it **pops back onto the defensive line at full
   opacity and plays on** (the previous "sat inert at the pen" must not recur).
3. Bot pressers now reach YOU: expect occasional bot fouls (whistle + protection in your favor) and
   bot exclusions on repeat offenders.
4. No small cyan puffs anywhere — the only steal visual is the snatch animation.

---

## SESSION LOG — 2026-07-09g (gameplay-feel overhaul: positional catching; anti-cluster positioning retention; bot shot power; pass-outlet fallback; replay feasibility)

Large tuning/behavior session driven by MEASUREMENT of the scene's live serialized values against
code defaults (the gotcha's 4th/5th/6th strikes — see the table below) plus a full read of the
brain/positioning architecture. Touched: `WaterPoloAI.cs`, `TeamSide.cs`, `PlayerMovement.cs`,
`TeammateAI.cs`, `BotMovement.cs`, `SampleScene_PoolB.unity`. **The 2026-07-09f
foul/steal/exclusion/protection system was NOT touched** (verified: TrySteal / TouchBlockSteal /
TryStealAI / ReportFoul / pen logic all unedited; steal reach `grabDistance` 1.0 preserved).

**MEASURED SCENE-vs-CODE DIVERGENCES found this session (scene = the live truth):**
| field | code default | scene (live) | consequence |
|---|---|---|---|
| TeammateAI/BotMovement `shootPower` (×12) | 11 | **20** | bots fired 20 u/s lasers (the 2026-06-15 "calmer bots" 13→11 never reached the scene) |
| TeammateAI/BotMovement `supportSpeed` (×12) | 2.5 | **0.5 / 1** | off-ball swimmers crawl — shapes 3–6u away are unreachable between possession flips |
| PlayerMovement `maxShootPower` | 12 (docs say 12) | **30** | human full-charge = 30 × 1.35 ≈ **40 u/s** — dev-tuned, liked, UNTOUCHED |
| PlayerMovement `grabDistance` | 1.6 | **1** | code default now aligned to 1 (scene wins; no runtime change) |
| bot `chaseSpeed`/`carrySpeed` (×12) | 3 / 1.8 | **1 / 0.5–1** | dev's deliberate difficulty tuning — left alone |

**TASK 1 — positional catching (`WaterPoloBrain.CanCatchLooseBall`, shared by AI + human):**
- The catch and the steal both rode `GrabDistance` — and the 09f steal fix (0.5 → 1.0) had
  silently DOUBLED every AI catch radius too. Instead of re-splitting the serialized field
  (which would re-touch the frozen foul system), catching got its own GEOMETRY rule on top:
  - **Slow/settled ball (≤ `FastBallSpeed` 2.5 u/s):** unchanged omnidirectional pickup at the
    full grab radius (AI 1.0 / player 1.0) — floaters never stall play.
  - **Flying ball (> 2.5 u/s):** catchable only within **`FastCatchRadius` 0.6u** AND while the
    catcher roughly FACES it (**`CatchFacingDot` 0.1** ≈ ±84° cone) — nobody vacuums a pass out
    of the water at full reach as it zips past; receivers must be positioned and looking.
- Wired into BOTH catch sites: the brain's collect gate and `PlayerMovement.TryGrabBall`
  (auto-collect AND the E press — one rule, no human/AI asymmetry). The KEEPER's catch/save
  system is untouched (it already rolls saves; its own grab distance stays 1.2).
- Pass landings still work: an arc lands at 25% speed (≈1.2–2.75 u/s) and decays fast under the
  ball's 2.5 linear damping, so a positioned receiver collects within a beat; a laser passing a
  mid-lane teammate is no longer auto-caught. All three tunables are consts in `WaterPoloAI.cs`
  (promote to Inspector fields later if per-player catching skill is wanted).

**TASK 2 — anti-cluster positioning (the core of this session):**
- **ROOT CAUSE A (churn):** the instant ANY pass/shot released, `PossessingTeam` went null and
  every off-ball agent on BOTH teams flipped into the DEFENSIVE branch — the attacking team
  collapsed toward its own goal on every single pass flight, re-expanded on the catch, collapsed
  again on the next pass. No shape could ever be held; play read as one clump following the ball.
  **FIX — positioning retention:** for POSITIONING ONLY, a loose ball still "belongs" to
  `MatchContext.LastTouchTeam` (already deflection-aware via BallTouchTracker): that team keeps
  its ATTACKING shape through the flight while its CLOSEST member goes to meet the ball
  (reception), and the other team keeps DEFENDING. Every legality gate (grabs, steals, bans,
  free throws) still reads real possession — a genuine turnover flips everyone the moment the
  other team takes the ball. Bonus fixes for free: counter runners no longer stop mid-outlet-pass,
  and a shooting team now holds its shape for the rebound instead of retreating.
- **ROOT CAUSE B (mobility):** scene `supportSpeed` 0.5 (bots) / 1 (player-team AI) meant off-ball
  swimmers covered <1u per possession phase — the spread targets existed (role lanes ±3.8u wide,
  anchors 0.7/1.5 verified sane) but were mathematically unreachable. **Scene → 1.5 on all 12**
  (sprint ×1.7 beyond 2u ⇒ ~2.55 peak; still below the human, near presser parity; `chaseSpeed`/
  `carrySpeed` difficulty tuning untouched).
- **Separation:** `MinTeammateSeparation` 1.2 → **1.5** (brain const) — teammates yield earlier,
  matching the 2.0 `teammateSpacing` target-level push.
- NOT changed (verified adequate, tune in Inspector if wanted): role-lane widths
  (`wideLateralMult` 1.2 ⇒ wings ±3.78u of a ±4 clamp), formation anchors, defend shapes
  (Zone/man-down are compact BY DESIGN; Press marking spreads with the now-spread attackers).

**TASK 3 — bot shot power → parity:** scene `shootPower` **20 → 12** on all 12 agents (+ code
defaults 11 → 12 so everything agrees). Grounding: the keeper's power save-penalty is BINARY above
9 u/s (`FastShotSpeed`), so 20 vs 12 had IDENTICAL save odds — 20 only made shots unreadable and
deflections violent. 12 sits at the low end of the human's charge band (tap ≈ min-floor ×1.35 up to
40.5 full charge with the scene's `maxShootPower` 30) — bots stay dangerous (still > the 9 penalty
threshold) but no longer out-shoot the player. Player shooting untouched, per instruction.

**TASK 4 — pass-target audit + least-bad outlet:** `BestPassTarget` ALREADY scores real openness
(distance-to-nearest-defender × `passOpennessWeight` 1.5, hard `openRadius` 1.6 gate), pass-lane
risk that widens with distance, receiver shot quality, and the Centre-feed bonuses — decision
quality mostly needed Task 2's spacing so those scores have spread targets to find. ONE real gap
fixed: under pressure with NOBODY clearing the openness gate the carrier returned null and dribbled
into the press until the 1.8s force-shot. New **pressured fallback**: pick the MOST-open teammate
with a clear lane even if formally covered (the least-bad outlet a real player makes); the keeper
last-resort and null-only-when-smothered behavior stay beneath it.
**Still simplistic (documented for a future session):** pass scoring evaluates receivers' CURRENT
positions (no lead-the-swimmer passes, no anticipating a defender closing on the landing); no pass
fakes/look-offs; `ThreatScore` is distance+carrier+openness only; Zone defense doesn't shift
ball-side; drives/screens tuning untouched from their first pass.

**TASK 5 — post-goal instant replay: FEASIBILITY REPORT ONLY (not built, per the brief).**
Verdict: **moderate scope, one dedicated session — do NOT bolt on quickly.** Needs: (1) a rolling
ring-buffer recorder (~20Hz × ~6s × 16 transforms — memory trivial, a simple component); (2) a
playback mode re-posing swimmers+ball along recorded frames inside a freeze, with a camera zoom on
the goal and a skip tap; (3) careful integration into ScoreManager's 5-phase goal restart — the
EXACT machinery that regressed twice (09c/09d goal-spam, frozen-pose gotchas, `restartInProgress`
latch). Recommended v1: position-only "ghost" playback during the existing hang-time (no animator
state capture — bodies glide), inserted as an optional phase-0b. Deferred.

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors, 22 warnings** (exact pre-existing baseline).

**Inspector / scene checklist (NO new serialized fields anywhere — new tunables are consts):**
- Scene (already edited in the YAML — reload the scene, don't re-save a stale open copy):
  all 12 AI agents now `Support Speed` **1.5**, `Shoot Power` **12**. Everything else per-agent
  (chase/carry/steal/grab) untouched.
- Consts to find later if tuning is wanted: `WaterPoloAI.cs` → `FastBallSpeed` 2.5 /
  `FastCatchRadius` 0.6 / `CatchFacingDot` 0.1 / `MinTeammateSeparation` 1.5.
- Standard slot re-check after script edits (in-place, nothing should have emptied): Player1–6
  PlayerMovement **Ball + Aim Line**; PlayerTeam/BotTeam TeamSide goals + members; GameManager rows.

**How to test (dev, in one match — plus the STILL-PENDING 09f foul checklist above):**
1. **Spacing:** watch a full possession — the attacking team should HOLD a wide spread (wings near
   the sidelines, Centre inside, CB back) while ONLY the receiver moves to a pass landing; nobody
   else should drift ballward during flights. On a turnover the shapes should swap sides cleanly.
2. **Catching:** throw a hard pass PAST a teammate (aim wide) — it should zip by unless someone is
   right on the line facing it; a soft pass to a stationed teammate still sticks. Your own player
   no longer hoovers fast balls from a body-length behind/beside.
3. **Bot shots:** bot shots should read as throws you can react to, not instant lasers; bots should
   still score on good looks.
4. **Pass choice:** a pressured bot carrier should now offload to SOMEBODY (watch for the outlet
   pass under press) instead of dribbling into the crowd and force-shooting.
5. **Regression watch:** fouls/whistle/protection/exclusions must still behave per the 09f list;
   pass receptions must not stall (if a landed ball ever sits uncollected with everyone ignoring
   it, report it — the retention chase should prevent exactly that).

---

## SESSION LOG — 2026-07-12 (waterline visuals; pass assist removal; OOB recovery; foul scope/stun; goal containment; steal range; crowd inset; HUD restore)

This session was completed against the live scene and the current serialized values, with isolated Unity verification runs where practical. The final blocking scene merge error was a duplicated `PlayerScoreText` block in `Assets/Scenes/SampleScene_PoolB.unity` that produced duplicate identifiers `1468651118` and `1468651119`; I removed the duplicate block, fixed a stray patch marker, and also removed stale `SessionVerification*` project entries that were still pointing at deleted temp harness files.

**TASK 1 — lane lines read as partially submerged**
- Result: lane-line visuals now blend into the water with a subtle submerged look instead of sitting flat on top.
- Root cause: purely visual. The existing line sprites were rendered with no waterline treatment, so they read as floating.
- Verification: checked in an isolated Unity pass that the lines still render and the added refracted layer stays translucent.
- Live manual check still recommended: confirm the effect reads softly in motion, not muddy or over-distorted.

**TASK 2 — pass auto-aim/assist removed**
- Result: pass direction now follows the aimed direction exactly, with no teammate snapping, auto-correction, or homing toward a receiver.
- Root cause: pass logic was still using assist-style target selection/spread behavior, which biased even careless input toward a teammate.
- Verification: isolated Unity test showed the ball traveling on the aimed vector with zero measurable correction toward a nearby teammate.
- Live manual check still recommended: aim at open water beside a teammate and confirm the ball stays on the exact intended line at low and full power.

**TASK 3 — out-of-bounds recovery fixed**
- Result: balls that leave the playable pool edge now trigger the existing recovery sequence instead of bouncing back into play from the wall.
- Root cause: the playable inner wall boundary was not being caught by the prior threshold logic, so the wall collider could bounce the ball before recovery engaged.
- Verification: isolated Unity test showed the ball being captured before the bounce, pausing correctly, and awarding to the proper side.
- Live manual check still recommended: hard-shot both the wall-adjacent edge and the corner edge to confirm the settle/pause/award sequence looks right.

**TASK 4 — foul protection scope narrowed**
- Result: the whistle still causes the brief universal freeze, but the longer protection window no longer freezes the whole match; other players keep moving and only the fouled matchup stays protected from steals.
- Root cause: the protection window was being treated too much like a global defensive hold, so the non-involved players backed off instead of playing on.
- Verification: isolated Unity test confirmed the short freeze ends, movement continues, and idle protection expires early if the fouled carrier does nothing.
- Live manual check still recommended: watch a real foul and make sure the rest of the field keeps repositioning while only the fouled carrier remains protected.

**TASK 5 — goal scoring smoothed and goal containment locked**
- Result: scoring now resolves cleanly without the old hitchy feel, and once the ball is truly inside the goal area it stays contained until the scoring sequence finishes.
- Root cause: after a goal the ball was still retaining enough motion to drift/rebound back out before the scoring sequence completed, and the goal-frame work was happening on the scoring frame.
- Verification: isolated Unity test measured the callback as fast enough for a clean score moment and confirmed the ball did not re-exit after entering the goal.
- Live manual check still recommended: shoot hard at both goals and confirm posts can still deflect, but a ball that is already inside the net never escapes.

**TASK 6 — steal range balanced, plus new stun on aggressive foul**
- Result: both player and AI steals now require genuinely close proximity, and aggressive foul outcomes can briefly stun the fouled player with a short disoriented state.
- Root cause: the live scene had inconsistent serialized steal values, and the player/AI steal checks were not measuring the same thing; the player was being checked against the held ball position while AI behavior felt looser.
- Verification: isolated Unity tests confirmed the close-range thresholds and the new stun drop/recovery behavior, including the visual stars state and action lockout.
- Live manual check still recommended: verify close-range steals feel symmetric in an actual match, and confirm the stun chance feels rare enough not to dominate fouls.

**TASK 7 — crowd spawner matches new bench art**
- Result: crowd placement now respects the 6x18 bench art and excludes the border margin so fans do not spawn in the wall/rail band.
- Root cause: the art changed but the spawn box still covered the full sprite, so fans could land in the unseated border area.
- Verification: isolated Unity test confirmed the measured anchors stay inside the seat area and do not violate the lower border band.
- Live manual check still recommended: flip the bench orientation and make sure the inset still tracks correctly from every side.

**TASK 8 — HUD restored**
- Result: the missing quarter/timer/scoreboard HUD elements are back in `SampleScene_PoolB`, and their manager references are rewired.
- Root cause: these objects were genuinely missing from the scene, not just disabled or mispositioned, and the scene also picked up a merge artifact that duplicated `PlayerScoreText`.
- Verification: the scene file now loads without duplicate identifiers, and the restored objects are present with their expected parent and UI wiring.
- Live manual check still recommended: open `SampleScene_PoolB` in Unity and confirm the HUD is visible, positioned correctly, and updating in play mode.

**Build**
- `dotnet build Assembly-CSharp.csproj` → `0` errors, `22` warnings.
- The warning count matches the existing baseline; there are no new compile errors after removing the temporary verification harness references.

**Manual verification priority**
1. Task 3: hard shots into the wall-adjacent boundary and corner edge should leave play and award correctly.
2. Task 5: hard goal shots should score smoothly, and balls already inside the net must not escape.
3. Task 2: pass at open water near a teammate and confirm there is no auto-aim or snap assist.
4. Task 4: foul one matchup and confirm only that matchup stays protected while the rest of the field continues moving.
5. Task 6: confirm steal range is close on both player and AI, then check the new stun is rare, short, and visually readable.
6. Task 8: verify the restored HUD is visible and wired in live play.
7. Task 7: check the new bench art inset from both bench orientations so no fan spawns in the border band.
8. Task 1: visually tune the submerged lane-line effect in motion so it reads subtle and intentional.

**Not fully live-playtest verified**
- Task 1, Task 4, Task 5, Task 6, Task 7, and Task 8 still need a human visual/gamefeel pass in the running game, even though the code and isolated Unity checks are in place.

---

## SESSION LOG — 2026-07-12b (goal-net visual scope; charge-scaled pass distance; speed-gated OOB bounce/recovery; AI possession delay)

Four ordered gameplay-polish tasks, verified in the live `SampleScene_PoolB` Play mode with measured
runtime values. Touched only `PoolLineFloat.cs`, `PlayerMovement.cs`, `BallOutOfBounds.cs`, and
`WaterPoloAI.cs`. The confirmed-good foul protection / steal range / stun systems and
`CrowdSpawner.cs` were not changed.

**TASK 1 — goal nets excluded from the submerged lane-line treatment (`PoolLineFloat.cs`).**
- **Root cause:** `GoalRight` and `GoalLeft` intentionally already had `PoolLineFloat` for their
  legacy subtle motion. The 2026-07-12 submerged setup ran unconditionally on every component, so it
  also tinted/faded each whole goal and created a `SubmergedRefraction` copy below it.
- **Fix:** submerged tint/refraction setup now runs only for actual line art (a `PoolLines` parent or
  the current `horizontal-line_*` / `vertical-line_*` names), with an explicit `Goal` component veto.
  Goal motion is preserved; goal sprite colour/opacity and geometry are never modified.
- **Play verification:** captured and visually inspected both goals. Both nets rendered fully normal;
  runtime assertions found no refraction child on either goal and one on every divider (`8/8`).

**TASK 2 — pass charge now controls real travel distance (`PlayerMovement.cs`).**
- **Root cause:** the old landing range was a narrow linear **3.5 → 6.5u**, so even a zero-charge tap
  received more than half of full-pass range before BallFlight's 25% landing roll was added. Charge
  therefore changed pace/arc much more noticeably than total distance.
- **Fix:** normal passes now use a convex charge curve (`charge^1.5`) over **1.5 → 7.0u**; lobs use
  the same curve over **2.5 → 9.0u**. Live scene pass-speed values were confirmed as 6/13 and the
  code defaults were aligned to them; direction remains exactly manual with no assist.
- **Measured Play result (including landing roll):** charge 0.0 = **2.04u**, charge 0.5 = **4.35u**,
  charge 1.0 = **8.30u**. The weak/medium/full bands are now materially distinct.

**TASK 3 — soft wall contacts stay in play; hard exits use the existing recovery (`BallOutOfBounds.cs`).**
- **Root cause A:** last session's collider-face prediction had no speed threshold, so it claimed
  every projected top/bottom contact before wall physics could respond.
- **Root cause B:** the live horizontal wall colliders have no bouncy PhysicsMaterial, so merely
  declining recovery made a soft ball stop dead at the edge instead of reflecting inward.
- **Fix:** outward normal speed must reach **12u/s** to start `RecoverEscapedBall`; lower-speed
  contacts are repositioned just inside and reflected inward with **75%** speed retention. A ball
  genuinely past the full-escape safety thresholds still uses the same existing recovery owner.
- **Measured Play result:** a **10u/s** top-edge contact reflected inward, never entered recovery,
  and stayed below y=3.67; an **18u/s** contact started recovery, switched physics off for the
  settle/pause, then completed the keeper-restart handoff with physics restored.

**TASK 4 — AI pass release has a realistic control touch (`WaterPoloAI.cs`).**
- **Root cause:** `PassSettleDelay` existed, but the pressured branch explicitly bypassed it. A newly
  receiving bot is commonly pressured, so it could release on the exact physics tick it caught the
  ball despite the nominal delay.
- **Fix:** the shared `Pass(...)` release path enforces a universal **0.30s** minimum possession age.
  Target selection, lane checks, spacing, drive decisions, and the existing 0.35s unpressured settle
  rule are unchanged.
- **Measured Play result:** the bot still held at 0ms and 150ms, then released at **0.32s**.

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors, 22 warnings**. Warning count matches the
pre-existing baseline. Temporary verification scripts/project references were removed after testing.

**Manual verification priority (dev):**
1. **Task 3:** aim a routine/tap shot at BOTH top and bottom wall-adjacent edges — it must visibly
   deflect inward with no whistle/recovery; then full-charge the same aim — it must leave, settle,
   pause, and award the correct keeper/team.
2. **Task 2:** from one stationary spot, throw low/half/full B charges into empty water. Expect roughly
   short (~2u), medium (~4.3u), and long (~8.3u) total travel; confirm the exact-aim/no-assist behavior
   from 2026-07-12 is unchanged.
3. **Task 4:** watch several bot receptions, especially under immediate pressure. The receiver should
   visibly control the ball for about 0.3s before passing, without holding long enough to invite the
   old pressure/steal problem.
4. **Task 1:** inspect GoalLeft and GoalRight while the water animates. Both complete net/frame sprites
   must stay fully opaque and stable-looking, while only the lane/divider lines retain the subtle
   submerged/refraction treatment.

---

## SESSION LOG — 2026-07-13 (moving-ball swim presentation; state-driven player depth; empirical art/overlay audit)

This was deliberately kept to a code-first visual pass because the current player art does not have
normalized bounds or a purpose-made two-hands-forward dribble/push cycle. Before changing code, the
full master plan, `PlayerAnimator.cs`, and `PlayerMovement.cs` were read. The live scene wiring, actual
PNG alpha bounds, imported sprite settings, clips, rig prefabs, and the surviving 2026-07-12 waterline
work were then inspected rather than inferred from comments or old setup notes.

**Empirical scene/art findings (important for future regeneration):**
- All six live player `PlayerAnimator` components have all 12 front/back flat and bone-body
  Animator/Renderer references populated. No player `WaterOverlayRenderer` or
  `WaterOverlayBackRenderer` fields, objects, or live scene references survive in the current tree.
  The current `SubmergedRefraction` work belongs to lane/divider lines in `PoolLineFloat`, not players.
- Imports use **100 pixels per unit** with centered pivots, but live body scales are inconsistent:
  front flat bodies are 0.07 or 0.08 depending on player, back flats are 0.07 or 0.077, front float
  rigs are 0.07, front hold rigs are **0.06**, and back float/hold rigs are 0.07. Back-hold bodies
  also have player-specific local-position nudges. This confirms the scene differs from older notes.
- Measured non-transparent PNG bounds (`alpha > 8`; width x height pixels, followed by approximate
  live world size at the common 0.07 scale unless the scene uses another scale):
  - front float `test.png`: **787x740** -> about **0.551x0.518**;
  - back float `test-back.png`: **789x758** -> about **0.552x0.531**;
  - front hold `hold.png`: **1049x892** -> about **0.629x0.535 at its live 0.06 scale**;
  - back/side hold `back-side.png`: **918x808** -> about **0.643x0.566**;
  - front swim `swiml.png` / `swimr.png`: **1064x869 / 1111x860** -> about
    **0.745x0.608 / 0.778x0.602**;
  - back swim `swim-backl.png` / `swim-backr.png`: both **936x683** -> about **0.655x0.478**;
  - front throw charge/release: about **1209x951 / 1190x948** -> about
    **0.846x0.666 / 0.833x0.664**;
  - back throw charge/release: about **982x786 / 970x780** -> about
    **0.687x0.550 / 0.679x0.546**.
  Players whose front flat body is 0.08 render those flat frames about 14% larger again.
- The flat swimming clips are only two sprite frames (keys at 0.00s and 0.12s; clip length about
  0.203s). The front frames depict alternating single-arm reach, not the requested real-water-polo
  both-hands-forward ball push. `back-side.png`, despite being used as the back hold body, reads as a
  side-profile one-arm-raised pose. The current `PlayerAnimator` facing split is also left-vs-right
  (`velocity.x < -0.1` selects the so-called back bodies), not the vertical split described by stale
  comments/offset names.

**Overlay decision (flagged before implementation):**
- One shared fixed-size player water overlay is **not defensible with the current art**. Across float,
  hold, swim, and throw, visible widths/heights, baselines, scales, and even view/proportions differ by
  roughly 15-35% (more for the 0.08 front bodies). An overlay tuned for float would clip or sit at the
  wrong level over hold/swim/throw.
- A polished overlay version therefore needs either (a) regenerated art normalized to the same canvas,
  pivot, baseline/waterline, view, and scale, or (b) state- and facing-specific mask/overlay dimensions
  and offsets. Option (b) is viable code work, but it should wait until the source art direction is
  settled so those masks are not immediately discarded.
- This pass uses state-specific **visual-body local Y offsets** instead. It creates the requested lower
  carry / higher release read without moving gameplay roots or colliders and without pretending a bad
  shared cover fits every body.

**TASK 1 — swimming while carrying, with automatic forward ball push (`PlayerAnimator.cs`,
`PlayerMovement.cs`):**
- While possession is true and Rigidbody2D speed is above **0.1**, the static front/back bone hold
  bodies are hidden and the existing flat two-frame swim controller is reused. The controller receives
  `IsHolding=false` only for this visual moving-carrier branch, so gameplay possession is unchanged.
- When the carrier stops, the existing bone-rigged static hold body returns exactly as before.
- During moving possession the held ball is pinned **0.58 world units** ahead along the swimmer's
  actual travel vector. When stationary it still uses the existing scene-tuned hand offsets.
  `lastDirection`, charge, pass target, shot target, power, and all pass/shoot mechanics are untouched.
- This is functional with current art, but it cannot show a convincing two-hands-forward push because
  those frames do not exist. New matched front/back push-swim frames are the art priority for that read.

**TASK 2 — state-driven visual depth (`PlayerAnimator.cs`):**
- Every visual body variant is eased to the same state offset while preserving its serialized base
  position and player-specific nudges: holding target **-0.04 local Y**, throwing target
  **+0.06 local Y**, return target 0, transition speed **0.8 local units/second**.
- Only child visual transforms move. Player roots, Rigidbody2D positions, colliders, possession, ball
  flight, and aiming stay unchanged.
- `PlayerMovement.Shoot()` now explicitly signals the presentation layer. Both flat facing controllers
  receive the shoot trigger, the raised depth lasts **0.22s** to match the approximately 0.203s throw
  clips, and the flat body temporarily wins over the idle bone rig so stationary shots are visible.
  This replaces the unreliable old heuristic that judged a shot from the swimmer's own release speed
  and therefore missed ordinary stationary/slow shots.

**Files changed:**
- `Assets/PlayerAnimator.cs`
- `Assets/PlayerMovement.cs`
- `WATERPOLO_MASTER_PLAN.md` (this log only)

No scene or controller asset was changed, and no new serialized object references were introduced.
Existing six-player wiring remains intact; the new numeric fields use their code defaults until tuned
per player in the Inspector.

**Build:** `dotnet build Assembly-CSharp.csproj` -> **0 errors, 22 warnings**. The warnings are the
existing obsolete-API/unused-field baseline; this work adds no compile error.

**Manual Unity verification/tuning priority:**
1. Carry in every direction, especially diagonal: the static raised-ball pose must swap to the two-frame
   swim and the ball must remain ahead of travel; stopping must restore the original hold pose.
2. Aim independently while moving, then pass/shoot: the visual ball position may follow travel, but
   the release direction must still follow the existing aim exactly.
3. Shoot both stationary and moving from both flat-facing variants: the throw must remain visible for
   its full short clip and the body should rise rather than swap immediately to the float rig.
4. Tune `movingHoldBallForwardOffset` (0.58), `holdingSubmergeOffsetY` (-0.04),
   `shootingRiseOffsetY` (+0.06), and `depthTransitionSpeed` (0.8) by eye against the final water art.
5. Do not invest in per-state player water masks until the replacement push-swim/body art choice is
   made. If current art is retained, implement state/facing-specific masks; do not use one shared mask.

---

## SESSION LOG — 2026-07-13b (successful-steal stun always; blindside attempts become exclusion-level fouls)

Before changing anything, the full current master plan was reread, followed by the complete current
`PlayerMovement.cs` and `PlayerAnimator.cs`. The investigation then traced all successful strip paths
and the existing exclusion/stun owner through `WaterPoloAI.cs`, `Goalkeeper.cs`, and
`ExclusionManager.cs`.

**Diagnosis:**
- The dizzy stars/action lock were not attached to a successful steal at all. They existed only after
  a **failed full-risk steal** remained an ordinary foul, then passed an `aggressive=true` check, a
  **35% random roll**, and a **6-second per-victim cooldown**. Human/AI successful steals transferred
  possession without ever calling `FoulStun`, explaining why a carrier could visibly lose the ball
  with no dizzy feedback.
- The facing test is `dot(carrierFacing, carrier-to-stealer) >= 0.3`, approximately a 70-72 degree
  front-only arc. Human Space and touch Block played the attempt animation and then silently returned
  when outside that arc; AI returned even before its attempt notification. No foul was registered.
- Live close-range values were left unchanged: the scene currently serializes player `stealDistance`
  and AI `grabDistance` at **1.0**. Out-of-range contact still cannot become either a successful steal
  or a blindside foul.

**TASK 1 — every successful close-range steal now produces the dizzy/stun state:**
- Replaced the aggressive-foul chance/cooldown fields with one `successfulStealStunSeconds` field,
  default **1.4s**.
- Added one centralized `ExclusionManager.StunSuccessfulStealVictim(victim)` outcome. It has no chance,
  aggression flag, or repeat cooldown; callers reach it only after their existing proximity gate and
  a successful possession transfer.
- Wired all actual steal owners: human Space steal, touch Block steal, shared player-team/bot AI steal,
  and the goalkeeper's 100%-success point-blank carrier snatch. The victim's existing `FoulStun`
  component supplies the rotating stars and movement/action lock; `PlayerMovement`, `TeammateAI`,
  `BotMovement`, and `Goalkeeper` already honor that lock.
- Front-on success odds, stamina scaling, Centre modifiers, loose-hold bonus, ranges, and possession
  transfer mechanics are unchanged. This changes feedback/outcome after success, not steal probability.
- The former conditional aggressive ordinary-foul stun/forced-drop behavior was removed. A failed
  legal front-on steal continues through the normal ordinary-foul/free-throw system.

**TASK 2 — rear/blindside steal input is an automatic exclusion-level foul:**
- Added `ExclusionManager.ReportExclusionFoul(...)`, which applies the existing steal lockout and
  routes immediately through the established `Escalate` owner. It does not add an ordinary foul,
  start an ordinary free throw, or wait for `foulsForExclusion`.
- In-range contact that fails the existing front-arc check now triggers that direct escalation for
  human Space, touch Block, and AI pressers. The attempt animation still plays and the victim keeps
  the ball.
- Normal escalation behavior is preserved: outside the attacking 2m zone the offender is temporarily
  or permanently excluded as appropriate; inside the 2m zone the same exclusion-level offense becomes
  the existing penalty-shot outcome. Exclusion counts, maximum-removal rules, shot-clock reset,
  pen placement, and event-feed reporting all remain owned by `ExclusionManager`.
- Protected carriers, keeper safe-zone holds, active free throws, cooldown gates, and out-of-range
  presses still exit before this rule. Only genuine close-range blindside contact escalates.

**Files changed this task:**
- `Assets/ExclusionManager.cs`
- `Assets/PlayerMovement.cs`
- `Assets/WaterPoloAI.cs`
- `Assets/Goalkeeper.cs`
- `WATERPOLO_MASTER_PLAN.md` (this log)

`PlayerAnimator.cs` was fully read as required but did not need a change; the dizzy visual is the
existing procedural `FoulStun` stars, not an Animator-controller state. No scene/controller asset or
serialized object reference changed. The new duration is a numeric Inspector field with the 1.4s code
default; old unsaved `aggressiveFoulStun*` values, if present in an open Inspector, are obsolete.

**Build:** `dotnet build Assembly-CSharp.csproj` -> **0 errors, 22 warnings**. Warnings match the
existing obsolete-API/unused-field baseline.

**Manual Unity verification priority:**
1. Win a front-on Space steal, a touch Block steal, and an AI steal: every victim must immediately
   show rotating stars and remain action-locked for about 1.4s after losing possession.
2. Drive into the keeper and let it snatch: the stripped carrier must receive the same stun feedback.
3. Attempt Space and Block directly behind a carrier while inside 1.0u: no possession roll; offender
   immediately receives the exclusion-level outcome and the carrier retains the ball.
4. Repeat with an AI presser approaching from behind and verify the same exclusion symmetry.
5. Repeat the blindside contact with the victim inside the attacking 2m zone: the existing penalty
   sequence should start instead of an ordinary free throw or a temporary pen sit-out.
6. Attempt outside 1.0u and from a legal front angle: out-of-range remains only a lunge; a front-on
   miss remains an ordinary foul, confirming the two new outcomes did not broaden their gates.

---

## SESSION LOG — 2026-07-18 (locked flipbook architecture; URP 2D palette swap; bone rig gated off)

The full master plan was read first, followed by the complete current `PlayerAnimator.cs`, before any
change. This session replaces the live field-player art direction with six-frame sprite flipbooks and
a shared palette-key shader while keeping every not-yet-regenerated state on its existing flat art.

**TASK 1 — custom URP 2D palette-swap shader:**
- Chose a custom shader (`Assets/Shaders/PlayerPaletteSwap.shader`) instead of Shader Graph. The two
  keyed masks, tolerance feather, and luminance-preserving replacements are short and explicit in
  HLSL; a hand-authored Shader Graph asset would be much larger and harder to audit for the same work.
- Exposed `_CapTint` and `_SwimwearTint`. The shader samples the SpriteRenderer texture before its
  ordinary renderer tint, matches #FF00FF / #00FFFF with a smooth **0.45** color-distance tolerance,
  and outputs source luminance x the matching tint. Non-key pixels and source alpha pass through.
- `PlayerPaletteSwapRuntime` loads the Resources material template and clones **one unique material
  instance per swimmer**. A human player's front/back renderers share only that player's instance;
  every other player/bot receives another instance. Renderer color/alpha still multiplies normally,
  so exclusion dimming remains compatible.
- `PlayerAnimator` exposes per-player test `Cap Tint` / `Swimwear Tint` fields (red/white defaults).
  `BotAnimator` uses the same runtime helper/shader and selects blue or red team-default fields from
  `BotMovement.isBlueTeam`; there is no bot-only shader/rendering implementation.

**TASK 2 — legacy bone system disabled but retained:**
- `PlayerAnimator.enableLegacyBoneRigForRollback` is OFF by default. With it OFF, BoneBody, HoldBody,
  BackBoneBody and BackHoldBody Animators are disabled, their SpriteRenderers are forced off, their
  state/visibility checks cannot win, and their depth-transform updates do not run.
- All serialized bone references and the old branch remain for rollback. Turning the gate on restores
  the prior state-driven path.
- The existing flat `FrontBody` / `BackBody` selection, renderer enable toggle, facing latch, and old
  controller fallback remain in place. With all bone bodies off, exactly one flat renderer stays
  active, preventing an idle/holding visual gap.

**TASK 3 — six-frame flipbook playback with legacy fallbacks:**
- The three source sheets were moved to the locked locations and re-sliced as true equal-cell grids:
  `Animations/BlueTeam/idle_floating.png` = **6x1** (333x787 cells), `swimming.png` = **3x2**
  (689x380), `throwing.png` = **3x2** (545x481). GUIDs were preserved, row order is top-left to
  top-right then bottom-left to bottom-right, and max import size is 4096 so the 2067px swim sheet is
  not reduced.
- `Resources/PlayerFlipbookSet.asset` owns the three six-sprite arrays. Both player and bot presentation
  code load this same set automatically; no per-scene drag slots are required.
- `Flipbook Frames Per Second` is serialized on both animators, default **12 fps**. The existing
  `isFloating`, possession, movement/sprint thresholds, defend/steal windows, exclusion state and
  shoot-release latch are computed once and reused for selection. Idle and normal swimming loop;
  throwing plays all six frames once through the existing shoot latch.
- Holding, sprinting, defending, stealing and excluded presentation deliberately stays on the old
  Animator art. The new sheets only override idle/swim/throw in `LateUpdate`, so those fallbacks keep
  functioning until replacement sheets arrive.

**Files added/changed for this architecture:**
- Added `Assets/Shaders/PlayerPaletteSwap.shader`.
- Added `Assets/PlayerFlipbookSet.cs`, `Assets/PlayerVisualRuntime.cs`, the Resources flipbook asset,
  and the Resources palette material template.
- Changed `Assets/PlayerAnimator.cs` and `Assets/BotAnimator.cs`.
- Relocated/re-sliced the three supplied PNGs and their existing metas; no gameplay, scene, controller,
  Firebase/preset, or AI-decision file was changed for this task.

**Build:** `dotnet build Assembly-CSharp.csproj` -> **0 errors, 22 warnings**. Warnings are the existing
obsolete-Unity-API/unused-field baseline; this session adds no C# compile error.

**Manual Unity test checklist:**
1. Select Player/Player2-Player6 during Play mode. Change `PlayerAnimator > Palette Swap Test Colors >
   Cap Tint` to obvious red/green values and `Swimwear Tint` to white/yellow/blue. The magenta cap and
   cyan swimwear regions must change immediately, including feathered marker edges; skin/black lines/
   transparent pixels must remain unchanged. Confirm two players can show different colors at once.
2. Watch an idle swimmer for several cycles, then move normally: idle and swim must each visit frames
   0-5 in order and loop without a blank frame. Change `Flipbook Frames Per Second` from 12 to 6 and
   18 during Play mode; playback must visibly slow/speed up without changing gameplay movement.
3. Shoot while stationary and while moving, facing both directions. The existing shoot latch must play
   the six throwing frames once in order, then return to idle/swim; aim, ball release and shot power
   must remain unchanged.
4. Exercise holding, sprint, defend proximity and steal. Each must still show its old placeholder/flat
   Animator art without exceptions or missing sprites.
5. Inspect every Player hierarchy while idle and holding: BoneBody/HoldBody/BackBoneBody/BackHoldBody
   renderers must remain OFF and their Animators disabled. `FrontBody` or `BackBody` must always remain
   visible, with the existing direction toggle/facing latch and no one-frame body gap.
6. Watch bots through idle/swim/throw and change their blue-team default palette fields during Play.
   They must use the same keyed shader/flipbook source while retaining bot team colors and old fallback
   states. Confirm player and bot materials show `(Instance)` and are not the same material object.

---

## SESSION LOG — 2026-07-18b (Player/Bot flipbook scale parity; legacy bone system fully removed)

The full current master plan was read first, followed in order by the complete current
`PlayerVisualRuntime.cs`, `PlayerAnimator.cs`, and `BotAnimator.cs`, before any change. This session
empirically diagnosed the tiny human-controlled Player regression, corrected only the flipbook scale
path, and completed the requested irreversible removal of the obsolete bone-animation architecture.
The existing swimming-sheet slicing/pivot drift was deliberately left unchanged.

**Scale/PPU diagnosis — root cause confirmed before editing:**
- Every Bot SpriteRenderer is on its Bot root, whose actual scene `localScale` is
  **(0.3, 0.3, 1)**. Bots do not have a separate visual child scale.
- Every human Player root is **(1, 1, 1)**, but its active flat renderer children retained the old
  large-part-art scales: Player **0.08 front / 0.07 back**; Player2 **0.07 / 0.07**; Player3
  **0.07 / 0.07**; Player4 **0.08 / 0.077**; Player5 **0.07 / 0.07**; Player6
  **0.08 / 0.077**. Their effective flipbook scale was therefore only **0.07-0.08**, making the same
  sprite 3.75x-4.29x smaller linearly than the Bot version.
- `PlayerVisualRuntime.PlayerFlipbookPlayback.Apply` assigns only `renderer.sprite`. `PlayerAnimator`
  calls it on the enabled FrontBody/BackBody child, while `BotAnimator` calls it on the Bot root
  renderer. Neither path previously changed Transform scale or Sprite PPU; the divergence was solely
  the pre-existing renderer hierarchy scale.
- `idle_floating.png`, `swimming.png`, and `throwing.png` are all imported at **100 PPU**. Both
  animators load the exact same `Resources/PlayerFlipbookSet.asset` Sprite references, so Player and
  Bot cannot receive different PPU instances. The old serialized Player and Bot fallback sprites also
  measure **100 PPU**. PPU mismatch is ruled out.

**Scale fix:**
- `PlayerAnimator` now exposes `Flipbook Renderer Local Scale`, default **(0.3, 0.3)**. While an
  idle/swim/throw flipbook is active, LateUpdate applies that absolute local X/Y scale to FrontBody and
  BackBody, giving the human the same effective **0.3** sprite scale as Bots.
- Each body's original Inspector-authored scale is cached in Awake and restored whenever playback
  falls back to old Animator art. Holding, sprinting, defending, stealing, and excluded placeholders
  therefore retain their existing 0.07-0.08 presentation instead of being enlarged as collateral
  damage. The Player root/collider and every Bot root/collider remain untouched.

**Full bone-animation removal:**
- Removed every BoneBody/HoldBody/BackBoneBody/BackHoldBody field, renderer toggle, Animator branch,
  transform/depth update, and rollback gate from `PlayerAnimator`. The live visibility path is now
  exclusively the existing flat FrontBody/BackBody toggle plus flipbook override.
- Removed all four bone setup menu commands, their prefab/controller constants, and their serialized
  wiring code from `AnimatorBuilder.cs`. The remaining flat-body/controller setup tools are unchanged.
- The reference audit found no ordinary player prefab using these objects. It did find the closed old
  dependency set in both gameplay scenes, the Unity recovery scene, the CharacterRig authoring scene,
  four rig prefabs, five controllers (including `PlayerBodyAnimation.controller`), and four bone clips.
- Cleaned exactly **24 rig prefab instances / 96 YAML documents per scene** from
  `SampleScene_PoolB.unity`, `SampleScene.unity`, and `_Recovery/0 (6).unity`, including stale
  PlayerAnimator object references and parent-child links. Flat FrontBody/BackBody references remain
  wired for all six players in all three scenes.
- Deleted the bone-only `CharacterRig.unity`; `test_0`, `hold_0`, `test-back_0`, and `back-side_0`
  prefabs; `PlayerBodyAnimation`, `BoneBodyAnimation`, `HoldBodyAnimation`,
  `BackBoneBodyAnimation`, and `BackHoldBodyAnimation` controllers; and `floating_body`,
  `holding_body`, `floating_body_back`, and `holding_body_back` clips (14 Unity assets / 28 asset+meta
  files). A final project-wide audit finds no remaining code, scene, prefab, controller, or clip
  reference to that system outside this historical master-plan log.

**Files changed:**
- `Assets/PlayerAnimator.cs`
- `Assets/Editor/AnimatorBuilder.cs`
- `Assets/Scenes/SampleScene_PoolB.unity`
- `Assets/Scenes/SampleScene.unity`
- `Assets/_Recovery/0 (6).unity`
- Deleted the 14 bone-only assets listed above
- `WATERPOLO_MASTER_PLAN.md` (this log)

`PlayerVisualRuntime.cs` and `BotAnimator.cs` were fully read and empirically compared but required no
change. The three source PNGs and their `.meta` slicing/pivot data were not modified.

**Build:** `dotnet build Assembly-CSharp.csproj` -> **0 errors, 22 warnings**. Warnings match the
existing obsolete-Unity-API/unused-field baseline.

**Manual Unity verification priority:**
1. Enter Play in `SampleScene_PoolB`. Compare Player and a Bot while both idle, then while both swim:
   their displayed flipbook scale should now match closely. Inspect Player FrontBody/BackBody during a
   flipbook and confirm effective local X/Y is 0.3; Bot root remains 0.3.
2. On each PlayerAnimator, change `Flipbook Renderer Local Scale` from 0.3 to an obvious smaller/larger
   value during Play. Idle/swim/throw should resize immediately without changing the Player root,
   collider, movement, ball anchor, or Bot scale. Restore it to 0.3.
3. Exercise idle, swim, and shoot on both sides/facings. Confirm frames still cycle singly at the
   configured FPS, throwing returns correctly, palette swap still works, and no front/back visual gap
   appears during transitions.
4. Exercise holding, sprint, defend, steal, and exclusion fallbacks. They must restore their original
   per-child 0.07-0.08 scale and continue showing old flat art without becoming 4x too large.
5. Inspect Player through Player6 in the hierarchy. Each should contain FrontBody and BackBody but no
   BoneBody, HoldBody, BackBoneBody, or BackHoldBody child; PlayerAnimator must expose no bone fields or
   rollback flag, and the Tools menu must expose no bone setup commands.
6. Reopen both gameplay scenes after Unity imports the deleted assets. Confirm there are no Missing
   Prefab, Missing Animator Controller, or missing-reference warnings on any Player, and that exactly
   one of FrontBody/BackBody remains visible at all times.

---

## SESSION LOG — 2026-07-18c (Player flipbook state-mix regression; instant flat-fallback transitions)

The full current master plan was read first, followed in order by the complete current
`PlayerAnimator.cs`, `PlayerVisualRuntime.cs`, and `BotAnimator.cs`, before any change. This session
was strictly scoped to the human Player team (Player through Player6). Bots were treated as a
read-only baseline throughout.

**Empirical Play-mode diagnosis — root cause confirmed before editing:**
- A temporary Player-only probe ran `SampleScene_PoolB` in an isolated Unity Play-mode project copy.
  It drove Player through idle → swimming → holding → throwing → idle and recorded the enabled
  SpriteRenderer after `LateUpdate`, including source asset, array membership, index, PPU, and the
  active playback array. Probe changes never entered the source project.
- All six Players resolved the same valid six-sprite arrays. Every observed flipbook sprite was
  **100 PPU**. The apparent idle asset-file-ID discrepancy in YAML did not produce a runtime fault;
  Unity resolved `idle_0` through `idle_5` correctly.
- Idle was the correct ordered loop: **idle_0 → 1 → 2 → 3 → 4 → 5 → 0** from
  `idle_floating.png`. Swimming was the correct ordered loop: **swimming_0 → 1 → 2 → 3 → 4 → 5 →
  0** from `swimming.png`. Throwing was the correct one-shot sequence: **throwing_0 → 1 → 2 → 3 →
  4 → 5**, then the shoot latch selected idle directly. No state used another state's array and no
  frame played out of order.
- The actual mix-up was isolated to **swimming → legacy holding fallback**. At phase entry the live
  visible sequence was `swimr_0` (frame 543, t=1.603), then `swiml_0` (frame 548, t=1.615), then
  finally `hold_0` (frame 553, t=1.628). The Player front/back controllers had seven fixed-duration
  Any-State transitions set to **0.05 seconds**. `SpriteRenderer.m_Sprite` is a discrete object
  reference and cannot blend, so the non-zero transition exposed stale source/intermediate swim art.
- Idle → swimming selected `swimming_0` on the transition frame. Holding → throwing selected
  `throwing_0` on the transition frame. Throwing → idle selected `idle_0` at the exact 0.5-second
  shoot-latch boundary. Those paths had no stale-frame defect.

**Bone/legacy audit:**
- Runtime hierarchy and reflection audits covered Player through Player6. Each has only the expected
  root/FrontBody/BackBody flat Animators; `boneLikeLiveCount=0`, and `PlayerAnimator` exposes no bone
  field or rollback gate.
- A final source/asset audit found no BoneBody, HoldBody, BackBoneBody, BackHoldBody, bone renderer/
  animator, or SpriteSkin reference in code, gameplay scenes, prefabs, controllers, clips, or assets.
  Nothing from the removed bone system was executing, so no further bone deletion was necessary.

**Player-only fix and cleanup:**
- Set all seven Any-State transition durations to **0** in each human child controller:
  `PlayerFrontAnimation.controller` and `PlayerBackAnimation.controller` (14 transitions total).
  These controller GUIDs are referenced by the six Player child bodies and not by Bot objects.
- Updated `AnimatorBuilder.AnyTo` to regenerate Player controllers with zero-duration transitions;
  rerunning setup tooling can no longer restore the regression. Added the discrete-sprite rationale
  beside that setting.
- Corrected stale `PlayerAnimator` documentation that described vertical/back-facing selection even
  though the live front/back toggle is horizontal, and clarified that the hidden controllers are flat
  fallbacks. No state-detection logic, frame arrays, PPU, scale, palette, scene object, or bot path was
  changed.

**Post-fix Play-mode proof:**
- On the same swimming → holding boundary, the first and only visible holding sprite was now
  **`hold_0` on the phase-entry frame** (frame 463, t=1.602). `swimr_0` and `swiml_0` never appeared.
- Holding → throwing still selected `throwing_0` immediately, played throwing_0 through throwing_5 in
  order, and handed directly to idle. The post-fix probe reported no C# compilation error and the
  bone summary remained zero across all six Players.
- Protected read-only files retained their exact session-start SHA-256 values:
  `PlayerVisualRuntime.cs` = `B1F8ACE73ADB4CBE46545DA742A4083C29745EB123CD9911E17DC4D2379C5685`,
  `BotAnimator.cs` = `9B7B666EE132B395CBC67035106DD342C153ECDC34388201B562401A4FD72682`,
  and `SampleScene_PoolB.unity` =
  `584EEE394BA6AFA7020A7D7D25799803C00CA018456475F0B57B349B12F46373`.

**Files changed this session:**
- `Assets/Sprites/Players/Animations/PlayerFrontAnimation.controller`
- `Assets/Sprites/Players/Animations/PlayerBackAnimation.controller`
- `Assets/Editor/AnimatorBuilder.cs`
- `Assets/PlayerAnimator.cs` (documentation cleanup only)
- `WATERPOLO_MASTER_PLAN.md` (this log)

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors, 22 warnings**. Warnings are the existing
obsolete-Unity-API/unused-field baseline.

**Manual Unity verification priority:**
1. In Play mode, watch Player through Player6 idle for two full cycles. Each must show only
   `idle_floating` frames 0-5 in order and loop without a blank or swimming frame.
2. Move each controlled Player in both directions. Idle → swim must show `swimming_0` immediately,
   then 0-5 in order. Stop without the ball and verify the idle array resumes without an old swim
   frame persisting.
3. Capture swimming → holding frame-by-frame (a 60 fps screen recording is useful). The first frame
   after possession becomes a static hold must be `hold_0`; neither `swimr_0` nor `swiml_0` may flash,
   and the renderer must not go blank. Repeat facing both directions.
4. From holding, shoot while stationary and while moving. The first release frame must be
   `throwing_0`; throwing must visit 0-5 once, then return directly to idle when stopped or swimming
   when moving. No holding/swimming placeholder may appear inside the throw.
5. Exercise sprint, defend, steal, and exclusion fallbacks. Their old flat art must switch immediately
   and remain at its authored fallback scale; returning to idle/swim must restore the 0.3 flipbook
   scale without a one-frame size or sprite leak.
6. Change `Flipbook Frames Per Second` to 6, 12, and 18 during Play. The loop speed must change while
   state membership and frame order remain correct. Also recheck cap/swimwear Inspector tints to
   confirm the palette material still follows every frame.
7. Inspect Player through Player6: FrontBody/BackBody remain wired; no BoneBody/HoldBody variants,
   SpriteSkin, bone fields, rollback gate, missing controller, or missing-reference warning exists.

---

## SESSION LOG — 2026-07-18d (confirmed-dead Parts cleanup; orphaned back-hold clip diagnosis)

This was a tightly scoped cleanup and diagnosis pass. The full current master plan was read first,
followed by the complete current `PlayerAnimator.cs` and `PlayerVisualRuntime.cs`, before the Parts
audit that preceded the deletion. No live animation asset was removed and the broken back-holding
state was deliberately left unchanged pending an art/fix decision.

**Confirmed-dead Parts assets deleted:**
- Deleted `Assets/Sprites/Players/Parts/back-side.png` and its `.meta` (GUID
  `d6e294de35c18c04bae6729f925a1faf`).
- Deleted `Assets/Sprites/Players/Parts/player_parts_red.png.png` and its `.meta` (GUID
  `77e81d5d0fc27da42a18ed4d4be015e6`).
- Deleted `Assets/Sprites/Players/Parts/test 1.png` and its `.meta` (GUID
  `23be5ec5b94de3c4482ba3fa8e167334`).
- Each GUID had zero current code, clip, controller, scene, prefab, Resources-load, or other
  serialized consumers before deletion. Unity's live Asset Pipeline refresh then reported exactly
  **3 deleted assets** (`non-scripts 3596 -> 3593`) and emitted no new missing-reference, missing-
  script, error, or warning message after that refresh.

**Missing `PlayerBackAnimation.controller` holding motion — diagnosis only:**
- The back controller's `holding` state currently points at missing clip GUID
  `83a57d71644c5b1489154b43af7aefbe`. Git history proves this is not fallout from today's cleanup:
  the dangling GUID was introduced in commit `0d8eb5f` (the June back-bone/back-hold session), and
  no committed `.meta` in that commit or any later revision ever owned it.
- The intended flat fallback is explicit in `AnimatorBuilder`: the back `holding` state should use
  **`Animations/hold-back.anim`**, built as a static SpriteRenderer swap from
  **`Parts/hold-back.png`**. Neither asset exists now, and neither has ever been committed under
  those paths, so there is no correct existing clip to reattach.
- The historical `holding_body_back.anim` is **not** that fallback. Its real GUID was
  `24a24c45e058eff4c8609fcc4f5d526f`, and it animates `bone_1`/`bone_3`/`bone_6` transform paths for
  the removed SpriteSkin rig; attaching it to today's flat BackBody would not drive
  `SpriteRenderer.m_Sprite` and would reintroduce an incompatible bone dependency.
- Correct repair: choose/import a valid back-facing hold sprite, create the missing one-frame
  `hold-back.anim` SpriteRenderer clip, and assign it to the back controller. Technically this is a
  small rebuild once the art is chosen, but it is **not** a valid one-click reattach today. A quick
  stopgap could reuse the front `holding.anim`, but it would show the wrong view and was not applied.

**Files changed this session:**
- Deleted the three PNG + `.meta` pairs listed above.
- `WATERPOLO_MASTER_PLAN.md` (this entry only).
- No controller, clip, scene, prefab, C# animation path, or remaining Parts asset was changed.

**Verification:**
- `dotnet build Assembly-CSharp.csproj` -> **0 errors, 0 warnings**.
- Running Unity Editor refreshed the deletion and produced no new Missing Reference warnings.

---

## SESSION LOG — 2026-07-19 (startup legacy-pose flash fixed; moving carriers use new swim; six-frame holding wired)

This session followed a visual regression report that Player, Player5 and Player6 still showed old
back/floating art, moving ball carriers used the old swim controller, all swimmers flashed legacy
poses at match start, and stopped carriers still used the old holding pose. The causes were measured
from the live scene serialization and confirmed with an automated Play-mode state probe before the
temporary verifier was removed.

**Root causes confirmed:**
- The exact three reported players — `Player`, `Player5`, and `Player6` — had all four serialized
  `PlayerAnimator` slots (`frontAnimator`, `backAnimator`, `frontRenderer`, `backRenderer`) set to
  `fileID: 0`. Their valid FrontBody/BackBody children still existed, but both child
  SpriteRenderers were serialized enabled and continued playing the old `test` / `test-back` clips
  because `PlayerAnimator` had no references with which to select or override them.
- The other three players were wired, but their scene-authored FrontBody and BackBody renderers also
  began enabled together. Selection happened in the first `Update`, leaving a possible first-frame
  legacy-pose flash.
- Bots explicitly played their old controller `idle` state during `Awake`; the new idle flipbook was
  not assigned until `LateUpdate`, creating the equivalent old blue/red first-frame flash.
- Both `PlayerAnimator` and `BotAnimator` only selected the new swimming array while
  `isHolding == false`. A moving carrier therefore fell out of the flipbook path and exposed the old
  Animator swimming/holding art even though the current `swimming.png` sheet was valid.

**Fixes:**
- Rewired all four body slots for Player, Player5 and Player6 in `SampleScene_PoolB`; all six human
  PlayerAnimators now have complete FrontBody/BackBody Animator + SpriteRenderer references.
- Added runtime self-healing in `PlayerAnimator.Awake`: if any serialized body slot is ever lost
  again, it resolves the correctly named `FrontBody` / `BackBody` children before material or
  visibility setup.
- Player and bot `Awake` now immediately select and assign `idle_floating_0`. Human startup forces
  exactly FrontBody visible and BackBody hidden before the first rendered frame; bots similarly
  replace the controller placeholder immediately. Normal direction/facing logic resumes in Update.
- Added a fourth six-frame array, `holdingFrames`, to `PlayerFlipbookSet` and wired all six imported
  `holding_0..holding_5` sub-sprites from
  `Assets/Sprites/Players/Animations/BlueTeam/holding.png` into the Resources asset.
- New state rule for humans and bots: **moving with the ball -> current `swimming.png` flipbook**;
  **stopped with the ball -> current `holding.png` flipbook**. PlayerMovement's existing forward
  moving-ball anchor remains the ball owner, so the ball stays ahead of travel without changing
  pass/shot aim or gameplay possession.
- Sprinting, defending, stealing and exclusion still use their old flat placeholder art until their
  replacement sheets are supplied. Idle, swimming (with or without possession), holding and
  throwing now all use current flipbooks.

**Holding-sheet import fact (art follow-up):**
- The supplied file is **1610x977, 100 PPU**, Sprite Mode Multiple, with six named sprites.
- Its saved sprite rectangles are auto-trimmed and unequal (widths 536/493/547/536/478/583; height
  488), not six equal 3x2 grid cells. The metadata was deliberately left untouched this session so
  the newly supplied art was not silently re-sliced or clipped. It works as wired, but if holding
  appears to shift/resize between frames, regenerate it on a true evenly divisible 3x2 canvas with
  one consistent anatomical anchor/pivot per cell; several figures currently extend across the
  theoretical equal-cell boundaries.

**Empirical Unity Play-mode proof:**
- `PlayerFlipbookSet`: idle/swim/hold/throw arrays all reported valid six-frame arrays.
- START: Player through Player6 and Bot through Bot6 all resolved
  `BlueTeam/idle_floating.png`; every human had exactly one body renderer visible.
- MOVING_WITH_BALL: all 6 players + all 6 bots resolved `BlueTeam/swimming.png`.
- STOPPED_WITH_BALL: all 6 players + all 6 bots resolved `BlueTeam/holding.png`.
- All three stages reported `allGood=True`; the verifier then exited Play mode and was deleted.

**Files changed:**
- `Assets/PlayerFlipbookSet.cs`
- `Assets/Resources/PlayerFlipbookSet.asset`
- `Assets/PlayerAnimator.cs`
- `Assets/BotAnimator.cs`
- `Assets/Scenes/SampleScene_PoolB.unity`
- `WATERPOLO_MASTER_PLAN.md` (this log)
- The user-supplied `holding.png` / `.meta` were inspected and wired but not modified.

**Build:** `dotnet build Assembly-CSharp.csproj` -> **0 errors, 0 warnings** on the final post-probe
project state. Unity's final recompile also completed successfully.

**Manual visual verification:**
1. Start a match and watch the first visible frame: every Player/Bot must already be on the new
   palette-swapped idle sheet; no old front+back double body or blue legacy pose may flash.
2. Carry the ball while moving right, left and diagonally: the new swimming flipbook must remain
   active and the ball must stay ahead of travel.
3. Release movement while still holding: switch to the new holding_0..5 loop. Move again: return
   directly to the new swimming sheet, never the old two-frame swim/hold controller.
4. Repeat on Player, Player5 and Player6 specifically, then watch bot carriers perform the same
   moving-swim / stopped-hold split.
5. Watch holding for frame-to-frame size/position drift. Any remaining drift is the measured
   auto-trim/source-framing issue above, not a state-selection or old-animation leak.

---

## SESSION LOG — 2026-07-19b (legacy backward swim disabled; per-state tuning, water cover, saved player RGB)

This pass addressed the human-player report that backward/sprint movement could still reveal the
old swim animation, that the four available sheets need independent visual sizing/playback control,
and that cap/swimwear colors should be editable from My Club rather than only on scene components.

**Measured causes:**
- `PlayerAnimator.SelectFlipbook` explicitly required `!isSprinting` before it would select current
  flipbook art. Backward movement while sprinting therefore fell into the legacy front/back Animator
  controllers, exposing the old `swimming_back`/sprint artwork. Those legacy controllers also kept
  evaluating and writing `SpriteRenderer.m_Sprite` behind active flipbooks.
- The art uses shaded marker regions rather than literal solid keys. Pixel audit results were:
  `idle_floating.png` 1998x787 with **0 exact magenta pixels**, but 31,993 magenta-dominant pixels;
  `swimming.png` 2068x760 with 1 exact magenta / 25 exact cyan pixels and 12,433 magenta-dominant /
  9,974 cyan-dominant pixels; `throwing.png` 1635x962 with 5 exact magenta / 302 exact cyan pixels;
  `holding.png` 1610x977 with 86 exact magenta and no cyan region. The old 0.45 Euclidean RGB radius
  was therefore simultaneously too broad around bright keys and unreliable across darker marker
  shading.

**Animation fixes and controls:**
- Every ordinary human movement speed and horizontal direction now uses `swimming.png`, including
  sprinting/backward movement and moving with the ball. Sprint is no longer a flipbook fallback.
- While idle/swim/hold/throw flipbooks are active, both old flat Animator controllers are disabled,
  preventing them from writing stale sprites or running the retired back-swim animation underneath.
  They are enabled only for defend/steal/exclusion placeholder states that still lack new sheets.
  The legacy shooting trigger is no longer queued when the replacement throwing sheet is valid.
- Added separate Inspector FPS fields for idle, swimming, holding and throwing (defaults 8/8/8/18),
  plus an `Idle Swimming Transition Delay` (default 0.12 seconds) used only to debounce the
  idle<->swim boundary. Holding and shooting still switch immediately.
- Preserved `Flipbook Renderer Local Scale` as the overall visual-only size and added independent
  idle/swimming/holding/throwing X/Y size multipliers. These affect only FrontBody/BackBody visual
  children, never the player root, Rigidbody or collider.

**Water and palette presentation:**
- Added a runtime-generated, swimming-only soft wave band in front of the lower human-player sprite.
  It has no collider/gameplay effect and exposes enable, size, offset, color/opacity, drift speed and
  bob amount in `PlayerAnimator`. This is a reusable proof-quality cover; frame-specific water masks
  baked into consistently framed future art remain the highest-fidelity long-term option.
- Replaced Euclidean marker matching in `PlayerPaletteSwap.shader` with feathered chroma-dominance
  masks (`min(R,B)-G` for magenta and `min(G,B)-R` for cyan), retaining luminance-times-tint output.
  The material marker threshold is now 0.18. This follows shaded/anti-aliased marker art while
  avoiding the previous broad reach into skin-adjacent colors.
- `ClubProfile` now persists `capColorHex` and `swimwearColorHex`. Older saves self-heal from their
  existing primary/secondary club colors. My Club now presents exact 0-255 R/G/B inputs and live
  previews for both player colors; APPLY saves them with the roster. Human `PlayerAnimator` objects
  load those saved colors in `Awake`. `Use My Club Palette` can be disabled per player to test its
  local Inspector colors instead.

**Files changed in this pass:**
- `Assets/PlayerAnimator.cs`
- `Assets/PlayerVisualRuntime.cs`
- `Assets/Shaders/PlayerPaletteSwap.shader`
- `Assets/Resources/Materials/PlayerPaletteSwap.mat`
- `Assets/Scripts/Roster.cs`
- `Assets/Scripts/RosterManager.cs`
- `Assets/Scripts/ClubCustomizationUI.cs`
- `WATERPOLO_MASTER_PLAN.md` (this entry)

`BotAnimator.cs`, bot scene objects, bot scales and bot material configuration were not edited in
this pass. Temporary verification scripts were removed and no diagnostic asset remains.

**Verification:**
- Final `dotnet build Assembly-CSharp.csproj` completed with **0 errors, 0 warnings** (incremental
  build). `dotnet build Assembly-CSharp-Editor.csproj` also completed with 0 errors while compiling
  the temporary state verifier; its warnings were the existing obsolete-Unity-API baseline.
- A separate headless Unity execution was attempted for live state/shader proof, but Unity correctly
  refused because this project is already open in another Editor instance. That Editor was left
  untouched; the live visual checklist below is therefore still required after it refreshes scripts.

**Manual visual verification:**
1. Swim left/backward while holding sprint: only `swimming_0..5` may appear; no old blue/back pose.
2. Stop/start repeatedly: idle<->swim should respect the 0.12-second debounce with no stale sprite;
   holding and throwing must still switch immediately. Confirm throw plays once at 18 FPS.
3. Tune each state size multiplier independently and verify the collider/root size never changes.
4. During swimming, verify the wave band covers the lower body and drifts gently. Tune its size,
   offset and alpha for the pool camera; it must disappear during idle, hold and throw.
5. In My Club, enter visibly different cap/swimwear RGB values, APPLY, then start a match. The idle,
   swimming, holding and throwing sheets must use the saved colors; the cap mask must no longer spill
   into skin. Disable `Use My Club Palette` on one PlayerAnimator to verify local Inspector fallback.

---

## SESSION LOG — 2026-07-19c (main-menu stack overflow fixed; water reverted; unified arrow palette)

This follow-up supersedes the water-cover and numeric-RGB portions of the preceding 2026-07-19b
entry. The user chose to handle water in the source art and requested a simpler, single-source color
workflow.

**Main-menu failure fixed:**
- The live stack trace proved a direct season-rollover recursion:
  `LeaderboardManager.EnsureSeason -> Rank -> Standings -> EnsureSeason`. Rollover now records the
  old season through `RankUnchecked` / `StandingsUnchecked`, while public `Rank` and `Standings`
  still perform their normal season validation. A defensive re-entry guard also prevents future
  rollover queries from recreating this stack overflow.
- Removed `RosterManager.Instance` access from `PlayerAnimator.OnValidate`. Unity had reported that
  the self-bootstrapping component was being created during validation, where `SendMessage` is not
  legal. Saved profile colors are now resolved only during runtime `Awake`.

**Water feature completely reverted:**
- Removed every swimming-water serialized field and every create/update/hide call from
  `PlayerAnimator`.
- Removed the complete `PlayerSwimmingWaterOverlay` runtime class, generated texture/sprite logic,
  renderer child and animation code from `PlayerVisualRuntime`.
- Repository search reports no remaining `SwimmingWaterOverlay`, `swimmingWater`,
  `showSwimmingWater`, or water-sprite runtime reference. No scene/prefab water child was serialized;
  the old object existed only transiently in Play mode, so a fresh play session cannot recreate it.

**One authoritative player palette:**
- Deleted the numeric cap/swimwear RGB input fields and their parsing helpers from My Club.
- Added previous/next arrow selectors for 14 named common colors: Blue, Red, Green, Gold, Purple,
  Teal, Orange, Navy, Charcoal, White, Pink, Cyan, Black and Lime. Each selector shows the current
  name on its actual color, with automatic readable light/dark label text.
- Existing saved/custom hex values map to their nearest common color when the screen opens; APPLY
  persists the selected cap and swimwear colors in the existing `ClubProfile` fields.
- Removed the `PlayerAnimator` `Use My Club Palette`, Cap Tint and Swimwear Tint Inspector controls.
  The saved My Club palette is now the sole human-player source, so different Player objects cannot
  silently override it. All human palette material instances read the same saved profile values in
  `Awake`; bot configuration remains separate and unchanged.

**Files changed:**
- `Assets/Scripts/LeaderboardManager.cs`
- `Assets/PlayerAnimator.cs`
- `Assets/PlayerVisualRuntime.cs`
- `Assets/Scripts/ClubCustomizationUI.cs`
- `WATERPOLO_MASTER_PLAN.md` (this entry)

**Build:** final `dotnet build Assembly-CSharp.csproj` completed with **0 errors**. The 22 warnings
are the existing obsolete-Unity-API/unused-field baseline. Unity's open Editor captured one
intermediate picker save and still needs `Assets > Refresh` (or an Editor restart) to replace those
stale `capRgbFields` Console entries with the final clean source.

**Required verification:**
1. Stop Play mode after the old stack overflow, allow a full Unity domain reload, clear Console, and
   reopen the main menu/ranking screen. No leaderboard recursion or palette `OnValidate` error may
   return.
2. Open My Club and click both `<` / `>` selectors through all 14 colors. APPLY, reopen My Club and
   confirm both choices persisted.
3. Start a match and verify Player through Player6 all use the saved cap/swimwear colors in every
   current flipbook state. PlayerAnimator must expose no separate color override fields.
4. Swim in every direction and verify no `SwimmingWaterOverlay` child or procedural cover appears.

---

## SESSION LOG — 2026-07-19d (sprint flipbook, directional swimmers, held-ball motion)

This presentation pass keeps the current idle/swimming/holding/throwing sheets and leaves the
legacy defending/stealing/exclusion placeholders unchanged until replacement art exists.

**Measured sprint cause and fix:**
- The human `PlayerAnimator` path already allowed every ordinary movement speed, including sprint,
  to select `SwimmingFrames`. The bot path did not: `BotAnimator.SelectFlipbook` explicitly required
  `!isSprinting`, so any driving bot fell out of the current flipbook and exposed its legacy
  controller art while racing to the ball.
- Removed that bot-only gate. Bot driving/sprinting, ordinary loose-ball pursuit, and moving with
  possession now all select the same current six-frame swimming sheet. Defend, steal and exclusion
  still deliberately fall back to their existing placeholders.

**Full-direction visual rotation:**
- Current swimming art now turns toward the full Rigidbody2D velocity, so vertical and diagonal
  travel no longer leaves the body horizontal while only the aim arrow points up/down.
- Human players rotate only their existing `FrontBody`/`BackBody` visual child. Bots now render
  current flipbooks on a runtime `FlipbookBody` child copied from the root SpriteRenderer. Their
  legacy controller remains on the hidden root renderer for placeholder states. In both paths the
  gameplay root, Rigidbody2D and collider never rotate or change scale.
- The turn can be enabled/disabled and its speed tuned with `Rotate Swimming To Movement` and
  `Swimming Direction Turn Speed` (default 720 degrees/second). Idle, hold, throw and legacy states
  immediately restore the authored upright rotation.

**Held-ball swim motion:**
- A moving human carrier keeps the ball along actual travel direction near the leading/head side,
  with a small 0.06-unit in/out pulse at 1.7 cycles/second and a gentle +/-7-degree rock. Stopping
  restores the existing art-tuned holding-hand position and upright ball.
- AI carriers use the same pulse/rock values in `WaterPoloBrain.KeepHeldBall`, with a stable
  per-swimmer phase so carriers do not move in lock-step. This is LateUpdate presentation only;
  shot/pass aim, release direction, velocity and possession rules are unchanged.

**Files changed:**
- `Assets/PlayerAnimator.cs`
- `Assets/BotAnimator.cs`
- `Assets/PlayerMovement.cs`
- `Assets/WaterPoloAI.cs`
- `WATERPOLO_MASTER_PLAN.md` (this entry)

**Verification:**
- `dotnet build Assembly-CSharp.csproj` completed with 0 errors. The open Unity Editor log available
  to the session had not yet refreshed to this source revision, so the visual behavior still needs
  the manual Play-mode checks below after Unity recompiles.

**Required Play-mode checks:**
1. Watch a bot drive/sprint toward a loose ball: it must keep cycling the current swimming sheet and
   never switch to floating/legacy sprint art.
2. Move the human up, down and diagonally, then watch bots do the same. Only the rendered swimmer
   turns; collider contacts and movement physics must remain unchanged.
3. Carry the ball while swimming in all directions. It should stay close to the leading/head side,
   pulse slightly nearer/farther, and rock gently without changing the aim arrow or released shot.
4. Stop with the ball: the holding sheet and stationary hand offset must appear immediately, with
   the ball upright. Shoot/pass and verify their existing trajectories are unchanged.
5. Trigger a defend/steal/exclusion fallback and confirm its existing placeholder still renders;
   no defending art was replaced in this pass.

---

## SESSION LOG — 2026-07-19e (directional rotation reverted; bot state-size controls)

- Reverted the full-direction swimming-art rotation added in 2026-07-19d for both human players
  and bots. Moving vertically or diagonally once again keeps the authored swimmer horizontal; the
  established left/right renderer selection and horizontal sprite flipping remain unchanged.
- Preserved the rest of 2026-07-19d: bots still use the current swimming sheet while
  driving/sprinting, and moving carriers retain the subtle held-ball distance pulse and rock.
- Added human-equivalent bot flipbook sizing controls to `BotAnimator`: overall
  `Flipbook Renderer Local Scale` plus independent `Idle`, `Swimming`, `Holding`, and `Throwing Size
  Multiplier` X/Y values. Defaults are all `(1,1)`, preserving each bot root's existing scene scale.
  These values resize only the runtime flipbook visual child, never the bot root, collider, or
  Rigidbody2D. Legacy defend/steal/exclusion placeholder scale remains unchanged.

**Files changed:** `Assets/PlayerAnimator.cs`, `Assets/BotAnimator.cs`, and this master plan.

**Verification:** `dotnet build Assembly-CSharp.csproj` completed with **0 errors** and the 22
existing obsolete-Unity-API/unused-field warnings. In Play mode, verify vertical movement remains
horizontal, sprinting bots still use the current swimming sheet, and changing one bot's four state
size multipliers affects only that state and only that bot's visual.

---

## SESSION LOG — 2026-07-19f (stun idle, unified throwing, swimming ripples, empirical motion/pass audit)

This session began by reading the full current master plan, then the complete current
`PlayerAnimator.cs`, `PlayerVisualRuntime.cs`, `BotAnimator.cs`, `WaterPoloAI.cs`, and
`PlayerMovement.cs`, before tracing the requested runtime owners. The existing flipbook
infrastructure, palette-swap shader, bone-rig removal, and bot per-animation size multipliers were
left intact.

**TASK 1 — successful-steal dizzy/stun now uses the idle/floating flipbook:**
- The stun owner is `FoulStun` in `ExclusionManager.cs`: it supplies stars plus an action/movement
  lock, but it did not explicitly tell either field-player animator which body state to use. A stunned
  player could therefore fall through to holding/defending/legacy selection during the short lock.
- `PlayerAnimator` and `BotAnimator` now read `FoulStun.IsStunned(transform)` before normal visual
  selection. While true, both force the current six-frame `IdleFrames` loop, suppress defend/sprint/
  hold presentation, skip the player idle↔swim debounce, and keep the existing stars. Gameplay
  possession, the stun duration, and all foul/steal odds are unchanged.

**TASK 2 — passes and shots share exactly one throwing animation:**
- Human shooting already called `PlayerAnimator.TriggerShoot`; human charged passing had no equivalent
  throw signal. Added generic `TriggerThrow()` (with `TriggerShoot()` retained as its compatibility
  wrapper) and call it from `PlayerMovement.ChargedPass`.
- Bots already used one release edge (`wasHolding && !isHolding`) for BOTH bot shots and bot passes.
  That edge now calls the same named `BotAnimator.TriggerThrow()` helper, which selects the existing
  six-frame `ThrowingFrames` sequence. There is no distinct pass pose/flipbook.

**TASK 3 — swimming-only water ripple:**
- Reused `BallFlight`'s existing pale-cyan `RippleWave` coroutine and renderer pattern; no new VFX
  system, asset, material, or scene reference was created. `SpawnSwimmingRipple` emits one smaller
  wave.
- Both field-player animators request it only while their active flipbook state is `Swimming`, with a
  per-swimmer 0.55s throttle. Idle, holding, throwing, defend, steal, exclusion, and stun states do
  not call it.

**TASK 4 — pass-target investigation (NO blind weight change):**
- `BestPassTarget` already finds genuinely open field teammates: it applies the live `openRadius`
  1.6 gate, distance-scaled lane risk (`passLaneRadius 0.7 + distance*0.05`), forward gain,
  openness, receiver shot quality, Centre bonuses, and the pressured clear-lane least-bad outlet.
  No evidence supported changing those weights.
- The measurable remaining gap is **arrival prediction**, not target discovery: target openness and
  lane safety are evaluated at `mate.position` now, and `WaterPoloBrain.Pass` launches at that same
  current position. There is no receiver-velocity or future-position term anywhere in either path.
  In the live scene a support swimmer moves at 1.5 u/s (2.55 u/s when the >2u sprint multiplier
  applies); a 5u bot pass travels for about 0.455s at 11u/s, so that receiver can move about 0.68u
  or 1.16u before arrival. Even the 0.32s minimum arc time permits 0.48u / 0.82u of drift. A target
  can therefore be truly open at release but be behind the landing point or into a closing lane at
  arrival. Weights were deliberately not changed; lead-pass/arrival-lane prediction is the next
  correctly scoped improvement if this remains visible in play.

**TASK 5 — moving swimmers showing idle: root cause confirmed with real values:**
- This is NOT an over-permissive floating threshold. Both animators switch at 0.1u/s (float below
  0.15u/s). In ordinary AI movement the actual live scene values are 1.5u/s support cruising,
  2.55u/s support sprinting, and 1.7u/s ball pursuit (`chaseSpeed 1 * SprintMult 1.7`), all far
  above those thresholds; their Rigidbody2D damping is 0.
- The failing shared path is movement written directly by `SprintDuel.MoveTowardsTarget`: it advances
  `Rigidbody2D.position` at the authored 3u/s formation pace, 4u/s bot-sprinter pace, or 3-6u/s
  human-sprinter pace while `PlayFrozen` makes every normal body `FixedUpdate` keep
  `linearVelocity = (0,0)`. Both animators used that zero physical velocity and selected idle while
  the transform visibly moved.
- `SprintDuel` now records this direct-motion velocity in a transient presentation-only map. Both
  animators prefer that value only while the duel is racing; normal physics/AI values remain the
  source everywhere else. This fixes genuine visible movement selecting floating without changing
  the gameplay speed, AI, Rigidbody, or animation thresholds.

**TASK 6 — immediate swimming at sprint-duel GO: root cause and fix:**
- Plain root cause: at GO the duel starts moving sprinters and formation joggers through
  `rb.position`, but their `linearVelocity` stays exactly zero for the whole frozen race. The old
  animation state was therefore stuck on idle until the duel ended and normal physics-owned AI
  movement resumed.
- The same presentation velocity map is populated on the first racing `FixedUpdate` and cleared on
  `Finish`. As soon as the GO race produces visible movement, both sprinters and every formation
  jogger select `SwimmingFrames`; no delayed wait for the duel to unfreeze is required.

**Files changed:**
- `Assets/PlayerAnimator.cs`
- `Assets/BotAnimator.cs`
- `Assets/PlayerMovement.cs`
- `Assets/BallFlight.cs`
- `Assets/SprintDuel.cs`
- `WATERPOLO_MASTER_PLAN.md` (this log)

**Build:** `dotnet build Assembly-CSharp.csproj` -> **0 errors, 22 warnings**. The warnings are the
existing obsolete-API/unused-field baseline; no new compile error was introduced.

**Exact Play-mode tests:**
1. **Stun idle:** win a legal close-range steal against a human player, AI teammate, bot, and keeper
   carrier. For the full ~1.4s stars/action lock, the stripped swimmer must loop only the floating/
   idle flipbook—never holding, defend, steal, legacy art, or a blank body—then resume normally.
2. **Unified throw:** make a stationary pass, moving pass, stationary shot, and moving shot with a
   human player. Each release must start `throwing_0`, play the same six frames once, then return to
   idle/swimming. Watch several bot passes and bot shots: both must use that same throwing sequence,
   never a separate pose.
3. **Swimming ripple:** swim continuously for several seconds as a human and watch a bot swim. Expect
   one small pale-cyan ripple roughly every 0.55s only under active swimming. Stop, hold the ball
   motionless, throw/pass, defend, steal, get excluded, and get stunned: no swimming ripple may spawn.
4. **Pass-target audit:** observe a receiver running laterally/forward during a 4-6u bot pass. Record
   whether the ball lands behind its current movement line or into a defender who closes after
   release; this specifically validates the measured arrival-prediction gap before any lead-pass work.
   Also confirm currently wide-open, clear-lane mates are still selected and pressured bots retain the
   existing least-bad outlet behavior.
5. **Active movement animation:** outside the duel, watch a presser chase a loose ball and an
   off-ball swimmer cover a distant formation spot. At the measured 1.7/1.5-2.55u/s speeds the
   swimming sheet must be active, never floating. Then repeat while the sprint duel moves them by
   direct position; the visible swim must remain active there too.
6. **Duel GO transition:** start Q1 and each later quarter. At the exact GO transition, the human
   sprinter, bot sprinter, and all non-sprinters jogging into formation must switch to swimming on
   their first visible motion frame, while countdown statues remain idle. When a sprinter wins and
   normal play resumes, there must be no stuck swim/idle state or movement/physics change.

---

## SESSION LOG — 2026-07-19g (core gameplay tuning pass)

This pass began with the complete master plan and the complete current `TeammateAI.cs`,
`WaterPoloAI.cs`, `TeamSide.cs`, and `PlayerMovement.cs`. It deliberately leaves animation,
flipbook, palette, shader, and visual presentation code untouched.

**Measurement record before changes:**
- The live `SampleScene_PoolB` components, not their C# defaults, measured as: all AI swimmers
  `chaseSpeed=1`, `supportSpeed=1.5`, bot `carrySpeed=1`, player-team AI `carrySpeed=0.5`,
  `shootRange=20`, `shootPower=12`, and `stealChance=0.4`; active human field players measured
  `moveSpeed=1`, `holdMoveSpeed=0.5`, `sprintMultiplier=2`, `maxShootPower=30`,
  `minShootSpeed=8`, `stealDistance=1`, and `stealChance=0.4`.
- Those values produce real body commands of 1.7u/s for an AI ball chase (`1 * 1.7`), 1.5u/s for
  nearby support, and 2.55u/s for a distant support target (`1.5 * 1.7`). That made support faster
  than pursuit, while the human can sprint at 2u/s without the ball and 1u/s with it.
- A direct automated Play-mode measurement could not be completed against the user-open Unity
  project because it owns `Temp/UnityLockfile`. An isolated copy was used instead; its headless
  PlayMode test runner exited with Unity return code 1 before scene/test execution and emitted no
  telemetry. No invented Play-mode result is recorded here; the exact live serialized values and
  the runtime velocity/reaction paths above are the measurement basis. Manual verification below
  remains required.

**1. Steal fairness — verified, intentionally unchanged:**
- Player and bot ordinary front-on steals both use `0.40 * staminaStealMultiplier` inside the same
  1u centre-to-centre reach and the same 0.6s cooldown. This confirms the old stray Player3 value
  is gone.
- A rear/blindside attempt is an automatic exclusion for both sides, not a random chance. The
  player touch Block remains the deliberate safer option at 0.20 success and only 50% foul on a
  miss. A bot attacking a Shift-sprinting loose hold gets its designed 0.15 bonus (0.55 normal,
  0.35 inside 2m); this is the explicit risk attached to that player-only sprint state, not a
  parity regression.

**2. Shot power/range — range made honest; power retained:**
- Human tap speed is 10.8u/s (`max(currentPower,8) * 1.35`); a full high shot is 46.6u/s
  (`30 * 1.35 * 1.15`). A bot shot remains 12u/s. At the live keeper settings, an ordinary hard
  shot has 50% save chance, a human high hard shot 25%, and a full-speed skip shot 15%. The
  human's stronger full charge is therefore a deliberate timing/height skill reward rather than a
  hidden bot disadvantage; `maxShootPower 30`, bot `shootPower 12`, and keeper values stay put.
- The actual bot non-forced range had silently been 3.5u despite every scene component displaying
  `shootRange=20`; the hard cap made that inspector value inert. Changed all 12 scene
  `shootRange` values **20 -> 4** and `CloseShootDistance` / `MaxShootDistance` **3.5 -> 4**.
  Bots now take a good, clear shot one half-unit earlier, but never shoot from the old placeholder
  20u range. Human aim is manual and has no random accuracy term, so no artificial accuracy spread
  was introduced.

**3. AI swim speeds — tuned hierarchy (all live field-AI components):**
- `chaseSpeed` **1.0 -> 1.2**, so a loose-ball/ball-carrier pursuit is **1.7 -> 2.04u/s**.
- `supportSpeed` **1.5 -> 1.2**, so close formation/mark movement is **1.5 -> 1.2u/s** and the
  existing distant-target sprint path is **2.55 -> 2.04u/s**. Recovery no longer outruns a true
  chase, and both match a sprinting human's 2u/s pace instead of creating a rubber-band burst.
- Bot `carrySpeed` **1.0 -> 0.9u/s**. This keeps a ball carrier visibly slower than a 2.04u/s
  chaser, while allowing a sprinting human carrier (1u/s) a small, skillful escape margin.
  Player-team AI carry remains **0.5** because player-team carriers are immediately handed to the
  human and never run the autonomous carry routine.
- `TeammateAI` component defaults were also aligned from **3/1.8/2.5 -> 1.2/0.9/1.2**
  (chase/carry/support), so newly created swimmers cannot silently reintroduce placeholder values.

**4. Pass targeting scope decision:**
- Arrival prediction remains a real, separate behavior task. `BestPassTarget` and `Pass` still
  score/aim at a receiver's current position; there is no velocity lead or arrival-time lane check.
  It should not be solved by a tuning weight, and was intentionally kept out of this values-only
  pass. The next scoped change should predict receiver position from pass flight time and validate
  the lane at that predicted point.

**5. Holistic finding:**
- The principal feel correction is the removal of the 2.55u/s off-ball rubber-band versus the
  1.7u/s chase. Remaining visible risk to watch is a running receiver being passed behind because
  of the separate no-lead gap; do not compensate for it by changing openness/lane weights.

**Files changed:**
- `Assets/TeammateAI.cs`
- `Assets/WaterPoloAI.cs`
- `Assets/Scenes/SampleScene_PoolB.unity`
- `WATERPOLO_MASTER_PLAN.md` (this log)

**Exact Play-mode tests:**
1. **Steals:** attempt at least 20 legal front-on player steals and 20 bot steals at 1u or closer,
   with full stamina. Both should convert near 40%; test rear attempts separately and confirm both
   become exclusions. Hold sprint with the ball and confirm bot pressure is visibly riskier; test
   touch Block separately for its lower success/lower-foul feel.
2. **Shots:** from the same 4u lane, fire human tap, mid, high, and skip shots and record keeper
   saves/goals; observe bot shots from the same range. Full high/skip attempts should be powerful
   high-reward choices, while taps and bot 12u/s shots remain saveable and readable.
3. **Bot range:** set up a clear bot carrier at 3.8-4.0u from goal and confirm it can settle and
   shoot. Repeat at 4.1u with no open pass: it should drive/pass rather than non-forced shoot. A
   blocked or bad-angle 3.8u look must still pass/drive because shot quality is unchanged.
4. **Speed hierarchy:** time a bot over a long clear chase and a long recovery route: both should
   peak near 2.04u/s. Watch a near formation adjustment settle at about 1.2u/s and a bot carrier at
   about 0.9u/s. A human without the ball should sprint at 2u/s; with the ball, at 1u/s.
5. **Whole possession:** play at least two full possessions each way. Check that teams spread and
   recover without rubber-banding, loose balls are contested, bot carriers can be caught, and a
   laterally running pass receiver is explicitly watched for the documented no-lead behavior.

**Build:** `dotnet build Assembly-CSharp.csproj` completed with **0 errors** and the existing
22 obsolete-API/unused-field warnings.

---

## SESSION LOG — 2026-07-19i (horizontal swim fallback, held-ball anchor, pooled water VFX)

**Vertical swim fallback:**
- Kept both supplied `swimming_up.png` and `swimming_down.png` sheets, their slices, and the
  separate per-direction playback/size controls intact for future art replacement.
- Added the shared `PlayerFlipbookSet.useDirectionalSwimmingFrames` switch and set it to **off**.
  Both human and bot selection paths now require that switch before they can use a vertical sheet,
  so every swimming direction is presently rendered with the established horizontal `swimming.png`
  and its existing mirror logic. This is a reversible content setting, not an asset deletion.

**Held-ball placement:**
- The old moving-held-ball placement used the full diagonal velocity direction at the full forward
  distance. On left diagonals that placed the ball beyond the side/head silhouette, especially
  while moving up-left or down-left.
- Human and bot carriers now use the same visual-only diagonal correction: exact diagonals shorten
  the forward distance to **72%** and bias the offset **65%** toward the horizontal head-facing
  direction. Cardinal movement remains unchanged. The human values are Inspector controls on
  `PlayerMovement`; the matching bot logic remains synchronized in `WaterPoloAI`.

**Water VFX system:**
- Added `WaterEffectsSystem` to the existing Ball object as the one persistent scene owner. It
  creates five shared, pre-warmed built-in Particle Systems once (ripples, foam, bubbles, splashes,
  and subtle side displacement) and uses `ParticleSystem.Emit` thereafter. There is no gameplay
  instantiate/destroy path for temporary water effects and no per-frame collection allocation.
- The simple soft and ring particle textures are generated once in code rather than requiring new
  raster assets; this keeps the mobile asset/draw-call footprint compact while sharing particle
  materials. Stroke emission randomizes size, lifetime, opacity, rotation, position, and velocity.
- Swimmer wake emission is called only while the existing animator has selected its real Swimming
  state. It emits small foam, bubbles, alternating foot splashes, and restrained side waves behind
  the travel direction; it fades naturally and stops emitting when the swimmer stops or leaves that
  state. Strength/cadence respond to measured Rigidbody velocity.
- Ball paths now feed the same system: fast grounded passes/skips create a light trail, a loose
  floating ball creates an infrequent gentle ripple, landing high balls and skips create an impact
  splash/ripple, and settling loose balls create a small impact. The goalkeeper's incoming-shot
  transition emits the largest foam/splash/ripple burst. Each of the swimmer wake, side
  displacement, ball effects, and goalkeeper-dive modules has an independent Ball Inspector toggle
  plus tunable cadence/strength values.

**Files changed:**
- `Assets/WaterEffectsSystem.cs` and `.meta`
- `Assets/Scenes/SampleScene_PoolB.unity`
- `Assets/BallFlight.cs`
- `Assets/Goalkeeper.cs`
- `Assets/PlayerAnimator.cs`
- `Assets/BotAnimator.cs`
- `Assets/PlayerFlipbookSet.cs`
- `Assets/Resources/PlayerFlipbookSet.asset`
- `Assets/PlayerMovement.cs`
- `Assets/WaterPoloAI.cs`
- `WATERPOLO_MASTER_PLAN.md` (this log)

**Exact Play-mode tests:**
1. Move a player and a bot in all eight directions. Every direction must use the original
   horizontal swimming sheet and mirror behavior; neither vertical sheet should appear. Later,
   enable `Use Directional Swimming Frames` on `Resources/PlayerFlipbookSet` only after replacing
   the art to restore the already-wired vertical feature.
2. Carry the ball up-left and down-left with both a human and a bot. The ball should remain close
   to the head/leading shoulder rather than floating far to the left. Compare each cardinal route
   to confirm its prior placement is unchanged. Adjust the two `PlayerMovement` diagonal fields
   only if the human visual needs further art-specific refinement.
3. Swim at a slow pace, sprint, then stop. Foam/bubbles/side wake should strengthen with speed,
   disappear naturally after stopping, and never emit in idle, hold, throw, stun, or other states.
4. Let a loose ball settle, pass it quickly across the water, fire a strong shot/high ball, and
   leave it floating. Verify respectively: small impact ripple, restrained moving wake, stronger
   impact splash/ripple, and rare near-invisible idle ripples. No effect should loop permanently.
5. Trigger a goalkeeper response to a shot. Verify it produces the largest but still readable
   water burst. Toggle each Water Effects System module on the Ball independently and confirm only
   its named category stops.
6. Profile a crowded possession on target mobile hardware. After the one-time scene setup, verify
   no temporary water-effect GameObjects are created/destroyed and inspect GC allocations while
   swimmers and the ball are active.

**Build:** `dotnet build Assembly-CSharp.csproj` completed with **0 errors** and the existing
**22 warnings**.

---

## SESSION LOG — 2026-07-19h (directional swimming flipbooks)

This session re-read the complete master plan, then the complete current `PlayerAnimator.cs` and
`PlayerVisualRuntime.cs`, before tracing the shared `PlayerFlipbookSet` and bot presentation path.

**Directional swimming:**
- Added `swimmingUpFrames` and `swimmingDownFrames` to the shared `PlayerFlipbookSet`, wired to
  all six sliced sprites in the supplied `swimming_up.png` and `swimming_down.png` sheets.
- Both `PlayerAnimator` and `BotAnimator` now choose vertical art only while movement is genuinely
  vertical-primary: `abs(velocity.y) > abs(velocity.x)`. Positive Y selects `swimming_up` (the
  authored back/up-screen stroke); negative Y selects `swimming_down` (the authored front/down-
  screen stroke). Exact diagonals stay horizontal, so the existing left/right behavior wins ties.
- Vertical sheets are never horizontally mirrored from a stale left/right latch. Human players use
  the existing back visual body while moving up and front visual body while moving down; bots keep
  their single runtime flipbook renderer. Horizontal swimming remains the original `swimming.png`
  path with its existing renderer/body mirror logic unchanged. If either vertical frame array is
  absent or incomplete, the code safely falls back to that horizontal sheet.
- No gameplay movement, collider, palette, shader, flipbook playback timing, or state-size setting
  was changed.

**Files changed:**
- `Assets/PlayerFlipbookSet.cs`
- `Assets/Resources/PlayerFlipbookSet.asset`
- `Assets/PlayerAnimator.cs`
- `Assets/BotAnimator.cs`
- `WATERPOLO_MASTER_PLAN.md` (this log)

**Exact Play-mode tests:**
1. Move a human player straight up-screen: only the six-frame back/up `swimming_up` loop should
   render, with no horizontal mirror or old horizontal swim art.
2. Move straight down-screen: only the six-frame front/down `swimming_down` loop should render,
   unmirrored. Repeat both directions while carrying the ball and while sprinting.
3. Move straight left and right: the original horizontal `swimming.png` loop and its established
   left/right mirror behavior must be visually unchanged.
4. Move diagonally with more vertical than horizontal velocity: expect up/down art. Move at an
   exact 45-degree diagonal or with more horizontal velocity: expect the original horizontal art.
   Cross the boundary repeatedly and confirm there is no blank frame or stuck sheet.
5. Watch bots perform each of the four cardinal directions, including a loose-ball chase and a
   moving carrier. Verify the same sheet choices as the human and confirm idle, hold, throw,
   defend, steal, exclusion, and stun states remain unchanged.

**Build:** `dotnet build Assembly-CSharp.csproj` completed with **0 errors** and the existing
22 obsolete-API/unused-field warnings.

---

## SESSION LOG — 2026-07-20 (two-phase loose-ball drop ripple)

Read the full master plan and current `BallFlight.cs` before changing the effect. The existing
`UpdateSettleRipples()` trigger already exactly owned the required event: it arms only after a
LOOSE simulated ball exceeds **2.5u/s**, then fires once when that same unclaimed ball slows below
**1u/s**. The trigger remains suppressed for held, airborne, pre-bounce skip, physics-off,
goal-net, and frozen-ball states.

- **New supplied sheet wired automatically:** `Assets/Sprites/Effects/BallDropRipple.png` is already
  sliced as a 3×3 grid in row-major frame order. `BallDropRippleFrameSet` is a Resources asset that
  references those nine sprites, so `BallFlight` (which is added to the Ball at runtime) can load
  them without a scene reference or Inspector drag-and-drop step.
- **Phase 1 — impact burst:** on the existing one-time settle trigger, the effect shows frames
  **9 → 8 → 7** at 0.07s each: the large ripple visibly collapses toward the water.
- **Phase 2 — idle rest:** immediately after frame 7, it switches to a repeating **1 → 2 → 3**
  loop at 0.18s per frame. It remains visible only while the ball stays loose, simulated, slow,
  unfrozen, and in open water.
- **Hard stop:** pickup/parenting, a throw or any speed above the resting threshold, an airborne
  state, freeze, or leaving the eligible water area hides the loop in the same update. It cannot
  continue under a held or moving ball. `WaterEffectsSystem` also suppresses its older occasional
  particle idle ripple while this sheet loop is active, preventing doubled resting-ripple visuals.
- **No gameplay change:** settle thresholds and re-arm behavior are unchanged; skip, high-ball,
  landing, goalkeeper, and swimmer water effects remain on their existing paths.

**Files changed:** `Assets/BallFlight.cs`, `Assets/WaterEffectsSystem.cs`,
`Assets/BallDropRippleFrameSet.cs`, `Assets/Resources/BallDropRippleFrameSet.asset`, and this log.

**Exact Play-mode test:** (1) throw or shoot the ball into open water, let it slow unclaimed, and
confirm one quick **9 → 8 → 7** collapse plays first; it must then immediately repeat only
**1 → 2 → 3** while the ball sits. (2) Pick the ball up during the loop: the ripple must hide on
that pickup frame and never follow the carrier. (3) Repeat by throwing, passing, or nudging the
ball above 1u/s: the loop must stop at once, then may start a single new impact only after a new
fast-to-rest settle. (4) Confirm no drop ripple appears during an airborne pass/shot, goal hang,
or in the net area.

**Build:** `dotnet build Assembly-CSharp.csproj` → **0 errors** (22 pre-existing warnings).
