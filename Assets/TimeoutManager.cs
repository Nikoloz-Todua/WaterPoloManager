using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Two official one-game-minute timeouts per team.  This owns a water-polo stoppage, not a full
// MatchContext freeze: clocks/competitive actions stop while transition-owned swimmers continue.
[DefaultExecutionOrder(-340)]
public sealed class TimeoutManager : MonoBehaviour
{
    public static TimeoutManager Instance { get; private set; }

    [SerializeField] private int timeoutsPerTeam = 2;
    [SerializeField] private float timeoutDisplayedSeconds = 60f;
    [SerializeField] private float defensiveHalfDisplayedSeconds = 45f;
    [SerializeField] private float positioningSpeed = 3.7f;
    [SerializeField] private float positioningArrivalRadius = 0.24f;
    [SerializeField] private float halfwayRestartInset = 0.35f;

    [Header("Bot coach")]
    [SerializeField] private float botEvaluationIntervalSeconds = 1f;
    [SerializeField] private float botCoachSpacingSeconds = 18f;
    [SerializeField] private int botCallScoreThreshold = 65;
    [SerializeField] private float severeFatigueThreshold = 0.22f;
    [SerializeField] private float tiredFatigueThreshold = 0.36f;

    private readonly Dictionary<TeamSide, int> used = new Dictionary<TeamSide, int>();
    private enum RestartMode { LivePossession, FreeThrow, OutOfBounds, Penalty, GoalRestart }

    private MatchContext context;
    private TeamSide callingTeam;
    private RestartMode restartMode;
    private Transform restartCarrier;
    private CompressedTimer timeoutClock;
    private bool active;
    private bool restartPreparation;
    private float nextPositionRefresh;
    private float timeoutStartedAt;
    private readonly List<Goalkeeper> timeoutKeepers = new List<Goalkeeper>();
    private float nextBotEvaluationTime;
    private float lastBotTimeoutTime = -1000f;
    private int observedHomeScore;
    private int observedAwayScore;
    private int humanUnansweredGoals;

    private GameObject canvasRoot;
    private Button timeoutButton;
    private Image timeoutButtonImage;
    private TMP_Text timeoutButtonText;
    private GameObject activePanel;
    private TMP_Text countdownText;
    private TMP_Text phaseText;

    public bool Active => active;
    public TeamSide CallingTeam => callingTeam;

    public static TimeoutManager Ensure(MatchContext owner)
    {
        if (Instance != null) return Instance;
        TimeoutManager manager = owner.GetComponent<TimeoutManager>();
        if (manager == null) manager = owner.gameObject.AddComponent<TimeoutManager>();
        return manager;
    }

    void Awake()
    {
        Instance = this;
        context = MatchContext.Instance != null ? MatchContext.Instance : GetComponent<MatchContext>();
        BuildUI();
    }

