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
    const float FlipEpsilon = 0.1f;       // |velocity.x| above this drives the sprite flip

    [SerializeField] private float defendProximityRadius = 1.5f; // enemy carrier this close → defend pose

    [Header("Team controller swap")]
    [SerializeField] private RuntimeAnimatorController redController;  // optional — empty keeps the assigned one
    [SerializeField] private RuntimeAnimatorController blueController; // BlueAnimation.controller (blue-team bots)

    private Animator animator;
    private IAgentBody body;
    private SpriteRenderer spriteRenderer;

    private bool wasHolding;      // last frame's IsHolding, for the shoot edge
    private float stealAnimUntil; // while the steal clip plays, defend must not interrupt it

    void Awake()
    {
        animator = GetComponent<Animator>();
        body = GetComponent<IAgentBody>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Blue-team bots swap to the blue controller BEFORE anything plays, so every
        // state/clip that follows is the blue set. Null slots leave the assigned
        // controller untouched (safe until the blue assets exist).
        BotMovement bm = GetComponent<BotMovement>();
        if (animator != null && bm != null)
        {
            RuntimeAnimatorController wanted = bm.isBlueTeam ? blueController : redController;
            if (wanted != null) animator.runtimeAnimatorController = wanted;
        }

        // Force the idle sprite immediately so the renderer never shows its
        // placeholder (a dot) before the first animation frame plays.
        if (animator != null) animator.Play("idle", 0, 0f);
    }

    void Update()
    {
        if (animator == null || body == null) return; // missing pieces → do nothing

        float speed = body.Body != null ? body.Body.linearVelocity.magnitude : 0f;
        bool isHolding = body.IsHolding;

        // Sheets face RIGHT: flip when swimming left, unflip when swimming right,
        // and HOLD the last facing while x-velocity is near zero (no snap-back).
        if (spriteRenderer != null && body.Body != null)
        {
            float vx = body.Body.linearVelocity.x;
            if (vx < -FlipEpsilon) spriteRenderer.flipX = true;
            else if (vx > FlipEpsilon) spriteRenderer.flipX = false;
        }

        animator.SetFloat(SpeedParam, speed);
        animator.SetBool(IsHoldingParam, isHolding);
        animator.SetBool(IsSprintingParam, body.IsDriving);
        // Defend is purely proximity-driven: the enemy CARRIER within the radius — not
        // the old "enemy team has the ball anywhere" tactical flag. Held off while a
        // steal clip plays so AnyState→defend can't cut the snatch short.
        animator.SetBool(IsDefendingParam,
            !isHolding && Time.time >= stealAnimUntil && EnemyCarrierNearby());
        animator.SetBool(IsExcludedParam,
            ExclusionManager.Instance != null && ExclusionManager.Instance.IsExcluded(transform));

        // Lost the ball this frame → treat the release as a shot.
        if (wasHolding && !isHolding)
            animator.SetTrigger(IsShootingParam);

        wasHolding = isHolding;
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
}
