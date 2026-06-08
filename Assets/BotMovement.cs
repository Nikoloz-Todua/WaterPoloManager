using UnityEngine;

public class BotMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody2D ball;          // drag Ball here
    [SerializeField] private Transform defendGoal;      // drag the Goal here (the net the bot protects)
    [SerializeField] private PlayerMovement[] myOpponents; // drag Player and Player2 here (your team)

    [Header("Speeds")]
    [SerializeField] private float chaseSpeed = 3f;
    [SerializeField] private float defendSpeed = 3.5f;

    [Header("Defending")]
    [SerializeField] private float guardDistance = 2f;  // how far in front of its goal it sits

    private Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        if (ball == null) return;

        if (OpponentHasBall())
            Defend();
        else
            ChaseBall();
    }

    bool OpponentHasBall()
    {
        // true if any of your players is currently holding the ball
        if (myOpponents == null) return false;
        foreach (PlayerMovement p in myOpponents)
        {
            if (p != null && p.IsHolding) return true;
        }
        return false;
    }

    void ChaseBall()
    {
        Vector2 dir = ((Vector2)ball.position - rb.position).normalized;
        rb.linearVelocity = dir * chaseSpeed;
    }

    void Defend()
    {
        if (defendGoal == null) { ChaseBall(); return; }

        // stand a little in front of its own goal, on the line between the goal and the ball
        Vector2 goalPos = defendGoal.position;
        Vector2 fromGoalToBall = ((Vector2)ball.position - goalPos).normalized;
        Vector2 guardSpot = goalPos + fromGoalToBall * guardDistance;

        Vector2 dir = (guardSpot - rb.position).normalized;
        rb.linearVelocity = dir * defendSpeed;
    }
}