using UnityEngine;

// Reads an AI swimmer's state through IAgentBody (implemented by BotMovement) and
// drives an Animator. Purely presentational: if anything it needs is missing it
// returns silently rather than throwing.
[RequireComponent(typeof(Animator))]
public class BotAnimator : MonoBehaviour
{
    // Parameter names must match the Animator controller exactly.
    const string SpeedParam = "Speed";
    const string IsHoldingParam = "IsHolding";
    const string IsSprintingParam = "IsSprinting";
    const string IsDefendingParam = "IsDefending";
    const string IsExcludedParam = "IsExcluded";
    const string IsShootingParam = "IsShooting";
    const string IsStealingParam = "IsStealing";

    const float StealAnimSeconds = 0.45f; // ~6 frames @ 14 fps; defend is held off meanwhile
    const float ShootVisualSeconds = 0.22f;
    const float FlipEpsilon = 0.1f;       // |velocity.x| above this drives the sprite flip
    const float MoveEpsilon = 0.1f;
    const float BobFloatSpeedMax = 0.15f;

    [SerializeField] private float defendProximityRadius = 1.5f; // enemy carrier this close → defend pose

    [Header("6-frame flipbooks")]
    [Tooltip("Shared idle/swim/hold/throw frame arrays. Empty uses Resources/PlayerFlipbookSet.")]
    [SerializeField] private PlayerFlipbookSet flipbookSet;
    [Tooltip("Default speed for idle, hold, and throw flipbooks. Directional swimming has separate controls below.")]
    [SerializeField, Range(1f, 30f)] private float flipbookFramesPerSecond = 12f;
    [Header("Directional swimming playback")]
    [SerializeField, Range(1f, 30f)] private float horizontalSwimmingFramesPerSecond = 12f;
    [SerializeField, Range(1f, 30f)] private float swimmingUpFramesPerSecond = 12f;
    [SerializeField, Range(1f, 30f)] private float swimmingDownFramesPerSecond = 12f;

    [Header("Per-animation visual size")]
    [Tooltip("Overall local X/Y scale for current bot flipbook art. Defaults to 1, preserving the bot root's existing scene scale.")]
    [SerializeField] private Vector2 flipbookRendererLocalScale = Vector2.one;
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

    [Header("Bot team default palette")]
    [SerializeField] private Color blueTeamCapTint = new Color(0.05f, 0.25f, 1f, 1f);
    [SerializeField] private Color blueTeamSwimwearTint = new Color(0.05f, 0.55f, 0.95f, 1f);
    [SerializeField] private Color redTeamCapTint = new Color(0.85f, 0.05f, 0.1f, 1f);
    [SerializeField] private Color redTeamSwimwearTint = Color.white;

    [Header("Team controller swap")]
    [SerializeField] private RuntimeAnimatorController redController;  // optional — empty keeps the assigned one
    [SerializeField] private RuntimeAnimatorController blueController; // BlueAnimation.controller (blue-team bots)

    private Animator animator;
    private IAgentBody body;
    private SpriteRenderer spriteRenderer;
    private SpriteRenderer flipbookRenderer;
    private BotMovement botMovement;
    private Material paletteMaterialInstance;
    private bool hasMatchTeamPalette;
    private Color matchTeamCapTint;
    private Color matchTeamSwimwearTint;
    private readonly PlayerFlipbookPlayback flipbookPlayback = new PlayerFlipbookPlayback();

    private bool wasHolding;      // last frame's IsHolding, for the shoot edge
    private float stealAnimUntil; // while the steal clip plays, defend must not interrupt it
    private float shootVisualUntil;
    private bool lastFacingLeft;
    private bool flipbookArtFacesLeft;
    private bool flipbookArtUsesHorizontalFacing;
    private PlayerSwimmingDirection swimmingDirection = PlayerSwimmingDirection.Horizontal;
    private PlayerFlipbookVisualState flipbookVisualState = PlayerFlipbookVisualState.Legacy;
    private Vector3 flipbookBaseLocalScale = Vector3.one;

