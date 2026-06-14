using UnityEngine;

// Implemented by every AI-driven swimmer (a bot or a non-active teammate) so the
// shared brain can read its tunables and drive its body without caring which it is.
public interface IAgentBody
{
    Rigidbody2D Body { get; }
    Transform Tf { get; }
    TeamSide Team { get; }
    bool IsHolding { get; set; }
    Vector2 LastDirection { get; set; }

    float ChaseSpeed { get; }
    float CarrySpeed { get; }
    float SupportSpeed { get; }
    float GrabDistance { get; }
    float HoldOffset { get; }
    float ShootRange { get; }
    float ShootPower { get; }   // used as a deterministic shot SPEED (units/sec)

    float StealChance { get; }
    float LooseHoldStealBonus { get; } // extra steal chance vs a Shift-sprinting (loose-hold) carrier
    float HoldStartTime { get; set; }
    float NextStealTime { get; set; }

    // Dynamic-marking state (anti-oscillation): who we're currently marking and the
    // earliest time we're allowed to switch to a different man.
    Transform CurrentMark { get; set; }
    float NextMarkSwitchTime { get; set; }

    // Drive state (Feature 1): a carrier bursting toward the 2m point after beating
    // its marker.
    bool IsDriving { get; set; }
    Vector2 DriveTarget { get; set; }

    // Screen / pick state (Feature 2). The screener tracks where it's planting and
    // since when; the CARRIER gets ScreenBoostUntil — until that time its marker
    // counts as beaten for the drive trigger.
    bool IsSettingScreen { get; set; }
    Vector2 ScreenTarget { get; set; }
    float ScreenBoostUntil { get; set; }
    float ScreenStartTime { get; set; }  // when this agent began its screen
    float ScreenSetSince { get; set; }   // when it first planted on the spot (-1 = not planted)

    // true while a human controls this swimmer; the brain then stands down.
    bool Suppressed { get; }
}

// All decision-making and ball handling for AI swimmers lives here, once.
// Roles each tick:
//   carrier  (holds the ball)      -> shoot / pass / dribble
//   support  (teammate has ball)   -> get open ahead of the carrier
//   presser  (nearest, we don't)   -> chase the ball / pressure the carrier
//   defender (everyone else)       -> hold a goal-side shape
public static class WaterPoloBrain
{
    const float ArriveDistance = 0.25f; // close enough to a target spot
    const float PassFactor = 2.5f;      // pass speed per unit of pass distance
    const float MinPassSpeed = 6f;
    const float MaxPassSpeed = 13f;
    const float SteerAwayWeight = 0.8f; // how hard the carrier veers off a defender
    const float SettleDelay = 0.4f;     // must hold the ball this long before shooting
    const float MaxCarrySeconds = 1.8f;    // carrier force-shoots after holding this long (anti-stall / more aggressive)
    const float CloseShootDistance = 4f;   // within this of goal, prefer shooting over dribbling
    const float StealCooldown = 0.6f;   // min time between steal attempts
    const float StealFacingDot = 0.3f;  // stealer must be within ~70° of the carrier's front
    const float LobInterceptFactor = 0.4f; // enemy lob in flight: steal chance reduced by 60%
    const float IdleDriftFraction = 0.2f; // idle float speed as a fraction of move speed
    const float IdleRadius = 0.35f;       // how far the idle bob point sits from the spot
    const float IdleFreq = 1.2f;          // idle bob speed (rad/s)
    const float MarkSwitchCooldown = 0.6f; // min time before a defender may switch its man
    const float KickoffPassSettle = 0.4f;  // AI carrier settles this long before the kickoff pass
    const float KeeperProtectRadius = 2.5f; // a presser can't crowd a ball-holding keeper
    const float MinTeammateSeparation = 1.2f; // teammates never pack tighter than this (lower priority yields)

    // ---- drives (Feature 1) ----
    const float DriveSpeedMult = 1.35f;      // drive burst over normal carry speed
    const float DriveBeatenDistance = 1.5f;  // marker must be this close (and behind us) to trigger
    const float DriveBeatenDot = 0.3f;       // marker direction vs our facing below this = "behind"
    const float DriveLaneRadius = 0.8f;      // lane width needed to the 2m point
    const float DriveShootDistance = 1.0f;   // this close to the drive target → shoot
    const float DriveCaughtDistance = 1.2f;  // marker back within this (and in front) ends the drive

    // ---- picks / screens (Feature 2) ----
    const float ScreenArriveDistance = 0.4f; // close enough to the screen spot to plant
    const float ScreenSetSeconds = 0.2f;     // planted this long → the screen is "set"
    const float ScreenRubDistance = 1.2f;    // carrier passing this close to a set screen → boost
    const float ScreenBoostSeconds = 0.5f;   // how long the carrier counts as having beaten its man
    const float MaxScreenSeconds = 3f;       // never hold a screen longer than this
    const float ScreenAbandonDistance = 3.5f;// carrier strays this far from the screen → give up
    const float ScreenMarkerRange = 2f;      // an enemy this close to the carrier counts as its marker

