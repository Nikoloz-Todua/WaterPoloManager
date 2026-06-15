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
    [Tooltip("Seconds of holding to reach a FULL-power shot (lower = snappier charge bar). Time-based so it stays fast no matter how high maxShootPower is.")]
    [SerializeField] private float shotChargeTime = 0.7f;
    [SerializeField] private float minShootSpeed = 8f;         // a quick tap still fires a real shot, never a limp drop
    [SerializeField] private float highShotSpeedBonus = 1.15f; // height > 0.7 → shot flies this much faster
    [SerializeField] private float skipShotHeight = 0.15f;     // Q+Space skip shot is locked to this LOW height

    [Header("Passing")]
    [SerializeField] private float passFactor = 2.5f; // (legacy; pass speed is charge-based now)
    [SerializeField] private float minPassSpeed = 6f;
    [SerializeField] private float maxPassSpeed = 13f;
    [Tooltip("Seconds of holding to reach a FULL-power pass (lower = snappier charge bar).")]
    [SerializeField] private float passChargeTime = 0.45f;
    [SerializeField] private float lobSpeedFactor = 0.7f; // F+B lob travels at this fraction of pass speed

    [Header("Pass aim (directional, FIFA-style)")]
    [Tooltip("How much the pass bends toward a teammate that lies along the aim. 0 = pure directional (goes exactly where you point), 1 = old auto-home onto the teammate.")]
    [SerializeField, Range(0f, 1f)] private float passAssist = 0.3f;
    [Tooltip("Only assist toward teammates within this distance.")]
    [SerializeField] private float passAssistRange = 8f;
    [Tooltip("A teammate must be within this cone of the aim (dot) to get any assist — aim away from everyone and the ball just goes there.")]
    [SerializeField, Range(0f, 1f)] private float passAssistMinDot = 0.5f;
    [Tooltip("1 = perfect aim. Lower it (or set per-player later) to add random spread for less-accurate passers.")]
    [SerializeField, Range(0f, 1f)] private float passAccuracy = 1f;
    [Tooltip("Max random spread (degrees) when passAccuracy is 0.")]
    [SerializeField] private float passInaccuracyDegrees = 12f;

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
    [Tooltip("World-unit width of the charge bar. Kept clearly longer than the keeper's 0.55u bar (>2x) so the player's shoot/pass charge reads at a glance. Grows left→right.")]
    [SerializeField] private float powerBarWidth = 1.2f;   // >2x the goalkeeper's hold bar (HudBarW = 0.55)
    [SerializeField] private float powerBarHeight = 0.07f; // matches the goalkeeper's hold bar (HudBarH)
    [SerializeField] private float powerBarYOffset = 0.9f;

    private LineRenderer powerBar;          // built in code, no Inspector wiring needed
    private LineRenderer powerBarBG;        // dark rounded track behind the fill
    private SpriteRenderer indicator;       // bouncing marker above the active player

    private const float IndicatorBaseY = 1.9f;   // rest height above the player center
    private const float IndicatorBobSpeed = 3f;  // sine frequency
    private const float IndicatorBobAmount = 0.12f;
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

    // ---- Stamina hooks (driven by StaminaSystem, if present) ----
    // NEUTRAL by default so the game plays identically with no StaminaSystem on the object;
    // StaminaSystem writes these each frame. Properties (not fields) → not serialized, no
    // Inspector clutter, and nothing here references the StaminaSystem type (it stays optional).
    public float StaminaSpeedMult { get; set; } = 1f;       // scales base swim speed
    public float StaminaSprintMult { get; set; } = 1f;      // scales the sprint multiplier
    public bool StaminaSprintBlocked { get; set; } = false; // true at 0% stamina → no sprint
    public float StaminaStealMult { get; set; } = 1f;       // scales steal success chance
    public float StaminaPercent01 { get; set; } = 1f;       // 0..1, mirrored for the touch HUD

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
        BuildIndicator();
    }

    // Bouncing sprite marker above the player's head — shown only while this player
    // is the human-controlled one (hidden from the start; Update toggles it).
    void BuildIndicator()
    {
        GameObject go = new GameObject("PlayerIndicator");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, IndicatorBaseY, 0f);

        indicator = go.AddComponent<SpriteRenderer>();
        indicator.sprite = Resources.Load<Sprite>("Sprites/indicator-triangle");
        indicator.sortingOrder = 60;
        indicator.enabled = false;

        if (indicator.sprite != null)
        {
            // scale the sprite to a 1.2 x 1.2 footprint regardless of its pixel size
            Vector2 s = indicator.sprite.bounds.size;
            go.transform.localScale = new Vector3(1.2f / s.x, 1.2f / s.y, 1f);
        }
        else
        {
            Debug.LogWarning("PlayerMovement: Sprites/indicator-triangle not found in a Resources folder.");
        }
    }

    // Create a self-contained power bar (a thick LineRenderer) above the player.
    // useWorldSpace=false → positions are local, so it follows the player automatically.
    void BuildPowerBar()
    {
        float half = powerBarWidth * 0.5f;

        // dark rounded track behind the fill, so even a low charge still reads as a bar
        GameObject bgGo = new GameObject("PowerBarBG");
        bgGo.transform.SetParent(transform, false);
        powerBarBG = bgGo.AddComponent<LineRenderer>();
        powerBarBG.useWorldSpace = false;
        powerBarBG.positionCount = 2;
        powerBarBG.numCapVertices = 8;                 // rounded ends
        powerBarBG.startWidth = powerBarBG.endWidth = powerBarHeight * 1.35f;
        powerBarBG.material = new Material(Shader.Find("Sprites/Default"));
        powerBarBG.sortingOrder = 49;
        powerBarBG.startColor = powerBarBG.endColor = new Color(0f, 0f, 0f, 0.55f);
        powerBarBG.SetPosition(0, new Vector3(-half, powerBarYOffset, 0f));
        powerBarBG.SetPosition(1, new Vector3( half, powerBarYOffset, 0f));
        powerBarBG.enabled = false;

        GameObject go = new GameObject("PowerBar");
        go.transform.SetParent(transform, false);
        powerBar = go.AddComponent<LineRenderer>();
        powerBar.useWorldSpace = false;
        powerBar.positionCount = 2;
        powerBar.numCapVertices = 8;                    // rounded fill
        powerBar.startWidth = powerBar.endWidth = powerBarHeight;
        powerBar.material = new Material(Shader.Find("Sprites/Default"));
        powerBar.sortingOrder = 50;
        powerBar.enabled = false;
    }

    void Update()
    {
        if (indicator != null)
        {
            indicator.enabled = IsActive;
            if (IsActive) // gentle bounce above the head while controlled
                indicator.transform.localPosition = new Vector3(
                    0f, IndicatorBaseY + Mathf.Sin(Time.time * IndicatorBobSpeed) * IndicatorBobAmount, 0f);
        }

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
                        currentPower = Mathf.Min(currentPower + (maxShootPower / Mathf.Max(shotChargeTime, 0.05f)) * Time.deltaTime, maxShootPower);
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

        // While our OWN keeper holds the ball, the human controls the KEEPER (Goalkeeper.cs,
        // Task 5) — stand this field swimmer down so WASD/Space/B don't drive two units at once.
        MatchContext kctx = MatchContext.Instance;
        if (kctx != null && kctx.KeeperHolding && kctx.KeeperHoldTeam == kctx.PlayerTeam)
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
                        currentPower = Mathf.Min(currentPower + (maxShootPower / Mathf.Max(shotChargeTime, 0.05f)) * Time.deltaTime, maxShootPower);
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
                        passPower = Mathf.Min(passPower + (1f / Mathf.Max(passChargeTime, 0.05f)) * Time.deltaTime, 1f);

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
        speed *= StaminaSpeedMult;                                          // tired = slower (stamina)
        // Shift sprint — disabled outright at 0% stamina ("normal swim only"), else the
        // sprint multiplier is scaled by stamina too (both move speed AND sprint cut when tired).
        if (sprintHeld && input != Vector2.zero && !StaminaSprintBlocked)
            speed *= sprintMultiplier * StaminaSprintMult;
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
        if (powerBarBG != null) powerBarBG.enabled = charging;
        if (!charging) return;

        float fill = chargeMode == Charging.Shoot
            ? currentPower / Mathf.Max(maxShootPower, 0.0001f)
            : passPower;
        fill = Mathf.Clamp01(fill);
        float half = powerBarWidth * 0.5f;
        powerBar.SetPosition(0, new Vector3(-half, powerBarYOffset, 0f));
        powerBar.SetPosition(1, new Vector3(-half + powerBarWidth * fill, powerBarYOffset, 0f));

        // green → yellow → red ramp (nicer than a flat green→red lerp)
        Color col = fill < 0.5f
            ? Color.Lerp(new Color(0.2f, 1f, 0.3f), new Color(1f, 0.9f, 0.2f), fill * 2f)
            : Color.Lerp(new Color(1f, 0.9f, 0.2f), new Color(1f, 0.25f, 0.2f), (fill - 0.5f) * 2f);
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
        if (ctx.IsProtectedKeeper(carrier)) // a keeper STILL in its safe zone can't be robbed (Task 5)
        {
            // trying inside the protect radius shoves us back out (FixedUpdate drives the push).
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

        if (Random.value <= stealChance * StaminaStealMult) // tired hands steal worse (stamina)
        {
            IAgentBody holder = carrier.GetComponent<IAgentBody>();
            if (holder != null) holder.IsHolding = false;
            else { Goalkeeper gkHeld = carrier.GetComponent<Goalkeeper>(); if (gkHeld != null) gkHeld.OnBallStolen(); } // strip a roaming keeper (Task 5)

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

    // Touch BLOCK button: a lower-RISK steal than the keyboard Space steal. Half the normal
    // success chance, and on a miss only a 50% chance of being whistled for a foul (a full
    // Space steal always fouls on a miss). Same cooldown / range / facing / keeper rules as
    // TrySteal. Keyboard steal (TrySteal) is intentionally left untouched.
    public void TouchBlockSteal()
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
        if (ctx.IsProtectedKeeper(carrier)) // a keeper STILL in its safe zone can't be robbed (Task 5)
        {
            // getting too close shoves us back out (same as TrySteal).
            Vector2 away = (Vector2)transform.position - (Vector2)carrier.position;
            if (away.magnitude < KeeperProtectRadius)
            {
                if (away.sqrMagnitude < 1e-4f) away = Vector2.down;
                keeperPushDir = away.normalized;
                keeperPushUntil = Time.time + KeeperPushSeconds;
            }
            return;
        }

        // Block reaches slightly further than the keyboard steal (matches the 1.5u defend
        // proximity in PlayerAnimator): the enemy carrier must be within 1.5 units.
        const float BlockStealRange = 1.5f;
        if (Vector2.Distance(transform.position, ball.position) > BlockStealRange) return;

        // In range = a real attempt → play the snatch animation now (success or not).
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

        if (Random.value <= stealChance * 0.5f * StaminaStealMult) // HALF success, and tired = worse
        {
            IAgentBody holder = carrier.GetComponent<IAgentBody>();
            if (holder != null) holder.IsHolding = false;
            else { Goalkeeper gkHeld = carrier.GetComponent<Goalkeeper>(); if (gkHeld != null) gkHeld.OnBallStolen(); } // strip a roaming keeper (Task 5)

            isHolding = true;
            ball.simulated = false;
            ball.linearVelocity = Vector2.zero;
            ball.transform.SetParent(transform);
            ball.transform.localPosition = (Vector3)(lastDirection * holdOffset);

            ctx.SetPossession(ctx.PlayerTeam);
        }
        else if (Random.value < 0.5f && ExclusionManager.Instance != null) // only HALF of misses foul
        {
            ExclusionManager.Instance.ReportFoul(transform, ctx.PlayerTeam, carrier);
        }
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

        float speed = Mathf.Max(currentPower, minShootSpeed); // a tap still fires a real shot, never a drop
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

    // DIRECTIONAL pass (FIFA-style): the ball goes where the player AIMS — lastDirection,
    // set by the joystick/WASD and shown by the facing triangle — NOT auto-homed onto a
    // teammate. A gentle assist (passAssist) bends the throw toward a teammate that lies
    // roughly along the aim; aim at empty water or the keeper and it goes exactly there.
    // passAccuracy can add spread. Speed scales with charge; a teammate must actually be in
    // the ball's path to receive it (so a stray pass can be intercepted or sail out).
    void ChargedPass(float charge)
    {
        if (ball == null || !isHolding) return;
        if (MatchContext.Instance == null) return;
        TeamSide myTeam = MatchContext.Instance.PlayerTeam;
        if (myTeam == null) return;

        Vector2 aimDir = lastDirection.sqrMagnitude > 1e-4f ? lastDirection.normalized : Vector2.up;

        // gentle assist toward a teammate that lies along the aim (none → pure directional)
        Vector2 fireDir = aimDir;
        Transform assist = FindPassAssistTarget(myTeam, aimDir);
        if (assist != null && passAssist > 0f)
        {
            Vector2 toMate = (Vector2)assist.position - (Vector2)transform.position;
            if (toMate.sqrMagnitude > 1e-6f)
                fireDir = Vector2.Lerp(aimDir, toMate.normalized, Mathf.Clamp01(passAssist)).normalized;
        }

        // imperfect passing: spread grows as accuracy drops (1 = perfect)
        if (passAccuracy < 1f)
        {
            float maxErr = passInaccuracyDegrees * (1f - Mathf.Clamp01(passAccuracy));
            fireDir = RotateVector(fireDir, Random.Range(-maxErr, maxErr));
        }

        lastDirection = fireDir;

        isHolding = false;
        ball.transform.SetParent(null);
        ball.simulated = true;
        float speed = Mathf.Clamp(Mathf.Lerp(minPassSpeed, maxPassSpeed, Mathf.Clamp01(charge)),
                                  minPassSpeed, maxPassSpeed);

        // F+B = HIGH LOB: slower flight, ball arcs overhead with a water shadow (BallFlight),
        // AI interception gated to a reduced roll. Otherwise a plain pass: no scaling/trail.
        bool lob = Input.GetKey(KeyCode.F);
        if (lob) speed *= lobSpeedFactor;
        ball.linearVelocity = fireDir * speed;
        shotHeight = lob ? 0.9f : 0.5f; // a pass overwrites LastReleaser → keep its height honest

        if (BallFlight.Instance != null)
        {
            if (lob)
            {
                float dist = assist != null ? Vector2.Distance(transform.position, assist.position) : 5f;
                BallFlight.Instance.NoteLob(myTeam, dist, speed);
            }
            else BallFlight.Instance.NotePass(); // plain pass → no swell, no trail "bridge"
        }

        MatchContext.Instance.NoteRelease(transform);
        MatchContext.Instance.SetPossession(null);
    }

    // The teammate to lightly assist the pass toward: best-aligned with the aim direction,
    // within passAssistRange and inside the aim cone (passAssistMinDot). Returns null when
    // nothing lies along the aim, so the pass flies exactly where the player points.
    Transform FindPassAssistTarget(TeamSide myTeam, Vector2 aimDir)
    {
        if (myTeam == null || myTeam.members == null) return null;
        Vector2 myPos = transform.position;
        Transform best = null;
        float bestScore = float.NegativeInfinity;

        foreach (Transform m in myTeam.members)
        {
            if (m == null || m == transform) continue;
            Vector2 to = (Vector2)m.position - myPos;
            float dist = to.magnitude;
            if (dist < 1e-4f || dist > passAssistRange) continue;

            float dot = Vector2.Dot(aimDir, to / dist);
            if (dot < passAssistMinDot) continue; // not along the aim → no assist toward it
            float score = dot - dist * 0.1f;       // prefer aligned + nearer
            if (score > bestScore) { bestScore = score; best = m; }
        }
        return best;
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