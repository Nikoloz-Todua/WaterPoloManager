using UnityEngine;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private TMP_Text scoreText;

    private int homeScore = 0; // YOU (attack the RIGHT goal)
    private int awayScore = 0; // BOT (attacks the LEFT goal)

    void Start()
    {
        UpdateText();
    }

    // called by a goal when the ball enters it
    public void BallEnteredGoal(string goalSide)
    {
        if (goalSide == "Right")      // ball went into the right net = YOU scored
            homeScore++;
        else if (goalSide == "Left")  // ball went into the left net = BOT scored
            awayScore++;

        UpdateText();
        ResetBall();
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