using System.Collections;
using UnityEngine;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TeamSide playerTeam;
    [SerializeField] private TeamSide botTeam;
    [SerializeField] private float goalFreezeSeconds = 1f; // settle pause after a goal

    private int homeScore = 0; // YOU  (= playerTeam)
    private int awayScore = 0; // BOT  (= botTeam)

    // public read-only access for other systems (e.g. MatchTimer's win condition)
    public int HomeScore => homeScore;
    public int AwayScore => awayScore;

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

    // Conceding team's centre holds the ball at centre; everyone else snaps home; brief
    // settle freeze; then play + shot clock resume.
    void RestartAfterGoal(TeamSide concedingTeam)
    {
        MatchContext ctx = MatchContext.Instance;

        ResetBall();
        if (ctx != null) { ctx.SetPossession(null); ctx.ClearGrabBan(); }

        if (playerTeam != null) playerTeam.SnapToKickoffFormation();
        if (botTeam != null) botTeam.SnapToKickoffFormation();

        Transform center = FirstMember(concedingTeam);
        if (center != null && ctx != null)
        {
            center.position = new Vector3(0f, 0f, center.position.z);
            ctx.GiveBallTo(center, concedingTeam);
        }

        if (ctx != null) ctx.FreezeAll();
        StopAllCoroutines();
        StartCoroutine(ResumeAfterGoal(concedingTeam));
    }

    IEnumerator ResumeAfterGoal(TeamSide concedingTeam)
    {
        yield return new WaitForSeconds(goalFreezeSeconds);
        MatchContext ctx = MatchContext.Instance;
        if (ctx != null)
        {
            ctx.Unfreeze();
            ctx.SetKickoffPass(concedingTeam); // center passes back to its deepest teammate first
        }
        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock(); // fresh clock as play resumes
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
        ball.linearVelocity = Vector2.zero;
        ball.position = Vector2.zero;
    }

    void UpdateText()
    {
        if (scoreText != null)
            scoreText.text = "YOU  " + homeScore + "  -  " + awayScore + "  BOT";
    }
}