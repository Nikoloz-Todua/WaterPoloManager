using UnityEngine;

// Out-of-bounds rule for the TOP and BOTTOM walls (plan B16.11). When a LOOSE ball
// reaches the top/bottom edge, possession is awarded to the nearest player of the team
// that did NOT touch it last. Left/right walls keep their normal goal-line physics
// (this only reacts to high |y|, never to |x|).
//
// Detection is by the loose ball's y reaching the wall (the wall is solid, so a loose
// ball can only get there by hitting it) — no wall tags/markers or extra wiring needed.
public class BallOutOfBounds : MonoBehaviour
{
    [SerializeField] private float outYThreshold = 4.2f; // |ball.y| at/above this = at the top/bottom wall
    [SerializeField] private float reentryInset = 0.5f;  // push the ball this far back inside the pool

    void FixedUpdate()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || ctx.Ball == null) return;
        if (!ctx.BallIsLoose) return; // held balls are unaffected

        if (Mathf.Abs(ctx.Ball.position.y) >= outYThreshold)
            Award(ctx);
    }

    void Award(MatchContext ctx)
    {
        Rigidbody2D ball = ctx.Ball;

        // the team that did NOT touch it last gets it (default to player team if unknown)
        TeamSide award = ctx.LastTouchTeam != null ? ctx.EnemyOf(ctx.LastTouchTeam) : ctx.PlayerTeam;
        if (award == null) award = ctx.PlayerTeam;
        if (award == null) return;

        // re-enter slightly inside the pool at the contact x
        float contactY = ball.position.y;
        float sign = contactY >= 0f ? 1f : -1f;
        float reY = Mathf.Max(0f, Mathf.Abs(contactY) - reentryInset);
        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = Vector2.zero;
        ball.position = new Vector2(ball.position.x, sign * reY);
        ctx.SetPossession(null);

        // hand it to the nearest player of the awarded team (reuses existing hold mechanics)
        Transform receiver = award.ClosestMemberTo(ball.position);
        if (receiver != null) ctx.GiveBallTo(receiver, award);

        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock();
        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Out - " + (award == ctx.PlayerTeam ? "YOU" : "BOT"));
    }
}
