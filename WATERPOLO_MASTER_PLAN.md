# Water Polo Manager — master plan and AI handoff

Last updated: **2026-07-20**. Read this file fully before changing gameplay. This is a
medium-detail handoff: it preserves operational knowledge, scene wiring, system contracts,
important tunables, and current priorities without repeating every historical session verbatim.
Use Git history for retired implementations and the owning code for line-level detail.

## 1. Project snapshot

- Unity **6000.4.7f1**, C#, 2D top-down water polo. Target: Android/iOS; development on Windows.
- Project root: `C:\Users\PC\Desktop\WaterPoloManager`.
- Build command: `dotnet build Assembly-CSharp.csproj`.
- Current build result: **0 errors, 0 warnings**.
- Build Settings contain exactly:
  1. `Assets/Scenes/MainMenu.unity`
  2. `Assets/HubScene.unity`
  3. `Assets/Scenes/SampleScene_PoolB.unity`
- `SampleScene_PoolB` is the sole live match scene. `SampleScene.unity`, recovery scenes, and
  `CharacterRig.unity` are retired/reference-only; do not propagate fixes into them unless asked.
- Current match: working 6v6, four quarters, shot clock, keepers, fouls, exclusions, penalties,
  out-of-bounds rules, halftime end swap, AI tactics, mobile controls, animation, and goal replay.
- The working tree contains intentional uncommitted work from the July 20 autonomous pass. Never
  reset or overwrite unrelated changes. `.claude/` is pre-existing and unrelated.

## 2. Rules for future AI work

1. Investigate before editing: read the owning script, inspect live `SampleScene_PoolB` YAML values,
   and use runtime logs/Play mode when behavior depends on Unity. Code defaults do not override
   serialized scene values.
2. Do not claim a runtime visual is fixed from static reading alone. State what was actually built,
   logged, or observed.
3. Do not delete art/code without a full-project reference audit. Preserve user assets and dirty
   worktree changes.
4. Do not touch recently verified systems merely for cleanup or speculative polish. Make the
   smallest evidence-backed change.
5. Do not generate placeholder art unless explicitly requested. Work with supplied assets.
6. After each gameplay fix: run the build, report errors/warnings, and add a dated bullet to section
   10. Include the observed cause, decision, changed files, and verification when those facts will
   prevent a future repeat; update existing current-state sections too.
7. Keep this file useful rather than artificially tiny. A practical target is **400–700 lines**:
   enough for a new AI to work safely, without restoring multi-thousand-line duplicated logs.
8. Explain manual Unity steps to Nikoloz with exact menu/Hierarchy/Inspector directions.
9. After a full script replacement, warn that Inspector drag-and-drop references may need checking.
10. This project is Unity/C# with state-machine AI. Ignore obsolete Swift, SDL2, SceneKit, web, or
    LLM-bot ideas. Future payments must use Apple/Google in-app billing.
11. Avoid PowerShell `echo >` for project files; encoding has caused corruption before.

## 3. Runtime ownership map

| Area | Authority |
|---|---|
| Match truth | `MatchContext.cs`: ball, possession, last touch/releaser/keeper touch, freeze state, grab bans, keeper/free-throw/foul protection, halftime goals. |
| Human control | `PlayerMovement.cs`; `TeamManager.cs` owns active-player switching and defense-mode cycling. |
| Field AI | `WaterPoloAI.cs` shared brain through `TeammateAI.cs` and `BotMovement.cs`; `TeamSide.cs` owns formation, tactics, target scoring, restart shapes, and pass selection. |
| Keepers | `Goalkeeper.cs` + `GoalkeeperAnimator.cs`: saves, catches, manual player-keeper control, bot distribution, safe zone, stamina. |
| Ball | `BallFlight.cs`: pass/lob/shot arcs, skip shot, air sprite/shadow, trail and spin. `BallTouchTracker.cs` must stay on the real Ball. |
| Boundaries | `GoalLineOut.cs` owns grounded loose balls beyond `|x|=7` outside the mouth. `BallOutOfBounds.cs` stands down when it does, and owns top/bottom/full escape otherwise. |
| Goals | `Goal.cs` reports to `ScoreManager.cs`; scoring uses actual line-crossing projection, team attack direction, one-goal restart latch, net reaction, replay, then conceding-team restart. |
| Replay | `GoalReplaySystem.cs`, auto-installed by `ScoreManager`: rolling transform/sprite/camera recorder, cinematic playback, Skip UI, exact state restoration. |
| Match flow | `MatchTimer.cs`, `ShotClock.cs`, `SprintDuel.cs`, `PenaltyManager.cs`, `ExclusionManager.cs`, `QuarterBreakUI.cs`, `MatchResultUI.cs`. |
| Camera/UI | `CameraFollow.cs`, `TouchControls.cs`, `PauseMenuUI.cs`, `EventFeed.cs`. Runtime UIs are mostly code-built. |
| Animation | `PlayerAnimator.cs`, `BotAnimator.cs`, `PlayerFlipbookSet.cs`, `PlayerVisualRuntime.cs`; editor setup is in `Assets/Editor/AnimatorBuilder.cs`. |
| Meta game | `NavigationManager.cs`, `RosterManager.cs`, `PlayerDatabase.cs`, `TeamScreenUI.cs`, `ShopUI.cs`, packs, missions, season pass, rewards, and `LeagueSeason.cs`. |