    void Start()
    {
        if (ScoreManager.Instance != null)
        {
            observedHomeScore = ScoreManager.Instance.HomeScore;
            observedAwayScore = ScoreManager.Instance.AwayScore;
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        UpdateHudButton();
        ObserveScoreMomentum();
        if (!active)
        {
            EvaluateBotTimeout();
            return;
        }
        if (Time.timeScale <= 0f) return; // hard user pause suspends even the timeout

        timeoutClock.Tick(Time.deltaTime);
        if (!restartPreparation &&
            timeoutClock.DisplayValue <= timeoutDisplayedSeconds - defensiveHalfDisplayedSeconds)
        {
            restartPreparation = true;
            CommandRestartPreparation();
        }
        if (Time.time >= nextPositionRefresh)
        {
            nextPositionRefresh = Time.time + 0.5f;
            if (restartPreparation) CommandRestartPreparation();
            else CommandDefensiveHalves();
        }
        UpdateActivePanel();
        if (timeoutClock.IsComplete)
        {
            CommandRestartPreparation();
            if (RestartTakerReady()) EndTimeout(true);
        }
    }

    public int UsedTimeouts(TeamSide team)
        => team != null && used.TryGetValue(team, out int count) ? count : 0;

    public int RemainingTimeouts(TeamSide team)
        => Mathf.Max(0, timeoutsPerTeam - UsedTimeouts(team));

    public bool CanCall(TeamSide team)
    {
        if (team == null || context == null || active || RemainingTimeouts(team) <= 0 ||
            Time.timeScale <= 0f || (MatchTimer.Instance != null && MatchTimer.Instance.MatchOver))
            return false;
        if (MatchTimer.Instance != null && MatchTimer.Instance.QuarterBreakActive) return false;
        return TeamEntitledToPossession() == team;
    }

    TeamSide TeamEntitledToPossession()
    {
        if (context == null) return null;
        if (PenaltyManager.Instance != null && PenaltyManager.Instance.Active)
            return PenaltyManager.Instance.AttackingTeam;
        if (context.OutOfBoundsRestartActive) return context.OutOfBoundsRestartTeam;
        if (context.FreeThrowActive && context.FreeThrowCarrier != null)
        {
            MatchPlayerState carrier = MatchPlayerState.For(context.FreeThrowCarrier);
            if (carrier != null) return carrier.Team;
            if (context.PlayerTeam != null && context.PlayerTeam.Contains(context.FreeThrowCarrier))
                return context.PlayerTeam;
            if (context.BotTeam != null && context.BotTeam.Contains(context.FreeThrowCarrier))
                return context.BotTeam;
        }
        return context.PossessingTeam;
    }

    public bool CallTimeout(TeamSide team)
    {
        if (!CanCall(team)) return false;
        callingTeam = team;
        CaptureRestartState(team);
        if (!context.BeginWaterPoloStoppage(WaterPoloStoppageKind.Timeout, team))
        {
            callingTeam = null;
            return false;
        }
        used[team] = UsedTimeouts(team) + 1;
        RefereeController.Instance?.TriggerFoul();
        float realSeconds = MatchTimer.Instance != null
            ? MatchTimer.Instance.RealSecondsForDisplayedSeconds(timeoutDisplayedSeconds)
            : timeoutDisplayedSeconds * (90f / 480f);
        timeoutClock = new CompressedTimer(timeoutDisplayedSeconds, realSeconds);
        timeoutStartedAt = Time.time;
        active = true;
        restartPreparation = false;
        nextPositionRefresh = Time.time;
        activePanel.SetActive(true);
        MatchSubstitutionSuggestionUI.Instance?.Close();
        if (TouchControls.Instance != null) TouchControls.Instance.SetGameplayVisible(false);
        CacheAndCommandKeepers();
        CommandDefensiveHalves();
        UpdateActivePanel();
        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Timeout - " + (team == context.PlayerTeam ? "YOU" : "BOT"));
        if (team == context.BotTeam)
        {
            lastBotTimeoutTime = Time.time;
            humanUnansweredGoals = 0;
        }
        return true;
    }

    void CommandDefensiveHalves()
    {
        CommandTeam(context != null ? context.PlayerTeam : null, false);
        CommandTeam(context != null ? context.BotTeam : null, false);
        CommandKeepers();
    }

    void CommandRestartPreparation()
    {
        CommandTeam(context != null ? context.PlayerTeam : null, true);
        CommandTeam(context != null ? context.BotTeam : null, true);
        CommandKeepers();
    }

    void CommandTeam(TeamSide team, bool restart)
    {
        if (team == null || team.members == null || MatchSquadManager.Instance == null) return;
        for (int i = 0; i < team.members.Length; i++)
        {
            MatchPlayerState player = MatchPlayerState.For(team.members[i]);
            if (player == null || !player.GameplayEligible ||
                (player.MovePurpose != MatchMovePurpose.None &&
                 player.MovePurpose != MatchMovePurpose.Timeout)) continue;

            Vector2 target = GetPositioningTarget(player, restart);

            if (player.MovePurpose == MatchMovePurpose.Timeout)
                player.Retarget(MatchMovePurpose.Timeout, target,
                                MatchMoveAnchor.TimeoutPosition);
            else
                player.BeginMove(MatchMovePurpose.Timeout, target, positioningSpeed,
                                 positioningArrivalRadius, true, false,
                                 MatchMoveAnchor.TimeoutPosition);
        }
    }

    // Shared with timeout substitutions so a replacement coming from the bench joins the exact
    // current phase rather than first swimming to the live flying-substitution exchange.
    public Vector2 GetPositioningTarget(MatchPlayerState player)
        => GetPositioningTarget(player, restartPreparation);

    Vector2 GetPositioningTarget(MatchPlayerState player, bool restart)
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (player == null || player.Team == null || squad == null) return Vector2.zero;
        TeamSide team = player.Team;

        if (!restart)
        {
            int ordinal = 0;
            int eligibleCount = 0;
            if (team.members != null)
            {
                for (int i = 0; i < team.members.Length; i++)
                {
                    MatchPlayerState member = MatchPlayerState.For(team.members[i]);
                    if (member == null || !member.GameplayEligible) continue;
                    if (member == player) ordinal = eligibleCount;
                    eligibleCount++;
                }
            }
            return squad.Geometry.TimeoutGatheringPoint(team, ordinal, eligibleCount);
        }

        if (restartMode == RestartMode.Penalty && PenaltyManager.Instance != null &&
            PenaltyManager.Instance.TryGetTimeoutRestartTarget(player.transform,
                                                                out Vector2 penaltyTarget))
            return penaltyTarget;

        EnsureRestartCarrier();
        Vector2 restartPoint = HalfwayRestartPoint(callingTeam);
        if (team == callingTeam && player.transform == restartCarrier) return restartPoint;

        TeamSide opponent = context != null ? context.EnemyOf(team) : null;
        if (team == callingTeam)
        {
            bool manUp = MissingLegalFieldPlayers(opponent) > 0;
            return manUp ? team.ManUpSpot(player.transform, restartPoint)
                         : team.AttackPositionFor(player.transform, restartPoint, opponent);
        }

        bool manDown = MissingLegalFieldPlayers(team) > 0;
        if (manDown) return team.ManDownSpot(player.transform, restartPoint);
        Transform assignment = team.MarkAssignmentFor(player.transform, callingTeam);
        return assignment != null ? team.MarkSpot(player.transform, assignment)
                                  : team.RestartFormationSpot(player.transform, false);
    }

