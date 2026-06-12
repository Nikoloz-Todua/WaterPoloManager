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
| `PlayerMovement.cs` | Human control of the active player: move, grab (E), shoot (hold Space), aim line. Ball held via **parenting** to the player; reports possession to MatchContext. Has `TakeOverHeldBall()` for clean control transfer. |
| `TeammateAI.cs` | Thin component on each player. When NOT human-controlled, runs the shared `WaterPoloBrain`. Implements `IAgentBody`. |
| `BotMovement.cs` | Thin component on each bot. Always runs `WaterPoloBrain`. Implements `IAgentBody`. |
| `WaterPoloAI.cs` | **The shared brain** + `IAgentBody` interface. All AI decisions live here once: carrier (shoot/pass/**drive**/dribble), support (get open), presser (nearest chases), defender (hold shape). 🟡 New: **drives** (beaten marker + clear lane → burst to 2m, shoot/kick-out/abort) and **picks/screens** (nominated screener plants on the carrier's marker; rubbing past = short "beaten" boost). Works, needs tuning. **This is C# state-machine AI — NOT an LLM.** |
| `TeamSide.cs` | One per team. Holds goals + roster (`members`), formation math (auto-spreads ANY number of players), passing/positioning logic, **attacking-spacing + tactics tunables (center-feed, counter, shot-quality threshold, free-throw clearance), shot-quality + pass-risk scoring, and 4 defense modes — Press/Zone/Drop/MPress — incl. man-up 4-2 umbrella + man-down zone shapes**. 🟡 New: **dynamic Centre** (fights for inside water goal-side of its guard at 2m), wider lanes + weak-side wing drift, receiver-shot-quality pass bonus, drive/screen helpers (`DrivePoint`, `GetScreenSpot`, `FindScreenerForCarrier`), and **bot adaptive defense** (`EvaluateDefenseMode`, auto-detected `isAI`: Drop when man-down / protecting a late lead / Centre conceded 2+; Press otherwise). Scales 2v2 → 6v6 with no code change. |
| `MatchContext.cs` | Singleton "match truth": ball position, possession + last toucher (`NoteTouch` for deflections), post-release grab cooldown, freeze flag, shot-clock grab-ban, kickoff-pass flag, **free-throw state, keeper-hold flag, counterattack window, player goal-line clamp (`playerLimitX`)**, halftime `SwapEnds()`, `GiveBallTo()` / `ForceDropHeldBall()`, `EnemyOf()`. |
| `TeamManager.cs` | On `GameManager`. Auto-switches control to the ball-holder; manual **C** switch (skips excluded); **Z** cycles defense (Press/Zone/Drop/MPress); never auto-activates excluded players. |
| `Goalkeeper.cs` | Kinematic keeper sliding along its physical goal line tracking ball Y (stays on its goal after the halftime swap). **Grab-and-control:** collects a slow loose ball near its net, holds briefly, then distributes to an open teammate (bot auto-passes; player keeper passes out on **B**). A keeper hold is NOT a possession change — the shot clock keeps ticking until the pass-out. |
| `Goal.cs` | Trigger on each net; reports `goalSide` ("Left"/"Right") to ScoreManager. |
| `ScoreManager.cs` | Team-based score (credits the team attacking that net → survives the halftime swap); conceding-team kickoff restart; **ignores held-ball goals**. |
| `MatchTimer.cs` | Quarters (90s) + win/lose/draw; pauses the clock during freezes; triggers the sprint duel each quarter; halftime swap; `ForfeitMatch()`. At full time / forfeit it calls `MatchResultUI.Show()` (falls back to the bare `resultText` if no MatchResultUI in the scene). |
| `ShotClock.cs` | 30s per-possession clock (singleton): resets on possession change / goal / defensive exclusion; turnover + grab-ban at 0; pauses when frozen, **during a free throw**, or match over; **a keeper hold does NOT reset it (keeps ticking until the keeper distributes)**. |
| `ExclusionManager.cs` | Fouls + exclusions (singleton): failed steal = foul → **free throw** to the fouled team; 2 fouls in 10s → 5s exclusion (roster slot nulled → AI auto-adapts) **or a PENALTY if the victim was in the 2m zone**; 3rd → removal; forfeit < 4 players; HUD countdowns. 🟡 New: **virtual foul** when the victim is an inside-water Centre (Centres draw exclusions/penalties faster; toggle `centerFoulBoost` — may be too hot, watch in testing). |
| `SprintDuel.cs` | Quarter-start duel (singleton): line-up + freeze, whistle, two sprinters race (human mashes Space), winner grabs → kickoff pass. |
| `EventFeed.cs` | Rolling last-5 event log (singleton): goals, exclusions, turnovers, out-of-bounds, forfeit, halftime. |
| `BallOutOfBounds.cs` | Top/bottom-wall out rule: a loose ball at the edge → possession to the nearest player of the team that didn't touch it last. |
| `PenaltyManager.cs` | Penalty shot (singleton, B16.11): on an exclusion-level foul inside the 2m zone, freezes play, puts the fouled shooter on the penalty spot (|x|≈2.47) facing the open corner, lines everyone else up **behind the shooter**. Human charges with **Space** within an aim cone; AI auto-fires after a delay (with a miss chance). The freeze lifts on the shot; a goal flows through the normal `Goal` path. |
| `GoalLineOut.cs` | Goal-line out rule (B16.11): a LOOSE ball crossing a goal line outside the mouth → re-enter just inside, nearest opponent gets it; a CARRIER pressing the end line → **corner restart** (ball + receiver placed at that corner). Awards to the team that didn't touch it last (deflection-aware via `LastTouchTeam`). |
| `BallTouchTracker.cs` | Sits on the **Ball**. Records the last team to physically touch a LOOSE ball, so a shot/pass that deflects off an opponent and goes out is awarded correctly. Ignores keeper touches and held-ball contacts. |
| `PlayerAnimator.cs` | Drives Animator for the human player. Reads speed from Rigidbody2D, IsHolding from PlayerMovement. Fires IsShooting trigger on fast release, IsStealing trigger on every grab attempt. Flips SpriteRenderer horizontally based on velocity.x. Defend animation triggers only when enemy carrier is within 1.5 units. |
| `BotAnimator.cs` | Drives Animator for AI bots. Reads state via IAgentBody. Same steal/defend/flip logic as PlayerAnimator. Reads isBlueTeam from BotMovement and swaps Animator controller to BlueAnimation.controller at Awake() if true. |
| `AnimationClipBuilder.cs` | Editor tool (Tools menu). Builds 7 animation clips (idle/swim/sprint/hold/throw/defend/steal) from sliced sprite sheets, assigns them to the Animator controller states, and wires all transitions. Two menu items: Tools/Build Water Polo Animations (red) and Tools/Build Blue Team Animations (blue). Creates BlueAnimation.controller programmatically if missing. |
| `GoalkeeperAnimator.cs` | Drives Animator on KeeperLeft and KeeperRight. Reads ball velocity and position from MatchContext to compute DiveState (0–7): idle, dive left/right, dive bottom-left/right, dive top-left/right, save. Single integer Animator parameter DiveState. SpriteRenderer flipX set in Awake based on keeper side. Shot height placeholder (0.5 = mid) ready for future height-zone system. |
| `GoalkeeperAnimationBuilder.cs` | Editor tool (Tools → Build Goalkeeper Animations). Builds 8 animation clips from goalkeeper_sheet.png frames, assigns them to GoalkeeperAnimation.controller states, wires DiveState int parameter and Any State transitions. Idempotent. |
| `TouchControls.cs` | Runtime-built mobile touch UI (no prefabs): virtual joystick bottom-left + SHOOT/PASS/SPRINT/SWITCH buttons bottom-right. Feeds the active player via `PlayerMovement.SetTouchInput` (merged with keyboard via `\|\|`); SWITCH consumed by TeamManager. Visible on mobile, or in Editor when `showInEditor`. Deliberately faint so the pool stays visible: ring alpha 0.10, knob 0.25, buttons 0.15 (Inspector sliders); labels fully opaque. |
| `PoolLineFloat.cs` | Standalone gentle bob (±0.04u) + sway (±1.5°) for the 12 pool lane-line sprites; random phase/speed (0.6–0.9 Hz) per object; offsets from the Start pose so it never drifts. |
| `MainMenuUI.cs` | MainMenu scene. Builds the whole main menu in code at runtime: canvas (1280x720), background + logo from `Assets/Resources/Sprites/`, PLAY/SETTINGS/QUIT buttons with hover scale + cyan-outline TMP labels, 1s fade-in, version footer. PLAY → SampleScene. |
| `MatchResultUI.cs` | Full-time result screen, built in code, hidden until `MatchTimer` calls `Show(title, outcome)`: dark 80% overlay, FULL TIME/FORFEIT title, "YOU n — n BOT" score from ScoreManager, colored winner line (cyan/red/yellow), PLAY AGAIN + MAIN MENU buttons; 0.5s unscaled-time fade-in (timeScale is 0 at match end). Singleton. |
| `PauseMenuUI.cs` | Pause system, built in code: 70x70 pause button top-right at (-20,-45) (sprite `Resources/Sprites/pause-button`; pulled down to clear the scoreboard), click → `Time.timeScale = 0` + centered 400x350 rounded panel with PAUSED + RESUME / QUIT / TEAM MANAGEMENT. QUIT opens a confirmation sub-panel ("If you quit, this match counts as a loss.") with YES QUIT (→ MainMenu) / CANCEL. TEAM MANAGEMENT is a placeholder (no functionality yet). Ignores clicks after full time (result screen owns the freeze). Works with mouse + touch. |

**Architecture rule for any AI:** keep `TeamSide` + `MatchContext` + `WaterPoloBrain`. It is roster-size-agnostic by design. To scale teams: add player/bot objects, drop them into the team `members` arrays + TeamManager arrays; formation & AI scale automatically.

## A6. Scene objects + wiring (the Hierarchy) — current 6v6 scene

> ⚠️ Slots are set by dragging objects from the Hierarchy into the Inspector. After any full-script replace, VERIFY these. The Unity Inspector is the real truth.

**Pool & arena**
- **Pool** — Square, Pos (0,0), Scale (16,9), blue.
- **Walls** (empty parent) → `WallTop`/`WallBottom`/`WallLeft`/`WallRight` — Squares, **Box Collider 2D (Is Trigger OFF)** at pool edges (±8 x, ±4.5 y). Top/bottom also act as out-of-bounds lines (handled in code by `BallOutOfBounds` via the ball's y — no wiring); left/right keep normal bounce physics.
- **PoolLines** — thin decorative strips (2m / 5m / half markings). Visual only, no colliders.

**Ball**
- **Ball** — Circle (~0.4), yellow, **Tag = "Ball"**, Order 1. Rigidbody2D: Gravity 0, Linear Damping 4, Collision Detection = Continuous. Circle Collider 2D (trigger OFF). Also has **`BallTouchTracker`** (no refs — pulls from MatchContext; tracks loose-ball deflections for the out rules).

**Players (your team, 6) — attack one end / defend the other; sides SWAP at halftime**
- **Player1 … Player6** — Circles (~0.5), red, Order 1. Each has: Rigidbody2D (Gravity 0, Freeze Rotation Z), Circle Collider 2D, a child **AimLine** (Line Renderer).
  - `PlayerMovement`: **Ball = Ball**, **Aim Line = its OWN AimLine child**, speed/grab/shoot/pass/steal tunables.
  - `TeammateAI`: **My Team = PlayerTeam** (+ AI tunables).
  - **Slot index in PlayerTeam.Members = role:** 0 Center, 1 Center-Back, 2/3 Wings, 4/5 Flats.

**Bots (enemy team, 6)**
- **Bot1 … Bot6** — Circles (~0.5), magenta. Each: Rigidbody2D + Circle Collider 2D + `BotMovement`: **My Team = BotTeam** (+ tunables).

**Goals & keepers**
- **GoalRight** (Pos (7,0)) / **GoalLeft** (Pos (-7,0)) — Squares (0.5,3), **Box Collider 2D Is Trigger ON**. `Goal`: Goal Side = "Right"/"Left", **Score Manager = ScoreManager**.
- **KeeperRight** (~(6.3,0)) / **KeeperLeft** (~(-6.3,0)) — thin tall Squares. Box Collider 2D (trigger OFF) + Rigidbody2D **Kinematic** (Use Full Kinematic Contacts ON, Gravity 0). `Goalkeeper`: **Ball = Ball**, Track Speed 4, Min/Max Y, and grab-and-control fields: Keeper Grab Distance 1.2, Keeper Grab Max Speed 3, Keeper Hold 1.5, Player Keeper Max Hold 3, Hold Offset 0.5. Keepers guard their physical goal even after the halftime swap.

**Managers — all components on ONE `GameManager` GameObject:**
- `MatchContext`: **Ball = Ball, Player Team = PlayerTeam, Bot Team = BotTeam**, Release Grab Delay 0.35, Free Throw AI Hold 3, Player Limit X 6.9, Counter Window 4.
- `TeamManager`: **Players = [Player1..6]**, **Teammate AIs = [Player1..6] (SAME ORDER)**, **Player Team = PlayerTeam**, **Defense Mode Text = DefenseModeText**.
- `MatchTimer`: **Score Manager = ScoreManager, Timer Text = TimerText, Quarter Text = QuarterText, Result Text = ResultText**, Quarter Length 90, Total Quarters 4.
- `ShotClock`: **Match Timer = (this GameManager's MatchTimer), Shot Clock Text = ShotClockText**, Shot Clock Seconds 30.
- `EventFeed`: **Feed Text = EventFeedText, Match Timer = MatchTimer**, Max Lines 5.
- `SprintDuel`: no required refs (pulls teams/ball from MatchContext); optional **Duel Text**; speed/timing tunables.
- `BallOutOfBounds`: no refs (pulls from MatchContext); Out Y Threshold 4.2, Reentry Inset 0.5.
- `PenaltyManager`: optional **Penalty Text = PenaltyText**; Penalty Spot X 2.47, Behind Spot Margin 1, Penalty Aim Cone 70, AI Shoot Delay 1, AI Miss Chance 0.25, AI Miss Offset 1.6, Penalty Shot Speed 13, Max Penalty Seconds 6.
- `GoalLineOut`: no refs (pulls from MatchContext); Goal Line X 7, Goal Mouth Half Height 1.5, Reentry Inset 0.5, Carrier Out X 6.7, Corner Inset X 6.2, Corner Y 3.5.

**Other manager objects (empty GameObjects)**
- **PlayerTeam** — `TeamSide`: Name "Player", **Attack Goal = GoalRight, Defend Goal = GoalLeft**, **Members = [Player1..6]**, formation + AI tunables, plus **attacking-spacing** (Teammate Spacing 2, Support Pass Range 5, Support Blend 0.5, Pass Openness Weight 1.5) and **tactics** (Center Feed Weight 3, Counter Runners 2, Drop Sag 0.5, Shot Quality Threshold 0.42, Free Throw Clearance 2.2) fields. (Defense mode is runtime-only, defaults Press.)
- **BotTeam** — `TeamSide`: Name "Bot", **Attack Goal = GoalLeft, Defend Goal = GoalRight**, **Members = [Bot1..6]**.
- **ScoreManager** — `ScoreManager`: **Ball = Ball, Score Text = ScoreText, Player Team = PlayerTeam, Bot Team = BotTeam**, Goal Freeze Seconds 1.
- **ExclusionManager** — `ExclusionManager`: **Match Timer = MatchTimer, Exclusion Text = ExclusionText**; Foul Window 10, Fouls For Exclusion 2, Exclusion 5, Max Exclusions 3, Min Players 4, Foul Steal Lockout 1.5, Penalty Zone X 4.28.

**UI — Canvas (TextMeshPro), + EventSystem (auto)**
- **ScoreText** ("YOU 0 - 0 BOT"), **TimerText** ("1:30"), **QuarterText** ("Q1"), **ResultText** (hidden until full time), **DefenseModeText** ("DEFENSE: PRESS/ZONE"), **ExclusionText** (exclusion countdowns), **ShotClockText** ("30"), **EventFeedText** (last 5 events), **PenaltyText** ("PENALTY!", hidden until a penalty; wired into `PenaltyManager.Penalty Text`).

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

**Sprint mechanic:**
- Player: Shift key = 2x speed multiplier. Shift + ball = IsLooseHold true
  (ball stays in hand but grab distance for enemies doubles — easier to strip)
- Bots: sprint decided by WaterPoloBrain IsDriving logic, unchanged

**SpriteRenderer flipping:**
- velocity.x > 0.1 → flipX = false (faces right, default)
- velocity.x < -0.1 → flipX = true (mirror)
- near zero → hold last value

**Known remaining issues (fix later):**
- Sprint animation not triggering correctly in all cases (IsSprinting threshold tuning needed)
- Idle/swim sprite size inconsistency (swim sprites slightly smaller — art fix needed in ChatGPT)

Goalkeeper animations: COMPLETE. goalkeeper_sheet.png (8 frames: idle, dive left/right, dive bottom-left/right, dive top-left/right, save). GoalkeeperAnimation.controller with DiveState integer parameter. GoalkeeperAnimator.cs on both KeeperLeft and KeeperRight.

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

## A9. Controls (keyboard — for PC testing; touch comes later)

- **WASD / arrows** — move active player.
- **Hold LeftShift** — **sprint** (2x speed while moving). Sprinting WITH the ball = **loose hold**: you keep the ball but opponents get 2x steal range + a steal-chance bonus (`looseHoldStealBonus` 0.15 on BotMovement/TeammateAI).
- **E** — grab / drop a loose ball.
- **Hold Space** — charge & shoot (release to fire).
- **Hold B** — charge & pass; auto-targets the teammate you're facing (nearest teammate as fallback).
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
- **Charged passing** — hold B to charge, auto-targets the teammate you're facing (nearest fallback), reusing the power bar.
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
- **Deferred visuals** (secondary per dev priority): keeper art/animation; crowd/stadium; camera zoom-out; water-flow effects. (Pool zone lines now exist as `PoolLines`.)
- **Other deferred:** per-player stamina system; weak no-hold deflection shot (a ball struck without a settled hold should be weaker than a settled one); corners on KEEPER deflections; referee.

## A12. Immediate roadmap (next bricks, rough order)

1. **Scale teams** — 4v4 first (verify formation+AI), then 6v6. Mostly cloning objects + adding to lists. ✅ **DONE** (now 6v6).
2. **Match timer + win condition** — game currently never ends. ✅ **DONE** (4 quarters, 30s each tunable, win/lose/draw at full time).
3. **Steal mechanic** — take ball from a holder (key + success chance); ties into fouls. ✅ **DONE** (human steal on Space + AI pressers strip the carrier; fouls not yet wired).
4. **Keeper grab-and-control** — keeper collects a slow loose ball, distributes to an open teammate (bot auto / player on B); clock keeps ticking through the hold. ✅ **DONE**.
5. Smarter AI: pass backward/around a block, better shot selection. ✅ **DONE** (shot-quality + pass-risk logic, center feed, counterattack, man-up/down shapes, Drop/MPress).
6. Rule systems: shot clock, quarters, exclusions (see Part B §16). ✅ **DONE** (incl. free throws + penalties + goal-line out).
7. Touch controls (virtual joystick + A/B/C + hand button) for mobile.
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

**Art style:** High quality semi-realistic cartoon. Characters are ~200px wide, shown from waist up, emerging from water. No legs visible.

**Approach:** Single bone rig per character type (field player / goalkeeper). Swap face sprite and cap color per player. All 240 players share the same animations.

**Body parts to rig separately:**
- Head with cap (cap color swappable)
- Face (swappable per player)
- Torso
- Left upper arm
- Left forearm and hand
- Right upper arm
- Right forearm and hand
- Water splash (separate child sprite, animates independently)
- Ball (separate sprite, hidden when loose, visible when holding)

**Ball visibility rule:** When player is holding the ball, hide the physics ball object and show the ball baked into the holding animation. When released, show physics ball again and hide animation ball.

**Full animation list — field players:**
1. Idle/floating — gentle upper body bob, water splash slow
2. Swimming — arms alternate stroke, faster splash
3. Sprinting — faster arm movement, bigger splash
4. Holding ball — ball raised in right hand, left arm out for balance
5. Charge shot — wind-up, arm pulls back further each frame
6. Shoot release — explosive forward throw, follow-through
7. Pass — side arm throw, lower than shot
8. Lob pass — high arc release, arm goes fully overhead
9. Skip shot — low fast release, arm comes down not up
10. Receiving/catching — both arms extend forward, close around ball
11. Steal attempt — one arm lunges sideways
12. Defending idle — both arms raised, blocking stance
13. Blocking shot — one arm shoots straight up
14. Foul committed — arms spread slightly, guilty pose
15. Celebration — fist pump, one arm raises
16. Excluded/ejected — arms down, swimming to corner

**Full animation list — goalkeeper:**
1. Idle — slight sway, hands at water level
2. Ready stance — arms wide, alert
3. Dive left/right/up/left-up/right-up/left-down/right-down/down — 8 directions (already partially done)
4. Save reaction — arms extended in save direction, hold pose
5. Distribution throw — same as field player pass
6. Celebration — same as field player

**Water splash rules:**
- Generate water splash as separate sprite sheet
- Idle = small slow ripple
- Swimming = medium directional splash
- Sprinting = large aggressive splash
- Receiving = small splash as player lunges

**Per-country customization:**
- Cap color changes per country (inspector color tint)
- Face sprite swaps per player (16 countries x 15 players = 240 faces)
- Skin tone adjusted via color tint on torso/arms
- Body shape stays identical across all players

**Difficulty notes:**
- Bone rigging: 2-3 days for one character
- Animating all states: 1-2 weeks
- Multiplying to 240 players: near zero extra work if rig is shared correctly
- Goalkeeper rig: separate 2-3 days
- Water splash sheet: 1 day
- Ball hide/show system: needs code change in PlayerMovement.cs and BotMovement.cs

**Next immediate steps:**
1. Generate body part sprites using AI (same style as current) — parts listed above, transparent background, consistent lighting
2. Import into Unity 2D Animation package
3. Rig one field player
4. Build all animations on that one rig
5. Test in game
6. Then clone and swap faces/caps for all 240 players

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

## B6. Main Screen Layout 🟡 PARTIAL (basic main menu DONE; full economy layout not started)
- ✅ **DONE (June 2026):** `MainMenu` scene with `MainMenuUI.cs` — entire menu built in code at runtime (no prefabs): full-screen canvas (1280x720 scale-with-screen), background + logo from `Assets/Resources/Sprites/` via `Resources.Load<Sprite>`, PLAY / SETTINGS / QUIT buttons (navy, white bold TMP with cyan outline, 1.05x hover scale), 1s fade-in, "Water Polo Manager v0.1" footer. PLAY loads SampleScene; SETTINGS is a stub (logs "coming soon"); QUIT quits.
- **Still future (the full vision):**
- **Top horizontal tab (always visible):** Settings icon + social link icon; Claim Rewards; Diamond currency (diamond + cyan bg + number); Gold currency (coin + number); Club logo + Team name.
- **Large buttons:** Career; Live ("Coming Soon", inactive).
- **Smaller buttons:** TEAM, TRANSFERS, My Club, Challenges.

## B7. Settings Screen ⬜ NOT STARTED
- Top tab stays; content area swaps; back arrow appears.
- Options: (1) Language `< >` instant — English/Russian/Georgian (+more). (2) Bot difficulty `< >` — Medium/Hard, default Medium. (3) Account — Log In/Out/Sign Up/Delete (Apple or Google Play); progress saved & synced across devices (Firebase planned). (4) Info links — FAQs, Legal Notices, ToS, System Info (external links).

## B8. Claim Rewards ⬜ NOT STARTED
- Popup: Season Pass + Activate Pass. Split horizontally: top = premium (pass) rewards, bottom = free. Rewards = coins/diamonds/items from wins/goals.

## B9. Currencies 🟡 PARTIAL (scoring exists; economy not)
- **Diamond:** icon + cyan bg + number; rare; buy high-rated random players / upgrade when gold short.
- **Gold:** coin + number; buy normal/good players, upgrade pool, upgrade players, buy caps/swimwear.
- Both have **+** → shop popup (real-money items/players via Apple/Google billing); purchase adds item to game.

## B10. Club Logo / Team Name Popup ⬜ NOT STARTED
- Manager standing, large club logo, overall team rating, changeable nationality flag, **Highlights** (saved goals), **Records** (games, W/L/D, goals for/against, biggest win/loss, win %, trophies).

## B11. Career Screen ⬜ NOT STARTED
- Full-screen change (not popup); top tab + bottom buttons stay; social icon → Home icon. Shows **Division 3** + ranked teams.
- **Standings:** other teams play simulated background matches (random scores); better performers rank higher; results visible.
- **Progression:** Div 3 (20 matches, 1st → cup → Div 2) → Div 2 → Div 1 → Champions League (repeatable).
- **Match screen (Division clicked):** full-screen; two symmetrical pools (left = you + cards + cup/logo, right = opponent), center Division logo + "Game 1 of 15", **PLAY** button.

## B12. Team Screen ⬜ NOT STARTED
- Full-screen; pool with 7 positions in water polo formation + subs. Drag to swap, upgrade, set captain, sell, save lineup.

## B13. Transfers Screen ⬜ NOT STARTED
- Daily random players (mostly low-level; tiered rare/golden chances). **Agents** cost diamonds → secret player by tier (Common 40 / Rare 150 / Golden 375 diamonds). Not enough diamonds → payment popup.

## B14. My Club Screen ⬜ NOT STARTED
- Full-screen. (1) Upgrade Stadium/Pool → more fans → more post-match money (win > loss). (2) Customize cap & swimwear (colors/designs).

## B15. Challenges Screen ⬜ NOT STARTED
- Popup; daily challenges ("Score 3 goals", "Win 5 games") → reward Gold + Diamonds.

## B16. MATCH GAMEPLAY (the core)

### B16.1 Pre-Match Intro ⬜
- Optional skippable intro (≤10s): both teams enter pool and warm up.

### B16.2 Match Start — Sprint Duel ✅ DONE
- At every quarter start (incl. Q1): ball at centre, all players line up on their own goal lines and freeze; after a whistle delay the two sprinters (each team's first available member) race. Bot swims at a fixed speed; the human **mashes Space** to go faster (boost decays). First to the ball grabs it; play + shot clock start. The winning AI centre then makes a **kickoff pass to its deepest teammate** before normal play. (`SprintDuel.cs`.)

### B16.3 Match Controls 🟡 PARTIAL (shoot + power-bar feedback, B-pass to nearest teammate, E=grab, Space-steal all built on keyboard; full A/B/C touch scheme not built)
- **A** — with ball: shoot (hold = power bar, directional arrow); without ball: aggressive defensive press. *(Charged-shot power bar for the active player is built ✅.)*
- **B** — with ball: regular pass (short=slow, long=fast, fast risks bad catch); without ball: pressure (not aggressive).
- **C** — with ball: high/long lob, late-game penalty-style lob (easier for keeper); without ball: manual player switch (auto-switch exists ✅; manual override).
- **Hand button ✋** — tap: pick ball up to hands; hold: water-polo hand movements; then A to shoot; single tap: release.
- **Joystick (bottom-right)** — 360° move; directs pass/shot aim via under-player arrow.
- **Swipes** — up = special evasion (pump fake/shoulder turn); down = different (reverse pivot); success = attacker rating vs defender rating; fail risks losing ball.
- **🟡 IN PROGRESS — planned shot/pass upgrades:**
  - **Charged shot** — hold Space = power + height; max charge = high shot, harder to block.
  - **Skip/bounce shot** — hold modifier key + Space.
  - **High lob pass** — hold pass key longer = high arc pass with shadow, harder to intercept.
  - **Block animation upgrade** — defending pose changes to one-arm raised block.

### B16.4 Camera & Visibility 🟡 PARTIAL (2D top-down + directional chevron done; player names not yet)
- Dream-League-style overhead angled; faces not clear in play. Name above each player; directional arrow below showing heading. *(A directional chevron indicator under the active player is built ✅; player-name labels still TODO.)*

### B16.5 Match Structure ✅ DONE (shot clock + halftime side-switch built)
- 4 quarters, **90s each** (tunable), win/lose/draw at full time. **30s shot clock** per possession — resets on possession change / goal / defensive exclusion; at 0 → turnover with a grab-ban on the violating team until the other side touches the ball. **Halftime side-switch** after the middle quarter: attack/defend goals swap, scoring stays correct, keepers keep their physical goal. Each quarter restarts through the sprint duel; the clock pauses during freezes.

### B16.6 HUD 🟡 PARTIAL (score display ✅, pause button ✅; logos/layout pass still to do)
- Score with both team logos ✅; quarter indicator; match timer; pause button ✅ (`PauseMenuUI`, top-right); exclusion countdown.

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
- **Visual Pass 1 COMPLETE:** 7-state animation system fully working in-engine for both red and blue teams. Red team: PlayerAnimation.controller on Player1–6. Blue team: BlueAnimation.controller on Bot1–6, blue cap sprites in BlueTeam folder. AnimationClipBuilder editor tool builds and wires everything (Tools menu). Steal animation fires on every grab attempt. Defend animation proximity-gated (1.5 units). Sprint mechanic with loose-hold strip bonus. SpriteRenderer horizontal flipping. **Remaining art:** goalkeeper animations; scale consistency between idle and swim/sprint sprites; 15 total animation states planned (7 done).
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
- Current focus: Visual Pass 1 complete (A7), pool water background complete (A8). Next priorities:
  (1) charged shot height system,
  (2) skip shot,
  (3) high lob pass with arc shadow,
  (4) touch controls (B16.3).
  Everything in Part B tagged ⬜ is future.