### 3.1 Core script contracts

- `PlayerMovement` owns only the currently human-controlled field player's input and ball actions.
  It merges keyboard and `TouchControls` input, maintains the aim direction, charges shot/pass
  power, owns held-ball parenting, and reports releases/possession to `MatchContext`. Do not add a
  second possession truth inside it.
- `TeamManager` switches the active human field player and cycles defense modes. Its player and
  `TeammateAI` arrays must remain index-aligned. It never activates an excluded/null roster slot.
- `TeammateAI` and `BotMovement` are thin `IAgentBody` adapters. Shared tactical behavior belongs in
  `WaterPoloAI`; team geometry, roles, pass scoring, and defense modes belong in `TeamSide`.
- `TeamSide.members` is the live roster and formation authority. Index roles are Center,
  Center-Back, Wings, then Flats. Null entries represent exclusions, so all loops and formations
  must tolerate missing members.
- `MatchContext` is the only cross-system source for possession, live ball position, last touch,
  last releaser, keeper touch/hold, freezes, free throws, foul protection, counter state, kickoff
  pass state, and grab bans. A held Rigidbody2D position is stale; use `MatchContext.BallPosition`.
- `BallFlight` owns all above-water presentation and temporary collision suppression. Call its
  public launch/note APIs; do not create a parallel arc or directly fake a ball transform from a
  player script.
- `Goalkeeper` stays attached to a physical goal side even after teams swap ends. The defending
  team is resolved dynamically from `TeamSide.defendGoal`; never hardcode left/right as bot/player.
- `ScoreManager` owns one validated goal from line crossing through replay and centre restart.
  `restartInProgress` is the re-entry guard. Goal effects must be prepared before the goal frame or
  otherwise avoid allocation-heavy work there.
- `GoalLineOut` has first refusal for goal-line exits. `BallOutOfBounds` owns top/bottom and full
  escapes only after asking GoalLineOut whether it owns the case.
- `MatchTimer`, `ShotClock`, and `ExclusionManager` use `CompressedTimer`: real seconds drive rules;
  display seconds are presentation. Do not compare printed clock values to gameplay deadlines.
- `GoalReplaySystem` is presentation-only. It records live poses, runs while gameplay is frozen,
  disables physics, then restores the exact live goal state. It must never decide score, collision,
  possession, AI, or restart outcomes.
- `PlayerAnimator`/`BotAnimator` choose state and palette while `PlayerFlipbookSet` supplies frames.
  `PlayerVisualRuntime` is the shared visual helper. Gameplay scripts should expose state, not swap
  art directly.
- Most hub screens are procedural canvases built by `NavigationManager` or a screen-specific class.
  Persistent data belongs in a manager/data model, not in transient UI GameObjects.

### 3.2 Live scene authority and required wiring

`Assets/Scenes/SampleScene_PoolB.unity` is the scene to inspect and save. The important hierarchy
contracts are:

- `Main Camera`: orthographic, tagged MainCamera, with `CameraFollow`; no target references are
  required because it reads `TeamManager.ActivePlayer` and `MatchContext`.
- `Ball`: tagged `Ball`, root authored at approximately `(0.04, 0.04, 1)`, Rigidbody2D with
  continuous collision, solid CircleCollider2D, and `BallTouchTracker`. `BallFlight` is auto-added.
  Any system that temporarily parents it must preserve world size on detach.
- `Player1..Player6`: Rigidbody2D, collider, `PlayerMovement`, `TeammateAI`, animator/flipbook
  presentation, and each player's own aim helper. `PlayerMovement.ball` points to the real Ball.
