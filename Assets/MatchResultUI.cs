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
        scoreText.text = "YOU  " + you + " — " + bot + "  BOT";

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
        winnerText = MakeText("Winner", "",          44f, new Vector2(0f, -20f));

        MakeButton("PLAY AGAIN", new Vector2(0f, -120f), () => LoadScene("SampleScene"));
        MakeButton("MAIN MENU",  new Vector2(0f, -210f), () => LoadScene("HubScene"));
    }

    static void LoadScene(string sceneName)
    {
        Time.timeScale = 1f; // the match end froze time — never carry that into the next scene
        SceneManager.LoadScene(sceneName);
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

    void MakeButton(string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn" + label);
        go.transform.SetParent(canvasRoot, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(300f, 70f);
        rt.anchoredPosition = pos;

        Image img = go.AddComponent<Image>();
        img.color = ButtonColor;

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        GameObject t = new GameObject("Label");
        t.transform.SetParent(go.transform, false);
        TextMeshProUGUI txt = t.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.fontSize = 28f;
        txt.fontStyle = FontStyles.Bold;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        txt.outlineWidth = 0.2f;
        txt.outlineColor = new Color32(0, 255, 255, 255); // cyan, same style as MainMenuUI
        RectTransform trt = txt.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
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
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }
}
