using UnityEngine;

// Reads the human-controlled swimmer's state from PlayerMovement (+ its Rigidbody2D) and
// drives TWO child body animators: a FRONT-facing body (camera-facing / sideways / idle)
// and a BACK-facing body (swimming away from the camera). Only one is shown at a time,
// chosen by vertical velocity. Purely presentational: if anything it needs is missing it
// returns silently rather than throwing.
//
// Wire frontAnimator / backAnimator / frontRenderer / backRenderer with
// Tools > Setup Player GameObjects (AnimatorBuilder.cs). Both child animators use the same
// parameter names; the front/back clips supply the visual difference.
public class PlayerAnimator : MonoBehaviour
{
    // Parameter names must match BOTH controllers exactly.
    const string SpeedParam = "Speed";
    const string IsHoldingParam = "IsHolding";
    const string IsSprintingParam = "IsSprinting";
    const string IsDefendingParam = "IsDefending";
    const string IsExcludedParam = "IsExcluded";
    const string IsShootingParam = "IsShooting";
    const string IsStealingParam = "IsStealing";

    const float SprintSpeed = 4.5f;            // Speed above this counts as sprinting (AI bursts)
    const float SprintChargeThreshold = 0.3f;  // sprintCharge above this reads as a sprint (Task 6)
    const float MoveEpsilon = 0.1f;            // Shift only reads as a sprint while actually moving
    const float ShotSpeed = 3f;                // release while moving faster than this = a shot, not a drop
    const float MinHoldBeforeShoot = 0.15f;    // must hold the ball this long before a release reads as a shot (low, so tap-shots count)
    const float StealAnimSeconds = 0.45f;      // ~6 frames @ 14 fps; defend is held off meanwhile

    const float FlipEpsilon = 0.1f;            // |velocity.x| above this drives the front sprite flip
    const float BackFacingThreshold = 0.3f;    // velocity.y ABOVE this -> show the BACK body (moving UP = swimming away)

    // Code-based idle bob: a gentle sine sway on the visible body's localPosition.y while floating
    // (floating == Speed < BobFloatSpeedMax and not holding). Each player gets a random phase so a
    // cluster of idlers doesn't bob in lockstep. NOT an Animator parameter.
    const float BobAmplitude = 0.04f;          // peak Y offset, in local units
    const float BobFrequency = 1.1f;           // cycles per second
    const float BobReturnSeconds = 0.15f;      // time to ease the offset back to 0 when swimming resumes
    const float BobFloatSpeedMax = 0.15f;      // Speed below this (and not holding) reads as floating — high enough that slow drift still floats

    [SerializeField] private float defendProximityRadius = 1.5f; // enemy carrier this close -> defend pose

    [Header("Ball anchor")]
    [Tooltip("Where a held ball is anchored. Leave empty to use this player's own transform.")]
    [SerializeField] private Transform handPosition;

    // The held-ball anchor; never null at runtime — falls back to this transform when unset.
    public Transform HandPosition => handPosition != null ? handPosition : transform;

    [Header("Body animators (wired by Tools > Setup Player GameObjects)")]
    [SerializeField] private Animator frontAnimator;
    [SerializeField] private Animator backAnimator;
    [SerializeField] private SpriteRenderer frontRenderer;
    [SerializeField] private SpriteRenderer backRenderer;

    [Header("Bone-rigged idle body (BoneBody child)")]
    [Tooltip("Animator on the BoneBody child running BoneBodyAnimation. Shown only while floating.")]
    [SerializeField] private Animator boneAnimator;
    [Tooltip("SpriteRenderer on the BoneBody child. Replaces front/back sprites while floating.")]
    [SerializeField] private SpriteRenderer boneRenderer;

    [Header("Bone-rigged holding body (HoldBody child)")]
    [Tooltip("Animator on the HoldBody child running HoldBodyAnimation. Shown only while holding.")]
    [SerializeField] private Animator holdAnimator;
    [Tooltip("SpriteRenderer on the HoldBody child. Replaces front/back sprites while holding.")]
    [SerializeField] private SpriteRenderer holdRenderer;

