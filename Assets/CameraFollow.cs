using UnityEngine;

// FIFA-style follow camera for the 2D orthographic water-polo match.
//
// SELF-CONTAINED: it pulls the active player from TeamManager (static ActivePlayer) and the ball
// from MatchContext (singleton) — NO Inspector wiring is required. If either manager is missing it
// simply parks at (0, 0, -10) at size 5 and never errors. Position is driven by SmoothDamp (smooth,
// never snaps); dynamic zoom and an additive screen-shake are layered on top of the follow result.
// The camera's Z is always -10. Works on any aspect (iPhone portrait/landscape, desktop) because
// every clamp is on the camera CENTRE, not on a resolution-dependent view edge.
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Header("Follow")]
    [Tooltip("SmoothDamp responsiveness for normal following (higher = catches up faster).")]
    [SerializeField] private float followSpeed = 3.5f;
    [Tooltip("Faster responsiveness for a brief window right after the active player switches.")]
    [SerializeField] private float switchSpeed = 8f;
    [SerializeField] private float switchSpeedDuration = 0.5f;

    [Header("Zoom (orthographic size)")]
    [SerializeField] private float baseSize = 4.2f;   // resting zoom
    [SerializeField] private float maxSize = 5.0f;    // zoomed OUT (player + ball far apart)
    [SerializeField] private float minSize = 3.8f;    // zoomed IN (player controls the keeper)
    [SerializeField] private float zoomSpeed = 2.0f;  // Mathf.Lerp factor for size changes

    [Header("Follow weighting (player vs ball)")]
    [Range(0f, 1f)] [SerializeField] private float playerWeight = 0.6f;
    [Range(0f, 1f)] [SerializeField] private float ballWeight = 0.4f;

    [Header("Pool bounds (HARD clamp on the camera centre)")]
    [SerializeField] private float boundsMinX = -5.5f;
    [SerializeField] private float boundsMaxX = 5.5f;
    [SerializeField] private float boundsMinY = -3.2f;
    [SerializeField] private float boundsMaxY = 3.2f;

    [Header("Screen shake")]
    [SerializeField] private bool shakeOnGoal = true;
    [SerializeField] private bool shakeOnShot = true;

    // ---- spec-derived constants (kept OUT of the Inspector to match the requested tunable list) ----
    const float LoosePlayerWeight = 0.7f;   // ball loose → 70% player / 30% ball
    const float LooseBallWeight   = 0.3f;
    const float FarDistance       = 4f;     // player↔ball gap beyond this → zoom out toward maxSize
    const float SprintSize        = 4.5f;   // active player sprinting → mild zoom out
    const float CameraZ           = -10f;   // Z never changes
    const float FallbackSize      = 5f;     // managers missing → park here at size 5

    const float GoalShakeMag = 0.15f, GoalShakeDur = 0.4f;
    const float ShotShakeMag = 0.05f, ShotShakeDur = 0.15f;
    const float ShotSpeedThreshold = 10f;   // ball speed crossing above this (rising edge) = a powerful shot
    const float ShotShakeCooldown  = 0.2f;  // don't re-fire the shot shake within this window

    private Camera cam;
    private Vector2 followVelocity;     // SmoothDamp state
    private float currentSize;
    private bool initialized;

    private PlayerMovement lastActivePlayer;
    private float switchBoostUntil = -10f;

    private int lastScoreTotal;
    private float prevBallSpeed;
    private float nextShotShakeTime;

    // active shake state
    private float shakeTimeLeft, shakeTotalDur, shakeMag;

    void Awake()
    {
        cam = GetComponent<Camera>();
        currentSize = cam != null ? cam.orthographicSize : FallbackSize;
    }

    void Start()
    {
        lastActivePlayer = TeamManager.ActivePlayer;
        if (ScoreManager.Instance != null)
            lastScoreTotal = ScoreManager.Instance.HomeScore + ScoreManager.Instance.AwayScore;
        MatchContext ctx = MatchContext.Instance;
        if (ctx != null && ctx.Ball != null) prevBallSpeed = ctx.Ball.linearVelocity.magnitude;
    }

    void LateUpdate()
    {
        if (cam == null) return;
        float dt = Time.deltaTime;

        MatchContext ctx = MatchContext.Instance;
        PlayerMovement player = TeamManager.ActivePlayer;

        // ---- managers missing → park smoothly at (0,0,-10) at size 5, no errors ----
        if (ctx == null || player == null)
        {
            Vector2 home = Vector2.SmoothDamp(transform.position, Vector2.zero, ref followVelocity,
                                              SmoothTime(followSpeed), Mathf.Infinity, dt);
            currentSize = Mathf.Lerp(currentSize, FallbackSize, zoomSpeed * dt);
            ApplyTransform(home, Vector2.zero);
            // keep the edge-trackers current so a returning manager can't fire a stale shake
            lastActivePlayer = player;
            if (ScoreManager.Instance != null)
                lastScoreTotal = ScoreManager.Instance.HomeScore + ScoreManager.Instance.AwayScore;
            return;
        }

        // ---- BEFORE the first touch (game start, after a goal, between quarters): hold the wide
        //      pool overview centred on (0,0) at maxSize 5.0 — no following — until someone grabs
        //      the ball (Task 1). The goal shake still plays over this recenter. ----
        if (!ctx.BallTouchedSinceReset)
        {
            Vector2 ov = Vector2.SmoothDamp(transform.position, Vector2.zero, ref followVelocity,
                                            SmoothTime(followSpeed), Mathf.Infinity, dt);
            ov.x = Mathf.Clamp(ov.x, boundsMinX, boundsMaxX);
            ov.y = Mathf.Clamp(ov.y, boundsMinY, boundsMaxY);
            currentSize = Mathf.Lerp(currentSize, maxSize, zoomSpeed * dt);
            initialized = true;        // so the move to the follow point on first touch eases in, never snaps
            lastActivePlayer = player; // don't fire a stale switch-boost when following resumes
            UpdateShakeTriggers(ctx);  // let the goal shake register + play during the overview
            ApplyTransform(ov, TickShake(dt));
            return;
        }

        Vector2 playerPos = player.transform.position;
        Vector2 ballPos = ctx.BallPosition;

        // ---- weighted follow point: 60/40 player/ball, leaning 70/30 when the ball is loose ----
        bool loose = ctx.BallIsLoose;
        float pw = loose ? LoosePlayerWeight : playerWeight;
        float bw = loose ? LooseBallWeight   : ballWeight;
        float wsum = pw + bw;
        Vector2 target = wsum > 0.0001f ? (playerPos * pw + ballPos * bw) / wsum : playerPos;

        // ---- active-player switch → brief speed-up (8 u/s for switchSpeedDuration) ----
        if (player != lastActivePlayer)
        {
            if (lastActivePlayer != null) switchBoostUntil = Time.time + switchSpeedDuration;
            lastActivePlayer = player;
        }
        float speed = Time.time < switchBoostUntil ? switchSpeed : followSpeed;

        // ---- dynamic zoom target (priority: keeper-control > far gap > sprint > resting) ----
        bool keeperControl = ctx.KeeperHolding && ctx.KeeperHoldTeam == ctx.PlayerTeam;
        bool sprinting = player.SprintHeld; // holding sprint = mild zoom out
        float gap = Vector2.Distance(playerPos, ballPos);

        float targetSize;
        if (keeperControl)          targetSize = minSize;    // 3.8 — tight control view
        else if (gap > FarDistance) targetSize = maxSize;    // 5.0 — see both player and ball
        else if (sprinting)         targetSize = SprintSize; // 4.5 — mild out on the sprint
        else                        targetSize = baseSize;   // 4.2 — resting
        currentSize = Mathf.Lerp(currentSize, targetSize, zoomSpeed * dt);

        // ---- smooth move (SmoothDamp), then HARD-clamp the base position to the pool bounds ----
        Vector2 basePos;
        if (!initialized)
        {
            basePos = target;               // first valid frame: place directly (no slide from 0,0)
            followVelocity = Vector2.zero;
            initialized = true;
        }
        else
        {
            basePos = Vector2.SmoothDamp(transform.position, target, ref followVelocity,
                                         SmoothTime(speed), Mathf.Infinity, dt);
        }
        basePos.x = Mathf.Clamp(basePos.x, boundsMinX, boundsMaxX);
        basePos.y = Mathf.Clamp(basePos.y, boundsMinY, boundsMaxY);

        // ---- screen shake (additive on TOP of the clamped base; never feeds back into it) ----
        UpdateShakeTriggers(ctx);
        Vector2 shakeOffset = TickShake(dt);

        ApplyTransform(basePos, shakeOffset);
    }

    // The "speed" tunables are expressed as SmoothDamp's smoothTime inverse, so a higher number
    // means a snappier (faster catch-up) camera.
    static float SmoothTime(float speed) => 1f / Mathf.Max(speed, 0.0001f);

    void ApplyTransform(Vector2 basePos, Vector2 shakeOffset)
    {
        transform.position = new Vector3(basePos.x + shakeOffset.x, basePos.y + shakeOffset.y, CameraZ);
        cam.orthographicSize = currentSize;
    }

    // ---- shake ----
    void UpdateShakeTriggers(MatchContext ctx)
    {
        // GOAL: poll the score total — a rise means someone just scored.
        if (shakeOnGoal && ScoreManager.Instance != null)
        {
            int total = ScoreManager.Instance.HomeScore + ScoreManager.Instance.AwayScore;
            if (total > lastScoreTotal) AddShake(GoalShakeMag, GoalShakeDur);
            lastScoreTotal = total;
        }

        // POWERFUL SHOT: the rising edge of ball speed past the threshold (with a small cooldown).
        if (shakeOnShot && ctx.Ball != null)
        {
            float speed = ctx.Ball.linearVelocity.magnitude;
            if (speed > ShotSpeedThreshold && prevBallSpeed <= ShotSpeedThreshold &&
                Time.time >= nextShotShakeTime)
            {
                AddShake(ShotShakeMag, ShotShakeDur);
                nextShotShakeTime = Time.time + ShotShakeCooldown;
            }
            prevBallSpeed = speed;
        }
    }

    // Start a shake, but only if it's at least as strong as one already running, so a tiny
    // shot-shake can never stomp an in-progress goal shake.
    void AddShake(float mag, float dur)
    {
        if (shakeTimeLeft <= 0f || mag >= shakeMag)
        {
            shakeMag = mag;
            shakeTotalDur = dur;
            shakeTimeLeft = dur;
        }
    }

    Vector2 TickShake(float dt)
    {
        if (shakeTimeLeft <= 0f) return Vector2.zero;
        shakeTimeLeft -= dt;
        float decay = shakeTotalDur > 0f ? Mathf.Clamp01(shakeTimeLeft / shakeTotalDur) : 0f; // fades to 0
        return Random.insideUnitCircle * (shakeMag * decay);
    }
}
