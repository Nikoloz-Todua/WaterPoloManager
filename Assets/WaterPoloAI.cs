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
    float HoldStartTime { get; set; }
    float NextStealTime { get; set; }

    // Dynamic-marking state (anti-oscillation): who we're currently marking and the
    // earliest time we're allowed to switch to a different man.
    Transform CurrentMark { get; set; }
    float NextMarkSwitchTime { get; set; }

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
    const float StealCooldown = 0.6f;   // min time between steal attempts
    const float StealFacingDot = 0.3f;  // stealer must be within ~70° of the carrier's front
    const float IdleDriftFraction = 0.2f; // idle float speed as a fraction of move speed
    const float IdleRadius = 0.35f;       // how far the idle bob point sits from the spot
    const float IdleFreq = 1.2f;          // idle bob speed (rad/s)
    const float MarkSwitchCooldown = 0.6f; // min time before a defender may switch its man
    const float KickoffPassSettle = 0.4f;  // AI carrier settles this long before the kickoff pass

    public static void Tick(IAgentBody a, MatchContext ctx)
    {
        if (ctx == null || a.Team == null || ctx.Ball == null) return;

        // Safety net for EVERY agent: if we think we're holding but the ball isn't
        // actually parented to us, drop the illusion. (A stale "holding" flag is what
        // left agents green/aiming with no ball and pinned/snapped the ball in place.)
        if (a.IsHolding && ctx.Ball.transform.parent != a.Tf)
            a.IsHolding = false;

        // A human just took control of this swimmer → drop the ball, stop steering.
        if (a.Suppressed)
        {
            if (a.IsHolding) Release(a, ctx);
            return;
        }

        if (a.IsHolding) { Carry(a, ctx); return; }

        // Collect a genuinely loose ball within reach (cooldown stops snatch-backs;
        // CanGrab enforces the shot-clock turnover ban on the violating team).
        if (ctx.BallGrabbable && ctx.CanGrab(a.Team) &&
            Vector2.Distance(a.Body.position, ctx.BallPosition) <= a.GrabDistance)
        {
            Grab(a, ctx);
            return;
        }

        TeamSide enemy = ctx.EnemyOf(a.Team);

        if (ctx.TeamHasBall(a.Team))
        {
            // We attack → drop any defensive mark so it recomputes fresh next phase.
            a.CurrentMark = null;
            // A teammate is carrying → hold our role's attacking spot (fixed spacing).
            MoveTo(a, a.Team.AttackPositionFor(a.Tf, ctx.BallPosition), a.SupportSpeed);
            return;
        }

        // Ball is loose or the enemy has it: the single nearest swimmer presses (and is
        // excluded from marking); every other swimmer dynamically marks the most
        // dangerous still-unmarked attacker.
        Transform presser = a.Team.ClosestMemberTo(ctx.BallPosition);
        if (presser == a.Tf)
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
        else // Press: threat-based 1-to-1 marking with dynamic switching (unchanged)
        {
            Transform mark = ResolveMark(a, ctx, enemy, presser);
            Vector2 spot = mark != null ? a.Team.MarkSpot(a.Tf, mark)
                                        : a.Team.DefendSpot(a.Tf, ctx.BallPosition);
            MoveTo(a, spot, a.SupportSpeed);
        }
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

        // 1) In range with a clear lane → square up, then shoot once settled.
        if (goalDist <= a.ShootRange && team.LaneClear(pos, aim, enemy, team.shotLaneRadius))
        {
            a.LastDirection = (aim - pos).normalized;
            if (Time.time - a.HoldStartTime >= SettleDelay)
            {
                Shoot(a, ctx);
            }
            else
            {
                a.Body.linearVelocity = Vector2.zero; // square up and wait
            }
            return;
        }

        // 2) An open teammate ahead (or any open mate while pressured) → pass.
        bool pressured = TeamSide.NearestDistance(pos, enemy) < team.pressDistance;
        Transform mate = team.BestPassTarget(a.Tf, enemy, pressured);
        if (mate != null) { Pass(a, ctx, mate); return; }

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
        Vector2 dir = delta.normalized;
        a.LastDirection = dir;
        a.Body.linearVelocity = dir * speed;
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
        a.Body.linearVelocity = vel;
        if (vel.sqrMagnitude > 1e-4f) a.LastDirection = vel.normalized;
    }

    // ---- ball handling ----
    static void Grab(IAgentBody a, MatchContext ctx)
    {
        a.IsHolding = true;
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
        if (enemy == null || !ctx.TeamHasBall(enemy)) return false;
        if (Time.time < a.NextStealTime) return false;
        Transform carrier = ctx.Ball.transform.parent;
        if (carrier == null) return false;
        if (Vector2.Distance(a.Body.position, ctx.BallPosition) > a.GrabDistance) return false;

        // Must come at the carrier from the front, not from behind.
        Vector2 dirToCarrier = (Vector2)carrier.position - a.Body.position;
        if (dirToCarrier.sqrMagnitude > 1e-4f) dirToCarrier.Normalize();
        Vector2 carrierFacing = CarrierFacing(carrier);
        if (carrierFacing.sqrMagnitude > 1e-4f &&
            Vector2.Dot(carrierFacing.normalized, -dirToCarrier) < StealFacingDot)
            return false;

        a.NextStealTime = Time.time + StealCooldown;
        if (Random.value > a.StealChance)
        {
            // failed steal = ordinary foul (carrier keeps the ball; offender locked out longer)
            if (ExclusionManager.Instance != null)
                ExclusionManager.Instance.ReportFoul(a.Tf, a.Team);
            return false;
        }
        IAgentBody holder = carrier.GetComponent<IAgentBody>();
        if (holder != null) holder.IsHolding = false;
        else { PlayerMovement pm = carrier.GetComponent<PlayerMovement>(); if (pm != null) pm.ReleaseBall(); }
        a.IsHolding = true;
        a.HoldStartTime = Time.time;
        ctx.Ball.simulated = false;
        ctx.Ball.linearVelocity = Vector2.zero;
        ctx.Ball.transform.SetParent(a.Tf);
        ctx.Ball.transform.localPosition = (Vector3)(a.LastDirection * a.HoldOffset);
        ctx.SetPossession(a.Team);
        return true;
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

        DetachBall(ctx);
        ctx.Ball.linearVelocity = dir * Mathf.Clamp(dist * PassFactor, MinPassSpeed, MaxPassSpeed);
        a.IsHolding = false;
        ctx.SetPossession(null); // receiver collects it after the cooldown
    }

    static void Release(IAgentBody a, MatchContext ctx)
    {
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
