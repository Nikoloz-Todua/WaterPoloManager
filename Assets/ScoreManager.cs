using UnityEngine;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TeamSide playerTeam;
    [SerializeField] private TeamSide botTeam;

    private int homeScore = 0; // YOU (attack the RIGHT goal)
    private int awayScore = 0; // BOT (attacks the LEFT goal)

    // public read-only access for other systems (e.g. MatchTimer's win condition)
    public int HomeScore => homeScore;
    public int AwayScore => awayScore;

    void Start()
    {
        UpdateText();
        ResetKickoff(); // clean opening shape
    }

    // called by a goal when the ball enters it
    public void BallEnteredGoal(string goalSide)
    {
        if (goalSide == "Right")      // ball went into the right net = YOU scored
            homeScore++;
        else if (goalSide == "Left")  // ball went into the left net = BOT scored
            awayScore++;

        UpdateText();
        ResetKickoff();
    }

    // Reset the ball to centre AND spread both teams back to their home shapes,
    // so players don't stay bunched after a goal.
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