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
    [SerializeField, Range(1f, 30f)] private float flipbookFramesPerSecond = 12f;

    [Header("Swimming direction (presentation only)")]
    [Tooltip("Rotate the current swimming sheet toward the bot's full 2D travel direction without rotating its Rigidbody2D or collider.")]
    [SerializeField] private bool rotateSwimmingToMovement = true;
    [SerializeField, Range(90f, 1440f)] private float swimmingDirectionTurnSpeed = 720f;

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
    private readonly PlayerFlipbookPlayback flipbookPlayback = new PlayerFlipbookPlayback();

    private bool wasHolding;      // last frame's IsHolding, for the shoot edge
    private float stealAnimUntil; // while the steal clip plays, defend must not interrupt it
    private float shootVisualUntil;
    private bool lastFacingLeft;
    private bool flipbookArtFacesLeft;
    private PlayerFlipbookVisualState flipbookVisualState = PlayerFlipbookVisualState.Legacy;
    private Quaternion flipbookBaseLocalRotation = Quaternion.identity;

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

        // Blue-team bots swap to the blue controller BEFORE anything plays, so every
        // state/clip that follows is the blue set. Null slots leave the assigned
        // controller untouched (safe until the blue assets exist).
        if (animator != null && botMovement != null)
        {
            RuntimeAnimatorController wanted = botMovement.isBlueTeam ? blueController : redController;
            if (wanted != null) animator.runtimeAnimatorController = wanted;
        }

        // Keep the legacy controller on its idle state underneath the flipbook fallback.
        if (animator != null) animator.Play("idle", 0, 0f);
        ApplyInitialIdleFrame();
    }

    void Update()
    {
        if (animator == null || body == null) return; // missing pieces → do nothing

        float speed = body.Body != null ? body.Body.linearVelocity.magnitude : 0f;
        bool isHolding = body.IsHolding;
        bool isMovingWithBall = isHolding && speed > MoveEpsilon;
        bool showStaticHold = isHolding && !isMovingWithBall;
        bool isSprinting = !isHolding && body.IsDriving;
        bool isStealingVisual = Time.time < stealAnimUntil;
        bool isDefending = !isHolding && !isStealingVisual && EnemyCarrierNearby();
        bool isExcluded = ExclusionManager.Instance != null && ExclusionManager.Instance.IsExcluded(transform);

        // Sheets face RIGHT: flip when swimming left, unflip when swimming right,
        // and HOLD the last facing while x-velocity is near zero (no snap-back).
        if (spriteRenderer != null && body.Body != null)
        {
            float vx = body.Body.linearVelocity.x;
            if (vx < -FlipEpsilon) lastFacingLeft = true;
            else if (vx > FlipEpsilon) lastFacingLeft = false;
        }

        // Lost the ball this frame → treat the release as a shot. The existing edge is also the
        // throwing flipbook latch; target/decision logic remains entirely in WaterPoloBrain.
        if (wasHolding && !isHolding)
        {
            animator.SetTrigger(IsShootingParam);
            float flipbookSeconds = flipbookSet != null
                ? PlayerFlipbookSet.Duration(flipbookSet.ThrowingFrames, flipbookFramesPerSecond)
                : 0f;
            shootVisualUntil = Time.time + Mathf.Max(ShootVisualSeconds, flipbookSeconds);
        }

        bool isShootingVisual = Time.time < shootVisualUntil;
        bool isFloating = speed < BobFloatSpeedMax && !isHolding && !isShootingVisual;
        SelectFlipbook(isFloating, isHolding, isMovingWithBall, isShootingVisual,
                       isDefending, isStealingVisual, isExcluded, speed);
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
            spriteRenderer.flipX = lastFacingLeft; // legacy sheets face right
            flipbookRenderer.transform.localRotation = flipbookBaseLocalRotation;
            return;
        }

        flipbookRenderer.flipX = flipbookArtFacesLeft ? !lastFacingLeft : lastFacingLeft;
        flipbookPlayback.Apply(flipbookRenderer, flipbookFramesPerSecond);
        UpdateSwimmingDirectionRotation();
    }

    void SelectFlipbook(bool isFloating, bool isHolding, bool isMovingWithBall,
                        bool isShootingVisual,
                        bool isDefending, bool isStealingVisual, bool isExcluded, float speed)
    {
        Sprite[] frames = null;
        bool loop = true;
        flipbookArtFacesLeft = false;
        PlayerFlipbookVisualState proposedState = PlayerFlipbookVisualState.Legacy;

        if (flipbookSet != null && isShootingVisual)
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
                    frames = flipbookSet.SwimmingFrames;
                    flipbookArtFacesLeft = true;
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
                frames = flipbookSet.SwimmingFrames;
                flipbookArtFacesLeft = true;
                proposedState = PlayerFlipbookVisualState.Swimming;
            }
        }

        if (!PlayerFlipbookSet.ValidFrames(frames)) proposedState = PlayerFlipbookVisualState.Legacy;
        flipbookVisualState = proposedState;
        flipbookPlayback.Select(frames, loop);
    }

    void CreateFlipbookRenderer()
    {
        if (spriteRenderer == null) return;

        GameObject visual = new GameObject("FlipbookBody (Runtime)");
        visual.transform.SetParent(transform, false);
        flipbookBaseLocalRotation = visual.transform.localRotation;
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

    void UpdateSwimmingDirectionRotation()
    {
        if (!rotateSwimmingToMovement || flipbookVisualState != PlayerFlipbookVisualState.Swimming ||
            body == null || body.Body == null || body.Body.linearVelocity.sqrMagnitude <= MoveEpsilon * MoveEpsilon)
        {
            flipbookRenderer.transform.localRotation = flipbookBaseLocalRotation;
            return;
        }

        Vector3 localTravel3 = transform.InverseTransformDirection(
            new Vector3(body.Body.linearVelocity.x, body.Body.linearVelocity.y, 0f));
        float travelAngle = Mathf.Atan2(localTravel3.y, localTravel3.x) * Mathf.Rad2Deg;
        float displayedHorizontalAngle = lastFacingLeft ? 180f : 0f;
        float turnFromDisplayedDirection = Mathf.DeltaAngle(displayedHorizontalAngle, travelAngle);
        Quaternion target = flipbookBaseLocalRotation * Quaternion.Euler(0f, 0f, turnFromDisplayedDirection);
        flipbookRenderer.transform.localRotation = Quaternion.RotateTowards(
            flipbookRenderer.transform.localRotation, target, swimmingDirectionTurnSpeed * Time.deltaTime);
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
        flipbookRenderer.flipX = lastFacingLeft;
        flipbookRenderer.sprite = idle[0];
        SetFlipbookRendererVisible(true);
    }

    void ApplyPaletteMaterial()
    {
        bool blueTeam = botMovement == null || botMovement.isBlueTeam;
        Color cap = blueTeam ? blueTeamCapTint : redTeamCapTint;
        Color swimwear = blueTeam ? blueTeamSwimwearTint : redTeamSwimwearTint;

        if (paletteMaterialInstance == null)
            paletteMaterialInstance = PlayerPaletteSwapRuntime.CreateInstance(
                this, cap, swimwear, spriteRenderer, flipbookRenderer);
        else
            PlayerPaletteSwapRuntime.SetTints(paletteMaterialInstance, cap, swimwear);
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
        swimmingDirectionTurnSpeed = Mathf.Clamp(swimmingDirectionTurnSpeed, 90f, 1440f);
        if (Application.isPlaying) ApplyPaletteMaterial();
    }

    void OnDestroy()
    {
        if (paletteMaterialInstance != null) Destroy(paletteMaterialInstance);
    }
}
