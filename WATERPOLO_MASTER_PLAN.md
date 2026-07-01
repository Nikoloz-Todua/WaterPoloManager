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
| `PlayerMovement.cs` | Human control of the active player: move, grab (E), **charged shoot** (hold Space; time-based `shotChargeTime` 0.7s, min-speed floor so a tap never drops), aim chevron + **power bar** (world-unit `powerBarWidth` 1.2 — >2× the keeper bar, grows left→right), **directional charged pass** (hold B — fires where the facing triangle/joystick points with a tunable `passAssist`, NOT auto-homed; `FindPassAssistTarget` scores teammates by dot with `lastDirection`). **Shot height** (`shotHeight` 0..1, charges in lock-step with power: low 0–0.3 / mid / high 0.7–1; read by Goalkeeper + GoalkeeperAnimator for the dive tier). **Skip shot** (hold Q while charging Space → fast LOW bounce shot via `BallFlight`). **Lob pass** (hold F while passing → high slow arc with a water shadow; AI interception cut ~60%). Ball held via **parenting**; reports possession to MatchContext. `TakeOverHeldBall()` for clean control transfer; `TouchBlockSteal()` (Block button — half steal chance, 50% foul-on-miss). **Stamina hooks** (`StaminaSpeedMult`/`StaminaSprintMult`/`StaminaSprintBlocked`/`StaminaStealMult`/`StaminaPercent01`, neutral 1 by default). |
| `TeammateAI.cs` | Thin component on each player. When NOT human-controlled, runs the shared `WaterPoloBrain`. Implements `IAgentBody`. |
| `BotMovement.cs` | Thin component on each bot. Always runs `WaterPoloBrain`. Implements `IAgentBody`. |
| `WaterPoloAI.cs` | **The shared brain** + `IAgentBody` interface. All AI decisions live here once: carrier (shoot/pass/**drive**/dribble), support (get open), presser (nearest chases), defender (hold shape). 🟡 New: **drives** (beaten marker + clear lane → burst to 2m, shoot/kick-out/abort) and **picks/screens** (nominated screener plants on the carrier's marker; rubbing past = short "beaten" boost). Works, needs tuning. **This is C# state-machine AI — NOT an LLM.** |
| `TeamSide.cs` | One per team. Holds goals + roster (`members`), formation math (auto-spreads ANY number of players), passing/positioning logic, **attacking-spacing + tactics tunables (center-feed, counter, shot-quality threshold, free-throw clearance), shot-quality + pass-risk scoring, and 4 defense modes — Press/Zone/Drop/MPress — incl. man-up 4-2 umbrella + man-down zone shapes**. 🟡 New: **dynamic Centre** (fights for inside water goal-side of its guard at 2m), wider lanes + weak-side wing drift, receiver-shot-quality pass bonus, drive/screen helpers (`DrivePoint`, `GetScreenSpot`, `FindScreenerForCarrier`), and **bot adaptive defense** (`EvaluateDefenseMode`, auto-detected `isAI`: Drop when man-down / protecting a late lead / Centre conceded 2+; Press otherwise). Scales 2v2 → 6v6 with no code change. |
| `MatchContext.cs` | Singleton "match truth": ball position, possession + last toucher (`NoteTouch` for deflections), post-release grab cooldown (`releaseGrabDelay` 0.5s), freeze flag, shot-clock grab-ban, kickoff-pass flag, **free-throw state, keeper-hold flag, counterattack window, player goal-line clamp (`playerLimitX`)**, halftime `SwapEnds()`, `GiveBallTo()` / `ForceDropHeldBall()`, `EnemyOf()`, **`IsProtectedKeeper(carrier)`** (the keeper-steal safe-zone rule — true while a keeper carries the ball inside its safe zone, Task 5). |
| `TeamManager.cs` | On `GameManager`. Auto-switches control to the ball-holder after `autoSwitchDelay` (0.5s — so you keep control to chase your own loose ball); manual **C** / touch SWITCH (skips excluded); **Z** cycles defense (Press/Zone/Drop/MPress); never auto-activates excluded players. Exposes static **`ActivePlayer`** + **`ActivePlayerIndex`** (read by `CameraFollow` and the stamina HUD). |
| `Goalkeeper.cs` | Kinematic keeper sliding along its physical goal line tracking ball Y (stays on its goal after the halftime swap). **Save % system:** a fast shot reaching its hands rolls `baseSaveChance` 0.65 minus penalties for HIGH (height >0.7), POWER (>9 u/s) and SKIP shots, plus a stamina penalty when tired; a slow ball is auto-collected. **Snatch:** an enemy carrier within `keeperSnatchDistance` (0.8u) is stripped with 100% success, no roll (`TrySnatchFromCarrier`; respects free throws, not vs another keeper). **Player keeper = full control:** while your own keeper holds the ball it plays like a field swimmer — free **2D movement** at `keeperMoveSpeed` (4), sprint, a charged shot fired in the **joystick/aim** direction (never auto-aimed at goal), and a **directional pass** (`FindKeeperPassTarget` scores ALL teammates by dot(aim,dir)−dist×0.05, no cone; reads the live `TouchControls.Instance` joystick, else `lastDir`). **No auto-pass** — fully manual; it SWIMS back to its line (never teleports) after you shoot/pass. **Safe zone (Task 5):** within `KeeperSafeZoneRadius` (1.5u) of the goal line the carrying keeper is unstealable; carry it OUTSIDE and `keeperLeftSafeZone` latches → enemies steal normally (exposed via `MatchContext.IsProtectedKeeper`; `OnBallStolen()` clears the hold on a successful strip). **Organic idle** (not holding, ball far): small random X drift 0.1–0.3u every 2–4s (≤0.4u off the line) + a subtle ±0.05u Y sine micro-bob. **Bot keeper** auto-distributes after `keeperHoldSeconds` (0.8s) OR immediately if crowded within `keeperPanicDistance` (2.5u) — UNCHANGED. **Stamina-aware** (tired = worse saves, no sprint at 0%). A keeper hold is NOT a possession change — the shot clock keeps ticking until the pass-out. |
| `Goal.cs` | Trigger on each net; reports `goalSide` ("Left"/"Right") to ScoreManager. |
| `ScoreManager.cs` | Team-based score (credits the team attacking that net → survives the halftime swap) shown on **separate `playerScoreText` + `botScoreText`** TMP fields; **ignores held-ball goals**; exposes `HomeScore`/`AwayScore` (read by the camera's goal-shake). **Goal restart (NOT a quarter start → NO sprint duel):** 4 phases — (1) ball loose at exact (0,0), play freezes, touch UI hidden + `ctx.ResetBallTouch()` (camera → overview), a `goalFreezeSeconds` (1s) celebration; (2) both teams snap into the **natural restart spread** (`TeamSide.SnapToRestartFormation(hasBall)` — conceding = attacking spread, scoring = sat-back defensive, never a rigid line), the **conceding team** is given the ball at exact centre (`ctx.GiveBallTo`) + `ctx.ResetBallTouch()` again to hold the overview through the pause; (3) a **`postGoalPauseSeconds` (3s) silent pause** (frozen, ball held at centre, no UI/countdown); (4) `Unfreeze` + `SetKickoffPass(conceding)` + `ctx.MarkBallTouched()` (camera eases back to follow) + restore UI + reset shot clock — the team in possession begins the attack naturally. |
| `MatchTimer.cs` | Quarters (90s) + win/lose/draw; pauses the clock during freezes; triggers the sprint duel each quarter; halftime swap; `ForfeitMatch()`. At full time / forfeit it calls `MatchResultUI.Show()` (falls back to the bare `resultText` if no MatchResultUI in the scene). |
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
| `MainMenuUI.cs` | MainMenu scene. Builds the whole main menu in code at runtime: canvas (1280x720), background + logo from `Assets/Resources/Sprites/`, PLAY/SETTINGS/QUIT buttons with hover scale + cyan-outline TMP labels, 1s fade-in, version footer. PLAY → **HubScene**. |
| `NavigationManager.cs` | HubScene. The whole hub-navigation shell built in code (design + navigation only, NO real data): persistent top bar (club logo placeholder, "My Club", gold + diamond displays (now LIVE from RosterManager), "SET" settings stub — the default TMP font has no ⚙ glyph) + bottom nav (CAREER/TEAM/TRANSFERS/MY CLUB/CHALLENGES, active tab cyan), 5 placeholder screens with 0.3s fade transitions. Career (Division 3 badge, fake 5-team standings, PLAY → SampleScene), **Team is now REAL** (delegates to `TeamScreenUI` — live roster, not a placeholder), Transfers (3 agent buttons, 6 fake player cards with BUY stubs), My Club (STADIUM/POOL upgrade cards + customize stubs), Challenges (3 daily cards, greyed CLAIM). The non-Team buttons still log "coming soon"; the top bar's gold/diamond read from `RosterManager`. |
| `PlayerData.cs` | **(Player data foundation, NEW)** ScriptableObject = one player CARD: `id`, `fullName`, `nation`, `position` (enum GK/CB/LW/RW/CF/LF/RF — enum order == starter-slot order), `overall` 0–100, a `Stats` struct (speed/shooting/passing/defense/stamina/goalKeeping 0–100), `rarity` (Common/Rare/Legendary → `RarityColor`), `portrait` (Sprite, null for now → UI draws a silhouette), `priceGold`, `isBot`. `[CreateAssetMenu]` (Create → Water Polo/Player). Static `ComputeOverall(stats,pos)` (GK leans on goalkeeping, field = outfield avg) shared by the generator + UpgradePlayer; `Clone()` so owned cards are mutated as runtime copies, never the source asset. PURELY data — never touched by the match. |
| `PlayerDatabase.cs` | **(NEW)** Read-only player CATALOG: lazy C# singleton that `Resources.LoadAll`s every `PlayerData` under `Resources/Players/` into a dict by id (`Get`/`Has`/`AllPlayers`/`FirstOfPosition`/`Count`). No scene object. |
| `Roster.cs` | **(NEW)** `[Serializable]` save payload: `List<string> ownedPlayerIds`, `string[7] starterSlots` (0=GK, 1–6 field by position), `int coins`, `int diamonds`. IDs only → tiny JSON. |
| `RosterManager.cs` | **(NEW)** Self-bootstrapping singleton MonoBehaviour (DontDestroyOnLoad, no wiring). Loads/saves `Roster` as JSON in `Application.persistentDataPath/roster.json` (guest-mode, no Firebase); seeds a default 7 + bench + coins/diamonds on first run (self-heals if the catalog was empty then). Owned cards held as `Clone()`s so upgrades never corrupt the source asset. API: `BuyPlayer`/`SellPlayer`/`UpgradePlayer` (bump stats + recompute overall, spend/earn gold)/`SetStarter(slot,id)`/`GetOwnedPlayers`/`GetStarters`/`TeamOverall` (avg of filled starters); auto-saves after every mutation. (Upgrades are in-session only — Roster stores ids only; extend later.) |
| `TeamScreenUI.cs` | **(NEW)** The REAL hub Team screen (B12), built in code in NavigationManager's style (no prefabs/wiring; NavigationManager attaches it + passes itself). Live 2-3-2 formation of the 7 starters, a scrollable owned-bench + buyable-market list, team OVR + gold/diamonds, and working **BUY / SELL / UPGRADE / START** buttons → `RosterManager` (each refreshes the screen + the top-bar currency). Each card: rarity-coloured border (grey/blue/gold) + name/OVR/position + silhouette (or `portrait`). |
| `SamplePlayerGenerator.cs` | **(NEW, Editor — `Assets/Editor/`)** **Tools → Generate Sample Players**: writes 21 sample `PlayerData` assets to `Resources/Players/` (all 7 positions, mixed rarities/ratings/prices; deterministic → idempotent). Run once so the Team screen has data. |
| `MatchResultUI.cs` | Full-time result screen, built in code, hidden until `MatchTimer` calls `Show(title, outcome)`: dark 80% overlay, FULL TIME/FORFEIT title, "YOU n — n BOT" score from ScoreManager, colored winner line (cyan/red/yellow), PLAY AGAIN + MAIN MENU buttons; 0.5s unscaled-time fade-in (timeScale is 0 at match end). Singleton. |
| `QuarterBreakUI.cs` | Between-quarters pause screen (built in code, **self-bootstrapping** via `Get()` — no scene object needed). `MatchTimer` raises it when a quarter ends (but the match isn't over): dimmed overlay + centred dark panel with **"QUARTER N COMPLETE"**, the score, and **RESUME** (→ next quarter's sprint duel) / **QUIT** (→ MainMenu if present, else stop play). Play freezes via `MatchContext.FreezeAll` until RESUME. Singleton. |
| `PauseMenuUI.cs` | Pause system, built in code: 70x70 pause button top-right at (-20,-45) (sprite `Resources/Sprites/pause-button`; pulled down to clear the scoreboard), click → `Time.timeScale = 0` + centered 400x350 rounded panel with PAUSED + RESUME / QUIT / TEAM MANAGEMENT. QUIT opens a confirmation sub-panel ("If you quit, this match counts as a loss.") with YES QUIT (→ MainMenu) / CANCEL. TEAM MANAGEMENT is a placeholder (no functionality yet). Ignores clicks after full time (result screen owns the freeze). Works with mouse + touch. |
| `CameraFollow.cs` | **FIFA-style follow camera** on **Main Camera** — self-contained, no Inspector wiring (pulls `TeamManager.ActivePlayer` + `MatchContext`). **Start/post-goal overview (Task 1):** until the ball is first touched after any reset (game start, after a goal, between quarters — `MatchContext.BallTouchedSinceReset`) it holds the wide pool overview centred on (0,0) at **maxSize 5.0**, no following; the first grab eases it smoothly into the normal follow. Tracks a weighted point between the active player (60%) and the ball (40%) — 70/30 when the ball is loose — via `SmoothDamp` (speeds up to `switchSpeed` 8 for 0.5s on a player switch). **Dynamic orthographic zoom** (`Mathf.Lerp`): 4.2 base → 5.0 (player/ball far) → 4.5 (`SprintHeld`) → 3.8 (you control the keeper). HARD pool-boundary clamps on the camera centre (X ±5.5, Y ±3.2); Z locked −10. **Screen shake** (additive): goal 0.15/0.4s (polls `ScoreManager` total), powerful shot (ball >10 u/s) 0.05/0.15s. Managers missing → parks at (0,0,−10) size 5, no errors. All tunables serialized. |
| `StaminaSystem.cs` | FIFA-style stamina on every field swimmer + keeper. **Auto-installs at runtime** (`RuntimeInitializeOnLoadMethod`) onto any `PlayerMovement`/`IAgentBody`/`Goalkeeper` lacking one → 14 objects (6 players, 6 bots, 2 keepers), zero wiring (the 2 keepers keep a hand-tuned copy). **Field drain/recovery per sec:** idle +8% (×2 after 5s rest), swim −3%, hold+move −5%, sprint −12% (−18% after 3s fatigue), excluded +15%; **second wind** at 0% (ease off sprint 2s → +15% burst). **Effects:** <40% speed ×0.8; <20% speed ×0.6 + steal ×0.8; 0% sprint disabled. **Keeper:** track −2%, hold −1%, idle +10%; tired = worse saves, no sprint at 0%. Writes only neutral hooks (deleting it leaves the game identical); HUD lives in `TouchControls`. |
| `BallFlight.cs` | Ball VFX, **auto-added to the Ball at runtime** by `PlayerMovement` (no wiring), singleton. Speed-gated **TrailRenderer** (>5 u/s); **high-shot** scale swell (≤1.2×) + warm glow; **skip-shot** bounce 1.5u before the goal (Y jitter, squash + expanding water ripple, 35% `KeeperFooled`); **lob** breathing blue-grey water shadow; **spin** (shots 54°/s, fast loose 18°/s, lobs 9°/s — none on skip / plain pass, only >6 u/s, snaps upright on catch). All scaling uniform, recomputed from a clean base each frame (never drifts on a re-parent). Exposes `ShotHeight`, `SkipActive`/`SkipBounced`, `LobActive`/`LobTeam`, `KeeperFooled`. |
| `GoalColliderFixer.cs` | Editor tool (**Tools → Fix Goal Colliders**). Resizes GoalRight/GoalLeft Box Collider 2D to the visual goal mouth (size (4,15) → world ≈0.8×3.0u at scale 0.2). Idempotent; marks the scene dirty (Ctrl+S to save). |
| `PlayerLabel.cs` | ⬜ **NOT YET BUILT** (planned). Future: world-space player-number labels floating above each swimmer. |
| `LeagueSeason.cs` | Static session-persistent **tournament** state, one per competition. 16 teams in 2 groups of 8 (player = team 0, Group A); 7-round group round-robin (circle method), each player match also simulates that round in both groups; top 4 per group → single-elim knockout (QF: A1vB4/A2vB3/B1vA4/B2vA3 → SF → Final, no draws — sudden-death goal). Player eliminated → rest of bracket simulates instantly. Phase enum (GroupStage/Quarterfinal/Semifinal/Final/Completed), per-group P/W/D/L/GF/GA/Pts, `KnockoutMatch` bracket, 30-club name pool (15 opponents drawn per division). Champion → NavigationManager persists the next-division unlock (PlayerPrefs div1_won/pl_won/cc_won/wcl_won). |
| `GameModeCardFX.cs` | Card hover/select animations, locked-card shake, staggered entry for Game Mode screen. |
| `GameModeBackgroundFX.cs` | Ken Burns + vignette + light specks animated background for Game Mode. |
| `CardPack.cs` | Static pack data model + open logic. `ShopPackType` (Basic/Super/Gold/Legendary shop packs: gem price, $-price/ad options, "at least one of X rarity" drop tables) and `CardTier` (post-match reward packs: unlock hours 3/7/12/24, per-card rarity odds). Draws catalog players by rarity (falls back a tier if a rarity pool is empty); `GrantAll` adds to roster, duplicates convert to coins. |
| `PostMatchRewardManager.cs` | Post-match reward slots (Clash-style). Self-bootstrapping singleton, own JSON save (`rewardSlots.json` — persists across relaunch). 4 slots, states Empty/Locked/Unlocking(+derived Ready); full-time in `MatchTimer` rolls a tier (Common 80/Rare 16/Epic 3.5/Legendary 0.5%) into the first empty slot; one unlock at a time; UTC-tick timing keeps counting while the app is closed. |
| `PackRevealUI.cs` | Shared "PACK OPENED!" overlay: rarity-framed cards scale in staggered, NEW / +coins per card, tap to dismiss. Used by hub reward slots and the shop. |
| `ShopUI.cs` | Full SHOP screen (code-built, hosted in NavigationManager's shop overlay): top bar (back/gear/event badge/currencies with [+] → Coins/Gems tabs) + 9 bottom tabs — OFFERS (Coach's Choice + pack row), PACKS, DAILY DEALS (UTC-day rotation, local only), FREE PRIZES (6h PlayerPrefs cooldowns), ADS PACK (fake 0.8s ad, TODO real SDK), COINS/GEMS (via IAPBridge), DRAFT TICKETS + EVENT (honest placeholders). Drop-rate "i" popups per pack. |
| `IAPBridge.cs` | Single entry point for ALL real-money buys (`PurchaseProduct(productId, onSuccess)`). Currently a stub: logs + succeeds immediately. Swap this one method body for Unity IAP (Apple/Google billing only) later. |

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

Game Mode screen with 4 competition cards, lock-sign sprite, animated background, card polish. Competition screen (per division): GROUP STAGE / KNOCKOUT tabs — two collapsible framed group tables (collapsed = top 5, Pos|Team|Pts; expanded = all 8 full columns; tap card to toggle; player row gold) in a vertical ScrollRect, plus a bracket view (QF 2x2, SF, Final; player's tie gold-framed, losers dimmed, "vs"/TBD for unplayed). Bottom bar: NEXT MATCH + TEAM shortcut (back from Team returns to the competition screen via `teamReturnTo` context; hub TEAM returns to hub). Pre-Match screen (two pool-screen pools, 6 formation markers, phase-aware match label, PLAY). Full nav flow: Hub → Game Mode → Competition → Pre-Match → SampleScene. Universal back-button sprite.

## A9. Controls (keyboard — for PC testing; touch comes later)

- **WASD / arrows** — move active player.
- **Hold LeftShift** — **sprint** (2x speed while moving). Sprinting WITH the ball = **loose hold**: you keep the ball but opponents get 2x steal range + a steal-chance bonus (`looseHoldStealBonus` 0.15 on BotMovement/TeammateAI).
- **E** — grab / drop a loose ball.
- **Hold Space** — charge & shoot (release to fire).
- **Hold B** — charge & pass. **DIRECTIONAL (FIFA-style):** the ball goes where you AIM (the facing triangle / joystick / WASD), not auto-homed to a teammate. A gentle `passAssist` (default 0.3) bends it toward a teammate that's roughly along the aim; aim at the keeper or empty water and it goes there. Tunables on PlayerMovement: `passAssist`, `passAssistRange`, `passAssistMinDot`, `passAccuracy`, `passInaccuracyDegrees`.
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
- **Scene** — `Assets/Scenes/SampleScene.unity`.

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

## B7. Settings Screen 🟡 PARTIAL (only the "SET" button stub in the hub top bar — logs "coming soon")
- Top tab stays; content area swaps; back arrow appears.
- Options: (1) Language `< >` instant — English/Russian/Georgian (+more). (2) Bot difficulty `< >` — Medium/Hard, default Medium. (3) Account — Log In/Out/Sign Up/Delete (Apple or Google Play); progress saved & synced across devices (Firebase planned). (4) Info links — FAQs, Legal Notices, ToS, System Info (external links).

## B8. Claim Rewards 🟡 PARTIAL (greyed CLAIM buttons exist on the Challenges shell; no reward logic)
- Popup: Season Pass + Activate Pass. Split horizontally: top = premium (pass) rewards, bottom = free. Rewards = coins/diamonds/items from wins/goals.

## B9. Currencies 🟡 PARTIAL (top-bar gold/diamond displays with placeholder numbers; no economy behind them)
> Foundation: **Player System Architecture** (end of Part A) — coins/diamonds stored in local JSON, synced to Firebase on login; payments require login, ads don't.
- **Diamond:** icon + cyan bg + number; rare; buy high-rated random players / upgrade when gold short.
- **Gold:** coin + number; buy normal/good players, upgrade pool, upgrade players, buy caps/swimwear.
- Both have **+** → shop popup (real-money items/players via Apple/Google billing); purchase adds item to game.

## B10. Club Logo / Team Name Popup 🟡 PARTIAL (logo placeholder circle + "My Club" name shown in the hub top bar; popup itself not built)
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

Pool variants per competition (visual only, same SampleScene):
- Division 1 → current outdoor pool (existing SampleScene)
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

## B14. My Club Screen 🟡 PARTIAL (shell built: STADIUM + POOL upgrade cards with placeholder levels/costs, CAP COLOR + SWIMWEAR stubs; no upgrade logic)
- Full-screen. (1) Upgrade Stadium/Pool → more fans → more post-match money (win > loss). (2) Customize cap & swimwear (colors/designs).

## B15. Challenges Screen 🟡 PARTIAL (shell built: 3 daily challenge cards with progress/rewards + greyed CLAIM, reset countdown placeholder; no tracking)
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
  - ✅ **Charged shot** — hold Space = power + height (`shotHeight` 0..1); max charge = high shot, harder to block.
  - ✅ **Skip/bounce shot** — **Q** + Space → fast LOW bounce shot (`BallFlight`; 35% keeper-fool chance).
  - ✅ **High lob pass** — **F** + B → high arc pass with a water shadow; AI interception cut ~60%.
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
- Card slots are visual only — no chest/reward logic yet
