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
    [Tooltip("HANG TIME (Phase 0): hold this long the instant the ball hits the net — everyone frozen where they stand, ball IN the net, camera still on the action — BEFORE the normal reset sequence (ball to centre, overview camera, formations) begins.")]
    [SerializeField] private float goalHangSeconds = 3.5f;

    // Frame-accuracy gate: a goal only counts if the ball's real path crosses the goal LINE
    // between the posts. These mirror GoalLineOut's serialized defaults — keep them in sync.
    const float GoalLineX = 7f;
    const float GoalMouthHalfHeight = 1.5f;

    private int homeScore = 0; // YOU  (= playerTeam)
    private int awayScore = 0; // BOT  (= botTeam)

    // net-pulse bookkeeping: originals restored even if a pulse is ever cut short
    private Transform pulseNet;
    private Vector3 pulseScale0, pulsePos0;

    // public read-only access for other systems (e.g. MatchTimer's win condition)
    public int HomeScore => homeScore;
    public int AwayScore => awayScore;

    void Awake() { Instance = this; }

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

        RestartAfterGoal(conceding);

        // Net reaction AFTER RestartAfterGoal — its StopAllCoroutines would kill a pulse
        // started any earlier. Plays over the hang-time hold.
        PlayNetReaction(goalNet, netSign);
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
    void RestartAfterGoal(TeamSide concedingTeam)
    {
        MatchContext ctx = MatchContext.Instance;

        if (ctx != null)
        {
            ctx.SetPossession(null);
            ctx.ClearGrabBan();
            ctx.FreezeAll();                      // Phase 0: everyone holds where they stand
        }
        // The net "catches" the ball: kill most of its pace instantly so it can't ricochet
        // around behind the goal; the coroutine stops it fully a beat later.
        if (ball != null) ball.linearVelocity *= 0.15f;

        if (TouchControls.Instance != null) TouchControls.Instance.SetGameplayVisible(false); // no UI during the restart
        StopAllCoroutines();
        StartCoroutine(ResumeAfterGoal(concedingTeam));
    }

    // Goal restart flow, no sprint duel:
    //   Phase 0  HANG TIME (goalHangSeconds): frozen in place, ball IN the net, camera on the
    //            action (the overview only starts with Phase 1) — the celebration hold
    //   Phase 1  celebration settle at the wide overview (goalFreezeSeconds), ball at centre
    //   Phase 2  natural restart spread inside each half; the CONCEDING team takes the ball at centre
    //   Phase 3  silent restart pause (postGoalPauseSeconds): no movement / pass / shoot / steal
    //   Phase 4  un-freeze; the team in possession begins the attack naturally
    IEnumerator ResumeAfterGoal(TeamSide concedingTeam)
    {
        MatchContext ctx = MatchContext.Instance;

        // Phase 0 — hang time. The ball stays in the net (fully stopped after a short
        // settle so the net visibly absorbs it), everyone stays put, the camera keeps its
        // normal follow + goal shake on the net instead of cutting straight to the overview.
        yield return new WaitForSeconds(0.15f);
        if (ball != null) { ball.linearVelocity = Vector2.zero; ball.angularVelocity = 0f; }
        yield return new WaitForSeconds(Mathf.Max(0f, goalHangSeconds - 0.15f));

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

    // ---- net reaction (Task 4): a springy squash on the goal sprite + an impact ripple ----
    // Cheapest thing that reads as "the ball hit the net": the goal object itself gets one
    // strong outward bulge (scale + a small push away from the pool) that wobbles back to
    // rest on a damped spring, plus an expanding white ring at the impact point. No cloth
    // sim, no new art — pure hand-rolled Lerp/sine on the existing net sprite.

    void PlayNetReaction(Transform goalNet, float netSign)
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
            StartCoroutine(NetPulse(goalNet, netSign));
        }
        SpawnNetRipple();
    }

    IEnumerator NetPulse(Transform net, float sign)
    {
        Vector3 s0 = pulseScale0;
        Vector3 p0 = pulsePos0;
        const float Dur = 0.45f;
        float t0 = Time.time;
        while (Time.time - t0 < Dur && net != null)
        {
            float t = (Time.time - t0) / Dur;
            // damped spring: a hard outward bulge on impact, wobbling back to rest
            float bulge = Mathf.Sin(t * 18f) * Mathf.Exp(-4.5f * t);
            net.localScale = new Vector3(s0.x * (1f + 0.22f * bulge),
                                         s0.y * (1f - 0.10f * bulge), s0.z);
            net.localPosition = p0 + new Vector3(sign * 0.09f * bulge, 0f, 0f);
            yield return null;
        }
        if (net != null) { net.localScale = s0; net.localPosition = p0; }
        if (pulseNet == net) pulseNet = null;
    }

    // Expanding, fading white ring where the ball met the net — the same trick as the
    // skip-shot's water ripple in BallFlight. World-space, self-destroys.
    void SpawnNetRipple()
    {
        if (ball == null) return;
        SpriteRenderer ballSr = ball.GetComponent<SpriteRenderer>();
        if (ballSr == null || ballSr.sprite == null) return;

        GameObject go = new GameObject("NetRipple");
        go.transform.position = ball.transform.position;
        SpriteRenderer rs = go.AddComponent<SpriteRenderer>();
        rs.sprite = ballSr.sprite;
        rs.sortingOrder = ballSr.sortingOrder + 1; // over the net art
        rs.color = Color.white;
        StartCoroutine(NetRippleRoutine(go.transform, rs));
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
        if (ring != null) Destroy(ring.gameObject);
    }

    void UpdateText()
    {
        if (playerScoreText != null) playerScoreText.text = homeScore.ToString();
        if (botScoreText != null) botScoreText.text = awayScore.ToString();
    }
}