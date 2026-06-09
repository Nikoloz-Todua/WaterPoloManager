using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float holdMoveSpeed = 2f;

    [Header("Ball")]
    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private float grabDistance = 1.0f;
    [SerializeField] private float holdOffset = 0.6f;

    [Header("Shooting")]
    [SerializeField] private float maxShootPower = 12f;
    [SerializeField] private float chargeRate = 8f;

    [Header("Passing")]
    [SerializeField] private float passFactor = 2.5f;
    [SerializeField] private float minPassSpeed = 6f;
    [SerializeField] private float maxPassSpeed = 13f;

    [Header("Stealing")]
    [SerializeField] private float stealDistance = 1.2f;
    [SerializeField] private float stealChance = 0.4f;
    [SerializeField] private float stealCooldown = 0.6f;

    [Header("Visual Feedback")]
    [SerializeField] private Color holdingColor = Color.green;
    [SerializeField] private Color activeColor = Color.red;
    [SerializeField] private Color inactiveColor = Color.gray;

    [Header("Aim line")]
    [SerializeField] private LineRenderer aimLine;
    [SerializeField] private float aimLineLength = 2.5f;

    public bool IsActive = false;
    public bool IsHolding => isHolding;
    public Vector2 Facing => lastDirection;

    private Rigidbody2D rb;
    private SpriteRenderer sprite;
    private Vector2 input;
    private Vector2 lastDirection = Vector2.up;
    private float currentPower = 0f;
    private bool isHolding = false;
    private float lastStealTime = -10f;
    private bool stealConsumedSpace = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();
        if (aimLine != null) aimLine.enabled = false;
    }

    void Update()
    {
        // If we lost the ball (e.g. it was stolen), our parent link is gone — clear
        // the stale holding flag before anything reads it, so we don't stay green/aiming.
        if (isHolding && ball != null && ball.transform.parent != transform)
            isHolding = false;

        if (IsActive)
        {
            float x = Input.GetAxisRaw("Horizontal");
            float y = Input.GetAxisRaw("Vertical");
            input = new Vector2(x, y).normalized;

            if (input != Vector2.zero)
                lastDirection = input;

            if (Input.GetKeyDown(KeyCode.E))
            {
                if (isHolding) DropBall();
                else TryGrabBall();
            }

            // Space with no ball = attempt steal. If it succeeds, consume this press
            // so releasing Space doesn't instantly fire a shot.
            if (!isHolding && Input.GetKeyDown(KeyCode.Space))
            {
                TrySteal();
                if (isHolding) stealConsumedSpace = true;
            }

            if (isHolding)
            {
                if (Input.GetKeyDown(KeyCode.B))
                    PassToNearestTeammate();

                if (!stealConsumedSpace)
                {
                    if (Input.GetKey(KeyCode.Space))
                        currentPower = Mathf.Min(currentPower + chargeRate * Time.deltaTime, maxShootPower);

                    if (Input.GetKeyUp(KeyCode.Space))
                    {
                        Shoot();
                        currentPower = 0f;
                    }
                }
            }

            // once the steal press is released, Space goes back to being shoot
            if (Input.GetKeyUp(KeyCode.Space))
                stealConsumedSpace = false;
        }
        else
        {
            input = Vector2.zero;
        }

        if (sprite != null)
        {
            if (isHolding) sprite.color = holdingColor;
            else sprite.color = IsActive ? activeColor : inactiveColor;
        }

        UpdateAimLine();
    }

    void FixedUpdate()
    {
        if (!IsActive) return;
        float speed = isHolding ? holdMoveSpeed : moveSpeed;
        rb.linearVelocity = input * speed;
    }

    void UpdateAimLine()
    {
        if (aimLine == null) return;

        bool show = isHolding;
        aimLine.enabled = show;
        if (!show) return;

        Vector3 start = transform.position;
        Vector3 end = start + (Vector3)(lastDirection * aimLineLength);
        aimLine.SetPosition(0, start);
        aimLine.SetPosition(1, end);
    }

    void TryGrabBall()
    {
        if (ball == null) return;
        if (MatchContext.Instance != null && !MatchContext.Instance.BallGrabbable) return;

        if (Vector2.Distance(transform.position, ball.position) <= grabDistance)
        {
            GrabBall();
        }
    }

    void GrabBall()
    {
        isHolding = true;
        ball.simulated = false;
        ball.linearVelocity = Vector2.zero;
        ball.transform.SetParent(transform);
        ball.transform.localPosition = (Vector3)(lastDirection * holdOffset);

        if (MatchContext.Instance != null)
            MatchContext.Instance.SetPossession(MatchContext.Instance.PlayerTeam);
    }

    void TrySteal()
    {
        if (isHolding || ball == null) return;
        if (Time.time - lastStealTime < stealCooldown) return;

        MatchContext ctx = MatchContext.Instance;
        if (ctx == null) return;

        TeamSide enemy = ctx.EnemyOf(ctx.PlayerTeam);
        if (enemy == null || !ctx.TeamHasBall(enemy)) return;

        Transform carrier = ball.transform.parent;
        if (carrier == null) return;

        if (Vector2.Distance(transform.position, ball.position) > stealDistance) return;

        lastStealTime = Time.time;

        if (Random.value <= stealChance)
        {
            IAgentBody holder = carrier.GetComponent<IAgentBody>();
            if (holder != null) holder.IsHolding = false;

            isHolding = true;
            ball.simulated = false;
            ball.linearVelocity = Vector2.zero;
            ball.transform.SetParent(transform);
            ball.transform.localPosition = (Vector3)(lastDirection * holdOffset);

            ctx.SetPossession(ctx.PlayerTeam);
        }
    }

    void DropBall()
    {
        if (ball == null) return;
        isHolding = false;
        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = Vector2.zero;

        if (MatchContext.Instance != null)
            MatchContext.Instance.SetPossession(null);
    }

    void Shoot()
    {
        if (ball == null) return;
        isHolding = false;
        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = lastDirection * currentPower;

        if (MatchContext.Instance != null)
            MatchContext.Instance.SetPossession(null);
    }

    void PassToNearestTeammate()
    {
        if (ball == null || !isHolding) return;
        if (MatchContext.Instance == null) return;

        TeamSide myTeam = MatchContext.Instance.PlayerTeam;
        if (myTeam == null || myTeam.members == null) return;

        Transform target = null;
        float bestDist = Mathf.Infinity;
        foreach (Transform m in myTeam.members)
        {
            if (m == null || m == transform) continue;
            float d = Vector2.Distance(transform.position, m.position);
            if (d < bestDist) { bestDist = d; target = m; }
        }
        if (target == null) return;

        Vector2 dir = ((Vector2)target.position - (Vector2)transform.position).normalized;
        lastDirection = dir;

        isHolding = false;
        ball.transform.SetParent(null);
        ball.simulated = true;
        float dist = Vector2.Distance(transform.position, target.position);
        ball.linearVelocity = dir * Mathf.Clamp(dist * passFactor, minPassSpeed, maxPassSpeed);

        if (MatchContext.Instance != null)
            MatchContext.Instance.SetPossession(null);
    }

    public void ReleaseBall()
    {
        isHolding = false;
        if (ball != null)
        {
            ball.transform.SetParent(null);
            ball.simulated = true;
        }

        if (MatchContext.Instance != null)
            MatchContext.Instance.SetPossession(null);
    }

    public void TakeOverHeldBall()
    {
        isHolding = true;
        if (ball != null)
        {
            ball.simulated = false;
            ball.transform.SetParent(transform);
            ball.transform.localPosition = (Vector3)(lastDirection * holdOffset);
        }
        if (MatchContext.Instance != null)
            MatchContext.Instance.SetPossession(MatchContext.Instance.PlayerTeam);
    }

    void LateUpdate()
    {
        if (isHolding && ball != null)
        {
            ball.transform.localPosition = (Vector3)(lastDirection * holdOffset);
        }
    }
}