- `Bot1..Bot6`: Rigidbody2D, collider, `BotMovement`, and bot animator/flipbook presentation.
- `KeeperLeft`/`KeeperRight`: kinematic Rigidbody2D, solid collider, `Goalkeeper`,
  `GoalkeeperAnimator`, and keeper stamina. They are not members of the six field-player arrays.
- `GoalLeft`/`GoalRight`: trigger colliders matching the mouth, each with `Goal` and correct
  `goalSide`. They both report to the same `ScoreManager`.
- `PlayerTeam`/`BotTeam`: `TeamSide` objects with opposite attack/defend goals and six ordered field
  members. `MatchTimer.SwapEnds` swaps the teams' goals at halftime; physical keepers do not swap.
- `GameManager`: holds `MatchContext`, `TeamManager`, `MatchTimer`, `ShotClock`, `EventFeed`,
  `SprintDuel`, `BallOutOfBounds`, `GoalLineOut`, and other match coordinators. Keep singleton
  references unique.
- `ScoreManager`: ball, two score TMP references, both TeamSide references, and goal/restart
  settings. It auto-installs `GoalReplaySystem` on the same host.
- `ExclusionManager`: timer/HUD references and foul/exclusion tunables. `PenaltyManager` owns only
  the penalty setup/shot sequence.
- Match Canvas: separate YOU/BOT score texts, timer, quarter, shot clock, defense mode, exclusions,
  event feed, result text, and optional penalty text. `BotNameText` has a specifically repaired TMP
  component and CanvasRenderer.
- `TouchControls`, pause, replay, quarter-break, result, sprint-duel, and most hub UI are built at
  runtime. Absence of their child GameObjects in edit mode is normal.

High-value serialized values to verify after a script replacement or scene conflict:

| Object/component | Required truth |
|---|---|
| `MatchContext` | real Ball; PlayerTeam/BotTeam; player X limit about 6.9; release-grab delay 0.5 |
| `TeamManager` | six players and six teammate-AI entries in identical order |
| `PlayerTeam` | attacks right at Q1 start; members are Player1..6 |
| `BotTeam` | attacks left at Q1 start; members are Bot1..6 |
| `GoalRight` / `GoalLeft` | side strings exactly `Right` / `Left`; trigger colliders cover only the mouth |
| `ScoreManager` | Ball + both score labels + both teams |
| `MatchTimer` | 90 real seconds, 480 displayed seconds, four quarters |
| `ShotClock` | 15 real seconds, 30 displayed seconds |
| `ExclusionManager` | center foul boost 0; 20 displayed / 7.5 real exclusion seconds |
| `BallFlight` | settle ripple disabled; it is normally runtime-added, not separately scene-authored |
| flipbook assets | directional-swimming toggle off; horizontal swim is the current fallback |

### 3.3 Self-bootstrapping and persistence boundaries

- `GoalReplaySystem`, field-player stamina, several data managers, and multiple procedural screens
  self-install. Before adding a scene copy, search for `RuntimeInitializeOnLoadMethod`, `Instance`,
  and `EnsureExists` to avoid duplicate singletons.
- Runtime UIs use a 1280×720 reference canvas and must remain usable with mouse and touch. Device
  safe-area validation is still required even when Editor layout looks correct.
- Roster/club, missions, leaderboard, season pass, and reward slots use local JSON/PlayerPrefs.
  These are separate saves with their own version assumptions; a UI rewrite must not silently
  change their keys or erase a file.
- `LeagueSeason` is currently session-static tournament state. The career flow does not yet have a
  single durable match-result pipeline joining gameplay, standings, rewards, and season rollover.

## 4. Current gameplay truth

- Teams scale through `TeamSide.members`; the live scene is 6 field players per side plus two
  keepers. Formation and AI adapt to exclusions.
- Passing is directional. Every field pass uses a small BallFlight arc; F+B/touch LOB uses the
  larger lob. Field AI predicts a moving receiver at actual pass arrival time. Human and manual
  keeper passes remain aim-driven.
- Space/Shoot charges a shot; Q+Space is a skip shot. High charge uses the airborne shot arc.
  A deliberate Shoot press within 0.75u of a grounded loose ball makes a weak 6u/s one-touch
  redirect instead of first collecting it.
- Airborne balls are not grabbable until landing. Held-ball goals do not count.
- Goal scoring requires motion into the net and a projected crossing inside `|y| <= 1.5` at
  `|x| = 7`; trigger overlap alone is not a goal.
