using System.Collections;
using UnityEngine;

// Flight effects for the ball (auto-added to the Ball by PlayerMovement — no wiring).
// Tracks the "fake third dimension" of a shot/pass in this top-down game:
//  - SKIP SHOT (Q+Space): a fast LOW shot bounces 1.5 units in front of the goal line
//    — small random Y deflection, squash + water ripple at the bounce, and a 35%
//    chance the keeper is fooled (Goalkeeper + GoalkeeperAnimator stop reacting).
//  - HIGH SHOT (charge > 0.7): ball swells and glows briefly, shrinking back as it
//    nears the goal.
//  - HIGH LOB (F+B): the ball arcs up over a breathing water-surface shadow; enemy AI
//    interception is gated to a reduced roll in WaterPoloBrain.
// Plus a speed-gated motion trail and spin only above 6 u/s (snaps upright on a catch).
// All scaling is uniform and recomputed from a clean base each frame, so it never drifts or
// stretches even when the ball is re-parented onto a non-uniformly-scaled swimmer.
public class BallFlight : MonoBehaviour
{
    public static BallFlight Instance { get; private set; }

    // ---- gameplay (unchanged) ----
    const float GoalLineX = 7f;             // matches GoalLineOut
    const float BounceBeforeGoal = 1.5f;    // skip bounce point distance from the goal line
    const float BounceYJitter = 0.3f;       // ± random Y reflection at the bounce
    const float FoolKeeperChance = 0.35f;   // skip shots fool the keeper this often
    const float LowHeightMax = 0.3f;        // only LOW shots can skip
    const float DeadSpeed = 1f;             // slower than this = the flight is over

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
    const float LobSpinDegPerSec = 9f;    // lob passes barely spin (was 30; -70%)

    // ---- scale (Task 2: UNIFORM only — X always == Y — capped at 1.2x, Lerp-smoothed) ----
    const float MaxBallScale = 1.2f;      // hard cap on EVERY ball-scale effect
    const float ScaleLerpSpeed = 10f;     // how fast the visual scale eases to its target (no snaps)

    // ---- high shot ----
    const float HighShotMaxScale = 1.2f;      // capped at 1.2x (was 1.4)
    const float HighShotSwellSeconds = 0.3f;  // smooth scale-up time after release
    const float HighShotShrinkDistance = 2f;  // back to 1x inside this of the goal line
    const float GlowSeconds = 0.2f;
    static readonly Color GlowColor = new Color(1f, 1f, 0.6f, 1f);

    // ---- skip bounce ----
    const float SquashSeconds = 0.1f;
    const float BounceScale = 0.9f; // brief UNIFORM impact pulse (never a stretch); was a non-uniform squash
    const float RippleSeconds = 0.3f;
    const float RippleMaxScale = 0.8f;

    // ---- lob ----
    const float LobMaxScale = 1.2f;          // ball at the arc peak (capped at 1.2x; was 1.3)
    const float ShadowMinSize = 0.45f;       // relative to the ball sprite, at launch/landing
    const float ShadowMaxSize = 0.9f;        // at the arc peak
    static readonly Color ShadowTint = new Color(0.1f, 0.1f, 0.2f); // dark blue-grey
    static readonly Vector3 ShadowOffset = new Vector3(0.06f, -0.18f, 0f);

    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Transform shadow;
    private SpriteRenderer shadowSr;
    private TrailRenderer trail;
    private Color baseColor = Color.white; // the ball's real tint (it's yellow, not white)
    private bool glowDirty;                // a glow tint needs restoring when it expires

    private bool skipActive;
    private float shotHeight = 0.5f;
    private bool bounced;
    private float squashUntil = -10f;

    private bool highShotActive;
    private float shotStartTime;
    private float glowUntil = -10f;

    private bool lobActive;
    private TeamSide lobTeam;
    private float lobStartTime;
    private float lobFlightTime;

    private bool passActive; // a plain pass: NO scale change and NO trail (gentle spin only)

