using System.Collections;
using UnityEngine;

// Flight effects for the ball (auto-added to the Ball by PlayerMovement — no wiring).
// Tracks the "fake third dimension" of a shot/pass in this top-down game:
//  - SKIP SHOT (Q+Space): a fast LOW shot bounces 1.5 units in front of the goal line
//    — small random Y deflection, squash + water ripple at the bounce, and a 35%
//    chance the keeper is fooled (Goalkeeper + GoalkeeperAnimator stop reacting).
//  - THE ARC (every airborne ball): ALL passes and HIGH shots leave the water. The
//    rigidbody flies a straight zero-damping line at constant speed with its colliders OFF
//    (nothing can deflect it — not players, keepers, walls or the goal trigger) while the
//    SPRITE is drawn offset upward over a water shadow that SHRINKS as the ball rises.
//    Three ArcKinds, three silhouettes:
//      • Pass (B / every bot pass) — a small, quick SYMMETRIC hop (toned-down lob):
//        peaks at the midpoint of the travel distance, subtle swell, no spin.
//      • Lob (F+B / a bot's long or blocked-lane ball) — the big floaty SYMMETRIC arc.
//      • Shot (charge > 0.7) — an ASYMMETRIC dart: steep fast rise into an early peak
//        (~35% of the flight), a slow hang near the top, then a sharp drop. Faster than
//        any pass, glows on release, and gets the release scale-snap below.
//    While airborne the ball is untouchable: MatchContext.BallGrabbable is false, so no
//    grab / steal / keeper save can take it — until it lands (flight timer completes at
//    the landing point), where it becomes a perfectly normal grounded ball again (a landed
//    SHOT keeps its full speed for the keeper to face; a landed PASS keeps a soft roll).
//  - RELEASE SNAP (shots only, every shot type): a ~0.12s squash-then-pop scale punch on
//    the ball at the instant of release, so a shot reads as a shot with no HUD.
//  - HIGH SHOT fallback (point-blank charge > 0.7, no room for an arc): ball swells and
//    glows briefly, shrinking back as it nears the goal — the classic flat high shot.
// Plus a speed-gated motion trail and spin only above 6 u/s (snaps upright on a catch).
// All scaling is uniform and recomputed from a clean base each frame, so it never drifts or
// stretches even when the ball is re-parented onto a non-uniformly-scaled swimmer.
public class BallFlight : MonoBehaviour
{
    public static BallFlight Instance { get; private set; }

    // The three airborne silhouettes. Pass/Lob share the symmetric parabola (different
    // heights); Shot uses its own asymmetric curve (steep rise, hang, sharp drop).
    public enum ArcKind { Pass, Lob, Shot }

    // ---- gameplay ----
    const float GoalLineX = 7f;             // matches GoalLineOut
    const float BounceBeforeGoal = 1.5f;    // skip bounce point distance from the goal line
    const float BounceYJitter = 0.3f;       // ± random Y reflection at the bounce
    const float FoolKeeperChance = 0.35f;   // skip shots fool the keeper this often
    const float LowHeightMax = 0.3f;        // only LOW shots can skip
    const float DeadSpeed = 1f;             // slower than this = the flight is over

    // ---- the arc (all airborne balls) ----
    // The old "< 2u throws stay flat" point-blank exception is GONE (Tasks 1 & 2): EVERY pass and
    // EVERY shot now arcs. Only a degenerate near-zero-distance launch is refused (it would be a
    // 0/0 direction), and short Pass/Lob throws are held airborne at least MinFlightTime so their
    // arc reads as an arc instead of a 2-frame blip.
    const float MinHighBallDistance = 0.05f; // refuse ONLY a zero-distance (degenerate) throw
    const float MinFlightTime = 0.32f;       // Pass/Lob: shortest airtime, so even a tiny throw reads as an arc
    const float MaxFlightTime = 2.5f;        // hard safety cap on any single flight
    const float LandMaxX = 6.4f;            // landings are pulled back inside the goal lines...
    const float LandMaxY = 3.9f;            // ...and inside the top/bottom walls (open water only)
    const float LandRollFactor = 0.25f;     // a landed PASS keeps this speed fraction (shots keep 100%)
    const int   AirSortingBoost = 30;       // the airborne sprite draws OVER the swimmers
    const float LandingProbeRadius = 0.5f;  // who did we land on? (collision-ignored until separated)
    const float LandingSeparationMax = 1.5f;// give-up cap for that separation wait

    // ---- physical goal response (grounded/live ball only) ----
    // Arc colliders deliberately remain OFF overhead. Every shot arc lands before the frame,
    // so the collider is live again when it can legitimately hit a post or the outer net.
    const float GoalCollisionMinSpeed = 1f;
    const float PostVelocityRetention = 0.72f; // rigid frame: a lively but controlled rebound
    const float NetVelocityRetention = 0.22f;  // net: catches most of the pace, with a soft kick-back

