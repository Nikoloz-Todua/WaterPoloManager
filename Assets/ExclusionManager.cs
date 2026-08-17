using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

// Match discipline and temporary exclusions.  Personal fouls belong to MatchPlayerState (the
// athlete identity), while TeamSide.members remains the one authoritative legal-player list used
// by every existing pass/mark/formation system.
public class ExclusionManager : MonoBehaviour
{
    public static ExclusionManager Instance { get; private set; }

    [Header("Foul escalation")]
    [SerializeField] private float foulWindowSeconds = 10f;
    [SerializeField] private int foulsForExclusion = 2;
    [FormerlySerializedAs("maxExclusionsPerPlayer")]
    [SerializeField] private int personalFoulsToRemove = 3;
    [SerializeField] private int minPlayersToContinue = 4;
    [SerializeField] private float foulStealLockout = 1.5f;
    [SerializeField] private float penaltyZoneX = 4.28f;
    [SerializeField] private bool centerFoulBoost = false;

    [Header("Ordinary exclusion (displayed/game seconds)")]
    [SerializeField] private float ordinaryExclusionDisplayedSeconds = 18f;
    [SerializeField] private float exclusionExitSpeed = 4.2f;
    [SerializeField] private float exclusionEntrySpeed = 4.2f;
    [SerializeField] private float exclusionArrivalRadius = 0.16f;

    [Header("Ordinary-foul presentation")]
    [SerializeField] private float foulProtectSeconds = 5f;
    [SerializeField] private float foulIdleProtectSeconds = 2.5f;
    [SerializeField] private float foulWhistleFreezeSeconds = 0.7f;

    [Header("Successful-steal stun")]
    [SerializeField, Range(0.1f, 2f)] private float successfulStealStunSeconds = 1.4f;
    private const float DefaultSuccessfulStealStunSeconds = 1.4f;

    [Header("References")]
    [SerializeField] private MatchTimer matchTimer;
    [SerializeField] private TMP_Text exclusionText;
    [SerializeField] private int excludedSortingOrder = 75;

    private enum ReentryPhase { Exiting, Waiting, MovingToGate, MovingToFormation }

    private sealed class TemporaryExclusion
    {
        public MatchPlayerState servingPlayer;
        public MatchPlayerState entrant;
        public TeamSide team;
        public int slot;
        public CompressedTimer timer;
        public ReentryPhase phase;
        public bool releaseAuthorized;
        public bool replacementExchangePending;
        public bool replacementRevisionInProgress;
    }

    private readonly List<TemporaryExclusion> activeExclusions =
        new List<TemporaryExclusion>();
    private readonly Dictionary<MatchPlayerState, List<float>> foulTimes =
        new Dictionary<MatchPlayerState, List<float>>();
    private readonly Dictionary<SpriteRenderer, int> regularSortingOrders =
        new Dictionary<SpriteRenderer, int>();

    private MatchContext context;
    private TeamSide playerTeam;
    private TeamSide botTeam;

    void Awake() { Instance = this; }

