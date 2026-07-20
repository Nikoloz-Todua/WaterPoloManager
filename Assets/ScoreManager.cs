using System.Collections;
using UnityEngine;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private TMP_Text playerScoreText;
    [SerializeField] private TMP_Text botScoreText;
    [SerializeField] private TeamSide playerTeam;
    [SerializeField] private TeamSide botTeam;
    [SerializeField] private float goalFreezeSeconds = 1f; // Phase 1: celebration settle right after a goal
    [Tooltip("Phase 3: silent restart pause AFTER the conceding team is set up with the ball at centre — players still, no UI, no countdown. A goal is NOT a quarter start, so there is NO sprint duel here (Task 2).")]
    [SerializeField] private float postGoalPauseSeconds = 3f;
    [Tooltip("Fallback Phase 0 hold used only if a goal replay could not be captured.")]
    [SerializeField] private float goalHangSeconds = 3.5f;

    [Header("Goal replay")]
    [Tooltip("Live in-net beat before the broadcast replay cuts in, leaving time for the net reaction and goal shake to read.")]
    [SerializeField] private float replayLeadInSeconds = 0.55f;
    [Tooltip("Short return to the live in-net shot after replay, before the normal centre restart begins.")]
    [SerializeField] private float replayReturnHoldSeconds = 0.2f;

    // Hang-time buoyancy (Task 3): while resting in the net the ball keeps a gentle float instead
    // of freezing solid — a small vertical bob plus a tinier horizontal sway around where it settled.
    const float NetBobAmpY = 0.07f; // vertical bob amplitude (units)
    const float NetBobAmpX = 0.035f;// horizontal sway amplitude (units)
    const float NetBobRate = 2.6f;  // rad/s — slow and calm, reads as floating not bouncing

    // Frame-accuracy gate: a goal only counts if the ball's real path crosses the goal LINE
    // between the posts. These mirror GoalLineOut's serialized defaults — keep them in sync.
    const float GoalLineX = 7f;
    const float GoalMouthHalfHeight = 1.5f;

    private int homeScore = 0; // YOU  (= playerTeam)
    private int awayScore = 0; // BOT  (= botTeam)

    // net-pulse bookkeeping: originals restored even if a pulse is ever cut short
    private Transform pulseNet;
    private Vector3 pulseScale0, pulsePos0;
    private Transform netRippleTransform;
    private SpriteRenderer netRippleRenderer;
    private CameraFollow goalCamera;

    // 2026-07-09d: re-entrancy latch. A goal starts a ~7.5s restart during which the ball is
    // parked loose in the net (bobbing), reset to centre, handed out — plenty of collider
    // activity near/inside the goal trigger. BallEnteredGoal previously had NO guard against
    // firing again mid-restart, so any re-entry of the trigger during that window could stack
    // spurious goals ("Goal - BOT" lines seconds apart with no play between). One goal at a
    // time: the latch closes the moment a goal counts and reopens when play actually resumes.
    private bool restartInProgress;

    // public read-only access for other systems (e.g. MatchTimer's win condition)
    public int HomeScore => homeScore;
    public int AwayScore => awayScore;

    void Awake()
    {
        Instance = this;
        GoalReplaySystem.EnsureExists(gameObject);
        PrepareNetRipple();
        Camera mainCamera = Camera.main;
        if (mainCamera != null) goalCamera = mainCamera.GetComponent<CameraFollow>();
    }

    void Start()
    {
        UpdateText();
        // The opening is normally the Q1 sprint duel; only do a plain kickoff if there's
        // no SprintDuel in the scene.
        if (SprintDuel.Instance == null) ResetKickoff();
    }

    // called by a goal when the ball enters it
    public void BallEnteredGoal(string goalSide, Transform goalNet)
    {
        // One goal at a time: nothing can score while the previous goal's restart is
        // still running (hang time / reset / handout) — see restartInProgress above.
        if (restartInProgress) return;
        // A HELD ball never scores — only a released/loose ball (shot, pass, loose) counts.
        if (MatchContext.Instance != null && !MatchContext.Instance.BallIsLoose) return;
        if (ball == null) return;

        float netSign = goalSide == "Right" ? 1f : -1f;

        // FRAME-ACCURACY GATE (Task 3): touching the trigger box is NOT a goal. The trigger
        // is a physical box (~0.8u deep, plus the ball's own radius), so skims along its
        // front face, corner clips and sideways drifts used to score without the ball ever
        // passing between the posts. Project the ball's REAL path onto the goal line: it
        // must be moving INTO this net, and the crossing point must be inside the mouth.
        // (There is no aim assist anywhere in a shot's flight — raw aim vector all the way —
        // this makes the SCORING end equally honest: badly aimed shots now miss.)
        float vx = ball.linearVelocity.x;
        if (vx * netSign <= 0.05f) return;                          // skimming / drifting / bouncing out
        float steps = (netSign * GoalLineX - ball.position.x) / vx; // time to the line (slightly
        float yAtLine = ball.position.y + ball.linearVelocity.y * steps; // negative for a fast ball
        if (Mathf.Abs(yAtLine) > GoalMouthHalfHeight) return;       // caught just past it — still exact)

        Collider2D goalCol = goalNet != null ? goalNet.GetComponent<Collider2D>() : null;
        Vector2 impactWorld = new Vector2(netSign * GoalLineX, yAtLine);
        Vector2 impactNorm = NormalizedImpact(goalCol, impactWorld);
        float goalHeight = goalCol != null ? goalCol.bounds.size.y : GoalMouthHalfHeight * 2f;

        // Credit the team ATTACKING that physical net (so scoring survives the halftime
        // side-swap — no hardcoded Right=YOU assumption).
        TeamSide scorer = TeamAttacking(netSign);

        if (scorer == playerTeam) homeScore++;
        else if (scorer == botTeam) awayScore++;

        UpdateText();

        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Goal - " + (scorer == playerTeam ? "YOU" : "BOT"));

        // The team that CONCEDED restarts with possession.
        TeamSide conceding = (scorer == playerTeam) ? botTeam : playerTeam;

        // Centre-goal tracking (Feature 3): if the scorer team's CENTRE released the
        // shot, the conceding team remembers it — feeds the bot's adaptive Drop defense.
        Transform shooter = MatchContext.Instance != null ? MatchContext.Instance.LastReleaser : null;
        if (scorer != null && conceding != null && shooter != null &&
            scorer.Contains(shooter) && scorer.RoleOf(shooter) == TeamSide.Role.Center)
            conceding.goalsConcededFromCenter++;

        // Snapshot the exact validated scoring frame BEFORE RestartAfterGoal parks the ball in
        // the net and freezes physics. The recorder already owns the preceding rolling history.
        GoalReplaySystem replay = GoalReplaySystem.Instance;
        if (replay != null)
        {
            // LastReleaser can be stale after an opponent's loose deflection. Only print a name
            // when that transform genuinely belongs to the team credited with this goal.
            Transform creditedShooter = scorer != null && shooter != null && scorer.Contains(shooter)
                ? shooter : null;
            replay.CaptureGoalReplay(scorer == playerTeam, creditedShooter, homeScore, awayScore,
                                     netSign);
        }

        if (goalCamera != null)
            goalCamera.FocusOnScoringBall(impactWorld, netSign,
                                          Mathf.Max(0.1f, replayLeadInSeconds + 0.1f));

        Vector2 hangAnchor = new Vector2(netSign * (GoalLineX + 0.22f),
                                         Mathf.Clamp(yAtLine, -GoalMouthHalfHeight + 0.08f,
                                                                 GoalMouthHalfHeight - 0.08f));
        RestartAfterGoal(conceding, hangAnchor);

        // WHERE the ball actually crossed the goal line (x = the line, y = the projected
        // crossing height from the frame-accuracy gate above) → the true impact point, and its
        // position NORMALIZED to the goal collider's real bounds (0..1 left→right, 0..1
        // bottom→top). Everything the net reaction does is driven off this, so a top-corner goal
        // reacts differently from a bottom-corner or centre goal — and it survives an art/collider
        // swap because nothing is a hardcoded pixel/world position.
        // Net reaction AFTER RestartAfterGoal — its StopAllCoroutines would kill a pulse
        // started any earlier. Plays over the hang-time hold.
        PlayNetReaction(goalNet, netSign, impactNorm, impactWorld, goalHeight);
    }

    // The world impact point expressed as a 0..1 coordinate inside the goal collider's REAL
    // bounds (x = left→right across the collider, y = bottom→top). Falls back to a dead-centre
    // (0.5, 0.5) hit if the goal has no Collider2D. Reads the live collider bounds, so no pixel
    // or world size is baked in — swap the goal sprite/collider for different art and this keeps
    // mapping impacts correctly.
    Vector2 NormalizedImpact(Collider2D goalCol, Vector2 worldPoint)
    {
        if (goalCol == null) return new Vector2(0.5f, 0.5f);
        Bounds b = goalCol.bounds;
        float nx = b.size.x > 1e-4f ? Mathf.InverseLerp(b.min.x, b.max.x, worldPoint.x) : 0.5f;
        float ny = b.size.y > 1e-4f ? Mathf.InverseLerp(b.min.y, b.max.y, worldPoint.y) : 0.5f;
        return new Vector2(Mathf.Clamp01(nx), Mathf.Clamp01(ny));
    }

    // Which team currently attacks the net on the given side (+1 = Right, -1 = Left).
    TeamSide TeamAttacking(float netSign)
    {
        if (playerTeam != null && playerTeam.attackGoal != null &&
            Mathf.Sign(playerTeam.attackGoal.position.x) == netSign) return playerTeam;
        if (botTeam != null && botTeam.attackGoal != null &&
            Mathf.Sign(botTeam.attackGoal.position.x) == netSign) return botTeam;
        return null;
    }

    // A goal restart is NOT a quarter start — there is NO sprint duel (Task 2). Play freezes
    // THE INSTANT the ball hits the net and holds there (hang time — ball in the net, players
    // where they stand); only then does the original reset sequence run in ResumeAfterGoal:
    // ball to centre + overview camera, the CONCEDING team set up with the ball, a silent
    // pause, then play resumes naturally with that team in possession.
    void RestartAfterGoal(TeamSide concedingTeam, Vector2 hangAnchor)
    {
        restartInProgress = true; // cleared at the end of ResumeAfterGoal (Phase 4)
        MatchContext ctx = MatchContext.Instance;

        if (ctx != null)
        {
            ctx.SetPossession(null);
            ctx.ClearGrabBan();
            ctx.FreezeAll();                      // Phase 0: everyone holds where they stand
        }
        // A counted goal is already fully inside the net. Take it out of physics on this
        // exact frame and anchor it behind the goal line so no hard shot can rebound back
        // into the border/play area before the scoring sequence completes.
        if (ball != null)
        {
            ball.transform.SetParent(null);
            ball.linearVelocity = Vector2.zero;
            ball.angularVelocity = 0f;
            ball.simulated = false;
            SetBallPose(hangAnchor);
        }

        if (TouchControls.Instance != null) TouchControls.Instance.SetGameplayVisible(false); // no UI during the restart
        StopAllCoroutines();
        StartCoroutine(ResumeAfterGoal(concedingTeam));
    }

    // Goal restart flow, no sprint duel:
    //   Phase 0  live goal beat → skippable cinematic replay → brief return to the live net.
    //            If recording was unavailable, the original goalHangSeconds hold is retained.
    //   Phase 1  celebration settle at the wide overview (goalFreezeSeconds), ball at centre
    //   Phase 2  natural restart spread inside each half; the CONCEDING team takes the ball at centre
    //   Phase 3  silent restart pause (postGoalPauseSeconds): no movement / pass / shoot / steal
    //   Phase 4  un-freeze; the team in possession begins the attack naturally
    IEnumerator ResumeAfterGoal(TeamSide concedingTeam)
    {
        MatchContext ctx = MatchContext.Instance;

        // Phase 0 — first let the live goal reaction read, then replay the actual rolling match
        // history while gameplay remains frozen. GoalReplaySystem restores this exact in-net
        // state before returning, so the established centre restart below is untouched. A clip
        // can always be skipped; if capture was unavailable, retain the proven old hang instead.
        GoalReplaySystem replay = GoalReplaySystem.Instance;
        if (replay != null && replay.HasCapturedGoalReplay)
        {
            yield return StartCoroutine(BallNetBob(Mathf.Max(0f, replayLeadInSeconds)));
            yield return replay.StartCoroutine(replay.PlayCapturedGoalReplay());
            yield return StartCoroutine(BallNetBob(Mathf.Max(0f, replayReturnHoldSeconds)));
        }
        else
        {
            yield return StartCoroutine(BallNetBob(Mathf.Max(0f, goalHangSeconds)));
        }

        // ---- the original reset sequence begins only now ----
        ResetBall();                              // ball loose at exact (0,0)
        if (ctx != null) ctx.ResetBallTouch();    // camera → wide overview (Task 3)

        // Phase 1 — celebration settle.
        yield return new WaitForSeconds(goalFreezeSeconds);

        // Phase 2 — natural spread (not a rigid goal-line); the conceding team gets the restart
        // at exact centre with the ball in hand (mates spread behind in their own half), while
        // the scoring team sits back into defensive positions.
        TeamSide scoringTeam = (concedingTeam == playerTeam) ? botTeam : playerTeam;
        if (concedingTeam != null) concedingTeam.SnapToRestartFormation(true);
        if (scoringTeam != null) scoringTeam.SnapToRestartFormation(false);

        Transform restartTaker = FirstMember(concedingTeam);
        if (ctx != null && restartTaker != null)
        {
            restartTaker.position = new Vector3(0f, 0f, restartTaker.position.z);
            ctx.GiveBallTo(restartTaker, concedingTeam); // conceding team now holds the ball at centre
            ctx.ResetBallTouch();                        // hold the wide overview through the pause (Task 3)
        }

        // Phase 3 — silent restart pause: still frozen, ball held at centre, no UI, no countdown.
        yield return new WaitForSeconds(postGoalPauseSeconds);

        // Phase 4 — resume play. The holder begins the attack: a bot relays the kickoff to its
        // deepest mate, a human is free to pass/move immediately (the pending flag clears on the
        // first move). Control auto-follows to the holder.
        if (ctx != null)
        {
            ctx.Unfreeze();
            ctx.SetKickoffPass(concedingTeam);
            ctx.MarkBallTouched();               // camera eases back into the follow (Task 3)
        }
        if (TouchControls.Instance != null) TouchControls.Instance.SetGameplayVisible(true);
        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock();
        restartInProgress = false; // play is live again — the goal trigger may score again
    }

    // Gentle in-net buoyancy during the goal hang (Task 3). The ball floats around where it
    // settled — a small vertical bob plus a tinier horizontal sway, easing from a slightly larger
    // initial settle down to a calm idle float — so the frozen goal celebration never looks like a
    // paused screenshot. Play is frozen and nothing else drives the loose ball here (its velocity
    // is zero and BallFlight runs no flight), so setting its position directly is safe.
    IEnumerator BallNetBob(float duration)
    {
        if (ball == null)
        {
            if (duration > 0f) yield return new WaitForSeconds(duration);
            yield break;
        }
        Vector2 rest = ball.transform.position;
        float phase = Random.value * Mathf.PI * 2f;   // random phase so it never looks mechanical
        float t0 = Time.time;
        while (Time.time - t0 < duration)
        {
            float t = Time.time - t0;
            float ease = Mathf.Lerp(1.25f, 0.85f, Mathf.Clamp01(t / Mathf.Max(duration, 0.01f)));
            float y = Mathf.Sin(t * NetBobRate + phase) * NetBobAmpY * ease;
            float x = Mathf.Sin(t * NetBobRate * 0.55f + phase) * NetBobAmpX * ease;
            SetBallPose(rest + new Vector2(x, y));
            yield return null;
        }
        if (ball != null) SetBallPose(rest);
    }

    void SetBallPose(Vector2 position)
    {
        if (ball == null) return;
        ball.position = position;
        Vector3 p = ball.transform.position;
        ball.transform.position = new Vector3(position.x, position.y, p.z);
    }

    Transform FirstMember(TeamSide team)
    {
        if (team == null || team.members == null) return null;
        foreach (Transform m in team.members)
            if (m != null) return m; // excluded members are null → first available
        return null;
    }

    // Plain kickoff (fallback opening when there's no SprintDuel).
    void ResetKickoff()
    {
        ResetBall();
        if (playerTeam != null) playerTeam.SnapToKickoffFormation();
        if (botTeam != null) botTeam.SnapToKickoffFormation();
    }

    void ResetBall()
    {
        if (ball == null) return;
        ball.transform.SetParent(null);          // drop any carrier parent first
        ball.simulated = true;
        ball.linearVelocity = Vector2.zero;
        ball.angularVelocity = 0f;
        ball.position = Vector2.zero;            // physics body -> exact centre
        ball.transform.position = Vector3.zero;  // transform -> exact (0,0,0)
    }

    // ---- net reaction: a springy squash on the goal sprite + an impact ripple, LOCALIZED to
    // where the ball actually hit (Task 2) ----
    // Cheapest thing that reads as "the ball hit the net HERE": the goal object gets one strong
    // bulge (scale + a push that leans toward the struck spot) that wobbles back on a damped
    // spring, plus an expanding white ring at the exact crossing point. A top-corner goal kicks
    // the net up-and-out, a bottom-corner goal down-and-out, and a centre goal straight out;
    // corner hits punch a touch harder. No cloth sim, no new art — hand-rolled Lerp/sine on the
    // existing net sprite, all driven by the normalized impact so an art swap needs no retuning.

    void PlayNetReaction(Transform goalNet, float netSign, Vector2 impactNorm, Vector2 impactWorld, float goalHeight)
    {
        // a previous pulse could have been cut short by StopAllCoroutines — restore it first
        if (pulseNet != null)
        {
            pulseNet.localScale = pulseScale0;
            pulseNet.localPosition = pulsePos0;
            pulseNet = null;
        }
        if (goalNet != null)
        {
            pulseNet = goalNet;
            pulseScale0 = goalNet.localScale;
            pulsePos0 = goalNet.localPosition;
            StartCoroutine(NetPulse(goalNet, netSign, impactNorm, goalHeight));
        }
        SpawnNetRipple(impactWorld);
    }

    IEnumerator NetPulse(Transform net, float sign, Vector2 impactNorm, float goalHeight)
    {
        Vector3 s0 = pulseScale0;
        Vector3 p0 = pulsePos0;

        // Impact as signed offsets from the net centre, in [-1, 1]:
        //   iy < 0 = hit LOW, iy > 0 = hit HIGH (top/bottom corner is the salient cue).
        float iy = Mathf.Clamp(impactNorm.y * 2f - 1f, -1f, 1f);
        float corner = Mathf.Abs(iy);              // 0 dead-centre → 1 hard in a corner
        float intensity = 1f + 0.6f * corner;      // corner goals punch a bit harder
        // Vertical throw is a fraction of the goal's REAL height (not a baked distance), so it
        // scales with whatever goal art/collider is in the scene.
        float yThrow = 0.10f * goalHeight;

        const float Dur = 0.45f;
        float t0 = Time.time;
        while (Time.time - t0 < Dur && net != null)
        {
            float t = (Time.time - t0) / Dur;
            // damped spring: a hard bulge on impact, wobbling back to rest
            float bulge = Mathf.Sin(t * 18f) * Mathf.Exp(-4.5f * t);

            // outward (into the net) stretch on x; a y-squash that folds a little MORE for a
            // corner hit (the net creases where it was struck)
            net.localScale = new Vector3(
                s0.x * (1f + 0.22f * intensity * bulge),
                s0.y * (1f - (0.10f + 0.06f * corner) * bulge),
                s0.z);

            // nudge outward from the pool (sign*x) AND toward the struck height (iy*y): a top
            // goal leans the net up-and-out, a bottom goal down-and-out, a centre goal straight out
            net.localPosition = p0 + new Vector3(
                sign * 0.09f * intensity * bulge,
                iy * yThrow * bulge,
                0f);
            yield return null;
        }
        if (net != null) { net.localScale = s0; net.localPosition = p0; }
        if (pulseNet == net) pulseNet = null;
    }

    // Expanding, fading white ring at the EXACT point the ball crossed the net (the projected
    // line-crossing, not just the ball's current pose) — same trick as the skip-shot's water
    // ripple in BallFlight. World-space, self-destroys.
    void PrepareNetRipple()
    {
        if (ball == null) return;
        SpriteRenderer ballSr = ball.GetComponent<SpriteRenderer>();
        if (ballSr == null || ballSr.sprite == null) return;

        GameObject go = new GameObject("NetRipple");
        go.transform.SetParent(transform, false);
        netRippleTransform = go.transform;
        netRippleRenderer = go.AddComponent<SpriteRenderer>();
        netRippleRenderer.sprite = ballSr.sprite;
        netRippleRenderer.sortingOrder = ballSr.sortingOrder + 1;
        go.SetActive(false);
    }

    void SpawnNetRipple(Vector2 impactWorld)
    {
        if (netRippleTransform == null || netRippleRenderer == null) PrepareNetRipple();
        if (netRippleTransform == null || netRippleRenderer == null) return;

        netRippleTransform.gameObject.SetActive(true);
        netRippleTransform.position = new Vector3(impactWorld.x, impactWorld.y, ball.transform.position.z);
        netRippleTransform.localScale = Vector3.zero;
        netRippleRenderer.color = Color.white;
        StartCoroutine(NetRippleRoutine(netRippleTransform, netRippleRenderer));
    }

    IEnumerator NetRippleRoutine(Transform ring, SpriteRenderer rs)
    {
        const float Dur = 0.35f, MaxScale = 1.1f;
        float t0 = Time.time;
        while (Time.time - t0 < Dur && ring != null)
        {
            float t = (Time.time - t0) / Dur;
            ring.localScale = Vector3.one * (MaxScale * t); // expand from the impact point
            rs.color = new Color(1f, 1f, 1f, 1f - t);       // fade out
            yield return null;
        }
        if (ring != null) ring.gameObject.SetActive(false);
    }

    void UpdateText()
    {
        if (playerScoreText != null) playerScoreText.text = homeScore.ToString();
        if (botScoreText != null) botScoreText.text = awayScore.ToString();
    }
}
