using UnityEngine;

// Enemy-team swimmer. All the thinking lives in WaterPoloBrain; this class just
// holds the per-swimmer tunables and exposes its body through IAgentBody.
[RequireComponent(typeof(Rigidbody2D))]
public class BotMovement : MonoBehaviour, IAgentBody
{
    [SerializeField] private TeamSide myTeam;
    [SerializeField] private float chaseSpeed = 3f;
    [SerializeField] private float carrySpeed = 1.8f;
    [SerializeField] private float supportSpeed = 2.5f;
    [SerializeField] private float grabDistance = 1.2f;
    [SerializeField] private float holdOffset = 0.6f;
    [SerializeField] private float shootRange = 4f;
    [SerializeField] private float shootPower = 11f;
    [SerializeField] private float stealChance = 0.2f;

    private Rigidbody2D rb;
    private bool isHolding = false;
    private Vector2 lastDirection = Vector2.left;
    private float holdStartTime;
    private float nextStealTime;
    private Transform currentMark;
    private float nextMarkSwitchTime;

    void Awake() { rb = GetComponent<Rigidbody2D>(); }

    void FixedUpdate()
    {
        MatchContext ctx = MatchContext.Instance;

        // Play frozen (sprint duel / goal settle) → inert. Sprinters are moved by SprintDuel.
        if (ctx != null && ctx.PlayFrozen) { rb.linearVelocity = Vector2.zero; return; }

        // Excluded → fully inert (frozen in the corner), brain does not run.
        if (ExclusionManager.Instance != null && ExclusionManager.Instance.IsExcluded(transform))
        { rb.linearVelocity = Vector2.zero; return; }

        WaterPoloBrain.Tick(this, ctx);
        if (ctx != null) WaterPoloBrain.ClampX(rb, ctx.PlayerLimitX); // can't cross the goal line
    }
    void LateUpdate()  { WaterPoloBrain.KeepHeldBall(this, MatchContext.Instance); }

    // ---- IAgentBody ----
    public Rigidbody2D Body => rb;
    public Transform Tf => transform;
    public TeamSide Team => myTeam;
    public bool IsHolding { get => isHolding; set => isHolding = value; }
    public Vector2 LastDirection { get => lastDirection; set => lastDirection = value; }
    public float ChaseSpeed => chaseSpeed;
    public float CarrySpeed => carrySpeed;
    public float SupportSpeed => supportSpeed;
    public float GrabDistance => grabDistance;
    public float HoldOffset => holdOffset;
    public float ShootRange => shootRange;
    public float ShootPower => shootPower;
    public float StealChance => stealChance;
    public float HoldStartTime { get => holdStartTime; set => holdStartTime = value; }
    public float NextStealTime { get => nextStealTime; set => nextStealTime = value; }
    public Transform CurrentMark { get => currentMark; set => currentMark = value; }
    public float NextMarkSwitchTime { get => nextMarkSwitchTime; set => nextMarkSwitchTime = value; }
    public bool Suppressed => false; // bots are never human-controlled
}