    void Start()
    {
        context = MatchContext.Instance;
        if (context != null)
        {
            playerTeam = context.PlayerTeam;
            botTeam = context.BotTeam;
        }
        if (matchTimer == null) matchTimer = MatchTimer.Instance;
        if (exclusionText == null) exclusionText = BuildFallbackExclusionHud();
        if (exclusionText != null) exclusionText.enabled = false;
        PersonalFoulOutUI.Ensure(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        bool actualPlay = context != null && !context.ClocksStopped && Time.timeScale > 0f &&
                          (matchTimer == null || !matchTimer.MatchOver);
        for (int i = activeExclusions.Count - 1; i >= 0; i--)
        {
            TemporaryExclusion exclusion = activeExclusions[i];
            if (exclusion == null || exclusion.servingPlayer == null)
            { activeExclusions.RemoveAt(i); continue; }

            if (actualPlay && !exclusion.releaseAuthorized)
            {
                exclusion.timer.Tick(Time.deltaTime);
                if (exclusion.timer.IsComplete) AuthorizeRelease(exclusion, "18 seconds");
            }

            UpdateExclusionMovement(exclusion);
            if (exclusion.phase == ReentryPhase.MovingToFormation && exclusion.entrant != null &&
                (exclusion.entrant.AtMoveTarget ||
                 exclusion.entrant.MovePurpose == MatchMovePurpose.None))
            {
                exclusion.entrant.StopMove(MatchMovePurpose.Exclusion);
                exclusion.entrant.SetStatus(MatchPlayerStatus.OnField, true);
                SetExcludedSorting(exclusion.entrant.transform, false);
                activeExclusions.RemoveAt(i);
                TeamManager.EnsureValidActive();
            }
        }
        UpdateHud();
    }

    void UpdateExclusionMovement(TemporaryExclusion exclusion)
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (squad == null) return;

        if (exclusion.phase == ReentryPhase.Exiting)
        {
            Vector2 area = squad.Geometry.ExclusionArea(exclusion.team);
            exclusion.servingPlayer.Retarget(MatchMovePurpose.Exclusion, area);
            if (!exclusion.servingPlayer.AtMoveTarget) return;
            exclusion.servingPlayer.StopMove(MatchMovePurpose.Exclusion);
            exclusion.servingPlayer.SetStatus(MatchPlayerStatus.ExclusionWaiting, false);
            SetExcludedSorting(exclusion.servingPlayer.transform, true);
            exclusion.phase = ReentryPhase.Waiting;
        }

        if (exclusion.phase == ReentryPhase.Waiting && exclusion.releaseAuthorized &&
            !exclusion.replacementExchangePending)
            BeginLegalReentry(exclusion);

        if (exclusion.phase == ReentryPhase.MovingToGate && exclusion.entrant != null &&
            exclusion.entrant.AtMoveTarget)
        {
            MatchPlayerState entrant = exclusion.entrant;
            entrant.StopMove(MatchMovePurpose.Exclusion);
            if (!squad.AssignToField(entrant, exclusion.slot, MatchPlayerStatus.SubstitutingIn))
                return; // deterministic safety: keep waiting instead of creating a duplicate slot

            if (exclusion.servingPlayer != entrant)
            {
                if (exclusion.servingPlayer.PermanentlyDisqualified)
                    exclusion.servingPlayer.SetStatus(MatchPlayerStatus.PermanentlyOut, false);
                else
                    exclusion.servingPlayer.SetStatus(MatchPlayerStatus.Bench, false);
                SetExcludedSorting(exclusion.servingPlayer.transform, false);
            }
            entrant.BeginMove(MatchMovePurpose.Exclusion, squad.FormationPoint(entrant),
                              exclusionEntrySpeed, 0.24f, true, true);
            exclusion.phase = ReentryPhase.MovingToFormation;
        }
    }

    void BeginLegalReentry(TemporaryExclusion exclusion)
    {
        MatchPlayerState entrant = exclusion.entrant != null
            ? exclusion.entrant : exclusion.servingPlayer;
        if (entrant == null || entrant.PermanentlyDisqualified) return;
        exclusion.entrant = entrant;
        entrant.SetStatus(MatchPlayerStatus.SubstitutingIn, false);
        entrant.BeginMove(MatchMovePurpose.Exclusion,
            MatchSquadManager.Instance.Geometry.ExclusionEntryInside(exclusion.team),
            exclusionEntrySpeed, exclusionArrivalRadius, true, true);
        exclusion.phase = ReentryPhase.MovingToGate;
    }

    // ---------- public eligibility / release API ----------

    public bool IsExcluded(Transform body)
    {
        MatchPlayerState player = MatchPlayerState.For(body);
        if (player == null) return false;
        switch (player.Status)
        {
            case MatchPlayerStatus.ExclusionExit:
            case MatchPlayerStatus.ExclusionWaiting:
            case MatchPlayerStatus.ExclusionReplacementApproach:
            case MatchPlayerStatus.ExclusionReplacementWaiting:
            case MatchPlayerStatus.ExcludedReplacedBench:
            case MatchPlayerStatus.PermanentlyOut:
                return true;
            default:
                return player.PermanentlyDisqualified;
        }
    }

    public int ExcludedCount(TeamSide team)
    {
        if (team == null || team.members == null) return 0;
        int missing = 0;
        for (int i = 0; i < team.members.Length; i++)
            if (team.members[i] == null) missing++;
        return missing;
    }

