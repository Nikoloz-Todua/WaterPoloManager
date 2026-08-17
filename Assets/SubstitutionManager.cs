using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Authoritative match substitution state machine.  There is deliberately no match-wide
// substitution counter: exchanges are unlimited whenever a valid bench athlete is available.
[DefaultExecutionOrder(-350)]
public sealed class SubstitutionManager : MonoBehaviour
{
    public static SubstitutionManager Instance { get; private set; }

    [Header("Physical exchange")]
    [SerializeField] private float choreographySpeed = 4.2f;
    [SerializeField] private float exchangeArrivalRadius = 0.16f;
    [SerializeField] private float handTouchSeconds = 0.45f;
    [SerializeField] private float formationArrivalRadius = 0.24f;

    [Header("Coach suggestions")]
    [SerializeField] private float firstRoutineEvaluationSeconds = 32.5f;
    [SerializeField] private float routineStaminaThreshold = 0.40f;
    [SerializeField] private float urgentStaminaThreshold = 0.20f;
    [SerializeField] private float minimumFreshnessGain = 0.18f;
    [SerializeField] private float urgentFreshnessGain = 0.10f;
    [SerializeField] private int maxRoutineSuggestionsPerQuarter = 2;
    [SerializeField] private float suggestionCooldownSeconds = 20f;
    [SerializeField] private float suggestionLifetimeSeconds = 8f;
    [SerializeField] private float evaluationIntervalSeconds = 1f;

    private enum ExchangeKind { Live, ExclusionReplacement }
    private enum ExchangePhase { Approaching, HandTouch, Dispersing }

    private sealed class Exchange
    {
        public ExchangeKind kind;
        public ExchangePhase phase;
        public MatchPlayerState outgoing;
        public MatchPlayerState incoming;
        public TeamSide team;
        public int slot;
        public Vector2 fixedAnchor;
        public float phaseElapsed;
        public bool incomingReachedBenchLane;
        public Action<MatchPlayerState> exclusionTouchComplete;
        public bool callbackSent;
    }

    private sealed class PendingExchange
    {
        public MatchPlayerState outgoing;
        public MatchPlayerState incoming;
    }

    private readonly List<Exchange> active = new List<Exchange>();
    private readonly List<MatchPlayerState> returningToBench = new List<MatchPlayerState>();
    private readonly Dictionary<TeamSide, PendingExchange> pending =
        new Dictionary<TeamSide, PendingExchange>();
    private readonly HashSet<string> suggestedPairsThisQuarter = new HashSet<string>();
    private MatchContext context;
    private int trackedQuarter;
    private int humanSuggestionsThisQuarter;
    private int botSuggestionsThisQuarter;
    private float nextSuggestionTime;
    private float nextEvaluationTime;

    public bool HasActiveExchange => active.Count > 0;

    public static SubstitutionManager Ensure(MatchContext owner)
    {
        if (Instance != null) return Instance;
        SubstitutionManager manager = owner.GetComponent<SubstitutionManager>();
        if (manager == null) manager = owner.gameObject.AddComponent<SubstitutionManager>();
        return manager;
    }

    void Awake()
    {
        Instance = this;
        context = MatchContext.Instance != null ? MatchContext.Instance : GetComponent<MatchContext>();
        MatchSubstitutionSuggestionUI.Ensure(gameObject);
    }

    void Start()
    {
        trackedQuarter = MatchTimer.Instance != null ? MatchTimer.Instance.CurrentQuarter : 1;
        nextSuggestionTime = Time.time + firstRoutineEvaluationSeconds;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (Time.timeScale <= 0f) return; // hard pause owns absolutely everything
        StartReadyPendingExchanges();
        UpdateExchanges();
        UpdateBenchReturns();
        UpdateCoachSuggestions();
    }

    public bool RequestLive(MatchPlayerState outgoing, MatchPlayerState incoming,
                            out string validationError)
    {
        if (!ValidateLive(outgoing, incoming, out validationError)) return false;
        StartLiveExchange(outgoing, incoming);
        return true;
    }

