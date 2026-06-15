using System.Collections.Generic;
using UnityEngine;

// Kinematic keeper that slides along its goal line tracking the ball, blocks shots, and
// (Part 1) COLLECTS a slow loose ball near its goal, holds briefly, then DISTRIBUTES to an
// open teammate. A keeper hold is not a possession change for the shot clock (it keeps
// ticking for the holding team; the reset happens on the pass-out). Never crosses the
// midline (only moves in y) and never dribbles.
[RequireComponent(typeof(Rigidbody2D))]
public class Goalkeeper : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody2D ball;

    [Header("Movement")]
    [SerializeField] private float trackSpeed = 4f;
    [SerializeField] private float minY = -2f;
    [SerializeField] private float maxY = 2f;
    [Tooltip("Task 2: only TRACK the ball's Y while it's within this X-distance of the goal line; farther out the keeper glides back to centre.")]
    [SerializeField] private float trackingRange = 3.5f;
    [Tooltip("Task 2/4: HARD limit — the keeper's Y is always clamped to ±this (goal-mouth height). minY/maxY are clamped to it too.")]
    [SerializeField] private float maxYOffset = 2.5f;

    [Header("Shot reaction")]
    [SerializeField] private float shotSpeedThreshold = 4f;      // loose ball faster than this = a shot
    [SerializeField] private float highShotReactionDelay = 0.2f; // extra freeze vs a HIGH (charge > 0.7) shot

    [Header("Saves (Task 3)")]
    [Tooltip("Base chance to save a fast shot that reaches the keeper's hands.")]
    [SerializeField] private float baseSaveChance = 0.65f;
    [Tooltip("HIGH shots (BallFlight.ShotHeight > 0.7) cut the save chance by this.")]
    [SerializeField] private float highShotSavePenalty = 0.25f;
    [Tooltip("HARD shots (ball speed > 9) cut the save chance further.")]
    [SerializeField] private float powerSavePenalty = 0.15f;
    [Tooltip("SKIP shots are the hardest to read — cut the save chance by this.")]
    [SerializeField] private float skipShotSavePenalty = 0.35f;
    [Tooltip("After a MISSED save the keeper can't grab for this long (it's beaten).")]
    [SerializeField] private float missedSaveCooldown = 1.5f;

    [Header("Grab & control (Part 1)")]
    [SerializeField] private float keeperGrabDistance = 1.2f;  // collect / save a loose ball within this
    [SerializeField] private float keeperSnatchDistance = 0.8f;// strip an enemy carrier this close — 100%, no roll
    [SerializeField] private float keeperHoldSeconds = 0.8f;   // bot keeper auto-distributes after this
    [SerializeField] private float keeperPanicDistance = 2.5f; // bot keeper distributes NOW if an opponent is this close
    [SerializeField] private float holdOffset = 0.5f;          // held ball sits this far toward the field

    // ---- player control (Task 5): when the HUMAN's own keeper holds the ball it plays like a
    //      field swimmer — Y-only movement along the line, sprint, a charged shot, and a pass. ----
    [Header("Player control (Task 5)")]
    [Tooltip("Task 4: free-roam swim speed while the human holds the ball — same as a field player's moveSpeed.")]
    [SerializeField] private float keeperMoveSpeed = 4f;          // free-roam speed while the human holds the ball
    [SerializeField] private float keeperShootPower = 12f;        // max charged shot speed
    [SerializeField] private float keeperChargeRate = 18f;        // shot charge gained per second (fast wind-up)
    [SerializeField] private float keeperSprintMultiplier = 1.8f; // move speed boost while sprinting

    const float KeeperRoamY = 4f;          // Task 4: how far up/down the pool a ball-carrying keeper may roam (field height)
    const float KeeperMinShootSpeed = 8f;  // a keeper shot tap still travels (never a limp drop)

    const float KeeperIdleRoamX = 0.4f;      // Task 6: NOT holding — keeper drifts at most this far off its goal line
    const float KeeperIdleBobY  = 0.05f;     // Task 6: NOT holding — subtle living Y micro-bob amplitude
    const float KeeperSafeZoneRadius = 1.5f; // Task 5: holding within this of the goal line → can't be robbed
    const float SaveRollSpeed   = 5f;      // Task 3: a ball faster than this needs a save roll; slower = auto-grab
    const float FastShotSpeed   = 9f;      // Task 3: above this a shot also takes the power penalty

    // ---- Stamina hooks (set by StaminaSystem, if present). NEUTRAL defaults so the keeper plays
    //      identically with no StaminaSystem attached — nothing here references StaminaSystem. ----
    public bool IsHolding => holding;                       // read by StaminaSystem for drain
    public bool LeftSafeZone => keeperLeftSafeZone;         // Task 5: true once it carried the ball out of its safe zone
    public float StaminaPercent01 { get; set; } = 1f;       // 0..1; gates the save penalty (Task 4)
    public bool StaminaSprintBlocked { get; set; } = false; // true at 0% stamina → keeper can't sprint

    private Rigidbody2D rb;
    private float homeX;        // the keeper's goal-line X — it returns here after a shot/pass/roam
    private float startX;       // Task 4: goal-line X recorded in Awake — keeper is hard-locked to it
    private float missedSaveUntil = -10f;  // Task 3: can't grab until this time (beaten-on-a-shot recovery)
    private Collider2D[] blockers;         // our non-trigger collider(s); dropped so a missed shot passes through
    private bool blockersDisabled;
    private bool holding;
    private float holdStartTime;
    private bool shotIncoming;              // edge-detects a NEW incoming shot
    private float reactBlockedUntil = -10f; // high-shot reaction-delay window

    private Vector2 lastDir = Vector2.left; // aim/facing for a keeper shot (set on grab)
    private float currentPower;             // current shot charge (0..keeperShootPower)
    private bool chargingShot;
    private float passPower;                // current pass charge (0..1)
    private bool chargingPass;

    private bool keeperLeftSafeZone;        // Task 5: carried the ball out of the safe zone this possession
    // Task 6: organic idle motion while NOT holding the ball
    private float driftOffsetX;             // current cosmetic X drift target (relative to homeX)
    private float driftNextTime;            // when to pick the next drift target
    private float yBobPeriod = 2f;          // current Y micro-bob period (s)
    private float yBobNextTime;             // when to re-randomise the bob period

    // touch input mirrored from TouchControls while the human controls this keeper
    private Vector2 touchAxis;
    private bool touchShootHeld, touchShootDown, touchShootUp;
    private bool touchPassHeld, touchPassDown, touchPassUp;
    private bool touchSprintHeld;

    // on-ball HUD shown only while the human controls this keeper (mirrors the field player)
    private SpriteRenderer keeperIndicator;       // bouncing green triangle
    private LineRenderer keeperAim;               // facing chevron
    private LineRenderer keeperBar, keeperBarBG;  // shoot / pass power bar
    const float HudIndicatorY = 0.5f, HudIndicatorBob = 0.06f, HudIndicatorSize = 0.3f;
    const float HudAimGap = 0.4f, HudAimLen = 0.25f, HudAimHalf = 0.12f, HudAimWidth = 0.05f;
    const float HudBarW = 0.55f, HudBarH = 0.07f, HudBarY = 0.34f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        homeX = startX = transform.position.x; // Task 4: lock the goal line to where the keeper starts
        CacheBlockers();
        BuildKeeperHud();
    }

    // Remember our solid (non-trigger) collider(s) so a missed save can briefly drop them and let
    // the ball pass through. Triggers (if any) are left alone.
    void CacheBlockers()
    {
        List<Collider2D> found = new List<Collider2D>();
        foreach (Collider2D c in GetComponents<Collider2D>())
            if (c != null && !c.isTrigger) found.Add(c);
        blockers = found.ToArray();
    }

    void SetBlockers(bool on)
    {
        if (blockers == null) return;
        foreach (Collider2D c in blockers) if (c != null) c.enabled = on;
    }

    // The team currently DEFENDING this physical goal (auto-corrects after a halftime swap),
    // derived from which team's defendGoal is on this keeper's side. No wiring needed.
    TeamSide KeeperTeam()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null) return null;
        float mySign = transform.position.x >= 0f ? 1f : -1f;
        if (Defends(ctx.PlayerTeam, mySign)) return ctx.PlayerTeam;
        if (Defends(ctx.BotTeam, mySign)) return ctx.BotTeam;
        return null;
    }
    static bool Defends(TeamSide t, float sign)
        => t != null && t.defendGoal != null && Mathf.Sign(t.defendGoal.position.x) == sign;

    bool IsPlayerKeeper()
    {
        MatchContext ctx = MatchContext.Instance;
        return ctx != null && KeeperTeam() == ctx.PlayerTeam;
    }

    void Update()
    {
        // Full player control while OUR keeper holds the ball (Task 5): aim with WASD / stick,
        // B or the PASS button distributes, Space or the SHOOT button charges then fires.
        // Movement itself is applied in FixedUpdate (HoldTick).
        bool playerControlled = holding && IsPlayerKeeper();
        if (!playerControlled)
        {
            if (chargingShot) { chargingShot = false; currentPower = 0f; }
            if (chargingPass) { chargingPass = false; passPower = 0f; }
            ClearKeeperTouch();
            UpdateKeeperHud();
            return;
        }

        // aim/facing from keyboard + touch (the body only ever moves in FixedUpdate)
        Vector2 inDir = new Vector2(Input.GetAxisRaw("Horizontal") + touchAxis.x,
                                    Input.GetAxisRaw("Vertical") + touchAxis.y);
        if (inDir.sqrMagnitude > 0.01f) lastDir = inDir.normalized;

        // PASS (B / pass button): hold to charge, release to distribute (charge scales speed).
        if (!chargingShot && !chargingPass && (Input.GetKeyDown(KeyCode.B) || touchPassDown))
            chargingPass = true;
        if (chargingPass)
        {
            if (Input.GetKey(KeyCode.B) || touchPassHeld)
                passPower = Mathf.Min(passPower + (1f / 0.45f) * Time.deltaTime, 1f);
            if (Input.GetKeyUp(KeyCode.B) || touchPassUp)
            {
                float p = passPower; chargingPass = false; passPower = 0f;
                PassOut(Mathf.Max(p, 0.35f)); // a tap still sends a real pass
                UpdateKeeperHud();
                return;
            }
        }

        // SHOOT (Space / shoot button): charge while held, fire in the aim direction on release.
        if (!chargingPass && !chargingShot && (Input.GetKeyDown(KeyCode.Space) || touchShootDown))
            chargingShot = true;
        if (chargingShot)
        {
            if (Input.GetKey(KeyCode.Space) || touchShootHeld)
                currentPower = Mathf.Min(currentPower + keeperChargeRate * Time.deltaTime, keeperShootPower);
            if (Input.GetKeyUp(KeyCode.Space) || touchShootUp)
            {
                KeeperShoot();
                currentPower = 0f; chargingShot = false;
                UpdateKeeperHud();
                return;
            }
        }

        UpdateKeeperHud();
    }

    void FixedUpdate()
    {
        if (ball == null || rb == null) return;
        MatchContext ctx = MatchContext.Instance;

        if (holding) { HoldTick(ctx); return; }

        // Restore our blocking collider once a missed-save recovery window has elapsed (Task 3).
        if (blockersDisabled && Time.time >= missedSaveUntil) { SetBlockers(true); blockersDisabled = false; }

        // Edge-detect a NEW incoming shot (loose + fast + toward our side). A HIGH shot
        // (charged past 0.7) freezes our reaction for an extra beat — harder to block.
        bool incoming = ctx != null && ctx.BallIsLoose &&
                        ball.linearVelocity.magnitude > shotSpeedThreshold &&
                        ball.linearVelocity.x * Mathf.Sign(transform.position.x) > 0f;
        if (incoming && !shotIncoming)
        {
            shotIncoming = true;
            if (IncomingShotHeight(ctx) > 0.7f)
                reactBlockedUntil = Time.time + highShotReactionDelay;
        }
        else if (!incoming) shotIncoming = false;

        // A skip shot that fooled us at its bounce (BallFlight) stops our tracking too —
        // we hold the line where we are and don't react to the deflection.
        bool fooled = incoming && BallFlight.Instance != null && BallFlight.Instance.KeeperFooled;

        Vector2 pos = rb.position;
        bool ballFar = Mathf.Abs(ball.position.x - startX) > trackingRange;

        // X: swim back onto the goal line. A player keeper that roamed out with the ball returns
        // SMOOTHLY here — the old per-frame hard clamp TELEPORTED it back the instant it released
        // (Tasks 4/5). While set on the line and the ball is far, add a tiny organic forward/back
        // drift so the keeper looks alive (Task 6); it's bounded to ±KeeperIdleRoamX of the line.
        if (ballFar)
        {
            if (Time.time >= driftNextTime)
            {
                float mag = Random.Range(0.1f, 0.3f);
                driftOffsetX = Random.value < 0.5f ? -mag : mag;          // small forward/back step
                driftNextTime = Time.time + Random.Range(2f, 4f);
            }
        }
        else driftOffsetX = 0f;                                            // ball near → hug the line
        float targetX = homeX + Mathf.Clamp(driftOffsetX, -KeeperIdleRoamX, KeeperIdleRoamX);
        pos.x = Mathf.MoveTowards(pos.x, targetX, trackSpeed * Time.fixedDeltaTime);

        // Y: TRACK the ball's height while it's near our line; otherwise glide to centre at half
        // speed. A subtle sine micro-bob (Task 6, ±KeeperIdleBobY, period 1.5–2.5s) is folded into
        // the target so the keeper isn't robotic. A shot reaction / skip-shot fake still freezes us.
        if (Time.time >= reactBlockedUntil && !fooled)
        {
            if (Time.time >= yBobNextTime)
            {
                yBobPeriod = Random.Range(1.5f, 2.5f);
                yBobNextTime = Time.time + Random.Range(2f, 4f);
            }
            float yBob = Mathf.Sin(Time.time * (2f * Mathf.PI / Mathf.Max(yBobPeriod, 0.01f))) * KeeperIdleBobY;

            if (!ballFar)
            {
                // clamp the tracking target to BOTH the serialized min/max AND the hard maxYOffset
                float lo = Mathf.Max(minY, -maxYOffset);
                float hi = Mathf.Min(maxY,  maxYOffset);
                float targetY = Mathf.Clamp(ball.position.y, lo, hi);
                pos.y = Mathf.MoveTowards(pos.y, targetY + yBob, trackSpeed * Time.fixedDeltaTime);
            }
            else
            {
                pos.y = Mathf.MoveTowards(pos.y, yBob, trackSpeed * 0.5f * Time.fixedDeltaTime);
            }
        }

        // Y safety: never leave the goal mouth (±maxYOffset). X is bounded by its drift target
        // itself (≤ KeeperIdleRoamX off the line once set), so there's NO hard X snap here — a
        // released roamer swims home instead of teleporting (Task 5).
        pos.y = Mathf.Clamp(pos.y, -maxYOffset, maxYOffset);

        rb.MovePosition(pos);

        TeamSide team = KeeperTeam();

        // SNATCH (Task 5): an enemy carrier point-blank on the keeper (within
        // keeperSnatchDistance) is stripped with 100% success — no probability roll.
        if (ctx != null && team != null && TrySnatchFromCarrier(ctx, team)) return;

        // SAVE / COLLECT a loose ball that's reached our hands (Task 3). Blocked during a
        // post-miss recovery window. Keeps the release-cooldown + grab-ban gates so a keeper can't
        // instantly re-grab a ball it just released or one it's banned from.
        if (ctx != null && team != null && Time.time >= missedSaveUntil &&
            ctx.BallGrabbable && ctx.CanGrab(team) &&
            Vector2.Distance(rb.position, ball.position) <= keeperGrabDistance)
        {
            TrySaveOrCollect(ctx, team);
        }
    }

    // A loose ball has reached the keeper's hands (Task 3). A SLOW ball (≤ SaveRollSpeed) is an
    // easy collection — always grabbed, no roll. A FAST shot rolls baseSaveChance minus the
    // penalties it earns (high / hard / skip). On a MISS the keeper is beaten: it does NOT grab,
    // its blocking collider is dropped so the ball flies on into the goal, and it can't grab again
    // for missedSaveCooldown. The dive animation keeps playing the whole time because the ball is
    // still loose, fast and heading our way (GoalkeeperAnimator reads that, not us).
    void TrySaveOrCollect(MatchContext ctx, TeamSide team)
    {
        float speed = ball.linearVelocity.magnitude;
        if (speed <= SaveRollSpeed) { Grab(ctx, team); return; } // easy save — always collected

        float chance = baseSaveChance;
        BallFlight flight = BallFlight.Instance;
        if (flight != null)
        {
            if (flight.ShotHeight > 0.7f) chance -= highShotSavePenalty; // high corner
            if (flight.SkipActive)        chance -= skipShotSavePenalty; // skip shot
        }
        if (speed > FastShotSpeed) chance -= powerSavePenalty;           // sheer pace

        // STAMINA (Task 4): a tired keeper reacts worse; an exhausted one worse still.
        if (StaminaPercent01 < 0.2f) chance -= 0.1f;
        if (StaminaPercent01 <= 0f)  chance -= 0.15f; // additional at full exhaustion

        if (Random.value <= chance)
        {
            Grab(ctx, team); // SAVE
        }
        else
        {
            // BEATEN: let it through and lock out grabs for the recovery window.
            missedSaveUntil = Time.time + missedSaveCooldown;
            SetBlockers(false);
            blockersDisabled = true;
        }
    }

    // Height (0..1) of the ball's current flight: the last releaser's charged ShotHeight
    // (human shots). AI swimmers have no height system yet → 0.5 (mid).
    static float IncomingShotHeight(MatchContext ctx)
    {
        if (ctx != null && ctx.LastReleaser != null)
        {
            PlayerMovement pm = ctx.LastReleaser.GetComponent<PlayerMovement>();
            if (pm != null) return pm.ShotHeight;
        }
        return 0.5f;
    }

    void HoldTick(MatchContext ctx)
    {
        // lost the ball (e.g. a shot-clock turnover detached it, or an enemy stole it) → clear state
        if (ball.transform.parent != transform)
        {
            holding = false;
            keeperLeftSafeZone = false; // Task 5: possession over → reset the steal flag
            if (ctx != null) ctx.ClearKeeperHold();
            return;
        }

        bool playerControlled = IsPlayerKeeper();

        // Player keeper (Issue 2): move FREELY like a field player (X + Y), faster while
        // sprinting. Clamped so it can roam out to make a play but can NEVER cross its own goal
        // line. There is NO automatic pass — the human is fully in charge; the keeper only
        // leaves the ball when the human shoots/passes (or the shot clock turns it over).
        if (playerControlled)
        {
            float ix = Input.GetAxisRaw("Horizontal") + touchAxis.x;
            float iy = Input.GetAxisRaw("Vertical") + touchAxis.y;
            Vector2 move = new Vector2(ix, iy);
            if (move.sqrMagnitude > 1f) move.Normalize();
            float speed = keeperMoveSpeed;
            // Sprint — disabled outright when the keeper is exhausted (Task 4: 0% stamina → no sprint).
            if ((Input.GetKey(KeyCode.LeftShift) || touchSprintHeld) && !StaminaSprintBlocked)
                speed *= keeperSprintMultiplier;

            Vector2 p = rb.position + move * (speed * Time.fixedDeltaTime);
            float limitX = ctx != null ? ctx.PlayerLimitX : 6.9f;
            // Full 2D freedom like a field player (Task 4): roam the whole pool, only barred from
            // crossing its OWN goal line. homeX (the goal line) is the OUTER bound; the opposite
            // player limit is the inner one. Y spans the full pool height (±KeeperRoamY).
            p.x = Mathf.Clamp(p.x, homeX > 0f ? -limitX : homeX, homeX > 0f ? homeX : limitX);
            p.y = Mathf.Clamp(p.y, -KeeperRoamY, KeeperRoamY);
            rb.MovePosition(p);
        }

        // Task 5: once the keeper carries the ball more than KeeperSafeZoneRadius off its goal line
        // it becomes fair game for steals for the REST of this possession (flag persists until
        // release). The bot keeper never roams off its line while holding, so this only fires for a
        // human-controlled keeper that swam out with the ball.
        if (!keeperLeftSafeZone && Mathf.Abs(rb.position.x - startX) > KeeperSafeZoneRadius)
            keeperLeftSafeZone = true;

        // pin the held ball just in front of the keeper (toward centre)
        float toCentre = transform.position.x >= 0f ? -1f : 1f;
        ball.transform.localPosition = new Vector3(toCentre * holdOffset, 0f, 0f);

        // Bot keeper distributes after a short hold or when crowded. The PLAYER keeper never
        // auto-passes (Issue 2) — only the human's shoot/pass releases it.
        if (!playerControlled &&
            (Time.time - holdStartTime >= keeperHoldSeconds || EnemyCrowding(ctx)))
            PassOut();
    }

    // True while an opponent swimmer sits within keeperPanicDistance of this (bot) keeper.
    bool EnemyCrowding(MatchContext ctx)
    {
        if (ctx == null) return false;
        TeamSide team = KeeperTeam();
        if (team == null) return false;
        TeamSide enemy = ctx.EnemyOf(team);
        if (enemy == null || enemy.members == null) return false;
        foreach (Transform m in enemy.members)
        {
            if (m == null) continue; // excluded slots are null
            if (Vector2.Distance(rb.position, m.position) <= keeperPanicDistance) return true;
        }
        return false;
    }

    // Touch PASS OUT button (player keeper): distribute now — same as the keyboard B.
    public void RequestPassOut()
    {
        if (holding) PassOut();
    }

    // Touch input mirrored from TouchControls while the human controls this keeper (Task 5).
    // SHOOT = the bottom-right button (tap/hold/release), PASS = bottom-left tap, SPRINT = top.
    public void SetTouchInput(Vector2 axis, bool shootHeld, bool shootDown, bool shootUp,
                              bool passHeld, bool passDown, bool passUp, bool sprintHeld)
    {
        touchAxis = axis;
        touchShootHeld = shootHeld;
        touchShootDown = shootDown;
        touchShootUp = shootUp;
        touchPassHeld = passHeld;
        touchPassDown = passDown;
        touchPassUp = passUp;
        touchSprintHeld = sprintHeld;
    }

    void ClearKeeperTouch()
    {
        touchAxis = Vector2.zero;
        touchShootHeld = touchShootDown = touchShootUp = false;
        touchPassHeld = touchPassDown = touchPassUp = false;
        touchSprintHeld = false;
    }

    // ---- on-ball HUD: green triangle + facing chevron + power bar, shown only while the human
    //      controls this keeper. World-space so the keeper's (possibly non-uniform) scale can't
    //      stretch it; mirrors the field player's look. ----
    void BuildKeeperHud()
    {
        GameObject ind = new GameObject("KeeperIndicator");
        keeperIndicator = ind.AddComponent<SpriteRenderer>();
        keeperIndicator.sprite = Resources.Load<Sprite>("Sprites/indicator-triangle");
        keeperIndicator.sortingOrder = 60;
        keeperIndicator.enabled = false;
        if (keeperIndicator.sprite != null)
        {
            Vector2 s = keeperIndicator.sprite.bounds.size;
            if (s.x > 0f && s.y > 0f) ind.transform.localScale = new Vector3(HudIndicatorSize / s.x, HudIndicatorSize / s.y, 1f);
        }

        keeperAim   = NewHudLine("KeeperAim", 3, HudAimWidth, 59, new Color(0.3f, 1f, 0.4f, 0.85f));
        keeperBarBG = NewHudLine("KeeperBarBG", 2, HudBarH * 1.35f, 49, new Color(0f, 0f, 0f, 0.55f));
        keeperBar   = NewHudLine("KeeperBar", 2, HudBarH, 50, Color.green);
    }

    static LineRenderer NewHudLine(string name, int pts, float w, int order, Color c)
    {
        GameObject go = new GameObject(name);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = pts;
        lr.numCapVertices = 6;
        lr.startWidth = lr.endWidth = w;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = c;
        lr.sortingOrder = order;
        lr.enabled = false;
        return lr;
    }

    void UpdateKeeperHud()
    {
        bool show = holding && IsPlayerKeeper();
        Vector3 c = transform.position;

        if (keeperIndicator != null)
        {
            keeperIndicator.enabled = show;
            if (show)
                keeperIndicator.transform.position = c + new Vector3(0f, HudIndicatorY + Mathf.Sin(Time.time * 3f) * HudIndicatorBob, 0f);
        }

        if (keeperAim != null)
        {
            keeperAim.enabled = show;
            if (show)
            {
                Vector2 f = lastDir.sqrMagnitude > 1e-4f ? lastDir.normalized : Vector2.up;
                Vector2 perp = new Vector2(-f.y, f.x);
                Vector3 b = c + (Vector3)(f * HudAimGap);
                keeperAim.SetPosition(0, b + (Vector3)(perp * HudAimHalf));
                keeperAim.SetPosition(1, b + (Vector3)(f * HudAimLen));
                keeperAim.SetPosition(2, b - (Vector3)(perp * HudAimHalf));
            }
        }

        bool charging = show && (chargingShot || chargingPass);
        if (keeperBarBG != null) keeperBarBG.enabled = charging;
        if (keeperBar != null)
        {
            keeperBar.enabled = charging;
            if (charging)
            {
                float fill = Mathf.Clamp01(chargingShot ? currentPower / Mathf.Max(keeperShootPower, 0.0001f) : passPower);
                float half = HudBarW * 0.5f, y = c.y + HudBarY;
                keeperBarBG.SetPosition(0, new Vector3(c.x - half, y, 0f));
                keeperBarBG.SetPosition(1, new Vector3(c.x + half, y, 0f));
                keeperBar.SetPosition(0, new Vector3(c.x - half, y, 0f));
                keeperBar.SetPosition(1, new Vector3(c.x - half + HudBarW * fill, y, 0f));
                keeperBar.startColor = keeperBar.endColor = fill < 0.5f
                    ? Color.Lerp(new Color(0.2f, 1f, 0.3f), new Color(1f, 0.9f, 0.2f), fill * 2f)
                    : Color.Lerp(new Color(1f, 0.9f, 0.2f), new Color(1f, 0.25f, 0.2f), (fill - 0.5f) * 2f);
            }
        }
    }

    // Player keeper SHOOT (Task 5): release the held ball in the aim direction at the charged
    // power, then drop straight back into normal tracking (holding cleared, keeper hold ended).
    void KeeperShoot()
    {
        MatchContext ctx = MatchContext.Instance;
        holding = false;
        keeperLeftSafeZone = false;
        if (ctx != null) ctx.ClearKeeperHold();
        if (ball == null) return;

        // Task 3: fire in the human's AIM direction (live joystick, else WASD/last facing) at the
        // charged power — never auto-aimed at the goal.
        Vector2 dir = KeeperAimDir();

        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = dir * Mathf.Max(currentPower, KeeperMinShootSpeed); // a tap still fires a real shot

        if (BallFlight.Instance != null) BallFlight.Instance.NoteShot(0.5f, false); // mid-height shot
        if (ctx != null)
        {
            ctx.NoteRelease(transform);  // credit a goal to the keeper if it scores
            ctx.SetPossession(null);     // ball is live again → normal tracking resumes
        }
    }

    // Strip an enemy carrier that comes within keeperSnatchDistance — 100% success, no roll.
    // (Normal loose-ball collection keeps its slow-speed requirement; this is only for a
    // carrier driving point-blank into the keeper.) Respects an active free throw.
    bool TrySnatchFromCarrier(MatchContext ctx, TeamSide team)
    {
        if (ctx.FreeThrowActive) return false;                      // a free-throw carrier is protected
        TeamSide enemy = ctx.EnemyOf(team);
        if (enemy == null || !ctx.TeamHasBall(enemy)) return false; // only when an ENEMY holds it

        Transform carrier = ball.transform.parent;
        if (carrier == null || carrier == transform) return false;
        if (carrier.GetComponent<Goalkeeper>() != null) return false; // not from another keeper
        if (Vector2.Distance(rb.position, carrier.position) > keeperSnatchDistance) return false;

        // clear the carrier's hold, then take it ourselves (becomes a keeper hold)
        IAgentBody body = carrier.GetComponent<IAgentBody>();
        if (body != null) body.IsHolding = false;
        else { PlayerMovement pm = carrier.GetComponent<PlayerMovement>(); if (pm != null) pm.ReleaseBall(); }

        Grab(ctx, team);
        return true;
    }

    void Grab(MatchContext ctx, TeamSide team)
    {
        holding = true;
        keeperLeftSafeZone = false; // Task 5: fresh possession starts inside the safe zone
        holdStartTime = Time.time;
        lastDir = new Vector2(transform.position.x >= 0f ? -1f : 1f, 0f); // aim toward the field
        chargingShot = false; currentPower = 0f;
        chargingPass = false; passPower = 0f;
        ball.simulated = false;
        ball.linearVelocity = Vector2.zero;
        ball.transform.SetParent(transform);
        ball.transform.localPosition = Vector3.zero;
        ctx.SetKeeperHold(team);   // mark BEFORE possession so the shot clock skips its reset
        ctx.SetPossession(team);
    }

    void PassOut(float charge = 1f)
    {
        MatchContext ctx = MatchContext.Instance;
        holding = false;
        keeperLeftSafeZone = false;
        if (ctx != null) ctx.ClearKeeperHold();
        if (ball == null) return;

        TeamSide team = KeeperTeam();
        Vector2 from = transform.position;

        // TARGET SELECTION:
        //  • PLAYER keeper (manual pass, Task 3): PURELY directional — pick the teammate best
        //    aligned with the human's aim (live joystick, else WASD/last facing); NO cone. Falls
        //    back to BestPassTarget only when the keeper has no available teammate at all.
        //  • BOT keeper: unchanged — most-open advanced teammate, else the safe deep outlet.
        Transform target = null;
        if (team != null)
        {
            if (IsPlayerKeeper())
                target = FindKeeperPassTarget(team, KeeperAimDir());
            if (target == null) // bot keeper, or the player keeper had no available teammate → fall back
            {
                target = team.BestPassTarget(transform, ctx != null ? ctx.EnemyOf(team) : null, false);
                if (target == null) target = team.DeepestMember(transform);
            }
        }

        Vector2 dir;
        float dist;
        if (target != null) { dir = ((Vector2)target.position - from).normalized; dist = Vector2.Distance(from, target.position); }
        else if (team != null && team.attackGoal != null) { dir = ((Vector2)team.attackGoal.position - from).normalized; dist = 6f; }
        else { dir = Vector2.right; dist = 6f; }

        ball.transform.SetParent(null);
        ball.simulated = true;
        float maxSpeed = Mathf.Clamp(dist * 2.5f, 6f, 13f);
        ball.linearVelocity = dir * Mathf.Lerp(6f, maxSpeed, Mathf.Clamp01(charge)); // charge scales the pass
        if (ctx != null) ctx.SetPossession(null);
        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock(); // distribution = fresh 30
    }

    // The human's current AIM (Task 3): the LIVE joystick if it's being pushed (touch), else the
    // keeper's facing (WASD / last stick direction). Used for BOTH the charged shot and the pass so
    // the keeper always acts where the player points — never auto-aimed at a teammate or the goal.
    Vector2 KeeperAimDir()
    {
        Vector2 stick = TouchControls.Instance != null ? TouchControls.Instance.JoystickAxis : Vector2.zero;
        if (stick.magnitude >= 0.1f) return stick.normalized;                  // touch: live joystick
        if (lastDir.sqrMagnitude > 1e-4f) return lastDir.normalized;           // keyboard / last facing
        return new Vector2(transform.position.x >= 0f ? -1f : 1f, 0f);         // default: toward the field
    }

    // The teammate the player keeper is aiming at (Task 3): PURELY directional, NO cone. Every
    // teammate is scored by alignment with the aim minus a small distance penalty (dot − dist×0.05);
    // the best-scoring one wins. Returns null only when there is no available teammate (excluded
    // slots are null) — the caller then falls back to BestPassTarget.
    Transform FindKeeperPassTarget(TeamSide team, Vector2 aimDir)
    {
        if (team == null || team.members == null) return null;
        Vector2 myPos = transform.position;
        Transform best = null;
        float bestScore = float.NegativeInfinity;

        foreach (Transform m in team.members)
        {
            if (m == null || m == transform) continue; // excluded slots are null; never aim at self
            Vector2 to = (Vector2)m.position - myPos;
            float dist = to.magnitude;
            if (dist < 1e-4f) continue;

            float score = Vector2.Dot(aimDir, to / dist) - dist * 0.05f; // aligned + nearer wins, no cone
            if (score > bestScore) { bestScore = score; best = m; }
        }
        return best;
    }

    // Task 5: called by a stealer (player or bot) that successfully rips the ball off this keeper
    // AFTER it left its safe zone. Clears our hold state only — the stealer re-parents the ball and
    // sets possession; the keeper then swims back to its line via the normal not-holding path.
    public void OnBallStolen()
    {
        holding = false;
        keeperLeftSafeZone = false;
        chargingShot = false; currentPower = 0f;
        chargingPass = false; passPower = 0f;
        if (MatchContext.Instance != null) MatchContext.Instance.ClearKeeperHold();
    }
}