    public void NotifyPossessionChanged(TeamSide previousPossession, TeamSide newPossession,
                                        TeamSide previousTouch)
    {
        if (newPossession == null) return;
        bool trueRegain = (previousPossession != null && previousPossession != newPossession) ||
                          (previousPossession == null && previousTouch != null &&
                           previousTouch != newPossession);
        if (trueRegain) ReleaseForAward(newPossession, "possession regained");
    }

    public void ReleaseForAward(TeamSide team, string reason)
    {
        if (team == null) return;
        for (int i = 0; i < activeExclusions.Count; i++)
            if (activeExclusions[i].team == team) AuthorizeRelease(activeExclusions[i], reason);
    }

    public void NotifyGoalAwarded()
    {
        for (int i = 0; i < activeExclusions.Count; i++)
            AuthorizeRelease(activeExclusions[i], "goal");
    }

    void AuthorizeRelease(TemporaryExclusion exclusion, string reason)
    {
        if (exclusion == null || exclusion.releaseAuthorized) return;
        exclusion.releaseAuthorized = true;
        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Exclusion released - " + reason);
    }

    // A goal includes a full dead-ball reset and is an explicit release condition. Resolve each
    // entrant into exactly one slot before that formation snaps, while permanent removals stay out.
    public void EndTemporaryExclusionsForRestart()
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (squad == null) { activeExclusions.Clear(); UpdateHud(); return; }

