using UnityEngine;

// Goal-line out rule (plan B16.11), modeled on BallOutOfBounds. Possession goes to the
// nearest player of the OTHER team (the team that didn't touch the ball last) in two cases:
//   (a) a LOOSE, grabbable ball crosses a goal line (|x| >= goalLineX) outside the goal
//       mouth (|y| > goalMouthHalfHeight) → re-enters just inside the line.
//   (b) a CARRIER presses the ball against the line (|x| >= carrierOutX) → corner restart:
//       the ball + the receiving player are placed at that end's corner.
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
    [SerializeField] private float cornerInsetX = 6.2f;  // restart x = sign(ball.x) * this
    [SerializeField] private float cornerY = 3.5f;       // restart y = sign(ball.y) * this

    void FixedUpdate()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || ctx.Ball == null) return;

        if (ctx.PlayFrozen) return; // sprint duel / goal settle / penalty setup
        if (PenaltyManager.Instance != null && PenaltyManager.Instance.Active) return;

        // (b) HELD ball: a carrier pressing against the goal line → corner turnover (any y).
        if (ctx.PossessingTeam != null && ctx.Ball.transform.parent != null)
        {
            if (Mathf.Abs(ctx.Ball.position.x) >= carrierOutX) CarrierOut(ctx);
            return; // held → only the carrier rule applies
        }

        // (a) LOOSE ball behind the goal line, outside the mouth.
        Vector2 p = ctx.Ball.position;
        if (Mathf.Abs(p.x) < goalLineX) return;
        if (Mathf.Abs(p.y) <= goalMouthHalfHeight) return; // inside the mouth → the Goal trigger's job
        if (!ctx.BallGrabbable) return;                    // still in the no-grab window → ignore
        LooseOut(ctx);
    }

    // Loose ball over the line → re-enter just inside the line, nearest of the other team.
    void LooseOut(MatchContext ctx)
    {
        if (ctx.Ball == null) return;
        Vector2 p = ctx.Ball.position;
        TeamSide award = OtherTeam(ctx);
        if (award == null) return;

        Rigidbody2D ball = ctx.Ball;
        float sx = p.x >= 0f ? 1f : -1f;
        float rx = Mathf.Max(0f, goalLineX - reentryInset);
        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = Vector2.zero;
        ball.position = new Vector2(sx * rx, p.y);
        ctx.SetPossession(null);

        Transform receiver = award.ClosestMemberTo(ball.position);
        if (receiver != null) ctx.GiveBallTo(receiver, award);

        Finish(ctx, award);
    }

    // Carrier pressing the line → drop it, restart at that end's corner with the other team.
    void CarrierOut(MatchContext ctx)
    {
        if (ctx.Ball == null) return;
        Vector2 p = ctx.Ball.position;
        TeamSide award = OtherTeam(ctx);
        if (award == null) return;

        ctx.ForceDropHeldBall(); // carrier drops the ball

        float sx = p.x >= 0f ? 1f : -1f;
        float sy = p.y >= 0f ? 1f : -1f;
        Vector2 corner = new Vector2(sx * cornerInsetX, sy * cornerY);

        Transform receiver = award.ClosestMemberTo(p); // nearest to where it went out

        Rigidbody2D ball = ctx.Ball;
        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = Vector2.zero;
        ball.position = corner;
        ctx.SetPossession(null);

        if (receiver != null)
        {
            // place the receiver at the corner too, then hand them the ball
            receiver.position = new Vector3(corner.x, corner.y, receiver.position.z);
            Rigidbody2D rrb = receiver.GetComponent<Rigidbody2D>();
            if (rrb != null) { rrb.position = corner; rrb.linearVelocity = Vector2.zero; }
            ctx.GiveBallTo(receiver, award);
        }

        Finish(ctx, award);
    }

    // The team that did NOT touch the ball last (deflection-aware via MatchContext.NoteTouch).
    TeamSide OtherTeam(MatchContext ctx)
    {
        TeamSide award = ctx.LastTouchTeam != null ? ctx.EnemyOf(ctx.LastTouchTeam) : ctx.PlayerTeam;
        return award != null ? award : ctx.PlayerTeam;
    }

    void Finish(MatchContext ctx, TeamSide award)
    {
        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock();
        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Goal-line out - " + (award == ctx.PlayerTeam ? "YOU" : "BOT"));
    }
}
