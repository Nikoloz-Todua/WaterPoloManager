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

**Current state: a working 6v6 match with full defensive AI.** Core gameplay works (role-based positioning, marking, dynamic mark-switching, and a press/zone toggle); menus/economy/career are not built yet.

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
| `WaterPoloAI.cs` | **The shared brain** + `IAgentBody` interface. All AI decisions live here once: carrier (shoot/pass/dribble), support (get open), presser (nearest chases), defender (hold shape). Nothing is attached to this file — pure logic the others call. **This is C# state-machine AI — NOT an LLM.** |
| `TeamSide.cs` | One per team. Holds goals + roster (`members`), formation math (auto-spreads ANY number of players), passing/positioning logic. Scales 2v2 → 6v6 with no code change. |
| `MatchContext.cs` | Singleton "match truth": ball position, possession + last toucher, post-release grab cooldown, freeze flag, shot-clock grab-ban, kickoff-pass flag, halftime `SwapEnds()`, `GiveBallTo()` / `ForceDropHeldBall()`, `EnemyOf()`. |
| `TeamManager.cs` | On `GameManager`. Auto-switches control to the ball-holder; manual **C** switch (skips excluded); **Z** toggles Press/Zone; never auto-activates excluded players. |
| `Goalkeeper.cs` | Kinematic keeper sliding along its physical goal line tracking ball Y. Stays on its goal even after the halftime swap. |
| `Goal.cs` | Trigger on each net; reports `goalSide` ("Left"/"Right") to ScoreManager. |
| `ScoreManager.cs` | Team-based score (credits the team attacking that net → survives the halftime swap); conceding-team kickoff restart; **ignores held-ball goals**. |
| `MatchTimer.cs` | Quarters (90s) + win/lose/draw; pauses the clock during freezes; triggers the sprint duel each quarter; halftime swap; `ForfeitMatch()`. |
| `ShotClock.cs` | 30s per-possession clock (singleton): resets on possession change / goal / defensive exclusion; turnover + grab-ban at 0; pauses when frozen or match over. |
| `ExclusionManager.cs` | Fouls + exclusions (singleton): failed steal = foul; 2 in 10s → 5s exclusion (roster slot nulled → AI auto-adapts); 3rd → removal; forfeit < 4 players; HUD countdowns. |
| `SprintDuel.cs` | Quarter-start duel (singleton): line-up + freeze, whistle, two sprinters race (human mashes Space), winner grabs → kickoff pass. |
| `EventFeed.cs` | Rolling last-5 event log (singleton): goals, exclusions, turnovers, out-of-bounds, forfeit, halftime. |
| `BallOutOfBounds.cs` | Top/bottom-wall out rule: a loose ball at the edge → possession to the nearest player of the team that didn't touch it last. |

**Architecture rule for any AI:** keep `TeamSide` + `MatchContext` + `WaterPoloBrain`. It is roster-size-agnostic by design. To scale teams: add player/bot objects, drop them into the team `members` arrays + TeamManager arrays; formation & AI scale automatically.

## A6. Scene objects + wiring (the Hierarchy) — current 6v6 scene

> ⚠️ Slots are set by dragging objects from the Hierarchy into the Inspector. After any full-script replace, VERIFY these. The Unity Inspector is the real truth.