        for (int i = 0; i < activeExclusions.Count; i++)
        {
            TemporaryExclusion exclusion = activeExclusions[i];
            if (exclusion == null || exclusion.servingPlayer == null) continue;
            MatchPlayerState entrant = exclusion.entrant;
            if (entrant == null || entrant.PermanentlyDisqualified)
            {
                if (!exclusion.servingPlayer.PermanentlyDisqualified)
                    entrant = exclusion.servingPlayer;
                else
                    entrant = squad.BestBenchReplacement(exclusion.servingPlayer);
            }

            if (entrant != null && !entrant.PermanentlyDisqualified)
            {
                entrant.StopMove();
                squad.AssignToField(entrant, exclusion.slot, MatchPlayerStatus.OnField);
                entrant.PlaceAt(squad.FormationPoint(entrant));
                SetExcludedSorting(entrant.transform, false);
            }
            if (exclusion.servingPlayer != entrant)
            {
                exclusion.servingPlayer.StopMove();
                exclusion.servingPlayer.SetStatus(exclusion.servingPlayer.PermanentlyDisqualified
                    ? MatchPlayerStatus.PermanentlyOut : MatchPlayerStatus.Bench, false);
                exclusion.servingPlayer.PlaceAt(squad.Geometry.BenchPoint(exclusion.team,
                                                        exclusion.servingPlayer.CapNumber));
                SetExcludedSorting(exclusion.servingPlayer.transform, false);
            }
        }
        activeExclusions.Clear();
        UpdateHud();
        TeamManager.EnsureValidActive();
    }

    public void OnEndsSwapped()
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (squad == null) return;
        for (int i = 0; i < activeExclusions.Count; i++)
        {
            TemporaryExclusion exclusion = activeExclusions[i];
            if (exclusion == null || exclusion.servingPlayer == null) continue;
            Vector2 area = squad.Geometry.ExclusionArea(exclusion.team);
            if (exclusion.phase == ReentryPhase.Exiting)
                exclusion.servingPlayer.Retarget(MatchMovePurpose.Exclusion, area);
            else if (exclusion.phase == ReentryPhase.Waiting)
            {
                MatchPlayerState waiting = exclusion.entrant != null
                    ? exclusion.entrant : exclusion.servingPlayer;
                waiting.PlaceAt(area);
                SetExcludedSorting(waiting.transform, true);
                if (waiting != exclusion.servingPlayer)
                    exclusion.servingPlayer.PlaceAt(squad.Geometry.BenchPoint(
                        exclusion.team, exclusion.servingPlayer.CapNumber));
            }
            else if (exclusion.phase == ReentryPhase.MovingToGate && exclusion.entrant != null)
            {
                exclusion.entrant.Retarget(MatchMovePurpose.Exclusion,
                    squad.Geometry.ExclusionEntryInside(exclusion.team));
            }
            else if (exclusion.phase == ReentryPhase.MovingToFormation && exclusion.entrant != null)
            {
                exclusion.entrant.Retarget(MatchMovePurpose.Exclusion,
                    squad.FormationPoint(exclusion.entrant));
            }
        }
    }

    public void OnQuarterEnded()
    {
        // Crossing the legal re-entry gate already restored the slot. If the horn sounds during
        // only the cosmetic swim to formation, finish that state now so SprintDuel owns the next
        // lineup. Unserved exclusions and entrants still outside the gate remain pending.
        for (int i = activeExclusions.Count - 1; i >= 0; i--)
        {
            TemporaryExclusion exclusion = activeExclusions[i];
            if (exclusion == null || exclusion.phase != ReentryPhase.MovingToFormation ||
                exclusion.entrant == null) continue;
            exclusion.entrant.StopMove(MatchMovePurpose.Exclusion);
            exclusion.entrant.SetStatus(MatchPlayerStatus.OnField, true);
            SetExcludedSorting(exclusion.entrant.transform, false);
            activeExclusions.RemoveAt(i);
        }
        UpdateHud();
    }

    public bool RequestReplacement(MatchPlayerState excluded, MatchPlayerState replacement,
                                   out string validationError)
    {
        validationError = string.Empty;
        TemporaryExclusion exclusion = FindExclusion(excluded);
        if (exclusion == null)
        { validationError = "Player is not serving an exclusion"; return false; }
        if (exclusion.entrant != null)
        { validationError = "Replacement has already completed the exchange"; return false; }
        if (replacement == null || !replacement.AvailableOnBench ||
            replacement.Team != exclusion.team || replacement.PermanentlyDisqualified)
        { validationError = "Replacement is not available"; return false; }
        if (MatchSquadManager.Instance == null ||
            !MatchSquadManager.Instance.IsCompatible(excluded, replacement))
        { validationError = "Incompatible position"; return false; }
        if (SubstitutionManager.Instance == null)
        { validationError = "Substitution service unavailable"; return false; }
        if (exclusion.replacementExchangePending)
        {
            exclusion.replacementRevisionInProgress = true;
            if (!SubstitutionManager.Instance.CancelUncompletedExclusionExchange(excluded))
            {
                exclusion.replacementRevisionInProgress = false;
                validationError = "Replacement exchange is already in progress";
                return false;
            }
            // The cancellation callback clears replacementExchangePending synchronously.
        }

        exclusion.replacementExchangePending = true;
        Vector2 anchor = MatchSquadManager.Instance.Geometry.ExclusionArea(exclusion.team);
        bool started = SubstitutionManager.Instance.BeginExclusionExchange(
            excluded, replacement, anchor,
            entrant => OnReplacementTouch(exclusion, entrant), out validationError);
        exclusion.replacementRevisionInProgress = false;
        if (!started)
        {
            exclusion.replacementExchangePending = false;
            if (exclusion.releaseAuthorized && exclusion.phase == ReentryPhase.Waiting)
                BeginLegalReentry(exclusion);
        }
        return started;
    }

    void OnReplacementTouch(TemporaryExclusion exclusion, MatchPlayerState entrant)
    {
        if (exclusion == null || !activeExclusions.Contains(exclusion)) return;
        if (entrant != null)
        {
            exclusion.entrant = entrant;
            SetExcludedSorting(entrant.transform, true);
        }
        exclusion.replacementExchangePending = false;
        if (!exclusion.replacementRevisionInProgress && exclusion.releaseAuthorized &&
            exclusion.phase == ReentryPhase.Waiting)
            BeginLegalReentry(exclusion);
    }

    TemporaryExclusion FindExclusion(MatchPlayerState serving)
    {
        if (serving == null) return null;
        for (int i = 0; i < activeExclusions.Count; i++)
            if (activeExclusions[i].servingPlayer == serving) return activeExclusions[i];
        return null;
    }

    // ---------- foul entry points ----------

    public void ReportFoul(Transform offenderBody, TeamSide offenderTeam, Transform victim)
    {
        MatchPlayerState offender = MatchPlayerState.For(offenderBody);
        if (offender == null || offender.PermanentlyDisqualified) return;
        ApplyStealLockout(offenderBody);
        RefereeController.Instance?.TriggerFoul();

        if (!foulTimes.TryGetValue(offender, out List<float> times))
        {
            times = new List<float>();
            foulTimes[offender] = times;
        }
        times.Add(Time.time);
        for (int i = times.Count - 1; i >= 0; i--)
            if (Time.time - times[i] > foulWindowSeconds) times.RemoveAt(i);

        if (centerFoulBoost && victim != null && context != null)
        {
            TeamSide victimTeam = context.EnemyOf(offenderTeam);
            if (victimTeam != null && victimTeam.Contains(victim) &&
                victimTeam.RoleOf(victim) == TeamSide.Role.Center &&
                TeamSide.IsInsideTwoMeter(victim, victimTeam))
                times.Add(Time.time - 0.1f);
        }

        if (times.Count >= foulsForExclusion) Escalate(offender, offenderTeam, victim);
        else FreeThrow(offenderTeam, victim);
    }

    public void ReportExclusionFoul(Transform offenderBody, TeamSide offenderTeam,
                                    Transform victim)
    {
        MatchPlayerState offender = MatchPlayerState.For(offenderBody);
        if (offender == null || offender.PermanentlyDisqualified) return;
        ApplyStealLockout(offenderBody);
        RefereeController.Instance?.TriggerFoul();
        Escalate(offender, offenderTeam, victim);
    }

    void Escalate(MatchPlayerState offender, TeamSide offenderTeam, Transform victim)
    {
        if (offender == null || offenderTeam == null || FindExclusion(offender) != null) return;
        foulTimes.Remove(offender);
        bool penalty = false;
        if (victim != null && offenderTeam.defendGoal != null)
        {
            float sign = Mathf.Sign(offenderTeam.defendGoal.position.x);
            if (sign == 0f) sign = 1f;
            penalty = victim.position.x * sign >= penaltyZoneX;
        }

        int personalFouls = offender.AddPersonalFoul();
        bool thirdFoul = personalFouls >= personalFoulsToRemove;
        if (thirdFoul)
        {
            offender.MarkPermanentlyDisqualified();
            PersonalFoulOutUI.Instance?.Show(offender);
            if (EventFeed.Instance != null)
                EventFeed.Instance.AddEvent("3 PERSONAL FOULS - " + offender.DisplayName + " OUT");
        }

        if (penalty) AwardPenalty(offender, offenderTeam, victim, thirdFoul);
        else Exclude(offender, offenderTeam, thirdFoul);
    }

    void AwardPenalty(MatchPlayerState offender, TeamSide offenderTeam, Transform victim,
                      bool thirdFoul)
    {
        TeamSide attackingTeam = context != null ? context.EnemyOf(offenderTeam) : null;
        ReleaseForAward(attackingTeam, "penalty throw awarded");

        if (thirdFoul)
        {
            SubstitutionManager.Instance?.OnPlayerExcluded(offender);
            MatchPlayerState replacement =
                SubstitutionManager.Instance?.InstallImmediateMandatoryReplacement(offender);
            if (replacement == null) CheckForfeit(offenderTeam);
        }

        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("PENALTY - " +
                (attackingTeam == playerTeam ? "YOU" : "BOT") +
                " | PF " + offender.PersonalFouls);
        if (PenaltyManager.Instance != null && attackingTeam != null && victim != null)
            PenaltyManager.Instance.StartPenalty(victim, attackingTeam);
    }

    void Exclude(MatchPlayerState offender, TeamSide offenderTeam, bool thirdFoul)
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (offender == null || offenderTeam == null || squad == null) return;
        SubstitutionManager.Instance?.OnPlayerExcluded(offender);
        int slot = squad.MemberIndex(offenderTeam, offender.transform);
        if (slot < 0) slot = offender.RoleSlot;
        DropBallHeldBy(offender.transform);
        squad.RemoveFromField(offender);
        offender.SetStatus(MatchPlayerStatus.ExclusionExit, false);
        SetExcludedSorting(offender.transform, true);
        offender.BeginMove(MatchMovePurpose.Exclusion,
            squad.Geometry.ExclusionArea(offenderTeam), exclusionExitSpeed,
            exclusionArrivalRadius, true, true);

        float realSeconds = matchTimer != null
            ? matchTimer.RealSecondsForDisplayedSeconds(ordinaryExclusionDisplayedSeconds)
            : ordinaryExclusionDisplayedSeconds * (90f / 480f);
        TemporaryExclusion exclusion = new TemporaryExclusion
        {
            servingPlayer = offender,
            team = offenderTeam,
            slot = slot,
            timer = new CompressedTimer(ordinaryExclusionDisplayedSeconds, realSeconds),
            phase = ReentryPhase.Exiting
        };
        activeExclusions.Add(exclusion);

        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Exclusion - " + offender.DisplayName +
                                        " | PF " + offender.PersonalFouls);
        if (context != null && context.PossessingTeam != null &&
            context.PossessingTeam != offenderTeam && ShotClock.Instance != null)
            ShotClock.Instance.ResetClock();

        // Third-foul replacement is mandatory and must wait for this exclusion's normal release.
        // The bot also chooses a deterministic substitute immediately; the human may choose one
        // through Team Management before the automatic fallback is needed.
        if (thirdFoul || offenderTeam == botTeam)
        {
            MatchPlayerState replacement = squad.BestBenchReplacement(offender);
            if (replacement != null)
                RequestReplacement(offender, replacement, out _);
            else if (thirdFoul)
                CheckForfeit(offenderTeam);
        }
        TeamManager.EnsureValidActive();
    }

    void FreeThrow(TeamSide offenderTeam, Transform victim)
    {
        if (context != null && victim != null)
        {
            context.StartFreeThrow(victim);
            context.StartFoulProtection(victim, foulProtectSeconds, foulIdleProtectSeconds);
            SpawnFoulPopup(victim.position);
            if (!context.PlayFrozen && foulWhistleFreezeSeconds > 0f)
                StartCoroutine(FoulWhistleRoutine(context));
        }
        TeamSide victimTeam = context != null ? context.EnemyOf(offenderTeam) : null;
        ReleaseForAward(victimTeam, "free throw awarded");
        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Foul - free throw " +
                                        (victimTeam == playerTeam ? "YOU" : "BOT"));
    }

    IEnumerator FoulWhistleRoutine(MatchContext ctx)
    {
        ctx.FreezeAll();
        yield return new WaitForSeconds(foulWhistleFreezeSeconds);
        if (ctx != null) ctx.Unfreeze();
    }

    public static void StunSuccessfulStealVictim(Transform victim)
    {
        float seconds = Instance != null
            ? Instance.successfulStealStunSeconds : DefaultSuccessfulStealStunSeconds;
        FoulStun.Apply(victim, seconds);
    }

    // ---------- helpers / presentation ----------

    void ApplyStealLockout(Transform offender)
    {
        IAgentBody body = offender != null ? offender.GetComponent<IAgentBody>() : null;
        if (body != null) body.NextStealTime = Time.time + foulStealLockout;
        PlayerMovement player = offender != null ? offender.GetComponent<PlayerMovement>() : null;
        if (player != null) player.ApplyStealLockout(foulStealLockout);
    }

    void DropBallHeldBy(Transform agent)
    {
        if (context == null || context.Ball == null || agent == null ||
            !context.Ball.transform.IsChildOf(agent)) return;
        context.ForceDropHeldBall();
    }

    void CheckForfeit(TeamSide losingTeam)
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (squad == null || squad.AvailableNonDisqualifiedCount(losingTeam) >= minPlayersToContinue)
            return;
        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Forfeit - " + (losingTeam != null ? losingTeam.teamName : "?"));
        if (matchTimer != null) matchTimer.ForfeitMatch(losingTeam != playerTeam);
    }

    void SetExcludedSorting(Transform agent, bool excluded)
    {
        if (agent == null) return;
        SpriteRenderer[] renderers = agent.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null) continue;
            if (excluded)
            {
                if (!regularSortingOrders.ContainsKey(renderer))
                    regularSortingOrders[renderer] = renderer.sortingOrder;
                renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, excludedSortingOrder);
            }
            else if (regularSortingOrders.TryGetValue(renderer, out int original))
            {
                renderer.sortingOrder = original;
                regularSortingOrders.Remove(renderer);
            }
        }
    }

    void UpdateHud()
    {
        if (exclusionText == null) return;
        float you = -1f;
        float bot = -1f;
        for (int i = 0; i < activeExclusions.Count; i++)
        {
            TemporaryExclusion exclusion = activeExclusions[i];
            float remaining = exclusion.releaseAuthorized ? 0f : exclusion.timer.DisplayValue;
            if (exclusion.team == playerTeam) you = Mathf.Max(you, remaining);
            else if (exclusion.team == botTeam) bot = Mathf.Max(bot, remaining);
        }
        if (you < 0f && bot < 0f) { exclusionText.enabled = false; return; }
        string value = string.Empty;
        if (you >= 0f) value = "YOU EXC  0:" + Mathf.CeilToInt(you).ToString("00");
        if (bot >= 0f)
        {
            if (value.Length > 0) value += "     ";
            value += "BOT EXC  0:" + Mathf.CeilToInt(bot).ToString("00");
        }
        exclusionText.enabled = true;
        exclusionText.text = value;
    }

    TMP_Text BuildFallbackExclusionHud()
    {
        GameObject canvasObject = new GameObject("ExclusionHudCanvas");
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 94;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        GameObject labelObject = new GameObject("ExclusionCountdown");
        labelObject.transform.SetParent(canvasObject.transform, false);
        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = 22f;
        label.fontStyle = FontStyles.Bold;
        label.color = new Color(1f, 0.80f, 0.23f);
        label.alignment = TextAlignmentOptions.Left;
        label.raycastTarget = false;
        RectTransform rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -145f);
        rect.sizeDelta = new Vector2(500f, 42f);
        return label;
    }

    void SpawnFoulPopup(Vector3 position)
    {
        GameObject popup = new GameObject("FoulPopup");
        popup.transform.position = position + Vector3.up * 0.9f;
        TextMesh text = popup.AddComponent<TextMesh>();
        text.text = "FOUL!";
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 64;
        text.fontStyle = FontStyle.Bold;
        text.characterSize = 0.035f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = new Color(1f, 0.85f, 0.15f);
        MeshRenderer renderer = popup.GetComponent<MeshRenderer>();
        if (text.font != null) renderer.material = text.font.material;
        renderer.sortingOrder = 90;
        StartCoroutine(FoulPopupRoutine(popup.transform, text));
    }

    IEnumerator FoulPopupRoutine(Transform popup, TextMesh text)
    {
        const float seconds = 1.1f;
        Color color = text.color;
        float elapsed = 0f;
        while (elapsed < seconds && popup != null)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / seconds);
            popup.position += Vector3.up * (0.35f * Time.deltaTime);
            text.color = new Color(color.r, color.g, color.b, 1f - progress * progress);
            yield return null;
        }
        if (popup != null) Destroy(popup.gameObject);
    }
}