    [Header("Bone-rigged back-facing idle body (BackBoneBody child)")]
    [Tooltip("Animator on the BackBoneBody child running BackBoneBodyAnimation. Shown only while floating AND swimming away (vel.y > BackFacingThreshold).")]
    [SerializeField] private Animator backBoneAnimator;
    [Tooltip("SpriteRenderer on the BackBoneBody child. Replaces all other bodies while floating away.")]
    [SerializeField] private SpriteRenderer backBoneRenderer;

    [Header("Bone-rigged back-facing holding body (BackHoldBody child)")]
    [Tooltip("Animator on the BackHoldBody child running BackHoldBodyAnimation. Shown only while holding AND swimming away (vel.y > BackFacingThreshold).")]
    [SerializeField] private Animator backHoldAnimator;
    [Tooltip("SpriteRenderer on the BackHoldBody child. Replaces all other bodies while holding away.")]
    [SerializeField] private SpriteRenderer backHoldRenderer;

    private PlayerMovement movement;
    private Rigidbody2D rb;

    private bool wasHolding;          // last frame's IsHolding, for the shoot edge
    private float ballPickupTime;     // when the ball was last grabbed — a fresh grab can't read as a shot
    private bool pendingShootTrigger; // a release was latched last frame; fire IsShooting this frame
    private float releaseSpeed;       // speed captured AT the release frame, judged the next frame
    private float stealAnimUntil;     // while the steal clip plays, defend must not interrupt it
    private Animator activeAnimator;  // the body currently shown — TriggerSteal targets this
    private bool lastShowBack;        // latched facing; held while stopped so release doesn't snap to front

    // Idle-bob state (code-only, never written to the Animator).
    private Transform frontBody, backBody;        // body transforms == the front/back animator transforms
    private Vector3 frontBodyBasePos, backBodyBasePos; // rest localPosition each bob is layered on top of
    private float bobPhase;                        // per-player random phase, so idlers don't bob in sync
    private float bobOffset;                       // current Y offset, lerped to 0 when not floating

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        rb = GetComponent<Rigidbody2D>();

        // Cache the body transforms + their rest positions and pick a random phase. Bobbing only ever
        // touches these children's localPosition — the parent transform is left alone.
        bobPhase = Random.Range(0f, Mathf.PI * 2f);
        if (frontAnimator != null) { frontBody = frontAnimator.transform; frontBodyBasePos = frontBody.localPosition; }
        if (backAnimator != null)  { backBody  = backAnimator.transform;  backBodyBasePos  = backBody.localPosition; }