    // Per-kind arc profiles: peak height per unit of ground distance (clamped), the swell at
    // the peak, and the shadow's on-water size. Pass = a toned-down lob (small quick hop);
    // Lob = the original big floaty ball; Shot = a lower dart with the asymmetric curve.
    // Pass floor RAISED (Task 1): even the shortest / weakest pass now hops a clearly visible
    // ~0.4u (was 0.18u — read as flat). Still scales up with distance, still stays under the Lob.
    const float PassPeakPerUnit = 0.08f,  PassPeakMin = 0.4f,  PassPeakMax = 0.6f;
    const float LobPeakPerUnit  = 0.14f,  LobPeakMin  = 0.45f, LobPeakMax  = 1.25f;
    // Shot floor LOWERED (Task 2) so a weak charge can scale its arc DOWN toward it — but never
    // to flat; ShotChargeMinScale is the smallest fraction of the distance-peak a min-charge shot keeps.
    const float ShotPeakPerUnit = 0.10f,  ShotPeakMin = 0.2f,  ShotPeakMax = 0.9f;
    const float ShotChargeMinScale = 0.3f;
    const float PassSwellMax = 1.08f, LobSwellMax = 1.2f, ShotSwellMax = 1.15f;
    const float PassShadowGround = 0.55f, LobShadowGround = 0.75f, ShotShadowGround = 0.6f;
    const float ShotArcPeakT = 0.35f;       // the shot curve peaks this far into the flight

    // ---- release snap (shots only): squash → pop → settle, ~0.12s, raw (not eased) ----
    const float SnapSeconds = 0.12f;
    const float SnapSquashScale = 0.84f;    // instant squash on release...
    const float SnapPopScale = 1.12f;       // ...popping past normal before settling back to 1

    // ---- trail ----
    const float TrailMinSpeed = 5f;         // emit only above this speed
    const float TrailTime = 0.15f;
    const float TrailStartWidth = 0.3f;
    static readonly Color TrailStartColor = new Color(1f, 1f, 0.6f, 1f); // white-yellow
    static readonly Color TrailEndColor = new Color(1f, 1f, 1f, 0f);     // → transparent

    // ---- spin (per flight type) — Task 3: spin ONLY above 6 u/s, every rate cut 70%,
    //      normal passes never spin, and a catch snaps the ball upright instantly ----
    const float SpinMinSpeed = 6f;        // below this the ball doesn't spin AT ALL
    const float ShotSpinSpeed = 8f;       // flight faster than this counts as a SHOT
    const float ShotSpinDegPerSec = 54f;  // shots spin (was 180; -70%)
    const float PassSpinDegPerSec = 18f;  // a fast loose ball (was 60; -70%)
    const float LobSpinDegPerSec = 9f;    // the arcing high ball barely spins (was 30; -70%)

    // ---- scale (Task 2: UNIFORM only — X always == Y — capped at 1.2x, Lerp-smoothed) ----
    const float MaxBallScale = 1.2f;      // hard cap on EVERY ball-scale effect
    const float ScaleLerpSpeed = 10f;     // how fast the visual scale eases to its target (no snaps)

    // ---- flat high-shot fallback swell ----
    const float HighShotMaxScale = 1.2f;
    const float HighShotSwellSeconds = 0.3f;  // smooth scale-up time after release
    const float HighShotShrinkDistance = 2f;  // back to 1x inside this of the goal line
    const float GlowSeconds = 0.2f;
    static readonly Color GlowColor = new Color(1f, 1f, 0.6f, 1f);

    // ---- settle ripple (2026-07-20): a loose ball resting on the water ----
    // Reuses the established fast-loose-ball settle trigger. The supplied 3x3 sheet plays an
    // impact collapse (frames 9 -> 7) once, then loops its small resting ripple (frames 1 -> 3)
    // until the ball is claimed or moves again.
    const float SettleArmSpeed = 2.5f;     // loose + faster than this = armed
    const float SettleTriggerSpeed = 1f;   // armed + slower than this = the splash
    const float SettleMaxX = 6f;           // no splash this close to a goal line (net area)
    const float SettleImpactFrameSeconds = 0.07f;
    const float SettleIdleFrameSeconds = 0.18f;
    private bool settleRippleEnabled = false; // art pass disabled at dev request (2026-07-20c)

    // ---- skip bounce ----
    const float SquashSeconds = 0.1f;
    const float BounceScale = 0.9f; // brief UNIFORM impact pulse (never a stretch); was a non-uniform squash
    // ---- arc shadow (ball rises → shadow shrinks; that contrast IS the height read) ----
    // The on-water (launch/landing) size is per-kind — see *ShadowGround above.
    const float ShadowPeakSize = 0.4f;       // at the arc peak (ball big + shadow small = high)
    const float ShadowOvalY = 0.6f;          // squashed into a flat oval on the water
    static readonly Color ShadowTint = new Color(0.1f, 0.1f, 0.2f); // dark blue-grey
    static readonly Vector3 ShadowOffset = new Vector3(0.06f, -0.18f, 0f);

    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Transform shadow;
    private SpriteRenderer shadowSr;
    private SpriteRenderer airSr;          // the airborne ball sprite (the root sprite hides mid-flight)
    private TrailRenderer trail;
    private Collider2D[] ballCols;         // the ball's own colliders (dropped while airborne)
    private Color baseColor = Color.white; // the ball's real tint (it's yellow, not white)
    private bool glowDirty;                // a glow tint needs restoring when it expires

    private bool skipActive;
    private float shotHeight = 0.5f;
    private bool bounced;
    private float squashUntil = -10f;

    private bool highShotActive;           // flat point-blank fallback only — the arc replaces it
    private float shotStartTime;
    private float glowUntil = -10f;

    // arc flight state (straight ground line from → to at constant speed)
    private bool highBallActive;
    private ArcKind highBallKind;
    private Vector2 highBallFrom;
    private Vector2 highBallTo;
    private float highBallSpeed;
    private float highBallStart;
    private float highBallFlightTime;
    private float highBallPeak;            // arc peak height (world units)
    private float highBallSwellMax;        // sprite swell at the peak (per-kind)
    private float highBallShadowGround;    // shadow's on-water size (per-kind)
    private float arc01;                   // THIS frame's normalized arc height (0..1)
    private float storedDamping;           // ball's authored linearDamping (zeroed in flight)
    private bool physicsSuppressed;
    private Vector2 prePhysicsVelocity;     // velocity entering the current physics step
    private float snapStart = -10f;        // release-snap start time (shots only)

