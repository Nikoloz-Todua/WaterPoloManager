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
// Plus a speed-gated motion trail and a slow spin during any flight.
// All scaling is multiplier-based so it survives re-parenting on a catch.
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

    // ---- spin (per flight type) ----
    const float SpinMinSpeed = 3f;        // below this the ball doesn't spin
    const float ShotSpinSpeed = 8f;       // flight faster than this counts as a SHOT
    const float ShotSpinDegPerSec = 180f; // shots spin fast
    const float PassSpinDegPerSec = 60f;  // normal passes spin slower
    const float LobSpinDegPerSec = 30f;   // lob passes barely spin

    // ---- high shot ----
    const float HighShotMaxScale = 1.4f;
    const float HighShotSwellSeconds = 0.3f;  // smooth scale-up time after release
    const float HighShotShrinkDistance = 2f;  // back to 1x inside this of the goal line
    const float GlowSeconds = 0.2f;
    static readonly Color GlowColor = new Color(1f, 1f, 0.6f, 1f);

    // ---- skip bounce ----
    const float SquashSeconds = 0.1f;
    const float SquashX = 1.15f; // subtle squash on the skip bounce (max 1.15x)
    const float SquashY = 0.87f;
    const float RippleSeconds = 0.3f;
    const float RippleMaxScale = 0.8f;

    // ---- lob ----
    const float LobMaxScale = 1.3f;          // ball at the arc peak
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

    private Vector3 visualMult = Vector3.one; // current sprite-scale multiplier (1 = normal)

    public bool LobActive => lobActive;
    public TeamSide LobTeam => lobTeam;
    public bool KeeperFooled { get; private set; }

    // Skip-shot flight state (read by WaterPoloBrain so AI can't intercept a skip mid-air).
    public bool SkipActive => skipActive;
    public bool SkipBounced => bounced;

    void Awake()
    {
        Instance = this;
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) baseColor = sr.color;
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
        bool flying = rb != null && rb.simulated && transform.parent == null &&
                      rb.linearVelocity.magnitude > SpinMinSpeed;
        if (flying)
        {
            float spin;
            if (skipActive) spin = 0f;                                         // bouncing ball — no spin
            else if (lobActive) spin = LobSpinDegPerSec;                       // gentle lob spin
            else if (passActive) spin = PassSpinDegPerSec;                     // a normal pass (gentle, even if fast)
            else if (rb.linearVelocity.magnitude > ShotSpinSpeed) spin = ShotSpinDegPerSec; // a shot
            else spin = PassSpinDegPerSec;                                     // a fast loose ball
            if (spin != 0f) transform.Rotate(0f, 0f, -spin * Time.deltaTime);
        }
        else if (transform.parent != null && transform.localEulerAngles.z != 0f)
            transform.localRotation = Quaternion.identity; // caught → stop spinning instantly
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
        float uniform = 1f;
        float arcT = 0f, arcPeak = 0f;

        if (lobActive)
        {
            arcT = Mathf.Clamp01((Time.time - lobStartTime) / lobFlightTime);
            arcPeak = Mathf.Sin(arcT * Mathf.PI); // 0 → 1 at the peak → 0
            uniform = Mathf.Max(uniform, 1f + (LobMaxScale - 1f) * arcPeak);
        }
        if (highShotActive && rb != null)
            uniform = Mathf.Max(uniform, HighShotMult());

        // skip bounce: brief flat squash layered on top, springs back when it expires
        Vector3 mult = new Vector3(uniform, uniform, 1f);
        if (Time.time < squashUntil) { mult.x *= SquashX; mult.y *= SquashY; }
        SetVisualScale(mult);

        UpdateShadow(arcT, arcPeak);

        // high-shot glow: warm tint fading back to the ball's own colour
        if (sr != null && glowDirty)
        {
            if (Time.time < glowUntil)
                sr.color = Color.Lerp(baseColor, GlowColor, (glowUntil - Time.time) / GlowSeconds);
            else { sr.color = baseColor; glowDirty = false; }
        }
    }

    // High shot: swells to 1.4x over 0.3s, then eases back to 1x inside the last
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
        shadow.localScale = Vector3.one * (size / Mathf.Max(visualMult.x, 0.01f));

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

    // Multiplier-based so it works no matter what localScale a parent imposed on us.
    void SetVisualScale(Vector3 mult)
    {
        if (mult == visualMult) return;
        Vector3 s = transform.localScale;
        transform.localScale = new Vector3(
            s.x * (mult.x / visualMult.x),
            s.y * (mult.y / visualMult.y),
            s.z * (mult.z / visualMult.z));
        visualMult = mult;
    }

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
        SetVisualScale(Vector3.one);
        if (shadow != null) shadow.gameObject.SetActive(false);
    }
}