    void EndTimeout(bool restoreControls)
    {
        if (!active) return;
        active = false;
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (squad != null)
        {
            IReadOnlyList<MatchPlayerState> players = squad.Participants;
            for (int i = 0; i < players.Count; i++)
            {
                MatchPlayerState player = players[i];
                if (player == null) continue;
                // A very late timeout substitution may still be visibly entering/leaving. Its
                // lineup slot is already legal and atomic; let that choreography finish instead
                // of marooning the body outside the wall or teleporting it at the whistle.
                bool timeoutSubStillMoving = player.MovePurpose == MatchMovePurpose.Timeout &&
                    (player.Status == MatchPlayerStatus.SubstitutingIn ||
                     player.Status == MatchPlayerStatus.SubstitutingOut);
                if (!timeoutSubStillMoving) player.StopMove(MatchMovePurpose.Timeout);
            }
        }
        if (context != null)
        {
            context.DelayTimedGameplayWindows(Time.time - timeoutStartedAt);
            bool preservedPenalty = restartMode == RestartMode.Penalty &&
                                    PenaltyManager.Instance != null &&
                                    PenaltyManager.Instance.Active;
            if (restoreControls && !preservedPenalty)
            {
                EnsureRestartCarrier();
                PrepareOrdinaryFreeThrowRestart();
            }
            context.EndWaterPoloStoppage(WaterPoloStoppageKind.Timeout);
        }
        if (restoreControls) RefereeController.Instance?.TriggerFoul();
        for (int i = 0; i < timeoutKeepers.Count; i++)
            if (timeoutKeepers[i] != null) timeoutKeepers[i].EndTimeoutPositioning();
        timeoutKeepers.Clear();
        if (MatchTeamManagementUI.Instance != null && MatchTeamManagementUI.Instance.IsOpen &&
            MatchTeamManagementUI.Instance.Mode == MatchTeamManagementMode.Timeout)
            MatchTeamManagementUI.Instance.ForceClose();
        activePanel.SetActive(false);
        bool goalRestartStillOwnsPresentation = ScoreManager.Instance != null &&
                                                ScoreManager.Instance.GoalRestartInProgress;
        if (restoreControls && !goalRestartStillOwnsPresentation && TouchControls.Instance != null)
            TouchControls.Instance.SetGameplayVisible(true);
        callingTeam = null;
        restartCarrier = null;
    }

