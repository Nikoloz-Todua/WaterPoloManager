using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Goalkeeper : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody2D ball;

    [Header("Movement")]
    [SerializeField] private float trackSpeed = 4f;
    [SerializeField] private float minY = -2f;
    [SerializeField] private float maxY = 2f;

    private Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        if (ball == null) return;

        float targetY = Mathf.Clamp(ball.position.y, minY, maxY);

        Vector2 pos = rb.position;
        pos.y = Mathf.MoveTowards(pos.y, targetY, trackSpeed * Time.fixedDeltaTime);

        // move via physics so it actually blocks/pushes the ball
        rb.MovePosition(pos);
    }
}