// Clear mandatory notification.  It is informational only: a third foul is never offered as an
// ACCEPT/IGNORE choice.
sealed class PersonalFoulOutUI : MonoBehaviour
{
    public static PersonalFoulOutUI Instance { get; private set; }
    private GameObject root;
    private TMP_Text detail;
    private float remaining;

    public static PersonalFoulOutUI Ensure(GameObject owner)
    {
        if (Instance != null) return Instance;
        PersonalFoulOutUI ui = owner.GetComponent<PersonalFoulOutUI>();
        if (ui == null) ui = owner.AddComponent<PersonalFoulOutUI>();
        return ui;
    }

    void Awake()
    {
        Instance = this;
        Build();
        root.SetActive(false);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public void Show(MatchPlayerState player)
    {
        if (player == null) return;
        detail.text = "3 PERSONAL FOULS\n<color=#FF5A66>PLAYER OUT</color>\n<size=65%>#" +
                      player.CapNumber + "  " + player.DisplayName + "</size>";
        remaining = 2.6f;
        root.SetActive(true);
    }

    void Update()
    {
        if (!root.activeSelf || Time.timeScale <= 0f) return;
        remaining -= Time.deltaTime;
        if (remaining <= 0f) root.SetActive(false);
    }

    void Build()
    {
        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }
        root = new GameObject("PersonalFoulOutCanvas");
        root.transform.SetParent(transform, false);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 116;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

        GameObject panel = new GameObject("MandatoryRemovalBanner");
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(560f, 205f);
        Image image = panel.AddComponent<Image>();
        image.color = new Color(0.025f, 0.075f, 0.17f, 0.97f);
        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.77f, 0.20f, 1f);
        outline.effectDistance = new Vector2(3f, -3f);