    private float baseScale = 1f;    // the ball's authored (uniform) localScale, captured in Awake
    private float scaleFactor = 1f;  // current uniform scale factor (1 = normal); eased every frame

    public bool LobActive => lobActive;
    public TeamSide LobTeam => lobTeam;
    public bool KeeperFooled { get; private set; }

    // Height (0..1) of the current shot's flight (set on NoteShot; low < 0.3, high > 0.7).
    // Read by Goalkeeper to weight its save chance against high shots.
    public float ShotHeight => shotHeight;

    // Skip-shot flight state (read by WaterPoloBrain so AI can't intercept a skip mid-air).
    public bool SkipActive => skipActive;
    public bool SkipBounced => bounced;

    void Awake()
    {
        Instance = this;
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) baseColor = sr.color;
        baseScale = transform.localScale.x; // ball is authored uniform (0.1) — scale never drifts now
        BuildShadow();
        BuildTrail();
    }

    // Soft shadow pinned to the water surface under the ball — the ball's own circle
    // sprite tinted dark blue-grey, one sorting order below it. Shown only during a lob;
    // size/alpha breathe with the arc (ApplyVisuals).
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

    // Called by PlayerMovement.Shoot right after the ball is released.
    public void NoteShot(float height, bool skip)
    {
        EndFlight();
        shotHeight = height;
        shotStartTime = Time.time;
        skipActive = skip;

        if (!skip && height > 0.7f)
        {
            // high shot: swell + a brief warm glow
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
    // height/skip/lob effects. Suppresses the swell AND the motion trail (the "bridge"
    // streak) for the whole flight; spin drops to the gentle pass rate.
    public void NotePass()
    {
        EndFlight();      // clear any prior flight + reset scale
        passActive = true;
    }

    // Called by PlayerMovement on an F+B lob pass.
    public void NoteLob(TeamSide team, float dist, float speed)
    {
        EndFlight();
        lobActive = true;
        lobTeam = team;
        lobStartTime = Time.time;
        // linear damping slows the ball hard, so pad the straight-line time estimate
        lobFlightTime = Mathf.Clamp(dist / Mathf.Max(speed * 0.5f, 1f), 0.3f, 2f);
        if (shadow != null) shadow.gameObject.SetActive(true);
    }

    void Update()
    {
        if (skipActive || lobActive || highShotActive || passActive) CheckFlight();
        UpdateSpin();
        UpdateTrail();
        ApplyVisuals();
    }

    void CheckFlight()
    {
        MatchContext ctx = MatchContext.Instance;
        bool caught = transform.parent != null || (ctx != null && !ctx.BallIsLoose);
        bool dead = rb == null || rb.linearVelocity.magnitude < DeadSpeed;
        bool lobLanded = lobActive && Time.time - lobStartTime >= lobFlightTime;

        if (caught || dead || lobLanded) { EndFlight(); return; }
        if (skipActive && !bounced && shotHeight < LowHeightMax) TryBounce();
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
        SpawnRipple();
    }

    // Spin rate depends on the flight type: shots spin fast, normal passes slowly, lobs barely,
    // skip shots not at all (a bouncing ball spinning looks wrong). Snaps upright on catch.
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
            else if (lobActive) spin = LobSpinDegPerSec;                       // gentle lob spin
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
        bool fast = rb != null && rb.simulated && transform.parent == null && !passActive &&
                    rb.linearVelocity.magnitude > TrailMinSpeed; // plain passes leave no trail "bridge"
        if (trail.emitting == fast) return;
        trail.emitting = fast;
        if (!fast) trail.Clear(); // vanish instantly on a catch / stop
    }

    void ApplyVisuals()
    {
        float target = 1f;
        float arcT = 0f, arcPeak = 0f;

        if (lobActive)
        {
            arcT = Mathf.Clamp01((Time.time - lobStartTime) / lobFlightTime);
            arcPeak = Mathf.Sin(arcT * Mathf.PI); // 0 → 1 at the peak → 0
            target = Mathf.Max(target, 1f + (LobMaxScale - 1f) * arcPeak);
        }
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

        UpdateShadow(arcT, arcPeak);

        // high-shot glow: warm tint fading back to the ball's own colour
        if (sr != null && glowDirty)
        {
            if (Time.time < glowUntil)
                sr.color = Color.Lerp(baseColor, GlowColor, (glowUntil - Time.time) / GlowSeconds);
            else { sr.color = baseColor; glowDirty = false; }
        }
    }

    // High shot: swells to 1.2x over 0.3s, then eases back to 1x inside the last
    // 2 units before the goal line it's flying at.
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

    // Shadow breathes with the lob arc: small at launch, biggest + darkest at the peak,
    // small and faint on the descent. Pinned to the surface (ignores the ball's spin)
    // and counter-scaled so the ball's own swell doesn't inflate it.
    void UpdateShadow(float arcT, float arcPeak)
    {
        if (shadow == null || !shadow.gameObject.activeSelf) return;
        if (!lobActive) return;

        shadow.position = transform.position + ShadowOffset;
        shadow.rotation = Quaternion.identity;

        float size = Mathf.Lerp(ShadowMinSize, ShadowMaxSize, arcPeak);
        shadow.localScale = Vector3.one * (size / Mathf.Max(scaleFactor, 0.01f));

        float alpha = arcT < 0.5f ? Mathf.Lerp(0.5f, 0.7f, arcT * 2f)        // rise: 0.5 → 0.7
                                  : Mathf.Lerp(0.7f, 0.3f, (arcT - 0.5f) * 2f); // fall: 0.7 → 0.3
        shadowSr.color = new Color(ShadowTint.r, ShadowTint.g, ShadowTint.b, alpha);
    }

    // Expanding, fading water ring at the skip-shot bounce point. Lives in world space
    // (not parented) so it stays where the ball touched down.
    void SpawnRipple()
    {
        GameObject go = new GameObject("SkipRipple");
        go.transform.position = transform.position;
        SpriteRenderer rs = go.AddComponent<SpriteRenderer>();
        if (sr != null)
        {
            rs.sprite = sr.sprite;
            rs.sortingOrder = sr.sortingOrder - 1;
        }
        rs.color = Color.white;
        StartCoroutine(RippleRoutine(go.transform, rs));
    }

    IEnumerator RippleRoutine(Transform ring, SpriteRenderer rs)
    {
        float t0 = Time.time;
        while (Time.time - t0 < RippleSeconds && ring != null)
        {
            float t = (Time.time - t0) / RippleSeconds;
            ring.localScale = Vector3.one * (RippleMaxScale * t); // expand 0 → 0.8
            rs.color = new Color(1f, 1f, 1f, 1f - t);             // fade out
            yield return null;
        }
        if (ring != null) Destroy(ring.gameObject);
    }

    // Set the ball's scale ABSOLUTELY from a clean base every frame so it can NEVER accumulate
    // stretch. Re-parenting onto a non-uniformly-scaled carrier (the bots are 0.2 x 0.25) used to
    // bake shear into localScale that compounded on every catch — the "ball gets more and more
    // stretched after bot passes" bug. We divide out the parent's scale per-axis so the VISIBLE
    // (world) scale is always a uniform baseScale * scaleFactor.
    void SetBallScale()
    {
        float world = baseScale * scaleFactor;
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
        lobActive = false;
        lobTeam = null;
        passActive = false;
        KeeperFooled = false;
        squashUntil = -10f;
        glowUntil = -10f;
        if (sr != null && glowDirty) { sr.color = baseColor; glowDirty = false; }
        // Scale is NOT snapped here — ApplyVisuals eases it back to 1x every frame (target = 1
        // once the flight flags clear), so a catch or flight-end never pops the ball (Task 2).
        if (shadow != null) shadow.gameObject.SetActive(false);
    }
}