- Goal restarts never use a sprint duel. Sprint duel runs only at quarter starts. After goal/replay,
  the conceding team receives the centre restart.
- Keeper deflection over its own goal line outside the mouth is a corner for the attacker. A later
  field-player touch clears keeper-specific corner ownership.
- Ordinary failed legal steal gives a free throw. Two qualifying fouls in 10 seconds escalate;
  inside-2m escalation becomes a penalty. Rear/blindside contact is exclusion-level.
- `centerFoulBoost` is **0**. Do not restore virtual double-counting: it made the first Centre foul
  immediately become a penalty.
- A post-foul carrier has a 5-second steal-protection window, ending early on release.
- Match clock displays 8:00 over 90 real seconds per quarter. Shot clock displays 30 over 15 real
  seconds. Both pause under their established freeze conditions.

### Controls

- Movement: WASD/axes; hold Left Shift to sprint.
- Hold Space to shoot; Q while charging for skip; hold B to pass; hold F+B for lob.
- E attempts pickup; Space against a carrier is the steal path.
- C switches active player; Z cycles Press → Zone → Drop → MPress.
- Quarter sprint duel: tap Space/Shift or the screen repeatedly.
- Mobile: joystick plus three context buttons. Attack = Sprint/Shoot/Pass with separate LOB toggle;
  defense = Switch/Defend/Block. The same controls route to the player keeper while it holds.
- Goal replay: `SKIP >` by mouse/touch; Space, Escape, or Enter also skips.

### 4.1 Match-state sequences

Quarter start:

1. Freeze play, reset touch history, pin the ball at exact centre, and hide ordinary touch actions.
2. Show the five-second sprint-duel countdown.
3. At GO, the selected sprinters race while all other swimmers jog toward formation.
4. First valid grab wins possession; kickoff-pass restrictions engage; UI/play resume.
5. This sequence runs at Q1–Q4 starts only, never after a goal or ordinary turnover.

Validated goal:

1. Reject held balls, wrong-way motion, corner grazes, and projected crossings outside the mouth.
2. Credit the team currently attacking that physical net and close `restartInProgress`.
3. Update score/event, capture the exact live scoring frame, focus the live camera on ball/net, and
   freeze all gameplay. The ball is parked safely just behind the goal line.
4. Let the net hit and camera shake read for about 0.55 seconds.
5. Play the skippable three-pass highlight. Restore the frozen in-net state exactly afterward.
6. Reset to centre and hold the wide overview; spread both teams naturally.
7. Give the conceding team the ball, wait through the silent restart pause, then unfreeze and reset
   the shot clock. No sprint duel occurs.

Ordinary foul/exclusion:

1. Failed legal steal becomes a visible foul/free throw; the carrier gains short steal protection.
2. Escalation inside the defined window becomes exclusion, or penalty inside the 2m zone.
3. Excluded roster member becomes null, waits at its own defending-side pen, and returns onto a live
   defensive spot with cleared stale AI intent.
4. Teams and formations must work while short-handed; do not maintain a separate 5v6 formation list.

Boundary ownership:

- Goal-line outside the mouth: `GoalLineOut`, including keeper-deflection corner logic.
- Top/bottom escape: `BallOutOfBounds`, awarded using last-touch truth.
- Full escape beyond the arena: settle beat, then keeper restart.
- During any freeze or airborne arc, boundary handlers must stand down.

### 4.2 AI and tactical behavior

- Shared AI states are carrier, support, presser, and shape defender. A loose ball still retains its
  last-touch attacking context for shape: the nearest eligible player chases while teammates keep
  their attacking spread instead of collapsing as if possession flipped.
- Catching a fast moving loose ball is stricter than picking up a settled ball: the receiver must be
  close and facing it. Airborne balls are never caught until `BallFlight` lands them.
- The carrier can shoot, find a scored pass, drive after beating a marker, or dribble. Under pressure
  it has a least-bad clear outlet fallback instead of holding forever.
- Pass choice combines lane safety, receiver openness, distance, role/tactical bonuses, and predicted
  receiver position at arrival. Do not alter those weights when fixing only trajectory timing.
- Dynamic Centre play fights for inside water; wings/Flats preserve width. Screen/drive intent is
  cleared after exclusions, teleports, and state resets.
- Defense modes are Press, Zone, Drop, and MPress. Human cycles them with Z/touch. Bot defense can
  choose Drop while short-handed, protecting a late lead, or after repeated Centre goals.
