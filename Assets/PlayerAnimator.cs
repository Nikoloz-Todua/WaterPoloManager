using UnityEngine;

// Reads the human-controlled swimmer's state from PlayerMovement (+ its Rigidbody2D) and
// drives TWO child body animators: a right-facing FRONT body and a left-facing BACK body.
// Only one is shown at a time, chosen by horizontal velocity. Purely presentational: if anything it needs is missing it
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
    const float ShootVisualSeconds = 0.22f;    // matches the 0.203s two-frame throwing clips, with a tiny settle margin
    const float StealAnimSeconds = 0.45f;      // ~6 frames @ 14 fps; defend is held off meanwhile

    const float FlipEpsilon = 0.1f;            // |velocity.x| above this drives the front sprite flip
    // Code-based idle bob: a gentle sine sway on the visible body's localPosition.y while floating
    // (floating == Speed < BobFloatSpeedMax and not holding). Each player gets a random phase so a
    // cluster of idlers doesn't bob in lockstep. NOT an Animator parameter.
    const float BobAmplitude = 0.04f;          // peak Y offset, in local units
    const float BobFrequency = 1.1f;           // cycles per second
    const float BobReturnSeconds = 0.15f;      // time to ease the offset back to 0 when swimming resumes
    const float BobFloatSpeedMax = 0.15f;      // Speed below this (and not holding) reads as floating — high enough that slow drift still floats

    [SerializeField] private float defendProximityRadius = 1.5f; // enemy carrier this close -> defend pose

    [Header("6-frame flipbooks")]
    [Tooltip("Shared idle/swim/hold/throw frame arrays. Empty uses Resources/PlayerFlipbookSet.")]
    [SerializeField] private PlayerFlipbookSet flipbookSet;
    [Tooltip("Floating loop speed. Lower this to make the idle motion calmer.")]
    [SerializeField, Range(1f, 30f)] private float idleFramesPerSecond = 8f;
    [Tooltip("Horizontal swimming.png loop speed, including left/right sprinting and carrying.")]
    [SerializeField, Range(1f, 30f)] private float swimmingFramesPerSecond = 8f;
    [Tooltip("Up-screen swimming_up.png loop speed.")]
    [SerializeField, Range(1f, 30f)] private float swimmingUpFramesPerSecond = 8f;
    [Tooltip("Down-screen swimming_down.png loop speed.")]
    [SerializeField, Range(1f, 30f)] private float swimmingDownFramesPerSecond = 8f;
    [Tooltip("Stopped-with-ball holding loop speed.")]
    [SerializeField, Range(1f, 30f)] private float holdingFramesPerSecond = 8f;
    [Tooltip("Throwing playback speed. This can be faster than the looping states.")]
    [SerializeField, Range(1f, 30f)] private float throwingFramesPerSecond = 18f;
    [Tooltip("Small debounce used only between floating and swimming. It prevents tiny velocity changes from snapping rapidly between sheets. Set to 0 for immediate changes.")]
    [SerializeField, Range(0f, 0.5f)] private float idleSwimmingTransitionDelay = 0.12f;

    [Header("Per-animation visual size")]
    [Tooltip("Overall absolute local X/Y scale for current flipbook art. This changes visuals only, never the player root or collider.")]
    [SerializeField] private Vector2 flipbookRendererLocalScale = new Vector2(0.3f, 0.3f);
    [Tooltip("Extra size multiplier used only by idle/floating frames.")]
    [SerializeField] private Vector2 idleSizeMultiplier = Vector2.one;
    [Tooltip("Extra size multiplier used only by horizontal swimming.png frames.")]
    [SerializeField] private Vector2 swimmingSizeMultiplier = Vector2.one;
    [Tooltip("Extra size multiplier used only by up-screen swimming_up.png frames.")]
    [SerializeField] private Vector2 swimmingUpSizeMultiplier = Vector2.one;
    [Tooltip("Extra size multiplier used only by down-screen swimming_down.png frames. Defaults larger because this sheet's rendered body is narrower.")]
    [SerializeField] private Vector2 swimmingDownSizeMultiplier = new Vector2(1.35f, 1.35f);
    [Tooltip("Extra size multiplier used only by stopped holding frames.")]
    [SerializeField] private Vector2 holdingSizeMultiplier = Vector2.one;
    [Tooltip("Extra size multiplier used only by throwing frames.")]
    [SerializeField] private Vector2 throwingSizeMultiplier = Vector2.one;

    [Header("State depth (presentation only)")]
    [Tooltip("Local Y offset applied to every body while carrying. Negative makes the swimmer sit lower without moving its collider or changing gameplay position.")]
    [SerializeField] private float holdingSubmergeOffsetY = -0.04f;
    [Tooltip("Local Y offset during the throwing clip. Positive makes the swimmer rise out of the water for the release.")]
    [SerializeField] private float shootingRiseOffsetY = 0.06f;
    [Tooltip("How quickly the visual body eases between normal, submerged-hold, and raised-shoot depths (local units per second).")]
    [SerializeField] private float depthTransitionSpeed = 0.8f;

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

    private PlayerMovement movement;
    private Rigidbody2D rb;
    private Material paletteMaterialInstance;
    private readonly PlayerFlipbookPlayback flipbookPlayback = new PlayerFlipbookPlayback();
    private PlayerFlipbookVisualState flipbookVisualState = PlayerFlipbookVisualState.Legacy;
    private PlayerFlipbookVisualState pendingFlipbookVisualState;
    private bool hasPendingFlipbookTransition;
    private float pendingFlipbookTransitionSince;

    private float stealAnimUntil;     // while the steal clip plays, defend must not interrupt it
    private float shootVisualUntil;   // body stays raised for the exact throw-animation window
    private Animator activeAnimator;  // the body currently shown — TriggerSteal targets this
    private bool lastShowBack;        // latched facing; held while stopped so release doesn't snap to front
    private bool backRendererBaseFlipX;
    private bool flipbookArtFacesLeft;
    private bool flipbookArtUsesHorizontalFacing;
    private PlayerSwimmingDirection swimmingDirection = PlayerSwimmingDirection.Horizontal;

    // Idle-bob state (code-only, never written to the Animator).
    private Transform frontBody, backBody;        // body transforms == the front/back animator transforms
    private Vector3 frontBodyBasePos, backBodyBasePos; // rest localPosition each bob is layered on top of
    private Vector3 frontBodyBaseScale, backBodyBaseScale;
    private float bobPhase;                        // per-player random phase, so idlers don't bob in sync
    private float bobOffset;                       // current Y offset, lerped to 0 when not floating
    private float bodyDepthOffsetY;                // current state-driven visual depth, eased every frame

    // Read by presentation code only. The gameplay transform/collider never moves for depth.
    public float VisualDepthOffsetY => bodyDepthOffsetY;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        rb = GetComponent<Rigidbody2D>();

        // Older scene saves can lose these serialized links while still retaining correctly named
        // FrontBody/BackBody children. Recover them before material setup or visibility selection so
        // no player can render both legacy bodies just because an Inspector slot went stale.
        ResolveBodyReferences();

        if (flipbookSet == null)
            flipbookSet = Resources.Load<PlayerFlipbookSet>("PlayerFlipbookSet");

        backRendererBaseFlipX = backRenderer != null && backRenderer.flipX;
        ResolvePaletteTints(out Color resolvedCapTint, out Color resolvedSwimwearTint);
        paletteMaterialInstance = PlayerPaletteSwapRuntime.CreateInstance(
            this, resolvedCapTint, resolvedSwimwearTint, frontRenderer, backRenderer);

        // Cache the body transforms + their rest positions and pick a random phase. Bobbing only ever
        // touches these children's localPosition — the parent transform is left alone.
        bobPhase = Random.Range(0f, Mathf.PI * 2f);
        if (frontRenderer != null)
        {
            frontBody = frontRenderer.transform;
            frontBodyBasePos = frontBody.localPosition;
            frontBodyBaseScale = frontBody.localScale;
        }
        if (backRenderer != null)
        {
            backBody = backRenderer.transform;
            backBodyBasePos = backBody.localPosition;
            backBodyBaseScale = backBody.localScale;
        }

        ApplyInitialIdleFrame();

        // Each body keeps its default sprite (set by Tools > Setup All Players); the sprite-swap
        // clips drive m_Sprite from their first frame. We deliberately do NOT clear the sprite here
        // — that avoids a blank first frame before the clip takes over.
    }

    void Update()
    {
        if (movement == null) return; // missing pieces -> do nothing

        Vector2 vel = rb != null ? rb.linearVelocity : Vector2.zero;
        // SprintDuel moves frozen swimmers by writing rb.position, so its physical velocity is
        // intentionally zero. Prefer that explicit presentation velocity only while the duel owns
        // movement; ordinary AI/human movement continues to read the Rigidbody directly.
        if (SprintDuel.TryGetPresentationVelocity(transform, out Vector2 duelVelocity) &&
            duelVelocity.sqrMagnitude > vel.sqrMagnitude)
            vel = duelVelocity;
        float speed = vel.magnitude;
        bool isHolding = movement.IsHolding;
        bool isStunned = FoulStun.IsStunned(transform);
        bool isMovingWithBall = movement.IsMovingWithBall;
        bool presentationHolding = isHolding && !isStunned;
        bool showStaticHold = presentationHolding && !isMovingWithBall;
        bool isShootingVisual = Time.time < shootVisualUntil;

        // Floating = idle and not carrying the ball (matches the controllers' floating rule).
        // Keep the throwing flipbook selected for the short release window before returning to idle.
        bool isFloating = speed < BobFloatSpeedMax && !presentationHolding && !isShootingVisual;
        // Despite the legacy front/back asset names, the presentation is a horizontal split:
        // moving LEFT shows the so-called BACK body; moving RIGHT shows FRONT. Exactly one body is
        // visible at a time. vel.x collapses to ~0 the instant the player stops,
        // which would snap the back body to front on release, so latch the facing: only update the
        // latch while actually moving, hold it through a brief stop, and reset to front when floating
        // idle (not holding) so an idler always faces the camera.
        bool isMoving = vel.magnitude > MoveEpsilon;
        // Directional sheets stay opt-in on the shared set. While disabled, preserve the exact
        // established horizontal left/right body and mirror behavior even during vertical travel.
        bool swimmingVertically = flipbookSet != null && flipbookSet.UseDirectionalSwimmingFrames &&
                                 isMoving && Mathf.Abs(vel.y) > Mathf.Abs(vel.x);
        bool movingUp = swimmingVertically && vel.y > 0f;
        bool movingLeft = vel.x < -FlipEpsilon;

        if (isMoving) lastShowBack = swimmingVertically ? movingUp : movingLeft;
        else if (!isHolding) lastShowBack = false; // floating idle always faces front

        bool showBack = isMoving ? (swimmingVertically ? movingUp : movingLeft) : lastShowBack;

        if (frontRenderer != null) frontRenderer.enabled = !showBack;
        if (backRenderer != null) backRenderer.enabled = showBack;
        activeAnimator = showBack ? backAnimator : frontAnimator;

        // Sheets face RIGHT: flip the FRONT body when swimming left, unflip when right, and
        // HOLD the last facing while x-velocity is near zero (no snap-back). The back body is
        // never flipped (its own clips handle left/right).
        if (frontRenderer != null)
        {
            if (vel.x < -FlipEpsilon) frontRenderer.flipX = true;
            else if (vel.x > FlipEpsilon) frontRenderer.flipX = false;
        }

        bool isSprinting = !presentationHolding && !isStunned &&
            ((movement.SprintCharge > SprintChargeThreshold && speed > MoveEpsilon) || speed > SprintSpeed);
        bool isStealingVisual = Time.time < stealAnimUntil;
        bool isDefending = !presentationHolding && !isStunned && !isStealingVisual && EnemyCarrierNearby();
        bool isExcluded = ExclusionManager.Instance != null && ExclusionManager.Instance.IsExcluded(transform);

        SelectFlipbook(isFloating, isHolding, isMovingWithBall, isShootingVisual,
                       isDefending, isStealingVisual, isExcluded, isStunned, speed,
                       swimmingVertically, movingUp);
        UpdateBodyDepth(presentationHolding, showBack, isFloating);

        // Do not let a legacy controller continue writing m_Sprite behind a current flipbook.
        // Besides avoiding a one-frame race, this makes the retired swim-back/sprint clips truly
        // inactive during every normal swimming direction. Controllers wake only for placeholder
        // defend/steal/exclusion states that do not have replacement sheets yet.
        bool legacyAnimatorNeeded = !flipbookPlayback.Active;
        SetLegacyAnimatorEnabled(legacyAnimatorNeeded);
        if (!legacyAnimatorNeeded) return;

        if (activeAnimator == null) return; // not wired up yet

        activeAnimator.SetFloat(SpeedParam, speed);
        // Gameplay possession remains true, but the flat controller deliberately sees IsHolding=false
        // while the carrier moves. That lets its existing Speed>0.1 swimming state play instead of
        // the static flat holding fallback. Stopping restores that holding state.
        activeAnimator.SetBool(IsHoldingParam, showStaticHold);
        // Sprint = tap-charge meter past 0.3 while actually moving (never while standing still),
        // OR the speed threshold so fast AI swimming still reads as a sprint.
        // Holding wins over sprint: a carrier is never flagged sprinting, so the holding state
        // always beats the sprint state regardless of speed.
        activeAnimator.SetBool(IsSprintingParam, isSprinting);
        // Defend is purely proximity-driven: an enemy CARRIER within the radius. Held off while
        // a steal clip plays so AnyState->defend can't cut the snatch short.
        activeAnimator.SetBool(IsDefendingParam, isDefending);
        activeAnimator.SetBool(IsExcludedParam, isExcluded);

    }

    void SetLegacyAnimatorEnabled(bool enabled)
    {
        if (frontAnimator != null && frontAnimator.enabled != enabled) frontAnimator.enabled = enabled;
        if (backAnimator != null && backAnimator.enabled != enabled) backAnimator.enabled = enabled;
    }

    void LateUpdate()
    {
        if (!flipbookPlayback.Active)
        {
            ApplyFlipbookRendererScale(false);
            if (backRenderer != null) backRenderer.flipX = backRendererBaseFlipX;
            return;
        }

        ApplyFlipbookRendererScale(true);
        SpriteRenderer visibleRenderer = frontRenderer != null && frontRenderer.enabled
            ? frontRenderer
            : (backRenderer != null && backRenderer.enabled ? backRenderer : null);
        if (visibleRenderer == null) return;

        // The swim source sheet faces left; the throwing source sheet faces right. Preserve the
        // existing front/back renderer toggle and only correct the selected sheet's orientation.
        if (flipbookArtUsesHorizontalFacing)
        {
            bool wantsLeft = visibleRenderer == backRenderer;
            visibleRenderer.flipX = flipbookArtFacesLeft ? !wantsLeft : wantsLeft;
        }
        else visibleRenderer.flipX = false;
        flipbookPlayback.Apply(visibleRenderer, FramesPerSecondForCurrentState());
    }

    void SelectFlipbook(bool isFloating, bool isHolding, bool isMovingWithBall,
                        bool isShootingVisual,
                        bool isDefending, bool isStealingVisual, bool isExcluded, bool isStunned,
                        float speed, bool swimmingVertically, bool movingUp)
    {
        Sprite[] frames = null;
        bool loop = true;
        bool artFacesLeft = false;
        bool artUsesHorizontalFacing = false;
        PlayerSwimmingDirection proposedSwimmingDirection = PlayerSwimmingDirection.Horizontal;
        PlayerFlipbookVisualState proposedState = PlayerFlipbookVisualState.Legacy;

        // A successful steal's action lock is deliberately a calm floating/idle presentation:
        // stars are the feedback, while the body must never fall through to defend/hold/legacy art.
        if (flipbookSet != null && isStunned)
        {
            frames = flipbookSet.IdleFrames;
            proposedState = PlayerFlipbookVisualState.Idle;
        }
        else if (flipbookSet != null && isShootingVisual)
        {
            frames = flipbookSet.ThrowingFrames;
            loop = false;
            proposedState = PlayerFlipbookVisualState.Throwing;
        }
        else if (flipbookSet != null && !isDefending &&
                  !isStealingVisual && !isExcluded)
        {
            if (isHolding)
            {
                if (isMovingWithBall && speed > MoveEpsilon)
                {
                    // Possession changes gameplay, not the swimmer's stroke: a moving carrier uses
                    // the same current six-frame swim sheet while PlayerMovement keeps the ball
                    // pinned ahead of travel. Only a stopped carrier uses the holding sheet.
                    frames = SwimmingFramesForDirection(swimmingVertically, movingUp,
                                                        out artFacesLeft, out artUsesHorizontalFacing,
                                                        out proposedSwimmingDirection);
                    proposedState = PlayerFlipbookVisualState.Swimming;
                }
                else
                {
                    frames = flipbookSet.HoldingFrames;
                    proposedState = PlayerFlipbookVisualState.Holding;
                }
            }
            else if (isFloating)
            {
                frames = flipbookSet.IdleFrames;
                proposedState = PlayerFlipbookVisualState.Idle;
            }
            else if (speed > MoveEpsilon)
            {
                frames = SwimmingFramesForDirection(swimmingVertically, movingUp,
                                                    out artFacesLeft, out artUsesHorizontalFacing,
                                                    out proposedSwimmingDirection);
                proposedState = PlayerFlipbookVisualState.Swimming;
            }
        }

        // Sprinting is intentionally NOT a fallback: every normal movement direction and speed now
        // uses the current swimming sheet, which completely removes the legacy swim-back/sprint
        // pose from human players. Defending, stealing and exclusion retain their old placeholder
        // states until replacement sheets exist.
        if (!PlayerFlipbookSet.ValidFrames(frames))
            proposedState = PlayerFlipbookVisualState.Legacy;

        bool idleSwimPair =
            (flipbookVisualState == PlayerFlipbookVisualState.Idle && proposedState == PlayerFlipbookVisualState.Swimming) ||
            (flipbookVisualState == PlayerFlipbookVisualState.Swimming && proposedState == PlayerFlipbookVisualState.Idle);

        if (!isStunned && idleSwimPair && idleSwimmingTransitionDelay > 0f)
        {
            if (!hasPendingFlipbookTransition || pendingFlipbookVisualState != proposedState)
            {
                hasPendingFlipbookTransition = true;
                pendingFlipbookVisualState = proposedState;
                pendingFlipbookTransitionSince = Time.time;
            }

            if (Time.time - pendingFlipbookTransitionSince < idleSwimmingTransitionDelay)
                return;
        }

        hasPendingFlipbookTransition = false;
        flipbookVisualState = proposedState;
        flipbookArtFacesLeft = artFacesLeft;
        flipbookArtUsesHorizontalFacing = artUsesHorizontalFacing;
        swimmingDirection = proposedState == PlayerFlipbookVisualState.Swimming
            ? proposedSwimmingDirection : PlayerSwimmingDirection.Horizontal;
        flipbookPlayback.Select(frames, loop);
    }

    // A strictly primary vertical velocity owns the new up/down art. If a sheet is ever absent,
    // retain the established horizontal six-frame sheet rather than falling back to legacy art.
    Sprite[] SwimmingFramesForDirection(bool swimmingVertically, bool movingUp,
                                        out bool artFacesLeft, out bool usesHorizontalFacing,
                                        out PlayerSwimmingDirection direction)
    {
        artFacesLeft = false;
        usesHorizontalFacing = false;
        direction = PlayerSwimmingDirection.Horizontal;
        if (swimmingVertically)
        {
            Sprite[] vertical = movingUp ? flipbookSet.SwimmingUpFrames : flipbookSet.SwimmingDownFrames;
            if (PlayerFlipbookSet.ValidFrames(vertical))
            {
                direction = movingUp ? PlayerSwimmingDirection.Up : PlayerSwimmingDirection.Down;
                return vertical;
            }
        }

        artFacesLeft = true;
        usesHorizontalFacing = true;
        return flipbookSet.SwimmingFrames;
    }

    void ResolveBodyReferences()
    {
        Transform front = transform.Find("FrontBody");
        Transform back = transform.Find("BackBody");

        if (frontAnimator == null && front != null) frontAnimator = front.GetComponent<Animator>();
        if (frontRenderer == null && front != null) frontRenderer = front.GetComponent<SpriteRenderer>();
        if (backAnimator == null && back != null) backAnimator = back.GetComponent<Animator>();
        if (backRenderer == null && back != null) backRenderer = back.GetComponent<SpriteRenderer>();
    }

    void ApplyInitialIdleFrame()
    {
        Sprite[] idle = flipbookSet != null ? flipbookSet.IdleFrames : null;
        if (!PlayerFlipbookSet.ValidFrames(idle)) return;

        // Awake runs before the first rendered frame. Force one known body and the new art now,
        // instead of allowing the scene's two enabled legacy SpriteRenderers to flash until Update.
        flipbookPlayback.Select(idle, true);
        flipbookVisualState = PlayerFlipbookVisualState.Idle;
        flipbookArtFacesLeft = false;
        flipbookArtUsesHorizontalFacing = false;
        if (frontRenderer != null)
        {
            frontRenderer.enabled = true;
            frontRenderer.flipX = false;
            frontRenderer.sprite = idle[0];
        }
        if (backRenderer != null)
        {
            backRenderer.enabled = false;
            backRenderer.sprite = idle[0];
        }
        ApplyFlipbookRendererScale(true);
    }

    void ApplyFlipbookRendererScale(bool flipbookActive)
    {
        Vector2 stateMultiplier = SizeMultiplierForCurrentState();
        Vector2 visualScale = Vector2.Scale(flipbookRendererLocalScale, stateMultiplier);
        if (frontBody != null)
        {
            frontBody.localScale = flipbookActive
                ? new Vector3(visualScale.x, visualScale.y, frontBodyBaseScale.z)
                : frontBodyBaseScale;
        }
        if (backBody != null)
        {
            backBody.localScale = flipbookActive
                ? new Vector3(visualScale.x, visualScale.y, backBodyBaseScale.z)
                : backBodyBaseScale;
        }
    }

    float FramesPerSecondForCurrentState()
    {
        switch (flipbookVisualState)
        {
            case PlayerFlipbookVisualState.Idle: return idleFramesPerSecond;
            case PlayerFlipbookVisualState.Swimming: return SwimmingFramesPerSecondForDirection();
            case PlayerFlipbookVisualState.Holding: return holdingFramesPerSecond;
            case PlayerFlipbookVisualState.Throwing: return throwingFramesPerSecond;
            default: return swimmingFramesPerSecond;
        }
    }

    float SwimmingFramesPerSecondForDirection()
    {
        switch (swimmingDirection)
        {
            case PlayerSwimmingDirection.Up: return swimmingUpFramesPerSecond;
            case PlayerSwimmingDirection.Down: return swimmingDownFramesPerSecond;
            default: return swimmingFramesPerSecond;
        }
    }

    Vector2 SizeMultiplierForCurrentState()
    {
        switch (flipbookVisualState)
        {
            case PlayerFlipbookVisualState.Idle: return idleSizeMultiplier;
            case PlayerFlipbookVisualState.Swimming: return SwimmingSizeMultiplierForDirection();
            case PlayerFlipbookVisualState.Holding: return holdingSizeMultiplier;
            case PlayerFlipbookVisualState.Throwing: return throwingSizeMultiplier;
            default: return Vector2.one;
        }
    }

    Vector2 SwimmingSizeMultiplierForDirection()
    {
        switch (swimmingDirection)
        {
            case PlayerSwimmingDirection.Up: return swimmingUpSizeMultiplier;
            case PlayerSwimmingDirection.Down: return swimmingDownSizeMultiplier;
            default: return swimmingSizeMultiplier;
        }
    }

    // Applies state depth to the flat front/back bodies. The gameplay root/collider stays untouched.
    void UpdateBodyDepth(bool isHolding, bool showBack, bool isFloating)
    {
        float targetDepth = Time.time < shootVisualUntil
            ? shootingRiseOffsetY
            : (isHolding ? holdingSubmergeOffsetY : 0f);
        bodyDepthOffsetY = Mathf.MoveTowards(
            bodyDepthOffsetY, targetDepth, Mathf.Max(0.01f, depthTransitionSpeed) * Time.deltaTime);

        if (isFloating)
            bobOffset = Mathf.Sin(Time.time * BobFrequency * 2f * Mathf.PI + bobPhase) * BobAmplitude;
        else
            bobOffset = Mathf.Lerp(bobOffset, 0f, Time.deltaTime / BobReturnSeconds);

        Vector3 depth = new Vector3(0f, bodyDepthOffsetY, 0f);
        if (frontBody != null)
            frontBody.localPosition = frontBodyBasePos + depth +
                                      (showBack ? Vector3.zero : new Vector3(0f, bobOffset, 0f));
        if (backBody != null)
            backBody.localPosition = backBodyBasePos + depth +
                                     (showBack ? new Vector3(0f, bobOffset, 0f) : Vector3.zero);
    }

    // Called explicitly by PlayerMovement.Shoot. The old release-speed heuristic inspected the
    // SWIMMER's velocity, so a stationary shot did not animate and a fast-moving non-shot release
    // could be mistaken for one. The explicit signal is presentation-only and changes no ball aim.
    public void TriggerShoot()
    {
        TriggerThrow();
    }

    // Shots and passes intentionally share the one throwing flipbook. Kept public so the
    // release owners can signal presentation without creating separate pass/shoot poses.
    public void TriggerThrow()
    {
        // Only queue the legacy trigger if the replacement throw sheet is unavailable. Queuing it
        // while the controllers are disabled would make the old shot fire later when a fallback
        // state wakes them again.
        bool hasThrowFlipbook = flipbookSet != null &&
                                PlayerFlipbookSet.ValidFrames(flipbookSet.ThrowingFrames);
        if (!hasThrowFlipbook)
        {
            if (frontAnimator != null) frontAnimator.SetTrigger(IsShootingParam);
            if (backAnimator != null) backAnimator.SetTrigger(IsShootingParam);
        }
        float flipbookSeconds = flipbookSet != null
            ? PlayerFlipbookSet.Duration(flipbookSet.ThrowingFrames, throwingFramesPerSecond)
            : 0f;
        shootVisualUntil = Time.time + Mathf.Max(ShootVisualSeconds, flipbookSeconds);
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

    void OnValidate()
    {
        idleFramesPerSecond = Mathf.Clamp(idleFramesPerSecond, 1f, 30f);
        swimmingFramesPerSecond = Mathf.Clamp(swimmingFramesPerSecond, 1f, 30f);
        swimmingUpFramesPerSecond = Mathf.Clamp(swimmingUpFramesPerSecond, 1f, 30f);
        swimmingDownFramesPerSecond = Mathf.Clamp(swimmingDownFramesPerSecond, 1f, 30f);
        holdingFramesPerSecond = Mathf.Clamp(holdingFramesPerSecond, 1f, 30f);
        throwingFramesPerSecond = Mathf.Clamp(throwingFramesPerSecond, 1f, 30f);
        idleSwimmingTransitionDelay = Mathf.Clamp(idleSwimmingTransitionDelay, 0f, 0.5f);
        flipbookRendererLocalScale.x = Mathf.Max(0.001f, flipbookRendererLocalScale.x);
        flipbookRendererLocalScale.y = Mathf.Max(0.001f, flipbookRendererLocalScale.y);
        ClampSizeMultiplier(ref idleSizeMultiplier);
        ClampSizeMultiplier(ref swimmingSizeMultiplier);
        ClampSizeMultiplier(ref swimmingUpSizeMultiplier);
        ClampSizeMultiplier(ref swimmingDownSizeMultiplier);
        ClampSizeMultiplier(ref holdingSizeMultiplier);
        ClampSizeMultiplier(ref throwingSizeMultiplier);
    }

    static void ClampSizeMultiplier(ref Vector2 value)
    {
        value.x = Mathf.Max(0.001f, value.x);
        value.y = Mathf.Max(0.001f, value.y);
    }

    void ResolvePaletteTints(out Color resolvedCapTint, out Color resolvedSwimwearTint)
    {
        Color defaultCap = new Color(0.78f, 0.16f, 0.16f, 1f);
        Color defaultSwimwear = Color.white;
        resolvedCapTint = defaultCap;
        resolvedSwimwearTint = defaultSwimwear;

        ClubProfile club = RosterManager.Instance.Club;
        if (club == null) return;
        resolvedCapTint = ParseSavedColor(club.capColorHex, defaultCap);
        resolvedSwimwearTint = ParseSavedColor(club.swimwearColorHex, defaultSwimwear);
    }

    static Color ParseSavedColor(string hex, Color fallback)
    {
        return !string.IsNullOrEmpty(hex) &&
               ColorUtility.TryParseHtmlString("#" + hex, out Color parsed)
            ? parsed
            : fallback;
    }

    void OnDestroy()
    {
        if (paletteMaterialInstance != null) Destroy(paletteMaterialInstance);
    }
}
