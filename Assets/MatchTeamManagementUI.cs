using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum MatchTeamManagementMode { Pause, Timeout }

// Shared mobile squad-management surface used from both hard pause and a live official timeout.
// It edits only match transactions; RosterManager.SetStarter is intentionally never called.
public sealed class MatchTeamManagementUI : MonoBehaviour
{
    public static MatchTeamManagementUI Instance { get; private set; }
    public bool IsOpen => root != null && root.activeSelf;
    public MatchTeamManagementMode Mode { get; private set; }

    private GameObject root;
    private RectTransform waterContent;
    private RectTransform benchContent;
    private TMP_Text statusText;
    private TMP_Text pendingText;
    private MatchPlayerState selectedOut;
    private MatchPlayerState selectedIn;
    private TeamSide team;
    private Action closed;
    private float nextRefresh;

    public static MatchTeamManagementUI Ensure(GameObject owner)
    {
        if (Instance != null) return Instance;
        MatchTeamManagementUI ui = owner.GetComponent<MatchTeamManagementUI>();
        if (ui == null) ui = owner.AddComponent<MatchTeamManagementUI>();
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
        if (!IsOpen || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        Refresh();
    }

    public void Show(TeamSide managedTeam, MatchTeamManagementMode mode, Action onClosed)
    {
        if (managedTeam == null || MatchSquadManager.Instance == null) return;
        team = managedTeam;
        Mode = mode;
        closed = onClosed;
        selectedOut = null;
        selectedIn = null;
        statusText.text = mode == MatchTeamManagementMode.Pause
            ? "Select OUT and IN. The physical exchange begins after RESUME."
            : "Timeout is live: confirmed players swim to the exchange area now.";
        root.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        root.SetActive(false);
        selectedOut = null;
        selectedIn = null;
        Action callback = closed;
        closed = null;
        callback?.Invoke();
    }

    public void ForceClose()
    {
        if (root != null) root.SetActive(false);
        selectedOut = null;
        selectedIn = null;
        closed = null;
    }

    void Refresh()
    {
        if (team == null || MatchSquadManager.Instance == null) return;
        ClearChildren(waterContent);
        ClearChildren(benchContent);

        var players = MatchSquadManager.Instance.PlayersFor(team);
        int waterRows = 0;
        int benchRows = 0;
        for (int i = 0; i < players.Count; i++)
        {
            MatchPlayerState player = players[i];
            if (player == null) continue;
            bool water = IsWaterStatus(player.Status);
            if (water)
            {
                bool selectable = CanSelectOut(player);
                MakePlayerRow(waterContent, player, true, selectable,
                              selectedOut == player, waterRows++);
            }
            else
            {
                bool selectable = player.AvailableOnBench;
                MakePlayerRow(benchContent, player, false, selectable,
                              selectedIn == player, benchRows++);
            }
        }

        if (waterRows == 0) MakeEmptyRow(waterContent, "No eligible swimmers");
        if (benchRows == 0) MakeEmptyRow(benchContent, "No bench players available");
        pendingText.text = SubstitutionManager.Instance != null
            ? SubstitutionManager.Instance.PendingDescription(team) : string.Empty;
    }

    static bool IsWaterStatus(MatchPlayerStatus status)
    {
        return status == MatchPlayerStatus.OnField ||
               status == MatchPlayerStatus.SubstitutingIn ||
               status == MatchPlayerStatus.SubstitutingOut ||
               status == MatchPlayerStatus.WaitingForExchange ||
               status == MatchPlayerStatus.ExclusionExit ||
               status == MatchPlayerStatus.ExclusionWaiting;
    }

    static bool CanSelectOut(MatchPlayerState player)
    {
        if (player == null || player.SubstitutionPending) return false;
        if (player.Selectable) return true;
        return player.Status == MatchPlayerStatus.ExclusionExit ||
               player.Status == MatchPlayerStatus.ExclusionWaiting;
    }

    void SelectPlayer(MatchPlayerState player, bool outgoing)
    {
        if (outgoing) selectedOut = player;
        else selectedIn = player;
        statusText.text = SelectionSummary();
        Refresh();
    }

    string SelectionSummary()
    {
        string outName = selectedOut != null
            ? "#" + selectedOut.CapNumber + " " + selectedOut.DisplayName : "—";
        string inName = selectedIn != null
            ? "#" + selectedIn.CapNumber + " " + selectedIn.DisplayName : "—";
        return "OUT  " + outName + "     →     IN  " + inName;
    }

    void Confirm()
    {
        if (selectedOut == null || selectedIn == null)
        {
            statusText.text = "Select both an OUT player and an IN player.";
            return;
        }
        SubstitutionManager substitutions = SubstitutionManager.Instance;
        if (substitutions == null)
        {
            statusText.text = "Substitution service unavailable.";
            return;
        }

        bool exclusionReplacement = selectedOut.Status == MatchPlayerStatus.ExclusionExit ||
                                    selectedOut.Status == MatchPlayerStatus.ExclusionWaiting;
        bool accepted;
        string message;
        if (exclusionReplacement)
            accepted = substitutions.RequestExclusionReplacement(selectedOut, selectedIn, out message);
        else if (Mode == MatchTeamManagementMode.Pause)
            accepted = substitutions.QueuePending(selectedOut, selectedIn, out message);
        else
            accepted = substitutions.RequestLive(selectedOut, selectedIn, out message);

        if (!accepted)
        {
            statusText.text = string.IsNullOrEmpty(message) ? "Selection is no longer legal." : message;
            Refresh();
            return;
        }
        Close();
    }

    void CancelPending()
    {
        SubstitutionManager.Instance?.CancelPending(team);
        selectedOut = null;
        selectedIn = null;
        statusText.text = "Pending substitution cancelled.";
        Refresh();
    }

    void MakePlayerRow(RectTransform content, MatchPlayerState player, bool outgoing,
                       bool selectable, bool selected, int rowIndex)
    {
        GameObject row = new GameObject((outgoing ? "Water_" : "Bench_") + player.PlayerId);
        row.transform.SetParent(content, false);
        RectTransform rect = row.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(492f, 58f);
        Image image = row.AddComponent<Image>();
        image.color = selected ? new Color(0.12f, 0.43f, 0.70f, 0.98f)
            : rowIndex % 2 == 0 ? new Color(0.055f, 0.12f, 0.23f, 0.96f)
                                : new Color(0.04f, 0.09f, 0.18f, 0.96f);
        Button button = row.AddComponent<Button>();
        button.targetGraphic = image;
        button.interactable = selectable;
        MatchPlayerState captured = player;
        button.onClick.AddListener(() => SelectPlayer(captured, outgoing));

        GameObject cap = new GameObject("Cap");
        cap.transform.SetParent(row.transform, false);
        TextMeshProUGUI capText = cap.AddComponent<TextMeshProUGUI>();
        capText.text = player.CapNumber.ToString();
        capText.fontSize = 21f;
        capText.fontStyle = FontStyles.Bold;
        capText.alignment = TextAlignmentOptions.Center;
        capText.color = new Color(1f, 0.82f, 0.28f);
        capText.raycastTarget = false;
        RectTransform capRect = capText.rectTransform;
        capRect.anchorMin = capRect.anchorMax = new Vector2(0f, 0.5f);
        capRect.pivot = new Vector2(0f, 0.5f);
        capRect.anchoredPosition = new Vector2(10f, 0f);
        capRect.sizeDelta = new Vector2(42f, 48f);

        GameObject labelObject = new GameObject("Details");
        labelObject.transform.SetParent(row.transform, false);
        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = PlayerLine(player);
        label.fontSize = 17f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = selectable ? Color.white : new Color(0.57f, 0.63f, 0.72f);
        label.raycastTarget = false;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(58f, 3f);
        labelRect.offsetMax = new Vector2(-8f, -3f);
    }

    static string PlayerLine(MatchPlayerState player)
    {
        string status = StatusLabel(player);
        return player.DisplayName + "  <color=#88BFEF>" + player.Position + "</color>" +
               "\nSTA " + Mathf.RoundToInt(player.StaminaPercent * 100f) + "%   PF " +
               player.PersonalFouls + "/3   " + status;
    }

    static string StatusLabel(MatchPlayerState player)
    {
        if (player.PermanentlyDisqualified) return "<color=#FF6670>PLAYER OUT</color>";
        switch (player.Status)
        {
            case MatchPlayerStatus.OnField: return "IN WATER";
            case MatchPlayerStatus.SubstitutingOut: return "SUBSTITUTING OUT";
            case MatchPlayerStatus.SubstitutingIn: return "SUBSTITUTING IN";
            case MatchPlayerStatus.ExclusionExit: return "EXCLUSION EXIT";
            case MatchPlayerStatus.ExclusionWaiting: return "EXCLUDED";
            case MatchPlayerStatus.ExclusionReplacementApproach: return "TO RE-ENTRY AREA";
            case MatchPlayerStatus.ExclusionReplacementWaiting: return "WAITING TO RE-ENTER";
            case MatchPlayerStatus.ExcludedReplacedBench: return "EXCLUSION SERVING";
            case MatchPlayerStatus.PermanentlyOut: return "PLAYER OUT";
            default: return "BENCH";
        }
    }

    static void MakeEmptyRow(RectTransform content, string message)
    {
        GameObject row = new GameObject("Empty");
        row.transform.SetParent(content, false);
        RectTransform rect = row.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(492f, 54f);
        TextMeshProUGUI text = row.AddComponent<TextMeshProUGUI>();
        text.text = message;
        text.fontSize = 17f;
        text.fontStyle = FontStyles.Italic;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.58f, 0.64f, 0.73f);
    }