- Current movement values intentionally reduce same-point convergence: AI chase/support 1.2,
  bot carry 0.9, player-team AI carry 0.5. Non-forced close shoot distance is 4.
- Keepers auto-collect slow loose balls and probabilistically save fast shots using shot power,
  height, skip state, and stamina. The player's keeper becomes manually controllable while holding;
  the bot keeper distributes automatically or early under pressure.

### 4.3 Ball presentation and collision rules

- Flat shots/passes use the live Rigidbody2D. Every field pass still has at least a small visual arc;
  LOB and high shots use larger `BallFlight` arcs with the root collider temporarily disabled.
- Arc kinds are distinct: Pass is quick/small, Lob is floaty/high, Shot is asymmetric and hangs near
  its peak. High-shot aim remains the raw chosen direction; landing points are bounded to valid water.
- Skip shot bounces before the target goal line and may fool a keeper. Point-blank cases fall back to
  a safe flat presentation instead of constructing an invalid arc.
- Release self-collision is temporarily ignored so the ball does not depenetrate off its own thrower.
  Collision is restored only after genuine separation.
- The root sprite, airborne child, shadow, trail, glow, scale, and spin are mutually coordinated.
  Any replay or VFX change must verify that only one visible ball exists.
- The rejected loose-ball `BallDropRipple` animation remains off. Net deformation/ripple on a goal is
  a separate ScoreManager presentation and must not be confused with water-settle effects.

### 4.4 Animation and color pipeline

- Current field swimmers use locked sprite flipbooks, not the retired bone-body experiment.
  `PlayerAnimator` and `BotAnimator` map gameplay state into idle/swim/sprint/hold/throw/defend/steal
  presentation; throwing is intentionally unified.
- Runtime palette materials recolor teams without duplicating every sprite sheet. Destroy only the
  per-renderer material instance a script itself created; never destroy a shared source material.
- Horizontal swim frames plus mirroring are the current reliable movement presentation.
  Directional up/down frame assets remain in the project but are not the active behavior.
- Held-ball offsets and flipbook size differ by state. The ball should follow a hand/hold anchor when
  present and otherwise use the proven body-relative offset.
- Water-cover/depth ordering is state-driven. A visual-size fix must be tested while idle, swimming,
  sprinting, holding, throwing, and stunned on both teams.
- Editor builders under `Assets/Editor` are setup tools, not runtime authorities. Re-running one can
  modify import/controller assets broadly; audit diffs before saving.

## 5. Goal replay — current implementation

- Records a continuous real-time window of up to **4 seconds** at a target 20 Hz, including all
  swimmers, keepers, ball visual children, active sprites, camera pose, and the exact goal frame.
- Every frame stores its real capture timestamp. Playback interpolates those timestamps instead of
  assuming perfect 0.05-second samples; this prevents replay speed/content drift at 30/60 fps or
  during a frame hitch.
- It does not show that full buffer. Only the final **1.25 source seconds**—the decisive shot/net
  approach—play three times at **1.00x, 0.82x, and 0.68x**, with short black repeat cuts and a
  0.28-second final hold. The replay badge identifies pass 1/3, 2/3, and 3/3.
- Replay camera framing follows the recorded ball toward the scored-on net with a 1.15-unit
  look-behind offset, while staying slightly wide enough to show the mouth and shooting lane.
- The live camera also focuses on the same impact for the short pre-replay goal beat.
- A pause, goal, quarter break, sprint duel, penalty setup, or app suspension starts fresh history.
  Replays never splice unrelated action across frozen phases.
- Replay runs only inside `MatchContext.PlayFrozen`. It snapshots/restores roots, sprites,
  Rigidbody2D simulation/velocity/awake state, auxiliary renderers, and camera. `CameraFollow` is
  paused rather than reset so its smoothing state survives.
- Root size is recorded in **world scale**, because the ball changes parent while held/released.
  Root SpriteRenderers no longer reapply the same transform a second time. These two rules prevent
  the tiny ~0.04 Ball root from becoming oversized when replay poses run with it detached.
- History frames and live-restore buffers are allocated at match startup. The goal path stores
  references into the frozen rolling buffer instead of deep-copying every root/sprite array. This
  removes the large replay allocation burst from the exact ball-crossing frame.
- The latest clip exists only in match memory. Permanent Save/Club Highlights is not built; it
  needs stable serialization IDs, storage/versioning limits, and records UI.
- Fallback: if fewer than two frames exist, `ScoreManager` uses the original in-net hold.

Replay invariants:

1. Playback must be the recorded event, never a physics re-simulation.
2. All three passes begin/end on the same source frames; only playback speed changes.
3. Skip restores the frozen live state before the ordinary restart continues.
4. Ball world size, child shadow/air-sprite state, sprite order, and active state must match capture.
5. No history frame may be overwritten while replay is active; `PlayFrozen` interrupts recording.
6. Saved highlights remain a separate future feature. Do not expose a fake Save button that cannot
   survive scene/app exit.

## 6. Verified working / do not touch without evidence

- `PlayerAnimator` and `BotAnimator` are enabled and working. A July 20 request accidentally
  disabled them and was immediately reverted. Do not disable the Parts/flipbook presentation.
- `PlayerFlipbookSet.useDirectionalSwimmingFrames` is **0**. Up/down sheets are retained for later
  replacement, but current swimming deliberately uses the proven horizontal sheet and mirroring.
- `BallDropRipple` is intentionally disabled by `BallFlight.settleRippleEnabled = false` after the
  developer rejected its look. Keep the PNG/frame-set asset, but do not re-enable it.
- The old generic `WaterEffectsSystem` and its “weird symbol” procedural particle renderers were
  removed after reference audit. Do not recreate them.
- Ball/world scale matters: the Ball root is about 0.04 scale. World effects must not inherit that
  scale accidentally.
- `BallTouchTracker` belongs on Ball, not GameManager. GoalLineOut has priority over the generic
  escape handler for goal-line positions; this avoids duplicate/dead-ball races.
- AI live values were empirically tuned: chase 1.2, support 1.2, bot carry 0.9, player-team AI carry
  0.5, non-forced shoot range 4. Do not retune without Play-mode evidence.
- Steal baseline is distance 1.5 and chance 0.4; touch Block has its distinct lower-risk behavior.
- Current true-convergence clustering around a contested ball is accepted, not the old whole-team
  collapse bug.
- Field AI pass weights are established. Arrival prediction changed only target/lane timing; do not
  “fix” it by altering tactical weights.
- The current camera, keeper control, goal restart, quarter duel, rule ownership, animation, and
  PC/mobile button-press behavior are confirmed systems. Integrate through their APIs.
- `BotNameText` in PoolB has a repaired TMP component/CanvasRenderer. Do not copy broken recovery
  scene serialization back over it.
- LOB intentionally reuses the existing pass icon because no dedicated lob art exists.

## 7. Scene and asset gotchas

- Live serialized values in `SampleScene_PoolB.unity` beat script initializers. Inspect both.
- Resource PNGs used as whole UI images must import as Sprite Mode **Single**; auto-sliced Multiple
  made tiny fragments load through `Resources.Load<Sprite>`. Some trimmed UI loaders also require
  `isReadable: 1`.
- Runtime-created systems generally need no scene wiring: replay, touch UI, stamina auto-install,
  roster/data services, and several overlays self-bootstrap.
- When editing YAML, audit every fileID reference before removing a component. Recovery scenes are
  evidence only, not live targets.
- Do not bulk-clean `Assets/Sprites/Players/Parts`. Old-looking assets can still be referenced by
  controllers, clips, Resources assets, or editor tooling.
- The URP water Shader Graph `_MainTex` message is cosmetic. Do not replace the working pool water
  merely to silence it.

### Performance-sensitive paths

- The live scene can contain roughly 700 crowd renderers. A goal celebration previously scanned all
  seats and changed about half their sprites in one Update, producing a repeatable score-frame hitch.
  `CrowdSpawner` now checks at most 96 seats per frame, completing the same random ~50% celebration
  over several frames. Preserve this amortization and profile it on Android.
- Replay capture once allocated a new clip, a `ReplayFrame`, and root/sprite arrays for every sample
  on the exact goal frame. Those buffers are now preallocated and the frozen ring frames are
  referenced directly. Do not restore deep-copying unless it is moved fully off the live frame and
  justified by a real persistent-save design.
- Net pulse/ripple GameObjects are prepared in `ScoreManager.Awake` and reused. Do not instantiate a
  burst of particles or load a sprite synchronously on scoring.
- Crowd fan idle code reads a ball-X value cached once per `CrowdSpawner.Update`; do not make every
  fan query `MatchContext` independently.
- Runtime UI construction is acceptable at screen creation, but not every frame. Cache component,
  font, sprite, and manager references used by match Update loops.
- Avoid `FindObjectsByType`, hierarchy scans, LINQ, string joins, and new arrays on ball collision,
  goal, steal, or AI-per-agent Update paths. Startup/editor usage is less sensitive.