    public static void Tick(IAgentBody a, MatchContext ctx)
    {
        if (ctx == null || a.Team == null || ctx.Ball == null) return;

        // Safety net for EVERY agent: if we think we're holding but the ball isn't
        // actually parented to us, drop the illusion. (A stale "holding" flag is what
        // left agents green/aiming with no ball and pinned/snapped the ball in place.)
        if (a.IsHolding && ctx.Ball.transform.parent != a.Tf)
            a.IsHolding = false;

        // No ball → any leftover drive is stale.
        if (!a.IsHolding && a.IsDriving) a.IsDriving = false;

        // A human just took control of this swimmer → drop the ball, stop steering.
        if (a.Suppressed)
        {
            if (a.IsHolding) Release(a, ctx);
            a.IsDriving = false;
            a.IsSettingScreen = false;
            return;
        }

        if (a.IsHolding)
        {
            if (a.IsDriving) DriveCarry(a, ctx);
            else Carry(a, ctx);
            return;
        }

        // Collect a genuinely loose ball within reach (cooldown stops snatch-backs;
        // CanGrab enforces the shot-clock turnover ban on the violating team). An enemy
        // HIGH LOB in flight is hard to pick off — reduced-chance roll inside.
        if (ctx.BallGrabbable && ctx.CanGrab(a.Team) &&
            Vector2.Distance(a.Body.position, ctx.BallPosition) <= a.GrabDistance &&
            !HumanTeammateCloserToBall(a, ctx) &&
            TryCollectLoose(a, ctx))
            return;

        TeamSide enemy = ctx.EnemyOf(a.Team);

        if (ctx.TeamHasBall(a.Team))
        {
            a.CurrentMark = null; // we attack → drop any defensive assignment

            // COUNTERATTACK: designated advanced runners sprint at the enemy goal.
            if (ctx.CounterActiveFor(a.Team) &&
                a.Team.IsCounterRunner(a.Tf, ctx.Ball.transform.parent))
            {
                MoveTo(a, a.Team.CounterRunTarget(a.Tf), a.ChaseSpeed);
                return;
            }

            // MAN-UP (6-on-5): enemy is a player short → spread into the 4-2 umbrella.
            int enemyOut = (ExclusionManager.Instance != null && enemy != null)
                ? ExclusionManager.Instance.ExcludedCount(enemy) : 0;
            if (enemyOut > 0)
            {
                MoveTo(a, a.Team.ManUpSpot(a.Tf, ctx.BallPosition), a.SupportSpeed);
                return;
            }

            // PICKS (Feature 2): the nominated screener plants a block on the carrier's
            // marker; everyone else holds the normal shape.
            if (TryScreen(a, ctx, enemy)) return;

            // normal: hold the role shape (width), drift toward a support outlet only when
            // genuinely useful, keep spacing off the ball.
            MoveTo(a, a.Team.AttackTarget(a.Tf, ctx.BallPosition, enemy), a.SupportSpeed);
            return;
        }

        // Not attacking → any screen state is stale.
        a.IsSettingScreen = false;

        // ---- sanctioned free-throw gate (Task 2): respect the enemy's free throw ----
        // We're in the defensive branch, so the free-throw taker is on the enemy team.
        bool enemyFreeThrow = ctx.FreeThrowActive && ctx.FreeThrowCarrier != null;
        if (enemyFreeThrow)
        {
            Vector2 cpos = ctx.FreeThrowCarrier.position;
            if (Vector2.Distance(a.Body.position, cpos) < a.Team.freeThrowClearance)
            {
                // too close to the taker → back straight off to the respect distance
                Vector2 away = a.Body.position - cpos;
                if (away.sqrMagnitude < 1e-4f) away = Vector2.down;
                MoveTo(a, cpos + away.normalized * a.Team.freeThrowClearance, a.SupportSpeed);
                return;
            }
        }

        // COUNTER-PREVENTION (Part 3): during the enemy's fast-break window, our furthest-up
        // player sprints back to guard the cage instead of joining the press.
        if (ctx.CounterActiveFor(enemy) && a.Team.MostAdvancedMember() == a.Tf)
        {
            a.CurrentMark = null;
            MoveTo(a, a.Team.CenterZoneSpot(ctx.BallPosition), a.ChaseSpeed);
            return;
        }

        // Ball is loose or the enemy has it: the single nearest swimmer presses; the rest
        // follow the active scheme (Press / Zone / Drop / MPress / man-down).
        Transform presser = a.Team.ClosestMemberTo(ctx.BallPosition);

        // MAN-DOWN (5-on-6): we're short → collapse compact in front of goal (ignore mode);
        // the nearest still lightly presses the ball.
        bool manDown = ExclusionManager.Instance != null &&
                       ExclusionManager.Instance.ExcludedCount(a.Team) > 0;
        if (manDown)
        {
            a.CurrentMark = null;
            if (presser == a.Tf && !enemyFreeThrow) ChaseBall(a, ctx);
            else MoveTo(a, a.Team.ManDownSpot(a.Tf, ctx.BallPosition), a.SupportSpeed);
            return;
        }

        if (presser == a.Tf && !enemyFreeThrow) // no pressing/chasing/stealing during a free throw
        {
            if (TryStealAI(a, ctx, enemy)) return;
            ChaseBall(a, ctx);
        }
        else if (a.Team.defenseMode == TeamSide.DefenseMode.Zone)
        {
            // Zone: forget individual marks, protect the space in front of our goal.
            a.CurrentMark = null;
            MoveTo(a, a.Team.DefendSpot(a.Tf, ctx.BallPosition), a.SupportSpeed);
        }
        else if (a.Team.defenseMode == TeamSide.DefenseMode.MPress)
        {
            // press (man-mark) everyone EXCEPT the centre defender, who sits in the help zone.
            if (a.Team.RoleOf(a.Tf) == TeamSide.Role.Center)
            {
                a.CurrentMark = null;
                MoveTo(a, a.Team.CenterZoneSpot(ctx.BallPosition), a.SupportSpeed);
            }
            else
            {
                Transform mark = ResolveMark(a, ctx, enemy, presser);
                MoveTo(a, mark != null ? a.Team.MarkSpot(a.Tf, mark)
                                       : a.Team.DefendSpot(a.Tf, ctx.BallPosition), a.SupportSpeed);
            }
        }
        else if (a.Team.defenseMode == TeamSide.DefenseMode.Drop)
        {
            DropDefense(a, ctx, enemy, presser);
        }
        else // Press: threat-based 1-to-1 marking with dynamic switching (unchanged)
        {
            Transform mark = ResolveMark(a, ctx, enemy, presser);
            Vector2 spot = mark != null ? a.Team.MarkSpot(a.Tf, mark)
                                        : a.Team.DefendSpot(a.Tf, ctx.BallPosition);
            MoveTo(a, spot, a.SupportSpeed);
        }
    }