    private bool passActive; // a plain pass: NO scale change and NO trail (gentle spin only)

    private bool settleArmed; // loose ball moved fast → the next slow-down splashes once
    private Sprite[] settleRippleFrames;
    private SpriteRenderer settleRippleSr;
    private SettleRipplePhase settleRipplePhase;
    private int settleRippleFrame;
    private float settleRippleFrameStarted;
    private bool settleRippleVerificationLogged;

    private enum SettleRipplePhase { Hidden, Impact, Idle }

    private float baseScale = 1f;    // the ball's authored (uniform) localScale, captured in Awake
    private float scaleFactor = 1f;  // current uniform scale factor (1 = normal); eased every frame

    public bool KeeperFooled { get; private set; }

    // Height (0..1) of the current shot's flight (low < 0.3, high > 0.7). Read by Goalkeeper
    // to weight its save chance against high shots — a landed high ball keeps its value.
    public float ShotHeight => shotHeight;

    // Skip-shot flight state (read by WaterPoloBrain so AI can't intercept a skip mid-air).
    public bool SkipActive => skipActive;
    public bool SkipBounced => bounced;

    // True while a high ball is overhead. MatchContext.BallGrabbable reads this: an airborne
    // ball can NOT be grabbed, stolen, saved or picked off by ANYONE until it lands.
    public bool HighBallActive => highBallActive;
    public bool SettleRippleActive => settleRipplePhase != SettleRipplePhase.Hidden;

