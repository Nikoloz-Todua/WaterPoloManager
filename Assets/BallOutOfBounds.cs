using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Detects a loose ball crossing the playable top/bottom edges and provides the shared
// restart-placement service used by GoalLineOut. Boundary colliders are discovered by their
// geometry/component type, never by scene-object name. Ball-to-boundary collisions are ignored:
// an outgoing shot is allowed to cross naturally and this rule places the restart afterward,
// avoiding the one-frame depenetration "snapshot" back into the pool.
public class BallOutOfBounds : MonoBehaviour
{
    [Header("Playable bounds (fallbacks when no generic line collider is present)")]
    [SerializeField] private float outYThreshold = 4.2f;
    [SerializeField] private float fullEscapeXThreshold = 8.2f;
    [SerializeField] private float restartInset = 0.18f;
    [SerializeField] private float restartMaxX = 6.8f;
    [Tooltip("Hard safety clamp for restart placement, independent of decorative walkway/line collider positions.")]
    [SerializeField] private float safeRestartMaxY = 3.5f;

    [Header("Exit presentation")]
    [Tooltip("Total visible time spent bouncing/sliding on the walkway before the restart pause.")]
    [SerializeField, Range(0.5f, 0.8f)] private float walkwayBounceSeconds = 0.65f;
    [SerializeField, Min(0.2f)] private float walkwayTravelDistance = 1.1f;
    [SerializeField, Min(0f)] private float walkwayPauseSeconds = 0.15f;

    [Header("Goalkeeper OOB assistance")]
    [SerializeField] private float goalkeeperFetchZoneDepth = 3.4f;
    [SerializeField] private float goalkeeperMaxFetchDistance = 5.2f;

    private Collider2D topBoundary;
    private Collider2D bottomBoundary;
    private Collider2D leftBoundary;
    private Collider2D rightBoundary;
    private GoalLineOut goalLineOut;
    private Rigidbody2D watchedBall;
    private Vector2 previousBallPosition;
    private bool havePreviousPosition;
    private readonly List<Collider2D> passThroughPoolLines = new List<Collider2D>();
    private bool exitAnimationActive;
    private bool boundaryIgnoreNeedsRefresh;
    private Transform boundaryIgnoredFetcher;
    private readonly List<Collider2D> ignoredFetcherColliders = new List<Collider2D>();
    private readonly List<Collider2D> ignoredFetcherBoundaries = new List<Collider2D>();

    void Start()
    {
        goalLineOut = GetComponent<GoalLineOut>();
        MatchContext ctx = MatchContext.Instance;
        watchedBall = ctx != null ? ctx.Ball : null;
        FindPlayableBoundaries();
        IgnoreBoundaryCollisions(true);
        if (watchedBall != null)
        {
            previousBallPosition = ctx.BallPosition;
            havePreviousPosition = true;
        }
    }

    void OnDestroy()
    {
        RestoreFetcherBoundaryCollisions();
        IgnoreBoundaryCollisions(false);
    }