    // ---- Drop (help) defense: centre defender fronts the enemy Centre; the nearest other
    // defender sags toward the help zone; everyone else marks. Concedes outside shots. ----
    static void DropDefense(IAgentBody a, MatchContext ctx, TeamSide enemy, Transform presser)
    {
        // centre defender denies the inside feed by fronting the enemy Centre
        if (enemy != null && a.Team.RoleOf(a.Tf) == TeamSide.Role.Center)
        {
            Transform enemyCenter = (enemy.members != null && enemy.members.Length > 0) ? enemy.members[0] : null;
            a.CurrentMark = null;
            MoveTo(a, a.Team.FrontSpot(enemyCenter, ctx.BallPosition), a.SupportSpeed);
            return;
        }

        // the nearest other defender sags off its man toward the help zone
        Vector2 zone = a.Team.CenterZoneSpot(ctx.BallPosition);
        if (NearestNonCenter(a.Team, presser, zone) == a.Tf)
        {
            Transform mark = ResolveMark(a, ctx, enemy, presser);
            Vector2 markSpot = mark != null ? a.Team.MarkSpot(a.Tf, mark)
                                            : a.Team.DefendSpot(a.Tf, ctx.BallPosition);
            MoveTo(a, Vector2.Lerp(markSpot, zone, Mathf.Clamp01(a.Team.dropSag)), a.SupportSpeed);
            return;
        }

        // everyone else marks normally
        Transform mk = ResolveMark(a, ctx, enemy, presser);
        MoveTo(a, mk != null ? a.Team.MarkSpot(a.Tf, mk)
                             : a.Team.DefendSpot(a.Tf, ctx.BallPosition), a.SupportSpeed);
    }