**Pool & arena**
- **Pool** — Square, Pos (0,0), Scale (16,9), blue.
- **Walls** (empty parent) → `WallTop`/`WallBottom`/`WallLeft`/`WallRight` — Squares, **Box Collider 2D (Is Trigger OFF)** at pool edges (±8 x, ±4.5 y). Top/bottom also act as out-of-bounds lines (handled in code by `BallOutOfBounds` via the ball's y — no wiring); left/right keep normal bounce physics.
- **PoolLines** — thin decorative strips (2m / 5m / half markings). Visual only, no colliders.

**Ball**
- **Ball** — Circle (~0.4), yellow, **Tag = "Ball"**, Order 1. Rigidbody2D: Gravity 0, Linear Damping 4, Collision Detection = Continuous. Circle Collider 2D (trigger OFF).

**Players (your team, 6) — attack one end / defend the other; sides SWAP at halftime**
- **Player1 … Player6** — Circles (~0.5), red, Order 1. Each has: Rigidbody2D (Gravity 0, Freeze Rotation Z), Circle Collider 2D, a child **AimLine** (Line Renderer).
  - `PlayerMovement`: **Ball = Ball**, **Aim Line = its OWN AimLine child**, speed/grab/shoot/pass/steal tunables.
  - `TeammateAI`: **My Team = PlayerTeam** (+ AI tunables).
  - **Slot index in PlayerTeam.Members = role:** 0 Center, 1 Center-Back, 2/3 Wings, 4/5 Flats.

**Bots (enemy team, 6)**
- **Bot1 … Bot6** — Circles (~0.5), magenta. Each: Rigidbody2D + Circle Collider 2D + `BotMovement`: **My Team = BotTeam** (+ tunables).

**Goals & keepers**
- **GoalRight** (Pos (7,0)) / **GoalLeft** (Pos (-7,0)) — Squares (0.5,3), **Box Collider 2D Is Trigger ON**. `Goal`: Goal Side = "Right"/"Left", **Score Manager = ScoreManager**.
- **KeeperRight** (~(6.3,0)) / **KeeperLeft** (~(-6.3,0)) — thin tall Squares. Box Collider 2D (trigger OFF) + Rigidbody2D **Kinematic** (Use Full Kinematic Contacts ON, Gravity 0). `Goalkeeper`: **Ball = Ball**, Track Speed, Min/Max Y. Keepers guard their physical goal even after the halftime swap.

**Managers — all components on ONE `GameManager` GameObject:**
- `MatchContext`: **Ball = Ball, Player Team = PlayerTeam, Bot Team = BotTeam**, Release Grab Delay 0.35.
- `TeamManager`: **Players = [Player1..6]**, **Teammate AIs = [Player1..6] (SAME ORDER)**, **Player Team = PlayerTeam**, **Defense Mode Text = DefenseModeText**.
- `MatchTimer`: **Score Manager = ScoreManager, Timer Text = TimerText, Quarter Text = QuarterText, Result Text = ResultText**, Quarter Length 90, Total Quarters 4.
- `ShotClock`: **Match Timer = (this GameManager's MatchTimer), Shot Clock Text = ShotClockText**, Shot Clock Seconds 30.
- `EventFeed`: **Feed Text = EventFeedText, Match Timer = MatchTimer**, Max Lines 5.
- `SprintDuel`: no required refs (pulls teams/ball from MatchContext); optional **Duel Text**; speed/timing tunables.
- `BallOutOfBounds`: no refs (pulls from MatchContext); Out Y Threshold 4.2, Reentry Inset 0.5.

**Other manager objects (empty GameObjects)**
- **PlayerTeam** — `TeamSide`: Name "Player", **Attack Goal = GoalRight, Defend Goal = GoalLeft**, **Members = [Player1..6]**, formation + AI tunables.
- **BotTeam** — `TeamSide`: Name "Bot", **Attack Goal = GoalLeft, Defend Goal = GoalRight**, **Members = [Bot1..6]**.
- **ScoreManager** — `ScoreManager`: **Ball = Ball, Score Text = ScoreText, Player Team = PlayerTeam, Bot Team = BotTeam**, Goal Freeze Seconds 1.
- **ExclusionManager** — `ExclusionManager`: **Match Timer = MatchTimer, Exclusion Text = ExclusionText**; Foul Window 10, Fouls For Exclusion 2, Exclusion 5, Max Exclusions 3, Min Players 4.

**UI — Canvas (TextMeshPro), + EventSystem (auto)**
- **ScoreText** ("YOU 0 - 0 BOT"), **TimerText** ("1:30"), **QuarterText** ("Q1"), **ResultText** (hidden until full time), **DefenseModeText** ("DEFENSE: PRESS/ZONE"), **ExclusionText** (exclusion countdowns), **ShotClockText** ("30"), **EventFeedText** (last 5 events).

## A7. Controls (keyboard — for PC testing; touch comes later)

- **WASD / arrows** — move active player.
- **E** — grab / drop a loose ball.
- **Hold Space** — charge & shoot (release to fire).
- **Hold B** — charge & pass; auto-targets the teammate you're facing (nearest teammate as fallback).
- **Space (when NOT holding)** — attempt steal (chance-based; must be in front of the carrier).
- **C** — manual player switch (mostly redundant: control auto-follows the ball-carrier).
- **Z** — toggle team defense between **Press** and **Zone**.

## A8. What's working today (DONE)

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

## A9. Known issues / tuning notes

- At 2v2, **passing is rare by design** (few open teammates) — comes alive at 6v6.
- Bots can feel **too strong** — lower Chase/Carry/Support speeds to tune.
- Some AI numbers (Shoot Range/Power ≈20) are placeholder/high — TUNABLE.
- Graphics are placeholder circles/squares on purpose — **art is a later phase, after gameplay is locked.**

**KNOWN ISSUES / NEXT:**
- Residual **clustering only when the ball + multiple players + an opponent genuinely converge** on the same spot — acceptable/realistic, not the old "everyone bunches" bug.
- **Done since last update:** sprint duel + kickoff pass; shot clock; halftime side-switch; exclusion system + forfeit; event feed; out-of-bounds; held-ball-goal fix; conceding-team goal restart.
- **Next brick:** **Penalty inside the 5m zone** (B16.11) — penalty shot when a foul occurs in the 5m area — then **keeper grab-and-control** (keeper collects a stuck ball; player controls the keeper to pass it out, then it returns to the line).
- **Deferred visuals** (secondary per dev priority): keeper art/animation; crowd/stadium; camera zoom-out; water-flow effects. (Pool zone lines now exist as `PoolLines`.)
- **Other deferred:** per-player stamina system; weak no-hold deflection shot (a ball struck without a settled hold should be weaker than a settled one); corners on keeper deflections; referee.

## A10. Immediate roadmap (next bricks, rough order)

1. **Scale teams** — 4v4 first (verify formation+AI), then 6v6. Mostly cloning objects + adding to lists. ✅ **DONE** (now 6v6).
2. **Match timer + win condition** — game currently never ends. ✅ **DONE** (4 quarters, 30s each tunable, win/lose/draw at full time).
3. **Steal mechanic** — take ball from a holder (key + success chance); ties into fouls. ✅ **DONE** (human steal on Space + AI pressers strip the carrier; fouls not yet wired).
4. **Keeper grab-and-control** — keeper grabs a stuck carrier's ball, player controls keeper to pass out, keeper returns to line.
5. Smarter AI: pass backward/around a block, better shot selection.
6. Rule systems: shot clock, quarters, exclusions (see Part B §16).
7. Touch controls (virtual joystick + A/B/C + hand button) for mobile.
8. Then the whole shell: menus, onboarding, currencies, career/divisions, store (Part B §1–15).
9. Android build/test (Build Support module + phone over USB). iOS needs a Mac later.

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

## B6. Main Screen Layout ⬜ NOT STARTED
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

### B16.4 Camera & Visibility 🟡 PARTIAL (2D top-down + directional chevron done; player names not yet)
- Dream-League-style overhead angled; faces not clear in play. Name above each player; directional arrow below showing heading. *(A directional chevron indicator under the active player is built ✅; player-name labels still TODO.)*

### B16.5 Match Structure ✅ DONE (shot clock + halftime side-switch built)
- 4 quarters, **90s each** (tunable), win/lose/draw at full time. **30s shot clock** per possession — resets on possession change / goal / defensive exclusion; at 0 → turnover with a grab-ban on the violating team until the other side touches the ball. **Halftime side-switch** after the middle quarter: attack/defend goals swap, scoring stays correct, keepers keep their physical goal. Each quarter restarts through the sprint duel; the clock pauses during freezes.

### B16.6 HUD ⬜ (score display ✅ done)
- Score with both team logos ✅; quarter indicator; match timer; pause button; exclusion countdown.

### B16.7 Pause Menu ⬜
- Timer stops; shows score, time elapsed, who scored & when. Options: Resume, Quit, Team Management. Subs in Team Management apply only after a goal/foul stop/exclusion end/quarter break.

### B16.8 In-Game Substitutions ⬜
- Players tap hands at pool edge; outgoing player must fully exit before new one enters; excluded/benched players uncontrollable during transition.

### B16.9 Exclusion System ✅ DONE (man-up/man-down via roster auto-adapt, not special-cased)
- A failed steal = foul (offender locked out, carrier keeps the ball). **2 fouls within 10s → 5s exclusion:** the player leaves its `TeamSide.members` slot (set null → formation + marking auto-adapt to the extra/missing man), parks in its goal corner, fully inert. **3rd exclusion → permanent removal** (GameObject disabled). If a team drops **below 4 players → forfeit** (other team wins, via `MatchTimer.ForfeitMatch`). HUD shows exclusion countdowns; event feed logs each. (`ExclusionManager.cs`.) Tunables: Foul Window 10, Fouls 2, Exclusion 5s, Max 3, Min Players 4.

### B16.10 AI Behaviour 🟡 PARTIAL (full defensive AI DONE in C#; only exclusion-based repositioning NOT yet)
- With ball → attack positions; lose ball → defensive positions; players hold assigned positions; opponent excluded → exploit extra man; own exclusion → shorthanded defense.
- **Built (defensive AI spec COMPLETE):** role-based positioning + 1-to-1 marking (nearest presses, others mark their man); facing-gated steal (no stealing from behind); dynamic threat-based mark-switching with hysteresis (coverage hands off automatically, no oscillation); selectable **Press vs Zone** defense for the player team (toggle **Z**, on-screen label) — Press = man-marking with switching, Zone = goal-side spread; bots always use Press.
- **Man-up / man-down:** now emerges automatically — an excluded player's roster slot is nulled, so formation spacing and marking re-solve for the extra/missing man with no special-case code (B16.9 done).
- **AI is C# state-machine logic (`WaterPoloBrain`), scaled by player stats.** The original "LLM-driven bots (LM Studio/llama.cpp/Claude API)" idea is **ABANDONED** — do not implement it; it's wrong for a real-time game.

### B16.11 Fouls & Rules 🟡 PARTIAL (fouls + exclusions + out-of-bounds + held-ball-goal done; penalties NOT)
- **Done:** failed-steal fouls + exclusions (see B16.9); **out-of-bounds** off the top/bottom walls (a loose ball at the edge → possession to the nearest player of the team that didn't touch it last, ball re-enters just inside, shot clock resets, "Out - YOU/BOT" feed line); **held-ball goals ignored** (only released/loose balls can score). Minor foul = possession change is approximated by the steal/foul system.
- **NOT yet:** penalty shot for a foul inside the 5m zone; corners on keeper deflections; poolside referee.

### B16.12 Goals & Replays 🟡 PARTIAL (goal detection + scoring DONE; replays/celebrations/sounds not)
- Goal → auto replay; player can save replay (→ Club highlights); celebrations; specific crowd sounds.

### B16.13 Post-Match ⬜
- Final whistle → earn coins; if enough progress, pass rewards + daily task rewards.

## B17. Art & Character Notes 🟡 (placeholders now; real art is a later phase)
- Believable body types/faces. In live play faces not detailed (Dream-League style). Goal replays use close-up → detailed faces matter there. **2D approach:** small simple sprites in-match; higher-detail 2D portraits for cards/managers/replays/celebrations. Art is deliberately deferred until gameplay is locked. (Old SceneKit/3D-mesh/GLTF notes are obsolete — this is a 2D Unity game.)

---

## FOR AN AI READING THIS

- Unity 6 / C# **2D** water polo game. Keep `TeamSide` + `MatchContext` + `WaterPoloBrain`. AI is **C# state machines, not an LLM.**
- Explain Unity steps **beginner-level, step-by-step** (name the panel + exact menu path).
- **After any full-script replace, remind Nikoloz to re-check drag-and-drop slots** (Part A6) and say exactly which object/slot.
- Don't suggest: Swift/SDL2/SceneKit, LLM-driven bots, web deployment, Stripe/PayPal, Tailwind. Mobile payments = Apple/Google billing, later.
- Nikoloz has **Claude Code in VS Code** — big multi-file AI work goes there; single-file features + guidance happen in chat.
- Commit routine: `git add . && git commit -m "..." && git push`. GitHub: https://github.com/Nikoloz-Todua
- Current focus: core match rules are in (6v6, sprint duel, shot clock, halftime swap, exclusions/forfeit, out-of-bounds, event feed). **Next: penalty inside the 5m zone (B16.11), then keeper grab-and-control.** Everything in Part B tagged ⬜ is future.