- A clean C# build proves compilation only. For frame hitches, use Unity Profiler Timeline + GC Alloc
  on the device or a development build and capture the frame before, at, and after goal entry.

## 8. Meta-game status

Built and usable:

- Main menu → Hub → Game Mode → competition/standings → pre-match → PoolB match.
- Local JSON roster, currencies, starters, team overview, club identity/colors/crest/country.
- Shop/pack reveal, missions, season pass, post-match reward slots, local leaderboards/shell UI.
- Group/knockout competition scaffolding in `LeagueSeason.cs`.

Not integrated:

- Firebase authentication/cloud sync/remote config/storage.
- Real-money IAP beyond the `IAPBridge` stub.
- Persistent player upgrade levels (roster primarily stores IDs), complete career result reporting,
  promotion/relegation, a real transfer market, and online/social systems.

### 8.1 Meta-game data and screen map

| System | Current responsibility | Important limitation |
|---|---|---|
| `RosterManager` | local roster, starters, currencies, club identity/save funnel | upgrade progression and full roster rules incomplete |
| `PlayerDatabase` / `PlayerData` | player definitions and card data | portraits/content set still limited |
| `NavigationManager` | procedural Hub, navigation, profile/currency cluster, Game Mode flow | large class; avoid adding business truth to button callbacks |
| `TeamScreenUI` | roster/team presentation | drag/swap, captain, roster cap incomplete |
| `ClubCustomizationUI` | name, crest, country, primary/secondary colors | procedural crest/flag placeholders await approved art |
| `CardPack` / `PackRevealUI` | unified pack tiers, odds, opening and duplicate conversion | economy balance is provisional |
| `ShopUI` | pack/deal/free/ad/economy shelf | IAP uses a stub; offers are not production commerce |
| `MissionManager` | newcomer/daily/weekly/Global Cup progress and claims | only real tracked stats should be exposed |
| `SeasonPassManager` | canonical 14-day epoch, XP, free/gold tracks | Gold activation price is placeholder economy |
| `LeaderboardManager` | local deterministic league ladder | non-league online tabs are honest locked stubs |
| `PostMatchRewardManager` | four persistent timed reward slots | must be fed by a completed real match flow |
| `LeagueSeason` | groups, fixtures, simulated opponents, knockout bracket | session-static; durable career integration incomplete |
| `IAPBridge` | one integration seam for purchases | currently logs/succeeds; must be replaced with store billing |

Data rules:

- All reward grants should pass through the established roster/reward funnel so balances, packs, and
  duplicate conversion stay consistent.
- Pack identity is `CardTier`; do not recreate the retired parallel shop-pack enum.
- The season-pass epoch is the canonical season clock shared by relevant seasonal systems.
- Fake local rivals are permitted only where clearly presented as simulated league data. Friends,
  country, world, cloud, and social surfaces remain locked until real accounts/services exist.
- Real-money purchases must ultimately use Apple/Google billing through `IAPBridge`; never ship the
  success-immediately stub as production commerce.
- A completed career loop must report one authoritative `MatchResult` into standings, missions,
  season XP, leaderboard points, rewards, knockout state, and persistence without double-awarding.

## 9. Priority backlog

1. **Play-test and profile the three-pass goal highlight** on desktop and mobile: same source moment
   three times, correct ball scale, ball/net framing, total duration, Skip, high-ball shadow, exact
   restoration, and a Profiler capture of the scoring frame.
2. **Android device pass:** controls, safe areas, goal-frame time/GC, crowd celebration ramp, scene
   transitions, save persistence, and build configuration on real hardware.
3. **Career loop completion:** record actual match results into `LeagueSeason`, rewards, standings,
   knockout progression, promotion/relegation, and season reset.
4. **Team management completion:** drag/swap starters, captain, roster cap, portraits, persistent
   upgrades; later expose safe substitutions through the pause menu.
5. **Transfers/economy completion:** replace fake cards/countdown with catalog-backed offers and
   balance real earning/spending before adding IAP.
6. **Saved replays/highlights:** serialize replay data, add retention/versioning, records browser,
   save/delete controls. Do not fake this with only an in-memory button.
7. **Presentation requiring approved assets:** celebrations, crowd/goal audio, names, referee,
   onboarding/manager/captain screens, and art polish. Do not generate substitutes automatically.

Deferred design question: a 1v1 keeper close-range low/chip/pass action was discussed but remains
under-specified. Do not build it without a clearer control/behavior decision.