    // Nearest member to `point` that isn't the presser or the centre defender.
    static Transform NearestNonCenter(TeamSide team, Transform exclude, Vector2 point)
    {
        if (team == null || team.members == null) return null;
        Transform best = null; float bestD = Mathf.Infinity;
        foreach (Transform m in team.members)
        {
            if (m == null || m == exclude) continue;
            if (team.RoleOf(m) == TeamSide.Role.Center) continue;
            float d = Vector2.Distance(m.position, point);
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }

    // ---- dynamic marking: pick the best man with hysteresis so we don't oscillate ----
    // Every defender runs the SAME deterministic greedy plan this tick (they all read
    // identical positions), so the team agrees on coverage without shared state.
    static Transform ResolveMark(IAgentBody a, MatchContext ctx, TeamSide enemy, Transform presser)
    {
        if (enemy == null) { a.CurrentMark = null; return null; }

        Transform carrier = ctx.Ball.transform.parent; // pressed by the presser → not a mark target
        Transform ideal = ComputeGreedyMark(a, ctx, a.Team, enemy, presser, carrier);
        if (ideal == null) ideal = a.Team.MarkAssignmentFor(a.Tf, enemy); // index fallback

        Transform current = a.CurrentMark;
        bool currentValid = current != null && current != carrier && IsMemberOf(current, enemy);

        if (!currentValid)
        {
            // lost our man (gone, or he's now the pressed carrier) → reassign immediately
            a.CurrentMark = ideal;
            a.NextMarkSwitchTime = Time.time + MarkSwitchCooldown;
        }
        else if (ideal != null && ideal != current && Time.time >= a.NextMarkSwitchTime)
        {
            // cooldown elapsed and the plan wants a different man → switch
            a.CurrentMark = ideal;
            a.NextMarkSwitchTime = Time.time + MarkSwitchCooldown;
        }
        // else: keep CurrentMark (hysteresis prevents per-frame flipping)

        return a.CurrentMark;
    }

    // Greedy assignment: rank attackers by threat, give each the closest still-free
    // defender. Returns the man assigned to `a` (or null if `a` got none). The pressed
    // carrier and the presser are excluded so nobody is double-marked while a man is free.
    static Transform ComputeGreedyMark(IAgentBody a, MatchContext ctx, TeamSide team,
                                       TeamSide enemy, Transform presser, Transform carrier)
    {
        Transform[] mates = team.members;
        Transform[] foes = enemy.members;
        if (mates == null || foes == null) return null;

        Vector2 ballPos = ctx.BallPosition;
        bool[] foeDone = new bool[foes.Length];
        bool[] mateUsed = new bool[mates.Length];
        Transform result = null;

        for (int picked = 0; picked < foes.Length; picked++)
        {
            // highest-threat unhandled attacker (skip nulls + the pressed carrier)
            int bestFoe = -1;
            float bestThreat = float.NegativeInfinity;
            for (int i = 0; i < foes.Length; i++)
            {
                if (foeDone[i] || foes[i] == null || foes[i] == carrier) continue;
                float th = team.ThreatScore(foes[i], ballPos, enemy);
                if (th > bestThreat) { bestThreat = th; bestFoe = i; }
            }
            if (bestFoe < 0) break;
            foeDone[bestFoe] = true;

            // closest unused defender (never the presser) covers this attacker
            Vector2 foePos = foes[bestFoe].position;
            int bestMate = -1;
            float bestDist = float.PositiveInfinity;
            for (int j = 0; j < mates.Length; j++)
            {
                Transform m = mates[j];
                if (m == null || mateUsed[j] || m == presser) continue;
                float d = Vector2.Distance(foePos, m.position);
                if (d < bestDist) { bestDist = d; bestMate = j; }
            }
            if (bestMate < 0) continue; // no defender left for this attacker

            mateUsed[bestMate] = true;
            if (mates[bestMate] == a.Tf) result = foes[bestFoe]; // this is OUR man
        }

        return result;
    }

    static bool IsMemberOf(Transform t, TeamSide team)
    {
        if (t == null || team == null || team.members == null) return false;
        foreach (Transform m in team.members) if (m == t) return true;
        return false;
    }

    // Keep a swimmer inside the play area — can't cross the goal line. Called from each
    // body's FixedUpdate during LIVE play only (the bodies return before this while frozen
    // or excluded, so duel/penalty/corner placements are never clamped). Ball + keepers
    // never call this.
    public static void ClampX(Rigidbody2D body, float limitX)
    {
        if (body == null) return;
        Vector2 p = body.position;
        if (p.x > limitX)
        {
            body.position = new Vector2(limitX, p.y);
            if (body.linearVelocity.x > 0f) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
        }
        else if (p.x < -limitX)
        {
            body.position = new Vector2(-limitX, p.y);
            if (body.linearVelocity.x < 0f) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
        }
    }

    // Keep the held ball glued in front of us (called from LateUpdate).
    public static void KeepHeldBall(IAgentBody a, MatchContext ctx)
    {
        if (a == null || ctx == null || ctx.Ball == null) return;
        if (!a.IsHolding) return;

        // If we're no longer the parent, clear the stale flag instead of pinning the
        // ball to a world point (this was the "ball freezes in place" bug).
        if (ctx.Ball.transform.parent != a.Tf) { a.IsHolding = false; return; }

        ctx.Ball.transform.localPosition = (Vector3)(a.LastDirection * a.HoldOffset);
    }

    // ---- carrier: shoot, pass, or dribble ----
    static void Carry(IAgentBody a, MatchContext ctx)
    {
        a.CurrentMark = null; // we hold the ball → no defensive assignment

        // Free throw: the fouled AI carrier settles in place (shot clock paused), then
        // decision-making resumes once the hold elapses and the flag clears.
        if (ctx.FreeThrowActive && ctx.FreeThrowCarrier == a.Tf)
        {
            if (Time.time - ctx.FreeThrowStartTime >= ctx.FreeThrowAIHoldSeconds)
                ctx.ClearFreeThrow();
            else { a.Body.linearVelocity = Vector2.zero; return; }
        }

        // Kickoff pass: an AI carrier's first action is one pass back to its deepest
        // teammate, then normal play. (Human carriers never reach here — suppressed.)
        if (ctx.KickoffPassPending && ctx.KickoffPassTeam == a.Team)
        {
            if (Time.time - ctx.KickoffPassTime < KickoffPassSettle)
            {
                a.Body.linearVelocity = Vector2.zero; // settle briefly so it looks natural
                return;
            }
            Transform deep = a.Team.DeepestMember(a.Tf);
            ctx.ClearKickoffPass();
            if (deep != null) { Pass(a, ctx, deep); return; } // reuse the normal pass path
            // no valid teammate → fall through to normal carry
        }

        TeamSide team = a.Team;
        if (team.attackGoal == null) { Release(a, ctx); return; }

        TeamSide enemy = ctx.EnemyOf(team);
        Vector2 pos = a.Body.position;
        Vector2 goal = team.attackGoal.position;
        Vector2 aim = team.ShotAimPoint(pos);                 // a corner, not the keeper
        float goalDist = Vector2.Distance(pos, goal);

        float quality = team.ShotQuality(pos, enemy);
        float heldTime = Time.time - a.HoldStartTime;
        bool closeToGoal = goalDist <= CloseShootDistance;

        // 1) SHOT SELECTION (Part 4): in range AND good enough quality (distance / angle /
        //    lane / pressure) → square up, then shoot. Bad-angle or blocked looks fall
        //    through to a drive or a pass instead. FORCE a shot if we've stalled on the
        //    ball too long (anti-dribble-forever) — fired immediately, no settle wait.
        bool forceShot = heldTime >= MaxCarrySeconds;
        if (forceShot || (goalDist <= a.ShootRange && quality >= team.shotQualityThreshold))
        {
            a.LastDirection = (aim - pos).normalized;
            if (forceShot || Time.time - a.HoldStartTime >= SettleDelay) Shoot(a, ctx);
            else a.Body.linearVelocity = Vector2.zero; // square up and wait
            return;
        }

        // 1.5) DRIVE (Feature 1): the shot isn't there, but our marker is beaten (or a
        //      screen just freed us) and the lane to the 2m point is open → attack the cage.
        //      Skipped when we're already close — we'd rather shoot than dribble in further.
        if (!closeToGoal && quality < team.shotQualityThreshold &&
            TryStartDrive(a, ctx, team, enemy, pos))
            return;

        // 2) PASS: a man-up or a counter wants the ball moved fast (lower threshold); else an
        //    open teammate ahead, or any open mate while pressured.
        int enemyOut = (ExclusionManager.Instance != null && enemy != null)
            ? ExclusionManager.Instance.ExcludedCount(enemy) : 0;
        bool quick = enemyOut > 0 || ctx.CounterActiveFor(team);
        bool pressured = quick || TeamSide.NearestDistance(pos, enemy) < team.pressDistance;
        Transform mate = team.BestPassTarget(a.Tf, enemy, pressured);
        if (mate != null) { Pass(a, ctx, mate); return; }

        // 2.5) CLOSE RANGE: within CloseShootDistance with no pass available → SHOOT rather
        //      than dribbling in any further (bots were over-dribbling at the cage).
        if (closeToGoal)
        {
            a.LastDirection = (aim - pos).normalized;
            if (Time.time - a.HoldStartTime >= SettleDelay) Shoot(a, ctx);
            else a.Body.linearVelocity = Vector2.zero;
            return;
        }

        // 3) Otherwise dribble toward goal, leaning away from the nearest defender.
        Vector2 dir = (aim - pos).normalized;
        if (pressured && enemy != null)
        {
            Transform foe = enemy.ClosestMemberTo(pos);
            if (foe != null)
            {
                Vector2 away = (pos - (Vector2)foe.position).normalized;
                dir = (dir + away * SteerAwayWeight).normalized;
            }
        }
        a.LastDirection = dir;
        a.Body.linearVelocity = dir * a.CarrySpeed;
    }

    // ---- DRIVES (Feature 1) ----

    // Trigger: low shot quality (checked by the caller) + marker beaten (close but BEHIND
    // us, or a fresh screen boost) + a clear lane to the 2m point → start the burst.
    static bool TryStartDrive(IAgentBody a, MatchContext ctx, TeamSide team, TeamSide enemy, Vector2 pos)
    {
        if (a.IsDriving) return false;

        // marker beaten: a screen just freed us, or the nearest defender is on us but behind
        bool beaten = a.ScreenBoostUntil > Time.time;
        if (!beaten && enemy != null)
        {
            Transform foe = enemy.ClosestMemberTo(pos);
            if (foe != null)
            {
                Vector2 toFoe = (Vector2)foe.position - pos;
                float d = toFoe.magnitude;
                if (d <= DriveBeatenDistance && d > 1e-4f &&
                    Vector2.Dot(toFoe / d, a.LastDirection) < DriveBeatenDot)
                    beaten = true;
            }
        }
        if (!beaten) return false;

        Vector2 driveTo = team.DrivePoint(pos);
        if (!team.LaneClear(pos, driveTo, enemy, DriveLaneRadius)) return false;

        a.IsDriving = true;
        a.DriveTarget = driveTo;
        DriveCarry(a, ctx); // start the burst this tick
        return true;
    }

    // Per-tick drive handling: movement + end conditions. Routed here from Tick()
    // whenever the carrier's IsDriving flag is set.
    static void DriveCarry(IAgentBody a, MatchContext ctx)
    {
        TeamSide team = a.Team;
        TeamSide enemy = ctx.EnemyOf(team);
        Vector2 pos = a.Body.position;

        // a foul mid-drive (free throw to us) supersedes the drive → settle via normal carry
        if (ctx.FreeThrowActive && ctx.FreeThrowCarrier == a.Tf)
        { a.IsDriving = false; Carry(a, ctx); return; }

        // reached the 2m point → finish: shoot at the open corner immediately
        if (Vector2.Distance(pos, a.DriveTarget) < DriveShootDistance)
        {
            a.IsDriving = false;
            Vector2 aim = team.ShotAimPoint(pos);
            if ((aim - pos).sqrMagnitude > 1e-4f) a.LastDirection = (aim - pos).normalized;
            Shoot(a, ctx);
            return;
        }

        // help defense stepped into the lane → kick out to the man they left open
        if (!team.LaneClear(pos, a.DriveTarget, enemy, DriveLaneRadius))
        {
            a.IsDriving = false;
            Transform mate = team.BestPassTarget(a.Tf, enemy, true); // pressured: any open mate
            if (mate == null) mate = MostOpenMate(team, enemy, a.Tf);
            if (mate != null) { Pass(a, ctx, mate); return; }
            Carry(a, ctx); // nobody open → back to a normal carry
            return;
        }

        // the marker recovered (close AND back in front) → the step is gone, end the drive
        if (a.ScreenBoostUntil <= Time.time && enemy != null)
        {
            Transform foe = enemy.ClosestMemberTo(pos);
            if (foe != null)
            {
                Vector2 toFoe = (Vector2)foe.position - pos;
                float d = toFoe.magnitude;
                if (d < DriveCaughtDistance && d > 1e-4f &&
                    Vector2.Dot(toFoe / d, a.LastDirection) >= DriveBeatenDot)
                { a.IsDriving = false; Carry(a, ctx); return; }
            }
        }

        // burst toward the 2m point
        Vector2 dir = a.DriveTarget - pos;
        if (dir.sqrMagnitude > 1e-4f) { dir.Normalize(); a.LastDirection = dir; }
        a.Body.linearVelocity = dir * (a.CarrySpeed * DriveSpeedMult);
    }

    // The most open teammate (largest distance to any enemy) — the drive kick-out when
    // BestPassTarget has no candidate (the helper's abandoned man is by definition open).
    static Transform MostOpenMate(TeamSide team, TeamSide enemy, Transform exclude)
    {
        if (team == null || team.members == null) return null;
        Transform best = null;
        float bestOpen = 0f;
        foreach (Transform m in team.members)
        {
            if (m == null || m == exclude) continue;
            float open = TeamSide.NearestDistance(m.position, enemy);
            if (open >= team.openRadius && open > bestOpen) { bestOpen = open; best = m; }
        }
        return best;
    }

    // ---- PICKS / SCREENS (Feature 2) ----
    // The team's nominated screener swims to the side of the carrier's marker, plants
    // there, and once SET, a carrier rubbing past it gets a short "marker beaten" boost
    // (which feeds the drive trigger). Returns true while it owns this agent's movement.
    static bool TryScreen(IAgentBody a, MatchContext ctx, TeamSide enemy)
    {
        TeamSide team = a.Team;
        Transform carrier = ctx.Ball.transform.parent;
        if (carrier == null || carrier == a.Tf || enemy == null || !IsMemberOf(carrier, team))
        { a.IsSettingScreen = false; return false; }

        IAgentBody carrierBody = carrier.GetComponent<IAgentBody>();
        if (carrierBody != null && carrierBody.IsDriving)   // already driving → no screen needed
        { a.IsSettingScreen = false; return false; }

        if (a.IsSettingScreen)
        {
            // abandon: held too long, or the carrier gave up on this side
            if (Time.time - a.ScreenStartTime > MaxScreenSeconds ||
                Vector2.Distance(carrier.position, a.ScreenTarget) > ScreenAbandonDistance)
            { a.IsSettingScreen = false; return false; }

            float d = Vector2.Distance(a.Body.position, a.ScreenTarget);
            if (d > ScreenArriveDistance)
            {
                a.ScreenSetSince = -1f; // not planted (yet / anymore)
                MoveTo(a, a.ScreenTarget, a.SupportSpeed);
                return true;
            }

            a.Body.linearVelocity = Vector2.zero; // plant on the spot
            if (a.ScreenSetSince < 0f) a.ScreenSetSince = Time.time;

            // screen is SET + the carrier rubs off it → grant the boost, then roll out
            // into normal spacing (replaces the vacated space, keeps the shape balanced).
            if (Time.time - a.ScreenSetSince >= ScreenSetSeconds && carrierBody != null &&
                Vector2.Distance(carrier.position, a.Body.position) <= ScreenRubDistance)
            {
                carrierBody.ScreenBoostUntil = Time.time + ScreenBoostSeconds;
                a.IsSettingScreen = false; // pick done
                return false;              // resume normal attack positioning this tick
            }
            return true;
        }

        // not screening yet: only the team's nominated screener starts one
        if (team.FindScreenerForCarrier(carrier, enemy) != a.Tf) return false;
        Transform marker = enemy.ClosestMemberTo(carrier.position);
        if (marker == null ||
            Vector2.Distance(marker.position, carrier.position) > ScreenMarkerRange) return false;

        a.IsSettingScreen = true;
        a.ScreenStartTime = Time.time;
        a.ScreenSetSince = -1f;
        a.ScreenTarget = team.GetScreenSpot(marker.position, CarrierFacing(carrier));
        MoveTo(a, a.ScreenTarget, a.SupportSpeed);
        return true;
    }

    // ---- presser: go win a loose / contested ball ----
    static void ChaseBall(IAgentBody a, MatchContext ctx)
    {
        Vector2 dir = ctx.BallPosition - a.Body.position;
        if (dir.sqrMagnitude > 1e-4f)
        {
            dir = dir.normalized;
            a.LastDirection = dir;
        }
        a.Body.linearVelocity = dir * a.ChaseSpeed;
    }

    static void MoveTo(IAgentBody a, Vector2 target, float speed)
    {
        Vector2 delta = target - a.Body.position;
        if (delta.magnitude < ArriveDistance) { IdleDrift(a, target, speed); return; }
        Vector2 dir = ApplySeparation(a, delta.normalized);
        a.LastDirection = dir;
        a.Body.linearVelocity = dir * speed;
    }

    // Anti-stacking: if a HIGHER-priority teammate (closer to the ball; instance id
    // breaks ties) is within MinTeammateSeparation, steer our movement away from him.
    // Only MoveTo/IdleDrift route through here, so carriers and pressers — who set
    // their velocity directly — are never deflected off the ball.
    static Vector2 ApplySeparation(IAgentBody a, Vector2 dir)
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || a.Team == null || a.Team.members == null) return dir;

