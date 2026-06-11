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

    [Header("Grab & control (Part 1)")]
    [SerializeField] private float keeperGrabDistance = 1.2f;  // collect a loose ball within this
    [SerializeField] private float keeperGrabMaxSpeed = 3f;    // ...only if it's moving slower than this
    [SerializeField] private float keeperHoldSeconds = 1.5f;   // bot keeper auto-distributes after this
    [SerializeField] private float playerKeeperMaxHold = 3f;   // player keeper auto-distributes if no B by then
    [SerializeField] private float holdOffset = 0.5f;          // held ball sits this far toward the field

    private Rigidbody2D rb;
    private bool holding;
    private float holdStartTime;

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

        // track the ball along the goal line (only y → never crosses the midline)
        float targetY = Mathf.Clamp(ball.position.y, minY, maxY);
        Vector2 pos = rb.position;
        pos.y = Mathf.MoveTowards(pos.y, targetY, trackSpeed * Time.fixedDeltaTime);
        rb.MovePosition(pos);

        // collect a slow, loose, nearby ball
        TeamSide team = KeeperTeam();
        if (ctx != null && team != null && ctx.BallGrabbable && ctx.CanGrab(team) &&
            Vector2.Distance(rb.position, ball.position) <= keeperGrabDistance &&
            ball.linearVelocity.magnitude < keeperGrabMaxSpeed)
        {
            Grab(ctx, team);
        }
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

        // distribute: bot after keeperHoldSeconds; player on B (Update) or a safety timeout
        float hold = IsPlayerKeeper() ? playerKeeperMaxHold : keeperHoldSeconds;
        if (Time.time - holdStartTime >= hold) PassOut();
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
