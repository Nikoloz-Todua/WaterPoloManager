using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Match-only athlete state.  The saved Roster remains read-only: personal fouls, stamina,
// eligibility and every in-water/bench transition live on this runtime identity instead.
public enum MatchPlayerStatus
{
    OnField,
    Bench,
    SubstitutingOut,
    WaitingForExchange,
    SubstitutingIn,
    ExclusionExit,
    ExclusionWaiting,
    ExclusionReplacementApproach,
    ExclusionReplacementWaiting,
    ExcludedReplacedBench,
    PermanentlyOut
}

// A single body can be temporarily owned by one presentation/rules transition.  Numeric order is
// intentional: a foul/exclusion can pre-empt a routine substitution, and a substitution can
// pre-empt timeout positioning without two FixedUpdates fighting over the Rigidbody.
public enum MatchMovePurpose
{
    None = 0,
    Q1Huddle = 10,
    Timeout = 20,
    Substitution = 30,
    Exclusion = 40
}

// Presentation ownership and destination meaning are separate. Keeping the anchor kind on the
// runtime athlete makes an invalid cross-system target diagnosable and lets geometry reject, for
// example, an exclusion exit accidentally pointed at a normal bench coordinate.
public enum MatchMoveAnchor
{
    Unspecified,
    Q1Huddle,
    SprintStart,
    TimeoutPosition,
    FlyingSubstitutionExchange,
    FlyingSubstitutionEntry,
    ExclusionReentry,
    ExclusionBenchApproach,
    Bench,
    Formation
}

[DefaultExecutionOrder(200)]
public sealed class MatchPlayerState : MonoBehaviour
{
    private static readonly Dictionary<Transform, MatchPlayerState> ByTransform =
        new Dictionary<Transform, MatchPlayerState>();

    public string PlayerId { get; private set; }
    public string DisplayName { get; private set; }
    public PlayerPosition Position { get; private set; }
    public int Overall { get; private set; }
    public int CapNumber { get; private set; }
    public PlayerData RosterData { get; private set; }
    public TeamSide Team { get; private set; }
    public bool HumanTeam { get; private set; }
    public int RoleSlot { get; private set; }
    public MatchPlayerStatus Status { get; private set; } = MatchPlayerStatus.Bench;
    public int PersonalFouls { get; private set; }
    public bool PermanentlyDisqualified { get; private set; }
    public bool SubstitutionPending { get; private set; }
    public bool LegalOnField { get; private set; }

    public MatchMovePurpose MovePurpose { get; private set; }
    public MatchMoveAnchor MoveAnchor { get; private set; }
    public Vector2 MoveTarget { get; private set; }
    public bool AtMoveTarget { get; private set; }
    public Vector2 ScriptedVelocity { get; private set; }

    public bool GameplayEligible => LegalOnField && !PermanentlyDisqualified &&
        (Status == MatchPlayerStatus.OnField || Status == MatchPlayerStatus.SubstitutingIn);
    public bool Selectable => GameplayEligible && !SubstitutionPending &&
        MovePurpose != MatchMovePurpose.Substitution && MovePurpose != MatchMovePurpose.Exclusion;
    public bool AvailableOnBench => Status == MatchPlayerStatus.Bench &&
        !PermanentlyDisqualified && !SubstitutionPending && MovePurpose == MatchMovePurpose.None;
    // Only genuinely stationary sideline states receive the strong existing rest recovery.
    // Swimmers still travelling out/in are exercising even though they are not yet legal targets.
    public bool IsRestingForStamina => Status == MatchPlayerStatus.Bench ||
        Status == MatchPlayerStatus.ExclusionWaiting ||
        Status == MatchPlayerStatus.ExclusionReplacementWaiting ||
        Status == MatchPlayerStatus.ExcludedReplacedBench ||
        Status == MatchPlayerStatus.PermanentlyOut;

    public float StaminaPercent
    {
        get
        {
            if (stamina == null) stamina = GetComponent<StaminaSystem>();
            return stamina != null ? stamina.StaminaPercent : 1f;
        }
    }

