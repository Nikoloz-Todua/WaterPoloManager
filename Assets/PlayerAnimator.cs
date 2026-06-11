using UnityEngine;

// Reads the human-controlled swimmer's state from PlayerMovement (+ its Rigidbody2D)
// and drives an Animator. Purely presentational: if anything it needs is missing it
// returns silently rather than throwing.
[RequireComponent(typeof(Animator))]
public class PlayerAnimator : MonoBehaviour
{
    // Parameter names must match the Animator controller exactly.
    const string SpeedParam = "Speed";
    const string IsHoldingParam = "IsHolding";
    const string IsSprintingParam = "IsSprinting";
    const string IsDefendingParam = "IsDefending";
    const string IsExcludedParam = "IsExcluded";
    const string IsShootingParam = "IsShooting";
    const string IsStealingParam = "IsStealing";

    const float SprintSpeed = 4.5f;  // Speed above this counts as sprinting (AI bursts)
    const float MoveEpsilon = 0.1f;  // Shift only reads as a sprint while actually moving
    const float ShotSpeed = 3f;      // release while moving faster than this = a shot, not a drop
    const float StealAnimSeconds = 0.45f; // ~6 frames @ 14 fps; defend is held off meanwhile

    [SerializeField] private float defendProximityRadius = 1.5f; // enemy carrier this close → defend pose

    const float FlipEpsilon = 0.1f;  // |velocity.x| above this drives the sprite flip

    private Animator animator;
    private PlayerMovement movement;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;

    private bool wasHolding;      // last frame's IsHolding, for the shoot edge
    private float stealAnimUntil; // while the steal clip plays, defend must not interrupt it

    void Awake()
    {
        animator = GetComponent<Animator>();
        movement = GetComponent<PlayerMovement>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Force the idle sprite immediately so the renderer never shows its
        // placeholder (a dot) before the first animation frame plays.
        if (animator != null) animator.Play("idle", 0, 0f);
    }

    void Update()
    {
        if (animator == null || movement == null) return; // missing pieces → do nothing

        float speed = rb != null ? rb.linearVelocity.magnitude : 0f;
        bool isHolding = movement.IsHolding;

        // Sheets face RIGHT: flip when swimming left, unflip when swimming right,
        // and HOLD the last facing while x-velocity is near zero (no snap-back).
        if (spriteRenderer != null && rb != null)
        {
            float vx = rb.linearVelocity.x;
            if (vx < -FlipEpsilon) spriteRenderer.flipX = true;
            else if (vx > FlipEpsilon) spriteRenderer.flipX = false;
        }

        animator.SetFloat(SpeedParam, speed);
        animator.SetBool(IsHoldingParam, isHolding);
        // Sprint = Shift held while actually moving (never while standing still), OR the
        // old speed threshold so fast AI swimming still reads as a sprint.
        animator.SetBool(IsSprintingParam,
            (movement.SprintHeld && speed > MoveEpsilon) || speed > SprintSpeed);
        // Defend is purely proximity-driven: an enemy CARRIER within the radius. Held
        // off while a steal clip plays so AnyState→defend can't cut the snatch short.
        animator.SetBool(IsDefendingParam,
            !isHolding && Time.time >= stealAnimUntil && EnemyCarrierNearby());
        animator.SetBool(IsExcludedParam,
            ExclusionManager.Instance != null && ExclusionManager.Instance.IsExcluded(transform));

        // Lost the ball while moving fast → it was a shot, not a drop.
        if (wasHolding && !isHolding && speed > ShotSpeed)
            animator.SetTrigger(IsShootingParam);

        wasHolding = isHolding;
    }

    // Called by PlayerMovement on every steal ATTEMPT (success or failure).
    public void TriggerSteal()
    {
        if (animator == null) return;
        animator.SetTrigger(IsStealingParam);
        stealAnimUntil = Time.time + StealAnimSeconds;
    }

    // True when the enemy team's ball carrier (keepers excluded) is within the
    // defend radius of THIS swimmer. The held ball is parented to its carrier,
    // so the carrier is simply the ball's parent.
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