    void Build()
    {
        EnsureEventSystem();
        root = new GameObject("MatchTeamManagementCanvas");
        root.transform.SetParent(transform, false);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 122;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        GameObject dim = new GameObject("Dim");
        dim.transform.SetParent(root.transform, false);
        RectTransform dimRect = dim.AddComponent<RectTransform>();
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
        Image dimImage = dim.AddComponent<Image>();
        dimImage.color = new Color(0f, 0f, 0f, 0.68f);

        GameObject panel = new GameObject("SquadPanel");
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(1160f, 650f);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.018f, 0.065f, 0.145f, 0.99f);
        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(0.94f, 0.71f, 0.20f, 0.95f);
        outline.effectDistance = new Vector2(3f, -3f);

        TMP_Text title = MakeText(panel.transform, "Title", "TEAM MANAGEMENT", 34f,
                                  new Vector2(0f, 286f), new Vector2(700f, 50f),
                                  TextAlignmentOptions.Center);
        title.color = new Color(1f, 0.82f, 0.28f);
        MakeText(panel.transform, "WaterHeader", "CURRENTLY IN WATER", 21f,
                 new Vector2(-278f, 237f), new Vector2(500f, 34f), TextAlignmentOptions.Center);
        MakeText(panel.transform, "BenchHeader", "BENCH", 21f,
                 new Vector2(278f, 237f), new Vector2(500f, 34f), TextAlignmentOptions.Center);