    public bool QueuePending(MatchPlayerState outgoing, MatchPlayerState incoming,
                             out string validationError)
    {
        if (!ValidateLive(outgoing, incoming, out validationError)) return false;
        CancelPending(outgoing.Team);
        outgoing.SetPending(true);
        incoming.SetPending(true);
        pending[outgoing.Team] = new PendingExchange { outgoing = outgoing, incoming = incoming };
        validationError = "SUBSTITUTION PENDING";
        return true;
    }

    public bool HasPending(TeamSide team) => team != null && pending.ContainsKey(team);

    public string PendingDescription(TeamSide team)
    {
        if (team != null && pending.TryGetValue(team, out PendingExchange item) &&
            item.outgoing != null && item.incoming != null)
            return "PENDING: #" + item.outgoing.CapNumber + " " + item.outgoing.DisplayName +
                   "  →  #" + item.incoming.CapNumber + " " + item.incoming.DisplayName;
        return string.Empty;
    }

    public void CancelPending(TeamSide team)
    {
        if (team == null || !pending.TryGetValue(team, out PendingExchange item)) return;
        if (item.outgoing != null) item.outgoing.SetPending(false);
        if (item.incoming != null) item.incoming.SetPending(false);
        pending.Remove(team);
    }

    public void StartPending(TeamSide team)
    {
        if (team == null || !pending.TryGetValue(team, out PendingExchange item)) return;
        if (context != null && context.PlayFrozen && !context.WaterPoloStoppageActive)
            return; // retain the transaction until the duel/goal/penalty full-freeze releases
        pending.Remove(team);
        if (item.outgoing != null) item.outgoing.SetPending(false);
        if (item.incoming != null) item.incoming.SetPending(false);
        if (ValidateLive(item.outgoing, item.incoming, out string error))
            StartLiveExchange(item.outgoing, item.incoming);
        else if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Substitution cancelled - " + error);
    }

    void StartReadyPendingExchanges()
    {
        if (context == null || (context.PlayFrozen && !context.WaterPoloStoppageActive) ||
            pending.Count == 0) return;
        TeamSide player = context.PlayerTeam;
        TeamSide bot = context.BotTeam;
        if (player != null && pending.ContainsKey(player)) StartPending(player);
        if (bot != null && pending.ContainsKey(bot)) StartPending(bot);
    }

    public bool RequestExclusionReplacement(MatchPlayerState excluded,
                                            MatchPlayerState replacement,
                                            out string validationError)
    {
        if (ExclusionManager.Instance == null)
        {
            validationError = "No active exclusion service";
            return false;
        }
        return ExclusionManager.Instance.RequestReplacement(excluded, replacement,
                                                             out validationError);
    }