    void OpenTeamManagement()
    {
        if (!active || context == null || MatchTeamManagementUI.Instance == null) return;
        MatchTeamManagementUI.Instance.Show(context.PlayerTeam, MatchTeamManagementMode.Timeout, null);
    }

    public void Shutdown()
    {
        EndTimeout(false);
        if (context != null) context.EndWaterPoloStoppage(WaterPoloStoppageKind.Timeout);
    }

    // A penalty shooter owns a dedicated transaction and cannot be substituted before the throw.
    // Ordinary timeout restarts may nominate another eligible field taker after a substitution.
    public bool IsProtectedRestartParticipant(MatchPlayerState player)
    {
        if (!active || player == null) return false;
        return restartMode == RestartMode.Penalty && PenaltyManager.Instance != null &&
               PenaltyManager.Instance.IsActiveShooter(player.transform);
    }

    void CaptureRestartState(TeamSide team)
    {
        restartCarrier = context != null && context.Ball != null
            ? context.Ball.transform.parent : null;

        if (PenaltyManager.Instance != null && PenaltyManager.Instance.Active)
            restartMode = RestartMode.Penalty;
        else if (context != null && context.OutOfBoundsRestartActive)
        {
            restartMode = RestartMode.OutOfBounds;
            restartCarrier = context.OutOfBoundsFetcher;
        }
        else if (context != null && context.FreeThrowActive)
            restartMode = RestartMode.FreeThrow;
        else if (ScoreManager.Instance != null && ScoreManager.Instance.GoalRestartInProgress)
            restartMode = RestartMode.GoalRestart;
        else
            restartMode = RestartMode.LivePossession;

        // A keeper may call a timeout while holding the ball, but every ordinary timeout restart
        // is taken by a legal field swimmer on/behind halfway. Preserve a penalty shooter only.
        if (restartMode != RestartMode.Penalty) EnsureRestartCarrier();
    }

    void EnsureRestartCarrier()
    {
        if (callingTeam == null) return;
        bool currentLegal = restartCarrier != null && callingTeam.Contains(restartCarrier) &&
                            MatchPlayerState.IsGameplayEligible(restartCarrier);
        if (!currentLegal)
            restartCarrier = ClosestEligibleMember(callingTeam, HalfwayRestartPoint(callingTeam));
    }

    Vector2 HalfwayRestartPoint(TeamSide team)
    {
        if (team == null || team.defendGoal == null || team.attackGoal == null) return Vector2.zero;
        float halfwayX = MatchSquadManager.Instance != null
            ? MatchSquadManager.Instance.Geometry.HalfwayX(team)
            : (team.defendGoal.position.x + team.attackGoal.position.x) * 0.5f;
        float forwardX = Mathf.Sign(team.attackGoal.position.x - team.defendGoal.position.x);
        if (forwardX == 0f) forwardX = -Mathf.Sign(team.defendGoal.position.x);
        return new Vector2(halfwayX - forwardX * Mathf.Max(positioningArrivalRadius,
                                                            halfwayRestartInset), 0f);
    }

