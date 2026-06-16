using UnityEngine;

// FIFA-style stamina for every field swimmer (the Player* and Bot* objects) AND the two
// goalkeepers.
//
// SELF-CONTAINED BY DESIGN: this component only ever *writes into* its neighbours through
// neutral hooks that default to "no effect" — PlayerMovement.Stamina* / Goalkeeper.Stamina*
// properties and the IAgentBody.StaminaMult / StaminaStealMult interface members. No other
// script references the StaminaSystem type, so deleting this file leaves the whole project
// compiling and playing exactly as before (every hook just stays at its neutral default).
//
//   Field swimmers — drain / recovery per second:
//     floating / idle (speed < idleSpeed) ........ +8%   (doubles to +16% after resting)
//     normal swimming ............................ -3%
//     holding the ball + moving .................. -5%
//     sprinting (sprintCharge > 0.5) ............. -15%  (0.2-0.5 charge -> -10%)
//     excluded / out of play ..................... +15%  (resting on the sideline)
//   Field-swimmer effects: < 40% speed ×0.8 ; < 20% ×0.6 + steal ×0.8 ; 0% sprint disabled.
//   Second wind: hit 0%, ease off sprinting for 2s → a one-time +15% burst.
//
//   Goalkeeper — drain / recovery per second (Task 4):
//     tracking the ball (moving) ................. -2%
//     holding the ball ........................... -1%
//     idle on the line ........................... +10%
//   Goalkeeper effects (applied in Goalkeeper.cs): < 20% save chance -0.1 ; 0% sprint off plus a
//   further -0.15 save chance.
//
// The readout lives in the TouchControls HUD panel (it reads PlayerMovement / Goalkeeper
// StaminaPercent01). There is NO world-space bar above heads.
[DefaultExecutionOrder(-90)] // set the speed/steal/sprint hooks before the movers read them
[RequireComponent(typeof(Rigidbody2D))]
public class StaminaSystem : MonoBehaviour
{
    [Header("Pool")]
    [SerializeField] private float maxStamina = 100f;

    [Header("Field-swimmer drain / recovery (% per second)")]
    [SerializeField] private float idleRecovery = 8f;       // floating (speed < idleSpeed)
    [SerializeField] private float swimDrain = 3f;          // normal swimming
    [SerializeField] private float holdMoveDrain = 5f;      // holding the ball + moving
    [Tooltip("Drain while sprintCharge is high (> 0.5).")]
    [SerializeField] private float sprintDrainHigh = 15f;   // sprintCharge > 0.5
    [Tooltip("Drain while sprintCharge is mid (0.2-0.5).")]
    [SerializeField] private float sprintDrainMid = 10f;    // 0.2 < sprintCharge <= 0.5
    [SerializeField] private float restRecovery = 15f;      // excluded / out of play
    [Tooltip("Speed (units/sec) under which a swimmer/keeper counts as floating/idle.")]
    [SerializeField] private float idleSpeed = 0.5f;

    [Header("Goalkeeper drain / recovery (% per second)")]
    [SerializeField] private float keeperTrackDrain = 2f;    // moving / tracking the ball
    [SerializeField] private float keeperHoldDrain = 1f;     // holding the ball
    [SerializeField] private float keeperIdleRecovery = 10f; // idle on the line

    [Header("Tactical depth (field swimmers)")]
    [Tooltip("Idle this many continuous seconds → idle recovery doubles.")]
    [SerializeField] private float restBoostAfter = 5f;
    [Tooltip("After hitting 0%, ease off sprinting this long for a one-time second-wind burst.")]
    [SerializeField] private float secondWindStopTime = 2f;
    [SerializeField] private float secondWindAmount = 15f;

    // sprintCharge thresholds (mirror PlayerMovement): boost begins at 0.2, "high" at 0.5.
    const float SprintBoostCharge = 0.2f;
    const float SprintHighCharge = 0.5f;

    // ---- public API ----
    public float StaminaPercent => maxStamina > 0f ? Mathf.Clamp01(currentStamina / maxStamina) : 0f;
    public bool IsExhausted => currentStamina < maxStamina * 0.1f;

    private float currentStamina;

    // cached neighbours — ANY may be null and everything still works
    private Rigidbody2D rb;
    private PlayerMovement pm;
    private IAgentBody agent;
    private Goalkeeper keeper;

    // tactical-depth timers / state (field swimmers)
    private float idleSince = -1f;
    private float exhaustedStopTime;
    private bool secondWindUsed;

    // keeper movement is detected from position delta (the keeper is KINEMATIC + MovePosition,
    // so rb.linearVelocity does not report its motion).
    private Vector3 lastKeeperPos;