        Vector2 myPos = a.Body.position;
        float myBallDist = Vector2.Distance(myPos, ctx.BallPosition);

        foreach (Transform m in a.Team.members)
        {
            if (m == null || m == a.Tf) continue;
            Vector2 toMate = (Vector2)m.position - myPos;
            float d = toMate.magnitude;
            if (d >= MinTeammateSeparation || d < 1e-4f) continue;

            // the one further from the ball is the lower priority and yields
            float mateBallDist = Vector2.Distance(m.position, ctx.BallPosition);
            bool iYield = myBallDist > mateBallDist ||
                          (Mathf.Approximately(myBallDist, mateBallDist) &&
                           a.Tf.GetInstanceID() > m.GetInstanceID());
            if (!iYield) continue;

            // push away harder the deeper the overlap
            Vector2 push = (-toMate / d) * ((MinTeammateSeparation - d) / MinTeammateSeparation);
            Vector2 blended = dir + push;
            dir = blended.sqrMagnitude > 1e-4f ? blended.normalized : (-toMate / d);
        }
        return dir;
    }

    // Arrived at our spot: instead of freezing, gently float around it. Each agent
    // gets a phase offset seeded from its instance id so they don't bob in sync, and
    // the drift always steers back toward the spot so it can't wander off-assignment.
    static void IdleDrift(IAgentBody a, Vector2 spot, float speed)
    {
        float seed = (a.Tf.GetInstanceID() & 0xFFFF) * 0.137f;
        float t = Time.time * IdleFreq + seed;
        Vector2 bob = spot + new Vector2(Mathf.Cos(t), Mathf.Sin(t * 0.8f + seed)) * IdleRadius;

        Vector2 toBob = bob - a.Body.position;
        // ClampMagnitude(...,1) eases the speed down as we near the bob point (no jitter)
        Vector2 vel = Vector2.ClampMagnitude(toBob, 1f) * (speed * IdleDriftFraction);

        // even while idling, keep the minimum separation off higher-priority teammates
        if (vel.sqrMagnitude > 1e-6f)
            vel = ApplySeparation(a, vel.normalized) * vel.magnitude;

        a.Body.linearVelocity = vel;
        if (vel.sqrMagnitude > 1e-4f) a.LastDirection = vel.normalized;
    }

    // ---- ball handling ----

    // Grab the loose ball — unless it's an ENEMY lob mid-flight (F+B high pass),
    // which takes a reduced steal roll (StealChance × LobInterceptFactor, with the
    // normal steal cooldown between tries). The lobbing team's own receivers collect
    // it normally. Returns true if the ball was collected; false = it sails on.
    // Anti-vulture: a player-team AI won't snatch a loose ball that the HUMAN-controlled
    // teammate is at least as close to — so a player who just dropped/lost the ball gets
    // first crack at their own loose ball instead of an AI mate instantly hoovering it up.
    // No effect for bots (the active player is never on their team). Doesn't block pass
    // receptions: by the time a pass reaches the receiver, the passer is far from the ball.
    static bool HumanTeammateCloserToBall(IAgentBody a, MatchContext ctx)
    {
        PlayerMovement active = TeamManager.ActivePlayer;
        if (active == null || a.Team == null || !a.Team.Contains(active.transform)) return false;
        float mine = Vector2.Distance(a.Body.position, ctx.BallPosition);
        float human = Vector2.Distance(active.transform.position, ctx.BallPosition);
        return human <= mine;
    }

    static bool TryCollectLoose(IAgentBody a, MatchContext ctx)
    {
        BallFlight flight = BallFlight.Instance;

        // A SKIP SHOT in mid-air (before its bounce) is too fast + low to grab — AI gets
        // ZERO intercept. It's a shot at goal, never a pass, so no auto-targeting either.
        // Only AFTER it bounces is it a normal, collectable loose ball.
        if (flight != null && flight.SkipActive && !flight.SkipBounced) return false;

        bool enemyLob = flight != null && flight.LobActive &&
                        flight.LobTeam != null && flight.LobTeam != a.Team;
        if (!enemyLob) { Grab(a, ctx); return true; }

        if (Time.time < a.NextStealTime) return false; // just missed it → no instant re-try
        a.NextStealTime = Time.time + StealCooldown;
        NotifyStealAttempt(a.Tf); // visible snatch at the high ball, hit or miss
        if (Random.value <= a.StealChance * LobInterceptFactor) { Grab(a, ctx); return true; }
        return false; // the ball sails over the outstretched arm
    }

    static void Grab(IAgentBody a, MatchContext ctx)
    {
        a.IsHolding = true;
        a.IsSettingScreen = false; // carriers don't screen
        ctx.Ball.simulated = false;
        ctx.Ball.linearVelocity = Vector2.zero;
        ctx.Ball.transform.SetParent(a.Tf);
        ctx.Ball.transform.localPosition = (Vector3)(a.LastDirection * a.HoldOffset);
        ctx.SetPossession(a.Team);
        a.HoldStartTime = Time.time;
    }

    // The presser tries to rip the ball off the enemy carrier when in range.
    static bool TryStealAI(IAgentBody a, MatchContext ctx, TeamSide enemy)
    {
        if (ctx.FreeThrowActive) return false; // no steals during a free throw
        if (enemy == null || !ctx.TeamHasBall(enemy)) return false;
        if (Time.time < a.NextStealTime) return false;
        Transform carrier = ctx.Ball.transform.parent;
        if (carrier == null) return false;
        if (carrier.GetComponent<Goalkeeper>() != null)
        {
            // can't steal from a keeper — and inside the protect radius we get pushed
            // straight back out at chase speed (returns true: movement handled).
            Vector2 away = a.Body.position - (Vector2)carrier.position;
            if (away.magnitude < KeeperProtectRadius)
            {
                if (away.sqrMagnitude < 1e-4f) away = Vector2.down;
                away.Normalize();
                a.LastDirection = away;
                a.Body.linearVelocity = away * a.ChaseSpeed;
                return true;
            }
            return false;
        }

        // A Shift-sprinting human carrier holds the ball LOOSELY: double steal range
        // and a flat success bonus (PlayerMovement.IsLooseHold).
        PlayerMovement carrierPm = carrier.GetComponent<PlayerMovement>();
        bool looseHold = carrierPm != null && carrierPm.IsLooseHold;
        float reach = looseHold ? a.GrabDistance * 2f : a.GrabDistance;
        if (Vector2.Distance(a.Body.position, ctx.BallPosition) > reach) return false;

        // Must come at the carrier from the front, not from behind.
        Vector2 dirToCarrier = (Vector2)carrier.position - a.Body.position;
        if (dirToCarrier.sqrMagnitude > 1e-4f) dirToCarrier.Normalize();
        Vector2 carrierFacing = CarrierFacing(carrier);
        if (carrierFacing.sqrMagnitude > 1e-4f &&
            Vector2.Dot(carrierFacing.normalized, -dirToCarrier) < StealFacingDot)
            return false;

        // Feature 5: stripping a settled Centre holding inside water rarely works — those
        // attempts come faster but fail more often, so the Centre draws fouls (and with
        // them exclusions/penalties via ExclusionManager).
        bool centerInside = enemy.Contains(carrier) &&
                            enemy.RoleOf(carrier) == TeamSide.Role.Center &&
                            TeamSide.IsInsideTwoMeter(carrier, enemy);
        a.NextStealTime = Time.time + (centerInside ? Mathf.Max(0.1f, StealCooldown - 0.2f)
                                                    : StealCooldown);
        NotifyStealAttempt(a.Tf); // play the snatch animation on the ATTEMPT, win or lose

        float chance = centerInside ? a.StealChance * 0.5f : a.StealChance;
        if (looseHold) chance += a.LooseHoldStealBonus;
        if (Random.value > chance)
        {
            // failed steal = ordinary foul (carrier keeps the ball; offender locked out longer)
            if (ExclusionManager.Instance != null)
                ExclusionManager.Instance.ReportFoul(a.Tf, a.Team, carrier);
            return false;
        }
        IAgentBody holder = carrier.GetComponent<IAgentBody>();
        if (holder != null) holder.IsHolding = false;
        else { PlayerMovement pm = carrier.GetComponent<PlayerMovement>(); if (pm != null) pm.ReleaseBall(); }
        a.IsHolding = true;
        a.IsSettingScreen = false; // carriers don't screen
        a.HoldStartTime = Time.time;
        ctx.Ball.simulated = false;
        ctx.Ball.linearVelocity = Vector2.zero;
        ctx.Ball.transform.SetParent(a.Tf);
        ctx.Ball.transform.localPosition = (Vector3)(a.LastDirection * a.HoldOffset);
        ctx.SetPossession(a.Team);
        return true;
    }

    // Fire the steal animation on whichever animator this swimmer carries
    // (BotAnimator on bots, PlayerAnimator on player-team swimmers).
    static void NotifyStealAttempt(Transform stealer)
    {
        BotAnimator ba = stealer.GetComponent<BotAnimator>();
        if (ba != null) { ba.TriggerSteal(); return; }
        PlayerAnimator pa = stealer.GetComponent<PlayerAnimator>();
        if (pa != null) pa.TriggerSteal();
    }

    // The direction the carrier is facing. A human-controlled carrier reports its
    // live facing; otherwise we read the AI body's LastDirection.
    static Vector2 CarrierFacing(Transform carrier)
    {
        PlayerMovement pm = carrier.GetComponent<PlayerMovement>();
        if (pm != null && pm.IsActive) return pm.Facing;
        IAgentBody body = carrier.GetComponent<IAgentBody>();
        if (body != null) return body.LastDirection;
        if (pm != null) return pm.Facing;
        return Vector2.zero;
    }

    static void Shoot(IAgentBody a, MatchContext ctx)
    {
        ctx.NoteRelease(a.Tf); // remember the shooter (Centre-goal tracking)
        DetachBall(ctx);
        // Set velocity directly so the shot is mass-independent (no weak/zeroed shot).
        ctx.Ball.linearVelocity = a.LastDirection * a.ShootPower;
        a.IsHolding = false;
        ctx.SetPossession(null); // starts the no-regrab cooldown
    }

    static void Pass(IAgentBody a, MatchContext ctx, Transform target)
    {
        Vector2 dir = ((Vector2)target.position - a.Body.position).normalized;
        float dist = Vector2.Distance(a.Body.position, target.position);
        a.LastDirection = dir;

        ctx.NoteRelease(a.Tf);
        DetachBall(ctx);
        ctx.Ball.linearVelocity = dir * Mathf.Clamp(dist * PassFactor, MinPassSpeed, MaxPassSpeed);
        if (BallFlight.Instance != null) BallFlight.Instance.NotePass(); // plain pass → no swell/trail
        a.IsHolding = false;
        ctx.SetPossession(null); // receiver collects it after the cooldown
    }

    static void Release(IAgentBody a, MatchContext ctx)
    {
        ctx.NoteRelease(a.Tf);
        DetachBall(ctx);
        a.IsHolding = false;
        ctx.SetPossession(null);
    }

    // Un-parent the ball and hand it back to physics with a clean slate.
    static void DetachBall(MatchContext ctx)
    {
        ctx.Ball.transform.SetParent(null);
        ctx.Ball.simulated = true;
        ctx.Ball.linearVelocity = Vector2.zero;
    }
}