    bool RestartTakerReady()
    {
        // A penalty survives the timeout unchanged, but the shooter may have joined the first-45
        // defensive-half gathering. Keep the timeout stoppage at 0:00 until that same shooter has
        // physically returned to the existing penalty spot; PenaltyManager then resumes without a
        // visible full-pool snap.
        if (restartMode == RestartMode.Penalty)
        {
            if (PenaltyManager.Instance == null || !PenaltyManager.Instance.Active) return true;
            if (restartCarrier == null ||
                !PenaltyManager.Instance.TryGetTimeoutRestartTarget(restartCarrier,
                                                                     out Vector2 penaltyTarget))
                return true;
            return Vector2.Distance(restartCarrier.position, penaltyTarget) <=
                   positioningArrivalRadius;
        }
        EnsureRestartCarrier();
        if (restartCarrier == null) return true; // forfeit/minimum-player flow owns this edge case
        return Vector2.Distance(restartCarrier.position, HalfwayRestartPoint(callingTeam)) <=
               positioningArrivalRadius;
    }

    void PrepareOrdinaryFreeThrowRestart()
    {
        if (context == null || callingTeam == null || restartCarrier == null) return;

        // Retire any old OOB collision reservation/kickoff instruction before assigning the dead
        // ball. GiveBallTo uses the normal holder implementations and deliberately runs before the
        // timeout-only clock hold is armed, because SetPossession clears an older free throw.
        context.ClearGrabBan();
        context.ClearKickoffPass();
        Transform oldHolder = context.Ball != null ? context.Ball.transform.parent : null;
        if (oldHolder != null && oldHolder != restartCarrier)
        {
            Goalkeeper oldKeeper = oldHolder.GetComponent<Goalkeeper>();
            if (oldKeeper != null) oldKeeper.OnBallStolen();
            else context.ForceDropHeldBall();
        }

        if (context.Ball == null || context.Ball.transform.parent != restartCarrier)
            context.GiveBallTo(restartCarrier, callingTeam);
        else
            context.SetPossession(callingTeam);
        context.StartTimeoutFreeThrow(restartCarrier);
    }

    static int MissingLegalFieldPlayers(TeamSide team)
    {
        if (team == null || team.members == null) return 0;
        int missing = 0;
        for (int i = 0; i < team.members.Length; i++)
            if (!MatchPlayerState.IsGameplayEligible(team.members[i])) missing++;
        return missing;
    }

    void CacheAndCommandKeepers()
    {
        timeoutKeepers.Clear();
        Goalkeeper[] keepers = Object.FindObjectsByType<Goalkeeper>(FindObjectsInactive.Include);
        for (int i = 0; i < keepers.Length; i++)
        {
            Goalkeeper keeper = keepers[i];
            if (keeper == null || keeper.DefendingTeam == null) continue;
            timeoutKeepers.Add(keeper);
        }
        CommandKeepers();
    }

    void CommandKeepers()
    {
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (squad == null) return;
        for (int i = 0; i < timeoutKeepers.Count; i++)
        {
            Goalkeeper keeper = timeoutKeepers[i];
            if (keeper == null || keeper.DefendingTeam == null) continue;
            keeper.BeginTimeoutPositioning(squad.Geometry.TimeoutKeeperPoint(keeper.DefendingTeam),
                                           positioningSpeed);
        }
    }