        GameObject textObject = new GameObject("Message");
        textObject.transform.SetParent(panel.transform, false);
        detail = textObject.AddComponent<TextMeshProUGUI>();
        detail.fontSize = 34f;
        detail.fontStyle = FontStyles.Bold;
        detail.alignment = TextAlignmentOptions.Center;
        detail.color = Color.white;
        detail.raycastTarget = false;
        RectTransform textRect = detail.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(15f, 12f);
        textRect.offsetMax = new Vector2(-15f, -12f);
    }
}

// Short visual/action lock applied whenever a carrier actually loses the ball to a close-range
// steal.  This remains independent of disciplinary state.
sealed class FoulStun : MonoBehaviour
{
    private float stunnedUntil;
    private float startedAt;
    private Transform stars;
    private static Material starMaterial;

    public static bool IsStunned(Transform target)
    {
        if (target == null) return false;
        FoulStun stun = target.GetComponent<FoulStun>();
        return stun != null && stun.enabled && Time.time < stun.stunnedUntil;
    }

    public static void Apply(Transform target, float seconds)
    {
        if (target == null) return;
        FoulStun stun = target.GetComponent<FoulStun>();
        if (stun == null) stun = target.gameObject.AddComponent<FoulStun>();
        stun.Begin(seconds);
    }

