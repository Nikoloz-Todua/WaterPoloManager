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

    private int homeScore = 0; // YOU  (= playerTeam)
    private int awayScore = 0; // BOT  (= botTeam)

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
    public void BallEnteredGoal(string goalSide)
    {
        // A HELD ball never scores — only a released/loose ball (shot, pass, loose) counts.
        if (MatchContext.Instance != null && !MatchContext.Instance.BallIsLoose) return;

        // Credit the team ATTACKING that physical net (so scoring survives the halftime
        // side-swap — no hardcoded Right=YOU assumption).
        float netSign = goalSide == "Right" ? 1f : -1f;
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

    // A goal restart is NOT a quarter start — there is NO sprint duel (Task 2). The ball sits
    // LOOSE at exact centre and play freezes for the celebration; the rest plays out in
    // ResumeAfterGoal: the CONCEDING team is set up with the ball at centre, a silent pause,
    // then play resumes naturally with that team in possession.
    void RestartAfterGoal(TeamSide concedingTeam)
    {
        MatchContext ctx = MatchContext.Instance;

        ResetBall();                              // ball loose at exact (0,0)
        if (ctx != null)
        {
            ctx.SetPossession(null);
            ctx.ClearGrabBan();
            ctx.ResetBallTouch();                 // camera → wide overview (Task 3)
            ctx.FreezeAll();                      // Phase 1: celebration freeze
        }

        if (TouchControls.Instance != null) TouchControls.Instance.SetGameplayVisible(false); // no UI during the restart
        StopAllCoroutines();
        StartCoroutine(ResumeAfterGoal(concedingTeam));
    }

    // Goal restart flow (Tasks 2 & 3), no sprint duel:
    //   Phase 1  celebration freeze (goalFreezeSeconds)
    //   Phase 2  natural restart spread inside each half; the CONCEDING team takes the ball at centre
    //   Phase 3  silent restart pause (postGoalPauseSeconds): no movement / pass / shoot / steal
    //   Phase 4  un-freeze; the team in possession begins the attack naturally
    IEnumerator ResumeAfterGoal(TeamSide concedingTeam)
    {
        MatchContext ctx = MatchContext.Instance;

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

    void UpdateText()
    {
        if (playerScoreText != null) playerScoreText.text = homeScore.ToString();
        if (botScoreText != null) botScoreText.text = awayScore.ToString();
    }
}