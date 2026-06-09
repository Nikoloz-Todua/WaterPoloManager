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
    [SerializeField] private float passFactor = 2.5f; // (legacy; pass speed is charge-based now)
    [SerializeField] private float minPassSpeed = 6f;
    [SerializeField] private float maxPassSpeed = 13f;
    [SerializeField] private float passChargeRate = 1.5f; // pass charge gained per second (0..1)
    private const float PassConeDot = 0.3f;               // teammate must be within this forward cone

    [Header("Stealing")]
    [SerializeField] private float stealDistance = 1.2f;
    [SerializeField] private float stealChance = 0.4f;
    [SerializeField] private float stealCooldown = 0.6f;
    private const float StealFacingDot = 0.3f; // stealer must be within ~70° of the carrier's front

    [Header("Visual Feedback")]
    [SerializeField] private Color holdingColor = Color.green;
    [SerializeField] private Color activeColor = Color.red;
    [SerializeField] private Color inactiveColor = Color.gray;

    [Header("Aim line")]
    [SerializeField] private LineRenderer aimLine;
    [SerializeField] private float aimLineLength = 2.5f; // (legacy; triangle uses the fields below)

    [Header("Aim triangle")]
    [SerializeField] private float aimTriangleLength = 0.7f; // tip distance from the base
    [SerializeField] private float aimTriangleWidth = 0.5f;  // base width
    [SerializeField] private float aimTriangleGap = 0.5f;    // gap from player centre to base
    [SerializeField] private float aimTriangleLineWidth = 0.08f;

    [Header("Power bar")]
    [SerializeField] private float powerBarWidth = 1.0f;
    [SerializeField] private float powerBarHeight = 0.12f;
    [SerializeField] private float powerBarYOffset = 0.9f;

    private LineRenderer powerBar; // built in code, no Inspector wiring needed

    public bool IsActive = false;
    public bool IsHolding => isHolding;
    public Vector2 Facing => lastDirection;

    private Rigidbody2D rb;
    private SpriteRenderer sprite;
    private Vector2 input;
    private Vector2 lastDirection = Vector2.up;
    private float currentPower = 0f;        // shoot charge (0..maxShootPower)
    private float passPower = 0f;           // pass charge (0..1)
    private bool isHolding = false;
    private float lastStealTime = -10f;
    private bool stealConsumedSpace = false;

    // Only one action charges at a time; whichever key was pressed first wins until released.
    private enum Charging { None, Shoot, Pass }
    private Charging chargeMode = Charging.None;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();

        // Configure the existing LineRenderer to draw a closed triangle.
        if (aimLine != null)
        {
            aimLine.useWorldSpace = true;
            aimLine.positionCount = 3;
            aimLine.loop = false; // open chevron ">" — no base line between the tails
            aimLine.startWidth = aimLine.endWidth = aimTriangleLineWidth;
            aimLine.enabled = false;
        }

        BuildPowerBar();
    }

    // Create a self-contained power bar (a thick LineRenderer) above the player.
    // useWorldSpace=false → positions are local, so it follows the player automatically.
    void BuildPowerBar()
    {
        GameObject go = new GameObject("PowerBar");
        go.transform.SetParent(transform, false);

        powerBar = go.AddComponent<LineRenderer>();
        powerBar.useWorldSpace = false;
        powerBar.positionCount = 2;
        powerBar.numCapVertices = 0;
        powerBar.startWidth = powerBar.endWidth = powerBarHeight;
        powerBar.material = new Material(Shader.Find("Sprites/Default"));
        powerBar.sortingOrder = 50;
        powerBar.enabled = false;
    }

    void Update()
    {
        // If we lost the ball (e.g. it was stolen), our parent link is gone — clear
        // the stale holding flag before anything reads it, so we don't stay green/aiming.
        if (isHolding && ball != null && ball.transform.parent != transform)
            isHolding = false;

        // No ball → nothing can be charging.
        if (!isHolding) { chargeMode = Charging.None; currentPower = 0f; passPower = 0f; }

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
                // Start a charge on key-down only if nothing else is already charging.
                // Space is blocked while a steal consumed this press.
                if (chargeMode == Charging.None && !stealConsumedSpace && Input.GetKeyDown(KeyCode.Space))
                    chargeMode = Charging.Shoot;
                else if (chargeMode == Charging.None && Input.GetKeyDown(KeyCode.B))
                    chargeMode = Charging.Pass;

                if (chargeMode == Charging.Shoot)
                {
                    if (Input.GetKey(KeyCode.Space))
                        currentPower = Mathf.Min(currentPower + chargeRate * Time.deltaTime, maxShootPower);

                    if (Input.GetKeyUp(KeyCode.Space))
                    {
                        Shoot();
                        currentPower = 0f;
                        chargeMode = Charging.None;
                    }
                }
                else if (chargeMode == Charging.Pass)
                {
                    if (Input.GetKey(KeyCode.B))
                        passPower = Mathf.Min(passPower + passChargeRate * Time.deltaTime, 1f);

                    if (Input.GetKeyUp(KeyCode.B))
                    {
                        ChargedPass(passPower);
                        passPower = 0f;
                        chargeMode = Charging.None;
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
        UpdatePowerBar();
    }

    void FixedUpdate()
    {
        if (!IsActive) return;
        float speed = isHolding ? holdMoveSpeed : moveSpeed;
        rb.linearVelocity = input * speed;
    }

    // Short triangle that points along lastDirection, sitting just in front of the player.
    void UpdateAimLine()
    {
        if (aimLine == null) return;

        bool show = isHolding;
        aimLine.enabled = show;
        if (!show) return;

        Vector2 f = lastDirection.sqrMagnitude > 1e-4f ? lastDirection.normalized : Vector2.up;
        Vector2 perp = new Vector2(-f.y, f.x);
        Vector3 c = transform.position;

        Vector3 baseCenter = c + (Vector3)(f * aimTriangleGap);
        Vector3 tip   = baseCenter + (Vector3)(f * aimTriangleLength);
        Vector3 baseL = baseCenter + (Vector3)(perp * (aimTriangleWidth * 0.5f));
        Vector3 baseR = baseCenter - (Vector3)(perp * (aimTriangleWidth * 0.5f));

        aimLine.SetPosition(0, baseL);
        aimLine.SetPosition(1, tip);
        aimLine.SetPosition(2, baseR); // tail → tip → tail draws an open ">"
    }

    // Fills 0..1 while EITHER a shot (Space) or a pass (B) is charging; hidden otherwise.
    void UpdatePowerBar()
    {
        if (powerBar == null) return;

        bool charging = IsActive && isHolding && chargeMode != Charging.None;
        powerBar.enabled = charging;
        if (!charging) return;

        float fill = chargeMode == Charging.Shoot
            ? currentPower / Mathf.Max(maxShootPower, 0.0001f)
            : passPower;
        fill = Mathf.Clamp01(fill);
        float half = powerBarWidth * 0.5f;
        powerBar.SetPosition(0, new Vector3(-half, powerBarYOffset, 0f));
        powerBar.SetPosition(1, new Vector3(-half + powerBarWidth * fill, powerBarYOffset, 0f));

        Color col = Color.Lerp(Color.green, Color.red, fill);
        powerBar.startColor = powerBar.endColor = col;
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

        // Must approach the carrier from its front, not from behind.
        Vector2 carrierFacing = Vector2.zero;
        IAgentBody carrierBody = carrier.GetComponent<IAgentBody>();
        if (carrierBody != null) carrierFacing = carrierBody.LastDirection;
        else { PlayerMovement cpm = carrier.GetComponent<PlayerMovement>(); if (cpm != null) carrierFacing = cpm.Facing; }
        Vector2 dirToCarrier = (Vector2)carrier.position - (Vector2)transform.position;
        if (dirToCarrier.sqrMagnitude > 1e-4f) dirToCarrier.Normalize();
        if (carrierFacing.sqrMagnitude > 1e-4f &&
            Vector2.Dot(carrierFacing.normalized, -dirToCarrier) < StealFacingDot)
            return;

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

    // Charged pass: speed scales with charge (0..1) between min/max, aimed at the
    // teammate the player is facing.
    void ChargedPass(float charge)
    {
        if (ball == null || !isHolding) return;
        if (MatchContext.Instance == null) return;

        TeamSide myTeam = MatchContext.Instance.PlayerTeam;
        if (myTeam == null || myTeam.members == null) return;

        Transform target = FindPassTarget(myTeam);
        if (target == null) return;

        Vector2 dir = ((Vector2)target.position - (Vector2)transform.position).normalized;
        if (dir.sqrMagnitude > 1e-6f) lastDirection = dir;

        isHolding = false;
        ball.transform.SetParent(null);
        ball.simulated = true;
        float speed = Mathf.Clamp(Mathf.Lerp(minPassSpeed, maxPassSpeed, Mathf.Clamp01(charge)),
                                  minPassSpeed, maxPassSpeed);
        ball.linearVelocity = dir * speed;

        MatchContext.Instance.SetPossession(null);
    }

    // Teammate best aligned with our facing (within a forward cone); else the nearest,
    // so a pass never fails silently.
    Transform FindPassTarget(TeamSide myTeam)
    {
        Vector2 facing = lastDirection.sqrMagnitude > 1e-4f ? lastDirection.normalized : Vector2.up;
        Vector2 myPos = transform.position;

        Transform best = null;       // best within the forward cone
        float bestDot = PassConeDot; // require dot above the cone threshold
        Transform nearest = null;    // fallback
        float nearestDist = Mathf.Infinity;

        foreach (Transform m in myTeam.members)
        {
            if (m == null || m == transform) continue;
            Vector2 to = (Vector2)m.position - myPos;
            float dist = to.magnitude;
            if (dist < 1e-4f) continue;

            float dot = Vector2.Dot(facing, to / dist);
            if (dot > bestDot) { bestDot = dot; best = m; }
            if (dist < nearestDist) { nearestDist = dist; nearest = m; }
        }
        return best != null ? best : nearest;
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