    void Awake()
    {
        Instance = this;
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) baseColor = sr.color;
        baseScale = transform.localScale.x; // ball is authored uniform (0.1) — scale never drifts now
        ballCols = GetComponents<Collider2D>();
        BuildShadow();
        BuildAirSprite();
        BuildTrail();
        if (settleRippleEnabled) BuildSettleRipple();
    }

    // Soft shadow pinned to the water surface under the ball — the ball's own circle
    // sprite tinted dark blue-grey, one sorting order below it. Shown only during a
    // high-ball flight; size/alpha breathe with the arc (UpdateShadow).
    void BuildShadow()
    {
        GameObject go = new GameObject("BallShadow");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = ShadowOffset;
        shadowSr = go.AddComponent<SpriteRenderer>();
        if (sr != null)
        {
            shadowSr.sprite = sr.sprite;
            shadowSr.sortingOrder = sr.sortingOrder - 1;
        }
        shadowSr.color = new Color(ShadowTint.r, ShadowTint.g, ShadowTint.b, 0.5f);
        shadow = go.transform;
        go.SetActive(false);
    }

    // The sprite shown while the ball is AIRBORNE: a copy of the ball's own sprite, drawn
    // offset upward along the arc and sorted above the swimmers. The root sprite (pinned to
    // the physics body at water level) hides while this is on, so there's only ever one ball
    // visible. Parented to the ball so it inherits the flight spin and the swell for free.
    void BuildAirSprite()
    {
        GameObject go = new GameObject("BallAirSprite");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        airSr = go.AddComponent<SpriteRenderer>();
        if (sr != null)
        {
            airSr.sprite = sr.sprite;
            airSr.color = sr.color;
            airSr.sortingOrder = sr.sortingOrder + AirSortingBoost;
        }
        airSr.enabled = false;
    }

    // Motion trail for any fast flight: white-yellow fading to nothing over 0.15s.
    // Emission is speed-gated in Update; Clear() makes it vanish instantly on a catch.
    void BuildTrail()
    {
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = TrailTime;
        trail.startWidth = TrailStartWidth;
        trail.endWidth = 0f;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = TrailStartColor;
        trail.endColor = TrailEndColor;
        trail.sortingOrder = sr != null ? sr.sortingOrder - 2 : 0; // under ball AND shadow
        trail.emitting = false;
    }

    // BallFlight is auto-added at runtime, so the sheet is supplied through a Resources asset
    // rather than a scene-only Inspector reference. The effect itself must stay in WORLD space:
    // the Ball is authored at 0.04 scale, which would make a parented 1.76u sheet invisible.
    void BuildSettleRipple()
    {
        BallDropRippleFrameSet frameSet = Resources.Load<BallDropRippleFrameSet>("BallDropRippleFrameSet");
        if (frameSet == null || frameSet.frames == null || frameSet.frames.Length < 9)
        {
            Debug.LogError("[BallDropRipple VERIFY] Frame set load FAILED: " +
                "Resources/BallDropRippleFrameSet is missing or has fewer than 9 frames.");
            return;
        }

        settleRippleFrames = frameSet.frames;
        GameObject go = new GameObject("BallDropRipple");
        go.transform.position = transform.position;
        go.transform.localScale = Vector3.one;
        settleRippleSr = go.AddComponent<SpriteRenderer>();
        if (sr != null) settleRippleSr.sortingLayerID = sr.sortingLayerID;
        settleRippleSr.sortingOrder = sr != null ? sr.sortingOrder + 1 : 1;
        settleRippleSr.enabled = false;
        Debug.Log($"[BallDropRipple VERIFY] Loaded frame set='{frameSet.name}', frames={settleRippleFrames.Length}, " +
            $"frame1='{settleRippleFrames[0].name}' texture='{settleRippleFrames[0].texture.name}', " +
            $"frame9='{settleRippleFrames[8].name}' texture='{settleRippleFrames[8].texture.name}', " +
            $"ballWorldScale={transform.lossyScale}, rippleWorldScale={go.transform.lossyScale}, " +
            $"sortingLayer={settleRippleSr.sortingLayerID}, sortingOrder={settleRippleSr.sortingOrder}.");
    }

    // Called on every FLAT shot release (skip / mid / point-blank high; human, bot or keeper).
    public void NoteShot(float height, bool skip)
    {
        EndFlight();
        shotHeight = height;
        shotStartTime = Time.time;
        skipActive = skip;
        TriggerReleaseSnap(); // every shot gets the release punch; passes never do

        if (!skip && height > 0.7f)
        {
            // point-blank high shot (the arc was refused): swell + a brief warm glow
            highShotActive = true;
            glowUntil = Time.time + GlowSeconds;
            glowDirty = true;
        }

        if (!skipActive || rb == null) return;

        // point-blank (already past the bounce point) or no X direction → nothing to skip
        float vx = rb.linearVelocity.x;
        if (Mathf.Abs(vx) < 0.01f ||
            Mathf.Sign(vx) * rb.position.x >= GoalLineX - BounceBeforeGoal)
            skipActive = false;
    }

    // Called by PlayerMovement/WaterPoloBrain on a NORMAL pass: a plain throw with no
    // height/skip effects. Suppresses the swell AND the motion trail (the "bridge"
    // streak) for the whole flight; spin drops to the gentle pass rate.
    public void NotePass()
    {
        EndFlight();      // clear any prior flight + reset scale
        passActive = true;
    }

    // Launch the ball on an ARC: an untouchable airborne ball from its current spot to
    // landPos, shaped by `kind` (Pass = small quick hop, Lob = big floaty ball, Shot = fast
    // asymmetric dart). Callers invoke this with the ball still parented at the hand, then
    // just un-parent — the flight owns the physics from here (simulated, zero damping,
    // colliders off, straight constant-speed ground line; no release-collision window needed
    // since nothing collides). Returns false — with NO side effects — when the throw is too
    // short for a real arc; the caller then releases flat exactly as before.
    public bool LaunchHighBall(Vector2 landPos, float groundSpeed, float height01, ArcKind kind)
    {
        if (rb == null) return false;
        Vector2 from = rb.transform.position;   // the held ball is pinned at the hand
        landPos = ClampLanding(from, landPos);
        float dist = Vector2.Distance(from, landPos);
        if (dist < MinHighBallDistance || groundSpeed < 1f) return false;

        EndFlight(); // clear any prior flight state/visuals first

        highBallActive = true;
        highBallKind = kind;
        highBallFrom = from;
        highBallTo = landPos;
        highBallStart = Time.time;

        // Flight time & speed. Shots keep their full pace (dist / groundSpeed). A very short
        // Pass/Lob is SLOWED to MinFlightTime so its arc stays airborne long enough to READ as an
        // arc — the ball still lands exactly at landPos (speed = dist / flightTime).
        float flightTime = dist / groundSpeed;
        float launchSpeed = groundSpeed;
        if (kind != ArcKind.Shot && flightTime < MinFlightTime)
        {
            flightTime = MinFlightTime;
            launchSpeed = dist / flightTime;
        }
        highBallSpeed = launchSpeed;
        highBallFlightTime = Mathf.Min(flightTime, MaxFlightTime);
        switch (kind)
        {
            case ArcKind.Pass:
                highBallPeak = Mathf.Clamp(dist * PassPeakPerUnit, PassPeakMin, PassPeakMax);
                highBallSwellMax = PassSwellMax;
                highBallShadowGround = PassShadowGround;
                break;
            case ArcKind.Lob:
                highBallPeak = Mathf.Clamp(dist * LobPeakPerUnit, LobPeakMin, LobPeakMax);
                highBallSwellMax = LobSwellMax;
                highBallShadowGround = LobShadowGround;
                break;
            default: // Shot — peak scales with distance AND charge (height01): a weak tap hops
                     // low, a full charge arcs high; floored at ShotPeakMin so even the faintest
                     // shot has a clearly visible hop — never flat (Task 2).
                float shotChargeScale = Mathf.Lerp(ShotChargeMinScale, 1f, Mathf.Clamp01(height01));
                highBallPeak = Mathf.Clamp(dist * ShotPeakPerUnit * shotChargeScale, ShotPeakMin, ShotPeakMax);
                highBallSwellMax = ShotSwellMax;
                highBallShadowGround = ShotShadowGround;
                break;
        }
        shotHeight = height01;       // the keeper's save penalty reads this once a shot lands
        shotStartTime = Time.time;

        // The ball is IN THE AIR: colliders off (players, keepers, walls and the goal trigger
        // can't touch it) and zero damping, so the body flies the ground line at CONSTANT
        // speed — the timer lands it exactly at landPos. Collider-off also means the release
        // can never deflect off the thrower's own body, so no IgnoreReleaseCollision window
        // is needed.
        SuppressBallPhysics();
        rb.simulated = true;
        rb.linearVelocity = (landPos - from) / dist * launchSpeed;

        if (kind == ArcKind.Shot)
        {
            glowUntil = Time.time + GlowSeconds; glowDirty = true; // release cue
            TriggerReleaseSnap();                                  // shots snap, passes don't
        }

        if (shadow != null) shadow.gameObject.SetActive(true);
        if (airSr != null)
        {
            if (sr != null) { airSr.sprite = sr.sprite; airSr.color = sr.color; sr.enabled = false; }
            airSr.enabled = true;
        }
        return true;
    }

    // Landings always end up in open water: pull the landing point back ALONG the throw line
    // until it's inside the goal lines and the top/bottom walls — the airborne ball ignores
    // all physics, so nothing else keeps it in the pool.
    static Vector2 ClampLanding(Vector2 from, Vector2 land)
    {
        Vector2 d = land - from;
        float t = 1f;
        if (Mathf.Abs(land.x) > LandMaxX && Mathf.Abs(d.x) > 1e-4f)
            t = Mathf.Min(t, (Mathf.Sign(d.x) * LandMaxX - from.x) / d.x);
        if (Mathf.Abs(land.y) > LandMaxY && Mathf.Abs(d.y) > 1e-4f)
            t = Mathf.Min(t, (Mathf.Sign(d.y) * LandMaxY - from.y) / d.y);
        return from + d * Mathf.Clamp01(t);
    }

    // Shared with AI pass prediction so selection and the real launch use the same pool clamp
    // and airtime floor/cap. These are calculations only; they do not start or alter a flight.
    public static Vector2 ClampLandingPoint(Vector2 from, Vector2 land)
    {
        return ClampLanding(from, land);
    }

    public static float EstimateFlightTime(Vector2 from, Vector2 land, float groundSpeed, ArcKind kind)
    {
        land = ClampLanding(from, land);
        float dist = Vector2.Distance(from, land);
        if (dist < MinHighBallDistance || groundSpeed < 1f) return 0f;

        float flightTime = dist / groundSpeed;
        if (kind != ArcKind.Shot) flightTime = Mathf.Max(flightTime, MinFlightTime);
        return Mathf.Min(flightTime, MaxFlightTime);
    }

    void SuppressBallPhysics()
    {
        if (physicsSuppressed || rb == null) return;
        physicsSuppressed = true;
        storedDamping = rb.linearDamping;
        rb.linearDamping = 0f;
        if (ballCols != null)
            foreach (Collider2D c in ballCols) if (c != null) c.enabled = false;
    }

    void RestoreBallPhysics()
    {
        if (!physicsSuppressed || rb == null) return;
        physicsSuppressed = false;
        rb.linearDamping = storedDamping;
        if (ballCols != null)
            foreach (Collider2D c in ballCols) if (c != null) c.enabled = true;
    }

    void Update()
    {
        if (skipActive || highBallActive || highShotActive || passActive) CheckFlight();
        arc01 = HighBallArc01();
        UpdateSpin();
        UpdateTrail();
        UpdateSettleRipples();
        ApplyVisuals();
    }

    void FixedUpdate()
    {
        // OnCollisionEnter2D runs after the solver may already have removed the velocity into a
        // zero-bounce collider. Preserve the incoming velocity so posts can reflect the real shot
        // rather than the solver's already-flattened result.
        prePhysicsVelocity = rb != null && rb.simulated && transform.parent == null
            ? rb.linearVelocity : Vector2.zero;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null || collision.collider == null || rb == null || !rb.simulated ||
            transform.parent != null || highBallActive)
            return;

        Collider2D hit = collision.collider;
        if (hit.isTrigger) return; // GoalLine is scoring-only; it must never deflect the ball.

        Goal goal = hit.GetComponentInParent<Goal>();
        if (goal == null) return;  // swimmers and pool boundaries keep their established response.

        bool post = hit is BoxCollider2D && hit.transform != goal.transform;
        bool net = hit is EdgeCollider2D || hit.transform == goal.transform;
        if (!post && !net) return;

        // A validated goal owns the ball from that scoring frame onward. Unity may deliver the
        // already-generated solid-net callback later in the same physics step; never let that
        // stale callback overwrite the scored-ball absorption with a 22% rebound toward play.
        if (net && ScoreManager.Instance != null && ScoreManager.Instance.GoalRestartInProgress)
            return;

        Vector2 incoming = prePhysicsVelocity;
        if (incoming.sqrMagnitude < GoalCollisionMinSpeed * GoalCollisionMinSpeed) return;

        Vector2 normal;
        Vector2 impactPoint;
        if (collision.contactCount > 0)
        {
            ContactPoint2D contact = collision.GetContact(0);
            normal = contact.normal;
            impactPoint = contact.point;
        }
        else
        {
            Vector2 closest = hit.ClosestPoint(rb.position);
            impactPoint = closest;
            normal = rb.position - closest;
            if (normal.sqrMagnitude < 1e-6f) normal = -incoming;
            normal.Normalize();
        }

        // Contact-normal orientation can differ with callback ownership. Reflection only needs
        // the normal facing against travel, so normalize that relationship explicitly.
        if (Vector2.Dot(incoming, normal) > 0f) normal = -normal;

        float retention = post ? PostVelocityRetention : NetVelocityRetention;
        Vector2 deflected = Vector2.Reflect(incoming, normal) * retention;
        rb.linearVelocity = deflected;
        prePhysicsVelocity = deflected;

        // The same ScoreManager-owned material effect handles scoring and non-scoring outer-net
        // contacts. This reports presentation only; it cannot award a goal or alter restart flow.
        if (net) goal.NotifyNetHit(impactPoint, incoming.magnitude);
    }

    // Start the two-phase ripple exactly once when a fast loose ball slows to a float. Held,
    // airborne, moving, physics-off, and frozen balls stop and hide it immediately.
    void UpdateSettleRipples()
    {
        if (!settleRippleEnabled) return;
        bool loose = transform.parent == null && rb != null && rb.simulated;
        MatchContext ctx = MatchContext.Instance;
        if (!loose || highBallActive || (skipActive && !bounced) ||
            (ctx != null && ctx.CompetitivePlayStopped))
        {
            settleArmed = false;
            StopSettleRipple();
            return;
        }

        float speed = rb.linearVelocity.magnitude;
        if (speed > SettleTriggerSpeed)
        {
            if (speed > SettleArmSpeed) settleArmed = true;
            StopSettleRipple();
            return;
        }
        if (!settleArmed) return;

        settleArmed = false;
        if (Mathf.Abs(transform.position.x) > SettleMaxX) return; // never splash inside the net area
        StartSettleRipple();
    }

    void StartSettleRipple()
    {
        if (settleRippleSr == null || settleRippleFrames == null || settleRippleFrames.Length < 9)
        {
            Debug.LogError("[BallDropRipple VERIFY] Settle trigger fired but no valid BallDropRipple renderer/frame list exists.");
            return;
        }

        settleRipplePhase = SettleRipplePhase.Impact;
        settleRippleFrame = 8; // frame 9: largest impact ring
        settleRippleFrameStarted = Time.time;
        settleRippleSr.transform.position = transform.position;
        settleRippleSr.transform.localScale = Vector3.one;
        settleRippleSr.sprite = settleRippleFrames[settleRippleFrame];
        settleRippleSr.enabled = true;
        if (!settleRippleVerificationLogged)
        {
            settleRippleVerificationLogged = true;
            Debug.Log($"[BallDropRipple VERIFY] TRIGGER speed={rb.linearVelocity.magnitude:F3}, " +
                $"position={transform.position}, sprite='{settleRippleSr.sprite.name}', " +
                $"texture='{settleRippleSr.sprite.texture.name}', rendererEnabled={settleRippleSr.enabled}, " +
                $"worldScale={settleRippleSr.transform.lossyScale}, sortingLayer={settleRippleSr.sortingLayerID}, " +
                $"sortingOrder={settleRippleSr.sortingOrder}.");
        }
    }

    void StopSettleRipple()
    {
        settleRipplePhase = SettleRipplePhase.Hidden;
        if (settleRippleSr != null) settleRippleSr.enabled = false;
    }

    void LateUpdate()
    {
        if (settleRipplePhase == SettleRipplePhase.Hidden || settleRippleSr == null) return;

        float frameSeconds = settleRipplePhase == SettleRipplePhase.Impact
            ? SettleImpactFrameSeconds : SettleIdleFrameSeconds;
        if (Time.time - settleRippleFrameStarted < frameSeconds) return;

        settleRippleFrameStarted += frameSeconds;
        if (settleRipplePhase == SettleRipplePhase.Impact)
        {
            if (settleRippleFrame > 6)
            {
                settleRippleFrame--;
            }
            else
            {
                settleRipplePhase = SettleRipplePhase.Idle;
                settleRippleFrame = 0; // immediately begin the small resting loop at frame 1
            }
        }
        else
        {
            settleRippleFrame = settleRippleFrame == 2 ? 0 : settleRippleFrame + 1;
        }
        settleRippleSr.sprite = settleRippleFrames[settleRippleFrame];
    }

    // Normalized arc height this frame: 0 on the water, 1 at the peak, 0 again right at the
    // landing frame. Pass/Lob use the symmetric parabola (peak at the midpoint of the travel
    // distance — ground speed is constant, so time-mid == distance-mid); Shot uses its own
    // asymmetric curve so the two silhouettes are never confusable.
    float HighBallArc01()
    {
        if (!highBallActive) return 0f;
        float t = Mathf.Clamp01((Time.time - highBallStart) / Mathf.Max(highBallFlightTime, 0.0001f));
        if (highBallKind == ArcKind.Shot) return ShotArc01(t);
        return 4f * t * (1f - t);
    }

    // The SHOT curve (hand-built, deliberately NOT the pass parabola): an easeOutQuad rise
    // that climbs aggressively and decelerates into an early peak (ShotArcPeakT ≈ 35% of the
    // flight), then an easeInQuad fall — the ball hangs near the top in small, slow
    // decrements before dropping sharply into the landing.
    static float ShotArc01(float t)
    {
        if (t <= ShotArcPeakT)
        {
            float u = t / ShotArcPeakT;
            return 1f - (1f - u) * (1f - u);   // steep start, eases into the peak
        }
        float v = (t - ShotArcPeakT) / (1f - ShotArcPeakT);
        return 1f - v * v;                     // barely falls at first, then drops hard
    }

    void CheckFlight()
    {
        MatchContext ctx = MatchContext.Instance;
        bool caught = transform.parent != null || (ctx != null && !ctx.BallIsLoose);

        if (highBallActive)
        {
            // An external system claimed the ball mid-air (kickoff hand-out, duel setup,
            // out-of-bounds award): stand down and hand physics back exactly as it was.
            if (caught || rb == null || !rb.simulated) { EndFlight(); return; }
            if (Time.time - highBallStart >= highBallFlightTime) LandHighBall();
            return; // constant-speed flight — the dead-speed rule below never applies mid-air
        }

        bool dead = rb == null || rb.linearVelocity.magnitude < DeadSpeed;
        if (caught || dead) { EndFlight(); return; }
        if (skipActive && !bounced && shotHeight < LowHeightMax) TryBounce();
    }

    // Touchdown: back to a perfectly normal grounded ball at the landing point. A SHOT keeps
    // its full speed (the keeper faces a real shot over the last stretch to the line); a PASS
    // keeps a soft roll for the receiver. Anyone we happened to land on top of is collision-
    // ignored until actually separated, so re-enabling the collider can't depenetration-pop
    // the ball away (same idea as MatchContext.ReleaseCollisionWindow).
    void LandHighBall()
    {
        Vector2 dir = highBallTo - highBallFrom;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
        float exitSpeed = highBallKind == ArcKind.Shot ? highBallSpeed
                                                       : highBallSpeed * LandRollFactor;

        RestoreBallPhysics();
        rb.linearVelocity = dir * exitSpeed;
        StartCoroutine(LandingSeparationWindow());
        EndFlight();
    }

    // Any swimmer overlapping the landing point is ignored until the ball and they have
    // genuinely separated. Statics (walls, goal posts) are never ignored — the ball must be
    // solid to the pool the instant it's back in the water.
    IEnumerator LandingSeparationWindow()
    {
        Collider2D ballCol = FirstEnabledBallCol();
        if (ballCol == null || rb == null) yield break;

        Collider2D[] hits = Physics2D.OverlapCircleAll(rb.position, LandingProbeRadius);
        int n = 0;
        for (int i = 0; i < hits.Length; i++)
            if (hits[i] != null && !hits[i].isTrigger &&
                hits[i].attachedRigidbody != null && hits[i].attachedRigidbody != rb)
                hits[n++] = hits[i];
        if (n == 0) yield break;

        for (int i = 0; i < n; i++) Physics2D.IgnoreCollision(ballCol, hits[i], true);

        float deadline = Time.time + LandingSeparationMax;
        bool touching = true;
        while (touching && Time.time < deadline)
        {
            touching = false;
            for (int i = 0; i < n; i++)
                if (SafeTouching(ballCol, hits[i])) { touching = true; break; }
            if (touching) yield return null;
        }

        for (int i = 0; i < n; i++)
            if (hits[i] != null && ballCol != null) Physics2D.IgnoreCollision(ballCol, hits[i], false);
    }

    Collider2D FirstEnabledBallCol()
    {
        if (ballCols == null) return null;
        foreach (Collider2D c in ballCols) if (c != null && c.enabled) return c;
        return null;
    }

    // Physics2D.Distance throws on disabled colliders / non-simulated bodies — guard all of it.
    static bool SafeTouching(Collider2D a, Collider2D b)
    {
        if (a == null || b == null || a == b || !a.enabled || !b.enabled) return false;
        if (a.attachedRigidbody != null && !a.attachedRigidbody.simulated) return false;
        if (b.attachedRigidbody != null && !b.attachedRigidbody.simulated) return false;
        return Physics2D.Distance(a, b).distance < 0.05f;
    }

    void TryBounce()
    {
        float vx = rb.linearVelocity.x;
        if (Mathf.Abs(vx) < 0.01f) return;
        if (Mathf.Sign(vx) * rb.position.x < GoalLineX - BounceBeforeGoal) return;

        bounced = true;
        Vector2 v = rb.linearVelocity;
        v.y = -v.y + Random.Range(-BounceYJitter, BounceYJitter);
        rb.linearVelocity = v;
        KeeperFooled = Random.value < FoolKeeperChance;

        squashUntil = Time.time + SquashSeconds; // flat squash, springs back in ApplyVisuals
    }

    // Spin rate depends on the flight type: shots spin fast, normal passes slowly, the arcing
    // high ball barely, skip shots not at all (a bouncing ball spinning looks wrong). Snaps
    // upright on catch.
    void UpdateSpin()
    {
        // Spin ONLY above 6 u/s and NEVER on a normal pass (Task 3). Below the threshold —
        // or a plain pass at any speed — the ball stays completely still.
        bool flying = rb != null && rb.simulated && transform.parent == null &&
                      !passActive && rb.linearVelocity.magnitude > SpinMinSpeed;
        if (flying)
        {
            float spin;
            if (skipActive) spin = 0f;                                         // bouncing ball — no spin
            else if (highBallActive)                                           // arcs: lobs/shots barely
                spin = highBallKind == ArcKind.Pass ? 0f : LobSpinDegPerSec;   // spin, plain passes never
            else if (rb.linearVelocity.magnitude > ShotSpinSpeed) spin = ShotSpinDegPerSec; // a shot
            else spin = PassSpinDegPerSec;                                     // a fast loose ball
            if (spin != 0f) transform.Rotate(0f, 0f, -spin * Time.deltaTime);
        }
        else if (transform.parent != null)
            transform.localRotation = Quaternion.identity; // caught/held → snap upright (also kills any shear a re-parent baked in)
    }

    void UpdateTrail()
    {
        if (trail == null) return;
        // No trail while a high ball is overhead — the trail renders at WATER level and would
        // draw a streak underneath the airborne sprite. It resumes the moment the ball lands.
        bool fast = rb != null && rb.simulated && transform.parent == null && !passActive &&
                    !highBallActive && rb.linearVelocity.magnitude > TrailMinSpeed;
        if (trail.emitting == fast) return;
        trail.emitting = fast;
        if (!fast) trail.Clear(); // vanish instantly on a catch / stop
    }

    void ApplyVisuals()
    {
        float target = 1f;

        // arc: swell follows the height — biggest at the peak, back to 1x at landing.
        // Amplitude is per-kind (a small pass hop swells far less than a full lob).
        if (highBallActive)
            target = Mathf.Max(target, 1f + (highBallSwellMax - 1f) * arc01);
        if (highShotActive && rb != null)
            target = Mathf.Max(target, HighShotMult());

        // skip bounce: a brief UNIFORM impact pulse layered on top (never a stretch — X == Y)
        if (Time.time < squashUntil) target *= BounceScale;

        // Hard-cap at 1.2x, then EASE toward it (no instant jumps — kills the old "swell then
        // snap back" fake-3D pop). Applied absolutely from a clean base in SetBallScale().
        target = Mathf.Clamp(target, BounceScale, MaxBallScale);
        float k = 1f - Mathf.Exp(-ScaleLerpSpeed * Time.deltaTime);
        scaleFactor = Mathf.Lerp(scaleFactor, target, k);
        SetBallScale();

        UpdateShadow();
        UpdateAirSprite();

        // high-shot glow: warm tint fading back to the ball's own colour
        if (sr != null && glowDirty)
        {
            if (Time.time < glowUntil)
                sr.color = Color.Lerp(baseColor, GlowColor, (glowUntil - Time.time) / GlowSeconds);
            else { sr.color = baseColor; glowDirty = false; }
        }
    }

    // Flat high-shot fallback: swells to 1.2x over 0.3s, then eases back to 1x inside the
    // last 2 units before the goal line it's flying at.
    float HighShotMult()
    {
        float up = Mathf.SmoothStep(0f, 1f,
            (Time.time - shotStartTime) / HighShotSwellSeconds);
        float down = 1f;
        float vx = rb.linearVelocity.x;
        if (Mathf.Abs(vx) > 0.01f)
        {
            float goalX = Mathf.Sign(vx) * GoalLineX;
            down = Mathf.Clamp01(Mathf.Abs(goalX - rb.position.x) / HighShotShrinkDistance);
        }
        return 1f + (HighShotMaxScale - 1f) * up * down;
    }

    // Drop-shadow pinned to the WATER under the airborne ball (the physics body's position —
    // the sprite is the thing that's offset upward, not the body): a dark oval that SHRINKS
    // and fades as the ball rises, and grows back for the landing. Ignores the ball's spin
    // and is counter-scaled so the ball's own swell doesn't inflate it.
    void UpdateShadow()
    {
        if (shadow == null || !shadow.gameObject.activeSelf) return;
        if (!highBallActive) return;

        shadow.position = transform.position + ShadowOffset;
        shadow.rotation = Quaternion.identity;

        float size = Mathf.Lerp(highBallShadowGround, ShadowPeakSize, arc01);
        float counter = Mathf.Max(scaleFactor, 0.01f);
        shadow.localScale = new Vector3(size / counter, size * ShadowOvalY / counter, 1f);

        shadowSr.color = new Color(ShadowTint.r, ShadowTint.g, ShadowTint.b,
                                   Mathf.Lerp(0.55f, 0.3f, arc01));
    }

    // The airborne sprite rides the arc: the physics body (and the hidden root sprite) stay
    // on the flat water line; this copy is drawn offset upward by the parabola height, so the
    // ball visibly climbs, sails over everyone and comes back down at the landing point.
    void UpdateAirSprite()
    {
        if (airSr == null || !airSr.enabled || !highBallActive) return;
        airSr.transform.position = transform.position + new Vector3(0f, highBallPeak * arc01, 0f);
        if (sr != null) { airSr.sprite = sr.sprite; airSr.color = sr.color; } // mirror tint/glow
    }

    // Set the ball's scale ABSOLUTELY from a clean base every frame so it can NEVER accumulate
    // stretch. Re-parenting onto a non-uniformly-scaled carrier (the bots are 0.2 x 0.25) used to
    // bake shear into localScale that compounded on every catch — the "ball gets more and more
    // stretched after bot passes" bug. We divide out the parent's scale per-axis so the VISIBLE
    // (world) scale is always a uniform baseScale * scaleFactor.
    // ---- release snap (shots only) ----
    // A raw, un-eased multiplier so the punch stays crisp: squash on release, pop past
    // normal, settle to exactly 1 — self-expires after SnapSeconds. Multiplied into the
    // final scale OUTSIDE the eased scaleFactor (the ScaleLerpSpeed smoothing would turn
    // a 0.12s punch into mush).
    void TriggerReleaseSnap() { snapStart = Time.time; }

    float ReleaseSnapMult()
    {
        float t = (Time.time - snapStart) / SnapSeconds;
        if (t < 0f || t >= 1f) return 1f;
        if (t < 0.3f) return Mathf.Lerp(1f, SnapSquashScale, t / 0.3f);              // squash
        if (t < 0.7f) return Mathf.Lerp(SnapSquashScale, SnapPopScale, (t - 0.3f) / 0.4f); // pop
        return Mathf.Lerp(SnapPopScale, 1f, (t - 0.7f) / 0.3f);                      // settle
    }

    void SetBallScale()
    {
        float world = baseScale * scaleFactor * ReleaseSnapMult();
        Transform p = transform.parent;
        if (p == null)
        {
            transform.localScale = new Vector3(world, world, 1f);
            return;
        }
        Vector3 ls = p.lossyScale;
        transform.localScale = new Vector3(world / SafeDivisor(ls.x), world / SafeDivisor(ls.y), 1f);
    }

    static float SafeDivisor(float v) => Mathf.Abs(v) < 1e-4f ? 1f : v;

    void EndFlight()
    {
        skipActive = false;
        bounced = false;
        highShotActive = false;
        highBallActive = false;
        passActive = false;
        KeeperFooled = false;
        squashUntil = -10f;
        glowUntil = -10f;
        arc01 = 0f;
        RestoreBallPhysics(); // idempotent — a normal flight end has nothing to restore
        if (sr != null && glowDirty) { sr.color = baseColor; glowDirty = false; }
        if (sr != null) sr.enabled = true;         // the water-level ball is the visible one again
        if (airSr != null) airSr.enabled = false;
        // Scale is NOT snapped here — ApplyVisuals eases it back to 1x every frame (target = 1
        // once the flight flags clear), so a catch or flight-end never pops the ball (Task 2).
        if (shadow != null) shadow.gameObject.SetActive(false);
    }
}