    // Team Management may revise an automatically chosen mandatory replacement while that
    // substitute is still only approaching the re-entry area. After the hand touch, the legal
    // identity waiting out the exclusion is fixed.
    public bool CancelUncompletedExclusionExchange(MatchPlayerState excluded)
    {
        if (excluded == null) return false;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Exchange exchange = active[i];
            if (exchange.kind != ExchangeKind.ExclusionReplacement ||
                exchange.outgoing != excluded || exchange.phase != ExchangePhase.Approaching)
                continue;
            CancelBrokenExchange(exchange);
            active.RemoveAt(i);
            return true;
        }
        return false;
    }

    // Called by ExclusionManager after rule validation.  The same exchange beat used by a live
    // substitution now brings the substitute from the bench to the exclusion re-entry area.
    public bool BeginExclusionExchange(MatchPlayerState excluded, MatchPlayerState replacement,
                                       Vector2 anchor, Action<MatchPlayerState> onTouchComplete,
                                       out string validationError)
    {
        validationError = string.Empty;
        if (excluded == null || replacement == null || excluded.Team != replacement.Team)
        { validationError = "Invalid exclusion replacement"; return false; }
        if (!replacement.AvailableOnBench || replacement.PermanentlyDisqualified)
        { validationError = "Replacement is not available"; return false; }
        if (IsInExchange(excluded) || IsInExchange(replacement))
        { validationError = "Player is already in a transition"; return false; }
        if (MatchSquadManager.Instance == null ||
            !MatchSquadManager.Instance.IsCompatible(excluded, replacement))
        { validationError = "Incompatible position"; return false; }

        replacement.SetPending(false);
        replacement.SetStatus(MatchPlayerStatus.ExclusionReplacementApproach, false);
        replacement.BeginMove(MatchMovePurpose.Exclusion,
                               MatchSquadManager.Instance.Geometry.ExclusionBenchLane(excluded.Team),
                               choreographySpeed, exchangeArrivalRadius,
                               true, true);
        active.Add(new Exchange
        {
            kind = ExchangeKind.ExclusionReplacement,
            phase = ExchangePhase.Approaching,
            outgoing = excluded,
            incoming = replacement,
            team = excluded.Team,
            slot = excluded.RoleSlot,
            fixedAnchor = anchor,
            exclusionTouchComplete = onTouchComplete
        });
        return true;
    }

    void StartLiveExchange(MatchPlayerState outgoing, MatchPlayerState incoming)
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        int slot = squad.MemberIndex(outgoing.Team, outgoing.transform);
        if (slot < 0) slot = outgoing.RoleSlot;

        DropHeldBall(outgoing);
        squad.RemoveFromField(outgoing); // legal count drops now; never two simultaneous players
        outgoing.SetStatus(MatchPlayerStatus.SubstitutingOut, false);
        incoming.SetStatus(MatchPlayerStatus.SubstitutingIn, false);
        outgoing.SetPending(false);
        incoming.SetPending(false);

        Vector2 inside = squad.Geometry.SubstitutionInside(outgoing.Team);
        Vector2 outsidePoint = squad.Geometry.SubstitutionOutside(outgoing.Team);
        outgoing.BeginMove(MatchMovePurpose.Substitution, inside, choreographySpeed,
                           exchangeArrivalRadius, true, true);
        incoming.BeginMove(MatchMovePurpose.Substitution, outsidePoint, choreographySpeed,
                           exchangeArrivalRadius, true, true);

        active.Add(new Exchange
        {
            kind = ExchangeKind.Live,
            phase = ExchangePhase.Approaching,
            outgoing = outgoing,
            incoming = incoming,
            team = outgoing.Team,
            slot = slot
        });
        TeamManager.EnsureValidActive();
        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Substitution - " + outgoing.DisplayName +
                                        " / " + incoming.DisplayName);
    }

    void UpdateExchanges()
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (squad == null) return;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Exchange exchange = active[i];
            if (!ExchangeObjectsValid(exchange))
            {
                CancelBrokenExchange(exchange);
                active.RemoveAt(i);
                continue;
            }

            if (exchange.phase == ExchangePhase.Approaching)
            {
                if (exchange.kind == ExchangeKind.Live)
                {
                    exchange.outgoing.Retarget(MatchMovePurpose.Substitution,
                        squad.Geometry.SubstitutionInside(exchange.team));
                    exchange.incoming.Retarget(MatchMovePurpose.Substitution,
                        squad.Geometry.SubstitutionOutside(exchange.team));
                }
                else if (!exchange.incomingReachedBenchLane)
                {
                    if (!exchange.incoming.AtMoveTarget) continue;
                    exchange.incomingReachedBenchLane = true;
                    exchange.incoming.BeginMove(MatchMovePurpose.Exclusion,
                        exchange.fixedAnchor + Vector2.up * 0.12f, choreographySpeed,
                        exchangeArrivalRadius, true, true);
                    continue;
                }

                bool outgoingReady = exchange.kind == ExchangeKind.ExclusionReplacement
                    ? Vector2.Distance(exchange.outgoing.transform.position, exchange.fixedAnchor) <=
                        exchangeArrivalRadius * 1.8f
                    : exchange.outgoing.AtMoveTarget;
                if (!outgoingReady || !exchange.incoming.AtMoveTarget) continue;

                exchange.phase = ExchangePhase.HandTouch;
                exchange.phaseElapsed = 0f;
                if (exchange.kind == ExchangeKind.Live)
                    exchange.outgoing.SetStatus(MatchPlayerStatus.WaitingForExchange, false);
                exchange.incoming.StopMove(exchange.kind == ExchangeKind.Live
                    ? MatchMovePurpose.Substitution : MatchMovePurpose.Exclusion);
                if (exchange.kind == ExchangeKind.Live)
                    exchange.outgoing.StopMove(MatchMovePurpose.Substitution);
                continue;
            }

            if (exchange.phase == ExchangePhase.HandTouch)
            {
                exchange.phaseElapsed += Time.deltaTime;
                if (exchange.phaseElapsed < handTouchSeconds) continue;

                if (exchange.kind == ExchangeKind.Live && !CompleteLiveTouch(exchange))
                {
                    active.RemoveAt(i);
                    continue;
                }
                if (exchange.kind == ExchangeKind.ExclusionReplacement)
                    CompleteExclusionTouch(exchange);
                exchange.phase = ExchangePhase.Dispersing;
                continue;
            }

            bool outgoingDone = exchange.outgoing.MovePurpose == MatchMovePurpose.None ||
                                exchange.outgoing.AtMoveTarget;
            bool incomingDone = exchange.kind == ExchangeKind.ExclusionReplacement ||
                                exchange.incoming.MovePurpose == MatchMovePurpose.None ||
                                exchange.incoming.AtMoveTarget;

            if (exchange.kind == ExchangeKind.Live && incomingDone)
            {
                exchange.incoming.StopMove(MatchMovePurpose.Substitution);
                exchange.incoming.SetStatus(MatchPlayerStatus.OnField, true);
            }
            if (outgoingDone)
            {
                MatchMovePurpose purpose = exchange.kind == ExchangeKind.Live
                    ? MatchMovePurpose.Substitution : MatchMovePurpose.Exclusion;
                exchange.outgoing.StopMove(purpose);
                if (exchange.outgoing.PermanentlyDisqualified)
                    exchange.outgoing.SetStatus(MatchPlayerStatus.PermanentlyOut, false);
                else if (exchange.kind == ExchangeKind.ExclusionReplacement)
                    exchange.outgoing.SetStatus(MatchPlayerStatus.ExcludedReplacedBench, false);
                else
                    exchange.outgoing.SetStatus(MatchPlayerStatus.Bench, false);
            }

            if (outgoingDone && incomingDone)
            {
                active.RemoveAt(i);
                TeamManager.EnsureValidActive();
            }
        }
    }

    bool CompleteLiveTouch(Exchange exchange)
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (!squad.AssignToField(exchange.incoming, exchange.slot,
                                 MatchPlayerStatus.SubstitutingIn))
        {
            CancelBrokenExchange(exchange);
            return false;
        }

        exchange.outgoing.SetStatus(MatchPlayerStatus.Bench, false);
        exchange.outgoing.BeginMove(MatchMovePurpose.Substitution,
            squad.Geometry.BenchPoint(exchange.team, exchange.outgoing.CapNumber),
            choreographySpeed, formationArrivalRadius, true, true);
        exchange.incoming.BeginMove(MatchMovePurpose.Substitution,
            squad.FormationPoint(exchange.incoming), choreographySpeed,
            formationArrivalRadius, true, true);
        TeamManager.EnsureValidActive();
        return true;
    }

    void CompleteExclusionTouch(Exchange exchange)
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        exchange.incoming.StopMove(MatchMovePurpose.Exclusion);
        exchange.incoming.SetStatus(MatchPlayerStatus.ExclusionReplacementWaiting, false);
        exchange.outgoing.SetStatus(exchange.outgoing.PermanentlyDisqualified
            ? MatchPlayerStatus.PermanentlyOut : MatchPlayerStatus.ExcludedReplacedBench, false);
        exchange.outgoing.BeginMove(MatchMovePurpose.Exclusion,
            squad.Geometry.BenchPoint(exchange.team, exchange.outgoing.CapNumber),
            choreographySpeed, formationArrivalRadius, true, true);

        if (!exchange.callbackSent)
        {
            exchange.callbackSent = true;
            exchange.exclusionTouchComplete?.Invoke(exchange.incoming);
        }
    }

    bool ValidateLive(MatchPlayerState outgoing, MatchPlayerState incoming,
                      out string validationError)
    {
        validationError = string.Empty;
        if (outgoing == null || incoming == null || outgoing == incoming)
        { validationError = "Select one OUT and one IN player"; return false; }
        if (MatchSquadManager.Instance == null || outgoing.Team == null ||
            outgoing.Team != incoming.Team)
        { validationError = "Players must be on the same team"; return false; }
        if (!outgoing.Selectable || !outgoing.GameplayEligible)
        { validationError = "OUT player is not eligible"; return false; }
        if (!incoming.AvailableOnBench)
        { validationError = "IN player is not available"; return false; }
        if (!MatchSquadManager.Instance.IsCompatible(outgoing, incoming))
        { validationError = "Incompatible goalkeeper/field position"; return false; }
        if (IsInExchange(outgoing) || IsInExchange(incoming))
        { validationError = "Player is already substituting"; return false; }
        if (MatchTimer.Instance != null && MatchTimer.Instance.MatchOver)
        { validationError = "Match is over"; return false; }
        if (PenaltyManager.Instance != null && PenaltyManager.Instance.Active &&
            (context == null || !context.WaterPoloStoppageActive))
        { validationError = "Wait for the penalty restart"; return false; }
        if (TimeoutManager.Instance != null &&
            TimeoutManager.Instance.IsProtectedRestartParticipant(outgoing))
        { validationError = "Choose another OUT player; this player owns the restart"; return false; }
        return true;
    }

    bool IsInExchange(MatchPlayerState player)
    {
        if (player == null) return false;
        for (int i = 0; i < active.Count; i++)
            if (active[i].outgoing == player || active[i].incoming == player) return true;
        return false;
    }

    bool ExchangeObjectsValid(Exchange exchange)
    {
        return exchange != null && exchange.outgoing != null && exchange.incoming != null &&
               exchange.team != null && !exchange.incoming.PermanentlyDisqualified;
    }

    void CancelBrokenExchange(Exchange exchange)
    {
        if (exchange == null) return;
        MatchSquadManager squad = MatchSquadManager.Instance;
        // ExclusionManager, not this exchange, owns the excluded swimmer's trip to the area.
        // Cancelling only the approaching substitute must not strand that swimmer mid-pool.
        if (exchange.outgoing != null && exchange.kind == ExchangeKind.Live)
            exchange.outgoing.StopMove();
        if (exchange.incoming != null)
        {
            exchange.incoming.StopMove();
            exchange.incoming.SetPending(false);
            if (!exchange.incoming.PermanentlyDisqualified)
            {
                exchange.incoming.SetStatus(MatchPlayerStatus.Bench, false);
                MatchSquadManager currentSquad = MatchSquadManager.Instance;
                if (currentSquad != null)
                {
                    exchange.incoming.BeginMove(MatchMovePurpose.Substitution,
                        currentSquad.Geometry.BenchPoint(exchange.incoming.Team,
                                                         exchange.incoming.CapNumber),
                        choreographySpeed, formationArrivalRadius, true, true);
                    if (!returningToBench.Contains(exchange.incoming))
                        returningToBench.Add(exchange.incoming);
                }
            }
        }
        if (exchange.kind == ExchangeKind.Live && squad != null && exchange.outgoing != null &&
            !exchange.outgoing.PermanentlyDisqualified)
        {
            squad.AssignToField(exchange.outgoing, exchange.slot, MatchPlayerStatus.OnField);
        }
        if (exchange.kind == ExchangeKind.ExclusionReplacement && !exchange.callbackSent)
        {
            exchange.callbackSent = true;
            exchange.exclusionTouchComplete?.Invoke(null);
        }
        TeamManager.EnsureValidActive();
    }

    void UpdateBenchReturns()
    {
        for (int i = returningToBench.Count - 1; i >= 0; i--)
        {
            MatchPlayerState player = returningToBench[i];
            if (player == null || player.MovePurpose == MatchMovePurpose.None)
            {
                returningToBench.RemoveAt(i);
                continue;
            }
            if (!player.AtMoveTarget) continue;
            player.StopMove(MatchMovePurpose.Substitution);
            if (!player.PermanentlyDisqualified)
                player.SetStatus(MatchPlayerStatus.Bench, false);
            returningToBench.RemoveAt(i);
        }
    }

    void DropHeldBall(MatchPlayerState player)
    {
        if (player == null || context == null || context.Ball == null ||
            !context.Ball.transform.IsChildOf(player.transform)) return;
        context.ForceDropHeldBall();
    }

    // Goal/quarter setup already owns a full-field positional reset.  Resolve every transaction
    // atomically first so that reset sees one legal body per slot and can never duplicate/revive one.
    public void ResolveForMatchStoppage()
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Exchange exchange = active[i];
            if (!ExchangeObjectsValid(exchange)) { CancelBrokenExchange(exchange); continue; }
            if (exchange.kind == ExchangeKind.Live)
            {
                squad.RemoveFromField(exchange.outgoing);
                if (!exchange.incoming.PermanentlyDisqualified &&
                    squad.AssignToField(exchange.incoming, exchange.slot, MatchPlayerStatus.OnField))
                {
                    exchange.incoming.StopMove();
                    exchange.incoming.PlaceAt(squad.FormationPoint(exchange.incoming));
                    exchange.outgoing.StopMove();
                    exchange.outgoing.SetStatus(exchange.outgoing.PermanentlyDisqualified
                        ? MatchPlayerStatus.PermanentlyOut : MatchPlayerStatus.Bench, false);
                    exchange.outgoing.PlaceAt(squad.Geometry.BenchPoint(exchange.team,
                                                                        exchange.outgoing.CapNumber));
                }
                else CancelBrokenExchange(exchange);
            }
            else
            {
                if (exchange.phase != ExchangePhase.Dispersing) CompleteExclusionTouch(exchange);
                exchange.outgoing.StopMove();
                exchange.outgoing.PlaceAt(squad.Geometry.BenchPoint(exchange.team,
                                                                    exchange.outgoing.CapNumber));
            }
        }
        active.Clear();
        for (int i = 0; i < returningToBench.Count; i++)
        {
            MatchPlayerState player = returningToBench[i];
            if (player == null) continue;
            player.StopMove();
            player.SetStatus(player.PermanentlyDisqualified
                ? MatchPlayerStatus.PermanentlyOut : MatchPlayerStatus.Bench, false);
            player.PlaceAt(squad.Geometry.BenchPoint(player.Team, player.CapNumber));
        }
        returningToBench.Clear();
        MatchSubstitutionSuggestionUI.Instance?.Close();
        TeamManager.EnsureValidActive();
    }

    public void OnQuarterEnded()
    {
        ResolveForMatchStoppage();
        if (context != null)
        {
            CancelPending(context.PlayerTeam);
            CancelPending(context.BotTeam);
        }
    }

    public void OnPlayerExcluded(MatchPlayerState player)
    {
        if (player == null) return;
        CancelPending(player.Team);
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Exchange exchange = active[i];
            if (exchange.outgoing != player && exchange.incoming != player) continue;
            CancelBrokenExchange(exchange);
            active.RemoveAt(i);
        }
    }

    // Third personal foul by penalty: dead-ball penalty setup will reposition the field anyway,
    // so the mandatory replacement can legally own the vacated slot immediately.
    public MatchPlayerState InstallImmediateMandatoryReplacement(MatchPlayerState outgoing)
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (outgoing == null || squad == null) return null;
        int slot = squad.MemberIndex(outgoing.Team, outgoing.transform);
        if (slot < 0) slot = outgoing.RoleSlot;
        MatchPlayerState incoming = squad.BestBenchReplacement(outgoing);
        squad.RemoveFromField(outgoing);
        outgoing.StopMove();
        outgoing.SetStatus(MatchPlayerStatus.PermanentlyOut, false);
        outgoing.PlaceAt(squad.Geometry.BenchPoint(outgoing.Team, outgoing.CapNumber));
        if (incoming == null || !squad.AssignToField(incoming, slot, MatchPlayerStatus.OnField))
        {
            TeamManager.EnsureValidActive();
            return null;
        }
        incoming.PlaceAt(squad.Geometry.ExclusionEntryInside(incoming.Team));
        TeamManager.EnsureValidActive();
        return incoming;
    }

    public void Shutdown()
    {
        foreach (KeyValuePair<TeamSide, PendingExchange> pair in pending)
        {
            PendingExchange item = pair.Value;
            if (item?.outgoing != null) item.outgoing.SetPending(false);
            if (item?.incoming != null) item.incoming.SetPending(false);
        }
        pending.Clear();
        active.Clear();
        returningToBench.Clear();
        MatchSubstitutionSuggestionUI.Instance?.Close();
        MatchSquadManager.Instance?.StopAllTransitions();
    }

    void UpdateCoachSuggestions()
    {
        MatchTimer timer = MatchTimer.Instance;
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (timer == null || squad == null || context == null || timer.MatchOver) return;

        if (trackedQuarter != timer.CurrentQuarter)
        {
            trackedQuarter = timer.CurrentQuarter;
            humanSuggestionsThisQuarter = 0;
            botSuggestionsThisQuarter = 0;
            suggestedPairsThisQuarter.Clear();
            nextSuggestionTime = Time.time + firstRoutineEvaluationSeconds;
            MatchSubstitutionSuggestionUI.Instance?.Close();
        }

        if (Time.time < nextEvaluationTime || Time.time < nextSuggestionTime ||
            timer.QuarterRealElapsed < firstRoutineEvaluationSeconds || !context.BallLive ||
            (ScoreManager.Instance != null && ScoreManager.Instance.GoalRestartInProgress)) return;
        nextEvaluationTime = Time.time + evaluationIntervalSeconds;

        if (humanSuggestionsThisQuarter < maxRoutineSuggestionsPerQuarter &&
            (MatchSubstitutionSuggestionUI.Instance == null ||
             !MatchSubstitutionSuggestionUI.Instance.IsShowing))
            EvaluateTeamSuggestion(context.PlayerTeam, true);

        if (botSuggestionsThisQuarter < maxRoutineSuggestionsPerQuarter)
            EvaluateTeamSuggestion(context.BotTeam, false);
    }

    void EvaluateTeamSuggestion(TeamSide team, bool showPopup)
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        MatchPlayerState tired = null;
        List<MatchPlayerState> players = squad.PlayersFor(team);
        for (int i = 0; i < players.Count; i++)
        {
            MatchPlayerState candidate = players[i];
            if (candidate == null || !candidate.Selectable ||
                candidate.StaminaPercent > routineStaminaThreshold) continue;
            if (context.Ball != null && context.Ball.transform.IsChildOf(candidate.transform)) continue;
            if (tired == null || candidate.StaminaPercent < tired.StaminaPercent ||
                (Mathf.Approximately(candidate.StaminaPercent, tired.StaminaPercent) &&
                 candidate.CapNumber < tired.CapNumber)) tired = candidate;
        }
        if (tired == null) return;

        float gain = tired.StaminaPercent <= urgentStaminaThreshold
            ? urgentFreshnessGain : minimumFreshnessGain;
        MatchPlayerState replacement = squad.BestBenchReplacement(tired, gain);
        if (replacement == null) return;
        string key = (team == context.PlayerTeam ? "H:" : "A:") +
                     tired.PlayerId + ":" + replacement.PlayerId;
        if (suggestedPairsThisQuarter.Contains(key)) return;
        suggestedPairsThisQuarter.Add(key);

        if (showPopup)
        {
            humanSuggestionsThisQuarter++;
            MatchSubstitutionSuggestionUI.Instance.Show(tired, replacement,
                suggestionLifetimeSeconds,
                () =>
                {
                    RequestLive(tired, replacement, out _);
                    nextSuggestionTime = Time.time + suggestionCooldownSeconds;
                },
                () => nextSuggestionTime = Time.time + suggestionCooldownSeconds);
        }
        else
        {
            botSuggestionsThisQuarter++;
            if (RequestLive(tired, replacement, out _))
                nextSuggestionTime = Time.time + suggestionCooldownSeconds;
        }
    }
}

