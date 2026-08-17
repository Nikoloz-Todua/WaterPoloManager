using System.Collections;
using System.Collections.Generic;
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
    const float ScoredBallAbsorbSeconds = 0.38f;
    const float ScoredBallMinTravel = 0.30f;
    const float ScoredBallMaxTravel = 0.50f;

    private int homeScore = 0; // YOU  (= playerTeam)
    private int awayScore = 0; // BOT  (= botTeam)

    // Localized net deformation is an overlay above each existing goal sprite. The original
    // renderer/material is never swapped, recolored or made transparent, so the red/white frame
    // remains rigid and PoolLineFloat keeps owning the complete goal transform.
    const string LocalizedNetShaderName = "WaterPolo/LocalizedGoalNet";
    const string LocalizedNetShaderResource = "Shaders/LocalizedGoalNet";
    static readonly int ImpactLocalId = Shader.PropertyToID("_ImpactLocal");
    static readonly int DeformDirectionUvId = Shader.PropertyToID("_DeformDirectionUV");
    static readonly int DeformAmountId = Shader.PropertyToID("_DeformAmount");
    static readonly int DeformRadiusId = Shader.PropertyToID("_DeformRadius");
    static readonly int WavePhaseId = Shader.PropertyToID("_WavePhase");
    sealed class NetReactionState
    {
        public Transform goal;
        public SpriteRenderer source;
        public SpriteRenderer overlay;
        public Material material;
        public Coroutine routine;
        public int generation;
    }

    readonly Dictionary<Transform, NetReactionState> netReactions =
        new Dictionary<Transform, NetReactionState>();
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
    public bool GoalRestartInProgress => restartInProgress;

    void Awake()
    {
        Instance = this;
        GoalReplaySystem.EnsureExists(gameObject);
        PrepareGoalNetOverlays();
        PrepareNetRipple();
        Camera mainCamera = Camera.main;
        if (mainCamera != null) goalCamera = mainCamera.GetComponent<CameraFollow>();
    }

    void Start()
    {
        EnsureScoreLabels();
        UpdateText();
        // The opening is normally the Q1 sprint duel; only do a plain kickoff if there's
        // no SprintDuel in the scene.
        if (SprintDuel.Instance == null) ResetKickoff();
    }

    // PoolB's serialized playerScoreText slot is currently empty. Build its mirror from the working
    // BotScoreText once at startup so both championship and casual matches show a complete score.
    // The position is inferred from the two club-name labels, so no scene coordinates are required.
    void EnsureScoreLabels()
    {
        if (botScoreText == null)
        {
            GameObject bot = GameObject.Find("BotScoreText");
            if (bot != null) botScoreText = bot.GetComponent<TMP_Text>();
        }
        if (playerScoreText != null)
        {
            playerScoreText.gameObject.name = "PlayerScoreText";
            return;
        }

        GameObject existing = GameObject.Find("PlayerScoreText");
        if (existing != null) playerScoreText = existing.GetComponent<TMP_Text>();
        if (playerScoreText != null || botScoreText == null) return;

        GameObject clone = Instantiate(botScoreText.gameObject, botScoreText.transform.parent);
        clone.name = "PlayerScoreText";
        playerScoreText = clone.GetComponent<TMP_Text>();

        RectTransform scoreRect = playerScoreText.rectTransform;
        RectTransform playerName = GameObject.Find("PlayerNameText")?.GetComponent<RectTransform>();
        RectTransform botName = GameObject.Find("BotNameText")?.GetComponent<RectTransform>();
        RectTransform botScore = botScoreText.rectTransform;
        float scoreboardCenterX = playerName != null && botName != null
            ? (playerName.anchoredPosition.x + botName.anchoredPosition.x) * 0.5f
            : 0f;
        scoreRect.anchoredPosition = new Vector2(
            scoreboardCenterX * 2f - botScore.anchoredPosition.x,
            botScore.anchoredPosition.y);
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
        float impactSpeed = ball.linearVelocity.magnitude;          // capture before the restart stops physics
        Vector2 scoredVelocity = ball.linearVelocity;

        // The upgraded goal keeps its physical outer-net EdgeCollider2D on the visible root
        // and its scoring BoxCollider2D trigger on a GoalLine child. Use that trigger's bounds
        // for the existing position-aware net reaction while leaving the root transform untouched.
        Collider2D goalCol = GoalImpactCollider(goalNet);
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
        {
            MatchPresentationContext.Restore();
            string scorerName = scorer == playerTeam ? MatchPresentationContext.PlayerClub : MatchPresentationContext.OpponentClub;
            EventFeed.Instance.AddEvent("Goal - " + (string.IsNullOrEmpty(scorerName) ? (scorer == playerTeam ? "YOU" : "BOT") : scorerName));
        }

        // The team that CONCEDED restarts with possession.
        TeamSide conceding = (scorer == playerTeam) ? botTeam : playerTeam;

        // Close any half-completed flying exchange before the replay snapshots/reset formations,
        // then authorize every temporary exclusion immediately on the awarded goal.
        if (SubstitutionManager.Instance != null)
            SubstitutionManager.Instance.ResolveForMatchStoppage();
        if (ExclusionManager.Instance != null)
            ExclusionManager.Instance.NotifyGoalAwarded();

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

        RestartAfterGoal(conceding, goalNet, netSign, impactNorm, goalHeight,
                         impactSpeed, scoredVelocity);
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

    static Collider2D GoalImpactCollider(Transform goalNet)
    {
        if (goalNet == null) return null;

        Collider2D[] colliders = goalNet.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null && colliders[i].isTrigger)
                return colliders[i];

        // Backward compatibility with the former hierarchy where the root BoxCollider2D
        // itself was the trigger.
        return goalNet.GetComponent<Collider2D>();
    }

    // BallFlight calls this only for a real collision with a goal root's solid outer-net edge.
    // It deliberately reuses the same presentation owner without touching scoring/restart state.
    public void BallHitPhysicalNet(Transform goalNet, Vector2 impactWorld, float impactSpeed)
    {
        if (restartInProgress || goalNet == null) return;

        float netSign = goalNet.position.x >= 0f ? 1f : -1f;
        Collider2D goalCol = GoalImpactCollider(goalNet);
        Vector2 impactNorm = NormalizedImpact(goalCol, impactWorld);
        float goalHeight = goalCol != null ? goalCol.bounds.size.y : GoalMouthHalfHeight * 2f;
        PlayNetReaction(goalNet, netSign, impactNorm, impactWorld, goalHeight, impactSpeed, false);
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

    // A goal restart is NOT a quarter start — there is NO sprint duel (Task 2). Swimmers freeze
    // the instant a valid line crossing is accepted, while the scored ball alone finishes a short
    // presentation-only absorption into the net; only then does the original reset sequence run:
    // ball to centre + overview camera, the CONCEDING team set up with the ball, a silent
    // pause, then play resumes naturally with that team in possession.
    void RestartAfterGoal(TeamSide concedingTeam, Transform goalNet, float netSign,
                          Vector2 impactNorm, float goalHeight, float impactSpeed,
                          Vector2 scoredVelocity)
    {
        restartInProgress = true; // cleared at the end of ResumeAfterGoal (Phase 4)
        MatchContext ctx = MatchContext.Instance;

        if (ctx != null)
        {
            ctx.SetPossession(null);
            ctx.ClearGrabBan();
            ctx.FreezeAll();                      // Phase 0: everyone holds where they stand
        }
        // From this accepted scoring frame onward the goal sequence owns the ball. Disable real
        // collision immediately (so a queued back-net callback cannot reflect it out), but keep
        // its current pose; AbsorbScoredBall advances it continuously deeper instead of snapping.
        if (ball != null)
        {
            ball.transform.SetParent(null);
            ball.linearVelocity = Vector2.zero;
            ball.angularVelocity = 0f;
            ball.simulated = false;
        }

        if (TouchControls.Instance != null) TouchControls.Instance.SetGameplayVisible(false); // no UI during the restart
        StopAllCoroutines();
        ClearAllNetReactions();
        StartCoroutine(ResumeAfterGoal(concedingTeam, goalNet, netSign, impactNorm,
                                       goalHeight, impactSpeed, scoredVelocity));
    }

    // Goal restart flow, no sprint duel:
    //   Phase 0  live goal beat → skippable cinematic replay → brief return to the live net.
    //            If recording was unavailable, the original goalHangSeconds hold is retained.
    //   Phase 1  celebration settle at the wide overview (goalFreezeSeconds), ball at centre
    //   Phase 2  natural restart spread inside each half; the CONCEDING team takes the ball at centre
    //   Phase 3  silent restart pause (postGoalPauseSeconds): no movement / pass / shoot / steal
    //   Phase 4  un-freeze; the team in possession begins the attack naturally
    IEnumerator ResumeAfterGoal(TeamSide concedingTeam, Transform goalNet, float netSign,
                                Vector2 impactNorm, float goalHeight, float impactSpeed,
                                Vector2 scoredVelocity)
    {
        MatchContext ctx = MatchContext.Instance;

        // Let the accepted ball continue visibly into the back net under deterministic,
        // collider-free absorption. This can never rebound toward the pool and its poses are
        // appended to the already-captured replay clip.
        yield return StartCoroutine(AbsorbScoredBall(goalNet, netSign, impactNorm, goalHeight,
                                                      impactSpeed, scoredVelocity));

        // Phase 0 — first let the live goal reaction read, then replay the actual rolling match
        // history while gameplay remains frozen. GoalReplaySystem restores this exact in-net
        // state before returning, so the established centre restart below is untouched. A clip
        // can always be skipped; if capture was unavailable, retain the proven old hang instead.
        GoalReplaySystem replay = GoalReplaySystem.Instance;
        if (replay != null && replay.HasCapturedGoalReplay)
        {
            yield return StartCoroutine(BallNetBob(Mathf.Max(0f, replayLeadInSeconds)));
            // The clip already owns the captured deformation frames. Enter replay from a clean
            // settled live state so a still-running live coroutine cannot fight replay material
            // values or restore a half-deformed overlay afterward.
            ClearAllNetReactions();
            replay.CaptureGoalPostFrame(true);
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

        // A goal ends every unfinished TEMPORARY exclusion. Restore those roster slots immediately
        // before formation placement so all eligible players take part in the restart; permanent
        // removals live outside activeExclusions and are deliberately never restored.
        if (ExclusionManager.Instance != null)
            ExclusionManager.Instance.EndTemporaryExclusionsForRestart();

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

        // The conceding team may take a timeout once it owns this restart. Let that timeout
        // finish before the existing goal-restart freeze releases play.
        while (TimeoutManager.Instance != null && TimeoutManager.Instance.Active)
            yield return null;

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
            if (GoalReplaySystem.Instance != null)
                GoalReplaySystem.Instance.CaptureGoalPostFrame();
            yield return null;
        }
        if (ball != null) SetBallPose(rest);
        if (GoalReplaySystem.Instance != null)
            GoalReplaySystem.Instance.CaptureGoalPostFrame();
    }

    IEnumerator AbsorbScoredBall(Transform goalNet, float netSign, Vector2 impactNorm,
                                 float goalHeight, float impactSpeed, Vector2 incomingVelocity)
    {
        if (ball == null) yield break;

        Vector2 start = ball.transform.position;
        float speed01 = Mathf.InverseLerp(4f, 20f, impactSpeed);
        float travel = Mathf.Lerp(ScoredBallMinTravel, ScoredBallMaxTravel, speed01);
        float startDepth = netSign * start.x;
        float targetDepth = Mathf.Max(startDepth, GoalLineX) + travel;
        targetDepth = Mathf.Min(targetDepth, GoalLineX + 0.62f);
        targetDepth = Mathf.Max(targetDepth, startDepth); // never pull an already-deep ball outward

        Vector2 direction = incomingVelocity.sqrMagnitude > 0.001f
            ? incomingVelocity.normalized : new Vector2(netSign, 0f);
        Vector2 target = new Vector2(
            netSign * targetDepth,
            Mathf.Clamp(start.y + direction.y * travel * 0.28f,
                        -GoalMouthHalfHeight + 0.08f, GoalMouthHalfHeight - 0.08f));

        float started = Time.unscaledTime;
        bool netImpactPlayed = false;
        while (Time.unscaledTime - started < ScoredBallAbsorbSeconds)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - started) / ScoredBallAbsorbSeconds);
            float eased = 1f - Mathf.Pow(1f - t, 3f); // fast arrival, naturally losing energy
            SetBallPose(Vector2.LerpUnclamped(start, target, eased));

            if (!netImpactPlayed && t >= 0.52f)
            {
                netImpactPlayed = true;
                Vector2 actualImpact = ball.transform.position;
                PlayNetReaction(goalNet, netSign, impactNorm, actualImpact,
                                goalHeight, impactSpeed, true);
            }

            if (GoalReplaySystem.Instance != null)
                GoalReplaySystem.Instance.CaptureGoalPostFrame();
            yield return null;
        }

        SetBallPose(target);
        if (!netImpactPlayed)
            PlayNetReaction(goalNet, netSign, impactNorm, target, goalHeight, impactSpeed, true);
        if (GoalReplaySystem.Instance != null)
            GoalReplaySystem.Instance.CaptureGoalPostFrame();
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

    // ---- localized net reaction ----
    // Each goal keeps its original combined sprite and Sprite-Lit material untouched. A child
    // overlay renders ONLY displaced warm-yellow net samples; the base sprite below guarantees
    // that transparent displaced pixels can never expose the blue pool as a whole-goal flash.
    void PlayNetReaction(Transform goalNet, float netSign, Vector2 impactNorm,
                         Vector2 impactWorld, float goalHeight, float impactSpeed,
                         bool showImpactRing)
    {
        NetReactionState state = EnsureNetReaction(goalNet);
        if (state != null)
        {
            SyncNetOverlay(state);
            Bounds spriteBounds = state.source.sprite.bounds;
            Vector3 localImpact = goalNet.InverseTransformPoint(impactWorld);
            Vector3 localOutward = goalNet.InverseTransformVector(new Vector3(netSign, 0f, 0f));
            Vector2 directionUv = new Vector2(
                localOutward.x / Mathf.Max(spriteBounds.size.x, 0.001f),
                localOutward.y / Mathf.Max(spriteBounds.size.y, 0.001f));

            Vector3 scale = goalNet.lossyScale;
            float averageScale = Mathf.Max(0.001f,
                (Mathf.Abs(scale.x) + Mathf.Abs(scale.y)) * 0.5f);
            float radiusWorld = Mathf.Clamp(goalHeight * 0.62f, 0.82f, 1.15f);
            float radiusLocal = radiusWorld / averageScale;

            state.material.SetVector(ImpactLocalId,
                new Vector4(localImpact.x, localImpact.y, 0f, 0f));
            state.material.SetVector(DeformDirectionUvId,
                new Vector4(directionUv.x, directionUv.y, 0f, 0f));
            state.material.SetFloat(DeformRadiusId, radiusLocal);
            state.material.SetFloat(DeformAmountId, 0f);
            state.overlay.enabled = true;

            float speedStrength = Mathf.Lerp(0.58f, 1.38f,
                Mathf.InverseLerp(3f, 20f, impactSpeed));
            float cornerStrength = Mathf.Lerp(1f, 1.06f,
                Mathf.Abs(impactNorm.y * 2f - 1f));
            state.generation++;
            if (state.routine != null) StopCoroutine(state.routine);
            state.routine = StartCoroutine(LocalizedNetWobble(
                state, speedStrength * cornerStrength, state.generation));
        }
        if (showImpactRing) SpawnNetRipple(impactWorld);
    }

    IEnumerator LocalizedNetWobble(NetReactionState state, float strength, int generation)
    {
        const float Duration = 1.10f;
        const float PushSeconds = 0.16f;
        const float MaxWorldPush = 0.16f;
        float started = Time.unscaledTime;

        while (state != null && generation == state.generation && state.overlay != null &&
               Time.unscaledTime - started < Duration)
        {
            float elapsed = Time.unscaledTime - started;
            float wobble;
            if (elapsed < PushSeconds)
            {
                float push01 = Mathf.Clamp01(elapsed / PushSeconds);
                wobble = push01 * push01 * (3f - 2f * push01); // readable build into the stretch
            }
            else
            {
                float springTime = elapsed - PushSeconds;
                wobble = Mathf.Cos(springTime * 14f) * Mathf.Exp(-3.15f * springTime);
            }

            state.material.SetFloat(DeformAmountId, MaxWorldPush * strength * wobble);
            state.material.SetFloat(WavePhaseId, elapsed * 13f);
            yield return null;
        }

        if (state == null || generation != state.generation) yield break;
        state.material.SetFloat(DeformAmountId, 0f);
        state.overlay.enabled = false;
        state.routine = null;
    }

    void PrepareGoalNetOverlays()
    {
        Goal[] goals = Object.FindObjectsByType<Goal>(FindObjectsInactive.Include);
        for (int i = 0; i < goals.Length; i++)
            if (goals[i] != null) EnsureNetReaction(goals[i].transform);
    }

    NetReactionState EnsureNetReaction(Transform goalNet)
    {
        if (goalNet == null) return null;
        if (netReactions.TryGetValue(goalNet, out NetReactionState existing)) return existing;

        SpriteRenderer source = goalNet.GetComponent<SpriteRenderer>();
        Shader shader = Resources.Load<Shader>(LocalizedNetShaderResource);
        if (shader == null) shader = Shader.Find(LocalizedNetShaderName);
        if (source == null || source.sprite == null || shader == null) return null;

        GameObject overlayObject = new GameObject("LocalizedNetOverlay");
        overlayObject.hideFlags = HideFlags.DontSave;
        overlayObject.transform.SetParent(goalNet, false);
        SpriteRenderer overlay = overlayObject.AddComponent<SpriteRenderer>();
        Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        material.SetColor("_Color", Color.white);
        material.SetColor("_RendererColor", Color.white);
        overlay.sharedMaterial = material;
        overlay.color = Color.white;
        overlay.enabled = false;

        NetReactionState state = new NetReactionState
        {
            goal = goalNet,
            source = source,
            overlay = overlay,
            material = material
        };
        netReactions.Add(goalNet, state);
        SyncNetOverlay(state);
        return state;
    }

    static void SyncNetOverlay(NetReactionState state)
    {
        if (state == null || state.source == null || state.overlay == null) return;
        SpriteRenderer source = state.source;
        SpriteRenderer overlay = state.overlay;
        overlay.sprite = source.sprite;
        overlay.flipX = source.flipX;
        overlay.flipY = source.flipY;
        overlay.drawMode = source.drawMode;
        overlay.size = source.size;
        overlay.maskInteraction = source.maskInteraction;
        overlay.spriteSortPoint = source.spriteSortPoint;
        overlay.sortingLayerID = source.sortingLayerID;
        overlay.sortingOrder = source.sortingOrder + 1;
        overlay.color = Color.white;
    }

    void ClearAllNetReactions()
    {
        foreach (NetReactionState state in netReactions.Values)
        {
            if (state == null) continue;
            state.generation++;
            state.routine = null;
            if (state.material != null) state.material.SetFloat(DeformAmountId, 0f);
            if (state.overlay != null) state.overlay.enabled = false;
        }
    }

    void OnDisable() { ClearAllNetReactions(); }

    void OnDestroy()
    {
        ClearAllNetReactions();
        foreach (NetReactionState state in netReactions.Values)
            if (state != null && state.material != null) Destroy(state.material);
        netReactions.Clear();
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
