using UnityEngine;

public class Goalkeeper : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody2D ball;

    [Header("Movement")]
    [SerializeField] private float trackSpeed = 4f;   // how fast it slides
    [SerializeField] private float minY = -2f;        // top/bottom limit of the goal mouth
    [SerializeField] private float maxY = 2f;

    void Update()
    {
        if (ball == null) return;

        // the keeper stays on its own X; it only slides up/down to match the ball's Y
        float targetY = Mathf.Clamp(ball.position.y, minY, maxY);

        Vector3 pos = transform.position;
        pos.y = Mathf.MoveTowards(pos.y, targetY, trackSpeed * Time.deltaTime);
        transform.position = pos;
    }
}