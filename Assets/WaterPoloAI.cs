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

    public static void Tick(IAgentBody a, MatchContext ctx)
    {
        if (ctx == null || a.Team == null || ctx.Ball == null) return;

        // A human just took control of this swimmer → drop the ball, stop steering.
        if (a.Suppressed)
        {
            if (a.IsHolding) Release(a, ctx);
            return;
        }

        // Safety net: if we think we're holding but the ball isn't actually parented
        // to us, drop the illusion. (A stale "holding" flag is what used to pin and
        // freeze the ball in LateUpdate.)
        if (a.IsHolding && ctx.Ball.transform.parent != a.Tf)
            a.IsHolding = false;

        if (a.IsHolding) { Carry(a, ctx); return; }

        // Collect a genuinely loose ball within reach (cooldown stops snatch-backs).
        if (ctx.BallGrabbable &&
            Vector2.Distance(a.Body.position, ctx.BallPosition) <= a.GrabDistance)
        {
            Grab(a, ctx);
            return;
        }

        TeamSide enemy = ctx.EnemyOf(a.Team);

        if (ctx.TeamHasBall(a.Team))
        {
            // A teammate is carrying → get open ahead of them for a pass.
            MoveTo(a, a.Team.SupportSpot(a.Tf, ctx.BallPosition, enemy), a.SupportSpeed);
            return;
        }

        // Ball is loose or the enemy has it: the nearest swimmer presses, rest defend.
        if (a.Team.ClosestMemberTo(ctx.BallPosition) == a.Tf)
            ChaseBall(a, ctx);
        else
            MoveTo(a, a.Team.DefendSpot(a.Tf, ctx.BallPosition), a.SupportSpeed);
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
        TeamSide team = a.Team;
        if (team.attackGoal == null) { Release(a, ctx); return; }

        TeamSide enemy = ctx.EnemyOf(team);
        Vector2 pos = a.Body.position;
        Vector2 goal = team.attackGoal.position;
        Vector2 aim = team.ShotAimPoint(pos);                 // a corner, not the keeper
        float goalDist = Vector2.Distance(pos, goal);

        // 1) In range with a clear lane → shoot at the open corner.
        if (goalDist <= a.ShootRange && team.LaneClear(pos, aim, enemy, team.shotLaneRadius))
        {
            a.LastDirection = (aim - pos).normalized;
            Shoot(a, ctx);
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
        if (delta.magnitude < ArriveDistance) { a.Body.linearVelocity = Vector2.zero; return; }
        Vector2 dir = delta.normalized;
        a.LastDirection = dir;
        a.Body.linearVelocity = dir * speed;
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
