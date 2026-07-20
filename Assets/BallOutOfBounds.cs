using System.Collections;
using UnityEngine;

// Out-of-bounds rules (plan B16.11).
//
// TOP/BOTTOM wall out: when a LOOSE ball reaches the top/bottom edge, possession is
// awarded to the nearest player of the team that did NOT touch it last. Left/right walls
// keep their normal goal-line physics (GoalLineOut owns those).
//
// FULL ESCAPE (2026-07-09c): a safety net for a ball that leaves the pool ENTIRELY (a
// violent shot/physics pop past the walls — previously it just sat outside forever,
// unreachable: "the ball disappears"). The escaped ball visibly bounces and settles on
// the deck, pauses briefly, then the DEFENDING team's goalkeeper restarts play (the team
// that didn't touch it last — a real water-polo goal-throw ruling). The ball is dropped
// just in front of that keeper and its normal collect logic takes over — no new handoff
// path into the keeper AI.
//
// Detection is positional (the walls are solid, so any ball past them has escaped) —
// no wall tags/markers or extra wiring needed.
public class BallOutOfBounds : MonoBehaviour
{
    [SerializeField] private float outYThreshold = 4.2f; // |ball.y| at/above this = at the top/bottom wall
    [Header("Full-escape recovery (2026-07-09c)")]
    [SerializeField] private float escapeXThreshold = 8.2f; // past the left/right walls (±8) = out of the pool
    [SerializeField] private float escapeYThreshold = 4.7f; // past the top/bottom walls (±4.5) = out of the pool
    [SerializeField] private float settleSeconds = 0.6f;    // the two deck bounces take about this long
    [SerializeField] private float throwInDelay = 0.8f;     // dead-ball pause before the keeper restart
    [Tooltip("Minimum velocity directly into a top/bottom wall required to leave play. Softer balls are reflected back into the pool.")]
    [SerializeField] private float minimumExitSpeed = 12f;
    [Tooltip("Fraction of outward speed retained by a soft in-play wall deflection.")]
    [SerializeField, Range(0.1f, 1f)] private float softBounceRetention = 0.75f;

    private bool recovering; // a full-escape sequence is running — all rules stand down
    private BoxCollider2D topWall;
    private BoxCollider2D bottomWall;
    private GoalLineOut goalLineOut;

    void Start()
    {
        goalLineOut = GetComponent<GoalLineOut>();
        FindPlayableEdgeColliders();
    }

