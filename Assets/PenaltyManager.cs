using UnityEngine;
using TMPro;

// Penalty shot (plan B16.11). Singleton modeled on SprintDuel: FreezeAll + direct
// positioning, so the brain never runs while frozen. Started by ExclusionManager when an
// exclusion-level foul happens with the victim inside the 2m zone.
//
// The fouled team's shooter takes a 1-on-keeper shot from the penalty spot. A human
// shooter charges with Space (PlayerMovement grants a frozen-shooter exemption); an AI
// shooter auto-fires after a short delay (with a miss chance). The keeper keeps tracking
// the ball during the freeze (Goalkeeper does not gate on PlayFrozen), so it can defend.
// On the shot's release the freeze lifts and a goal flows through the normal Goal path.
public class PenaltyManager : MonoBehaviour
{
    public static PenaltyManager Instance { get; private set; }

    [Header("Geometry")]
    [SerializeField] private float penaltySpotX = 2.47f;   // |x| the shooter stands at
    [SerializeField] private float behindSpotMargin = 1.0f;// other players line up at least this far behind the spot
    [SerializeField] private float penaltyAimCone = 70f;   // human aim: max degrees off the goal-centre direction

    const float PoolHalfHeight = 4.3f; // clamp repositioned y just inside the pool walls

    [Header("AI shooter")]
    [SerializeField] private float aiShootDelay = 1f;     // bot waits this long, then shoots
    [SerializeField] private float aiMissChance = 0.25f;  // chance the AI sprays it wide
    [SerializeField] private float aiMissOffset = 1.6f;   // extra y pushed past the post on a miss
    [SerializeField] private float penaltyShotSpeed = 13f;// AI penalty shot speed (units/sec)

    [Header("Safety")]
    [SerializeField] private float maxPenaltySeconds = 6f; // force-resolve if the shot never comes

    [Header("Optional UI")]
    [SerializeField] private TMP_Text penaltyText;        // "PENALTY!" banner (optional)

    private bool active;
    private bool shotFired;
    private bool humanShooter;
    private Transform shooter;
    private Rigidbody2D shooterRb;
    private Vector3 spotPos;
    private TeamSide attackingTeam;
    private float startTime;

    void Awake() { Instance = this; }

    void Start()
    {
        if (penaltyText != null) penaltyText.enabled = false; // hidden until an actual penalty
    }

    public bool Active => active;
    public bool IsActiveShooter(Transform t) => active && t != null && t == shooter;
    public float AimCone => penaltyAimCone;

    // Direction from the active shooter toward the attacked goal's centre (the aim-cone axis).
    public Vector2 ShooterGoalDir()
    {
        if (!active || shooter == null || attackingTeam == null || attackingTeam.attackGoal == null)
            return Vector2.zero;
        return ((Vector2)attackingTeam.attackGoal.position - (Vector2)shooter.position).normalized;
    }