    void Begin(float seconds)
    {
        startedAt = Time.time;
        stunnedUntil = Mathf.Max(stunnedUntil, Time.time + Mathf.Max(0f, seconds));
        if (stars == null) BuildStars();
        if (stars != null) stars.gameObject.SetActive(true);
        enabled = true;
    }

    void Update()
    {
        if (Time.time >= stunnedUntil)
        {
            if (stars != null) stars.gameObject.SetActive(false);
            enabled = false;
            return;
        }
        if (stars == null) return;
        float elapsed = Time.time - startedAt;
        stars.localPosition = new Vector3(Mathf.Sin(elapsed * 9f) * 0.07f,
                                          0.78f + Mathf.Sin(elapsed * 6f) * 0.04f, 0f);
        stars.localRotation = Quaternion.Euler(0f, 0f, elapsed * 220f);
        float pulse = 1f + Mathf.Sin(elapsed * 12f) * 0.12f;
        stars.localScale = new Vector3(pulse, pulse, 1f);
    }

    void OnDisable() { if (stars != null) stars.gameObject.SetActive(false); }

    void BuildStars()
    {
        GameObject root = new GameObject("FoulStunStars");
        root.hideFlags = HideFlags.DontSave;
        root.transform.SetParent(transform, false);
        stars = root.transform;
        if (starMaterial == null)
        {
            starMaterial = new Material(Shader.Find("Sprites/Default"));
            starMaterial.hideFlags = HideFlags.DontSave;
        }
        for (int i = 0; i < 3; i++)
        {
            float angle = i * Mathf.PI * 2f / 3f;
            GameObject star = new GameObject("Star" + (i + 1));
            star.hideFlags = HideFlags.DontSave;
            star.transform.SetParent(stars, false);
            star.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.34f,
                                                       Mathf.Sin(angle) * 0.12f, 0f);
            LineRenderer line = star.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 10;
            line.startWidth = line.endWidth = 0.025f;
            line.material = starMaterial;
            line.sortingOrder = 95;
            line.startColor = new Color(1f, 0.9f, 0.15f, 1f);
            line.endColor = new Color(1f, 0.55f, 0.05f, 1f);
            for (int point = 0; point < 10; point++)
            {
                float pointAngle = Mathf.PI * 0.5f + point * Mathf.PI / 5f;
                float radius = (point & 1) == 0 ? 0.105f : 0.045f;
                line.SetPosition(point, new Vector3(Mathf.Cos(pointAngle) * radius,
                                                    Mathf.Sin(pointAngle) * radius, 0f));
            }
        }
    }
}