    void Awake()
    {
        animator = GetComponent<Animator>();
        body = GetComponent<IAgentBody>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        botMovement = GetComponent<BotMovement>();

        CreateFlipbookRenderer();

        if (flipbookSet == null)
            flipbookSet = Resources.Load<PlayerFlipbookSet>("PlayerFlipbookSet");

        lastFacingLeft = spriteRenderer != null && spriteRenderer.flipX;
        ApplyPaletteMaterial();

        // Legacy defend/steal/excluded clips are pre-coloured red or blue. They must follow the
        // match-owned TeamSide palette just like the generic flipbooks; otherwise switching the
        // visible renderer mid-play also appears to switch the swimmer's cap colour.
        ApplyLegacyControllerForPalette();

        // Keep the legacy controller on its idle state underneath the flipbook fallback.
        if (animator != null) animator.Play("idle", 0, 0f);
        ApplyInitialIdleFrame();
    }

    void Update()
    {
        if (animator == null || body == null) return; // missing pieces → do nothing

        Vector2 velocity = body.Body != null ? body.Body.linearVelocity : Vector2.zero;
        if (SprintDuel.TryGetPresentationVelocity(transform, out Vector2 duelVelocity) &&
            duelVelocity.sqrMagnitude > velocity.sqrMagnitude)
            velocity = duelVelocity;
        float speed = velocity.magnitude;
        // Directional sheets are intentionally opt-in while the supplied up/back art is being
        // revised. Disabled means every movement direction keeps the proven horizontal sheet.
        bool swimmingVertically = flipbookSet != null && flipbookSet.UseDirectionalSwimmingFrames &&
                                 speed > MoveEpsilon && Mathf.Abs(velocity.y) > Mathf.Abs(velocity.x);
        bool movingUp = swimmingVertically && velocity.y > 0f;
        bool isHolding = body.IsHolding;
        bool isMovingWithBall = isHolding && speed > MoveEpsilon;
        bool isStunned = FoulStun.IsStunned(transform);
        bool presentationHolding = isHolding && !isStunned;
        bool showStaticHold = presentationHolding && !isMovingWithBall;
        bool isSprinting = !presentationHolding && !isStunned && body.IsDriving;
        bool isStealingVisual = Time.time < stealAnimUntil;
        bool isDefending = !presentationHolding && !isStunned && !isStealingVisual && EnemyCarrierNearby();
        bool isExcluded = ExclusionManager.Instance != null && ExclusionManager.Instance.IsExcluded(transform);

        // Sheets face RIGHT: flip when swimming left, unflip when swimming right,
        // and HOLD the last facing while x-velocity is near zero (no snap-back).
        if (spriteRenderer != null && body.Body != null)
        {
            float vx = velocity.x;
            if (vx < -FlipEpsilon) lastFacingLeft = true;
            else if (vx > FlipEpsilon) lastFacingLeft = false;
        }

        // Lost the ball this frame → treat the release as a shot. The existing edge is also the
        // throwing flipbook latch; target/decision logic remains entirely in WaterPoloBrain.
        if (wasHolding && !isHolding)
            TriggerThrow();

        bool isShootingVisual = Time.time < shootVisualUntil;
        bool isFloating = speed < BobFloatSpeedMax && !presentationHolding && !isShootingVisual;
        SelectFlipbook(isFloating, isHolding, isMovingWithBall, isShootingVisual,
                       isDefending, isStealingVisual, isExcluded, isStunned, speed,
                       swimmingVertically, movingUp);
        SetFlipbookRendererVisible(flipbookPlayback.Active);

        animator.SetFloat(SpeedParam, speed);
        animator.SetBool(IsHoldingParam, showStaticHold);
        animator.SetBool(IsSprintingParam, isSprinting);
        // Defend is purely proximity-driven: the enemy CARRIER within the radius — not
        // the old "enemy team has the ball anywhere" tactical flag. Held off while a
        // steal clip plays so AnyState→defend can't cut the snatch short.
        animator.SetBool(IsDefendingParam, isDefending);
        animator.SetBool(IsExcludedParam, isExcluded);

        wasHolding = isHolding;
    }