    // Called by ExclusionManager. `shooter` = the fouled player; `attackingTeam` = its team.
    public void StartPenalty(Transform shooter, TeamSide attackingTeam)
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || shooter == null || attackingTeam == null || attackingTeam.attackGoal == null) return;

        this.shooter = shooter;
        this.attackingTeam = attackingTeam;
        this.humanShooter = (attackingTeam == ctx.PlayerTeam); // player-team victim → human takes it
        this.startTime = Time.time;
        this.shotFired = false;
        this.active = true;

        ctx.FreezeAll();
        ctx.GiveBallTo(shooter, attackingTeam); // shooter holds the ball (parented + possession)

        // stand on the penalty spot in front of the attacked goal
        float sx = Mathf.Sign(attackingTeam.attackGoal.position.x);
        if (sx == 0f) sx = 1f;
        Vector3 spot = new Vector3(sx * penaltySpotX, 0f, shooter.position.z);
        spotPos = spot;
        shooter.position = spot;
        shooterRb = shooter.GetComponent<Rigidbody2D>();
        if (shooterRb != null) { shooterRb.position = spot; shooterRb.linearVelocity = Vector2.zero; }

        // face the open corner of the attacked goal
        Vector2 aim = attackingTeam.ShotAimPoint(spot);
        SetShooterFacing((Vector2)aim - (Vector2)spot);

        // line every other field player up BEHIND the shooter (both teams), frozen there
        RepositionBehindShooter(ctx, sx);

        if (penaltyText != null) { penaltyText.enabled = true; penaltyText.text = "PENALTY!"; }
    }

    // Move all other field players (both teams; skip the shooter + excluded/disabled) to
    // the far side of the penalty spot from the goal, at least behindSpotMargin past it,
    // keeping each player's current y (clamped to the pool). Keepers are not in members.
    void RepositionBehindShooter(MatchContext ctx, float goalSign)
    {
        float targetX = goalSign * penaltySpotX - goalSign * behindSpotMargin; // margin behind the spot
        RepositionTeam(ctx.PlayerTeam, goalSign, targetX);
        RepositionTeam(ctx.BotTeam, goalSign, targetX);
    }

    void RepositionTeam(TeamSide team, float goalSign, float targetX)
    {
        if (team == null || team.members == null) return;
        foreach (Transform m in team.members)
        {
            if (m == null || m == shooter) continue;            // skip the shooter + excluded slots
            if (!m.gameObject.activeInHierarchy) continue;       // skip disabled (permanently removed)

            float x = m.position.x;
            if (goalSign * x > goalSign * targetX) x = targetX;  // pull goal-side players back behind the line
            float y = Mathf.Clamp(m.position.y, -PoolHalfHeight, PoolHalfHeight);

            m.position = new Vector3(x, y, m.position.z);
            Rigidbody2D rb = m.GetComponent<Rigidbody2D>();
            if (rb != null) { rb.position = new Vector2(x, y); rb.linearVelocity = Vector2.zero; }
        }
    }

    void Update()
    {
        if (!active) return;

        MatchContext ctx = MatchContext.Instance;
        if (ctx == null) { Resolve(null); return; }

        // shot taken (ball released / no longer the shooter's) → resolve
        if (ctx.PossessingTeam == null || ctx.Ball == null || ctx.Ball.transform.parent != shooter)
        { Resolve(ctx); return; }

        // keep the shooter planted on the spot while it aims/charges
        if (shooter != null)
        {
            if (shooterRb != null) { shooterRb.position = spotPos; shooterRb.linearVelocity = Vector2.zero; }
            else shooter.position = spotPos;
        }

        // safety: never stall the match if the shooter never releases
        if (Time.time - startTime >= maxPenaltySeconds)
        {
            ctx.ForceDropHeldBall();
            Resolve(ctx);
            return;
        }

        // AI shooter auto-fires once, after the delay
        if (!humanShooter && !shotFired && Time.time - startTime >= aiShootDelay)
            FireAIShot(ctx);
    }

    void FireAIShot(MatchContext ctx)
    {
        if (ctx.Ball == null || shooter == null || attackingTeam == null) return;
        shotFired = true;

        Vector2 spot = shooter.position;
        Vector2 aim = attackingTeam.ShotAimPoint(spot);
        if (Random.value < aiMissChance)
            aim.y += (aim.y >= 0f ? 1f : -1f) * aiMissOffset; // shove it past the post
        Vector2 dir = ((Vector2)aim - spot).normalized;

        // detach + fire (mirrors the brain's shot without touching WaterPoloBrain)
        IAgentBody body = shooter.GetComponent<IAgentBody>();
        if (body != null) { body.IsHolding = false; body.LastDirection = dir; }

        ctx.Ball.transform.SetParent(null);
        ctx.Ball.simulated = true;
        ctx.Ball.linearVelocity = dir * penaltyShotSpeed;
        ctx.SetPossession(null); // next Update() sees this and resolves
    }

    void Resolve(MatchContext ctx)
    {
        active = false;
        shooter = null;
        shooterRb = null;
        attackingTeam = null;
        if (ctx != null) ctx.Unfreeze();
        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock();
        if (penaltyText != null) penaltyText.enabled = false;
    }

    void SetShooterFacing(Vector2 dir)
    {
        if (dir.sqrMagnitude < 1e-6f || shooter == null) return;
        Vector2 n = dir.normalized;

        PlayerMovement pm = shooter.GetComponent<PlayerMovement>();
        if (pm != null) pm.SetFacing(n);

        IAgentBody body = shooter.GetComponent<IAgentBody>();
        if (body != null) body.LastDirection = n;
    }
}
