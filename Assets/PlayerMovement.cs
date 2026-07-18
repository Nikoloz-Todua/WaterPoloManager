using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float holdMoveSpeed = 2f;

    [Header("Sprint (hold)")]
    [Tooltip("Speed multiplier while LEFT SHIFT / the Sprint button is HELD and moving (regular play). The sprint duel uses its own tap mechanic, not this.")]
    [SerializeField] private float sprintMultiplier = 2f;

    [Header("Ball")]
    [SerializeField] private Rigidbody2D ball;
    [Tooltip("How close (centre-to-centre) the player must be to collect a loose ball. Kept generous so colliders pushing the ball away can't make your own loose ball unreachable.")]
    [SerializeField] private float grabDistance = 1f; // aligned to the scene's live value (was 1.6 in code only — the scene always said 1)
    [SerializeField] private float holdOffset = 0.6f;
    [Tooltip("Anchor the held ball snaps to on pickup. Leave empty to parent to this player's root.")]
    [SerializeField] private Transform handPosition;

    [Header("Held-ball hand position (units from player centre; tune to your sprite art)")]
    [Tooltip("Ball position when facing RIGHT — the lead hand.")]
    [SerializeField] private Vector2 handOffsetRight = new Vector2(0.5f, 0.15f);
    [Tooltip("Ball position when facing LEFT — independent of Right (arms aren't symmetrical).")]
    [SerializeField] private Vector2 handOffsetLeft = new Vector2(-0.2f, 0.35f);
    [Tooltip("Ball position in BACK view swimming away to the RIGHT (swim-backr) — upper-arm area.")]
    [SerializeField] private Vector2 handOffsetUp = new Vector2(0.1f, 0.5f);
    [Tooltip("Ball position in BACK view swimming away to the LEFT (swim-backl) — independent of Up (frames differ).")]
    [SerializeField] private Vector2 handOffsetUpLeft = new Vector2(-0.1f, 0.5f);
    [Tooltip("Ball position when facing DOWN / idle — the front sprite's resting hand.")]
    [SerializeField] private Vector2 handOffsetDown = new Vector2(0.28f, -0.05f);
    [Tooltip("While carrying and moving, the ball is pushed this far ahead along the swimmer's actual travel direction. Presentation only: pass/shot aim still uses lastDirection exactly as before.")]
    [SerializeField] private float movingHoldBallForwardOffset = 0.58f;
    [Tooltip("Small in/out distance change while swimming with the ball, to suggest repeated hand pushes through the water.")]
    [SerializeField, Range(0f, 0.2f)] private float movingHoldBallPushAmplitude = 0.06f;
    [Tooltip("How many gentle held-ball push cycles play per second while swimming.")]
    [SerializeField, Range(0.1f, 5f)] private float movingHoldBallPushCyclesPerSecond = 1.7f;
    [Tooltip("Maximum clockwise/counter-clockwise rocking of the held ball while swimming.")]
    [SerializeField, Range(0f, 30f)] private float movingHoldBallRockDegrees = 7f;
    [SerializeField] private Vector2 handOffsetRightSwapped;
    [SerializeField] private Vector2 handOffsetLeftSwapped;
    [SerializeField] private Vector2 handOffsetUpSwapped;
    [SerializeField] private Vector2 handOffsetUpLeftSwapped;
    [SerializeField] private Vector2 handOffsetDownSwapped;
    // Retained for the legacy stationary hand-offset selector below. Moving carriers now bypass all
    // art-specific hand offsets and place the ball directly along their travel direction.
    private const float BackFacingThreshold = 0.3f;
    private const float MovingHoldSpeedThreshold = 0.1f;

    [Header("Shooting")]
    [SerializeField] private float maxShootPower = 12f;
    [Tooltip("Seconds of holding to reach a FULL-power shot (lower = snappier charge bar). Time-based so it stays fast no matter how high maxShootPower is.")]
    [SerializeField] private float shotChargeTime = 0.7f;
    [SerializeField] private float minShootSpeed = 8f;         // a quick tap still fires a real shot, never a limp drop
    [SerializeField] private float highShotSpeedBonus = 1.15f; // height > 0.7 → shot flies this much faster
    [SerializeField] private float skipShotHeight = 0.15f;     // Q+Space skip shot is locked to this LOW height

    [Header("Passing")]
    [SerializeField] private float passFactor = 2.5f; // (legacy; pass speed is charge-based now)
    [Tooltip("Even an untimed tap-pass has enough pace to leave the hand; distance is controlled separately by charge.")]
    [SerializeField] private float minPassSpeed = 6f;  // aligned to the live scene
    [SerializeField] private float maxPassSpeed = 13f; // aligned to the live scene
    // A pass computed slower than this is a dud (would just plop at the player's feet) → refuse to
    // release the ball at all, so a press never "drops" the ball instead of throwing it.
    private const float MinPassReleaseSpeed = 4f;

    // ---- HIGH BALL (the arcing, untouchable release — BallFlight.LaunchHighBall) ----
    private const float HighShotGoalLineX = 7f;      // matches GoalLineOut / BallFlight.GoalLineX
    private const float HighShotLandShort = 1.5f;    // a high shot lands this far before the line
    private const float HighShotClearDistance = 6f;  // arc length for a near-vertical high shot
    private const float HighLobRangeMin = 2.5f;      // even a tapped lob is longer than a tap pass
    private const float HighLobRangeMax = 9f;
    private const float PassArcRangeMin = 1.5f;      // a tap genuinely falls short
    private const float PassArcRangeMax = 7f;        // a full charge still reaches across shape
    private const float PassDistancePower = 1.5f;    // convex curve: separates low/mid/full charge

    // Shots must read faster than passes (human pass tops out at maxPassSpeed 16 — ABOVE the
    // serialized maxShootPower 12). Applied in code so no Inspector value needs re-tuning:
    // full-charge shot = 12 × 1.35 = 16.2, high shot ≈ 18.6, tap floor = 8 × 1.35 = 10.8.
    private const float ShotSpeedMult = 1.35f;
    [Tooltip("Seconds of holding to reach a FULL-power pass (lower = snappier charge bar).")]
    [SerializeField] private float passChargeTime = 0.45f;
    [SerializeField] private float lobSpeedFactor = 0.7f; // F+B lob travels at this fraction of pass speed

    [Header("Stealing")]
    // Measured carrier-centre to stealer-centre so human and AI reach match and neither is
    // affected by which side of the carrier currently holds the ball.
    [SerializeField] private float stealDistance = 1f;
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
    public bool IsMovingWithBall => isHolding && rb != null &&
                                    rb.linearVelocity.sqrMagnitude > MovingHoldSpeedThreshold * MovingHoldSpeedThreshold;
    public Vector2 Facing => lastDirection;

    // 0..1, charged in lock-step with shot power (0–0.3 low, 0.3–0.7 mid, 0.7–1 high).
    // Keeps the LAST shot's value through its flight — Goalkeeper/GoalkeeperAnimator
    // read it via MatchContext.LastReleaser to pick the dive tier.
    public float ShotHeight => shotHeight;

    // True while the carrier sprints WITH the ball (Shift/Sprint held + holding): the hold is
    // "loose" — opponents get 2x steal range and a success bonus (read by WaterPoloBrain). The
    // ball is NOT dropped. Clears the moment sprint is released.
    public bool IsLooseHold { get; private set; }

    // Raw HOLD-sprint state, honoured only on the human-controlled player. Regular play is
    // hold-to-sprint (the sprint duel uses its own tap mechanic in SprintDuel).
    public bool SprintHeld => IsActive && sprintHeld;

    // 0/1 proxy of the hold-sprint state, kept so the camera zoom / animator / stamina drain /
    // teammate-hustle AI (which read "SprintCharge") keep working unchanged: 1 = sprinting.
    public float SprintCharge => sprintHeld ? 1f : 0f;

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
    private bool sprintHeld = false;        // LEFT SHIFT / Sprint button held this frame (active player only)
    private PlayerAnimator playerAnimator; // optional; fires the steal animation on attempts
    private float heldBallMotionPhase;

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
    private bool touchLobHeld;   // F-equivalent for touch: makes the next pass fire as the big LOB (Task 4)

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
        heldBallMotionPhase = Random.Range(0f, Mathf.PI * 2f);

        // flight effects (skip bounce, high-ball arc + shadow) live on the Ball — first Awake adds them
        if (ball != null && ball.GetComponent<BallFlight>() == null)
            ball.gameObject.AddComponent<BallFlight>();

        // The aim chevron is the "AimLine" child (a LineRenderer). If the Inspector slot is empty,
        // grab it by name so we ALWAYS control its visibility — an unwired slot used to make
        // UpdateAimLine bail early, leaving the chevron stuck visible on every player.
        if (aimLine == null)
        {
            Transform t = transform.Find("AimLine");
            if (t != null) aimLine = t.GetComponent<LineRenderer>();
        }

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

        // --- HOLD-to-sprint (regular play): LEFT SHIFT / the Sprint button held while we're the
        //     controlled player in LIVE play. Forced off while frozen / excluded / controlling the
        //     keeper, so it can't linger into those states. (The sprint duel uses its own tap
        //     mechanic in SprintDuel — this is only regular gameplay.) ---
        MatchContext sctx = MatchContext.Instance;
        bool sprintFrozen = sctx != null && sctx.PlayFrozen;
        bool keeperHasHuman = sctx != null && sctx.KeeperHolding && sctx.KeeperHoldTeam == sctx.PlayerTeam;
        sprintHeld = IsActive && !sprintFrozen && !Excluded && !keeperHasHuman &&
                     (Input.GetKey(KeyCode.LeftShift) || touchSprintHeld);

        // If we lost the ball (e.g. it was stolen), it's no longer parented under us — clear the
        // stale holding flag before anything reads it, so we don't stay green/aiming. Use IsChildOf
        // (not == transform) so a ball parented to our handPosition anchor still counts as held —
        // otherwise wiring handPosition silently drops the hold and the LateUpdate hand-offset block
        // (which only runs while isHolding) never fires, freezing the ball at the anchor.
        if (isHolding && ball != null && !ball.transform.IsChildOf(transform))
            isHolding = false;

        // Play frozen (sprint duel / goal settle / penalty) → no control, charge, steal, or aim
        // — EXCEPT the active penalty shooter, who may only charge & shoot (Space), no moving.
        if (MatchContext.Instance != null && MatchContext.Instance.PlayFrozen)
        {
            IsLooseHold = false; // no sprinting while play is frozen

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
            IsLooseHold = false;
            input = Vector2.zero;
            chargeMode = Charging.None; currentPower = 0f; passPower = 0f;
            if (aimLine != null) aimLine.enabled = false;
            if (powerBar != null) powerBar.enabled = false;
            return;
        }

        if (FoulStun.IsStunned(transform))
        {
            IsLooseHold = false;
            input = Vector2.zero;
            chargeMode = Charging.None; currentPower = 0f; passPower = 0f;
            if (aimLine != null) aimLine.enabled = false;
            if (powerBar != null) powerBar.enabled = false;
            if (powerBarBG != null) powerBarBG.enabled = false;
            return;
        }

        // While our OWN keeper holds the ball, the human controls the KEEPER (Goalkeeper.cs,
        // Task 5) — stand this field swimmer down so WASD/Space/B don't drive two units at once.
        MatchContext kctx = MatchContext.Instance;
        if (kctx != null && kctx.KeeperHolding && kctx.KeeperHoldTeam == kctx.PlayerTeam)
        {
            IsLooseHold = false;
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

            // Face the way the player is AIMING (raw input) the instant a key/stick is pressed —
            // even while standing still or barely moving — so the held-ball hand offset snaps to the
            // correct facing immediately. Only with no input do we fall back to the travel direction
            // (velocity), so drift / knockback still faces sensibly.
            if (input.magnitude > 0.1f)
                lastDirection = input;
            else if (rb != null && rb.linearVelocity.magnitude > 0.1f)
                lastDirection = rb.linearVelocity.normalized;

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

            // AUTO-COLLECT: a loose, grabbable ball within reach is picked up automatically — no E
            // press needed. Guarantees you can always reclaim your own bad pass/drop just by swimming
            // back to it (TryGrabBall still enforces the loose + cooldown + grab-ban gates).
            if (!isHolding) TryGrabBall();

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
        IsLooseHold = IsActive && isHolding && sprintHeld;

        UpdateAimLine();
        UpdatePowerBar();
    }

    void FixedUpdate()
    {
        if (rb == null) return; // no body → nothing to drive (defensive)
        if (MatchContext.Instance != null && MatchContext.Instance.PlayFrozen)
        { rb.linearVelocity = Vector2.zero; return; } // frozen during duel / goal settle
        if (Excluded) { rb.linearVelocity = Vector2.zero; return; } // frozen in the corner
        if (FoulStun.IsStunned(transform)) { rb.linearVelocity = Vector2.zero; return; }
        if (!IsActive) return;
        if (Time.time < keeperPushUntil) // shoved off a ball-holding keeper
        {
            rb.linearVelocity = keeperPushDir * moveSpeed;
            return;
        }
        // Clearance rule: a ball-holding ENEMY keeper gets working room. The old push fired only
        // on steal ATTEMPTS, so simply STANDING on the keeper jammed its outlet pass forever
        // (pass → deflects off the crowder → slow ball → keeper re-collects → repeat). Now the
        // active player is walked straight back out of the protect radius whenever the enemy
        // keeper is protected — matching what the AI swimmers already do.
        MatchContext kctx = MatchContext.Instance;
        if (kctx != null && kctx.KeeperHolding && kctx.KeeperHoldTeam != kctx.PlayerTeam &&
            kctx.Ball != null)
        {
            Transform holder = kctx.Ball.transform.parent;
            if (holder != null && kctx.IsProtectedKeeper(holder))
            {
                // Clear slightly PAST the protect radius so we settle outside the keeper's
                // crowding ring (same 2.5) — parking exactly on it would flicker its
                // "crowded" check and stall the pass-out we're making room for.
                Vector2 away = rb.position - (Vector2)holder.position;
                if (away.magnitude < KeeperProtectRadius + 0.35f)
                {
                    rb.linearVelocity = (away.sqrMagnitude > 1e-4f ? away.normalized : Vector2.down)
                                        * moveSpeed;
                    return;
                }
            }
        }
        float speed = isHolding ? holdMoveSpeed : moveSpeed;
        speed *= StaminaSpeedMult;                                          // tired = slower (stamina)
        // HOLD sprint — disabled outright at 0% stamina ("normal swim only"), else the sprint
        // multiplier is scaled by stamina too (both move speed AND sprint cut when tired).
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

        bool show = IsActive; // chevron belongs to the human-controlled player only (hidden the
                              // instant control moves away, since this runs every Update)
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

        // The bar itself tells shot from pass at a glance:
        //   PASS  — a cool blue→cyan ramp (calm, never flashes).
        //   SHOT  — the hot green→yellow→red ramp, and past 0.7 fill (the HIGH-shot zone,
        //           where release becomes the arcing dart) it strobes toward white.
        Color col;
        if (chargeMode == Charging.Pass)
        {
            col = Color.Lerp(new Color(0.25f, 0.55f, 1f), new Color(0.2f, 0.95f, 1f), fill);
        }
        else
        {
            col = fill < 0.5f
                ? Color.Lerp(new Color(0.2f, 1f, 0.3f), new Color(1f, 0.9f, 0.2f), fill * 2f)
                : Color.Lerp(new Color(1f, 0.9f, 0.2f), new Color(1f, 0.25f, 0.2f), (fill - 0.5f) * 2f);
            if (fill > 0.7f)
                col = Color.Lerp(col, Color.white, Mathf.PingPong(Time.time * 6f, 1f) * 0.65f);
        }
        powerBar.startColor = powerBar.endColor = col;
    }

    void TryGrabBall()
    {
        if (ball == null) return;
        MatchContext ctx = MatchContext.Instance;
        // loose + past cooldown + not under a shot-clock turnover ban
        if (ctx != null && (!ctx.BallGrabbable || !ctx.CanGrab(ctx.PlayerTeam))) return;

        // 2026-07-09g: the same positional catch rule the AI uses — a slow/settled ball is
        // picked up inside grabDistance from any side; a ball still FLYING past can only be
        // caught close-in while roughly facing it. Applies to auto-collect AND the E press.
        if (ctx != null)
        {
            if (WaterPoloBrain.CanCatchLooseBall(ctx, transform.position, lastDirection, grabDistance))
                GrabBall();
            return;
        }

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
        ball.transform.SetParent(handPosition != null ? handPosition : transform);
        ball.transform.localPosition = Vector3.zero; // snap to the hand anchor

        if (MatchContext.Instance != null)
            MatchContext.Instance.SetPossession(MatchContext.Instance.PlayerTeam);
    }

    // (The 2026-07-09c "steal whiff" puff — a small expanding circle on every silent steal
    // exit — is REMOVED (2026-07-09f): the dev read it as a random ball appearing/disappearing.
    // The out-of-range lunge below keeps the snatch ANIMATION, which predates the puff.)

    void TrySteal()
    {
        if (isHolding || ball == null) return;
        if (Time.time - lastStealTime < stealCooldown) return; // locked out between attempts

        MatchContext ctx = MatchContext.Instance;
        if (ctx == null) return;
        if (ctx.FreeThrowActive) return; // no steals during a free throw

        TeamSide enemy = ctx.EnemyOf(ctx.PlayerTeam);
        if (enemy == null || !ctx.TeamHasBall(enemy)) return;

        Transform carrier = ball.transform.parent;
        if (carrier == null) return;
        if (ctx.IsFoulProtected(carrier)) return; // freshly-fouled carrier is untouchable (2026-07-09f)
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

        // ctx.BallPosition, not ball.position: a held ball's rigidbody pose is frozen at the
        // catch point — the live (transform) position is where the carrier actually has it.
        if (Vector2.Distance(transform.position, carrier.position) > stealDistance)
        {
            // lunged at open water — the snatch animation is the whiff feedback
            if (playerAnimator != null) playerAnimator.TriggerSteal();
            return;
        }

        // In range = a real attempt: play the snatch animation NOW, before the facing
        // gate or the dice roll, so EVERY attempt is visible (success or not).
        if (playerAnimator != null) playerAnimator.TriggerSteal();

        // A blindside/rear attempt is no longer a silent blocked input. It is illegal contact and
        // therefore goes straight to the existing exclusion-level foul path (or a penalty in 2m).
        Vector2 carrierFacing = Vector2.zero;
        IAgentBody carrierBody = carrier.GetComponent<IAgentBody>();
        if (carrierBody != null) carrierFacing = carrierBody.LastDirection;
        else { PlayerMovement cpm = carrier.GetComponent<PlayerMovement>(); if (cpm != null) carrierFacing = cpm.Facing; }
        Vector2 dirToCarrier = (Vector2)carrier.position - (Vector2)transform.position;
        if (dirToCarrier.sqrMagnitude > 1e-4f) dirToCarrier.Normalize();
        if (carrierFacing.sqrMagnitude > 1e-4f &&
            Vector2.Dot(carrierFacing.normalized, -dirToCarrier) < StealFacingDot)
        {
            if (ExclusionManager.Instance != null)
                ExclusionManager.Instance.ReportExclusionFoul(transform, ctx.PlayerTeam, carrier);
            return; // ball stays with the victim; anim already fired above
        }

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
            ExclusionManager.StunSuccessfulStealVictim(carrier);
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
        if (Time.time - lastStealTime < stealCooldown) return; // locked out between attempts

        MatchContext ctx = MatchContext.Instance;
        if (ctx == null) return;
        if (ctx.FreeThrowActive) return; // no steals during a free throw

        TeamSide enemy = ctx.EnemyOf(ctx.PlayerTeam);
        if (enemy == null || !ctx.TeamHasBall(enemy)) return;

        Transform carrier = ball.transform.parent;
        if (carrier == null) return;
        if (ctx.IsFoulProtected(carrier)) return; // freshly-fouled carrier is untouchable (2026-07-09f)
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

        // The safer Block action uses the same genuinely-close carrier distance as Space.
        if (Vector2.Distance(transform.position, carrier.position) > stealDistance)
        {
            // lunged at open water — the snatch animation is the whiff feedback
            if (playerAnimator != null) playerAnimator.TriggerSteal();
            return;
        }

        // In range = a real attempt → play the snatch animation now (success or not).
        if (playerAnimator != null) playerAnimator.TriggerSteal();

        // The safer Block button changes the front-on success/foul odds, but it does not legalize
        // blindside contact: rear/outside-front-arc contact is still an exclusion-level foul.
        Vector2 carrierFacing = Vector2.zero;
        IAgentBody carrierBody = carrier.GetComponent<IAgentBody>();
        if (carrierBody != null) carrierFacing = carrierBody.LastDirection;
        else { PlayerMovement cpm = carrier.GetComponent<PlayerMovement>(); if (cpm != null) carrierFacing = cpm.Facing; }
        Vector2 dirToCarrier = (Vector2)carrier.position - (Vector2)transform.position;
        if (dirToCarrier.sqrMagnitude > 1e-4f) dirToCarrier.Normalize();
        if (carrierFacing.sqrMagnitude > 1e-4f &&
            Vector2.Dot(carrierFacing.normalized, -dirToCarrier) < StealFacingDot)
        {
            if (ExclusionManager.Instance != null)
                ExclusionManager.Instance.ReportExclusionFoul(transform, ctx.PlayerTeam, carrier);
            return; // ball stays with the victim; anim already fired above
        }

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
            ExclusionManager.StunSuccessfulStealVictim(carrier);
        }
        else if (Random.value < 0.5f && ExclusionManager.Instance != null) // only HALF of misses foul
        {
            ExclusionManager.Instance.ReportFoul(transform, ctx.PlayerTeam, carrier);
        }
    }

    // Called by TouchControls every frame on the active player. SHOOT maps to Space
    // (so it also steals when not holding), PASS to B, SPRINT to LeftShift (HELD = sprinting),
    // SWITCH to C (consumed by TeamManager via TouchSwitchDown).
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

    // Touch LOB modifier (Task 4): TouchControls sets this true while its on-screen LOB toggle is
    // armed, so a touch pass fires as the big F+B lob. Merged with the F key in ChargedPass.
    public void SetLobModifier(bool on) { touchLobHeld = on; }

    private void ClearTouchInput()
    {
        touchAxis = Vector2.zero;
        touchShootHeld = touchShootDown = touchShootUp = false;
        touchPassHeld = touchPassDown = touchPassUp = false;
        touchSprintHeld = false;
        touchSwitchDown = false;
        touchLobHeld = false;
    }

    void DropBall()
    {
        if (ball == null) return;
        isHolding = false;
        // The dropped ball lies overlapping our feet — without the ignore window physics would
        // pop it away with a depenetration shove instead of leaving it where it was dropped.
        if (MatchContext.Instance != null) MatchContext.Instance.IgnoreReleaseCollision(transform);
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

        // Explicit presentation signal: stationary shots must animate too. This does not alter
        // lastDirection, shot power, landing point, or any pass/shoot mechanics.
        if (playerAnimator != null) playerAnimator.TriggerShoot();

        bool skip = skipCharge;
        skipCharge = false;
        if (skip) shotHeight = skipShotHeight; // a skip shot is fast and LOW by definition

        // ShotSpeedMult: a shot always travels measurably faster than a pass of the same
        // charge — that speed gap (plus the snap/arc-shape in BallFlight) is what makes a
        // shot readable as a shot with no HUD.
        float speed = Mathf.Max(currentPower, minShootSpeed) * ShotSpeedMult; // a tap still fires a real shot
        if (!skip && shotHeight > 0.7f) speed *= highShotSpeedBonus; // high shots fly faster

        // EVERY shot now leaves the water as the untouchable ASYMMETRIC shot arc (steep rise,
        // hang, sharp drop — ArcKind.Shot), landing 1.5u short of the aimed goal line then flying
        // on as a normal shot the keeper can save. BallFlight scales the arc height DOWN with
        // charge, so a weak tap still visibly hops (never flat) while a full charge arcs high —
        // the old flat quick-tap shot is gone (Task 2). Only a skip shot (deliberate LOW bounce)
        // or a degenerate zero-distance launch takes the flat path below.
        if (!skip && BallFlight.Instance != null &&
            BallFlight.Instance.LaunchHighBall(HighShotLandPoint(), speed, shotHeight,
                                               BallFlight.ArcKind.Shot))
        {
            isHolding = false;
            ball.transform.SetParent(null); // airborne — no collisions exist to ignore
        }
        else
        {
            isHolding = false;
            // Release from the HAND, where the hold visually pinned it — the old snap-to-centre
            // spawned the ball INSIDE our own collider and the depenetration deflected/weakened the
            // shot. The self-collision window is what makes any release angle safe, including firing
            // back across the body.
            if (MatchContext.Instance != null) MatchContext.Instance.IgnoreReleaseCollision(transform);
            ball.transform.SetParent(null);
            ball.simulated = true;
            ball.linearVelocity = lastDirection * speed;

            if (BallFlight.Instance != null)
                BallFlight.Instance.NoteShot(shotHeight, skip); // arms the bounce for a skip
        }

        if (MatchContext.Instance != null)
        {
            MatchContext.Instance.NoteRelease(transform); // remember the shooter (Centre-goal tracking)
            MatchContext.Instance.SetPossession(null);
        }
    }

    // Where a HIGH shot comes back down: 1.5u short of the goal line the aim crosses, so the
    // keeper contests a normal (landed) ball at its doorstep — the same spacing the skip shot's
    // bounce point uses. Near-vertical aims (no goal line ahead) arc a fixed clearance instead.
    Vector2 HighShotLandPoint()
    {
        Vector2 from = ball != null ? (Vector2)ball.transform.position : (Vector2)transform.position;
        Vector2 dir = lastDirection.sqrMagnitude > 1e-4f ? lastDirection.normalized : Vector2.up;
        if (Mathf.Abs(dir.x) > 0.05f)
        {
            float lineX = Mathf.Sign(dir.x) * (HighShotGoalLineX - HighShotLandShort);
            float t = (lineX - from.x) / dir.x;
            if (t > 0f) return from + dir * t;
        }
        return from + dir * HighShotClearDistance;
    }

    // Fully manual pass: the ball follows lastDirection exactly. Charge controls speed and
    // landing distance; teammate positions never bend the direction or replace the landing point.
    void ChargedPass(float charge)
    {
        if (ball == null || !isHolding) return;
        if (MatchContext.Instance == null) return;
        Vector2 aimDir = lastDirection.sqrMagnitude > 1e-4f ? lastDirection.normalized : Vector2.up;

        Vector2 fireDir = aimDir;

        lastDirection = fireDir;

        // Work out the throw speed BEFORE releasing so a dud (near-zero) pass can be refused — the
        // floor is minPassSpeed even for an untimed tap, so a pass always carries to a teammate.
        // EVERY pass is airborne now (BallFlight arc — untouchable mid-flight by either team):
        //   B alone      = ArcKind.Pass — a small quick hop at full pass speed (the toned-down arc).
        //   F + B (or the touch LOB toggle) = ArcKind.Lob — the big slow floaty ball over the top.
        // Only a degenerate near-zero-distance throw stays flat now — every real pass, however
        // short or weak, arcs (Task 1).
        float speed = Mathf.Clamp(Mathf.Lerp(minPassSpeed, maxPassSpeed, Mathf.Clamp01(charge)),
                                  minPassSpeed, maxPassSpeed);
        bool lob = Input.GetKey(KeyCode.F) || touchLobHeld;
        if (lob) speed *= lobSpeedFactor;

        // Too weak to be a real pass → keep holding rather than dropping the ball at our feet.
        if (speed < MinPassReleaseSpeed) return;

        // Land at a charge-scaled spot on the exact aim ray.
        bool high = false;
        if (BallFlight.Instance != null)
        {
            Vector2 land = (Vector2)ball.transform.position +
                           fireDir * PassTravelDistance(charge, lob);
            high = BallFlight.Instance.LaunchHighBall(land, speed, lob ? 0.9f : 0.5f,
                                                      lob ? BallFlight.ArcKind.Lob
                                                          : BallFlight.ArcKind.Pass);
        }

        isHolding = false;
        if (high)
        {
            ball.transform.SetParent(null); // airborne — no collisions exist to ignore
        }
        else
        {
            MatchContext.Instance.IgnoreReleaseCollision(transform); // a backward pass must clear our own body
            ball.transform.SetParent(null);
            ball.simulated = true;
            ball.linearVelocity = fireDir * speed;
            if (BallFlight.Instance != null) BallFlight.Instance.NotePass(); // point-blank flat pass → no swell/trail
        }
        shotHeight = lob ? 0.9f : 0.5f; // a pass overwrites LastReleaser → keep its height honest

        MatchContext.Instance.NoteRelease(transform);
        MatchContext.Instance.SetPossession(null);
    }

    // Charge controls WHERE the pass lands, independently of its flight pace.  The old
    // linear 3.5→6.5 range guaranteed a weak tap most of a full pass's distance before
    // the landing roll was even added.  A convex curve makes the three useful bands
    // distinct: tap ≈ short outlet, half charge ≈ midfield pass, full charge ≈ long ball.
    static float PassTravelDistance(float charge, bool lob)
    {
        float distance01 = Mathf.Pow(Mathf.Clamp01(charge), PassDistancePower);
        return lob
            ? Mathf.Lerp(HighLobRangeMin, HighLobRangeMax, distance01)
            : Mathf.Lerp(PassArcRangeMin, PassArcRangeMax, distance01);
    }

    public void ReleaseBall()
    {
        isHolding = false;
        if (ball != null)
        {
            if (MatchContext.Instance != null) MatchContext.Instance.IgnoreReleaseCollision(transform);
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
            // Moving carrier: push the ball automatically ahead of the ACTUAL travel vector. This
            // is independent from lastDirection, so presentation never rewrites pass/shoot aim.
            // Stationary carrier: retain the existing art-tuned holding-hand offsets exactly.
            bool movingWithBall = IsMovingWithBall;
            float motion = Mathf.Sin(
                Time.time * movingHoldBallPushCyclesPerSecond * Mathf.PI * 2f + heldBallMotionPhase);
            Vector2 visualOffset = movingWithBall
                ? rb.linearVelocity.normalized *
                  (movingHoldBallForwardOffset + motion * movingHoldBallPushAmplitude)
                : HeldBallHandOffset();
            Vector3 p = transform.position + (Vector3)visualOffset;
            p.z = ball.transform.position.z;
            ball.transform.position = p;
            ball.transform.localRotation = movingWithBall
                ? Quaternion.Euler(0f, 0f, motion * movingHoldBallRockDegrees)
                : Quaternion.identity;
        }
    }

    // Legacy world-space hand offsets, now used only while the carrier is stationary. The down/idle
    // hand is a single fixed spot, so the ball never jumps sides between A→S and D→S.
    Vector2 HeldBallHandOffset()
    {
        // Kept for compatibility with the existing serialized up/back offsets. Under the moving-hold
        // path this method is bypassed; at a true stop vy is normally below this branch's threshold.
        float vy = rb != null ? rb.linearVelocity.y : 0f;

        // Ends are swapped at halftime (P3/P4): the player team's defendGoal moves to the RIGHT (+x).
        // MatchContext has no explicit "swapped" flag, so we read it the same way the rest of the
        // codebase does — the sign of defendGoal.x. When swapped we use the dedicated *Swapped offsets
        // (tuned for P3/P4 directly); P1/P2 (own goal on the LEFT, -x) uses the normal offsets.
        MatchContext ctx = MatchContext.Instance;
        bool swapped = ctx != null && ctx.PlayerTeam != null && ctx.PlayerTeam.defendGoal != null &&
                       ctx.PlayerTeam.defendGoal.position.x > 0f;

        Vector2 offset;
        if (vy > BackFacingThreshold)
            offset = lastDirection.x >= 0f
                ? (swapped ? handOffsetUpSwapped : handOffsetUp)
                : (swapped ? handOffsetUpLeftSwapped : handOffsetUpLeft);
        // FRONT body, explicit left/right aim.
        else if (lastDirection.x > 0.1f) offset = swapped ? handOffsetRightSwapped : handOffsetRight;
        else if (lastDirection.x < -0.1f) offset = swapped ? handOffsetLeftSwapped : handOffsetLeft;
        // Facing DOWN / idle: the hand sits at the SAME spot regardless of which way we last faced
        // horizontally, so the ball no longer jumps sides between A→S and D→S.
        else offset = swapped ? handOffsetDownSwapped : handOffsetDown;

        return offset;
    }
}
