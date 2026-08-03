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
| `PlayerMovement.cs` | Human control of the active player: move, grab (E), **charged shoot** (hold Space; time-based `shotChargeTime` 0.7s, min-speed floor so a tap never drops), aim chevron + **power bar** (world-unit `powerBarWidth` 1.2 — >2× the keeper bar, grows left→right), **directional charged pass** (hold B — fires where the facing triangle/joystick points with a tunable `passAssist`, NOT auto-homed; `FindPassAssistTarget` scores teammates by dot with `lastDirection`). **Shot height** (`shotHeight` 0..1, charges in lock-step with power: low 0–0.3 / mid / high 0.7–1; read by Goalkeeper + GoalkeeperAnimator for the dive tier; **charge >0.7 releases as the untouchable ASYMMETRIC shot arc** landing 1.5u before the aimed goal line — `HighShotLandPoint`, raw aim, no assist). **Shots ×1.35 code-side speed** (`ShotSpeedMult` — shots always outpace passes; serialized `maxShootPower` 12 untouched). **Skip shot** (hold Q while charging Space → fast LOW bounce shot via `BallFlight`). **Every B pass arcs** (`ArcKind.Pass` small hop to the assist target, else a charge-scaled 3.5–6.5u spot along the aim); **F+B = the big high LOB** (`ArcKind.Lob`, ×0.7 speed) — both untouchable mid-flight (nobody intercepts an airborne ball; contests happen at the landing). **Charge bar reads shot-vs-pass:** pass = cool blue→cyan; shot = green→yellow→red strobing white past 0.7 (the high-shot zone). Ball held via **parenting**; reports possession to MatchContext. `TakeOverHeldBall()` for clean control transfer; `TouchBlockSteal()` (Block button — half steal chance, 50% foul-on-miss). **Stamina hooks** (`StaminaSpeedMult`/`StaminaSprintMult`/`StaminaSprintBlocked`/`StaminaStealMult`/`StaminaPercent01`, neutral 1 by default). |
| `TeammateAI.cs` | Thin component on each player. When NOT human-controlled, runs the shared `WaterPoloBrain`. Implements `IAgentBody`. |
| `BotMovement.cs` | Thin component on each bot. Always runs `WaterPoloBrain`. Implements `IAgentBody`. |
| `WaterPoloAI.cs` | **The shared brain** + `IAgentBody` interface. All AI decisions live here once: carrier (shoot/pass/**drive**/dribble), support (get open), presser (nearest chases), defender (hold shape). 🟡 New: **drives** (beaten marker + clear lane → burst to 2m, shoot/kick-out/abort) and **picks/screens** (nominated screener plants on the carrier's marker; rubbing past = short "beaten" boost). Works, needs tuning. **This is C# state-machine AI — NOT an LLM.** |
| `TeamSide.cs` | One per team. Holds goals + roster (`members`), formation math (auto-spreads ANY number of players), passing/positioning logic, **attacking-spacing + tactics tunables (center-feed, counter, shot-quality threshold, free-throw clearance), shot-quality + pass-risk scoring, and 4 defense modes — Press/Zone/Drop/MPress — incl. man-up 4-2 umbrella + man-down zone shapes**. 🟡 New: **dynamic Centre** (fights for inside water goal-side of its guard at 2m), wider lanes + weak-side wing drift, receiver-shot-quality pass bonus, drive/screen helpers (`DrivePoint`, `GetScreenSpot`, `FindScreenerForCarrier`), and **bot adaptive defense** (`EvaluateDefenseMode`, auto-detected `isAI`: Drop when man-down / protecting a late lead / Centre conceded 2+; Press otherwise). Scales 2v2 → 6v6 with no code change. |
| `MatchContext.cs` | Singleton "match truth": ball position, possession + last toucher (`NoteTouch` for deflections), post-release grab cooldown (`releaseGrabDelay` 0.5s), freeze flag, shot-clock grab-ban, kickoff-pass flag, **free-throw state, keeper-hold flag, counterattack window, player goal-line clamp (`playerLimitX`)**, halftime `SwapEnds()`, `GiveBallTo()` / `ForceDropHeldBall()`, `EnemyOf()`, **`IsProtectedKeeper(carrier)`** (the keeper-steal safe-zone rule — true while a keeper carries the ball inside its safe zone, Task 5). |
| `TeamManager.cs` | On `GameManager`. Auto-switches control to the ball-holder after `autoSwitchDelay` (0.5s — so you keep control to chase your own loose ball); manual **C** / touch SWITCH (skips excluded); **Z** cycles defense (Press/Zone/Drop/MPress); never auto-activates excluded players. Exposes static **`ActivePlayer`** + **`ActivePlayerIndex`** (read by `CameraFollow` and the stamina HUD). |
| `Goalkeeper.cs` | Kinematic keeper sliding along its physical goal line tracking ball Y (stays on its goal after the halftime swap). **Save % system:** a fast shot reaching its hands rolls `baseSaveChance` 0.65 minus penalties for HIGH (height >0.7), POWER (>9 u/s) and SKIP shots, plus a stamina penalty when tired; a slow ball is auto-collected. **Snatch:** an enemy carrier within `keeperSnatchDistance` (0.8u) is stripped with 100% success, no roll (`TrySnatchFromCarrier`; respects free throws, not vs another keeper). **Player keeper = full control:** while your own keeper holds the ball it plays like a field swimmer — free **2D movement** at `keeperMoveSpeed` (4), sprint, a charged shot fired in the **joystick/aim** direction (never auto-aimed at goal), and a **directional pass** (`FindKeeperPassTarget` scores ALL teammates by dot(aim,dir)−dist×0.05, no cone; reads the live `TouchControls.Instance` joystick, else `lastDir`). **No auto-pass** — fully manual; it SWIMS back to its line (never teleports) after you shoot/pass. **Safe zone (Task 5):** within `KeeperSafeZoneRadius` (1.5u) of the goal line the carrying keeper is unstealable; carry it OUTSIDE and `keeperLeftSafeZone` latches → enemies steal normally (exposed via `MatchContext.IsProtectedKeeper`; `OnBallStolen()` clears the hold on a successful strip). **Organic idle** (not holding, ball far): small random X drift 0.1–0.3u every 2–4s (≤0.4u off the line) + a subtle ±0.05u Y sine micro-bob. **Bot keeper** auto-distributes after `keeperHoldSeconds` (0.8s) OR immediately if crowded within `keeperPanicDistance` (2.5u) — UNCHANGED. **Stamina-aware** (tired = worse saves, no sprint at 0%). A keeper hold is NOT a possession change — the shot clock keeps ticking until the pass-out. **Distribution arcs (2026-07-04b):** `PassOut` throws the same untouchable BallFlight arc as every other pass (`ArcKind.Pass`; the forced DEEP outlet = the big `Lob`); point-blank falls back flat. **Freeze gate:** the keeper now fully freezes during `PlayFrozen` like every swimmer (needed so it can't fish the dead ball out of its own net during the goal hang-time). |
| `Goal.cs` | Trigger on each net; reports `goalSide` ("Left"/"Right") + its own transform (for the net-pulse reaction) to ScoreManager. |
| `ScoreManager.cs` | Team-based score (credits the team attacking that net → survives the halftime swap) shown on **separate `playerScoreText` + `botScoreText`** TMP fields; **ignores held-ball goals**; exposes `HomeScore`/`AwayScore` (read by the camera's goal-shake). **FRAME-ACCURACY GATE (2026-07-04b):** touching the goal trigger is NOT a goal — the ball's real velocity is projected onto the goal line and the crossing must land inside the mouth (|y| ≤ 1.5, moving INTO the net; consts mirror GoalLineOut) → skims/corner-clips/sideways drifts no longer score, badly-aimed shots miss. **NET REACTION:** on every goal the net sprite gets a damped-spring squash/bulge pulse (0.45s, scale + outward nudge, originals restored) + an expanding white impact ring at the ball. **Goal restart (NOT a quarter start → NO sprint duel):** 5 phases — (0) **HANG TIME (`goalHangSeconds` 3.5, NEW):** play freezes THE INSTANT the ball hits the net; ball stays IN the net (velocity cut ×0.15, fully stopped after 0.15s), everyone holds position, camera keeps following the action + goal shake — the reset only starts after this hold; (1) ball loose at exact (0,0), touch UI hidden + `ctx.ResetBallTouch()` (camera → overview), a `goalFreezeSeconds` (1s) celebration; (2) both teams snap into the **natural restart spread** (`TeamSide.SnapToRestartFormation(hasBall)`), the **conceding team** is given the ball at exact centre (`ctx.GiveBallTo`) + `ctx.ResetBallTouch()` again; (3) a **`postGoalPauseSeconds` (3s) silent pause**; (4) `Unfreeze` + `SetKickoffPass(conceding)` + `ctx.MarkBallTouched()` + restore UI + reset shot clock. |
| `MatchTimer.cs` | Quarters (90s) + win/lose/draw; pauses during freezes; sprint duel each quarter; halftime swap. Full time submits the real score once through `MatchPresentationContext`, then runs reward-slot/mission/ranking/pass hooks and `MatchResultUI.Show()`. `ForfeitMatch()` also consumes a championship fixture with a forced one-goal winner if the live score does not match the forced outcome. |
| `ShotClock.cs` | 30s per-possession clock (singleton): resets on possession change / goal / defensive exclusion; turnover + grab-ban at 0; pauses when frozen, **during a free throw**, or match over; **a keeper hold does NOT reset it (keeps ticking until the keeper distributes)**. |
| `ExclusionManager.cs` | Fouls + exclusions (singleton): failed steal = foul → **free throw** to the fouled team; 2 fouls in 10s → 5s exclusion (roster slot nulled → AI auto-adapts) **or a PENALTY if the victim was in the 2m zone**; 3rd → removal; forfeit < 4 players; HUD countdowns. 🟡 New: **virtual foul** when the victim is an inside-water Centre (Centres draw exclusions/penalties faster; toggle `centerFoulBoost` — may be too hot, watch in testing). |
| `SprintDuel.cs` | Quarter-start duel (singleton), fully rebuilt. Builds its OWN screen-space UI in code (no wiring): a big centred **"5 → 4 → 3 → 2 → 1 → GO!" countdown** (1s each, scale-pulse per number; `countdownStart` 5) + a "TAP SPACE / TAP SPRINT FOR SPEED" hint, then a tall **vertical SPEED bar on the left** (red→orange→green, fills with the human's speed) under a pulsing "TAP FASTER!". Ball is pinned to EXACT (0,0,0) with physics OFF during the countdown, goes live at GO. At GO! the two sprinters race (bot fixed speed; human base speed + each **Space / LeftShift tap OR a tap anywhere on screen** boosts toward a cap, decays) AND **every other swimmer immediately jogs into formation at ~60% speed** (`formationMoveSpeed`, both teams alike — `RestartFormationSpot`, position-based so it ignores the freeze; no statues, no waiting for possession). The designated sprinter starts slightly ahead of its line (`sprinterForwardOffset`) so it's clearly the sprinter, not the keeper, and is made the **active player**. Runs at **quarter starts ONLY** (Q1 via `MatchTimer.Start`, Q2–Q4 via `AdvanceToNextQuarter`) — **never after goals/penalties/turnovers** (a goal restart is a separate, duel-free system in `ScoreManager`). `StartDuel` calls `ctx.ResetBallTouch()` so the camera holds the wide overview until a sprinter grabs. First within grabDistance wins → grabs → un-freeze → kickoff pass; the rest transition straight into normal AI from wherever they jogged to. **Hides the gameplay touch UI** (`TouchControls.SetGameplayVisible(false)`) for the duel's duration and restores it on finish. The TAP-for-speed mechanic lives ONLY here — regular play is hold-to-sprint. |
| `EventFeed.cs` | Rolling last-5 event log (singleton): goals, exclusions, turnovers, out-of-bounds, forfeit, halftime. |
| `BallOutOfBounds.cs` | Top/bottom-wall out rule: a loose ball at the edge → possession to the nearest player of the team that didn't touch it last. |
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
| `MainMenuUI.cs` | MainMenu scene. Builds the whole launch screen in code at runtime: canvas (1280x720), background from `Assets/Resources/Sprites/`, and polished **Log In** (bright blue) / **Play as a Guest** (dark blue) entry buttons with hover scale and 1s fade-in. Firebase is not integrated yet, so both buttons intentionally use the same local-profile route → **HubScene**. |
| `NavigationManager.cs` | HubScene. The whole hub built in code. **Top bar:** left = profile cluster (circular avatar tinted club-primary with the club CREST as its glyph + country "flag" dot + club name/XP/level — avatar OR name opens the My Club screen; settings/inbox/gifts icon buttons with real art (`settings/message/gifts-button.png`, 42px group at 95px pitch; labels are hover / 0.4s press-hold **tooltips** with a dark pill backing via the nested `IconTooltip` — no permanent captions) → stub settings panel + COMING SOON overlays; **FREE +100** watch-ad pill at x 660, 3/day via `AdWatchCap`), right = live gold/diamond with [+]. **Left column:** RANKING (coming soon) / SHOP (`ShopUI` overlay) / TEAM (`TeamScreenUI`) — 135/140/135px, rows at yOff −40 ± 140. **Right column:** FRIENDS 135 / CLUBS 150 (bigger box: its trimmed art is ~1.8:1 wide), same rows/offset as the left column → COMING SOON stubs (no online backend yet). ALL hub button art loads via the nested **`LoadTrimmedSprite`** (alpha-trim, needs `isReadable: 1` metas) because the source PNGs carry 10-60% transparent margins. **Bottom bar:** season pass (locked) + missions + **4 live post-match reward slots** (state from `PostMatchRewardManager`; pitch 84; Ready/Unlocking slots scale 1.18x + get `PackCardFX` float/shine; a slot filled by the last match scale-ins with overshoot on hub load via `ConsumeNewRewardSlot`) + PLAY → Game Mode overlay. Also hosts: Game Mode / Standings / Pre-Match / Club-customization overlays, the reward-slot unlock popup (odds table via shared `PackInfoPopup`), and `RefreshClubProfile()` (re-reads `RosterManager.Club` into the cluster). |
| `PlayerData.cs` | **(Player data foundation, NEW)** ScriptableObject = one player CARD: `id`, `fullName`, `nation`, `position` (enum GK/CB/LW/RW/CF/LF/RF — enum order == starter-slot order), `overall` 0–100, a `Stats` struct (speed/shooting/passing/defense/stamina/goalKeeping 0–100), `rarity` (Common/Rare/Legendary → `RarityColor`), `portrait` (Sprite, null for now → UI draws a silhouette), `priceGold`, `isBot`. `[CreateAssetMenu]` (Create → Water Polo/Player). Static `ComputeOverall(stats,pos)` (GK leans on goalkeeping, field = outfield avg) shared by the generator + UpgradePlayer; `Clone()` so owned cards are mutated as runtime copies, never the source asset. PURELY data — never touched by the match. |
| `PlayerDatabase.cs` | **(NEW)** Read-only player CATALOG: lazy C# singleton that `Resources.LoadAll`s every `PlayerData` under `Resources/Players/` into a dict by id (`Get`/`Has`/`AllPlayers`/`FirstOfPosition`/`Count`). No scene object. |
| `Roster.cs` | `[Serializable]` save payload: `List<string> ownedPlayerIds`, `string[7] starterSlots` (0=GK, 1–6 field by position), `int coins`, `int diamonds`, plus **`ClubProfile club`** (clubName / logoId / primaryColorHex / secondaryColorHex / countryId — the My Club identity). IDs only → tiny JSON. |
| `RosterManager.cs` | Self-bootstrapping singleton MonoBehaviour (DontDestroyOnLoad, no wiring). Loads/saves `Roster` as JSON in `Application.persistentDataPath/roster.json` (guest-mode, no Firebase); seeds a default 7 + bench + coins/diamonds on first run (self-heals if the catalog was empty then). Owned cards held as `Clone()`s so upgrades never corrupt the source asset. API: `BuyPlayer`/`SellPlayer`/`UpgradePlayer`/`SetStarter(slot,id)`/`GetOwnedPlayers`/`GetStarters`/`TeamOverall`; **`Club` + `SaveClub()`** (the ClubProfile; generates a one-time "Guest_XXXXXX" name on first load); auto-saves after every mutation. (Upgrades are in-session only — Roster stores ids only; extend later.) |
| `TeamScreenUI.cs` | **(NEW)** The REAL hub Team screen (B12), built in code in NavigationManager's style (no prefabs/wiring; NavigationManager attaches it + passes itself). Live 2-3-2 formation of the 7 starters, a scrollable owned-bench + buyable-market list, team OVR + gold/diamonds, and working **BUY / SELL / UPGRADE / START** buttons → `RosterManager` (each refreshes the screen + the top-bar currency). Each card: rarity-coloured border (grey/blue/gold) + name/OVR/position + silhouette (or `portrait`). |
| `SamplePlayerGenerator.cs` | **(NEW, Editor — `Assets/Editor/`)** **Tools → Generate Sample Players**: writes 21 sample `PlayerData` assets to `Resources/Players/` (all 7 positions, mixed rarities/ratings/prices; deterministic → idempotent). Run once so the Team screen has data. |
| `MatchResultUI.cs` | Full-time/forfeit result screen, built in code. Championship scores use the real two club names and CONTINUE returns to HubScene/its persistent competition; casual CONTINUE reloads PoolB. Colored outcome line + MAIN MENU; 0.5s unscaled fade (timeScale is 0). Singleton. |
| `QuarterBreakUI.cs` | Between-quarters pause screen (built in code, **self-bootstrapping** via `Get()` — no scene object needed). `MatchTimer` raises it when a quarter ends (but the match isn't over): dimmed overlay + centred dark panel with **"QUARTER N COMPLETE"**, the score, and **RESUME** (→ next quarter's sprint duel) / **QUIT** (→ MainMenu if present, else stop play). Play freezes via `MatchContext.FreezeAll` until RESUME. Singleton. |
| `PauseMenuUI.cs` | Pause system, built in code: pause button → `Time.timeScale = 0` + PAUSED / RESUME / QUIT / TEAM MANAGEMENT. QUIT confirms that the match counts as a loss; YES QUIT now calls the championship-aware `MatchTimer.ForfeitMatch(false)` before loading HubScene, so the fixture and simulated round advance. TEAM MANAGEMENT is still a placeholder. |
| `CameraFollow.cs` | **FIFA-style follow camera** on **Main Camera** — self-contained, no Inspector wiring (pulls `TeamManager.ActivePlayer` + `MatchContext`). **Start/post-goal overview (Task 1):** until the ball is first touched after any reset (game start, after a goal, between quarters — `MatchContext.BallTouchedSinceReset`) it holds the wide pool overview centred on (0,0) at **maxSize 5.0**, no following; the first grab eases it smoothly into the normal follow. Tracks a weighted point between the active player (60%) and the ball (40%) — 70/30 when the ball is loose — via `SmoothDamp` (speeds up to `switchSpeed` 8 for 0.5s on a player switch). **Dynamic orthographic zoom** (`Mathf.Lerp`): 4.2 base → 5.0 (player/ball far) → 4.5 (`SprintHeld`) → 3.8 (you control the keeper). HARD pool-boundary clamps on the camera centre (X ±5.5, Y ±3.2); Z locked −10. **Screen shake** (additive): goal 0.15/0.4s (polls `ScoreManager` total), powerful shot (ball >10 u/s) 0.05/0.15s. Managers missing → parks at (0,0,−10) size 5, no errors. All tunables serialized. |
| `StaminaSystem.cs` | FIFA-style stamina on every field swimmer + keeper. **Auto-installs at runtime** (`RuntimeInitializeOnLoadMethod`) onto any `PlayerMovement`/`IAgentBody`/`Goalkeeper` lacking one → 14 objects (6 players, 6 bots, 2 keepers), zero wiring (the 2 keepers keep a hand-tuned copy). **Field drain/recovery per sec:** idle +8% (×2 after 5s rest), swim −3%, hold+move −5%, sprint −12% (−18% after 3s fatigue), excluded +15%; **second wind** at 0% (ease off sprint 2s → +15% burst). **Effects:** <40% speed ×0.8; <20% speed ×0.6 + steal ×0.8; 0% sprint disabled. **Keeper:** track −2%, hold −1%, idle +10%; tired = worse saves, no sprint at 0%. Writes only neutral hooks (deleting it leaves the game identical); HUD lives in `TouchControls`. |
| `BallFlight.cs` | Ball VFX + **the airborne-arc system**, **auto-added to the Ball at runtime** by `PlayerMovement` (no wiring), singleton. **ALL passes and HIGH shots fly as arcs** (`LaunchHighBall(landPos, speed, height01, ArcKind)`): the rigidbody flies a straight zero-damping constant-speed line with **colliders OFF** (players/keepers/walls/goal trigger can't touch it) while a sprite copy (`BallAirSprite`, sorted over swimmers) rides the height curve above a shrinking oval water shadow; the root sprite hides mid-flight. **Untouchable mid-air:** `MatchContext.BallGrabbable` is false while `HighBallActive` — grabs/steals/keeper saves all wait for the landing (exact at landPos; landings clamped into open water; overlapped swimmers collision-ignored until separated). **Three ArcKinds:** `Pass` (B / every bot pass — small quick SYMMETRIC hop, peak ≈ dist×0.055 clamped 0.18–0.5, swell 1.08, no spin), `Lob` (F+B / bot long-or-blocked ball — the big floaty parabola, peak ≈ dist×0.14 clamped 0.45–1.25, swell 1.2), `Shot` (charge >0.7 — **ASYMMETRIC** hand-built curve: easeOutQuad rise into a peak at 35% of the flight, easeInQuad fall that hangs near the top then drops; peak ≈ dist×0.10 clamped 0.35–0.9, swell 1.15; glows + keeps FULL speed on landing — passes land with a 25% roll). **Release SNAP (shots only, incl. bot/keeper):** raw un-eased squash 0.84 → pop 1.12 → settle over 0.12s at the instant of release. Plus: speed-gated **TrailRenderer** (>5 u/s, suppressed mid-arc); **flat point-blank high-shot** swell+glow fallback; **skip-shot** bounce 1.5u before the goal (Y jitter, squash + water ripple, 35% `KeeperFooled`); **spin** (shots 54°/s, fast loose 18°/s, arcs 9°/s — none on skip or any Pass, only >6 u/s, snaps upright on catch). All scaling uniform, recomputed from a clean base each frame. Exposes `ShotHeight`, `SkipActive`/`SkipBounced`, `HighBallActive`, `KeeperFooled`. |
| `GoalColliderFixer.cs` | Editor tool (**Tools → Fix Goal Colliders**). Resizes GoalRight/GoalLeft Box Collider 2D to the visual goal mouth (size (4,15) → world ≈0.8×3.0u at scale 0.2). Idempotent; marks the scene dirty (Ctrl+S to save). |
| `PlayerLabel.cs` | ⬜ **NOT YET BUILT** (planned). Future: world-space player-number labels floating above each swimmer. |
| `LeagueSeason.cs` | Durable offline championship domain. One JSON-saved run per competition; the player's saved **My Club** is automatically injected into Group A with nine fixed AI clubs (no official-club picker); five-round schedule with one bye per club whose round/bye order is shuffled once per fresh run; seeded deterministic simulation; real player result + every other fixture in both groups; top-2 cross-group semifinals; simulated 5th/7th/9th and third-place matches; unique 1–10 final order. Exact top-3 Gold/Diamond rewards are granted once; 1st unlocks only the next tier. A win-gated mid-run restart resets only that competition while preserving currencies/unlocks. |
| `ClubCatalog.cs` | Offline Resources ScriptableObject: 34 club IDs/display names/strength levels/tightly cropped direct logo Sprite references plus four trophies and three medals. Runtime lookups never scan AssetDatabase or require internet. |
| `MatchPresentationContext.cs` | Persisted pending-fixture handoff between HubScene and PoolB. Validates competition, My Club, and the exact next opponent before submitting one score. `ChampionshipHudBinder` replaces championship You/Bot names, adds large layered My Club/opponent crests beside the timer/name HUD, auto-sizes long names, and preserves casual fallbacks. |
| `ClubCatalogBuilder.cs` | Editor-only revisioned auto-builder/validator (`Assets/Editor`; Tools → Water Polo → Rebuild Club Catalog). Finds all supplied club/trophy/medal Sprite assets, alpha-crops each club image into a generated catalogue subasset (source art remains untouched), and serializes direct offline references into `Assets/Resources/ClubCatalog.asset`; explicit `Stu-Bucha` → `Stua-Bucha` filename alias. |
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
| `ClubCustomizationUI.cs` | The code-built My Club screen hosted by NavigationManager. It has the 20-template tintable crest browser (18 currently valid), three 14-colour crest palettes, player cap/swimwear colours, nine-character name, and a full **36-country flag selector**: inline `< Country >` arrows plus a `v` opener into a scrollable modal; the active country has a green check and a new selection saves immediately. Crest/name changes still save through the existing `RosterManager.Club` / `roster.json` contract. |

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
- `MatchTimer`: **Score Manager = ScoreManager, Timer Text = TimerText, Quarter Text = QuarterText, Result Text = ResultText**, Quarter Length 90, Total Quarters 4.
- `ShotClock`: **Match Timer = (this GameManager's MatchTimer), Shot Clock Text = ShotClockText**, Shot Clock Seconds 30.
- `EventFeed`: **Feed Text = EventFeedText, Match Timer = MatchTimer**, Max Lines 5.
- `SprintDuel`: no required refs (pulls teams/ball from MatchContext); optional **Duel Text**; speed/timing tunables.
- `BallOutOfBounds`: no refs (pulls from MatchContext); Out Y Threshold 4.2, Reentry Inset 0.5.
- `PenaltyManager`: optional **Penalty Text = PenaltyText**; Penalty Spot X 2.47, Behind Spot Margin 1, Penalty Aim Cone 70, AI Shoot Delay 1, AI Miss Chance 0.25, AI Miss Offset 1.6, Penalty Shot Speed 13, Max Penalty Seconds 6.
- `GoalLineOut`: no refs (pulls from MatchContext); Goal Line X 7, Goal Mouth Half Height 1.5, Reentry Inset 0.5, Carrier Out X 6.7, Corner Inset X 6.2, Corner Y 3.5.

**Other manager objects (empty GameObjects)**
- **PlayerTeam** — `TeamSide`: Name "Player", **Attack Goal = GoalRight, Defend Goal = GoalLeft**, **Members = [Player1..6]**, formation + AI tunables, plus **attacking-spacing** (Teammate Spacing 2, Support Pass Range 5, Support Blend 0.5, Pass Openness Weight 1.5) and **tactics** (Center Feed Weight 3, Counter Runners 2, Drop Sag 0.5, Shot Quality Threshold 0.30, Free Throw Clearance 2.2) fields. (Defense mode is runtime-only, defaults Press.)
- **BotTeam** — `TeamSide`: Name "Bot", **Attack Goal = GoalLeft, Defend Goal = GoalRight**, **Members = [Bot1..6]**.
- **ScoreManager** — `ScoreManager`: **Ball = Ball, Player Score Text = PlayerScoreText, Bot Score Text = BotScoreText, Player Team = PlayerTeam, Bot Team = BotTeam**, Goal Freeze Seconds 1.
- **ExclusionManager** — `ExclusionManager`: **Match Timer = MatchTimer, Exclusion Text = ExclusionText**; Foul Window 10, Fouls For Exclusion 2, Exclusion 5, Max Exclusions 3, Min Players 4, Foul Steal Lockout 1.5, Penalty Zone X 4.28.

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
- ✅ **DONE (updated 2026-08-03):** `MainMenu` scene with `MainMenuUI.cs` — full-screen code-built launch screen with polished **Log In** and **Play as a Guest** choices. Both intentionally bypass authentication and load **HubScene** until Firebase is integrated.
- ✅ **DONE (June 2026, shell only):** `HubScene` + `NavigationManager.cs` — full navigation shell for B6–B15: persistent top bar (logo placeholder, team name, gold/diamond displays, settings gear stub) + bottom nav with 5 tabs, Career/Team/Transfers/My Club/Challenges placeholder screens, 0.3s fades. **All numbers are hardcoded placeholders; no economy, saving, or real data.**
- **Still future (the full vision):**
- **Top horizontal tab (always visible):** Settings icon + social link icon; Claim Rewards; Diamond currency (diamond + cyan bg + number); Gold currency (coin + number); Club logo + Team name.
- **Large buttons:** Career; Live ("Coming Soon", inactive).
- **Smaller buttons:** TEAM, TRANSFERS, My Club, Challenges.

## B7. Settings Screen 🟡 PARTIAL (hub gear now has a working persisted English/Georgian/Russian language selector; sound/account/info options remain future)
- Top tab stays; content area swaps; back arrow appears.
- Options: (1) Language `< >` instant — English/Russian/Georgian (+more). (2) Bot difficulty `< >` — Medium/Hard, default Medium. (3) Account — Log In/Out/Sign Up/Delete (Apple or Google Play); progress saved & synced across devices (Firebase planned). (4) Info links — FAQs, Legal Notices, ToS, System Info (external links).

## B8. Claim Rewards ✅ MOSTLY DONE (2026-07: working CLAIM flows in Missions + Season Pass track + reward slots; all grants through RosterManager)
- Popup: Season Pass + Activate Pass. Split horizontally: top = premium (pass) rewards, bottom = free. Rewards = coins/diamonds/items from wins/goals.

## B9. Currencies 🟡 MOSTLY DONE (gold/diamonds LIVE from RosterManager everywhere; earned/spent via shop, packs, rewards, ads; IAP still stubbed through IAPBridge)
> Foundation: **Player System Architecture** (end of Part A) — coins/diamonds stored in local JSON, synced to Firebase on login; payments require login, ads don't.
- **Diamond:** icon + cyan bg + number; rare; buy high-rated random players / upgrade when gold short.
- **Gold:** coin + number; buy normal/good players, upgrade pool, upgrade players, buy caps/swimwear.
- Both have **+** → shop popup (real-money items/players via Apple/Google billing); purchase adds item to game.

## B10. Club Logo / Team Name Popup 🟡 MOSTLY DONE (`ClubCustomizationUI`: template crest/three colours/player colours/name plus all 36 real country flags; country changes save immediately and the hub cluster shows the real selected flag. Records/highlights remain future.)
- Manager standing, large club logo, overall team rating, changeable nationality flag, **Highlights** (saved goals), **Records** (games, W/L/D, goals for/against, biggest win/loss, win %, trophies).

## B11. Career / Championship Screen ✅ CORE COMPLETE (2026-07-26)

The hub PLAY button opens four club competitions. Each competition contains the player's saved
**My Club** plus nine fixed real AI clubs, split into two fixed groups of five. My Club is always
the human-controlled entry; the player never selects or takes ownership of an official club.
A championship run is saved to
`Application.persistentDataPath/championships.json`; switching scenes, reopening a competition, or
restarting the app does not redraw clubs, fixtures, simulated scores, standings, or the PRNG state.

| Competition | Teams | Format | Unlock |
|---|---:|---|---|
| Division 1 | 10 | 2 groups of 5 → semifinals → placement matches → final | Always open |
| Premium League | 10 | same | Finish 1st in Division 1 |
| Continental Cup | 10 | same | Finish 1st in Premium League |
| World Champions League | 10 | same | Finish 1st in Continental Cup |

Fixed groups:

| Competition | Group A | Group B |
|---|---|---|
| Division 1 | **My Club**, Arenna, Didi-Orod, Ineri, Locomoco | Tbili, Astinna, Dinamo, Poseidon, Alnguard |
| Premium League | **My Club**, Aurelio-Posillipo, Barcelona, Mularis-Dubonic, Piranias | Randolla, Red-Star, Spartakus, Stu-Bucha, Apollon |
| Continental Cup | **My Club**, Dabrovnik, Marselo, Matador, mlodest | Prianik, Radni, Saas-Planka, Vipa-Pospo, Crab |
| World Champions League | **My Club**, Jordani, New-Grand, Olimpi, Pru-Rico | Sebedel, WP-Lions, WTC, Crab, Matador |

Across the four nine-AI rosters, all 34 supplied official clubs appear. `Crab` and `Matador` are the
only deliberate repeats. The progression is strength-based: Division 1 begins with low/low-mid AI,
Premium introduces mid clubs, Continental is mid-top/top, and World Champions is top-heavy.

Run lifecycle:

1. An unlocked competition with no run shows its trophy, medals, exact rewards, both club lists, and
   **Start Championship**. A large identity panel states that the saved My Club enters
   automatically. Pressing Start creates a fresh run with that exact saved club name/crest and the
   nine listed AI rivals; there is no club picker. AI clubs use the shared PoolB Bot objects/players.
2. Each five-club group uses a five-round circle schedule with one bye per club. Every fresh or
   restarted run shuffles the round/opponent/bye calendar once; the resulting fixtures are then
   saved and remain fixed. Every club still plays the other four exactly once. Completing the player's real PoolB fixture records its actual score and
   simulates every other fixture in both groups for that round. A player bye becomes an honest
   **Simulate Bye Round** action. The Group Stage view shows logos, P/W/D/L/GD/PTS, and the latest
   round's four scores plus both byes.
3. Top two from each group qualify: A1–B2 and B1–A2 semifinals. A3–B3, A4–B4, and A5–B5 are simulated
   for 5th/6th, 7th/8th, and 9th/10th. The other semifinal is simulated with the player's semifinal.
   The third-place match is simulated before the player final. A player eliminated in groups or a
   semifinal never has to play a placement match; the remaining tournament safely simulates.
4. Completion always creates one unique 1st–10th order. The final screen shows every club vertically
   with large layered logo/name rows, all reward tiles, the player's exact earned Gold/Diamonds, promotion
   status, and **Play Championship Again**. Replay removes only that completed run, retains all
   unlocks/rewards, and immediately starts a fresh championship with My Club and reset fixtures.
5. During an active run the bottom bar always shows restart state. It remains disabled as
   **RESTART (WIN 1)** until My Club has won at least one match. It then becomes **RESTART RUN** and
   opens a confirmation panel. Confirming resets only that competition to zero matches and draws a
   fresh fixture calendar; Gold, Diamonds, unlocks, previous rewards, and other competitions remain.

Rewards and promotion:

| Competition | 1st | 2nd | 3rd |
|---|---|---|---|
| Division 1 | 3,000 Gold + 30 Diamonds + Premium unlock | 2,000 + 20 | 1,000 + 10 |
| Premium League | 5,000 + 50 + Continental unlock | 3,500 + 35 | 2,000 + 20 |
| Continental Cup | 8,000 + 80 + World Champions unlock | 5,000 + 50 | 3,000 + 30 |
| World Champions League | 15,000 + 150 | 8,000 + 80 | 5,000 + 50 |

Rewards are idempotent (`rewardsGranted` is saved), so reloading the end screen cannot duplicate
currency. Only 1st unlocks the next competition; won/current competitions remain replayable.
PlayerPrefs compatibility keys remain `div1_won`, `pl_won`, and `cc_won`.

Locked cards are clickable. Their information-only screen shows the lock reason, trophy, all medal
and reward rows, and both fixed logo lists, but no standings, pairings, fixtures, or Start button.
A stale saved run can never bypass a currently locked gate.

Club art/data:

- `ClubCatalog` is an offline-first Resources ScriptableObject with 34 club IDs, level, and direct
  Sprite references. `ClubCatalogBuilder` is revisioned: on editor import it rebuilds an outdated
  catalogue and bakes tightly alpha-cropped Texture/Sprite subassets for every crest. This removes
  inconsistent transparent margins without changing the supplied source files. It also exposes
  **Tools → Water Polo → Rebuild Club Catalog**. `Stu-Bucha` intentionally maps to the supplied
  `Stua-Bucha.png`; `Poseidon.jpeg` is supported. All supplied source sprites are Single mode.
- Trophy mapping: Division1, Premier-League, Continental-Cup, Champions-League. Medal mapping:
  Gold-Medal, Silver-Medal, Bronze-Medal.
- Every displayed crest uses a compact shadow/rim/white-plate stack. The tightly cropped crest is
  deliberately larger than its close-fitting white plate, so transparent art remains readable
  without the previous oversized empty white circle. A two-letter fallback prevents an empty badge.
- Club names/logos, competition definitions, fallback Bot data, and art stay bundled for offline play.
  Firebase is a later optional, versioned patch/cache layer for player stats, club statistics/win
  rate, and balance values. Remote logos should only supplement a bundled/cached fallback.

Match handoff:

- `MatchPresentationContext` persists the pending competition/club/opponent identity, validates that
  it still matches the season's exact next fixture, and submits one real final score.
- PoolB shows My Club and the real opponent names/logos instead of hardcoded You/Bot for championships. The
  missing player score label self-heals from the Bot score label and mirrors itself using the live
  club-name positions, so no coordinates or extra Inspector wiring are required. Long HUD names
  auto-size; the quarter-break and replay score overlays also use the real fixture identities, and
  the quarter-break panel displays both crests.
- The pre-match real club panels slide horizontally from opposite sides and settle around `VS`
  using unscaled time. Full-time and forfeit results use the same real club names.
- Normal full time, exclusion forfeits, and pause-menu **YES QUIT** all consume the pending fixture.
  Quitting/forfeiting stores a forced one-goal loss/win margin when needed, so a run cannot become
  stuck on the same match.

Still later: a separate post-championship Cup needs its own entrants, bracket, rewards, unlock rules,
and UI specification. Firebase integration, real per-club player rosters/stats, remote club records,
and alternate competition-specific pool art are also intentionally deferred.

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

### B16.1 Pre-Match Intro ✅ FIXTURE BEAT DONE; player warm-up cinematic later
- Championship pre-match: both real logo/name panels slide horizontally from opposite sides and
  settle around a centred VS in 0.6s unscaled time. A longer player-entry/warm-up cinematic remains optional.

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

### B16.6 HUD 🟡 MOSTLY DONE (championship names/logos + split score + score-tab art + stamina + pause done; broader layout polish later)
- Split score (PlayerScoreText/BotScoreText) on a `score-tab.png` board ✅; the absent serialized
  PlayerScoreText is rebuilt/mirrored at runtime ✅; championship club names + white-backed crests
  replace You/Bot beside the timer ✅; quarter indicator; match timer; **stamina HUD** (P#/GK + bar,
  in `TouchControls`) ✅; pause button ✅ (`PauseMenuUI`, top-right); exclusion countdown.

### B16.7 Pause Menu 🟡 PARTIAL (core + championship loss handoff DONE; Team Management not)
- ✅ **DONE:** pause button (top-right, below the scoreboard) → `Time.timeScale = 0` + centered panel
  with PAUSED + RESUME / QUIT / TEAM MANAGEMENT (`PauseMenuUI.cs`, all built in code). QUIT confirms
  "counts as a loss"; YES QUIT now routes through `MatchTimer.ForfeitMatch(false)`, records the
  championship loss, simulates the round, clears the pending fixture, and returns to HubScene.
  TEAM MANAGEMENT remains a placeholder. Timer/clock stop automatically. Full-time/forfeit result
  screen with championship-aware CONTINUE + MAIN MENU is done.
- **Still future:** score/time/event summary inside pause; Team Management with substitutions.

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

### B16.13 Post-Match 🟡 MOSTLY DONE
- Final whistle → championship actual-score handoff and round simulation; normal reward-slot pack;
  mission stats; league points; season XP. Championship completion pays the exact placement reward
  once and shows final 1–10 standings/promotion/replay. Rich celebrations/audio remain future.

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

**Slot re-check (CrowdSpawner object in SampleScene_PoolB):** `Fan Variants` should still show the 8
sprites (verify — the script was replaced!); new tunables `Fans Per Bench` 4, `Rows In Bench Art` 7,
`Fan Height In Rows` 1.5, `Seat Surface 01` 0.48, `Fan Seat Anchor 01` 0.34. To seat fans on a
different row: seat-surface ≈ (rowIndexFromBottom + 0.35) / 7.

**How to test:** enter a match → each tagged bench shows 4 fans in one line across its width, slightly
uneven spacing, sized so a fan roughly fills one painted seat; fans' bottoms sit ON a seat row (legs
over the bench front), not floating above/below; watch 10s — movement is a subtle breath and neighbours
move on independent phases.

---

## SESSION LOG — 2026-07-26 (fixed real-club championships, rewards, offline logos, match identity)

This session replaces the old random 16-team/2×8/QF placeholder tournament with the requested
complete 10-club championship loop for all four competitions. The authoritative current description
is B11 above; this log records implementation and verification details for handoff.

### Data and catalogue

- Added `ClubCatalog.cs` plus `ClubCatalogBuilder.cs`. The builder contains all 34 exact club IDs and
  levels and makes direct Sprite references, so shipped logos work offline. The supplied
  `Stua-Bucha.png` is intentionally aliased to club ID `Stu-Bucha`; `Poseidon.jpeg` works normally.
- All 34 club and seven trophy/medal texture metas were checked: Texture Type Sprite (2D and UI),
  Sprite Mode Single. Zero missing files.
- The builder maps `Division1`, `Premier-League`, `Continental-Cup`, `Champions-League`,
  `Gold-Medal`, `Silver-Medal`, and `Bronze-Medal` and creates
  `Assets/Resources/ClubCatalog.asset`. Revision 2 also alpha-crops each club art into generated
  catalogue Texture/Sprite subassets, fixing source PNGs whose transparent margins made the visible
  crest tiny. It runs automatically when the asset is absent/outdated and can be forced with
  **Tools → Water Polo → Rebuild Club Catalog**.
- Every code-built crest holder and the match HUD binder renders a shadow, tier rim, close-fitting
  opaque white plate, then a larger tightly cropped crest. My Club resolves its saved procedural
  logo/tint directly from `RosterManager.Club`.

### Tournament domain (`LeagueSeason.cs`)

- Ownership was corrected after the first pass: every championship automatically reserves Group A
  slot 0 for the player's saved My Club. There is no official-club picker. Each competition has nine
  fixed AI rivals, rearranged by strength as documented in B11.
- All 34 official clubs appear across the 36 AI slots. Only Crab and Matador repeat, and there are no
  duplicates inside a competition. `playerIndex` is deliberately Group A team zero for schema 3.
- Each group uses five calendar rounds with a virtual BYE: two fixtures + one bye per round, ten
  unique group pairs total, four matches and one bye for every club.
- A real player score advances one calendar round and simulates all other unplayed fixtures in both
  groups. A player bye simulates all four live fixtures. The PRNG state is stored in the run; no
  Unity global Random call can reroll saved history.
- Standings use 3/1/0 points, then GD, GF, stable club name. Saved table counters are re-derived from
  the authoritative fixture records after load.
- A1–B2 and B1–A2 semifinals; A3–B3 / A4–B4 / A5–B5 placement ties; semifinal losers play an
  automatically simulated third-place match; semifinal winners play the final. Knockout draws gain
  a deterministic sudden-death goal. Completion yields exactly ten unique rank slots.
- `championships.json` stores all active/completed runs independently, including the saved My Club
  display name, fixtures, scores, phase/round, PRNG, final order, and reward/promotion flags.
  Schema 2 official-club-selection runs intentionally restart under schema 3 so ownership cannot be
  silently changed mid-tournament.

### Result, reward, and quit safety

- Added `MatchPresentationContext`: PLAY persists the exact player/opponent/competition, PoolB
  records nothing early, and only MatchTimer's real final whistle or forfeit submits. Submission
  verifies that the saved next opponent still matches, preventing duplicate/stale fixture writes.
- Normal full time, exclusion forfeits, and pause-menu YES QUIT all advance the tournament.
  A forced result gets a one-goal walkover margin only when the live scoreboard does not already
  agree with the forced winner. Pause quit returns to HubScene after recording the loss.
- Completion reward payout is idempotent and self-heals if a save occurred between final-order
  creation and payout. Exact top-three currency values and next-tier unlocks match the specification.
- Play Championship Again deletes only the completed run, preserves earned currency/unlocks and
  other competitions, then immediately creates a fresh run with My Club and reset fixtures/tables.

### Competition and match UI

- Locked game-mode cards are inspectable. Locked screens show the reason, trophy, medals, exact
  rewards, and both five-club logo lists; no fixtures, tables, or Start button are constructed.
- An unlocked fresh screen opens a designed ownership overview: trophy/status hero, large My Club
  identity card, two side-by-side five-club lists, and three individual medal/currency reward tiles.
  The old official-club picker is deleted. Active Group Stage shows both five-club tables with large
  crests/readable names, compact/details modes, logo-backed latest-round scores, and byes. Knockout
  shows crest-backed semifinal, 5th/7th/9th, third-place, and final score cards.
- Final screen: all ten ranks vertically with logo/name, full reward card, player's exact earned
  Gold/Diamonds, promotion/no-promotion status, top-three medals, My Club highlight, and replay button.
- All hub/game-mode/competition currency readouts are now integrated icon/frame/count/+ chips with
  thousands separators. Action buttons use a raised face, lower shadow, highlight, and bold text.
- A layout bug that treated left-anchored text centres as left edges was corrected in the overview,
  rewards, and final rows; long club names use bounded TMP auto-sizing instead of disappearing under
  logos or outside card bounds.
- Pre-match: real club panels/logos slide horizontally in from opposite sides and settle around VS
  in ~0.6s unscaled time.
- PoolB: `ChampionshipHudBinder` updates `PlayerNameText` and `BotNameText`, auto-sizes long names,
  adds large layered logo holders, and event-feed goals use real names. Quarter breaks display both
  fixture crests/names; goal replays use real club names instead of You/Bot. `ScoreManager` self-heals the scene's missing
  PlayerScoreText by cloning the working BotScoreText and inferring the mirrored X coordinate from
  the two live name labels; no coordinates or Inspector slots are needed from the user.
- Match result score text uses both real club names. CONTINUE returns championships to HubScene
  while casual matches retain PoolB replay behavior.

### Offline/backend decision

Bundled catalogue, logos, trophy/medal art, competition rules, Bot fallback players, and local JSON
are the offline source of truth. Future Firebase should provide optional versioned overrides for
player speed/shooting/range/accuracy and other gameplay stats, club win-rate/stat snapshots, and
balance values, with validation and last-known-good disk caching. Frequently unchanged logos should
remain bundled; a remotely replaced logo needs disk cache plus the bundled crest fallback.

### Verification completed

- `dotnet build Assembly-CSharp.csproj --no-restore` → **0 warnings, 0 errors**.
- `dotnet build Assembly-CSharp-Editor.csproj --no-restore` → **0 warnings, 0 errors**.
- `git diff --check` → clean (only Git's informational LF→CRLF notices).
- Static catalogue/schedule audit:
  - 34 catalogue entries; 36 official AI slots + 4 My Club slots; all 34 official clubs covered;
    only Crab/Matador repeat; zero missing sprites; zero invalid sprite imports.
  - 4 competitions / 8 groups validated.
  - Each group has 10 unique pairings; every club has 4 matches and exactly 1 bye.
- Unity's second isolated batch instance could not finish because the already-running editor owns
  the active Personal-license client. This is not a compiler issue. On the next main-editor
  refresh/reopen, the `[InitializeOnLoadMethod]` builder automatically materializes
  `Assets/Resources/ClubCatalog.asset`; if auto-refresh is disabled, run the Tools menu item once.
  No Inspector wiring or scene-coordinate input is required.

### Focused Play Mode checklist

1. Reopen/refocus Unity; confirm `Assets/Resources/ClubCatalog.asset` appears and Console has no
   `ClubCatalogBuilder: missing ...` warnings.
2. Hub → PLAY: open every locked card and verify logo lists/rewards are visible but no fixtures or
   Start button. Open Division 1 and verify the saved My Club appears automatically in Group A with
   its own name/crest and a YOUR CLUB tag; no official-club selection screen should exist.
3. Play one group fixture: PoolB shows both real names/logos and both scores; finish or pause-quit;
   return to the competition and verify four round scores, both standings, and next opponent moved.
4. Progress through a player bye and all five rounds. Verify only top two per group appear in
   semifinals and 5th/7th/9th score cards simulate with the semifinal.
5. Reach a final: third place already has a score. Complete it and verify ten unique final rows,
   one exact reward payout, promotion only for 1st, and next competition unlock.
6. Press Play Championship Again: a fresh My Club run starts directly, all prior fixtures/tables are
   reset, and earned currency/unlocked cards remain.

### Deferred, explicitly not part of this completed pass

- Separate post-championship Cup competition (entrants/format/rewards/unlock/UI still need a spec).
- Firebase SDK/admin panel, online accounts/cloud merge, per-club rosters, backend player-stat patch,
  club record/win-rate screens, and remote-logo download/cache.
- Competition-specific pool art and longer player warm-up/entry cinematics.

---

## SESSION LOG — 2026-07-26b (Play crash fix + win-gated championship restart)

### Crash fixed

Opening a competition threw `ArgumentNullException: source` from
`TMP_Text.CreateMaterialInstance` → `set_outlineWidth` →
`NavigationManager.MakeActionButton`. The raised button pass was setting TMP outline thickness
immediately after dynamically adding `TextMeshProUGUI`; on this project's current TMP initialization
path, the text material had not been assigned yet. The unsafe `outlineWidth`/`outlineColor` writes
were removed. The raised face, shadow, highlight, font weight, colours, hover and click behaviour
remain unchanged.

### Active championship restart

- `LeagueSeason.PlayerMatchWins` counts real player wins across played group, semifinal, and final
  fixtures.
- The active competition bottom bar now always communicates restart state:
  **RESTART (WIN 1)** is disabled before the first win; after at least one win it becomes the red
  **RESTART RUN** action.
- Restart opens a confirmation panel explaining exactly what is reset and retained.
- Confirm calls `LeagueSeason.RestartCurrent`, which rejects missing/completed/no-win runs and then
  replaces only that competition save through `StartNew`.
- Reset: fixtures, scores, standings, phase, round, simulated history, PRNG, and final order.
- Preserved: RosterManager Gold/Diamonds, PlayerPrefs competition unlocks, already granted rewards,
  My Club identity/crest, and all other competition saves.
- Completed championships still use the existing Play Championship Again flow.

### Fresh fixture draw

`BuildGroupFixtures` now Fisher–Yates shuffles each group's five team indices plus BYE before applying
the circle schedule. Clubs remain in the fixed known groups. The shuffle changes round opponents and
bye position, still produces ten unique pairings, four games and one bye per club, and is serialized
immediately with the run so it cannot change until a new/restarted championship begins.

### Verification

- `dotnet build Assembly-CSharp.csproj --no-restore` → **0 warnings, 0 errors**.
- `dotnet build Assembly-CSharp-Editor.csproj --no-restore` → **0 warnings, 0 errors**.
- 200 independently shuffled five-club schedules audited: every one retained 10/10 unique pairings,
  four games per club, and five unique bye recipients.
- Restart has three guards: active saved run, matching competition, and at least one player win.
- The restart code contains no currency grant/deduction, unlock mutation, or other-competition delete.
- No Inspector slots or scene edits were added.

---

## SESSION LOG — 2026-07-26c (currency scale, saved My Club identity, live HUD tags)

### Currency chips

- `NavigationManager.MakeCurrencyChip` was checked before editing: its coin/diamond art was exactly
  **32×32** inside a 40×40 well and a 164×50 chip.
- Both icons are now exactly **64×64 (2×)**. The well is 68×68 and the chip is 196×72 so the art
  cannot clip. Gold/diamond centres moved to −98/−306, leaving a 12px gap between the wider chips;
  the count and integrated `+` control no longer overlap the icon.
- The shared chip builder feeds the hub, Game Mode, competition and other code-built top bars, so
  the size fix applies consistently.

### Saved My Club identity in competition presentation

- `RosterManager.Club` / `ClubProfile` in `roster.json` remains the single durable source for the
  saved name, logo ID, primary colour and secondary colour.
- `ClubCustomizationUI.ApplySavedClubIdentity` is now the shared render contract: primary colour
  fills the badge field; secondary colour tints the selected procedural crest.
- `NavigationManager.AddClubLogo` uses that contract for My Club and also recognizes the saved club
  name automatically instead of depending only on each caller passing a special flag. Competition
  overview lists, group fixtures/byes, tables, knockout cards, final standings, restart cards and
  pre-match panels therefore resolve the current saved identity.
- Quarter-break full-crest presentation uses the same saved badge contract. Official AI clubs still
  use their fixed `ClubCatalog` sprites.

### Live gameplay HUD identity

- `ChampionshipHudBinder` now installs for PoolB live gameplay and changes only the existing
  `PlayerNameText` / `BotNameText` area. The full live-HUD crests added by the previous pass are
  retired; pre-match, competition, standings and quarter-break crests remain unchanged.
- PoolB's serialized fallback text is now `---` instead of `You` / `Bot`, preventing a one-frame
  flash of the retired hardcoded labels before the runtime binder starts.
- The live labels are bold 32px uppercase three-character tags on dark high-contrast plates with a
  coloured team edge. `MatchPresentationContext.ClubAbbreviation` strips hyphens/non-alphanumeric
  separators before taking the first three characters (`New-Grand` → `NEW`,
  `Aurelio-Posillipo` → `AUR`).
- Championship matches use the saved My Club name and exact fixture opponent. A non-championship
  PoolB match uses the saved My Club name and `OPP`, eliminating the scene's hardcoded YOU/BOT HUD.

### Verification

- `dotnet build Assembly-CSharp.csproj` → **0 warnings, 0 errors**.
- `dotnet build Assembly-CSharp-Editor.csproj` → **0 warnings, 0 errors**.
- `git diff --check` → clean apart from informational LF→CRLF notices.
- An automated in-editor Play Mode check was prepared to inspect the real HubScene, Division 1
  identity layers and PoolB tags. It could not execute because the two already-running Unity
  processes are parked in the editor's recovery/backup state and are not importing files; an
  isolated second editor also exits before creating a log while those instances own Unity.
  **Focused Play Mode visual verification is still required after resolving/closing the recovery
  editor state; do not treat this session as Play-Mode-verified yet.**

### Focused Play Mode checklist

1. Resolve the Unity recovery prompt, refocus the project and wait for compilation.
2. Hub / Game Mode: verify both currency icons are visibly 2×, stay inside the 72px chips, counts
   remain readable and the two chips retain a gap.
3. My Club: choose a visibly different crest, primary and secondary colour, press APPLY, then open
   Division 1. Verify the same two-colour crest in the overview, Group A table, a fixture row and
   pre-match. Reopen My Club afterward to confirm the saved selections remain.
4. Start the fixture. Beside the live scoreboard verify My Club and the opponent show bold 3-letter
   tags (hyphenated `New-Grand` must read `NEW`), with no full crest and no YOU/BOT label.
5. Reach a quarter break and verify its full crests still appear; return to competition standings
   and verify full crests remain there too.

---

## SESSION LOG — 2026-07-26d (currency clarification + white crest and HUD installer root fixes)

This corrects the 2026-07-26c interpretation after an actual visual report from Play Mode.

### Currency correction

- The requested target was the coloured yellow/cyan halo behind the currency art, not the currency
  art itself. The coin/diamond returned from 64×64 to their original **32×32**.
- The chip returned to its original 164×50 geometry and −94/−270 positions. `IconWell` remains as a
  harmless layout transform but has no sprite and is fully transparent, so the extra coloured outer
  circle is gone while the original icon, count, frame and `+` layout remain.

### Why My Club looked like a blank white badge

- The actual saved `roster.json` profile was inspected read-only: My Club is named `Dinamo`, uses
  procedural logo ID 1 (solid circle), purple primary (`6A1B9A`) and white secondary (`FFFFFF`).
- Competition badges were drawing procedural crests at 94% of the holder while the primary-colour
  field was only 82%. The solid white circle therefore covered the entire purple field and looked
  like a blank white disc.
- My Club procedural crests now use the customization preview's ~58% ratio. Official fixed
  `ClubCatalog` art stays at 94%. Quarter-break My Club crests use the same corrected proportion.
- Name-based fallback detection was removed. The saved club name `Dinamo` collides with the official
  Division 1 club `Dinamo`; all competition renderers already pass the exact `playerIndex` /
  `__MY_CLUB__` ownership flag, so only the player's slot gets the saved procedural identity and the
  official Dinamo keeps its fixed catalogue crest.

### Why the live HUD stayed `---`

- Root cause: `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` runs once for the Play session. It ran
  while HubScene was active, returned because that was not PoolB, and never ran again when PLAY
  loaded PoolB. The scene's safe `---` defaults consequently remained on both sides.
- `ChampionshipHudBinder` now registers `SceneManager.sceneLoaded` before the initial scene load and
  installs itself whenever `SampleScene_PoolB` loads. Duplicate registration/instances are guarded.
  It therefore replaces the placeholders with the saved club abbreviation (`Dinamo` → `DIN`) and
  the exact fixture opponent abbreviation on every Hub → match transition.

### Verification

- Exact saved profile inspected without modifying the user's `roster.json`.
- All 14 competition `AddClubLogo` call sites audited: every player-bearing row/card supplies an
  explicit player-slot boolean; pre-match supplies `true` for the player and `false` for the rival.
- `dotnet build Assembly-CSharp.csproj --no-restore` and editor build completed before the final
  warning cleanup; final clean builds are recorded in the handoff response.
- No Inspector slots or manual scene wiring were added.

---

## SESSION LOG — 2026-07-26e (template crest customization and full identity propagation)

### Template processing and QC

- Added `CrestTemplateBuilder`, an automatic/editor-menu importer for `Template01`–`Template20`.
  It detects tolerant colour clusters, assigns the three non-black fills by pixel coverage, feathers
  their edges by two pixels, and packs primary/secondary/tertiary/fixed-outline into one RGBA mask.
- Added the URP-compatible `UI/Crest Mask Tint` shader, one shared material, and
  `CrestTemplateCatalog`. Each crest remains one UI draw instead of four stacked Images.
- Generated masks and `Assets/Resources/CrestMasks/CrestTemplate_QC.txt`.
  **18/20 templates passed.** Template03 and Template08 were correctly rejected and skipped because
  their intended third regions are transparent in the source PNG (tertiary coverage 0.92% and
  0.76%, below the meaningful 1% threshold). They require regenerated opaque source art.

### Customization and persistence

- Rebuilt My Club customization with valid-template left/right browsing, a shared live renderer,
  three identical 14-colour swatch palettes, and an immediate preview.
- Club name is horizontal and centred on the badge, auto-sizes down, and is capped at nine
  characters. Save persists template index, all three colours, and name through the existing
  `RosterManager.Club` / `roster.json` path; no second identity store was introduced.
- Added `tertiaryColorHex` migration for existing saves.

### Identity propagation and live HUD

- Hub, competition/group lists, standings, pre-match, and quarter-break now use the same
  `CrestTemplateView` renderer and the same normalized 90% crest scale as customization.
- Player ownership is always supplied by the actual player slot / `__MY_CLUB__` reference. No
  club-name matching was reintroduced, so a player club named `Dinamo` cannot replace the official
  Dinamo crest.
- The live scoreboard now includes the saved My Club crest and the fixed opponent crest in the
  small identity area. Existing three-letter tags remain, but were reduced from 32px to 20px and
  moved closer to the scoreboard on compact 68×36 high-contrast plates.

### Verification

- The mask builder completed and emitted the 18-pass / 2-skip QC report.
- Automated Unity Play Mode navigation changed template and all three colours, saved both
  `LONGNAMES` and `ACE`, and asserted identical mask/material/name data in customization, hub,
  Division 1 competition/standings, pre-match, and PoolB live HUD.
- The PoolB assertion confirmed `ACE` / `NEW`, 20px tag sizing, corrected placement, and the live
  My Club crest. Unity logged `CODEX CREST PLAY MODE CHECK PASSED`.
- The temporary test restored the original saved `Dinamo` profile and championship state afterward.

---

## SESSION LOG — 2026-07-26f (crest presentation and pre-match polish)

### Crest rendering

- Removed the `ClubName` text child from `CrestTemplateView`; the shared renderer now draws only the
  tintable crest mask. Existing standalone club-name labels in the hub, tables and pre-match remain.
- Preserved `CrestTemplateView.ContentScale` at 90%, so removing labels and backing graphics does not
  change the crest's apparent size within any holder.

### Customization palette

- Replaced each long swatch strip with a rounded, accented 500×102 palette card.
- Primary, secondary and tertiary each use the same 14 colors in a consistent 7×2 grid.
- Selection now combines a gold frame, 112% scale-up, glow/shadow and a small drawn check badge;
  the check uses UI geometry rather than relying on an unsupported font glyph.
- Player cap/swimwear controls were moved down to preserve clean spacing below the three cards.

### Backing removal and pre-match redesign

- The hub avatar keeps its 60×60 click target but its circular backing is transparent.
- My Club no longer creates the shared helper's `Shadow`, `Rim` or `Plate` layers in competition and
  standings rows. Player-slot ownership remains explicit and is never inferred from the club name.
- Pre-match requests direct/bare rendering for both My Club and the official opponent while keeping
  each existing 78px logo holder and its normal crest scale.
- Pre-match now uses balanced accented team cards, tighter pools, a restrained center divider,
  framed VS badge, stronger `PLAY MATCH` CTA and clearer context hierarchy.
- The six functional formation positions on each pool were retained but redesigned from large white
  rectangles into compact 36px broadcast-style tactical dots with dark centers and team accents.

### Template replacement behavior

- Template03 and Template08 remain skipped exactly as before. No special-case repair was added.
  Replacing either source PNG under the same `Template03.png` / `Template08.png` filename lets the
  existing automatic builder reprocess it and include it once all three opaque regions pass QC.

### Verification

- Automated Unity Play Mode navigation covered customization, hub, Division 1 standings and
  pre-match. It asserted no crest `ClubName` children, one selected check/scale state per palette,
  two-row 14-swatch grouping, no My Club backing layers, no backing on either pre-match crest,
  12 intentional tactical markers, and the unchanged 90% relative crest scale.
- Unity logged `CODEX CREST POLISH PLAY MODE CHECK PASSED`.
- The temporary championship created for navigation was removed/restored after the check.

---

## SESSION LOG — 2026-07-26g (World Cup country tournament)

### Shared architecture and country data

- Added `TournamentCore` as the shared standings/result layer. Both `LeagueSeason` and the World Cup
  use the same points → goal difference → goals-for ordering and group-result application;
  `WorldCupSeason` also reuses `LeagueSeason.Fixture` rather than introducing a parallel fixture
  contract. Existing club competition behavior and screens are unchanged.
- Added `CountryCatalog` / `CountryCatalogBuilder`, matching the existing direct-reference catalog
  pattern. The generated `Assets/Resources/CountryCatalog.asset` contains the exact 36 requested
  country names/rates, 36 linked flag sprites and the existing World Cup trophy.
- The supplied `Swedan.png` is accepted as a compatibility fallback for the exact runtime identity
  `Sweden`; a future correctly named `Sweden.png` is preferred automatically on rebuild.

### Tournament model

- `WorldCupSeason` persists independently in `worldcup.json`: 36 teams, six groups of six, five
  global group rounds, complete table statistics, Round of 16, quarterfinals, semifinals and final.
- Every new run sorts the catalog into six strength pots and shuffles one country from every pot
  into every group. Restart explicitly rejects the previous draw signature and generates a new one.
- A player's real group match advances all 18 fixtures in that round. All 17 non-player matches use
  both countries' rates as a probabilistic bias; weaker countries retain a real upset chance.
- Qualification is the top two in each group plus the four best third-place teams, ordered by the
  shared points/GD/GF tiebreak. Round-of-16 seeding gives all six winners a lower seed from another
  group, then pairs the four remaining lower seeds while avoiding same-group rematches.
- Each real player knockout result simulates the rest of that round. Knockout draws receive the
  existing one-goal sudden-death resolution before the next bracket is created.

### Navigation and presentation

- Added a touch-sensitive World Cup trophy button to the hub's right column. No scene was added;
  all screens are code-built panels on the existing Hub canvas.
- Added a searchable, vertically scrollable three-column country picker with 36 flag cards and an
  explicit country confirmation dialog.
- Added six group-table cards, player/qualifier highlighting, next-match action, compact gated
  restart control, pre-win requirement message and a Yes/No destructive confirmation.
- Added a readable horizontally scrollable connected bracket with Round of 16/QF/SF/final columns,
  advancing winners, faded/struck losers, a prominent trophy and first/second podium slots.
- Added a dedicated World Cup pre-match panel with both country flags/names and the shared PoolB
  scene as its only PLAY destination.

### Match identity

- Generalized `MatchPresentationContext` with club/World-Cup identity kinds while retaining the
  existing single persisted match handoff.
- World Cup PoolB matches use country names, three-letter tags and flags in the live HUD, quarter
  breaks and final result overlay. Replays/scorer labels inherit the country names through the same
  existing context. Bot players/AI and gameplay rosters are untouched.

### Verification

- Unity's catalog build logged `CountryCatalogBuilder: 36/36 countries and World Cup trophy linked`.
- Automated Play Mode covered hub trophy → scroll/search picker → seeded draw → all five group
  rounds → all other groups simulated → exact best-four-thirds set → Round of 16 → QF → SF → final
  → champion/runner-up podium → eligible restart with a different draw → PoolB.
- The check also sampled 4,000 Spain-vs-Latvia simulations: Spain won more often while Latvia still
  won matches, confirming probabilistic bias rather than deterministic strength selection.
- PoolB assertions confirmed `GEO` plus the opponent tag, both HUD flags, both quarter-break flags,
  result flags, and a real result advancing all 18 fixtures in its group round.
- Unity logged `CODEX WORLD CUP PLAY MODE CHECK PASSED`; any pre-existing World Cup save was restored.

---

## SESSION LOG — 2026-08-03 (authentication-skip launch, button catalog/localization, profile country selector)

### Launch screen

- `MainMenuUI` now presents exactly two polished choices: bright-blue **Log In** and dark-blue
  **Play as a Guest**, both with layered gloss/edge/shadow treatment and crisp auto-sized white TMP.
- Firebase remains unintegrated, so both choices intentionally call the same loading-overlay route
  into HubScene. The Log In button is no longer a dead click.

### Button asset migration and localization foundation

- Added `ButtonSpriteCatalog` plus the revisioned `ButtonSpriteCatalogBuilder`. The source sprites
  remain exactly in `Assets/Sprites/Buttons`; the Resources catalog contains only direct references
  and editor-measured alpha bounds, so runtime/player builds never use `AssetDatabase` or depend on
  the deleted `Assets/Resources/Sprites` button copies.
- All 26 supplied button assets are registered. Hub/settings/message/gifts/back/info/pause, main hub
  buttons, touch actions, Team position filters, and generic code-built button helpers now resolve
  through the new catalog. Recognized button keys never retry the deleted Resources paths; missing
  entries leave the screen alive and use the caller's procedural fallback where applicable.
- `Button1` is now the universal background for generic labelled actions across NavigationManager,
  Team, Shop, Missions, Ranking, Season Pass, Pause, quarter-break, and result UI. The Team tactic
  buttons now draw FORMATIONS / PLAYERS / SUBSTITUTIONS as live TMP instead of baked image text.
- `UILocalization` provides persisted English/Georgian/Russian lookup and a live Settings selector.
  `LocalizedButtonText` uses TMP auto-sizing plus bounded horizontal expansion; specialty art uses
  lower text zones so protruding symbols remain clear. The supplied Play art still contains English
  lettering, so translated languages automatically swap that background to clean `Button1`.

### Profile country flow

- Replaced the old 12-code coloured-dot grid with an inline `<  flag  Country  >` selector and a
  separate `v` button.
- The dropdown opens a dimmed, scrollable three-column modal containing all 36 exact CountryCatalog
  countries and real flags from `Assets/Sprites/Countries`. The active row is green with a drawn
  check badge. Choosing a row saves `ClubProfile.countryId` immediately, closes the modal, and
  refreshes the hub badge; left/right arrows also cycle and persist immediately.
- Legacy three-letter country saves migrate to exact CountryCatalog names. The hub avatar badge now
  shows the real saved flag rather than a generated colour dot.

### Verification

- Button catalog audit: **26/26 entries have matching source PNGs**.
- Country asset audit: **36/36 requested flag files present**.
- `dotnet build Assembly-CSharp.csproj --no-restore` → **0 warnings, 0 errors**.
- `dotnet build Assembly-CSharp-Editor.csproj --no-restore` → **0 warnings, 0 errors**.
- `git diff --check` clean apart from informational LF→CRLF notices.
- No scene objects or Inspector slots were added. Focused Play Mode visual verification is still
  required because the two already-running Unity processes are parked on `Temp/__Backupscenes/0.backup`
  and are not importing the project. On the next normal editor refresh, the revision-0 bootstrap
  catalog rebuilds automatically (or run **Tools → Water Polo → Rebuild Button Sprite Catalog**).

### Hub blank-screen hotfix

- The runtime log showed the fatal exception was TMP material initialization in
  `LocalizedButtonStyler.AddLabel`, not the warning site at `TeamScreenUI.BuildRightPanel`.
  Assigning `outlineWidth` to a newly added `TextMeshProUGUI` attempted to clone a null material and
  aborted `NavigationManager.BuildOverlays`; the outline effect now uses a material-independent UI
  `Shadow`. The same unsafe pattern was removed from the sprint-duel countdown.
- `NavigationManager`, `TeamScreenUI`, `ShopUI`, and `TouchControls` now distinguish catalog-backed
  button keys from ordinary Resources art. Catalog buttons never fall through to legacy paths; a
  failed lookup logs a targeted warning and allows the rest of the UI initialization loop to finish.
- The generated catalog currently contains **26/26 non-null sprite references**.
- Runtime and editor assemblies both compile with **0 warnings, 0 errors**; `git diff --check` reports
  no whitespace errors, and the code scan reports **0 legacy button `Resources.Load` calls**.
- A follow-up TMP failure in `GetPreferredValues` was caused by measuring labels while their overlay
  was inactive and TMP had not initialized its font/material reference. Labels now receive the
  default font/material explicitly, use a safe Unicode-aware width estimate during construction,
  and defer exact TMP measurement until `LateUpdate`; any package-level measurement failure is
  contained and auto-fit remains active instead of aborting the Hub build.
- The button catalog resets its static runtime handle on every Play Mode start (including projects
  with domain reload disabled), performs fresh entry lookup instead of retaining a stale index, and
  the editor builder now repairs any incomplete/null catalog even when its revision number matches.
  An enter-Play callback validates it synchronously so the delayed editor bootstrap cannot race a
  quick Play click.

---

## SESSION LOG — 2026-08-03b (button visual alignment, missing labels, My Club layout repair)

### Shared button presentation

- `LocalizedButtonStyler` now gives generic `Button1` labels a 14px bottom inset, lifting text away
  from the sprite's lower 3D bevel and into the visual centre. Icon-led lower plates were raised too.
- Runtime-cropped `Button1` sprites now carry a proportional nine-slice border. Every shared button
  helper applies the authored art at pure white instead of tinting its blue gradient/gloss/bevel;
  procedural colors and shine remain fallback-only when the catalog art is unavailable.
- `LocalizedButtonText` detects `LayoutElement` and writes measured min/preferred dimensions into the
  layout contract rather than fighting a parent layout group through `RectTransform.sizeDelta`.

### Restored labels and specialty art

- Hub Friends and Clubs now always create localized TMP labels. Team position filters now always
  create localized WINGS / CENTER / DEFENSE / GK labels rather than doing so only on missing-art
  fallbacks. Their authored red/blue/green/yellow palettes remain white-tinted and selection uses a
  separate gold underline/text treatment.
- The Season Pass button uses its source-art aspect ratio in a 220×126 rect. Its label has a custom
  text zone inside the yellow field to the right of the character, clear of both character art and
  the bottom frame.

### Layout and action consistency

- My Club's editor side is now one `VerticalLayoutGroup`: template row, dedicated country row,
  three non-overlapping palette rows and one player-color row, with 9px spacing. The country and
  selector/palette internals use `HorizontalLayoutGroup`/`GridLayoutGroup` plus explicit
  `LayoutElement` minimums, so Country can no longer float over Secondary Color.
- Team position filters use a width-sharing horizontal layout. Club-championship and World Cup
  RESTART / NEXT MATCH actions use equal 300×64 (World Cup 300×62) layout elements and the same
  untinted universal art; disabled restart state is communicated by group alpha rather than a
  destructive sprite tint.

### Verification

- `dotnet build Assembly-CSharp.csproj --no-restore` → **0 warnings, 0 errors**.
- `dotnet build Assembly-CSharp-Editor.csproj --no-restore` → **0 warnings, 0 errors**.
- `git diff --check` reports no whitespace errors (only informational LF→CRLF notices).
- Static audit finds no direct `Button1` sprite assignment outside the shared styler. The already
  running Unity editor is still parked on `Temp/__Backupscenes/0.backup`, so focused Play Mode visual
  verification remains required after the recovery editor is resolved/refocused.

---

## SESSION LOG - 2026-08-03c (nine-slice import, compact toggles, ad art, Georgian TMP)

### Sprite geometry and button routing

- `Button1.png` now has an importer-level full-rect nine-slice border (373.08 left, 427.92 bottom,
  372.08 right, 402.92 top). The catalog builder revision is 2 and recalculates/repairs this border
  from the measured alpha bounds, so a reimport cannot silently restore a zero-border Simple sprite.
- Runtime alpha-cropped `Button1` instances translate the imported border into cropped-sprite space.
  The shared styler uses `Image.Type.Sliced` with a pure-white image color; the localized Play swap
  also switches between aspect-preserved authored Play art and sliced translated `Button1` art.
- My Club template, player-color, country, and dropdown arrows no longer route through `Button1`.
  They use a compact native circular control with a fixed-aspect image and layout-element sizing.
- All rewarded-ad controls created by `ShopUI.MakeWatchButton` now use the green `Ad-Button` asset,
  pure-white tint, `Image.Type.Simple`, and Preserve Aspect. The legacy play triangle appears only
  if the catalog art is unavailable.

### Georgian typography

- Added project-local `Assets/Fonts/Sylfaen.ttf` and `GeorgianFontAssetBuilder`. On editor refresh it
  generates `Assets/Resources/Fonts/GeorgianFallback SDF.asset`, pre-baking ASCII plus U+10A0-10FF
  and U+2D00-2D2F into a 2048 atlas. It also exposes Tools > Water Polo > Rebuild Georgian TMP Font.
- `UILocalization` installs that asset in both TMP's global fallback list and the default font's
  fallback list. Georgian localized labels select it directly; switching back selects the default
  font/material, so hot language changes do not retain a mismatched material.

### Verification

- Runtime and editor assemblies compile with **0 warnings, 0 errors**, including the new editor font
  generator (validated by explicitly including it in the generated editor project for the build).
- `git diff --check` reports no whitespace errors (only informational LF-to-CRLF notices).
- Static audits confirm all crest arrows use the compact helper and Shop ad buttons resolve
  `Ad-Button` with Simple/Preserve Aspect. The open recovery Unity process has not refreshed assets;
  the editor initializer will generate the SDF asset automatically on the next normal refresh.