// Non-blocking FIFA-style suggestion card.  It has no full-screen raycast layer, so live touch
// controls and play continue beneath it until ACCEPT, IGNORE, or the short expiry.
public sealed class MatchSubstitutionSuggestionUI : MonoBehaviour
{
    public static MatchSubstitutionSuggestionUI Instance { get; private set; }
    public bool IsShowing => root != null && root.activeSelf;

    private GameObject root;
    private TMP_Text outgoingText;
    private TMP_Text incomingText;
    private TMP_Text titleText;
    private Button acceptButton;
    private Button ignoreButton;
    private Action accept;
    private Action ignore;
    private float remaining;

    public static MatchSubstitutionSuggestionUI Ensure(GameObject owner)
    {
        if (Instance != null) return Instance;
        MatchSubstitutionSuggestionUI ui = owner.GetComponent<MatchSubstitutionSuggestionUI>();
        if (ui == null) ui = owner.AddComponent<MatchSubstitutionSuggestionUI>();
        return ui;
    }

    void Awake()
    {
        Instance = this;
        Build();
        root.SetActive(false);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (!IsShowing || Time.timeScale <= 0f) return;
        remaining -= Time.deltaTime;
        if (remaining <= 0f) Ignore();
    }

    public void Show(MatchPlayerState outgoing, MatchPlayerState incoming, float lifetime,
                     Action onAccept, Action onIgnore)
    {
        if (outgoing == null || incoming == null) return;
        accept = onAccept;
        ignore = onIgnore;
        remaining = Mathf.Max(2f, lifetime);
        titleText.text = "SUBSTITUTION";
        outgoingText.text = "<color=#FF5964>▼ OUT</color>   #" + outgoing.CapNumber + "  " +
                            outgoing.DisplayName + "\n" + outgoing.Position + "   STAMINA " +
                            Mathf.RoundToInt(outgoing.StaminaPercent * 100f) + "%";
        incomingText.text = "<color=#4EE69A>▲ IN</color>      #" + incoming.CapNumber + "  " +
                            incoming.DisplayName + "\n" + incoming.Position + "   STAMINA " +
                            Mathf.RoundToInt(incoming.StaminaPercent * 100f) + "%";
        root.SetActive(true);
    }

