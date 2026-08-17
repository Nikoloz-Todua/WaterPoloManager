using System;
using System.Collections.Generic;
using UnityEngine;

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
                          bool ignorePoolBoundaries = false)
    {
        if (purpose == MatchMovePurpose.None || (int)purpose < (int)MovePurpose) return false;

        if (moveIgnoresPoolBoundaries != ignorePoolBoundaries)
        {
            MatchSquadManager.Instance?.Geometry.SetBoundaryCollisionIgnored(this, ignorePoolBoundaries);
            moveIgnoresPoolBoundaries = ignorePoolBoundaries;
        }

        MovePurpose = purpose;
        MoveTarget = target;
        moveSpeed = Mathf.Max(0.1f, speed);
        moveArrivalRadius = Mathf.Max(0.02f, arrivalRadius);
        moveAllowDuringFullFreeze = allowDuringFullFreeze;
        AtMoveTarget = false;
        return true;
    }

    public void Retarget(MatchMovePurpose purpose, Vector2 target)
    {
        if (MovePurpose != purpose) return;
        if ((MoveTarget - target).sqrMagnitude <= 0.0001f) return;
        MoveTarget = target;
        AtMoveTarget = false;
    }

    public void StopMove(MatchMovePurpose purpose = MatchMovePurpose.None)
    {
        if (purpose != MatchMovePurpose.None && MovePurpose != purpose) return;
        if (moveIgnoresPoolBoundaries)
            MatchSquadManager.Instance?.Geometry.SetBoundaryCollisionIgnored(this, false);
        moveIgnoresPoolBoundaries = false;
        MovePurpose = MatchMovePurpose.None;
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

// One-time scene-derived geometry.  No GameObject names are required for correct operation:
// colliders establish the pool walls and the current defendGoal establishes each team's end.
// Existing bench/exclusion transforms are used when present, with geometry fallbacks otherwise.
public sealed class PoolMatchGeometry
{
    private readonly MatchContext context;
    private readonly List<Collider2D> poolBoundaries = new List<Collider2D>();
    private readonly List<Transform> benchVisuals = new List<Transform>();
    private readonly List<Transform> exclusionMarkers = new List<Transform>();
    private float topInsideY = 3.72f;
    private float topOutsideY = 4.18f;
    private float bottomInsideY = -3.72f;

    public PoolMatchGeometry(MatchContext context)
    {
        this.context = context;
        Discover();
    }

    void Discover()
    {
        Collider2D top = null;
        Collider2D bottom = null;
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
                if (boundary.bounds.center.y > 0f &&
                    (top == null || boundary.bounds.center.y > top.bounds.center.y)) top = boundary;
                if (boundary.bounds.center.y < 0f &&
                    (bottom == null || boundary.bounds.center.y < bottom.bounds.center.y)) bottom = boundary;
            }
        }

        if (top != null)
        {
            topInsideY = top.bounds.min.y - 0.16f;
            topOutsideY = top.bounds.max.y + 0.20f;
        }
        if (bottom != null) bottomInsideY = bottom.bounds.max.y + 0.20f;

        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null) continue;
            string lower = t.name.ToLowerInvariant();
            if (lower.Contains("exclusionspot") || lower.Contains("exclusion_spot"))
                exclusionMarkers.Add(t);
            else if (lower.Contains("players-bench") || lower.Contains("playerbench"))
                benchVisuals.Add(t);
        }
    }

    float DefendSign(TeamSide team)
    {
        if (team != null && team.defendGoal != null && Mathf.Abs(team.defendGoal.position.x) > 0.01f)
            return Mathf.Sign(team.defendGoal.position.x);
        return context != null && team == context.PlayerTeam ? -1f : 1f;
    }

    float DefensiveExchangeX(TeamSide team)
    {
        float goalX = team != null && team.defendGoal != null
            ? team.defendGoal.position.x : DefendSign(team) * 7f;
        return goalX * 0.78f;
    }

    public Vector2 SubstitutionInside(TeamSide team)
        => new Vector2(DefensiveExchangeX(team), topInsideY);

    public Vector2 SubstitutionOutside(TeamSide team)
        => new Vector2(DefensiveExchangeX(team), topOutsideY);

    public Vector2 BenchPoint(TeamSide team, int ordinal)
    {
        float sign = DefendSign(team);
        Transform best = null;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < benchVisuals.Count; i++)
        {
            Transform candidate = benchVisuals[i];
            if (candidate == null || Mathf.Sign(candidate.position.x) != sign) continue;
            float score = Mathf.Abs(candidate.position.x - DefensiveExchangeX(team));
            if (score < bestScore) { bestScore = score; best = candidate; }
        }

        Vector2 basePoint = best != null
            ? (Vector2)best.position
            : new Vector2(DefensiveExchangeX(team), topOutsideY + 1.05f);
        int row = ordinal / 4;
        int column = ordinal % 4;
        return basePoint + new Vector2((column - 1.5f) * 0.42f, row * 0.30f);
    }

    public Vector2 ExclusionArea(TeamSide team)
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
        if (best != null) return best.position;

        float goalX = team != null && team.defendGoal != null
            ? team.defendGoal.position.x : sign * 7f;
        return new Vector2(goalX + sign * 0.18f, bottomInsideY - 0.42f);
    }

    public Vector2 ExclusionEntryInside(TeamSide team)
    {
        float sign = DefendSign(team);
        float goalX = team != null && team.defendGoal != null
            ? team.defendGoal.position.x : sign * 7f;
        float limit = context != null ? context.PlayerLimitX : 6.9f;
        return new Vector2(Mathf.Clamp(goalX - sign * 0.55f, -limit + 0.1f, limit - 0.1f),
                           bottomInsideY + 0.28f);
    }

    // Bench bodies are staged above the top wall while the authored re-entry markers sit by the
    // bottom wall. Route a replacement behind its own goal line instead of cutting diagonally
    // through live play as an ineligible ghost swimmer.
    public Vector2 ExclusionBenchLane(TeamSide team)
    {
        Vector2 area = ExclusionArea(team);
        return new Vector2(area.x, topOutsideY + 0.12f);
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
    public static MatchSquadManager Instance { get; private set; }
    public PoolMatchGeometry Geometry { get; private set; }
    public IReadOnlyList<MatchPlayerState> Participants => participants;

    private readonly List<MatchPlayerState> participants = new List<MatchPlayerState>();
    private readonly Dictionary<Transform, MatchPlayerState> byBody =
        new Dictionary<Transform, MatchPlayerState>();
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
        return true;
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