    static Transform ClosestEligibleMember(TeamSide team, Vector2 point)
    {
        if (team == null || team.members == null) return null;
        Transform best = null;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < team.members.Length; i++)
        {
            Transform member = team.members[i];
            if (!MatchPlayerState.IsGameplayEligible(member)) continue;
            float distance = ((Vector2)member.position - point).sqrMagnitude;
            if (distance < bestDistance) { bestDistance = distance; best = member; }
        }
        return best;
    }

    void ObserveScoreMomentum()
    {
        ScoreManager score = ScoreManager.Instance;
        if (score == null) return;
        if (score.HomeScore > observedHomeScore)
        {
            humanUnansweredGoals += score.HomeScore - observedHomeScore;
            observedHomeScore = score.HomeScore;
        }
        if (score.AwayScore > observedAwayScore)
        {
            humanUnansweredGoals = 0;
            observedAwayScore = score.AwayScore;
        }
        // Defensive guard for a scene reset/replay restoring an earlier score.
        if (score.HomeScore < observedHomeScore || score.AwayScore < observedAwayScore)
        {
            observedHomeScore = score.HomeScore;
            observedAwayScore = score.AwayScore;
            humanUnansweredGoals = 0;
        }
    }

    void EvaluateBotTimeout()
    {
        if (Time.timeScale <= 0f || Time.time < nextBotEvaluationTime || context == null ||
            context.BotTeam == null) return;
        nextBotEvaluationTime = Time.time + Mathf.Max(0.25f, botEvaluationIntervalSeconds);
        if (!CanCall(context.BotTeam)) return;

        MatchTimer timer = MatchTimer.Instance;
        ScoreManager scoreboard = ScoreManager.Instance;
        if (timer == null || scoreboard == null || timer.CurrentQuarter <= 0) return;
        float displayedRemaining = timer.QuarterDisplayedRemaining;
        if (displayedRemaining < 10f) return; // no usable organized possession remains

        int botMargin = scoreboard.AwayScore - scoreboard.HomeScore;
        int absMargin = Mathf.Abs(botMargin);
        bool close = absMargin <= 2;
        int coachScore = 0;

        // High-leverage Q4 possessions, particularly the last two displayed minutes.
        if (timer.CurrentQuarter == 4 && close && botMargin <= 0) coachScore += 24;
        if (timer.CurrentQuarter == 4 && close && displayedRemaining <= 150f)
        {
            coachScore += 30;
            if (botMargin <= 0) coachScore += 16;
            if (displayedRemaining <= 65f) coachScore += 14;
        }

        // An opponent exclusion is the strongest non-late-game signal: organize the man-up.
        bool manUp = MissingLegalFieldPlayers(context.PlayerTeam) > 0;
        if (manUp)
        {
            coachScore += 45;
            if (timer.CurrentQuarter >= 3) coachScore += 12;
            if (close) coachScore += 8;
        }

        if (humanUnansweredGoals >= 3) coachScore += 34;
        else if (humanUnansweredGoals >= 2) coachScore += 20;

        int severe = 0;
        int tired = 0;
        int activeCount = 0;
        float staminaTotal = 0f;
        MatchSquadManager squad = MatchSquadManager.Instance;
        if (squad != null)
        {
            IReadOnlyList<MatchPlayerState> players = squad.Participants;
            for (int i = 0; i < players.Count; i++)
            {
                MatchPlayerState player = players[i];
                if (player == null || player.Team != context.BotTeam || !player.GameplayEligible)
                    continue;
                float stamina = player.StaminaPercent;
                activeCount++;
                staminaTotal += stamina;
                if (stamina <= severeFatigueThreshold) severe++;
                if (stamina <= tiredFatigueThreshold) tired++;
            }
        }
        if (severe >= 2) coachScore += 18;
        if (severe >= 3) coachScore += 8;
        if (tired >= 4) coachScore += 12;
        if (activeCount > 0 && staminaTotal / activeCount < tiredFatigueThreshold)
            coachScore += 8;

        // Preserve resources in low-value states. This is coaching policy, never a rules gate.
        if (timer.CurrentQuarter == 1 && timer.QuarterRealElapsed < 30f)
            coachScore -= manUp ? 20 : 50;
        if (botMargin >= 3) coachScore -= 25;
        if (botMargin >= 5 && (timer.CurrentQuarter < 4 || displayedRemaining < 180f)) return;
        if (timer.CurrentQuarter <= 2 && !manUp && humanUnansweredGoals < 2 && severe < 2)
            coachScore -= 15;

        int threshold = botCallScoreThreshold + (UsedTimeouts(context.BotTeam) > 0 ? 5 : 0);
        bool critical = coachScore >= 100;
        if (!critical && Time.time - lastBotTimeoutTime < botCoachSpacingSeconds) return;
        if (coachScore >= threshold) CallTimeout(context.BotTeam);
    }

    void UpdateHudButton()
    {
        if (timeoutButton == null || context == null) return;
        int count = UsedTimeouts(context.PlayerTeam);
        string dots = string.Empty;
        for (int i = 0; i < timeoutsPerTeam; i++)
            dots += i < count ? "<color=#667587>○</color> " : "<color=#F4C54B>●</color> ";
        timeoutButtonText.text = "TIMEOUT   " + dots.TrimEnd();
        bool legal = CanCall(context.PlayerTeam);
        timeoutButton.interactable = legal;
        timeoutButtonImage.color = legal
            ? new Color(0.035f, 0.28f, 0.48f, 0.96f)
            : new Color(0.08f, 0.12f, 0.18f, 0.68f);
    }

    void UpdateActivePanel()
    {
        if (!active || countdownText == null) return;
        int displayed = Mathf.CeilToInt(timeoutClock.DisplayValue);
        countdownText.text = "TIMEOUT   " + (displayed / 60) + ":" + (displayed % 60).ToString("00");
        phaseText.text = timeoutClock.IsComplete
            ? "READY FOR RESTART"
            : restartPreparation
                ? "FINAL 15 - MOVE TO RESTART SHAPE"
                : "DEFENSIVE HALF - COACH SIDE";
    }

    void BuildUI()
    {
        EnsureEventSystem();
        canvasRoot = new GameObject("TimeoutCanvas");
        canvasRoot.transform.SetParent(transform, false);
        Canvas canvas = canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 97;
        CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRoot.AddComponent<GraphicRaycaster>();

        GameObject buttonObject = new GameObject("TimeoutButton");
        buttonObject.transform.SetParent(canvasRoot.transform, false);
        RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0f, 1f);
        buttonRect.pivot = new Vector2(0f, 1f);
        buttonRect.anchoredPosition = new Vector2(22f, -76f);
        buttonRect.sizeDelta = new Vector2(210f, 44f);
        timeoutButtonImage = buttonObject.AddComponent<Image>();
        CrestUITheme.ApplyButton(timeoutButtonImage, new Color(0.035f, 0.28f, 0.48f, 0.96f));
        timeoutButton = buttonObject.AddComponent<Button>();
        timeoutButton.targetGraphic = timeoutButtonImage;
        timeoutButton.onClick.AddListener(() => CallTimeout(context != null ? context.PlayerTeam : null));
        timeoutButtonText = MakeText(buttonObject.transform, "Label", 17f,
                                     Vector2.zero, buttonRect.sizeDelta);

        activePanel = new GameObject("ActiveTimeoutPanel");
        activePanel.transform.SetParent(canvasRoot.transform, false);
        RectTransform panelRect = activePanel.AddComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -90f);
        panelRect.sizeDelta = new Vector2(475f, 126f);
        Image panelImage = activePanel.AddComponent<Image>();
        panelImage.color = new Color(0.018f, 0.07f, 0.16f, 0.97f);
        Outline outline = activePanel.AddComponent<Outline>();
        outline.effectColor = new Color(0.94f, 0.71f, 0.20f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);
        countdownText = MakeText(activePanel.transform, "Countdown", 28f,
                                 new Vector2(-92f, 31f), new Vector2(270f, 42f));
        countdownText.color = new Color(1f, 0.82f, 0.28f);
        phaseText = MakeText(activePanel.transform, "Phase", 15f,
                             new Vector2(-92f, -18f), new Vector2(280f, 35f));
        phaseText.color = new Color(0.78f, 0.88f, 0.98f);
        MakePanelButton(activePanel.transform, "TEAM\nMANAGEMENT", new Vector2(145f, 0f),
                        OpenTeamManagement);
        activePanel.SetActive(false);
    }

    static TMP_Text MakeText(Transform parent, string name, float size,
                             Vector2 position, Vector2 dimensions)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        return text;
    }

    static void MakePanelButton(Transform parent, string label, Vector2 position,
                                UnityEngine.Events.UnityAction click)
    {
        GameObject go = new GameObject("BtnTeamManagement");
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(150f, 76f);
        Image image = go.AddComponent<Image>();
        CrestUITheme.ApplyButton(image, new Color(0.08f, 0.22f, 0.38f, 1f));
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(click);
        TextMeshProUGUI text = MakeText(go.transform, "Label", 15f, Vector2.zero, rect.sizeDelta)
            as TextMeshProUGUI;
        text.text = label;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        GameObject events = new GameObject("EventSystem");
        events.AddComponent<EventSystem>();
        events.AddComponent<StandaloneInputModule>();
    }
}
