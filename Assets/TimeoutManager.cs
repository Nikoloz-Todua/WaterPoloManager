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

    private readonly Dictionary<TeamSide, int> used = new Dictionary<TeamSide, int>();
    private enum RestartMode { LivePossession, FreeThrow, OutOfBounds, Penalty, GoalRestart }

    private MatchContext context;
    private TeamSide callingTeam;
    private RestartMode restartMode;
    private Transform restartCarrier;
    private Vector2 preservedRestartPoint;
    private Goalkeeper keeperRestartSource;
    private CompressedTimer timeoutClock;
    private bool active;
    private bool restartPreparation;
    private float nextPositionRefresh;
    private float timeoutStartedAt;

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

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        UpdateHudButton();
        if (!active) return;
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
        if (timeoutClock.IsComplete) EndTimeout(true);
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
        CaptureRestartState(team);
        if (!context.BeginWaterPoloStoppage(WaterPoloStoppageKind.Timeout, team)) return false;
        used[team] = UsedTimeouts(team) + 1;
        callingTeam = team;
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
        CommandDefensiveHalves();
        UpdateActivePanel();
        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Timeout - " + (team == context.PlayerTeam ? "YOU" : "BOT"));
        return true;
    }

    void CommandDefensiveHalves()
    {
        CommandTeam(context != null ? context.PlayerTeam : null, false);
        CommandTeam(context != null ? context.BotTeam : null, false);
    }

    void CommandRestartPreparation()
    {
        CommandTeam(context != null ? context.PlayerTeam : null, true);
        CommandTeam(context != null ? context.BotTeam : null, true);
    }

    void CommandTeam(TeamSide team, bool restart)
    {
        if (team == null || team.members == null || MatchSquadManager.Instance == null) return;
        Vector2 ownGoal = team.defendGoal != null ? (Vector2)team.defendGoal.position : Vector2.zero;
        float span = team.attackGoal != null && team.defendGoal != null
            ? Vector2.Distance(team.attackGoal.position, team.defendGoal.position) : 14f;
        Vector2 forward = team.attackGoal != null
            ? ((Vector2)team.attackGoal.position - ownGoal).normalized
            : (ownGoal.x < 0f ? Vector2.right : Vector2.left);
        Vector2 across = new Vector2(-forward.y, forward.x);
        for (int i = 0; i < team.members.Length; i++)
        {
            MatchPlayerState player = MatchPlayerState.For(team.members[i]);
            if (player == null || !player.GameplayEligible ||
                (player.MovePurpose != MatchMovePurpose.None &&
                 player.MovePurpose != MatchMovePurpose.Timeout)) continue;

            Vector2 target;
            if (!restart)
            {
                // Compact but readable organization in the team's own defensive half.
                float lateral = team.members.Length > 1
                    ? ((float)i / (team.members.Length - 1)) * 2f - 1f : 0f;
                target = ownGoal + forward * (span * 0.28f) + across * (lateral * 1.45f);
            }
            else if (restartMode == RestartMode.Penalty &&
                     PenaltyManager.Instance != null &&
                     PenaltyManager.Instance.TryGetTimeoutRestartTarget(player.transform,
                                                                         out Vector2 penaltyTarget))
            {
                target = penaltyTarget;
            }
            else if (restartMode == RestartMode.OutOfBounds &&
                     player.transform == restartCarrier)
            {
                target = preservedRestartPoint;
            }
            else if (player.transform == restartCarrier && team == callingTeam)
            {
                if (restartMode == RestartMode.FreeThrow || restartMode == RestartMode.GoalRestart)
                    target = preservedRestartPoint;
                else
                {
                    // A live-possession timeout resumes on or just behind halfway for its owner.
                    Vector2 middle = team.attackGoal != null
                        ? ((Vector2)team.attackGoal.position + ownGoal) * 0.5f : Vector2.zero;
                    target = middle - forward * 0.25f;
                }
            }
            else
            {
                target = team.RestartFormationSpot(player.transform, team == callingTeam);
            }

            if (player.MovePurpose == MatchMovePurpose.Timeout)
                player.Retarget(MatchMovePurpose.Timeout, target);
            else
                player.BeginMove(MatchMovePurpose.Timeout, target, positioningSpeed,
                                 positioningArrivalRadius, true, false);
        }
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
                if (players[i] != null) players[i].StopMove(MatchMovePurpose.Timeout);
        }
        if (context != null)
        {
            if (keeperRestartSource != null && restartCarrier != null && context.Ball != null &&
                context.Ball.transform.parent == keeperRestartSource.transform)
            {
                keeperRestartSource.OnBallStolen(); // clear its hold without making the ball live
                context.GiveBallTo(restartCarrier, callingTeam);
            }
            context.DelayTimedGameplayWindows(Time.time - timeoutStartedAt);
            context.EndWaterPoloStoppage(WaterPoloStoppageKind.Timeout);
        }
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
        keeperRestartSource = null;
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

    // The awarded restart taker cannot be removed during timeout management: doing so would
    // destroy a free-throw/goal-throw/penalty transaction owned by an existing match system.
    public bool IsProtectedRestartParticipant(MatchPlayerState player)
    {
        if (!active || player == null) return false;
        if (restartMode == RestartMode.Penalty)
            return PenaltyManager.Instance != null &&
                   PenaltyManager.Instance.IsActiveShooter(player.transform);
        return player.transform == restartCarrier;
    }

    void CaptureRestartState(TeamSide team)
    {
        keeperRestartSource = null;
        restartCarrier = context != null && context.Ball != null
            ? context.Ball.transform.parent : null;
        preservedRestartPoint = restartCarrier != null
            ? (Vector2)restartCarrier.position : Vector2.zero;

        if (PenaltyManager.Instance != null && PenaltyManager.Instance.Active)
            restartMode = RestartMode.Penalty;
        else if (context != null && context.OutOfBoundsRestartActive)
        {
            restartMode = RestartMode.OutOfBounds;
            restartCarrier = context.OutOfBoundsFetcher;
            preservedRestartPoint = context.OutOfBoundsRestartPoint;
        }
        else if (context != null && context.FreeThrowActive)
            restartMode = RestartMode.FreeThrow;
        else if (ScoreManager.Instance != null && ScoreManager.Instance.GoalRestartInProgress)
            restartMode = RestartMode.GoalRestart;
        else
            restartMode = RestartMode.LivePossession;

        // A keeper may call a timeout while holding the ball, but the live-possession restart is
        // still taken on/behind halfway. Nominate a legal field taker now and transfer the dead
        // ball only when the timeout ends; the swimmer gets there physically during the final 15.
        if (restartMode == RestartMode.LivePossession && restartCarrier != null)
        {
            keeperRestartSource = restartCarrier.GetComponent<Goalkeeper>();
            if (keeperRestartSource != null)
            {
                Transform fieldTaker = ClosestEligibleMember(team, Vector2.zero);
                if (fieldTaker != null) restartCarrier = fieldTaker;
                else keeperRestartSource = null;
            }
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
        phaseText.text = restartPreparation
            ? "PREPARE TO RESTART"
            : "ORGANIZE IN DEFENSIVE HALVES";
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
