using UnityEngine;

public class BotMovement : MonoBehaviour
{
    [SerializeField] private Rigidbody2D ball;     // drag the Ball here in the Inspector
    [SerializeField] private float moveSpeed = 3f; // a bit slower than the player (player is 5)

    private Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        if (ball == null) return;

        // figure out the direction from the bot toward the ball
        Vector2 direction = ((Vector2)ball.position - rb.position).normalized;

        // swim toward the ball
        rb.linearVelocity = direction * moveSpeed;
    }
}