using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

// Full-time result screen, built entirely in code (no prefabs, no Inspector wiring).
// Hidden until MatchTimer calls Show() at the final whistle or on a forfeit:
// dark overlay, "FULL TIME" (or "FORFEIT") title, final score from ScoreManager,
// colored winner line, and PLAY AGAIN / MAIN MENU buttons in the MainMenuUI style.
// Fades in over 0.5s using UNSCALED time (the match end sets Time.timeScale = 0).
public class MatchResultUI : MonoBehaviour
{
    public static MatchResultUI Instance { get; private set; }

    [SerializeField] private float fadeSeconds = 0.5f;

    private static readonly Color ButtonColor = new Color(0.05f, 0.1f, 0.25f, 0.85f);

    private GameObject root;       // whole overlay canvas — inactive until Show()
    private CanvasGroup group;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI scoreText;
    private TextMeshProUGUI winnerText;
    private Image playerFlag;
    private Image opponentFlag;
    private Transform canvasRoot;

    void Awake()
    {
        Instance = this;
        BuildUI();
        root.SetActive(false);
    }

    // outcome: +1 = player wins, -1 = player loses, 0 = draw.
    // title is "FULL TIME" for a normal end, "FORFEIT" for a forfeit.
    public void Show(string title, int outcome)
    {
        int you = ScoreManager.Instance != null ? ScoreManager.Instance.HomeScore : 0;
        int bot = ScoreManager.Instance != null ? ScoreManager.Instance.AwayScore : 0;

        titleText.text = title;
        bool championship = MatchPresentationContext.ResultWasChampionship;
        string playerClub = championship ? MatchPresentationContext.ResultPlayerClub : "YOU";
        string opponentClub = championship ? MatchPresentationContext.ResultOpponentClub : "BOT";
        if (championship)
        {
            // Forfeits may need a one-goal walkover margin even when the frozen live scoreboard was
            // tied. Show the authoritative score that was actually written to the championship.
            you = MatchPresentationContext.ResultPlayerGoals;
            bot = MatchPresentationContext.ResultOpponentGoals;
            outcome = you.CompareTo(bot);
        }
        scoreText.text = playerClub + "  " + you + " — " + bot + "  " + opponentClub;
        bool worldCup = MatchPresentationContext.ResultWasWorldCup;
        playerFlag.gameObject.SetActive(worldCup);
        opponentFlag.gameObject.SetActive(worldCup);
        if (worldCup)
        {
            CountryCatalog catalog = CountryCatalog.Instance;
            playerFlag.sprite = catalog != null ? catalog.FlagFor(playerClub) : null;
            opponentFlag.sprite = catalog != null ? catalog.FlagFor(opponentClub) : null;
        }

        if (outcome > 0)      { winnerText.text = "YOU WIN!"; winnerText.color = Color.cyan; }
        else if (outcome < 0) { winnerText.text = "YOU LOSE"; winnerText.color = new Color(1f, 0.25f, 0.25f); }
        else                  { winnerText.text = "DRAW";     winnerText.color = Color.yellow; }

        root.SetActive(true);
        StartCoroutine(FadeIn());
    }

    void BuildUI()
    {
        EnsureEventSystem();

        root = new GameObject("ResultCanvas");
        root.transform.SetParent(transform, false);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120; // above the HUD and the touch controls
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        group = root.AddComponent<CanvasGroup>();
        canvasRoot = root.transform;

        // Full-screen dark overlay (also blocks clicks reaching the game underneath).
        GameObject overlayGo = new GameObject("Overlay");
        overlayGo.transform.SetParent(canvasRoot, false);
        Image overlay = overlayGo.AddComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0.8f);
        RectTransform oRt = overlay.rectTransform;
        oRt.anchorMin = Vector2.zero;
        oRt.anchorMax = Vector2.one;
        oRt.offsetMin = oRt.offsetMax = Vector2.zero;

        titleText  = MakeText("Title",  "FULL TIME", 64f, new Vector2(0f, 190f));
        scoreText  = MakeText("Score",  "",          56f, new Vector2(0f, 70f));
        scoreText.enableAutoSizing = true;
        scoreText.fontSizeMin = 28f;
        scoreText.fontSizeMax = 56f;
        scoreText.textWrappingMode = TextWrappingModes.NoWrap;
        winnerText = MakeText("Winner", "",          44f, new Vector2(0f, -20f));
        playerFlag = MakeFlag("PlayerCountryFlag", new Vector2(-515f, 70f));
        opponentFlag = MakeFlag("OpponentCountryFlag", new Vector2(515f, 70f));
        playerFlag.gameObject.SetActive(false);
        opponentFlag.gameObject.SetActive(false);

        // Championship fixtures return to their persistent table. Casual matches retain replay.
        MakeButton("CONTINUE", new Vector2(0f, -120f), ContinueAfterResult);
        MakeButton("MAIN MENU",  new Vector2(0f, -210f), () => LoadScene("HubScene"));
    }

    static void LoadScene(string sceneName)
    {
        Time.timeScale = 1f; // the match end froze time — never carry that into the next scene
        LoadingOverlayUI.LoadScene(sceneName, false, "LOADING...");
    }

    static void ContinueAfterResult()
    {
        LoadScene(MatchPresentationContext.ResultWasChampionship ? "HubScene" : NavigationManager.MatchScene);
    }

    TextMeshProUGUI MakeText(string name, string content, float size, Vector2 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(canvasRoot, false);
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = content;
        txt.fontSize = size;
        txt.fontStyle = FontStyles.Bold;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        RectTransform rt = txt.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(900f, 90f);
        rt.anchoredPosition = pos;
        return txt;
    }

    Image MakeFlag(string name, Vector2 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(canvasRoot, false);
        Image image = go.AddComponent<Image>();
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        RectTransform rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(92f, 62f);
        return image;
    }

    void MakeButton(string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn" + label);
        go.transform.SetParent(canvasRoot, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(300f, 70f);
        rt.anchoredPosition = pos;

        Image img = go.AddComponent<Image>();
        img.sprite = LocalizedButtonStyler.UniversalSprite();
        img.color = ButtonColor;

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        LocalizedButtonStyler.AddLabel(go.transform, label, 28f, new Vector2(300f, 70f));
    }

    IEnumerator FadeIn()
    {
        group.alpha = 0f;
        float t = 0f;
        while (t < fadeSeconds)
        {
            t += Time.unscaledDeltaTime; // timeScale is 0 here — must use unscaled time
            group.alpha = Mathf.Clamp01(t / fadeSeconds);
            yield return null;
        }
        group.alpha = 1f;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }
}
