using UnityEngine;

// Goal-line out rule (plan B16.11), modeled on BallOutOfBounds. The loose restart is reserved
// for the OTHER team (the team that didn't touch the ball last) in two cases:
//   (a) a LOOSE, grounded ball fully crosses a goal line outside the goal
//       mouth (|y| > goalMouthHalfHeight) → re-enters just inside the line.
//   (b) a CARRIER takes the complete ball beyond carrierOutX → corner restart:
//       the ball is dropped just inside at the carrier's Y. The receiver swims to it.
// The |y| check keeps the loose case clear of real goals. Never reacts during a freeze
// (sprint duel / goal settle / penalty setup) or an active penalty.
public class GoalLineOut : MonoBehaviour
{
    [Header("Loose ball over the line")]
    [SerializeField] private float goalLineX = 7.0f;           // |x| at/beyond the goal line
    [SerializeField] private float goalMouthHalfHeight = 1.5f; // |y| within this = goal mouth → leave goals alone
    [SerializeField] private float reentryInset = 0.5f;        // re-enter this far inside the line

    [Header("Carrier-at-line turnover")]
    [SerializeField] private float carrierOutX = 6.7f;   // held ball at/over this |x| → corner turnover (any y)
    [SerializeField] private float restartMaxY = 3.5f;

    private BallOutOfBounds restartService;

    void Start()
    {
        restartService = GetComponent<BallOutOfBounds>();
    }

    void FixedUpdate()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || ctx.Ball == null) return;
        if (ctx.OutOfBoundsRestartActive) return;

        if (ctx.CompetitivePlayStopped) return; // full freeze or water-polo stoppage
        if (PenaltyManager.Instance != null && PenaltyManager.Instance.Active) return;

        // (b) HELD ball: a carrier pressing against the goal line → corner turnover (any y).
        if (ctx.PossessingTeam != null && ctx.Ball.transform.parent != null)
        {
            Vector2 heldBallPosition = ctx.BallPosition;
            if (Mathf.Abs(heldBallPosition.x) - BallRadiusX(ctx.Ball) >= carrierOutX)
                CarrierOut(ctx);
            return; // held → only the carrier rule applies
        }

        // (a) LOOSE grounded ball behind the goal line, outside the mouth.
        Vector2 p = ctx.Ball.position;
        if (!OwnsLooseOut(ctx, p)) return;
        LooseOut(ctx);
    }

    // Shared boundary ownership test. BallOutOfBounds asks this before starting its broader
    // full-escape/top-bottom recovery so the two rules cannot claim the same diagonal exit.
    // A dead-ball ruling should not wait for the post-release GRAB cooldown; only a genuinely
    // airborne/physics-off ball remains owned by BallFlight or another match-flow system.
    public bool OwnsLooseOut(MatchContext ctx, Vector2 p)
    {
        if (ctx == null || ctx.Ball == null || ctx.CompetitivePlayStopped) return false;
        if (PenaltyManager.Instance != null && PenaltyManager.Instance.Active) return false;
        if (!ctx.BallIsLoose || ctx.Ball.transform.parent != null || !ctx.Ball.simulated) return false;
        if (Mathf.Abs(p.x) - BallRadiusX(ctx.Ball) < goalLineX ||
            Mathf.Abs(p.y) <= goalMouthHalfHeight) return false;
        BallFlight flight = BallFlight.Instance;
        return flight == null || !flight.HighBallActive;
    }

    // Loose ball over the line: preserve the Y coordinate where it crossed and leave it loose.
    // The awarded team must swim to it; nobody is teleported and possession is not auto-handed.
    void LooseOut(MatchContext ctx)
    {
        if (ctx.Ball == null) return;
        Vector2 p = ctx.Ball.position;
        float sx = p.x >= 0f ? 1f : -1f;
        float fullCrossingCenterX = sx * (goalLineX + BallRadiusX(ctx.Ball));
        float rx = Mathf.Max(0f, goalLineX - reentryInset);
        Vector2 restart = restartService != null
            ? restartService.VerticalRestartPoint(p, fullCrossingCenterX, sx * rx, restartMaxY)
            : new Vector2(sx * rx, Mathf.Clamp(p.y, -restartMaxY, restartMaxY));
        PlaceRestart(ctx, restart,
                     WasKeeperDeflectionAtThisEnd(ctx, p) ? "Corner" : "Goal-line out");
    }

    // Carrier pressing the line → drop it just inside at the crossing Y for the other team.
    void CarrierOut(MatchContext ctx)
    {
        if (ctx.Ball == null) return;
        Vector2 p = ctx.BallPosition;
        ctx.ForceDropHeldBall(); // carrier drops the ball

        float sx = p.x >= 0f ? 1f : -1f;
        float rx = Mathf.Max(0f, goalLineX - reentryInset);
        Vector2 restart = new Vector2(sx * rx, Mathf.Clamp(p.y, -restartMaxY, restartMaxY));
        PlaceRestart(ctx, restart, "Corner");
    }

    static float BallRadiusX(Rigidbody2D ball)
    {
        if (ball == null) return 0f;
        float radius = 0f;
        foreach (Collider2D collider in ball.GetComponentsInChildren<Collider2D>(true))
            if (collider != null && collider.enabled)
                radius = Mathf.Max(radius, collider.bounds.extents.x);
        return radius;
    }

    // The keeper flag is meaningful only at the physical end that keeper currently defends.
    // This side check prevents a keeper's own long distribution going out at the far end from
    // ever being misread as a corner (distributions do not set the flag, but keep the rule safe).
    bool WasKeeperDeflectionAtThisEnd(MatchContext ctx, Vector2 ballPos)
    {
        TeamSide keeperTeam = ctx.LastKeeperTouchTeam;
        if (keeperTeam == null || keeperTeam.defendGoal == null) return false;
        float outSign = ballPos.x >= 0f ? 1f : -1f;
        return Mathf.Sign(keeperTeam.defendGoal.position.x) == outSign;
    }

    void PlaceRestart(MatchContext ctx, Vector2 point, string eventLabel)
    {
        if (restartService == null) restartService = GetComponent<BallOutOfBounds>();
        if (restartService != null)
        {
            restartService.AwardRestart(ctx, point, eventLabel);
            return;
        }

        // Stripped-down test scenes may omit BallOutOfBounds; retain the same loose-ball contract.
        TeamSide offending = ctx.LastTouchTeam;
        TeamSide awarded = offending != null ? ctx.EnemyOf(offending) : ctx.PlayerTeam;
        if (awarded == null) return;
        Rigidbody2D ball = ctx.Ball;
        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.position = point;
        ball.transform.position = point;
        ball.linearVelocity = Vector2.zero;
        ball.angularVelocity = 0f;
        ctx.SetPossession(null);
        Transform fetcher = awarded.ClosestMemberTo(point);
        ctx.BeginOutOfBoundsRestart(awarded, offending, point, fetcher);
        if (ExclusionManager.Instance != null)
            ExclusionManager.Instance.ReleaseForAward(awarded, "goal throw awarded");
        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock();
    }
}