    void FixedUpdate()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || ctx.Ball == null) return;
        if (watchedBall != ctx.Ball)
        {
            RestoreFetcherBoundaryCollisions();
            IgnoreBoundaryCollisions(false);
            watchedBall = ctx.Ball;
            FindPlayableBoundaries();
            IgnoreBoundaryCollisions(true);
            havePreviousPosition = false;
        }
        RefreshBoundaryPassThroughAfterColliderToggle();
        SyncFetcherBoundaryCollisions(ctx);

        Vector2 pos = ctx.BallPosition;
        if (ctx.PlayFrozen || !ctx.BallIsLoose || !ctx.Ball.simulated ||
            ctx.OutOfBoundsRestartActive)
        {
            Remember(pos);
            return;
        }

        BallFlight flight = BallFlight.Instance;
        if (flight != null && flight.HighBallActive)
        {
            Remember(pos);
            return;
        }

        // GoalLineOut owns grounded exits beyond either goal line outside the mouth. This check
        // avoids a diagonal high-speed exit being awarded twice in the same physics step.
        if (goalLineOut != null && goalLineOut.OwnsLooseOut(ctx, pos))
        {
            return;
        }

        float radiusY = BallRadiusY(ctx.Ball);
        // A foul is called only after the TRAILING edge clears the OUTSIDE face of the line.
        // Merely touching/riding its inside face remains live. Fallback thresholds represent
        // the line itself, so the ball radius is applied there as well.
        float topFullyOut = topBoundary != null
            ? topBoundary.bounds.max.y + radiusY
            : outYThreshold + radiusY;
        float bottomFullyOut = bottomBoundary != null
            ? bottomBoundary.bounds.min.y - radiusY
            : -outYThreshold - radiusY;
        float topRestartY = topBoundary != null
            ? topBoundary.bounds.min.y - radiusY - restartInset
            : outYThreshold - radiusY - restartInset;
        float bottomRestartY = bottomBoundary != null
            ? bottomBoundary.bounds.max.y + radiusY + restartInset
            : -outYThreshold + radiusY + restartInset;

        if (pos.y >= topFullyOut)
        {
            Vector2 crossing = CrossingAtY(previousBallPosition, pos, topFullyOut);
            AwardRestart(ctx, new Vector2(Mathf.Clamp(crossing.x, -restartMaxX, restartMaxX),
                                          topRestartY), "Out");
            Remember(ctx.BallPosition);
            return;
        }

        if (pos.y <= bottomFullyOut)
        {
            Vector2 crossing = CrossingAtY(previousBallPosition, pos, bottomFullyOut);
            AwardRestart(ctx, new Vector2(Mathf.Clamp(crossing.x, -restartMaxX, restartMaxX),
                                          bottomRestartY), "Out");
            Remember(ctx.BallPosition);
            return;
        }

        // Safety net for a ball that crossed a side in the goal mouth without a goal trigger.
        // Normal outside-the-mouth goal-line exits are deliberately left to GoalLineOut above.
        if (Mathf.Abs(pos.x) > fullEscapeXThreshold)
        {
            float sign = Mathf.Sign(pos.x);
            Vector2 restart = new Vector2(sign * restartMaxX,
                                          Mathf.Clamp(pos.y, bottomRestartY, topRestartY));
            AwardRestart(ctx, restart, "Goal-line out");
            Remember(ctx.BallPosition);
            return;
        }

        Remember(pos);
    }

    void Remember(Vector2 position)
    {
        previousBallPosition = position;
        havePreviousPosition = true;
    }

    Vector2 CrossingAtY(Vector2 from, Vector2 to, float y)
    {
        if (!havePreviousPosition || Mathf.Abs(to.y - from.y) < 0.0001f)
            return new Vector2(to.x, y);
        float t = Mathf.Clamp01((y - from.y) / (to.y - from.y));
        return Vector2.Lerp(from, to, t);
    }

    // GoalLineOut uses the same previous physics position so a fast diagonal shot restarts at
    // the interpolated Y where it crossed the vertical line, not at its later overshoot point.
    public Vector2 VerticalRestartPoint(Vector2 current, float boundaryX, float insideX, float maxY)
    {
        float y = current.y;
        if (havePreviousPosition && Mathf.Abs(current.x - previousBallPosition.x) > 0.0001f)
        {
            float t = Mathf.Clamp01((boundaryX - previousBallPosition.x) /
                                    (current.x - previousBallPosition.x));
            y = Mathf.Lerp(previousBallPosition.y, current.y, t);
        }
        return new Vector2(insideX, Mathf.Clamp(y, -maxY, maxY));
    }

    // Shared by top/bottom and goal-line exits. The ball remains loose and live. Only the
    // awarded team may collect it; the ban and physical collision ignore lift on that grab.
    public void AwardRestart(MatchContext ctx, Vector2 restartPoint, string eventLabel)
    {
        if (ctx == null || ctx.Ball == null || ctx.OutOfBoundsRestartActive || exitAnimationActive)
            return;

        TeamSide offending = ctx.LastTouchTeam;
        TeamSide awarded = offending != null ? ctx.EnemyOf(offending) : ctx.PlayerTeam;
        if (awarded == null) return;
        if (offending == null) offending = ctx.EnemyOf(awarded);
        restartPoint = SafeRestartPoint(restartPoint);

        ctx.SetPossession(null);

        Transform fetcher = SelectFetcher(awarded, restartPoint);
        ctx.BeginOutOfBoundsRestart(awarded, offending, restartPoint, fetcher);
        SyncFetcherBoundaryCollisions(ctx);

        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock();
        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent(eventLabel + " - " +
                                        (awarded == ctx.PlayerTeam ? "YOU" : "BOT"));

        exitAnimationActive = true;
        StartCoroutine(AnimateExitAndPlace(ctx, ctx.Ball, awarded, restartPoint));
    }

    IEnumerator AnimateExitAndPlace(MatchContext ctx, Rigidbody2D ball, TeamSide awarded,
                                    Vector2 restartPoint)
    {
        Vector2 exit = ball.position;
        Vector2 velocity = ball.linearVelocity;
        Vector2 awayFromWater = exit - restartPoint;
        Vector2 travel = velocity.sqrMagnitude > 0.01f ? velocity.normalized
            : (awayFromWater.sqrMagnitude > 0.01f ? awayFromWater.normalized : Vector2.up);
        if (awayFromWater.sqrMagnitude > 0.01f && Vector2.Dot(travel, awayFromWater) < 0f)
            travel = awayFromWater.normalized;

        float extraTravel = Mathf.Clamp(velocity.magnitude * 0.035f, 0f, 0.45f);
        Vector2 rest = exit + travel * (walkwayTravelDistance + extraTravel);
        rest.x = Mathf.Clamp(rest.x, -9f, 9f);
        rest.y = Mathf.Clamp(rest.y, -5.1f, 5.1f);
        Vector2 mid = Vector2.Lerp(exit, rest, 0.62f);

        // The walkway sequence owns the transform. The sprite stays visible for both decaying
        // hops; this intentionally replaces the previous disappearance/reset presentation.
        ball.simulated = false;
        ball.linearVelocity = Vector2.zero;
        ball.angularVelocity = 0f;

        float firstHop = walkwayBounceSeconds * 0.58f;
        yield return WalkwayHop(ctx, ball, awarded, exit, mid, 0.28f, firstHop);
        if (!RestartStillOwned(ctx, ball, awarded))
        {
            CancelExitAnimation(ctx);
            yield break;
        }
        yield return WalkwayHop(ctx, ball, awarded, mid, rest, 0.12f,
                                Mathf.Max(0f, walkwayBounceSeconds - firstHop));
        if (!RestartStillOwned(ctx, ball, awarded))
        {
            CancelExitAnimation(ctx);
            yield break;
        }

        ball.transform.position = rest;
        if (walkwayPauseSeconds > 0f) yield return new WaitForSeconds(walkwayPauseSeconds);
        if (!RestartStillOwned(ctx, ball, awarded))
        {
            CancelExitAnimation(ctx);
            yield break;
        }

        ball.transform.SetParent(null);
        ball.transform.position = restartPoint;
        ball.position = restartPoint;
        ball.simulated = true;
        ball.position = restartPoint; // sync physics after re-enabling simulation
        ball.linearVelocity = Vector2.zero;
        ball.angularVelocity = 0f;
        exitAnimationActive = false;
        ctx.MarkOutOfBoundsRestartReady();
        Remember(restartPoint);
    }

    IEnumerator WalkwayHop(MatchContext ctx, Rigidbody2D ball, TeamSide awarded,
                           Vector2 from, Vector2 to, float height, float seconds)
    {
        if (seconds <= 0f) yield break;
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            if (!RestartStillOwned(ctx, ball, awarded)) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / seconds);
            float arc = 4f * t * (1f - t);
            ball.transform.position = Vector2.Lerp(from, to, t) + Vector2.up * (height * arc);
            ball.transform.Rotate(0f, 0f, 320f * Time.deltaTime);
            yield return null;
        }
        ball.transform.position = to;
    }

    static bool RestartStillOwned(MatchContext ctx, Rigidbody2D ball, TeamSide awarded)
        => ctx != null && ctx == MatchContext.Instance && ball != null && ctx.Ball == ball &&
           ctx.OutOfBoundsRestartActive && ctx.OutOfBoundsRestartTeam == awarded &&
           ball.transform.parent == null && !ctx.PlayFrozen;

    void CancelExitAnimation(MatchContext ctx)
    {
        exitAnimationActive = false;
        if (ctx != null && ctx.OutOfBoundsRestartActive) ctx.ClearGrabBan();
        RestoreFetcherBoundaryCollisions();
    }

    Vector2 SafeRestartPoint(Vector2 point)
    {
        point.x = Mathf.Clamp(point.x, -restartMaxX, restartMaxX);
        point.y = Mathf.Clamp(point.y, -safeRestartMaxY, safeRestartMaxY);
        return point;
    }

    Transform SelectFetcher(TeamSide awarded, Vector2 restartPoint)
    {
        Goalkeeper keeper = KeeperOf(awarded);
        if (keeper != null && awarded.defendGoal != null)
        {
            float defendSign = Mathf.Sign(awarded.defendGoal.position.x);
            bool inDefensiveZone = restartPoint.x * defendSign > 0f &&
                Mathf.Abs(restartPoint.x - awarded.defendGoal.position.x) <= goalkeeperFetchZoneDepth;
            if (inDefensiveZone &&
                Vector2.Distance(keeper.transform.position, restartPoint) <= goalkeeperMaxFetchDistance)
                return keeper.transform;
        }
        return awarded.ClosestMemberTo(restartPoint);
    }

    static Goalkeeper KeeperOf(TeamSide team)
    {
        if (team == null || team.defendGoal == null) return null;
        float sign = Mathf.Sign(team.defendGoal.position.x);
        foreach (Goalkeeper keeper in Object.FindObjectsByType<Goalkeeper>())
            if (keeper != null && Mathf.Sign(keeper.transform.position.x) == sign)
                return keeper;
        return null;
    }

    void FindPlayableBoundaries()
    {
        topBoundary = bottomBoundary = leftBoundary = rightBoundary = null;
        passThroughPoolLines.Clear();
        foreach (PoolLineFloat line in Object.FindObjectsByType<PoolLineFloat>())
        {
            if (line == null || line.GetComponentInParent<Goal>() != null ||
                line.GetComponentInChildren<Goal>() != null) continue;

            foreach (Collider2D boundary in line.GetComponentsInChildren<Collider2D>(true))
            {
                if (boundary == null || !boundary.enabled) continue;
                if (!passThroughPoolLines.Contains(boundary)) passThroughPoolLines.Add(boundary);

                Vector2 size = boundary.bounds.size;
                Vector2 center = boundary.bounds.center;
                if (size.x >= 5f)
                {
                    if (center.y > 0f &&
                        (topBoundary == null || center.y > topBoundary.bounds.center.y))
                        topBoundary = boundary;
                    else if (center.y < 0f &&
                             (bottomBoundary == null || center.y < bottomBoundary.bounds.center.y))
                        bottomBoundary = boundary;
                }
                if (size.y >= 3f)
                {
                    if (center.x > 0f &&
                        (rightBoundary == null || center.x > rightBoundary.bounds.center.x))
                        rightBoundary = boundary;
                    else if (center.x < 0f &&
                             (leftBoundary == null || center.x < leftBoundary.bounds.center.x))
                        leftBoundary = boundary;
                }
            }
        }
    }

    void IgnoreBoundaryCollisions(bool ignore)
    {
        if (watchedBall == null) return;
        Collider2D[] ballColliders = watchedBall.GetComponentsInChildren<Collider2D>(true);

        foreach (Collider2D ballCollider in ballColliders)
            foreach (Collider2D boundary in passThroughPoolLines)
                if (ballCollider != null && boundary != null && ballCollider != boundary)
                    Physics2D.IgnoreCollision(ballCollider, boundary, ignore);
    }

    // During a live restart only the designated fetcher may cross a solid line. Keeping this
    // pair-specific preserves the pool barriers for every other swimmer and restores the normal
    // collision setup immediately after the awarded player/keeper takes possession.
    void SyncFetcherBoundaryCollisions(MatchContext ctx)
    {
        Transform desired = ctx != null && ctx.OutOfBoundsRestartActive
            ? ctx.OutOfBoundsFetcher : null;
        if (desired == boundaryIgnoredFetcher) return;

        RestoreFetcherBoundaryCollisions();
        if (desired == null) return;

        boundaryIgnoredFetcher = desired;
        foreach (Collider2D collider in desired.GetComponentsInChildren<Collider2D>(true))
            if (collider != null && !ignoredFetcherColliders.Contains(collider))
                ignoredFetcherColliders.Add(collider);
        foreach (Collider2D boundary in passThroughPoolLines)
            if (boundary != null && !ignoredFetcherBoundaries.Contains(boundary))
                ignoredFetcherBoundaries.Add(boundary);

        foreach (Collider2D fetcherCollider in ignoredFetcherColliders)
            foreach (Collider2D boundary in ignoredFetcherBoundaries)
                if (fetcherCollider != null && boundary != null && fetcherCollider != boundary)
                    Physics2D.IgnoreCollision(fetcherCollider, boundary, true);
    }

    void RestoreFetcherBoundaryCollisions()
    {
        foreach (Collider2D fetcherCollider in ignoredFetcherColliders)
            foreach (Collider2D boundary in ignoredFetcherBoundaries)
                if (fetcherCollider != null && boundary != null && fetcherCollider != boundary)
                    Physics2D.IgnoreCollision(fetcherCollider, boundary, false);

        ignoredFetcherColliders.Clear();
        ignoredFetcherBoundaries.Clear();
        boundaryIgnoredFetcher = null;
    }

    // BallFlight disables the ball collider during an arc. Unity can discard pair-ignore state
    // when a collider is disabled/re-enabled, so reapply it on that transition before the next
    // physics step; otherwise the first grounded frame can hit a pool line and become trapped.
    void RefreshBoundaryPassThroughAfterColliderToggle()
    {
        if (watchedBall == null) return;
        bool anyEnabled = false;
        foreach (Collider2D ballCollider in watchedBall.GetComponentsInChildren<Collider2D>(true))
            if (ballCollider != null && ballCollider.enabled) { anyEnabled = true; break; }

        if (!anyEnabled)
        {
            boundaryIgnoreNeedsRefresh = true;
            return;
        }
        if (!boundaryIgnoreNeedsRefresh) return;
        IgnoreBoundaryCollisions(true);
        boundaryIgnoreNeedsRefresh = false;
    }

    static float BallRadiusY(Rigidbody2D ball)
    {
        if (ball == null) return 0f;
        float radius = 0f;
        foreach (Collider2D collider in ball.GetComponentsInChildren<Collider2D>(true))
            if (collider != null && collider.enabled)
                radius = Mathf.Max(radius, collider.bounds.extents.y);
        return radius;
    }
}
