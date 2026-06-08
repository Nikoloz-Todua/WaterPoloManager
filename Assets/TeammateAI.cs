using UnityEngine;

[RequireComponent(typeof(PlayerMovement))]
public class TeammateAI : MonoBehaviour
{
    [SerializeField] private TeamSide myTeam;
    [SerializeField] private float chaseSpeed = 3f;
    [SerializeField] private float carrySpeed = 1.8f;
    [SerializeField] private float supportSpeed = 2.5f;
    [SerializeField] private float grabDistance = 1.2f;
    [SerializeField] private float holdOffset = 0.6f;
    [SerializeField] private float shootRange = 4f;
    [SerializeField] private float shootPower = 11f;

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
        if (self.IsActive) { if (isHolding) ReleaseAIHold(); return; }

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
        Transform closest = myTeam.ClosestMemberTo(ctx.BallPosition);

        // the closest player chases the ball (only when ball is loose or enemy has it)
        if (!weHaveBall && closest == transform)
        {
            Vector2 dir = (ctx.BallPosition - rb.position).normalized;
            if (dir != Vector2.zero) lastDirection = dir;
            rb.linearVelocity = dir * chaseSpeed;
            return;
        }

        // everyone else goes to their formation spot
        int role = myTeam.RoleIndexOf(transform);
        Vector2 spot = myTeam.FormationSpot(role, weHaveBall, ctx.BallPosition);
        MoveTo(spot, supportSpeed);
    }

    void GrabBall(MatchContext ctx)
    {
        isHolding = true;
        ctx.Ball.simulated = false;
        ctx.Ball.linearVelocity = Vector2.zero;
        ctx.Ball.transform.SetParent(transform);
        ctx.Ball.transform.localPosition = (Vector3)(lastDirection * holdOffset);
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

    void MoveTo(Vector2 target, float speed)
    {
        Vector2 dir = (target - rb.position);
        if (dir.magnitude < 0.3f) { rb.linearVelocity = Vector2.zero; return; }
        rb.linearVelocity = dir.normalized * speed;
    }

    void Shoot(MatchContext ctx)
    {
        isHolding = false;
        ctx.Ball.transform.SetParent(null);
        ctx.Ball.simulated = true;
        ctx.Ball.linearVelocity = Vector2.zero;
        ctx.Ball.AddForce(lastDirection * shootPower, ForceMode2D.Impulse);
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