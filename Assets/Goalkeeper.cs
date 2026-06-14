using UnityEngine;

// Kinematic keeper that slides along its goal line tracking the ball, blocks shots, and
// (Part 1) COLLECTS a slow loose ball near its goal, holds briefly, then DISTRIBUTES to an
// open teammate. A keeper hold is not a possession change for the shot clock (it keeps
// ticking for the holding team; the reset happens on the pass-out). Never crosses the
// midline (only moves in y) and never dribbles.
[RequireComponent(typeof(Rigidbody2D))]
public class Goalkeeper : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody2D ball;

    [Header("Movement")]
    [SerializeField] private float trackSpeed = 4f;
    [SerializeField] private float minY = -2f;
    [SerializeField] private float maxY = 2f;

    [Header("Shot reaction")]
    [SerializeField] private float shotSpeedThreshold = 4f;      // loose ball faster than this = a shot
    [SerializeField] private float highShotReactionDelay = 0.2f; // extra freeze vs a HIGH (charge > 0.7) shot

    [Header("Grab & control (Part 1)")]
    [SerializeField] private float keeperGrabDistance = 1.2f;  // collect a loose ball within this
    [SerializeField] private float keeperGrabMaxSpeed = 3f;    // ...only if it's moving slower than this
    [SerializeField] private float keeperSnatchDistance = 0.8f;// strip an enemy carrier this close — 100%, no roll
    [SerializeField] private float keeperHoldSeconds = 0.8f;   // bot keeper auto-distributes after this
    [SerializeField] private float keeperPanicDistance = 2.5f; // bot keeper distributes NOW if an opponent is this close
    [SerializeField] private float playerKeeperMaxHold = 3f;   // player keeper auto-distributes if no B by then
    [SerializeField] private float holdOffset = 0.5f;          // held ball sits this far toward the field

    private Rigidbody2D rb;
    private bool holding;
    private float holdStartTime;
    private bool shotIncoming;              // edge-detects a NEW incoming shot
    private float reactBlockedUntil = -10f; // high-shot reaction-delay window

    void Awake() { rb = GetComponent<Rigidbody2D>(); }

    // The team currently DEFENDING this physical goal (auto-corrects after a halftime swap),
    // derived from which team's defendGoal is on this keeper's side. No wiring needed.
    TeamSide KeeperTeam()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null) return null;
        float mySign = transform.position.x >= 0f ? 1f : -1f;
        if (Defends(ctx.PlayerTeam, mySign)) return ctx.PlayerTeam;
        if (Defends(ctx.BotTeam, mySign)) return ctx.BotTeam;
        return null;
    }
    static bool Defends(TeamSide t, float sign)
        => t != null && t.defendGoal != null && Mathf.Sign(t.defendGoal.position.x) == sign;

    bool IsPlayerKeeper()
    {
        MatchContext ctx = MatchContext.Instance;
        return ctx != null && KeeperTeam() == ctx.PlayerTeam;
    }

    void Update()
    {
        // Player keeper distributes on B (only B works — no swimming off the line).
        if (holding && IsPlayerKeeper() && Input.GetKeyDown(KeyCode.B)) PassOut();
    }

    void FixedUpdate()
    {
        if (ball == null || rb == null) return;
        MatchContext ctx = MatchContext.Instance;

        if (holding) { HoldTick(ctx); return; }

        // Edge-detect a NEW incoming shot (loose + fast + toward our side). A HIGH shot
        // (charged past 0.7) freezes our reaction for an extra beat — harder to block.
        bool incoming = ctx != null && ctx.BallIsLoose &&
                        ball.linearVelocity.magnitude > shotSpeedThreshold &&
                        ball.linearVelocity.x * Mathf.Sign(transform.position.x) > 0f;
        if (incoming && !shotIncoming)
        {
            shotIncoming = true;
            if (IncomingShotHeight(ctx) > 0.7f)
                reactBlockedUntil = Time.time + highShotReactionDelay;
        }
        else if (!incoming) shotIncoming = false;

        // A skip shot that fooled us at its bounce (BallFlight) stops our tracking too —
        // we hold the line where we are and don't react to the deflection.
        bool fooled = incoming && BallFlight.Instance != null && BallFlight.Instance.KeeperFooled;

        if (Time.time >= reactBlockedUntil && !fooled)
        {
            // track the ball along the goal line (only y → never crosses the midline)
            float targetY = Mathf.Clamp(ball.position.y, minY, maxY);
            Vector2 pos = rb.position;
            pos.y = Mathf.MoveTowards(pos.y, targetY, trackSpeed * Time.fixedDeltaTime);
            rb.MovePosition(pos);
        }

        TeamSide team = KeeperTeam();

        // SNATCH (Task 5): an enemy carrier point-blank on the keeper (within
        // keeperSnatchDistance) is stripped with 100% success — no probability roll.
        if (ctx != null && team != null && TrySnatchFromCarrier(ctx, team)) return;

        // collect a slow, loose, nearby ball
        if (ctx != null && team != null && ctx.BallGrabbable && ctx.CanGrab(team) &&
            Vector2.Distance(rb.position, ball.position) <= keeperGrabDistance &&
            ball.linearVelocity.magnitude < keeperGrabMaxSpeed)
        {
            Grab(ctx, team);
        }
    }

    // Height (0..1) of the ball's current flight: the last releaser's charged ShotHeight
    // (human shots). AI swimmers have no height system yet → 0.5 (mid).
    static float IncomingShotHeight(MatchContext ctx)
    {
        if (ctx != null && ctx.LastReleaser != null)
        {
            PlayerMovement pm = ctx.LastReleaser.GetComponent<PlayerMovement>();
            if (pm != null) return pm.ShotHeight;
        }
        return 0.5f;
    }

    void HoldTick(MatchContext ctx)
    {
        // lost the ball (e.g. a shot-clock turnover detached it) → clear our state
        if (ball.transform.parent != transform)
        {
            holding = false;
            if (ctx != null) ctx.ClearKeeperHold();
            return;
        }

        // pin the held ball just in front of the keeper (toward centre)
        float toCentre = transform.position.x >= 0f ? -1f : 1f;
        ball.transform.localPosition = new Vector3(toCentre * holdOffset, 0f, 0f);

        if (IsPlayerKeeper())
        {
            // player keeper distributes on B (Update) or the touch PASS OUT button; this is
            // only the safety timeout so a held ball can never get stuck.
            if (Time.time - holdStartTime >= playerKeeperMaxHold) PassOut();
        }
        else
        {
            // bot keeper: distribute after the short hold, OR immediately if an opponent is
            // crowding it (stops attackers swarming the keeper into a stuck situation).
            if (Time.time - holdStartTime >= keeperHoldSeconds || EnemyCrowding(ctx)) PassOut();
        }
    }

    // True while an opponent swimmer sits within keeperPanicDistance of this (bot) keeper.
    bool EnemyCrowding(MatchContext ctx)
    {
        if (ctx == null) return false;
        TeamSide team = KeeperTeam();
        if (team == null) return false;
        TeamSide enemy = ctx.EnemyOf(team);
        if (enemy == null || enemy.members == null) return false;
        foreach (Transform m in enemy.members)
        {
            if (m == null) continue; // excluded slots are null
            if (Vector2.Distance(rb.position, m.position) <= keeperPanicDistance) return true;
        }
        return false;
    }

    // Touch PASS OUT button (player keeper): distribute now — same as the keyboard B.
    public void RequestPassOut()
    {
        if (holding) PassOut();
    }

    // Strip an enemy carrier that comes within keeperSnatchDistance — 100% success, no roll.
    // (Normal loose-ball collection keeps its slow-speed requirement; this is only for a
    // carrier driving point-blank into the keeper.) Respects an active free throw.
    bool TrySnatchFromCarrier(MatchContext ctx, TeamSide team)
    {
        if (ctx.FreeThrowActive) return false;                      // a free-throw carrier is protected
        TeamSide enemy = ctx.EnemyOf(team);
        if (enemy == null || !ctx.TeamHasBall(enemy)) return false; // only when an ENEMY holds it

        Transform carrier = ball.transform.parent;
        if (carrier == null || carrier == transform) return false;
        if (carrier.GetComponent<Goalkeeper>() != null) return false; // not from another keeper
        if (Vector2.Distance(rb.position, carrier.position) > keeperSnatchDistance) return false;

        // clear the carrier's hold, then take it ourselves (becomes a keeper hold)
        IAgentBody body = carrier.GetComponent<IAgentBody>();
        if (body != null) body.IsHolding = false;
        else { PlayerMovement pm = carrier.GetComponent<PlayerMovement>(); if (pm != null) pm.ReleaseBall(); }

        Grab(ctx, team);
        return true;
    }

    void Grab(MatchContext ctx, TeamSide team)
    {
        holding = true;
        holdStartTime = Time.time;
        ball.simulated = false;
        ball.linearVelocity = Vector2.zero;
        ball.transform.SetParent(transform);
        ball.transform.localPosition = Vector3.zero;
        ctx.SetKeeperHold(team);   // mark BEFORE possession so the shot clock skips its reset
        ctx.SetPossession(team);
    }

    void PassOut()
    {
        MatchContext ctx = MatchContext.Instance;
        holding = false;
        if (ctx != null) ctx.ClearKeeperHold();
        if (ball == null) return;

        TeamSide team = KeeperTeam();
        Vector2 from = transform.position;

        // most-open advanced teammate (reuse the pass-target logic); else the safe deep outlet
        Transform target = null;
        if (team != null)
        {
            target = team.BestPassTarget(transform, ctx != null ? ctx.EnemyOf(team) : null, false);
            if (target == null) target = team.DeepestMember(transform);
        }

        Vector2 dir;
        float dist;
        if (target != null) { dir = ((Vector2)target.position - from).normalized; dist = Vector2.Distance(from, target.position); }
        else if (team != null && team.attackGoal != null) { dir = ((Vector2)team.attackGoal.position - from).normalized; dist = 6f; }
        else { dir = Vector2.right; dist = 6f; }

        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = dir * Mathf.Clamp(dist * 2.5f, 6f, 13f); // same clamp as a normal pass
        if (ctx != null) ctx.SetPossession(null);
        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock(); // distribution = fresh 30
    }
}