    public void Close()
    {
        if (root != null) root.SetActive(false);
        accept = null;
        ignore = null;
    }

    void Accept()
    {
        Action callback = accept;
        Close();
        callback?.Invoke();
    }

    void Ignore()
    {
        Action callback = ignore;
        Close();
        callback?.Invoke();
    }

    void Build()
    {
        EnsureEventSystem();
        root = new GameObject("SubstitutionSuggestionCanvas");
        root.transform.SetParent(transform, false);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 106;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        GameObject card = new GameObject("CoachCard");
        card.transform.SetParent(root.transform, false);
        RectTransform cardRect = card.AddComponent<RectTransform>();
        cardRect.anchorMin = cardRect.anchorMax = new Vector2(1f, 1f);
        cardRect.pivot = new Vector2(1f, 1f);
        cardRect.anchoredPosition = new Vector2(-24f, -105f);
        cardRect.sizeDelta = new Vector2(450f, 285f);
        Image cardImage = card.AddComponent<Image>();
        cardImage.color = new Color(0.025f, 0.09f, 0.20f, 0.97f);
        Outline outline = card.AddComponent<Outline>();
        outline.effectColor = new Color(0.93f, 0.70f, 0.20f, 0.95f);
        outline.effectDistance = new Vector2(3f, -3f);

        titleText = MakeText(card.transform, "Title", 30f, FontStyles.Bold,
                             new Vector2(0f, 112f), new Vector2(410f, 42f));
        titleText.color = new Color(1f, 0.82f, 0.28f);
        outgoingText = MakeText(card.transform, "Outgoing", 22f, FontStyles.Bold,
                                new Vector2(0f, 52f), new Vector2(400f, 70f));
        incomingText = MakeText(card.transform, "Incoming", 22f, FontStyles.Bold,
                                new Vector2(0f, -22f), new Vector2(400f, 70f));
        acceptButton = MakeButton(card.transform, "ACCEPT", new Vector2(-103f, -108f), Accept,
                                  new Color(0.04f, 0.42f, 0.28f, 0.98f));
        ignoreButton = MakeButton(card.transform, "IGNORE", new Vector2(103f, -108f), Ignore,
                                  new Color(0.15f, 0.20f, 0.32f, 0.98f));
    }

    static TMP_Text MakeText(Transform parent, string name, float size, FontStyles style,
                             Vector2 position, Vector2 dimensions)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        return text;
    }

    static Button MakeButton(Transform parent, string label, Vector2 position,
                             UnityEngine.Events.UnityAction click, Color color)
    {
        GameObject go = new GameObject("Btn" + label);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(180f, 48f);
        Image image = go.AddComponent<Image>();
        CrestUITheme.ApplyButton(image, color);
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(click);
        LocalizedButtonStyler.AddLabel(go.transform, label, 20f, rect.sizeDelta,
                                       LocalizedButtonStyler.TextZone.NativeCenter);
        return button;
    }

    static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
        GameObject events = new GameObject("EventSystem");
        events.AddComponent<EventSystem>();
        events.AddComponent<StandaloneInputModule>();
    }
}