    void LateUpdate()
    {
        if (spriteRenderer == null || flipbookRenderer == null) return;

        if (!flipbookPlayback.Active)
        {
            ApplyFlipbookRendererScale(false);
            spriteRenderer.flipX = lastFacingLeft; // legacy sheets face right
            return;
        }

        ApplyFlipbookRendererScale(true);
        flipbookRenderer.flipX = flipbookArtUsesHorizontalFacing
            ? (flipbookArtFacesLeft ? !lastFacingLeft : lastFacingLeft)
            : false;
        flipbookPlayback.Apply(flipbookRenderer, FramesPerSecondForCurrentState());
    }

    void SelectFlipbook(bool isFloating, bool isHolding, bool isMovingWithBall,
                        bool isShootingVisual,
                        bool isDefending, bool isStealingVisual, bool isExcluded, bool isStunned,
                        float speed, bool swimmingVertically, bool movingUp)
    {
        Sprite[] frames = null;
        bool loop = true;
        flipbookArtFacesLeft = false;
        flipbookArtUsesHorizontalFacing = false;
        PlayerSwimmingDirection proposedSwimmingDirection = PlayerSwimmingDirection.Horizontal;
        PlayerFlipbookVisualState proposedState = PlayerFlipbookVisualState.Legacy;

        // Stunned carriers always float visually; the procedural stars provide the distinct
        // feedback while the body never drops into holding/defend/legacy presentation.
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
                if (isMovingWithBall)
                {
                    frames = SwimmingFramesForDirection(swimmingVertically, movingUp,
                                                        out flipbookArtFacesLeft,
                                                        out flipbookArtUsesHorizontalFacing,
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
                                                    out flipbookArtFacesLeft,
                                                    out flipbookArtUsesHorizontalFacing,
                                                    out proposedSwimmingDirection);
                proposedState = PlayerFlipbookVisualState.Swimming;
            }
        }

