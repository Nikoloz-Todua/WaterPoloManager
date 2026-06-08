using UnityEngine;

[RequireComponent(typeof(PlayerMovement))]
public class TeammateAI : MonoBehaviour
{
    [Header("Team")]
    [SerializeField] private TeamSide myTeam;   // drag PlayerTeam here

    [Header("Speeds")]
    [SerializeField] private float chaseSpeed = 4f;
    [SerializeField] private float carrySpeed = 2.5f;
    [SerializeField] private float supportSpeed = 3.5f;

    [Header("Ball handling")]
    [SerializeField] private float grabDistance = 1.2f;
    [SerializeField] private float holdOffset = 0.6f;
    [SerializeField] private float shootRange = 4f;
    [SerializeField] private float shootPower = 11f;

    [Header("Formation")]
    [SerializeField] private Vector2 homeSpot;

    private Rigidbody2D rb;
    private PlayerMovement self;
    private bool isHolding = false;
    private Vector2 lastDirection = Vector2.right;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        self = GetComponent<PlayerMovement>();
    }

    void FixedUpdate()
    {
        // If the human is controlling THIS player, do nothing — PlayerMovement drives it.
        if (self.IsActive)
        {
            // safety: if I was AI-holding and the human just took over, drop my AI hold
            if (isHolding) ReleaseAIHold();
            return;
        }

        var ctx = MatchContext.Instance;
        if (ctx == null || myTeam == null) return;

        if (isHolding) { Attack(ctx); return; }

        if (ctx.BallIsLoose &&
            Vector2.Distance(rb.position, ctx.BallPosition) <= grabDistance)
        {
            GrabBall(ctx);
            return;
        }

        bool weHaveBall = ctx.TeamHasBall(myTeam);

        if (weHaveBall)
        {
            MoveTo(SupportSpot(ctx), supportSpeed);
        }
        else
        {
            Transform closest = myTeam.ClosestMemberTo(ctx.BallPosition);
            if (closest == transform)
            {
                Vector2 dir = (ctx.BallPosition - rb.position).normalized;
                if (dir != Vector2.zero) lastDirection = dir;
                rb.linearVelocity = dir * chaseSpeed;
            }
            else
            {
                MoveTo(homeSpot, supportSpeed);
            }
        }
    }

    void GrabBall(MatchContext ctx)
    {
        isHolding = true;
        Rigidbody2D ball = ctx.Ball;
        ball.simulated = false;
        ball.linearVelocity = Vector2.zero;
        ball.transform.SetParent(transform);
        ball.transform.localPosition = (Vector3)(lastDirection * holdOffset);
        ctx.SetPossession(myTeam);
    }

    void Attack(MatchContext ctx)
    {
        if (myTeam.attackGoal == null) { ReleaseAIHold(); return; }
        Vector2 toGoal = (Vector2)myTeam.attackGoal.position - rb.position;
        lastDirection = toGoal.normalized;
        rb.linearVelocity = lastDirection * carrySpeed;

        if (toGoal.magnitude <= shootRange) Shoot(ctx);
    }

    Vector2 SupportSpot(MatchContext ctx)
    {
        if (myTeam.attackGoal == null) return rb.position;
        return Vector2.Lerp(rb.position, (Vector2)myTeam.attackGoal.position, 0.4f);
    }

    void MoveTo(Vector2 target, float speed)
    {
        Vector2 dir = (target - rb.position);
        if (dir.magnitude < 0.25f) { rb.linearVelocity = Vector2.zero; return; }
        rb.linearVelocity = dir.normalized * speed;
    }

    void Shoot(MatchContext ctx)
    {
        isHolding = false;
        Rigidbody2D ball = ctx.Ball;
        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = Vector2.zero;
        ball.AddForce(lastDirection * shootPower, ForceMode2D.Impulse);
        ctx.SetPossession(null);
    }

    void ReleaseAIHold()
    {
        isHolding = false;
        var ctx = MatchContext.Instance;
        if (ctx != null && ctx.Ball != null)
        {
            ctx.Ball.transform.SetParent(null);
            ctx.Ball.simulated = true;
            ctx.SetPossession(null);
        }
    }

    void LateUpdate()
    {
        if (isHolding && MatchContext.Instance != null)
            MatchContext.Instance.Ball.transform.localPosition = (Vector3)(lastDirection * holdOffset);
    }
}