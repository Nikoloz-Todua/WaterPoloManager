using UnityEngine;

// Drives the keeper's Animator (GoalkeeperAnimation.controller) on KeeperLeft/KeeperRight,
// alongside Goalkeeper.cs. Sets ONE integer parameter, DiveState:
//   0 idle, 1 dive_left, 2 dive_right, 3 dive_bottom_left, 4 dive_bottom_right,
//   5 dive_top_left, 6 dive_top_right, 7 save
// Pulls everything from MatchContext (ball rigidbody + possession); no wiring needed.
// "Holding" is detected the same way Goalkeeper.cs tracks it internally: the ball's
// transform parented to this keeper (set by Goalkeeper.Grab).
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(SpriteRenderer))]
public class GoalkeeperAnimator : MonoBehaviour
{
    // DiveState values — must match the Any State transitions wired by
    // GoalkeeperAnimationBuilder into GoalkeeperAnimation.controller.
    const int StateIdle            = 0;
    const int StateDiveLeft        = 1;
    const int StateDiveRight       = 2;
    const int StateDiveBottomLeft  = 3;
    const int StateDiveBottomRight = 4;
    const int StateDiveTopLeft     = 5;
    const int StateDiveTopRight    = 6;
    const int StateSave            = 7;

    [Header("Shot detection")]
    [Tooltip("A loose ball moving faster than this counts as a shot.")]
    [SerializeField] private float shotSpeedThreshold = 4f;
    [Tooltip("Ball Y within this of the keeper Y = coming straight at us → keep tracking, no dive.")]
    [SerializeField] private float midBandY = 0.5f;

    private static readonly int DiveStateId = Animator.StringToHash("DiveState");

    private Animator animator;
    private SpriteRenderer sr;

    void Awake()
    {
        animator = GetComponent<Animator>();
        sr = GetComponent<SpriteRenderer>();
        // right-side keeper faces left toward the field; the left keeper faces right (no flip)
        sr.flipX = transform.position.x > 0f;
    }

    void Update()
    {
        animator.SetInteger(DiveStateId, ComputeState());
    }

    int ComputeState()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || ctx.Ball == null) return StateIdle;
        Rigidbody2D ball = ctx.Ball;

        // save: this keeper caught the ball (Goalkeeper.Grab parents it to us)
        if (ball.transform.parent == transform) return StateSave;

        // a dive needs a loose ball, moving fast, heading toward our side of the pool;
        // anything else (caught, possessed, slowed down) drops us back to idle
        if (!ctx.BallGrabbable) return StateIdle;
        if (ball.linearVelocity.magnitude <= shotSpeedThreshold) return StateIdle;
        float sideSign = transform.position.x >= 0f ? 1f : -1f;
        if (ball.linearVelocity.x * sideSign <= 0f) return StateIdle;

        // side: ball above/below our Y; inside the mid band = straight shot, just track it
        float dy = ball.position.y - transform.position.y;
        if (Mathf.Abs(dy) <= midBandY) return StateIdle;

        // "left" is the keeper's OWN left while facing the field, so it mirrors between
        // the two keepers (and the sprite flip mirrors the art to match).
        bool facingRight = transform.position.x < 0f;
        bool diveLeft = facingRight ? dy > 0f : dy < 0f;

        // a skip shot that fooled us at its bounce → stuck in the MID dive, no reaction
        if (BallFlight.Instance != null && BallFlight.Instance.KeeperFooled)
            return diveLeft ? StateDiveLeft : StateDiveRight;

        float shotHeight = ShotHeightForFlight(ctx);
        if (shotHeight < 0.3f) return diveLeft ? StateDiveBottomLeft : StateDiveBottomRight;
        if (shotHeight > 0.7f) return diveLeft ? StateDiveTopLeft : StateDiveTopRight;
        return diveLeft ? StateDiveLeft : StateDiveRight;
    }

    // Height (0..1) of the current flight, read from the last releaser's charged
    // PlayerMovement.ShotHeight (low < 0.3, high > 0.7). AI shots have no height
    // system yet → 0.5 (mid dive).
    static float ShotHeightForFlight(MatchContext ctx)
    {
        if (ctx != null && ctx.LastReleaser != null)
        {
            PlayerMovement pm = ctx.LastReleaser.GetComponent<PlayerMovement>();
            if (pm != null) return pm.ShotHeight;
        }
        return 0.5f;
    }
}
