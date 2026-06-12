using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float holdMoveSpeed = 2f;
    [SerializeField] private float sprintMultiplier = 2f; // LeftShift speed boost while moving

    [Header("Ball")]
    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private float grabDistance = 1.0f;
    [SerializeField] private float holdOffset = 0.6f;

    [Header("Shooting")]
    [SerializeField] private float maxShootPower = 12f;
    [SerializeField] private float chargeRate = 8f;
    [SerializeField] private float highShotSpeedBonus = 1.15f; // height > 0.7 → shot flies this much faster
    [SerializeField] private float skipShotHeight = 0.15f;     // Q+Space skip shot is locked to this LOW height

    [Header("Passing")]
    [SerializeField] private float passFactor = 2.5f; // (legacy; pass speed is charge-based now)
    [SerializeField] private float minPassSpeed = 6f;
    [SerializeField] private float maxPassSpeed = 13f;
    [SerializeField] private float passChargeRate = 1.5f; // pass charge gained per second (0..1)
    [SerializeField] private float lobSpeedFactor = 0.7f; // F+B lob travels at this fraction of pass speed
    private const float PassConeDot = 0.3f;               // teammate must be within this forward cone

    [Header("Stealing")]
    [SerializeField] private float stealDistance = 1.2f;
    [SerializeField] private float stealChance = 0.4f;
    [SerializeField] private float stealCooldown = 0.6f;
    private const float StealFacingDot = 0.3f; // stealer must be within ~70° of the carrier's front

    [Header("Aim line")]
    [SerializeField] private LineRenderer aimLine;
    [SerializeField] private float aimLineLength = 2.5f; // (legacy; triangle uses the fields below)

    [Header("Aim triangle")]
    [SerializeField] private float aimTriangleLength = 0.4f; // tip distance from the base
    [SerializeField] private float aimTriangleWidth = 0.3f;  // base width
    [SerializeField] private float aimTriangleGap = 0.5f;    // gap from player centre to base
    [SerializeField] private float aimTriangleLineWidth = 0.05f;

    [Header("Power bar")]
    [SerializeField] private float powerBarWidth = 1.0f;
    [SerializeField] private float powerBarHeight = 0.12f;
    [SerializeField] private float powerBarYOffset = 0.9f;

    private LineRenderer powerBar;          // built in code, no Inspector wiring needed
    private GameObject selectionTriangle;   // FIFA-style marker above the active player

    private const float SelectionTriangleSize = 0.15f;
    private const float KeeperProtectRadius = 2.5f; // can't crowd a ball-holding keeper
    private const float KeeperPushSeconds = 0.25f;  // how long the shove-back drives us

    private Vector2 keeperPushDir;
    private float keeperPushUntil = -10f;

    public bool IsActive = false;
    public bool IsHolding => isHolding;
    public Vector2 Facing => lastDirection;

    // 0..1, charged in lock-step with shot power (0–0.3 low, 0.3–0.7 mid, 0.7–1 high).
    // Keeps the LAST shot's value through its flight — Goalkeeper/GoalkeeperAnimator
    // read it via MatchContext.LastReleaser to pick the dive tier.
    public float ShotHeight => shotHeight;

    // True while the carrier sprints WITH the ball (Shift held + holding): the hold is
    // "loose" — opponents get 2x steal range and a success bonus (read by WaterPoloBrain).
    // The ball is NOT dropped. Resets the moment Shift is released.
    public bool IsLooseHold { get; private set; }

    // Raw Shift state, honoured only on the human-controlled player (PlayerAnimator reads this).
    public bool SprintHeld => IsActive && sprintHeld;

    private Rigidbody2D rb;
    private Vector2 input;
    private Vector2 lastDirection = Vector2.up;
    private float currentPower = 0f;        // shoot charge (0..maxShootPower)
    private float shotHeight = 0.5f;        // see ShotHeight
    private bool skipCharge = false;        // Q held during the shot charge → skip shot
    private float passPower = 0f;           // pass charge (0..1)
    private bool isHolding = false;
    private float lastStealTime = -10f;
    private bool stealConsumedSpace = false;
    private bool sprintHeld = false;
    private PlayerAnimator playerAnimator; // optional; fires the steal animation on attempts

    // --- Touch input (written by TouchControls.SetTouchInput every frame; each field is
    // merged into its matching keyboard check with || so keyboard keeps working as-is) ---
    private Vector2 touchAxis;
    private bool touchShootHeld;
    private bool touchShootDown;
    private bool touchShootUp;
    private bool touchPassHeld;
    private bool touchPassDown;
    private bool touchPassUp;
    private bool touchSprintHeld;
    private bool touchSwitchDown;

    // TeamManager merges this with the C key for manual player switching.
    public bool TouchSwitchDown => touchSwitchDown;

    // True while this player is serving (or permanently out of) an exclusion → inert.
    private bool Excluded => ExclusionManager.Instance != null && ExclusionManager.Instance.IsExcluded(transform);

    // Only one action charges at a time; whichever key was pressed first wins until released.
    private enum Charging { None, Shoot, Pass }
    private Charging chargeMode = Charging.None;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerAnimator = GetComponent<PlayerAnimator>();

        // flight effects (skip bounce, lob shadow) live on the Ball — first Awake adds them
        if (ball != null && ball.GetComponent<BallFlight>() == null)
            ball.gameObject.AddComponent<BallFlight>();

        // Configure the existing LineRenderer to draw a soft chevron.
        if (aimLine != null)
        {
            aimLine.useWorldSpace = true;
            aimLine.positionCount = 3;
            aimLine.loop = false; // open chevron ">" — no base line between the tails
            aimLine.startWidth = aimLine.endWidth = aimTriangleLineWidth;
            aimLine.startColor = aimLine.endColor = new Color(1f, 1f, 1f, 0.6f);
            aimLine.enabled = false;
        }

        BuildPowerBar();
        BuildSelectionTriangle();
    }

    // Small white equilateral triangle hovering above the player's head, pointing
    // down at it — shown only while this player is the human-controlled one.
    void BuildSelectionTriangle()
    {
        selectionTriangle = new GameObject("SelectionTriangle");
        selectionTriangle.transform.SetParent(transform, false);
        selectionTriangle.transform.localPosition = new Vector3(0f, 0.6f, 0f);

        float s = SelectionTriangleSize;
        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[]
        {
            new Vector3(-s, s * 0.866f, 0f), // top left
            new Vector3( s, s * 0.866f, 0f), // top right
            new Vector3(0f, -s * 0.866f, 0f) // bottom tip (points down at the player)
        };
        mesh.triangles = new int[] { 0, 1, 2 };

        selectionTriangle.AddComponent<MeshFilter>().mesh = mesh;
        MeshRenderer mr = selectionTriangle.AddComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Sprites/Default")) { color = Color.white };
        mr.sortingOrder = 50;

        selectionTriangle.SetActive(false);
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
        if (selectionTriangle != null && selectionTriangle.activeSelf != IsActive)
            selectionTriangle.SetActive(IsActive);

        // Stale touch state must never drive a player the human isn't controlling.
        if (!IsActive) ClearTouchInput();

        // Sprint input only counts on the human-controlled player; the frozen /
        // excluded branches below force it back off before they return.
        sprintHeld = IsActive && (Input.GetKey(KeyCode.LeftShift) || touchSprintHeld);

        // If we lost the ball (e.g. it was stolen), our parent link is gone — clear
        // the stale holding flag before anything reads it, so we don't stay green/aiming.
        if (isHolding && ball != null && ball.transform.parent != transform)
            isHolding = false;

        // Play frozen (sprint duel / goal settle / penalty) → no control, charge, steal, or aim
        // — EXCEPT the active penalty shooter, who may only charge & shoot (Space), no moving.
        if (MatchContext.Instance != null && MatchContext.Instance.PlayFrozen)
        {
            sprintHeld = false; IsLooseHold = false; // no sprinting while play is frozen

            bool penaltyShooter = PenaltyManager.Instance != null &&
                                  PenaltyManager.Instance.IsActiveShooter(transform);
            if (penaltyShooter && isHolding)
            {
                // AIM with movement keys: rotate the shot within a cone toward the goal
                // (never move the body — position stays on the spot).
                Vector2 goalDir = PenaltyManager.Instance.ShooterGoalDir();
                Vector2 aimIn = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")) + touchAxis;
                if (aimIn.sqrMagnitude > 0.01f && goalDir.sqrMagnitude > 1e-4f)
                {
                    float cone = PenaltyManager.Instance.AimCone;
                    float ang = Mathf.Clamp(Vector2.SignedAngle(goalDir, aimIn.normalized), -cone, cone);
                    lastDirection = RotateVector(goalDir.normalized, ang);
                }

                input = Vector2.zero; // planted on the penalty spot — aiming only
                if (chargeMode == Charging.None && (Input.GetKeyDown(KeyCode.Space) || touchShootDown))
                { chargeMode = Charging.Shoot; skipCharge = Input.GetKey(KeyCode.Q); }
                if (chargeMode == Charging.Shoot)
                {
                    if (Input.GetKey(KeyCode.Space) || touchShootHeld)
                    {
                        currentPower = Mathf.Min(currentPower + chargeRate * Time.deltaTime, maxShootPower);
                        ChargeHeight();
                    }
                    if (Input.GetKeyUp(KeyCode.Space) || touchShootUp)
                    {
                        Shoot();
                        currentPower = 0f;
                        chargeMode = Charging.None;
                    }
                }
                UpdateAimLine();
                UpdatePowerBar();
                return;
            }

            input = Vector2.zero;
            chargeMode = Charging.None; currentPower = 0f; passPower = 0f;
            if (aimLine != null) aimLine.enabled = false;
            if (powerBar != null) powerBar.enabled = false;
            return;
        }

        // Excluded → fully inert: no control, charge, steal, or aim visuals.
        if (Excluded)
        {
            sprintHeld = false; IsLooseHold = false;
            input = Vector2.zero;
            chargeMode = Charging.None; currentPower = 0f; passPower = 0f;
            if (aimLine != null) aimLine.enabled = false;
            if (powerBar != null) powerBar.enabled = false;
            return;
        }

        // No ball → nothing can be charging.
        if (!isHolding) { chargeMode = Charging.None; currentPower = 0f; passPower = 0f; }

        if (IsActive)
        {
            float x = Input.GetAxisRaw("Horizontal");
            float y = Input.GetAxisRaw("Vertical");
            input = new Vector2(x, y).normalized + touchAxis; // analog joystick adds in
            if (input.sqrMagnitude > 1f) input = input.normalized;

            if (input != Vector2.zero)
                lastDirection = input;

            // A human carrier isn't forced to kickoff-pass: their first move voids the
            // pending flag (shooting/passing already clears it via the possession change).
            if (isHolding && input != Vector2.zero && MatchContext.Instance != null &&
                MatchContext.Instance.KickoffPassPending &&
                MatchContext.Instance.KickoffPassTeam == MatchContext.Instance.PlayerTeam)
                MatchContext.Instance.ClearKickoffPass();

            // A human free-throw carrier resumes free play the moment they move.
            if (isHolding && input != Vector2.zero && MatchContext.Instance != null &&
                MatchContext.Instance.FreeThrowActive &&
                MatchContext.Instance.FreeThrowCarrier == transform)
                MatchContext.Instance.ClearFreeThrow();

            if (Input.GetKeyDown(KeyCode.E))
            {
                if (isHolding) DropBall();
                else TryGrabBall();
            }

            // Space with no ball = attempt steal. If it succeeds, consume this press
            // so releasing Space doesn't instantly fire a shot.
            if (!isHolding && (Input.GetKeyDown(KeyCode.Space) || touchShootDown))
            {
                TrySteal();
                if (isHolding) stealConsumedSpace = true;
            }

            if (isHolding)
            {
                // Start a charge on key-down only if nothing else is already charging.
                // Space is blocked while a steal consumed this press.
                if (chargeMode == Charging.None && !stealConsumedSpace && (Input.GetKeyDown(KeyCode.Space) || touchShootDown))
                { chargeMode = Charging.Shoot; skipCharge = Input.GetKey(KeyCode.Q); }
                else if (chargeMode == Charging.None && (Input.GetKeyDown(KeyCode.B) || touchPassDown))
                    chargeMode = Charging.Pass;

                if (chargeMode == Charging.Shoot)
                {
                    if (Input.GetKey(KeyCode.Space) || touchShootHeld)
                    {
                        currentPower = Mathf.Min(currentPower + chargeRate * Time.deltaTime, maxShootPower);
                        ChargeHeight();
                    }

                    if (Input.GetKeyUp(KeyCode.Space) || touchShootUp)
                    {
                        Shoot();
                        currentPower = 0f;
                        chargeMode = Charging.None;
                    }
                }
                else if (chargeMode == Charging.Pass)
                {
                    if (Input.GetKey(KeyCode.B) || touchPassHeld)
                        passPower = Mathf.Min(passPower + passChargeRate * Time.deltaTime, 1f);

                    if (Input.GetKeyUp(KeyCode.B) || touchPassUp)
                    {
                        ChargedPass(passPower);
                        passPower = 0f;
                        chargeMode = Charging.None;
                    }
                }
            }

            // once the steal press is released, Space goes back to being shoot
            if (Input.GetKeyUp(KeyCode.Space) || touchShootUp)
                stealConsumedSpace = false;
        }
        else
        {
            input = Vector2.zero;
        }

        // Re-derive AFTER input handling so a grab/steal/shoot this frame is reflected.
        IsLooseHold = sprintHeld && isHolding;

        UpdateAimLine();
        UpdatePowerBar();
    }

    void FixedUpdate()
    {
        if (rb == null) return; // no body → nothing to drive (defensive)
        if (MatchContext.Instance != null && MatchContext.Instance.PlayFrozen)
        { rb.linearVelocity = Vector2.zero; return; } // frozen during duel / goal settle
        if (Excluded) { rb.linearVelocity = Vector2.zero; return; } // frozen in the corner
        if (!IsActive) return;
        if (Time.time < keeperPushUntil) // shoved off a ball-holding keeper
        {
            rb.linearVelocity = keeperPushDir * moveSpeed;
            return;
        }
        float speed = isHolding ? holdMoveSpeed : moveSpeed;
        if (sprintHeld && input != Vector2.zero) speed *= sprintMultiplier; // Shift sprint
        rb.linearVelocity = input * speed;
        if (MatchContext.Instance != null)
            WaterPoloBrain.ClampX(rb, MatchContext.Instance.PlayerLimitX); // can't cross the goal line
    }

    // Soft chevron that points along lastDirection, sitting just in front of the player.
    void UpdateAimLine()
    {
        if (aimLine == null) return;

        bool show = IsActive && isHolding; // only the human-controlled player aims
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
        MatchContext ctx = MatchContext.Instance;
        // loose + past cooldown + not under a shot-clock turnover ban
        if (ctx != null && (!ctx.BallGrabbable || !ctx.CanGrab(ctx.PlayerTeam))) return;

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
        if (ctx.FreeThrowActive) return; // no steals during a free throw

        TeamSide enemy = ctx.EnemyOf(ctx.PlayerTeam);
        if (enemy == null || !ctx.TeamHasBall(enemy)) return;

        Transform carrier = ball.transform.parent;
        if (carrier == null) return;
        if (carrier.GetComponent<Goalkeeper>() != null)
        {
            // can't steal from a keeper — and trying inside the protect radius shoves
            // us back out (FixedUpdate drives the push for KeeperPushSeconds).
            Vector2 away = (Vector2)transform.position - (Vector2)carrier.position;
            if (away.magnitude < KeeperProtectRadius)
            {
                if (away.sqrMagnitude < 1e-4f) away = Vector2.down;
                keeperPushDir = away.normalized;
                keeperPushUntil = Time.time + KeeperPushSeconds;
            }
            return;
        }

        if (Vector2.Distance(transform.position, ball.position) > stealDistance) return;

        // In range = a real attempt: play the snatch animation NOW, before the facing
        // gate or the dice roll, so EVERY attempt is visible (success or not).
        if (playerAnimator != null) playerAnimator.TriggerSteal();

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
        else if (ExclusionManager.Instance != null)
        {
            // failed steal = ordinary foul: carrier keeps the ball, we get locked out
            ExclusionManager.Instance.ReportFoul(transform, ctx.PlayerTeam, carrier);
        }
    }

    // Block this player's steal for `seconds` (called by ExclusionManager after a foul).
    public void ApplyStealLockout(float seconds)
    {
        lastStealTime = Time.time + Mathf.Max(0f, seconds - stealCooldown);
    }

    // Called by TouchControls every frame on the active player. SHOOT maps to Space
    // (so it also steals when not holding), PASS to B, SPRINT to LeftShift, SWITCH to C
    // (consumed by TeamManager via TouchSwitchDown).
    public void SetTouchInput(Vector2 axis, bool shootHeld, bool shootDown, bool shootUp,
                              bool passHeld, bool passDown, bool passUp,
                              bool sprintHeld, bool switchDown)
    {
        touchAxis = axis;
        touchShootHeld = shootHeld;
        touchShootDown = shootDown;
        touchShootUp = shootUp;
        touchPassHeld = passHeld;
        touchPassDown = passDown;
        touchPassUp = passUp;
        touchSprintHeld = sprintHeld;
        touchSwitchDown = switchDown;
    }

    private void ClearTouchInput()
    {
        touchAxis = Vector2.zero;
        touchShootHeld = touchShootDown = touchShootUp = false;
        touchPassHeld = touchPassDown = touchPassUp = false;
        touchSprintHeld = false;
        touchSwitchDown = false;
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

    // Height charges in lock-step with power: the same hold that fills the bar raises
    // the shot from low (0–0.3) through mid to high (0.7–1). Q at any point during
    // the charge turns it into a skip shot.
    void ChargeHeight()
    {
        shotHeight = maxShootPower > 0f ? currentPower / maxShootPower : 0.5f;
        if (Input.GetKey(KeyCode.Q)) skipCharge = true;
    }

    void Shoot()
    {
        if (ball == null) return;
        isHolding = false;
        ball.transform.SetParent(null);
        ball.simulated = true;

        bool skip = skipCharge;
        skipCharge = false;
        if (skip) shotHeight = skipShotHeight; // a skip shot is fast and LOW by definition

        float speed = currentPower;
        if (!skip && shotHeight > 0.7f) speed *= highShotSpeedBonus; // high shots fly faster
        ball.linearVelocity = lastDirection * speed;

        if (BallFlight.Instance != null)
            BallFlight.Instance.NoteShot(shotHeight, skip); // arms the bounce for a skip

        if (MatchContext.Instance != null)
        {
            MatchContext.Instance.NoteRelease(transform); // remember the shooter (Centre-goal tracking)
            MatchContext.Instance.SetPossession(null);
        }
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

        // F+B = HIGH LOB: slower flight, ball arcs overhead with a water shadow
        // (BallFlight), and AI interception is gated to a reduced roll (WaterPoloBrain).
        bool lob = Input.GetKey(KeyCode.F);
        if (lob) speed *= lobSpeedFactor;
        ball.linearVelocity = dir * speed;
        shotHeight = lob ? 0.9f : 0.5f; // a pass overwrites LastReleaser → keep its height honest
        if (lob && BallFlight.Instance != null)
        {
            float dist = Vector2.Distance(transform.position, target.position);
            BallFlight.Instance.NoteLob(MatchContext.Instance.PlayerTeam, dist, speed);
        }

        MatchContext.Instance.NoteRelease(transform);
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

    // Point the aim/facing at a world direction (used by PenaltyManager to face the goal).
    public void SetFacing(Vector2 dir)
    {
        if (dir.sqrMagnitude > 1e-6f) lastDirection = dir.normalized;
    }

    // Rotate a 2D vector by `degrees` (CCW), used for the penalty aim cone.
    static Vector2 RotateVector(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
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