        // Each body keeps its default sprite (set by Tools > Setup All Players); the sprite-swap
        // clips drive m_Sprite from their first frame. We deliberately do NOT clear the sprite here
        // — that avoids a blank first frame before the clip takes over.
    }

    void Update()
    {
        if (movement == null) return; // missing pieces -> do nothing

        Vector2 vel = rb != null ? rb.linearVelocity : Vector2.zero;
        float speed = vel.magnitude;
        bool isHolding = movement.IsHolding;

        // Floating = idle and not carrying the ball (matches the controllers' floating rule). While
        // floating, the bone-rigged BoneBody plays its own swaying clip and takes over the visuals.
        // The bone body only takes over when BOTH its parts are wired — otherwise the bone branch
        // would hide the flat front/back sprites with nothing to replace them and the swimmer would
        // vanish, so we fall through to the normal front/back visibility logic instead.
        bool isFloating = speed < BobFloatSpeedMax && !isHolding;
        // Two SpriteSkin bone bodies, driven the same proven way as each other: BoneBody shows ONLY
        // while floating, HoldBody (its own hold.png-rigged bone body) shows ONLY while holding the
        // ball. Their Animators stay running in the background and only the RENDERER is toggled —
        // toggling the Animator caused a pause/restart stutter. They're mutually exclusive (floating
        // requires !isHolding), so at most one ever shows; otherwise the flat front/back sprites drive.
        // Facing the camera (down / sideways / idle) shows the FRONT body; swimming away (up)
        // shows the BACK body. Exactly one of the bodies is visible at a time.
        // Show the BACK body only when RETREATING toward our OWN goal (defendGoal), not merely when
        // swimming up the screen. defendGoal is swapped at halftime, so its X sign tells us which way
        // "toward own goal" is in the current period (P1/2 left = -x, P3/4 right = +x). If MatchContext
        // isn't up yet, fall back to "swimming left" (period 1/2 own goal).
        MatchContext ctx = MatchContext.Instance;
        bool showBack = (ctx != null && ctx.PlayerTeam != null && ctx.PlayerTeam.defendGoal != null)
            ? vel.x * Mathf.Sign(ctx.PlayerTeam.defendGoal.position.x) > FlipEpsilon
            : vel.x < -FlipEpsilon;
        // vel.x collapses to ~0 the instant the player stops, which would snap the back body to the
        // front on release. Only update the latch while actually moving; otherwise hold last facing.
        if (speed > MoveEpsilon) lastShowBack = showBack;
        else showBack = lastShowBack;

        // Back-facing bone bodies take priority while swimming away: BackBoneBody (its own
        // floating_body_back rig) when floating, BackHoldBody (holding_body_back rig) when holding.
        // Each only wins when BOTH its parts are wired; otherwise we fall through to the front-facing
        // bone bodies (and then the flat sprites), so an unwired back body never blanks the swimmer.
        bool showBoneBack = isFloating && showBack && backBoneRenderer != null && backBoneAnimator != null;
        bool showHoldBack = isHolding && showBack && backHoldRenderer != null && backHoldAnimator != null;
        // Front-facing bone bodies: BoneBody while floating, HoldBody while holding — but only when the
        // matching back body isn't already taking over.
        bool showBone = isFloating && !showBoneBack && boneRenderer != null && boneAnimator != null;
        bool showHold = isHolding && !showHoldBack && holdRenderer != null && holdAnimator != null;
        if (boneAnimator != null) boneAnimator.enabled = true;
        if (holdAnimator != null) holdAnimator.enabled = true;
        if (backBoneAnimator != null) backBoneAnimator.enabled = true;
        if (backHoldAnimator != null) backHoldAnimator.enabled = true;

        // Any bone body showing replaces BOTH flat sprites; the flat front/back only drive when no
        // bone body is active.
        bool anyBone = showBone || showHold || showBoneBack || showHoldBack;
        if (boneRenderer != null) boneRenderer.enabled = showBone;
        if (holdRenderer != null) holdRenderer.enabled = showHold;
        if (backBoneRenderer != null) backBoneRenderer.enabled = showBoneBack;
        if (backHoldRenderer != null) backHoldRenderer.enabled = showHoldBack;
        if (frontRenderer != null) frontRenderer.enabled = !anyBone && !showBack;
        if (backRenderer != null) backRenderer.enabled = !anyBone && showBack;
        activeAnimator = showBack ? backAnimator : frontAnimator;

        // Sheets face RIGHT: flip the FRONT body when swimming left, unflip when right, and
        // HOLD the last facing while x-velocity is near zero (no snap-back). The back body is
        // never flipped (its own clips handle left/right).
        if (frontRenderer != null)
        {
            if (vel.x < -FlipEpsilon) frontRenderer.flipX = true;
            else if (vel.x > FlipEpsilon) frontRenderer.flipX = false;
        }

        if (activeAnimator == null) { wasHolding = isHolding; return; } // not wired up yet

        activeAnimator.SetFloat(SpeedParam, speed);
        activeAnimator.SetBool(IsHoldingParam, isHolding);
        // Sprint = tap-charge meter past 0.3 while actually moving (never while standing still),
        // OR the speed threshold so fast AI swimming still reads as a sprint.
        // Holding wins over sprint: a carrier is never flagged sprinting, so the holding state
        // always beats the sprint state regardless of speed.
        activeAnimator.SetBool(IsSprintingParam,
            !isHolding && ((movement.SprintCharge > SprintChargeThreshold && speed > MoveEpsilon) || speed > SprintSpeed));
        // Defend is purely proximity-driven: an enemy CARRIER within the radius. Held off while
        // a steal clip plays so AnyState->defend can't cut the snatch short.
        activeAnimator.SetBool(IsDefendingParam,
            !isHolding && Time.time >= stealAnimUntil && EnemyCarrierNearby());
        activeAnimator.SetBool(IsExcludedParam,
            ExclusionManager.Instance != null && ExclusionManager.Instance.IsExcluded(transform));

        // Remember when the ball was first grabbed, so the very first grab can't read as a shot.
        if (!wasHolding && isHolding) ballPickupTime = Time.time;

        // Fire a shot latched on the PREVIOUS frame's release. Judging it a frame late (a) uses the
        // velocity captured at release — before it bled off — and (b) gives the Animator an extra
        // frame to register the trigger. Checked before the new latch below so it fires next frame.
        if (pendingShootTrigger)
        {
            if (releaseSpeed > ShotSpeed) activeAnimator.SetTrigger(IsShootingParam);
            pendingShootTrigger = false;
        }

        // Lost the ball after holding it long enough -> latch a shot and cache the release speed.
        // The speed test is deferred to next frame (regardless of this frame's speed) so a release
        // whose velocity has already started dropping still reads as a shot, not a drop.
        if (wasHolding && !isHolding && Time.time - ballPickupTime > MinHoldBeforeShoot)
        {
            pendingShootTrigger = true;
            releaseSpeed = speed;
        }

        ApplyIdleBob(showBack, isFloating);

        wasHolding = isHolding;
    }

    // Sine-bob the currently visible body while floating; ease the offset back to rest otherwise so
    // there's no snap when swimming resumes. Only the visible body carries the offset — the hidden
    // one sits at its rest localPosition. localPosition only; the parent transform is never touched.
    void ApplyIdleBob(bool showBack, bool isFloating)
    {
        if (isFloating)
            bobOffset = Mathf.Sin(Time.time * BobFrequency * 2f * Mathf.PI + bobPhase) * BobAmplitude;
        else
            bobOffset = Mathf.Lerp(bobOffset, 0f, Time.deltaTime / BobReturnSeconds);

        if (frontBody != null)
            frontBody.localPosition = frontBodyBasePos + (showBack ? Vector3.zero : new Vector3(0f, bobOffset, 0f));
        if (backBody != null)
            backBody.localPosition = backBodyBasePos + (showBack ? new Vector3(0f, bobOffset, 0f) : Vector3.zero);
    }

    // Called by PlayerMovement / WaterPoloAI on every steal ATTEMPT (success or failure).
    public void TriggerSteal()
    {
        Animator a = activeAnimator != null ? activeAnimator : frontAnimator;
        if (a == null) return;
        a.SetTrigger(IsStealingParam);
        stealAnimUntil = Time.time + StealAnimSeconds;
    }

    // True when the enemy team's ball carrier (keepers excluded) is within the defend radius of
    // THIS swimmer. The held ball is parented to its carrier, so the carrier is simply the
    // ball's parent.
    bool EnemyCarrierNearby()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || ctx.Ball == null) return false;

        TeamSide enemy = ctx.EnemyOf(ctx.PlayerTeam);
        if (enemy == null || !ctx.TeamHasBall(enemy)) return false;

        Transform carrier = ctx.Ball.transform.parent;
        if (carrier == null) return false;
        if (carrier.GetComponent<Goalkeeper>() != null) return false; // keeper holds don't count

        return Vector2.Distance(transform.position, carrier.position) <= defendProximityRadius;
    }
}