    void FixedUpdate()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || ctx.Ball == null) return;
        if (recovering) return;       // the escape sequence owns the ball right now
        if (!ctx.BallIsLoose) return; // held balls are unaffected

        // 2026-07-09d REGRESSION FIX — two guards the first version was missing:
        // (1) NEVER judge a frozen game: during a goal hang-time / restart / sprint duel /
        //     penalty the loose ball is parked by that system, not "out".
        // (2) NEVER judge a physics-off ball, and never through the STALE rigidbody pose
        //     (the frozen-pose gotcha): a non-simulated ball's rb.position freezes at its
        //     last simulated spot — e.g. the duel pins the ball at (0,0) physics-off while
        //     rb.position still says wherever play ended, which could read as "escaped"
        //     and made this rule misfire repeatedly, claim possession mid-restart and spam
        //     the feed. Both rules now read the transform-aware ctx.BallPosition and only
        //     ever act on a live, simulated, unfrozen ball.
        if (ctx.PlayFrozen) return;
        if (!ctx.Ball.simulated) return; // physics-off = some system is managing the ball

        // An airborne arc flies a straight, in-pool line to a clamped landing — never
        // escaped, and never "at the wall" (its colliders are off; position is transient).
        BallFlight flight = BallFlight.Instance;
        if (flight != null && flight.HighBallActive) return;

        // FULL ESCAPE first: checked before the wall rule because a violent ball can jump
        // from inside to far outside between physics steps — the wall rule must not grab it.
        Vector2 pos = ctx.BallPosition;

        // GoalLineOut owns every grounded loose exit beyond a goal line and outside the mouth,
        // including a diagonal full escape. It is serialized after this component in the live
        // scene, so stand down explicitly instead of racing its FixedUpdate.
        if (goalLineOut != null && goalLineOut.OwnsLooseOut(ctx, pos)) return;

        if (Mathf.Abs(pos.x) > escapeXThreshold || Mathf.Abs(pos.y) > escapeYThreshold)
        {
            StartCoroutine(RecoverEscapedBall(ctx));
            return;
        }

        // The scene's solid wall colliders sit INSIDE the old outYThreshold. Waiting for
        // the ball centre to reach 4.2 therefore let physics bounce it back first. Detect
        // the actual inner collider faces and project one physics step ahead so this rule
        // claims a hard outgoing ball before the wall collision can repel it.
        int side = PlayableSideCrossing(ctx, pos);
        if (side != 0)
        {
            float outwardSpeed = Mathf.Abs(ctx.Ball.linearVelocity.y);
            if (outwardSpeed >= minimumExitSpeed)
                StartCoroutine(RecoverEscapedBall(ctx));
            else
                ReflectSoftBall(ctx, side);
        }
    }

    private void FindPlayableEdgeColliders()
    {
        topWall = null;
        bottomWall = null;
        foreach (PoolLineFloat line in Object.FindObjectsByType<PoolLineFloat>())
        {
            if (line == null) continue;
            BoxCollider2D wall = line.GetComponent<BoxCollider2D>();
            if (wall == null || !wall.enabled || wall.isTrigger || wall.bounds.size.x < 5f) continue;

            if (wall.bounds.center.y > 0f &&
                (topWall == null || wall.bounds.center.y > topWall.bounds.center.y))
                topWall = wall;
            else if (wall.bounds.center.y < 0f &&
                     (bottomWall == null || wall.bounds.center.y < bottomWall.bounds.center.y))
                bottomWall = wall;
        }
    }

    // Returns +1 for the top edge, -1 for the bottom edge, 0 for no outward crossing.
    private int PlayableSideCrossing(MatchContext ctx, Vector2 pos)
    {
        if (ctx == null || ctx.Ball == null) return 0;
        if ((topWall == null || bottomWall == null) && Time.frameCount % 60 == 0)
            FindPlayableEdgeColliders();

        Collider2D ballCollider = ctx.Ball.GetComponent<Collider2D>();
        float radiusY = ballCollider != null ? ballCollider.bounds.extents.y : 0f;
        float projectedY = pos.y + ctx.Ball.linearVelocity.y * Time.fixedDeltaTime;

        float topLimit = topWall != null ? topWall.bounds.min.y - radiusY : outYThreshold;
        float bottomLimit = bottomWall != null ? bottomWall.bounds.max.y + radiusY : -outYThreshold;

        float velocityY = ctx.Ball.linearVelocity.y;

        if (velocityY > 0f && (pos.y >= topLimit || projectedY >= topLimit)) return 1;
        if (velocityY < 0f && (pos.y <= bottomLimit || projectedY <= bottomLimit)) return -1;
        return 0;
    }

    private void ReflectSoftBall(MatchContext ctx, int side)
    {
        if (ctx == null || ctx.Ball == null) return;
        Collider2D ballCollider = ctx.Ball.GetComponent<Collider2D>();
        float radiusY = ballCollider != null ? ballCollider.bounds.extents.y : 0f;
        float limit = side > 0
            ? (topWall != null ? topWall.bounds.min.y - radiusY : outYThreshold)
            : (bottomWall != null ? bottomWall.bounds.max.y + radiusY : -outYThreshold);

        Vector2 p = ctx.Ball.position;
        p.y = limit - side * 0.01f; // stay just inside so the next step cannot re-trigger
        ctx.Ball.position = p;
        ctx.Ball.transform.position = p;

        Vector2 v = ctx.Ball.linearVelocity;
        v.y = -v.y * softBounceRetention;
        ctx.Ball.linearVelocity = v;
    }

    // ---- full-escape recovery ----

    // Bounce/settle the escaped ball on the deck, pause, then restart through the awarded
    // team's keeper. While this runs, possession is claimed for the awarded team so every
    // loose-ball rule (this one's wall rule, GoalLineOut, grabs/steals via BallGrabbable)
    // stands down off the parked ball. If any other system claims the ball mid-sequence
    // (quarter break → sprint duel, a restart), the sequence aborts and defers to it.
    IEnumerator RecoverEscapedBall(MatchContext ctx)
    {
        recovering = true;
        Rigidbody2D ball = ctx.Ball;

        // Same ruling as the wall rule: the team that did NOT touch it last is awarded —
        // that IS the defending team of whatever attack sent the ball out.
        TeamSide award = ctx.LastTouchTeam != null ? ctx.EnemyOf(ctx.LastTouchTeam) : ctx.PlayerTeam;
        if (award == null) award = ctx.PlayerTeam;

        Vector2 exit = ball.position;
        Vector2 slide = ball.linearVelocity.sqrMagnitude > 1e-4f
            ? ball.linearVelocity.normalized
            : (exit.sqrMagnitude > 1e-4f ? exit.normalized : Vector2.right);

        ball.transform.SetParent(null);
        ball.simulated = false;          // physics off — the sequence animates the transform
        ball.linearVelocity = Vector2.zero;
        ctx.SetPossession(award);        // fences the parked ball off from every other rule

        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Out - keeper ball " + (award == ctx.PlayerTeam ? "YOU" : "BOT"));

        // Two decaying hops sliding a touch further out: the ball visibly bounces on the
        // deck and comes to rest just outside where it escaped (kept near the pool so the
        // camera's clamped view still shows it).
        Vector2 rest = new Vector2(Mathf.Clamp(exit.x + slide.x * 0.6f, -9f, 9f),
                                   Mathf.Clamp(exit.y + slide.y * 0.6f, -5.1f, 5.1f));
        Vector2 mid = Vector2.Lerp(exit, rest, 0.65f);
        yield return Hop(ball, exit, mid, 0.3f, settleSeconds * 0.6f);
        yield return Hop(ball, mid, rest, 0.12f, settleSeconds * 0.4f);

        // dead-ball pause ("someone fetches it"), abort-aware
        float wait = throwInDelay;
        while (wait > 0f)
        {
            if (Claimed(ctx, ball)) { recovering = false; yield break; }
            wait -= Time.deltaTime;
            yield return null;
        }
        if (Claimed(ctx, ball)) { recovering = false; yield break; }

        // Keeper restart: drop the ball just in FRONT of the awarded team's keeper (toward
        // the field) with a grab ban on the other team — the keeper's own save/collect logic
        // picks it up within its normal release-cooldown beat, holds, and distributes as it
        // always does (bot: auto pass-out; human team: full keeper control). The grab ban
        // lifts automatically the moment the keeper takes possession (SetPossession does it).
        Goalkeeper keeper = KeeperOf(award);
        Vector2 drop = Vector2.zero; // no keeper in the scene → restart from centre
        if (keeper != null)
        {
            float toField = keeper.transform.position.x >= 0f ? -1f : 1f;
            drop = (Vector2)keeper.transform.position + new Vector2(toField * 0.5f, 0f);
        }

        ball.transform.position = drop;
        ball.simulated = true;
        ball.position = drop;            // sync the physics pose to the teleport
        ball.linearVelocity = Vector2.zero;
        ctx.SetPossession(null);         // loose for exactly the beat the keeper needs
        TeamSide enemy = ctx.EnemyOf(award);
        if (enemy != null) ctx.SetGrabBan(enemy); // an attacker camped at the cage can't snipe the throw-in

        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock();
        recovering = false;
    }

    // One decaying deck bounce: slide from → to while a small parabolic hop lifts the ball
    // (up-screen = up in the air, the same visual language as the arc system's air sprite).
    IEnumerator Hop(Rigidbody2D ball, Vector2 from, Vector2 to, float height, float seconds)
    {
        float t0 = Time.time;
        while (Time.time - t0 < seconds)
        {
            // Claimed mid-hop (incl. a freeze: the sprint duel pins the ball with physics
            // off, which the simulated/parent checks alone wouldn't see) → stop steering it.
            if (Claimed(MatchContext.Instance, ball)) yield break;
            float t = Mathf.Clamp01((Time.time - t0) / Mathf.Max(seconds, 0.01f));
            float arc = 4f * t * (1f - t);
            ball.transform.position = Vector2.Lerp(from, to, t) + Vector2.up * (height * arc);
            yield return null;
        }
    }

    // Another system took the ball (re-parented it, re-simulated it, or froze play for a
    // restart that will reposition it) → this sequence must stand down and defer.
    static bool Claimed(MatchContext ctx, Rigidbody2D ball)
        => ball == null || ball.transform.parent != null || ball.simulated ||
           (ctx != null && ctx.PlayFrozen);

    // The keeper guarding the goal `team` currently defends — matched by which half its
    // body is in vs team.defendGoal (the same x-sign rule Goalkeeper.KeeperTeam uses, so
    // it stays correct after the halftime SwapEnds).
    static Goalkeeper KeeperOf(TeamSide team)
    {
        Goalkeeper[] keepers = Object.FindObjectsByType<Goalkeeper>();
        if (keepers.Length == 0) return null;
        if (team == null || team.defendGoal == null) return keepers[0];
        float sign = Mathf.Sign(team.defendGoal.position.x);
        foreach (Goalkeeper k in keepers)
            if (k != null && Mathf.Sign(k.transform.position.x) == sign) return k;
        return keepers[0];
    }
}