        if (!PlayerFlipbookSet.ValidFrames(frames)) proposedState = PlayerFlipbookVisualState.Legacy;
        flipbookVisualState = proposedState;
        swimmingDirection = proposedState == PlayerFlipbookVisualState.Swimming
            ? proposedSwimmingDirection : PlayerSwimmingDirection.Horizontal;
        flipbookPlayback.Select(frames, loop);
    }

    // Keep the existing horizontal sheet/mirroring as the safe fallback if a directional sheet
    // is not wired. Vertical sheets are authored facing their own travel direction and are never
    // horizontally mirrored from a stale left/right facing latch.
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

    // The shared release edge invokes this for both bot shots and bot passes. Their current
    // six-frame throwing flipbook is intentionally the same visual state.
    public void TriggerThrow()
    {
        if (animator != null) animator.SetTrigger(IsShootingParam);
        float flipbookSeconds = flipbookSet != null
            ? PlayerFlipbookSet.Duration(flipbookSet.ThrowingFrames, flipbookFramesPerSecond)
            : 0f;
        shootVisualUntil = Time.time + Mathf.Max(ShootVisualSeconds, flipbookSeconds);
    }

    void CreateFlipbookRenderer()
    {
        if (spriteRenderer == null) return;

        GameObject visual = new GameObject("FlipbookBody (Runtime)");
        visual.transform.SetParent(transform, false);
        flipbookBaseLocalScale = visual.transform.localScale;
        flipbookRenderer = visual.AddComponent<SpriteRenderer>();
        flipbookRenderer.sprite = spriteRenderer.sprite;
        flipbookRenderer.color = spriteRenderer.color;
        flipbookRenderer.flipX = spriteRenderer.flipX;
        flipbookRenderer.flipY = spriteRenderer.flipY;
        flipbookRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
        flipbookRenderer.sortingOrder = spriteRenderer.sortingOrder;
        flipbookRenderer.maskInteraction = spriteRenderer.maskInteraction;
        flipbookRenderer.spriteSortPoint = spriteRenderer.spriteSortPoint;
        flipbookRenderer.sharedMaterial = spriteRenderer.sharedMaterial;
    }

    void SetFlipbookRendererVisible(bool visible)
    {
        if (flipbookRenderer != null) flipbookRenderer.enabled = visible;
        if (spriteRenderer != null) spriteRenderer.enabled = !visible;
    }

    void ApplyFlipbookRendererScale(bool flipbookActive)
    {
        if (flipbookRenderer == null) return;

        Vector2 stateMultiplier = SizeMultiplierForCurrentState();
        Vector2 visualScale = Vector2.Scale(flipbookRendererLocalScale, stateMultiplier);
        flipbookRenderer.transform.localScale = flipbookActive
            ? new Vector3(visualScale.x, visualScale.y, flipbookBaseLocalScale.z)
            : flipbookBaseLocalScale;
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

    float FramesPerSecondForCurrentState()
    {
        if (flipbookVisualState != PlayerFlipbookVisualState.Swimming) return flipbookFramesPerSecond;
        switch (swimmingDirection)
        {
            case PlayerSwimmingDirection.Up: return swimmingUpFramesPerSecond;
            case PlayerSwimmingDirection.Down: return swimmingDownFramesPerSecond;
            default: return horizontalSwimmingFramesPerSecond;
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

    void ApplyInitialIdleFrame()
    {
        Sprite[] idle = flipbookSet != null ? flipbookSet.IdleFrames : null;
        if (spriteRenderer == null || flipbookRenderer == null || !PlayerFlipbookSet.ValidFrames(idle)) return;

        // Apply the new sheet during Awake so the old blue/red controller sprite can never be the
        // first rendered frame while waiting for LateUpdate.
        flipbookPlayback.Select(idle, true);
        flipbookVisualState = PlayerFlipbookVisualState.Idle;
        flipbookArtFacesLeft = false;
        flipbookArtUsesHorizontalFacing = false;
        swimmingDirection = PlayerSwimmingDirection.Horizontal;
        flipbookRenderer.flipX = lastFacingLeft;
        flipbookRenderer.sprite = idle[0];
        SetFlipbookRendererVisible(true);
        ApplyFlipbookRendererScale(true);
    }

    void ApplyPaletteMaterial()
    {
        ResolvePaletteTints(out Color cap, out Color swimwear);

        if (paletteMaterialInstance == null)
            paletteMaterialInstance = PlayerPaletteSwapRuntime.CreateInstance(
                this, cap, swimwear, spriteRenderer, flipbookRenderer);
        else
            PlayerPaletteSwapRuntime.SetTints(paletteMaterialInstance, cap, swimwear);
    }

    void ResolvePaletteTints(out Color capTint, out Color swimwearTint)
    {
        if (hasMatchTeamPalette)
        {
            capTint = matchTeamCapTint;
            swimwearTint = matchTeamSwimwearTint;
            return;
        }

        BotMovement movement = botMovement != null ? botMovement : GetComponent<BotMovement>();
        bool blueTeam = movement == null || movement.isBlueTeam;
        capTint = blueTeam ? blueTeamCapTint : redTeamCapTint;
        swimwearTint = blueTeam ? blueTeamSwimwearTint : redTeamSwimwearTint;
    }

    // MatchSquadManager captures this from an authored starter before creating bench clones.
    public void GetConfiguredTeamPalette(out Color capTint, out Color swimwearTint)
        => ResolvePaletteTints(out capTint, out swimwearTint);

    // MatchSquadManager calls this before BotAnimator.Awake. The saved starter renderer is the
    // authoritative visual identity; BotMovement.isBlueTeam remains an AI/controller fallback,
    // never a substitute for the match team's presentation palette.
    public void GetAuthoredTeamPalette(out Color capTint, out Color swimwearTint)
    {
        SpriteRenderer authoredRenderer = spriteRenderer != null
            ? spriteRenderer : GetComponent<SpriteRenderer>();
        if (authoredRenderer != null && PlayerPaletteSwapRuntime.TryGetTints(
                authoredRenderer.sharedMaterial, out capTint, out swimwearTint)) return;
        ResolvePaletteTints(out capTint, out swimwearTint);
    }

    public void ApplyMatchTeamPalette(Color capTint, Color swimwearTint)
    {
        hasMatchTeamPalette = true;
        matchTeamCapTint = capTint;
        matchTeamSwimwearTint = swimwearTint;
        if (paletteMaterialInstance != null)
            PlayerPaletteSwapRuntime.SetTints(paletteMaterialInstance, capTint, swimwearTint);
        ApplyLegacyControllerForPalette();
    }

    void ApplyLegacyControllerForPalette()
    {
        if (animator == null) return;

        RuntimeAnimatorController wanted;
        if (hasMatchTeamPalette)
        {
            float redDistance = PaletteDistance(matchTeamCapTint, redTeamCapTint) +
                                PaletteDistance(matchTeamSwimwearTint, redTeamSwimwearTint);
            float blueDistance = PaletteDistance(matchTeamCapTint, blueTeamCapTint) +
                                 PaletteDistance(matchTeamSwimwearTint, blueTeamSwimwearTint);
            wanted = blueDistance < redDistance ? blueController : redController;
        }
        else
        {
            BotMovement movement = botMovement != null ? botMovement : GetComponent<BotMovement>();
            wanted = movement == null || movement.isBlueTeam ? blueController : redController;
        }

        if (wanted != null && animator.runtimeAnimatorController != wanted)
            animator.runtimeAnimatorController = wanted;
    }

    static float PaletteDistance(Color a, Color b)
    {
        float r = a.r - b.r;
        float g = a.g - b.g;
        float blue = a.b - b.b;
        return r * r + g * g + blue * blue;
    }

    // Called by WaterPoloBrain on every steal ATTEMPT (success or failure).
    public void TriggerSteal()
    {
        if (animator == null) return;
        animator.SetTrigger(IsStealingParam);
        stealAnimUntil = Time.time + StealAnimSeconds;
    }

    // True when the team opposing this bot currently holds the ball.
    bool EnemyHasBall()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || body.Team == null) return false;
        TeamSide enemy = ctx.EnemyOf(body.Team);
        return enemy != null && ctx.TeamHasBall(enemy);
    }

    // Builds on EnemyHasBall(): the enemy carrier (keepers excluded) must also be
    // physically within the defend radius. The held ball is parented to its carrier.
    bool EnemyCarrierNearby()
    {
        if (!EnemyHasBall()) return false;

        MatchContext ctx = MatchContext.Instance;
        Transform carrier = (ctx != null && ctx.Ball != null) ? ctx.Ball.transform.parent : null;
        if (carrier == null) return false;
        if (carrier.GetComponent<Goalkeeper>() != null) return false; // keeper holds don't count

        return Vector2.Distance(transform.position, carrier.position) <= defendProximityRadius;
    }

    void OnValidate()
    {
        flipbookFramesPerSecond = Mathf.Clamp(flipbookFramesPerSecond, 1f, 30f);
        horizontalSwimmingFramesPerSecond = Mathf.Clamp(horizontalSwimmingFramesPerSecond, 1f, 30f);
        swimmingUpFramesPerSecond = Mathf.Clamp(swimmingUpFramesPerSecond, 1f, 30f);
        swimmingDownFramesPerSecond = Mathf.Clamp(swimmingDownFramesPerSecond, 1f, 30f);
        ClampSize(ref flipbookRendererLocalScale);
        ClampSize(ref idleSizeMultiplier);
        ClampSize(ref swimmingSizeMultiplier);
        ClampSize(ref swimmingUpSizeMultiplier);
        ClampSize(ref swimmingDownSizeMultiplier);
        ClampSize(ref holdingSizeMultiplier);
        ClampSize(ref throwingSizeMultiplier);
        // OnValidate can run while Play Mode is active. Never let it reconstruct an in-match
        // swimmer from the serialized blue/red fallback after TeamSide has supplied its palette.
        if (Application.isPlaying && hasMatchTeamPalette)
        {
            ApplyPaletteMaterial();
            ApplyLegacyControllerForPalette();
        }
    }

    static void ClampSize(ref Vector2 value)
    {
        value.x = Mathf.Max(0.001f, value.x);
        value.y = Mathf.Max(0.001f, value.y);
    }

    void OnDestroy()
    {
        if (paletteMaterialInstance != null) Destroy(paletteMaterialInstance);
    }
}