    private Rigidbody2D body;
    private StaminaSystem stamina;
    private PlayerMovement playerMovement;
    private IAgentBody agentBody;
    private Collider2D[] bodyColliders;
    private bool moveAllowDuringFullFreeze;
    private bool moveIgnoresPoolBoundaries;
    private float moveSpeed;
    private float moveArrivalRadius;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        stamina = GetComponent<StaminaSystem>();
        playerMovement = GetComponent<PlayerMovement>();
        agentBody = GetComponent<IAgentBody>();
        bodyColliders = GetComponentsInChildren<Collider2D>(true);
        ByTransform[transform] = this;
    }

    void OnDestroy()
    {
        if (ByTransform.TryGetValue(transform, out MatchPlayerState current) && current == this)
            ByTransform.Remove(transform);
    }

    public void Bind(PlayerData data, string fallbackId, string fallbackName,
                     PlayerPosition position, int overall, int capNumber,
                     TeamSide team, bool humanTeam, int roleSlot,
                     MatchPlayerStatus status, bool legalOnField)
    {
        RosterData = data;
        PlayerId = data != null && !string.IsNullOrEmpty(data.id) ? data.id : fallbackId;
        DisplayName = data != null && !string.IsNullOrEmpty(data.fullName)
            ? data.fullName : fallbackName;
        Position = data != null ? data.position : position;
        Overall = data != null ? data.overall : overall;
        CapNumber = capNumber;
        Team = team;
        HumanTeam = humanTeam;
        RoleSlot = roleSlot;
        PersonalFouls = 0;
        PermanentlyDisqualified = false;
        SubstitutionPending = false;
        SetStatus(status, legalOnField);
    }

    public void SetRoleSlot(int slot) { RoleSlot = slot; }
    public void SetPending(bool pending) { SubstitutionPending = pending; }

    public int AddPersonalFoul()
    {
        PersonalFouls++;
        return PersonalFouls;
    }

    public void MarkPermanentlyDisqualified()
    {
        PermanentlyDisqualified = true;
        LegalOnField = false;
        SubstitutionPending = false;
    }

    public void SetStatus(MatchPlayerStatus status, bool legalOnField)
    {
        Status = status;
        LegalOnField = legalOnField && !PermanentlyDisqualified;

        // Bodies stay visible and Rigidbody-driven throughout choreography, but only a legal
        // in-water athlete may collide with the ball or another player. This prevents an excluded
        // or committed outgoing swimmer from interfering on the way to the boundary.
        SetBodyColliders(LegalOnField);
    }

    public void SetLegalOnField(bool legal)
    {
        LegalOnField = legal && !PermanentlyDisqualified;
        SetBodyColliders(LegalOnField);
    }

    public bool BeginMove(MatchMovePurpose purpose, Vector2 target, float speed,
                          float arrivalRadius = 0.12f, bool allowDuringFullFreeze = false,
                          bool ignorePoolBoundaries = false,
                          MatchMoveAnchor anchor = MatchMoveAnchor.Unspecified)
    {
        if (purpose == MatchMovePurpose.None || (int)purpose < (int)MovePurpose) return false;
        PoolMatchGeometry geometry = MatchSquadManager.Instance?.Geometry;
        if (geometry != null &&
            !geometry.ValidateScriptedDestination(this, purpose, anchor, target,
                                                   out string invalidReason))
        {
            LogInvalidMove(purpose, anchor, target, invalidReason);
            return false;
        }

        if (moveIgnoresPoolBoundaries != ignorePoolBoundaries)
        {
            MatchSquadManager.Instance?.Geometry.SetBoundaryCollisionIgnored(this, ignorePoolBoundaries);
            moveIgnoresPoolBoundaries = ignorePoolBoundaries;
        }

        MovePurpose = purpose;
        MoveAnchor = anchor;
        MoveTarget = target;
        moveSpeed = Mathf.Max(0.1f, speed);
        moveArrivalRadius = Mathf.Max(0.02f, arrivalRadius);
        moveAllowDuringFullFreeze = allowDuringFullFreeze;
        AtMoveTarget = false;
        return true;
    }

    public bool Retarget(MatchMovePurpose purpose, Vector2 target,
                         MatchMoveAnchor anchor = MatchMoveAnchor.Unspecified)
    {
        if (MovePurpose != purpose) return false;
        MatchMoveAnchor resolvedAnchor = anchor == MatchMoveAnchor.Unspecified
            ? MoveAnchor : anchor;
        PoolMatchGeometry geometry = MatchSquadManager.Instance?.Geometry;
        if (geometry != null &&
            !geometry.ValidateScriptedDestination(this, purpose, resolvedAnchor, target,
                                                   out string invalidReason))
        {
            LogInvalidMove(purpose, resolvedAnchor, target, invalidReason);
            return false;
        }
        if ((MoveTarget - target).sqrMagnitude <= 0.0001f)
        {
            MoveAnchor = resolvedAnchor;
            return true;
        }
        MoveTarget = target;
        MoveAnchor = resolvedAnchor;
        AtMoveTarget = false;
        return true;
    }

    public void StopMove(MatchMovePurpose purpose = MatchMovePurpose.None)
    {
        if (purpose != MatchMovePurpose.None && MovePurpose != purpose) return;
        if (moveIgnoresPoolBoundaries)
            MatchSquadManager.Instance?.Geometry.SetBoundaryCollisionIgnored(this, false);
        moveIgnoresPoolBoundaries = false;
        MovePurpose = MatchMovePurpose.None;
        MoveAnchor = MatchMoveAnchor.Unspecified;
        MoveTarget = body != null ? body.position : (Vector2)transform.position;
        AtMoveTarget = false;
        ScriptedVelocity = Vector2.zero;
        moveSpeed = 0f;
        moveArrivalRadius = 0f;
        moveAllowDuringFullFreeze = false;
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void LogInvalidMove(MatchMovePurpose purpose, MatchMoveAnchor anchor, Vector2 target,
                        string reason)
    {
        Debug.LogError($"Rejected scripted match move: player={DisplayName ?? name}, " +
                       $"team={(Team != null ? Team.teamName : "none")}, status={Status}, " +
                       $"purpose={purpose}, anchor={anchor}, destination={target}. {reason}", this);
    }

    public void PlaceAt(Vector2 world)
    {
        transform.position = new Vector3(world.x, world.y, transform.position.z);
        if (body != null)
        {
            body.position = world;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    void FixedUpdate()
    {
        if (MovePurpose == MatchMovePurpose.None || body == null) return;
        if (Time.timeScale <= 0f)
        {
            body.linearVelocity = Vector2.zero;
            ScriptedVelocity = Vector2.zero;
            return;
        }

        MatchContext ctx = MatchContext.Instance;
        // Q1 owns the full freeze directly. Timeout-owned choreography may move through a
        // pre-existing goal/penalty freeze only while the water-polo stoppage itself is active;
        // it pauses again if that timeout ends before the transition has completed.
        bool mayMoveThroughFreeze = moveAllowDuringFullFreeze &&
            (MovePurpose == MatchMovePurpose.Q1Huddle ||
             (ctx != null && ctx.WaterPoloStoppageActive));
        if (ctx != null && ctx.PlayFrozen && !mayMoveThroughFreeze)
        {
            body.linearVelocity = Vector2.zero;
            ScriptedVelocity = Vector2.zero;
            return;
        }

        Vector2 delta = MoveTarget - body.position;
        float distance = delta.magnitude;
        if (distance <= moveArrivalRadius)
        {
            body.linearVelocity = Vector2.zero;
            ScriptedVelocity = Vector2.zero;
            AtMoveTarget = true;
            return;
        }

        Vector2 direction = delta / distance;
        float exactArrivalSpeed = distance / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        ScriptedVelocity = direction * Mathf.Min(moveSpeed, exactArrivalSpeed);
        body.linearVelocity = ScriptedVelocity;
        AtMoveTarget = false;

        if (playerMovement != null) playerMovement.SetFacing(direction);
        if (agentBody != null) agentBody.LastDirection = direction;
    }

    void SetBodyColliders(bool enabled)
    {
        if (bodyColliders == null) bodyColliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < bodyColliders.Length; i++)
        {
            Collider2D collider = bodyColliders[i];
            if (collider == null || (body != null && collider.attachedRigidbody != null &&
                                     collider.attachedRigidbody != body)) continue;
            collider.enabled = enabled;
        }
    }

    public static MatchPlayerState For(Transform swimmer)
    {
        if (swimmer == null) return null;
        ByTransform.TryGetValue(swimmer, out MatchPlayerState state);
        return state;
    }

    public static bool IsGameplayEligible(Transform swimmer)
    {
        if (swimmer == null || !swimmer.gameObject.activeInHierarchy) return false;
        MatchPlayerState state = For(swimmer);
        return state == null || state.GameplayEligible;
    }

    public static bool AllowsNormalControl(Transform swimmer)
    {
        if (!IsGameplayEligible(swimmer)) return false;
        MatchPlayerState state = For(swimmer);
        if (state != null && state.MovePurpose != MatchMovePurpose.None) return false;
        MatchContext ctx = MatchContext.Instance;
        return ctx == null || !ctx.CompetitivePlayStopped;
    }
}

// Centralized description of one team's legal flying-substitution section.  The points are
// recomputed from the team's CURRENT defending end, so the same physical scene landmarks change
// ownership automatically after the Q3 end swap.
public struct FlyingSubstitutionArea
{
    public Vector2 BenchStaging;
    public Vector2 ExchangeInside;
    public Vector2 ExchangeOutside;
    public Vector2 EntryInside;
    public float OwnHalfMinX;
    public float OwnHalfMaxX;

    public Vector2 ClampToOwnHalf(Vector2 point)
    {
        point.x = Mathf.Clamp(point.x, OwnHalfMinX, OwnHalfMaxX);
        return point;
    }
}

// One-time scene-derived geometry. Colliders establish the pool walls, authored bench-side
// landmarks refine the presentation, and the current defendGoal establishes each team's end.
// Every named scene lookup is optional and geometrically validated; safe derived fallbacks keep
// the system working if the PoolB art is renamed or replaced.
public sealed class PoolMatchGeometry
{
    private readonly MatchContext context;
    private readonly List<Collider2D> poolBoundaries = new List<Collider2D>();
    private readonly List<Transform> benchVisuals = new List<Transform>();
    private readonly List<Transform> exclusionMarkers = new List<Transform>();
    private Collider2D benchSideBoundary;
    private Transform negativeEndFlyingMarker;
    private Transform positiveEndFlyingMarker;
    private Transform halfwayMarker;
    private float topInsideY = 3.72f;
    private float topOutsideY = 4.18f;
    private bool exclusionMarkerRetryComplete;

    public PoolMatchGeometry(MatchContext context)
    {
        this.context = context;
        Discover();
    }

    void Discover()
    {
        Collider2D top = null;
        Collider2D namedBenchSide = null;
        PoolLineFloat[] lines = UnityEngine.Object.FindObjectsByType<PoolLineFloat>(
            FindObjectsInactive.Include);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i] == null || lines[i].GetComponentInParent<Goal>() != null ||
                lines[i].GetComponentInChildren<Goal>() != null) continue;
            Collider2D[] colliders = lines[i].GetComponentsInChildren<Collider2D>(true);
            for (int c = 0; c < colliders.Length; c++)
            {
                Collider2D boundary = colliders[c];
                if (boundary == null || !boundary.enabled) continue;
                if (!poolBoundaries.Contains(boundary)) poolBoundaries.Add(boundary);
                if (boundary.bounds.size.x < 5f) continue;
                if (lines[i].name == "horizontal-line_0" && boundary.bounds.center.y > 0f)
                    namedBenchSide = boundary;
                if (boundary.bounds.center.y > 0f &&
                    (top == null || boundary.bounds.center.y > top.bounds.center.y)) top = boundary;
            }
        }

        benchSideBoundary = namedBenchSide != null ? namedBenchSide : top;
        if (benchSideBoundary != null)
        {
            // PoolB's observed legal waiting neighbourhood is y ~= 4.20.  Deriving from the
            // actual line thickness gives that result without baking the scene coordinate in.
            topInsideY = benchSideBoundary.bounds.min.y - 0.12f;
            topOutsideY = benchSideBoundary.bounds.max.y + 0.12f;
        }
        DiscoverSceneLandmarks();
    }

    void DiscoverSceneLandmarks()
    {
        Scene scene = context != null ? context.gameObject.scene : default;
        if (scene.IsValid() && scene.isLoaded)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] descendants = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < descendants.Length; i++) CacheSceneLandmark(descendants[i]);
            }
            return;
        }

        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++) CacheSceneLandmark(all[i]);
    }

    void CacheSceneLandmark(Transform t)
    {
        if (t == null) return;
        string lower = t.name.ToLowerInvariant();
        if (lower.Contains("exclusionspot") || lower.Contains("exclusion_spot"))
        {
            if (!exclusionMarkers.Contains(t)) exclusionMarkers.Add(t);
        }
        else if (lower.Contains("players-bench") || lower.Contains("playerbench"))
        {
            if (!benchVisuals.Contains(t)) benchVisuals.Add(t);
        }

        // These are optional physical calibration cones found in the saved PoolB scene.
        // Their X sign identifies a pool END only; team/kit colour is never consulted.
        if (t.name == "green-conus-1_0" && t.position.x < 0f)
            negativeEndFlyingMarker = t;
        else if (t.name == "red-conus4 (1)" && t.position.x > 0f)
            positiveEndFlyingMarker = t;
        else if (t.name == "blue-conus2")
            halfwayMarker = t;
    }

    float DefendSign(TeamSide team)
    {
        if (team != null && team.defendGoal != null && Mathf.Abs(team.defendGoal.position.x) > 0.01f)
            return Mathf.Sign(team.defendGoal.position.x);
        return context != null && team == context.PlayerTeam ? -1f : 1f;
    }

    void OwnHalfBounds(TeamSide team, out float goalX, out float halfwayX,
                       out float minX, out float maxX)
    {
        goalX = team != null && team.defendGoal != null
            ? team.defendGoal.position.x : DefendSign(team) * 7f;
        float attackX = team != null && team.attackGoal != null
            ? team.attackGoal.position.x : -goalX;
        halfwayX = (goalX + attackX) * 0.5f;

        // PoolB authors the bench-side halfway cone at x ~= -0.01. Prefer it to the midpoint of
        // slightly asymmetric goal art, but only after proving it lies between both goals and on
        // the discovered bench-side boundary. Renamed/rebuilt pools retain the goal midpoint.
        float endMin = Mathf.Min(goalX, attackX);
        float endMax = Mathf.Max(goalX, attackX);
        if (halfwayMarker != null && halfwayMarker.position.x > endMin + 0.5f &&
            halfwayMarker.position.x < endMax - 0.5f &&
            Mathf.Abs(halfwayMarker.position.y - topOutsideY) <= 1.2f)
            halfwayX = halfwayMarker.position.x;

        // Keep exchange/entry targets visibly inside the team's own legal half.  The small inset
        // avoids a sprite straddling the goal/halfway line due to its visual width.
        float rawMin = Mathf.Min(goalX, halfwayX);
        float rawMax = Mathf.Max(goalX, halfwayX);
        minX = rawMin + 0.18f;
        maxX = rawMax - 0.18f;
        if (minX > maxX)
        {
            float middle = (goalX + halfwayX) * 0.5f;
            minX = maxX = middle;
        }
    }

    public float HalfwayX(TeamSide team)
    {
        OwnHalfBounds(team, out _, out float halfwayX, out _, out _);
        return halfwayX;
    }

    public FlyingSubstitutionArea GetFlyingSubstitutionArea(TeamSide team)
    {
        OwnHalfBounds(team, out float goalX, out float halfwayX,
                      out float ownHalfMinX, out float ownHalfMaxX);
        float sign = DefendSign(team);
        Transform authored = sign < 0f ? negativeEndFlyingMarker : positiveEndFlyingMarker;

        // Prefer the authored PoolB cone only when it is actually on the discovered bench-side
        // line and between this team's goal line and halfway. Otherwise use the middle of that
        // geometrically legal interval. No caller needs to know scene object names or coordinates.
        float exchangeX = Mathf.Lerp(goalX, halfwayX, 0.45f);
        if (authored != null && authored.position.x >= ownHalfMinX &&
            authored.position.x <= ownHalfMaxX &&
            Mathf.Abs(authored.position.y - topOutsideY) <= 1.2f)
            exchangeX = authored.position.x;
        exchangeX = Mathf.Clamp(exchangeX, ownHalfMinX, ownHalfMaxX);

        Vector2 bench = BenchBase(team);
        return new FlyingSubstitutionArea
        {
            BenchStaging = bench,
            ExchangeInside = new Vector2(exchangeX, topInsideY),
            ExchangeOutside = new Vector2(exchangeX, topOutsideY),
            EntryInside = new Vector2(exchangeX, topInsideY - 0.38f),
            OwnHalfMinX = ownHalfMinX,
            OwnHalfMaxX = ownHalfMaxX
        };
    }

    public Vector2 SubstitutionInside(TeamSide team)
        => GetFlyingSubstitutionArea(team).ExchangeInside;

    public Vector2 SubstitutionOutside(TeamSide team)
        => GetFlyingSubstitutionArea(team).ExchangeOutside;

    Vector2 BenchBase(TeamSide team)
    {
        float sign = DefendSign(team);
        Transform best = null;
        float bestScore = float.PositiveInfinity;
        float goalX = team != null && team.defendGoal != null
            ? team.defendGoal.position.x : sign * 7f;
        for (int i = 0; i < benchVisuals.Count; i++)
        {
            Transform candidate = benchVisuals[i];
            if (candidate == null || Mathf.Sign(candidate.position.x) != sign) continue;
            float score = Mathf.Abs(candidate.position.x - goalX);
            if (score < bestScore) { bestScore = score; best = candidate; }
        }
        if (best != null)
        {
            Renderer renderer = best.GetComponent<Renderer>();
            if (renderer == null) renderer = best.GetComponentInChildren<Renderer>();
            return renderer != null ? (Vector2)renderer.bounds.center : (Vector2)best.position;
        }
        return new Vector2(Mathf.Lerp(goalX, 0f, 0.2f), topOutsideY + 1.05f);
    }

    public Vector2 BenchPoint(TeamSide team, int ordinal)
    {
        Vector2 basePoint = BenchBase(team);
        int row = ordinal / 4;
        int column = ordinal % 4;
        return basePoint + new Vector2((column - 1.5f) * 0.42f, row * 0.30f);
    }

    // Timeout organization deliberately uses the same coach/bench side, but is a staggered
    // tactical spread rather than the Q1 circle. Every target remains in the current defensive
    // half and therefore swaps ends with defendGoal before Q3.
    public Vector2 TimeoutGatheringPoint(TeamSide team, int ordinal, int participantCount)
    {
        OwnHalfBounds(team, out float goalX, out float halfwayX,
                      out float ownHalfMinX, out float ownHalfMaxX);
        float baseX = Mathf.Lerp(goalX, halfwayX, 0.38f);
        int columns = Mathf.Clamp(participantCount, 1, 3);
        int column = ordinal % columns;
        int row = ordinal / columns;
        float directionToHalf = Mathf.Sign(halfwayX - goalX);
        float centeredColumn = column - (columns - 1) * 0.5f;
        float x = baseX + directionToHalf * centeredColumn * 0.62f;
        float y = topInsideY - 0.52f - row * 0.72f;
        return new Vector2(Mathf.Clamp(x, ownHalfMinX, ownHalfMaxX), y);
    }

    public Vector2 TimeoutKeeperPoint(TeamSide team)
    {
        if (team == null || team.defendGoal == null) return Vector2.zero;
        Vector2 goal = team.defendGoal.position;
        float directionToField = team.attackGoal != null
            ? Mathf.Sign(team.attackGoal.position.x - goal.x) : -Mathf.Sign(goal.x);
        return new Vector2(goal.x + directionToField * 0.18f, goal.y);
    }

    Transform ResolveAuthoredExclusionSpot(TeamSide team)
    {
        float sign = DefendSign(team);
        Transform best = null;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < exclusionMarkers.Count; i++)
        {
            Transform marker = exclusionMarkers[i];
            if (marker == null || Mathf.Sign(marker.position.x) != sign) continue;
            float score = team != null && team.defendGoal != null
                ? Vector2.Distance(marker.position, team.defendGoal.position) : 0f;
            if (score < bestScore) { bestScore = score; best = marker; }
        }
        if (best != null) return best;

        // Geometry is constructed from a component added during MatchContext.Awake. If a scene
        // landmark was not registered in that first lifecycle pass, retry against this scene's
        // actual root hierarchy before ever accepting a generated corner fallback.
        if (!exclusionMarkerRetryComplete)
        {
            exclusionMarkerRetryComplete = true;
            DiscoverSceneLandmarks();
            return ResolveAuthoredExclusionSpot(team);
        }
        return null;
    }

    public bool TryGetAuthoredExclusionSpot(TeamSide team, out Vector2 position)
    {
        Transform marker = ResolveAuthoredExclusionSpot(team);
        position = marker != null ? (Vector2)marker.position : default;
        return marker != null;
    }

    public Vector2 ExclusionReentrySpot(TeamSide team)
    {
        if (TryGetAuthoredExclusionSpot(team, out Vector2 authored)) return authored;

        // PoolB authors the physical re-entry spots on the bench-side edge beside each defensive
        // goal. If a future scene omits those markers, stay on that same edge; never fall back to
        // an unrelated bottom corner. The current defendGoal sign makes this swap with the teams
        // before Q3 without tying the physical anchor to a kit colour or Home/Away label.
        float sign = DefendSign(team);
        float goalX = team != null && team.defendGoal != null
            ? team.defendGoal.position.x : sign * 7f;
        return new Vector2(goalX + sign * 0.055f, topInsideY - 0.04f);
    }

    // Bench bodies approach the authored bench-side re-entry point from outside active play.
    public Vector2 ExclusionBenchApproach(TeamSide team)
    {
        Vector2 area = ExclusionReentrySpot(team);
        return new Vector2(area.x, topOutsideY + 0.12f);
    }

    public bool ValidateScriptedDestination(MatchPlayerState player, MatchMovePurpose purpose,
                                            MatchMoveAnchor anchor, Vector2 destination,
                                            out string reason)
    {
        reason = string.Empty;
        if (!float.IsFinite(destination.x) || !float.IsFinite(destination.y))
        {
            reason = "Destination is not finite.";
            return false;
        }
        if (anchor == MatchMoveAnchor.Unspecified) return true;

        bool compatible;
        switch (anchor)
        {
            case MatchMoveAnchor.Q1Huddle:
            case MatchMoveAnchor.SprintStart:
                compatible = purpose == MatchMovePurpose.Q1Huddle;
                break;
            case MatchMoveAnchor.TimeoutPosition:
                compatible = purpose == MatchMovePurpose.Timeout;
                break;
            case MatchMoveAnchor.FlyingSubstitutionExchange:
            case MatchMoveAnchor.FlyingSubstitutionEntry:
                compatible = purpose == MatchMovePurpose.Substitution;
                break;
            case MatchMoveAnchor.ExclusionReentry:
            case MatchMoveAnchor.ExclusionBenchApproach:
                compatible = purpose == MatchMovePurpose.Exclusion;
                break;
            case MatchMoveAnchor.Bench:
                compatible = purpose == MatchMovePurpose.Substitution ||
                             purpose == MatchMovePurpose.Exclusion ||
                             purpose == MatchMovePurpose.Timeout;
                break;
            case MatchMoveAnchor.Formation:
                compatible = purpose != MatchMovePurpose.None;
                break;
            default:
                compatible = true;
                break;
        }
        if (!compatible)
        {
            reason = $"Anchor {anchor} is incompatible with movement purpose {purpose}.";
            return false;
        }

        if (player == null || player.Team == null) return true;
        Vector2 expected;
        float tolerance;
        switch (anchor)
        {
            case MatchMoveAnchor.FlyingSubstitutionExchange:
            {
                FlyingSubstitutionArea area = GetFlyingSubstitutionArea(player.Team);
                float insideDistance = (destination - area.ExchangeInside).sqrMagnitude;
                float outsideDistance = (destination - area.ExchangeOutside).sqrMagnitude;
                if (Mathf.Min(insideDistance, outsideDistance) <= 0.35f * 0.35f) return true;
                reason = "Destination is outside the team's flying-substitution exchange anchors.";
                return false;
            }
            case MatchMoveAnchor.FlyingSubstitutionEntry:
                expected = GetFlyingSubstitutionArea(player.Team).EntryInside;
                tolerance = 0.35f;
                break;
            case MatchMoveAnchor.ExclusionReentry:
                expected = ExclusionReentrySpot(player.Team);
                tolerance = 0.35f; // includes the short hand-touch offset above the marker
                break;
            case MatchMoveAnchor.ExclusionBenchApproach:
                expected = ExclusionBenchApproach(player.Team);
                tolerance = 0.35f;
                break;
            default:
                return true;
        }
        if ((destination - expected).sqrMagnitude <= tolerance * tolerance) return true;

        reason = $"Destination does not match the team's {anchor} anchor at {expected}.";
        return false;
    }

    public void SetBoundaryCollisionIgnored(MatchPlayerState player, bool ignored)
    {
        if (player == null) return;
        Collider2D[] swimmers = player.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < swimmers.Length; i++)
            for (int b = 0; b < poolBoundaries.Count; b++)
                if (swimmers[i] != null && poolBoundaries[b] != null &&
                    swimmers[i] != poolBoundaries[b])
                    Physics2D.IgnoreCollision(swimmers[i], poolBoundaries[b], ignored);
    }
}