        waterContent = MakeScrollColumn(panel.transform, "WaterList", new Vector2(-278f, 15f));
        benchContent = MakeScrollColumn(panel.transform, "BenchList", new Vector2(278f, 15f));

        statusText = MakeText(panel.transform, "Status", string.Empty, 17f,
                              new Vector2(0f, -238f), new Vector2(1030f, 34f),
                              TextAlignmentOptions.Center);
        statusText.color = new Color(0.80f, 0.88f, 0.97f);
        pendingText = MakeText(panel.transform, "Pending", string.Empty, 16f,
                               new Vector2(0f, -270f), new Vector2(1030f, 30f),
                               TextAlignmentOptions.Center);
        pendingText.color = new Color(1f, 0.80f, 0.25f);

        MakeButton(panel.transform, "CONFIRM", new Vector2(-310f, -305f), Confirm,
                   new Color(0.03f, 0.40f, 0.27f, 1f), 210f);
        MakeButton(panel.transform, "CANCEL PENDING", new Vector2(0f, -305f), CancelPending,
                   new Color(0.18f, 0.24f, 0.36f, 1f), 250f);
        MakeButton(panel.transform, "BACK", new Vector2(310f, -305f), Close,
                   new Color(0.16f, 0.20f, 0.30f, 1f), 210f);
    }

    static RectTransform MakeScrollColumn(Transform parent, string name, Vector2 position)
    {
        GameObject viewportObject = new GameObject(name);
        viewportObject.transform.SetParent(parent, false);
        RectTransform viewport = viewportObject.AddComponent<RectTransform>();
        viewport.anchorMin = viewport.anchorMax = new Vector2(0.5f, 0.5f);
        viewport.anchoredPosition = position;
        viewport.sizeDelta = new Vector2(510f, 405f);
        Image background = viewportObject.AddComponent<Image>();
        background.color = new Color(0.01f, 0.03f, 0.08f, 0.72f);
        Mask mask = viewportObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        GameObject contentObject = new GameObject("Content");
        contentObject.transform.SetParent(viewport, false);
        RectTransform content = contentObject.AddComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = new Vector2(0f, -7f);
        content.sizeDelta = new Vector2(-14f, 0f);
        VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewportObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;
        return content;
    }

    static TMP_Text MakeText(Transform parent, string name, string value, float size,
                             Vector2 position, Vector2 dimensions, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        return text;
    }

    static Button MakeButton(Transform parent, string label, Vector2 position,
                             UnityEngine.Events.UnityAction click, Color color, float width)
    {
        GameObject go = new GameObject("Btn" + label);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(width, 48f);
        Image image = go.AddComponent<Image>();
        CrestUITheme.ApplyButton(image, color);
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(click);
        LocalizedButtonStyler.AddLabel(go.transform, label, 18f, rect.sizeDelta,
                                       LocalizedButtonStyler.TextZone.NativeCenter);
        return button;
    }

    static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
        GameObject events = new GameObject("EventSystem");
        events.AddComponent<EventSystem>();
        events.AddComponent<StandaloneInputModule>();
    }
}