## 10. Compact change log

- **2026-07-20v — three-pass goal highlight + score-hitch fixes + handoff expansion:** replaced the
  overlong replay with the final 1.25s repeated at 1.00x/0.82x/0.68x; camera now tracks the ball/net.
  Replay ball-size root cause was parent-relative root scale plus the root SpriteRenderer applying
  that transform a second time; capture now stores world scale and root renderers no longer overwrite
  it. Goal hitch had two concrete synchronous spikes: full replay deep-copy allocation on line
  crossing and CrowdSpawner changing hundreds of ~700 fan sprites in one Update. Replay buffers are
  preallocated/reference-backed, and crowd checks are capped at 96 per frame. Expanded this handoff
  from the over-compressed 225-line version with scene/system/data/performance details. The Unity
  log also proved LiberationSans lacked the replay button's triangle glyph, so `SKIP ▶` became the
  supported `SKIP >` instead of rendering a square/warning. Build: 0 errors, 0 warnings.
- **2026-07-20u — replay timing + plan compaction:** replay now uses actual sample timestamps,
  captures up to 7.5s, plays at 0.78x with a longer 0.42x finish and 0.8s goal hold. Replaced the
  multi-thousand-line duplicated plan with a compact handoff. Superseded by 2026-07-20v timing and
  documentation balance. Build: 0 errors, 0 warnings.
- **2026-07-20t — automatic goal replay:** added rolling capture, cinematic wider camera,
  letterbox/score/scorer UI, slow finish, Skip, frozen-state restoration, and discontinuity reset.
- **2026-07-20f–s — autonomous bug/maintenance pass:** disabled Centre virtual foul double-count;
  added weak loose-ball redirect; completed keeper-deflection corners; moved BallTouchTracker to
  Ball; resolved goal-line/escape ownership; repaired BotNameText; removed missing LOB-icon load;
  added AI receiver-arrival prediction; migrated obsolete Unity lookups. Clean runtime/editor builds.
- **2026-07-20b–e — visual reversals:** removed generic WaterEffects after identifying its placeholder
  symbols; BallDropRipple remains disabled; field-player animation disable was immediately reverted.
- **2026-07-19 — animation/gameplay tuning:** locked current flipbook presentation, horizontal swim
  fallback, held-ball anchor correction, state fixes, AI speed/range tuning. Do not casually retune.
- **2026-07-12–18 — gameplay feel/rules:** waterline presentation, honest aim/passing, OOB/goal
  containment, foul/stun rules, successful-steal stun, palette/flipbook runtime architecture.
- **2026-07-06 — scene authority:** retired pool selection and `SampleScene`; PoolB became the only
  gameplay scene. Added crowd, penalty corners, and cameraman presentation.
- **June–July foundation:** 6v6 AI/tactics, keeper control, timers/rules, sprint duel, local roster,
  hub/team/shop/packs/missions/season pass/competition shell, and Visual Pass 1 animation.

## 11. Replay test required now

1. Play at least four continuous seconds and score with a normal flat shot. Confirm the replay shows
   only the final ball-to-net moment—not a long buildup—and repeats that exact moment **three times**.
2. Confirm order/speed: pass **1/3 at normal speed**, **2/3 slightly slower**, **3/3 slowest**, with a
   very short black cut between them and no long frozen ending.
3. Compare the replay ball against the live Ball immediately before/after. It must remain the same
   small world size on all three passes; no giant ball and no second overlapping ball sprite.
4. Watch framing: the short live goal beat and all replay passes should keep the moving ball, final
   shooting lane, and scored-on net visible. Camera should not remain centred on a distant swimmer.
5. Profile one goal if possible. The line-crossing frame should no longer contain a replay deep-copy
   allocation burst; crowd cheer sprite swaps should ramp over several frames instead of one spike.
6. Skip one replay with `SKIP >`, one with Space, and one with Escape/Enter. Each score increments
   exactly once, restores cleanly, and still gives the conceding team the centre restart.
7. Score with a high-ball/arc shot. Verify root sprite, airborne child, shadow, scale, and sorting
   reproduce the live action and restore with no invisible or duplicate ball.
8. Score shortly after a quarter start or other freeze. Footage must start only in the new continuous
   live segment and never jump into countdown, penalty, pause, or a prior goal.
9. After replay, verify actor positions, ball physics/velocity, helper renderers, camera smoothing,
   touch controls, timers, possession, and shot clock all resume through the established flow.
