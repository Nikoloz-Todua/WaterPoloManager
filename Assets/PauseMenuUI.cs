using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

// Pause system, built entirely in code (no prefabs, no Inspector wiring): a pause
// button in the top-right corner (sprite from Resources/Sprites/pause-button) that
// freezes the game (Time.timeScale = 0) and opens a centered rounded panel with
// PAUSED + RESUME / RESTART / MAIN MENU. Works with mouse and touch alike — the
// button is always visible on both mobile and desktop, alongside TouchControls.
public class PauseMenuUI : MonoBehaviour
{
    private static readonly Color ButtonColor = new Color(0.05f, 0.1f, 0.25f, 0.85f);
    private static readonly Color PanelColor = new Color(0.02f, 0.05f, 0.15f, 0.92f);

    private Transform canvasRoot;
    private GameObject pauseButton;
    private GameObject panel;
    private GameObject confirmPanel; // "quit = loss" confirmation, covers the pause panel

    void Awake()
    {
        EnsureEventSystem();
        BuildUI();
        panel.SetActive(false);
    }

    void Pause()
    {
        // After the final whistle the result screen owns the frozen state — don't
        // let a pause click fight it (RESUME would un-freeze a finished match).
        if (MatchTimer.Instance != null && MatchTimer.Instance.MatchOver) return;

        Time.timeScale = 0f;
        confirmPanel.SetActive(false); // always open on the main pause panel, not a stale confirm
        panel.SetActive(true);
        pauseButton.SetActive(false);
    }

    void Resume()
    {
        Time.timeScale = 1f;
        confirmPanel.SetActive(false);
        panel.SetActive(false);
        pauseButton.SetActive(true);
    }

    static void LoadScene(string sceneName)
    {
        Time.timeScale = 1f; // never carry a frozen timescale into the next scene
        SceneManager.LoadScene(sceneName);
    }

    void BuildUI()
    {
        GameObject canvasGo = new GameObject("PauseCanvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110; // above HUD + touch controls, below the result screen
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        canvasRoot = canvasGo.transform;

        // --- Pause button, top-right with a 20px margin ---
        pauseButton = new GameObject("PauseButton");
        pauseButton.transform.SetParent(canvasRoot, false);
        RectTransform pRt = pauseButton.AddComponent<RectTransform>();
        pRt.anchorMin = pRt.anchorMax = new Vector2(1f, 1f);
        pRt.pivot = new Vector2(1f, 1f);
        pRt.anchoredPosition = new Vector2(-180f, 15f); // pulled down to clear the scoreboard
        pRt.sizeDelta = new Vector2(70f, 70f);
        Image pImg = pauseButton.AddComponent<Image>();
        pImg.sprite = Resources.Load<Sprite>("Sprites/pause-button");
        if (pImg.sprite == null)
        {
            // fallback so the button still exists if the sprite goes missing
            pImg.color = ButtonColor;
            Debug.LogWarning("PauseMenuUI: Sprites/pause-button not found in a Resources folder.");
        }
        Button pBtn = pauseButton.AddComponent<Button>();
        pBtn.targetGraphic = pImg;
        pBtn.onClick.AddListener(Pause);

        // --- Pause panel (hidden until paused) ---
        // Full-screen dim layer behind the panel; also blocks clicks into the game.
        panel = new GameObject("PausePanel");
        panel.transform.SetParent(canvasRoot, false);
        RectTransform dimRt = panel.AddComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;
        Image dim = panel.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.5f);

        // Centered rounded box, 400x350.
        GameObject box = new GameObject("Box");
        box.transform.SetParent(panel.transform, false);
        RectTransform boxRt = box.AddComponent<RectTransform>();
        boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        boxRt.sizeDelta = new Vector2(400f, 350f);
        Image boxImg = box.AddComponent<Image>();
        boxImg.sprite = MakeRoundedRectSprite(128, 24);
        boxImg.type = Image.Type.Sliced;
        boxImg.color = PanelColor;

        MakeText(box.transform, "PAUSED", 36f, new Vector2(0f, 125f), new Vector2(360f, 50f));

        MakeButton(box.transform, "RESUME", new Vector2(0f, 45f), Resume);
        MakeButton(box.transform, "QUIT", new Vector2(0f, -30f), () => confirmPanel.SetActive(true));
        // Placeholder — no functionality yet; just makes sure no stale confirm is showing.
        MakeButton(box.transform, "TEAM MANAGEMENT", new Vector2(0f, -105f),
                   () => confirmPanel.SetActive(false));

        // --- Quit confirmation, covers the whole pause box until answered ---
        confirmPanel = new GameObject("QuitConfirm");
        confirmPanel.transform.SetParent(box.transform, false);
        RectTransform cRt = confirmPanel.AddComponent<RectTransform>();
        cRt.anchorMin = Vector2.zero;
        cRt.anchorMax = Vector2.one;
        cRt.offsetMin = cRt.offsetMax = Vector2.zero;
        Image cImg = confirmPanel.AddComponent<Image>();
        cImg.sprite = boxImg.sprite;
        cImg.type = Image.Type.Sliced;
        cImg.color = new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.98f); // near-opaque: hides the buttons underneath

        MakeText(confirmPanel.transform, "If you quit, this match\ncounts as a loss.", 26f,
                 new Vector2(0f, 80f), new Vector2(360f, 100f));
        MakeButton(confirmPanel.transform, "YES QUIT", new Vector2(0f, -30f), () => LoadScene("MainMenu"));
        MakeButton(confirmPanel.transform, "CANCEL", new Vector2(0f, -105f),
                   () => confirmPanel.SetActive(false));
        confirmPanel.SetActive(false);
    }

    TextMeshProUGUI MakeText(Transform parent, string content, float size, Vector2 pos, Vector2 box)
    {
        GameObject go = new GameObject("Text" + content);
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
        rt.sizeDelta = box;
        rt.anchoredPosition = pos;
        return txt;
    }

    void MakeButton(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn" + label);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(300f, 60f);
        rt.anchoredPosition = pos;

        Image img = go.AddComponent<Image>();
        img.color = ButtonColor;

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        TextMeshProUGUI txt = MakeText(go.transform, label, 26f, Vector2.zero, Vector2.zero);
        txt.outlineWidth = 0.2f;
        txt.outlineColor = new Color32(0, 255, 255, 255); // cyan, MainMenuUI style
        RectTransform trt = txt.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
    }

    // Rounded white square with a 9-slice border (same generator as TouchControls).
    static Sprite MakeRoundedRectSprite(int size, int corner)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        float half = size * 0.5f - 0.5f;
        float inner = half - corner;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Max(Mathf.Abs(x - half) - inner, 0f);
                float qy = Mathf.Max(Mathf.Abs(y - half) - inner, 0f);
                float d = Mathf.Sqrt(qx * qx + qy * qy);
                byte a = (byte)(Mathf.Clamp01(corner - d) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        Vector4 border = new Vector4(corner + 2, corner + 2, corner + 2, corner + 2);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                             100f, 0, SpriteMeshType.FullRect, border);
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }
}