[DefaultExecutionOrder(-450)]
public sealed class MatchSquadManager : MonoBehaviour
{
    private struct FieldTeamVisualIdentity
    {
        public Color capTint;
        public Color swimwearTint;
    }

    public static MatchSquadManager Instance { get; private set; }
    public PoolMatchGeometry Geometry { get; private set; }
    public IReadOnlyList<MatchPlayerState> Participants => participants;

    private readonly List<MatchPlayerState> participants = new List<MatchPlayerState>();
    private readonly Dictionary<Transform, MatchPlayerState> byBody =
        new Dictionary<Transform, MatchPlayerState>();
    private readonly Dictionary<TeamSide, FieldTeamVisualIdentity> teamVisualIdentities =
        new Dictionary<TeamSide, FieldTeamVisualIdentity>();
    private MatchContext context;
    private bool initialized;

    public static MatchSquadManager Ensure(MatchContext owner)
    {
        if (Instance != null) return Instance;
        MatchSquadManager manager = owner.GetComponent<MatchSquadManager>();
        if (manager == null) manager = owner.gameObject.AddComponent<MatchSquadManager>();
        return manager;
    }

    void Awake()
    {
        Instance = this;
        Initialize();
    }

    void Start()
    {
        // TeamManager's serialized six remain authoritative; this adds match-only bench bodies to
        // its control pool without altering the scene asset or saved starter lineup.
        for (int i = 0; i < participants.Count; i++)
        {
            MatchPlayerState state = participants[i];
            if (state != null && state.HumanTeam)
                TeamManager.RegisterRuntimePlayer(state.GetComponent<PlayerMovement>(),
                                                  state.GetComponent<TeammateAI>());
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Initialize()
    {
        if (initialized) return;
        context = MatchContext.Instance != null ? MatchContext.Instance : GetComponent<MatchContext>();
        if (context == null) return;
        initialized = true;
        Geometry = new PoolMatchGeometry(context);
        MatchPresentationContext.Restore(); // championship opponent identity before bot bench names

        PlayerData[] starters = RosterManager.Instance.GetStarters();
        CaptureTeamVisualIdentity(context.PlayerTeam);
        CaptureTeamVisualIdentity(context.BotTeam);
        BindStartingTeam(context.PlayerTeam, true, starters);
        BindStartingTeam(context.BotTeam, false, null);
        BuildHumanBench(starters);
        BuildBotBench();
    }

    void BindStartingTeam(TeamSide team, bool human, PlayerData[] starters)
    {
        if (team == null || team.members == null) return;
        for (int slot = 0; slot < team.members.Length; slot++)
        {
            Transform body = team.members[slot];
            if (body == null) continue;
            PlayerPosition position = PositionForRole(slot);
            PlayerData data = human && starters != null && (int)position < starters.Length
                ? starters[(int)position] : null;
            MatchPlayerState state = body.GetComponent<MatchPlayerState>();
            if (state == null) state = body.gameObject.AddComponent<MatchPlayerState>();
            string prefix = human ? "home" : "away";
            string fallbackName = human ? "PLAYER " + (slot + 2) : "OPPONENT " + (slot + 2);
            state.Bind(data, prefix + "_starter_" + slot, fallbackName, position,
                       human ? 65 : 70, slot + 2, team, human, slot,
                       MatchPlayerStatus.OnField, true);
            ApplyTeamVisualIdentity(body, team);
            AddParticipant(state);
        }
    }

    void BuildHumanBench(PlayerData[] starters)
    {
        if (context.PlayerTeam == null || context.PlayerTeam.members == null) return;
        HashSet<string> starterIds = new HashSet<string>();
        if (starters != null)
            for (int i = 0; i < starters.Length; i++)
                if (starters[i] != null && !string.IsNullOrEmpty(starters[i].id))
                    starterIds.Add(starters[i].id);

        List<PlayerData> owned = RosterManager.Instance.GetOwnedPlayers();
        int benchIndex = 0;
        for (int i = 0; i < owned.Count; i++)
        {
            PlayerData data = owned[i];
            if (data == null || data.position == PlayerPosition.GK || starterIds.Contains(data.id))
                continue;
            int preferredSlot = RoleForPosition(data.position);
            Transform template = TemplateFor(context.PlayerTeam, preferredSlot);
            if (template == null) continue;
            CreateBenchBody(template, context.PlayerTeam, true, data, data.id, data.fullName,
                            data.position, data.overall, 8 + benchIndex, preferredSlot, benchIndex);
            benchIndex++;
        }
    }

    void BuildBotBench()
    {
        TeamSide team = context.BotTeam;
        if (team == null || team.members == null) return;
        string club = MatchPresentationContext.OpponentClub;
        if (string.IsNullOrEmpty(club)) club = "OPPONENT";
        for (int slot = 0; slot < team.members.Length; slot++)
        {
            Transform template = TemplateFor(team, slot);
            if (template == null) continue;
            PlayerPosition position = PositionForRole(slot);
            CreateBenchBody(template, team, false, null, "bot_bench_" + slot,
                            club + " " + (slot + 8), position, 68 + (slot % 3) * 2,
                            slot + 8, slot, slot);
        }
    }

    MatchPlayerState CreateBenchBody(Transform template, TeamSide team, bool human,
                                     PlayerData data, string id, string displayName,
                                     PlayerPosition position, int overall, int capNumber,
                                     int preferredSlot, int benchIndex)
    {
        Vector2 point = Geometry.BenchPoint(team, benchIndex);
        GameObject clone = Instantiate(template.gameObject,
            new Vector3(point.x, point.y, template.position.z), template.rotation);
        clone.name = (human ? "Home" : "Away") + "Bench_" + id;
        ApplyTeamVisualIdentity(clone.transform, team);
        PlayerMovement pm = clone.GetComponent<PlayerMovement>();
        if (pm != null) pm.IsActive = false;
        IAgentBody agent = clone.GetComponent<IAgentBody>();
        if (agent != null) agent.IsHolding = false;

        MatchPlayerState state = clone.GetComponent<MatchPlayerState>();
        if (state == null) state = clone.AddComponent<MatchPlayerState>();
        state.Bind(data, id, displayName, position, overall, capNumber, team, human,
                   preferredSlot, MatchPlayerStatus.Bench, false);
        state.PlaceAt(point);
        AddParticipant(state);
        return state;
    }

    void AddParticipant(MatchPlayerState state)
    {
        if (state == null || participants.Contains(state)) return;
        participants.Add(state);
        byBody[state.transform] = state;
    }

    public MatchPlayerState StateOf(Transform body)
    {
        if (body == null) return null;
        byBody.TryGetValue(body, out MatchPlayerState state);
        return state;
    }

    public List<MatchPlayerState> PlayersFor(TeamSide team)
    {
        List<MatchPlayerState> result = new List<MatchPlayerState>();
        for (int i = 0; i < participants.Count; i++)
            if (participants[i] != null && participants[i].Team == team) result.Add(participants[i]);
        return result;
    }

    public int MemberIndex(TeamSide team, Transform body)
    {
        if (team == null || team.members == null || body == null) return -1;
        for (int i = 0; i < team.members.Length; i++)
            if (team.members[i] == body) return i;
        return -1;
    }

    public bool RemoveFromField(MatchPlayerState player)
    {
        if (player == null || player.Team == null || player.Team.members == null) return false;
        int slot = MemberIndex(player.Team, player.transform);
        if (slot < 0) slot = player.RoleSlot;
        if (slot >= 0 && slot < player.Team.members.Length &&
            player.Team.members[slot] == player.transform)
            player.Team.members[slot] = null;
        player.SetRoleSlot(slot);
        player.SetLegalOnField(false);
        return slot >= 0;
    }

    public bool AssignToField(MatchPlayerState player, int slot, MatchPlayerStatus status)
    {
        if (player == null || player.Team == null || player.Team.members == null ||
            slot < 0 || slot >= player.Team.members.Length) return false;
        Transform occupant = player.Team.members[slot];
        if (occupant != null && occupant != player.transform) return false;

        // A runtime athlete can own exactly one legal slot.
        for (int i = 0; i < player.Team.members.Length; i++)
            if (player.Team.members[i] == player.transform) player.Team.members[i] = null;
        player.Team.members[slot] = player.transform;
        player.SetRoleSlot(slot);
        player.SetStatus(status, true);
        ApplyTeamVisualIdentity(player.transform, player.Team);
        return true;
    }

    // Capture once from the same authored field-body path that made starters look correct. The
    // dictionary key is TeamSide identity, never defendGoal/left/right, so Q3 moves geometry only.
    void CaptureTeamVisualIdentity(TeamSide team)
    {
        if (team == null || team.members == null || teamVisualIdentities.ContainsKey(team)) return;
        for (int i = 0; i < team.members.Length; i++)
        {
            Transform body = team.members[i];
            if (body == null) continue;
            PlayerAnimator playerAnimator = body.GetComponent<PlayerAnimator>();
            if (playerAnimator != null)
            {
                playerAnimator.GetConfiguredTeamPalette(out Color cap, out Color swimwear);
                teamVisualIdentities[team] = new FieldTeamVisualIdentity
                    { capTint = cap, swimwearTint = swimwear };
                return;
            }
            BotAnimator botAnimator = body.GetComponent<BotAnimator>();
            if (botAnimator == null) continue;
            botAnimator.GetAuthoredTeamPalette(out Color botCap, out Color botSwimwear);
            teamVisualIdentities[team] = new FieldTeamVisualIdentity
                { capTint = botCap, swimwearTint = botSwimwear };
            return;
        }
    }

    void ApplyTeamVisualIdentity(Transform body, TeamSide team)
    {
        if (body == null || team == null) return;
        if (!teamVisualIdentities.TryGetValue(team, out FieldTeamVisualIdentity identity))
        {
            CaptureTeamVisualIdentity(team);
            if (!teamVisualIdentities.TryGetValue(team, out identity)) return;
        }

        PlayerAnimator playerAnimator = body.GetComponent<PlayerAnimator>();
        if (playerAnimator != null)
            playerAnimator.ApplyMatchTeamPalette(identity.capTint, identity.swimwearTint);
        BotAnimator botAnimator = body.GetComponent<BotAnimator>();
        if (botAnimator != null)
            botAnimator.ApplyMatchTeamPalette(identity.capTint, identity.swimwearTint);
    }

    public bool IsCompatible(MatchPlayerState outgoing, MatchPlayerState incoming)
    {
        if (outgoing == null || incoming == null || outgoing.Team != incoming.Team) return false;
        bool outgoingKeeper = outgoing.Position == PlayerPosition.GK;
        bool incomingKeeper = incoming.Position == PlayerPosition.GK;
        return outgoingKeeper == incomingKeeper; // all six outfield roles are legally interchangeable
    }

    public MatchPlayerState BestBenchReplacement(MatchPlayerState outgoing,
                                                  float minimumFreshnessGain = 0f)
    {
        if (outgoing == null) return null;
        MatchPlayerState best = null;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < participants.Count; i++)
        {
            MatchPlayerState candidate = participants[i];
            if (candidate == null || candidate.Team != outgoing.Team || !candidate.AvailableOnBench ||
                !IsCompatible(outgoing, candidate)) continue;
            // Positive gains are a coach-suggestion quality gate. Mandatory replacements pass
            // zero and must choose the best legal athlete even when every bench player is tired.
            if (minimumFreshnessGain > 0f &&
                candidate.StaminaPercent < outgoing.StaminaPercent + minimumFreshnessGain) continue;
            float compatibility = candidate.Position == outgoing.Position ? 35f :
                                  SameRoleFamily(candidate.Position, outgoing.Position) ? 15f : 0f;
            float score = candidate.StaminaPercent * 100f + candidate.Overall * 0.35f + compatibility;
            if (score > bestScore || (Mathf.Approximately(score, bestScore) &&
                                      string.CompareOrdinal(candidate.PlayerId, best?.PlayerId) < 0))
            { bestScore = score; best = candidate; }
        }
        return best;
    }

    public int AvailableNonDisqualifiedCount(TeamSide team)
    {
        int count = 0;
        for (int i = 0; i < participants.Count; i++)
        {
            MatchPlayerState player = participants[i];
            if (player != null && player.Team == team && player.Position != PlayerPosition.GK &&
                !player.PermanentlyDisqualified) count++;
        }
        return count;
    }

    public Vector2 FormationPoint(MatchPlayerState player)
    {
        if (player == null || player.Team == null) return Vector2.zero;
        return player.Team.RestartFormationSpot(player.transform,
            MatchContext.Instance != null && MatchContext.Instance.PossessingTeam == player.Team);
    }

    public void OnEndsSwapped()
    {
        Dictionary<TeamSide, int> ordinals = new Dictionary<TeamSide, int>();
        for (int i = 0; i < participants.Count; i++)
        {
            MatchPlayerState player = participants[i];
            if (player == null || (player.Status != MatchPlayerStatus.Bench &&
                                   player.Status != MatchPlayerStatus.PermanentlyOut &&
                                   player.Status != MatchPlayerStatus.ExcludedReplacedBench)) continue;
            ordinals.TryGetValue(player.Team, out int ordinal);
            ordinals[player.Team] = ordinal + 1;
            player.StopMove();
            player.PlaceAt(Geometry.BenchPoint(player.Team, ordinal));
        }
    }

    public void StopAllTransitions()
    {
        for (int i = 0; i < participants.Count; i++)
            if (participants[i] != null) participants[i].StopMove();
    }

    static Transform TemplateFor(TeamSide team, int preferredSlot)
    {
        if (team == null || team.members == null || team.members.Length == 0) return null;
        if (preferredSlot >= 0 && preferredSlot < team.members.Length &&
            team.members[preferredSlot] != null) return team.members[preferredSlot];
        for (int i = 0; i < team.members.Length; i++)
            if (team.members[i] != null) return team.members[i];
        return null;
    }

    public static PlayerPosition PositionForRole(int roleSlot)
    {
        switch (roleSlot)
        {
            case 0: return PlayerPosition.CF;
            case 1: return PlayerPosition.CB;
            case 2: return PlayerPosition.LW;
            case 3: return PlayerPosition.RW;
            case 4: return PlayerPosition.LF;
            default: return PlayerPosition.RF;
        }
    }

    public static int RoleForPosition(PlayerPosition position)
    {
        switch (position)
        {
            case PlayerPosition.CF: return 0;
            case PlayerPosition.CB: return 1;
            case PlayerPosition.LW: return 2;
            case PlayerPosition.RW: return 3;
            case PlayerPosition.LF: return 4;
            case PlayerPosition.RF: return 5;
            default: return -1;
        }
    }

    static bool SameRoleFamily(PlayerPosition a, PlayerPosition b)
    {
        if (a == b) return true;
        bool aWide = a == PlayerPosition.LW || a == PlayerPosition.RW ||
                     a == PlayerPosition.LF || a == PlayerPosition.RF;
        bool bWide = b == PlayerPosition.LW || b == PlayerPosition.RW ||
                     b == PlayerPosition.LF || b == PlayerPosition.RF;
        return aWide && bWide;
    }
}
