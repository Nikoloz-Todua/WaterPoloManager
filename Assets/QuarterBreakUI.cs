using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

// Between-quarters pause screen (Feature 2), built entirely in code (no prefabs / no Inspector
// wiring). Shown by MatchTimer when a quarter ends (but the match is NOT over): a dark
// semi-transparent overlay + a centred panel with "QUARTER N COMPLETE", the score, and two
// buttons — RESUME (starts the next quarter's sprint duel) and QUIT (leaves to the main menu /
// stops play). Self-bootstrapping via Get() so nothing has to be placed in the scene.
public class QuarterBreakUI : MonoBehaviour
{
    public static QuarterBreakUI Instance { get; private set; }

    private static readonly Color PanelColor  = new Color(0.05f, 0.07f, 0.16f, 0.96f);
    private static readonly Color ButtonColor = new Color(0.05f, 0.1f, 0.25f, 0.95f);

    private GameObject root;     // whole overlay canvas — inactive until Show()
    private CanvasGroup group;
    private Transform canvasRoot;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI scoreText;
    private Action onResume;

    // Lazily create (or fetch) the singleton, so MatchTimer can call it with zero scene setup.
    public static QuarterBreakUI Get()
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("QuarterBreakUI");
            Instance = go.AddComponent<QuarterBreakUI>(); // Awake runs now → builds the UI
        }
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
        root.SetActive(false);
    }

    // completedQuarter = the quarter that just finished (1-based). resume = called on RESUME.
    public void Show(int completedQuarter, int you, int bot, Action resume)
    {
        onResume = resume;
        titleText.text = "QUARTER " + completedQuarter + " COMPLETE";
        scoreText.text = "YOU  " + you + " — " + bot + "  BOT";

        root.SetActive(true);
        StopAllCoroutines();
        StartCoroutine(FadeIn());
    }

    void Hide()
    {
        StopAllCoroutines();
        root.SetActive(false);
    }

    void OnResumeClicked()
    {
        Action r = onResume;
        onResume = null;
        Hide();
        r?.Invoke(); // MatchTimer advances to the next quarter (which restarts the touch UI via the duel)
    }

    void OnQuitClicked()
    {
        Time.timeScale = 1f;
        // Prefer a clean return to the hub if that scene exists in the build; otherwise quit.
        if (Application.CanStreamedLevelBeLoaded("HubScene"))
        {
            SceneManager.LoadScene("HubScene");
            return;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void BuildUI()
    {
        EnsureEventSystem();

        root = new GameObject("QuarterBreakCanvas");
        root.transform.SetParent(transform, false);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 125; // above HUD, touch controls and the duel overlay (110)
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        group = root.AddComponent<CanvasGroup>();
        canvasRoot = root.transform;

        // dimmed full-screen overlay (also blocks clicks reaching the game / pause button)
        GameObject dimGo = new GameObject("Dim");
        dimGo.transform.SetParent(canvasRoot, false);
        Image dim = dimGo.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        RectTransform dRt = dim.rectTransform;
        dRt.anchorMin = Vector2.zero; dRt.anchorMax = Vector2.one;
        dRt.offsetMin = dRt.offsetMax = Vector2.zero;

        // centred dark panel
        GameObject panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasRoot, false);
        Image panel = panelGo.AddComponent<Image>();
        panel.sprite = MakeRoundedRectSprite(420, 360, 28);
        panel.type = Image.Type.Simple;
        panel.color = PanelColor;
        RectTransform pRt = panel.rectTransform;
        pRt.anchorMin = pRt.anchorMax = new Vector2(0.5f, 0.5f);
        pRt.pivot = new Vector2(0.5f, 0.5f);
        pRt.anchoredPosition = Vector2.zero;
        pRt.sizeDelta = new Vector2(420f, 360f);

        titleText = MakeText(panelGo.transform, "Title", "QUARTER 1 COMPLETE", 36f, new Vector2(0f, 115f));
        scoreText = MakeText(panelGo.transform, "Score", "YOU  0 — 0  BOT",    50f, new Vector2(0f, 35f));

        MakeButton(panelGo.transform, "RESUME", new Vector2(0f, -55f),  OnResumeClicked);
        MakeButton(panelGo.transform, "QUIT",   new Vector2(0f, -140f), OnQuitClicked);
    }

    TextMeshProUGUI MakeText(Transform parent, string name, string content, float size, Vector2 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = content;
        txt.fontSize = size;
        txt.fontStyle = FontStyles.Bold;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        RectTransform rt = txt.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(400f, 80f);
        rt.anchoredPosition = pos;
        return txt;
    }

    void MakeButton(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn" + label);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(280f, 66f);
        rt.anchoredPosition = pos;

        Image img = go.AddComponent<Image>();
        img.sprite = MakeRoundedRectSprite(280, 66, 20);
        img.type = Image.Type.Simple;
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
        txt.outlineColor = new Color32(0, 255, 255, 255); // cyan, same style as MainMenuUI / result screen
        RectTransform trt = txt.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
    }

    IEnumerator FadeIn()
    {
        const float fade = 0.3f;
        group.alpha = 0f;
        float t = 0f;
        while (t < fade)
        {
            t += Time.unscaledDeltaTime; // robust even if something paused timeScale
            group.alpha = Mathf.Clamp01(t / fade);
            yield return null;
        }
        group.alpha = 1f;
    }

    static Sprite MakeRoundedRectSprite(int w, int h, int radius)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[w * h];
        float r = Mathf.Clamp(radius, 0, Mathf.Min(w, h) / 2);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float cx = Mathf.Clamp(x, r, w - 1 - r);
                float cy = Mathf.Clamp(y, r, h - 1 - r);
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                float a = d <= r - 1f ? 1f : Mathf.Clamp01(r - d);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }
}
