using UnityEngine;

public class Goal : MonoBehaviour
{
    private int score = 0;

    void OnTriggerEnter2D(Collider2D other)
    {
        // only count it if the thing that entered is the ball
        if (other.CompareTag("Ball"))
        {
            score++;
            Debug.Log("GOAL! Score: " + score);

            // reset the ball to the middle of the pool
            other.attachedRigidbody.linearVelocity = Vector2.zero;
            other.attachedRigidbody.position = Vector2.zero;
        }
    }
}