    // ---- zero-wiring install ----
    // The HUD bar reads each body's StaminaPercent01, but in the scene the StaminaSystem was only
    // attached to the two keepers — so every FIELD swimmer's StaminaPercent01 sat at its neutral
    // default (1f) and the bar never moved. Attach a StaminaSystem to every field swimmer + keeper
    // that lacks one right after the scene loads, so the system needs NO per-object wiring.
    // Idempotent (objects that already carry one are skipped), so the keepers keep their tuned values.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall()
    {
        foreach (PlayerMovement p in Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
            EnsureOn(p.gameObject);                       // the 6 player-team swimmers
        foreach (Goalkeeper gk in Object.FindObjectsByType<Goalkeeper>(FindObjectsSortMode.None))
            EnsureOn(gk.gameObject);                       // the 2 keepers (usually already have one)
        foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (mb is IAgentBody) EnsureOn(mb.gameObject); // bots + player-team AI swimmers
    }

    static void EnsureOn(GameObject go)
    {
        if (go != null && go.GetComponent<Rigidbody2D>() != null && go.GetComponent<StaminaSystem>() == null)
            go.AddComponent<StaminaSystem>();
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        pm = GetComponent<PlayerMovement>();
        agent = GetComponent<IAgentBody>();
        keeper = GetComponent<Goalkeeper>();
        currentStamina = maxStamina;
        lastKeeperPos = transform.position;
    }

    void FixedUpdate()
    {
        if (keeper != null) { UpdateKeeperStamina(Time.fixedDeltaTime); ApplyKeeperEffects(); return; }
        UpdateStamina(Time.fixedDeltaTime);
        ApplyEffects();
    }

    // ---- goalkeeper (Task 4) ----
    void UpdateKeeperStamina(float dt)
    {
        bool holding = keeper.IsHolding;
        float moveSpeed = dt > 0f ? Vector2.Distance(transform.position, lastKeeperPos) / dt : 0f;
        lastKeeperPos = transform.position;
        bool moving = moveSpeed >= idleSpeed;

        float rate = holding ? -keeperHoldDrain        // holding the ball
                   : moving  ? -keeperTrackDrain        // sliding to track the ball
                             :  keeperIdleRecovery;     // set on the line → recover
        currentStamina = Mathf.Clamp(currentStamina + rate * dt, 0f, maxStamina);
    }

    void ApplyKeeperEffects()
    {
        keeper.StaminaPercent01 = StaminaPercent;            // save penalty + HUD read this
        keeper.StaminaSprintBlocked = StaminaPercent <= 0f;  // exhausted → no sprint
    }

    // ---- field-swimmer core drain / recovery ----
    void UpdateStamina(float dt)
    {
        bool excluded = ExclusionManager.Instance != null &&
                        ExclusionManager.Instance.IsExcluded(transform);
        bool moving = rb != null && rb.linearVelocity.magnitude >= idleSpeed;
        bool holding = (pm != null && pm.IsHolding) || (agent != null && agent.IsHolding);

        // Tap-charge sprint (Task 5): the player's sprintCharge picks the drain tier. Bots
        // have no charge (sprintCharge stays 0) so they never hit the sprint tiers. Gated by
        // stamina so 0% = "normal swim only" (the meter can't drain you while already empty).
        float charge = pm != null ? pm.SprintCharge : 0f;
        bool sprinting = charge >= SprintBoostCharge && currentStamina > 0f;

        float rate;
        bool idleState = false;

        if (excluded)
        {
            rate = restRecovery;                                   // resting on the sideline
        }
        else if (sprinting)
        {
            rate = -(charge > SprintHighCharge ? sprintDrainHigh : sprintDrainMid); // >0.5 vs 0.2-0.5
        }
        else if (holding && moving)
        {
            rate = -holdMoveDrain;
        }
        else if (moving)
        {
            rate = -swimDrain;
        }
        else
        {
            idleState = true;                                      // floating / idle → recover
            if (idleSince < 0f) idleSince = Time.time;
            bool boosted = Time.time - idleSince >= restBoostAfter;
            rate = boosted ? idleRecovery * 2f : idleRecovery;     // doubles after resting
        }
        if (!idleState) idleSince = -1f;

        currentStamina = Mathf.Clamp(currentStamina + rate * dt, 0f, maxStamina);

        // ---- second wind: at 0%, ease off sprinting for a bit → one-time burst ----
        bool rawSprint = charge >= SprintBoostCharge; // "easing off" = letting the meter fall below the boost point
        if (currentStamina <= 0f)
        {
            if (!rawSprint) exhaustedStopTime += dt; else exhaustedStopTime = 0f;
            if (!secondWindUsed && exhaustedStopTime >= secondWindStopTime)
            {
                currentStamina = Mathf.Min(maxStamina, currentStamina + secondWindAmount);
                secondWindUsed = true;
            }
        }
        else
        {
            exhaustedStopTime = 0f;
            if (currentStamina > maxStamina * 0.5f) secondWindUsed = false; // re-arm once recovered
        }
    }

    // ---- apply the speed / steal effects through neutral hooks ----
    void ApplyEffects()
    {
        float pct = StaminaPercent;
        float moveMult, stealMult;
        bool sprintBlocked;

        if (pct <= 0f)        { moveMult = 0.6f; stealMult = 0.8f; sprintBlocked = true; }
        else if (pct < 0.20f) { moveMult = 0.6f; stealMult = 0.8f; sprintBlocked = false; }
        else if (pct < 0.40f) { moveMult = 0.8f; stealMult = 1f;   sprintBlocked = false; }
        else                  { moveMult = 1f;   stealMult = 1f;   sprintBlocked = false; }

        if (pm != null)
        {
            pm.StaminaSpeedMult = moveMult;      // scales base swim speed
            pm.StaminaSprintMult = moveMult;     // scales the sprint multiplier too (both × the tier)
            pm.StaminaSprintBlocked = sprintBlocked;
            pm.StaminaStealMult = stealMult;
            pm.StaminaPercent01 = pct;           // mirrored for the touch HUD (no hard dependency)
        }
        if (agent != null)
        {
            agent.StaminaMult = moveMult;        // bot/teammate speed getters scale by this
            agent.StaminaStealMult = stealMult;
        }
    }
}
