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

    [Header("Shot reaction")]
    [SerializeField] private float shotSpeedThreshold = 4f;      // loose ball faster than this = a shot
    [SerializeField] private float highShotReactionDelay = 0.2f; // extra freeze vs a HIGH (charge > 0.7) shot

    [Header("Grab & control (Part 1)")]
    [SerializeField] private float keeperGrabDistance = 1.2f;  // collect a loose ball within this
    [SerializeField] private float keeperGrabMaxSpeed = 3f;    // ...only if it's moving slower than this
    [SerializeField] private float keeperSnatchDistance = 0.8f;// strip an enemy carrier this close — 100%, no roll
    [SerializeField] private float keeperHoldSeconds = 0.8f;   // bot keeper auto-distributes after this
    [SerializeField] private float keeperPanicDistance = 2.5f; // bot keeper distributes NOW if an opponent is this close
    [SerializeField] private float holdOffset = 0.5f;          // held ball sits this far toward the field

    // ---- player control (Task 5): when the HUMAN's own keeper holds the ball it plays like a
    //      field swimmer — Y-only movement along the line, sprint, a charged shot, and a pass. ----
    [Header("Player control (Task 5)")]
    [SerializeField] private float keeperMoveSpeed = 1.6f;        // free-roam speed while the human holds the ball
    [SerializeField] private float keeperShootPower = 12f;        // max charged shot speed
    [SerializeField] private float keeperChargeRate = 18f;        // shot charge gained per second (fast wind-up)
    [SerializeField] private float keeperSprintMultiplier = 1.8f; // move speed boost while sprinting

    const float KeeperRoamY = 3.5f;        // how far up/down the pool a controlled keeper may roam
    const float KeeperMinShootSpeed = 8f;  // a keeper shot tap still travels (never a limp drop)

    private Rigidbody2D rb;
    private float homeX;        // the keeper's goal-line X — it returns here after a shot/pass/roam
    private bool holding;
    private float holdStartTime;
    private bool shotIncoming;              // edge-detects a NEW incoming shot
    private float reactBlockedUntil = -10f; // high-shot reaction-delay window

    private Vector2 lastDir = Vector2.left; // aim/facing for a keeper shot (set on grab)
    private float currentPower;             // current shot charge (0..keeperShootPower)
    private bool chargingShot;

    // touch input mirrored from TouchControls while the human controls this keeper
    private Vector2 touchAxis;
    private bool touchShootHeld, touchShootDown, touchShootUp;
    private bool touchPassDown;
    private bool touchSprintHeld;

    void Awake() { rb = GetComponent<Rigidbody2D>(); homeX = transform.position.x; }

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
            ClearKeeperTouch();
            return;
        }

        // aim/facing from keyboard + touch (the body only ever moves on Y, in FixedUpdate)
        Vector2 inDir = new Vector2(Input.GetAxisRaw("Horizontal") + touchAxis.x,
                                    Input.GetAxisRaw("Vertical") + touchAxis.y);
        if (inDir.sqrMagnitude > 0.01f) lastDir = inDir.normalized;

        // PASS (B / touch PASS): distribute to the best open teammate — same as PassOut().
        if (Input.GetKeyDown(KeyCode.B) || touchPassDown)
        {
            chargingShot = false; currentPower = 0f;
            PassOut();
            return;
        }

        // SHOOT (Space / touch SHOOT): charge while held, fire in the aim direction on release.
        if (!chargingShot && (Input.GetKeyDown(KeyCode.Space) || touchShootDown))
            chargingShot = true;
        if (chargingShot)
        {
            if (Input.GetKey(KeyCode.Space) || touchShootHeld)
                currentPower = Mathf.Min(currentPower + keeperChargeRate * Time.deltaTime, keeperShootPower);
            if (Input.GetKeyUp(KeyCode.Space) || touchShootUp)
            {
                KeeperShoot();
                currentPower = 0f;
                chargingShot = false;
            }
        }
    }

    void FixedUpdate()
    {
        if (ball == null || rb == null) return;
        MatchContext ctx = MatchContext.Instance;

        if (holding) { HoldTick(ctx); return; }

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

        // Always slide back onto the goal line in X (a player keeper may have roamed with the
        // ball; once released it swims home). Track the ball in Y unless a shot reaction / skip
        // fake has frozen us.
        Vector2 pos = rb.position;
        pos.x = Mathf.MoveTowards(pos.x, homeX, trackSpeed * Time.fixedDeltaTime);
        if (Time.time >= reactBlockedUntil && !fooled)
        {
            float targetY = Mathf.Clamp(ball.position.y, minY, maxY);
            pos.y = Mathf.MoveTowards(pos.y, targetY, trackSpeed * Time.fixedDeltaTime);
        }
        rb.MovePosition(pos);

        TeamSide team = KeeperTeam();

        // SNATCH (Task 5): an enemy carrier point-blank on the keeper (within
        // keeperSnatchDistance) is stripped with 100% success — no probability roll.
        if (ctx != null && team != null && TrySnatchFromCarrier(ctx, team)) return;

        // collect a slow, loose, nearby ball
        if (ctx != null && team != null && ctx.BallGrabbable && ctx.CanGrab(team) &&
            Vector2.Distance(rb.position, ball.position) <= keeperGrabDistance &&
            ball.linearVelocity.magnitude < keeperGrabMaxSpeed)
        {
            Grab(ctx, team);
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
        // lost the ball (e.g. a shot-clock turnover detached it) → clear our state
        if (ball.transform.parent != transform)
        {
            holding = false;
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
            if (Input.GetKey(KeyCode.LeftShift) || touchSprintHeld) speed *= keeperSprintMultiplier;

            Vector2 p = rb.position + move * (speed * Time.fixedDeltaTime);
            float limitX = ctx != null ? ctx.PlayerLimitX : 6.9f;
            // homeX (the goal line) is the OUTER bound; the opposite player limit is the inner one
            p.x = Mathf.Clamp(p.x, homeX > 0f ? -limitX : homeX, homeX > 0f ? homeX : limitX);
            p.y = Mathf.Clamp(p.y, -KeeperRoamY, KeeperRoamY);
            rb.MovePosition(p);
        }

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
                              bool passDown, bool sprintHeld)
    {
        touchAxis = axis;
        touchShootHeld = shootHeld;
        touchShootDown = shootDown;
        touchShootUp = shootUp;
        touchPassDown = passDown;
        touchSprintHeld = sprintHeld;
    }

    void ClearKeeperTouch()
    {
        touchAxis = Vector2.zero;
        touchShootHeld = touchShootDown = touchShootUp = false;
        touchPassDown = false;
        touchSprintHeld = false;
    }

    // Player keeper SHOOT (Task 5): release the held ball in the aim direction at the charged
    // power, then drop straight back into normal tracking (holding cleared, keeper hold ended).
    void KeeperShoot()
    {
        MatchContext ctx = MatchContext.Instance;
        holding = false;
        if (ctx != null) ctx.ClearKeeperHold();
        if (ball == null) return;

        Vector2 dir = lastDir.sqrMagnitude > 1e-4f
            ? lastDir.normalized
            : new Vector2(transform.position.x >= 0f ? -1f : 1f, 0f); // default: toward the field

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
        holdStartTime = Time.time;
        lastDir = new Vector2(transform.position.x >= 0f ? -1f : 1f, 0f); // aim toward the field
        chargingShot = false; currentPower = 0f;
        ball.simulated = false;
        ball.linearVelocity = Vector2.zero;
        ball.transform.SetParent(transform);
        ball.transform.localPosition = Vector3.zero;
        ctx.SetKeeperHold(team);   // mark BEFORE possession so the shot clock skips its reset
        ctx.SetPossession(team);
    }

    void PassOut()
    {
        MatchContext ctx = MatchContext.Instance;
        holding = false;
        if (ctx != null) ctx.ClearKeeperHold();
        if (ball == null) return;

        TeamSide team = KeeperTeam();
        Vector2 from = transform.position;

        // most-open advanced teammate (reuse the pass-target logic); else the safe deep outlet
        Transform target = null;
        if (team != null)
        {
            target = team.BestPassTarget(transform, ctx != null ? ctx.EnemyOf(team) : null, false);
            if (target == null) target = team.DeepestMember(transform);
        }

        Vector2 dir;
        float dist;
        if (target != null) { dir = ((Vector2)target.position - from).normalized; dist = Vector2.Distance(from, target.position); }
        else if (team != null && team.attackGoal != null) { dir = ((Vector2)team.attackGoal.position - from).normalized; dist = 6f; }
        else { dir = Vector2.right; dist = 6f; }

        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = dir * Mathf.Clamp(dist * 2.5f, 6f, 13f); // same clamp as a normal pass
        if (ctx != null) ctx.SetPossession(null);
        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock(); // distribution = fresh 30
    }
}
