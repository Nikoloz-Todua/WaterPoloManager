using UnityEngine;

[RequireComponent(typeof(PlayerMovement))]
public class TeammateAI : MonoBehaviour
{
    [Header("Who/what this teammate reads")]
    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private Transform enemyGoal;      // the goal you attack (GoalRight)
    [SerializeField] private Transform ownGoal;        // the goal you defend (GoalLeft)
    [SerializeField] private PlayerMovement teammate;  // the OTHER player (the one you usually control)
    [SerializeField] private Transform opponent;       // the Bot

    [Header("Tuning")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float aheadDistance = 3f;   // how far toward enemy goal it gets open
    [SerializeField] private float spreadDistance = 2f;  // how far it steps off the ball-carrier's line
    [SerializeField] private float avoidOpponent = 1.5f; // pushes its target away from the bot
    [SerializeField] private float arriveDeadzone = 0.3f;// stop jittering when basically arrived

    private Rigidbody2D rb;
    private PlayerMovement self;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        self = GetComponent<PlayerMovement>();
    }

    void FixedUpdate()
    {
        // If the human is controlling THIS player, do nothing — let them drive.
        if (self.IsActive) return;
        if (ball == null) return;

        Vector2 target = DecideTarget();

        Vector2 toTarget = target - rb.position;
        if (toTarget.magnitude <= arriveDeadzone)
        {
            rb.linearVelocity = Vector2.zero; // arrived: hold position
            return;
        }

        rb.linearVelocity = toTarget.normalized * moveSpeed;
    }

    Vector2 DecideTarget()
    {
        bool weHaveBall = (teammate != null && teammate.IsHolding);

        if (weHaveBall && enemyGoal != null && teammate != null)
        {
            // ATTACK: get open ahead of the ball carrier, toward the enemy goal
            Vector2 carrier = teammate.transform.position;
            Vector2 toGoal = ((Vector2)enemyGoal.position - carrier).normalized;

            // a spot ahead of the carrier toward goal...
            Vector2 spot = carrier + toGoal * aheadDistance;

            // ...stepped sideways so we're not directly in line (easier pass lane)
            Vector2 sideways = new Vector2(-toGoal.y, toGoal.x); // perpendicular
            spot += sideways * spreadDistance;

            // ...nudged away from the opponent so we're actually open
            if (opponent != null)
            {
                Vector2 awayFromBot = (spot - (Vector2)opponent.position).normalized;
                spot += awayFromBot * avoidOpponent;
            }

            return spot;
        }
        else if (ownGoal != null)
        {
            // DEFEND/SUPPORT: drop back between our goal and the ball
            Vector2 goalPos = ownGoal.position;
            Vector2 toBall = ((Vector2)ball.position - goalPos).normalized;
            return goalPos + toBall * (aheadDistance); // sit in front of own goal, ball side
        }

        return rb.position; // fallback: stay put
    }
}