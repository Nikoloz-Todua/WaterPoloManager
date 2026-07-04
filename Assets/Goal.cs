using UnityEngine;

public class Goal : MonoBehaviour
{
    [SerializeField] private string goalSide = "Right"; // "Right" or "Left"
    [SerializeField] private ScoreManager scoreManager;

    void OnTriggerEnter2D(Collider2D other)
    {
        // Pass our transform along so ScoreManager can play the net reaction on THIS goal.
        if (other.CompareTag("Ball") && scoreManager != null)
            scoreManager.BallEnteredGoal(goalSide, transform);
    }
}