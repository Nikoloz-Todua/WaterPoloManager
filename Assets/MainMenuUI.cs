using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

// Builds the entire main menu in code at runtime — canvas, background, logo,
// PLAY / SETTINGS / QUIT buttons, version label, hover scaling, and a 1-second
// fade-in. No prefabs, no Inspector wiring: drop it on an empty GameObject in
// the MainMenu scene. Sprites load from Assets/Resources/Sprites/ via
// Resources.Load<Sprite> ("Sprites/background", "Sprites/logo").
public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private float fadeSeconds = 1f;

    private static readonly Color ButtonColor = new Color(0.05f, 0.1f, 0.25f, 0.85f);

    private CanvasGroup fadeGroup;
    private Transform canvasRoot;

    void Start()
    {
        EnsureEventSystem();
        BuildMenu();
        StartCoroutine(FadeIn());
    }

    void BuildMenu()
    {
        // --- Canvas ---
        GameObject canvasGo = new GameObject("MenuCanvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        fadeGroup = canvasGo.AddComponent<CanvasGroup>();
        fadeGroup.alpha = 0f; // fade-in starts fully transparent
        canvasRoot = canvasGo.transform;

        // --- Full-screen background ---
        Image bg = MakeImage("Background", Resources.Load<Sprite>("Sprites/background"));
        RectTransform bgRt = bg.rectTransform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
        if (bg.sprite == null)
        {
            bg.color = new Color(0.02f, 0.15f, 0.3f); // pool blue fallback
            Debug.LogWarning("MainMenuUI: Sprites/background not found in a Resources folder.");
        }

        // --- Logo, centered top ---
        Image logo = MakeImage("Logo", Resources.Load<Sprite>("Sprites/logo"));
        RectTransform logoRt = logo.rectTransform;
        logoRt.anchorMin = logoRt.anchorMax = new Vector2(0.5f, 0.5f);
        logoRt.sizeDelta = new Vector2(400f, 400f);
        logoRt.anchoredPosition = new Vector2(0f, 220f);
        logo.preserveAspect = true;
        if (logo.sprite == null)
        {
            logo.enabled = false;
            Debug.LogWarning("MainMenuUI: Sprites/logo not found in a Resources folder.");
        }

        // --- Buttons, vertically centered ---
        MakeButton("PLAY", new Vector2(0f, 90f), () => SceneManager.LoadScene("HubScene"));
        MakeButton("SETTINGS", new Vector2(0f, 0f), () => Debug.Log("Settings coming soon"));
        MakeButton("QUIT", new Vector2(0f, -90f), Application.Quit);

        // --- Version label, bottom center ---
        TextMeshProUGUI version = MakeText("VersionText", "Water Polo Manager v0.1", 16f, FontStyles.Normal);
        RectTransform vRt = version.rectTransform;
        vRt.anchorMin = vRt.anchorMax = new Vector2(0.5f, 0f);
        vRt.sizeDelta = new Vector2(400f, 30f);
        vRt.anchoredPosition = new Vector2(0f, 25f);
    }

    Image MakeImage(string name, Sprite sprite)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(canvasRoot, false);
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    TextMeshProUGUI MakeText(string name, string content, float size, FontStyles style)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(canvasRoot, false);
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = content;
        txt.fontSize = size;
        txt.fontStyle = style;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
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

        // Label: white bold TMP with a cyan outline.
        TextMeshProUGUI txt = MakeText("Label", label, 28f, FontStyles.Bold);
        txt.transform.SetParent(go.transform, false);
        RectTransform trt = txt.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        txt.outlineWidth = 0.2f;
        txt.outlineColor = new Color32(0, 255, 255, 255);

        // Hover effect: grow to 1.05x on pointer enter, back to 1x on exit.
        EventTrigger trigger = go.AddComponent<EventTrigger>();
        AddTriggerEntry(trigger, EventTriggerType.PointerEnter,
                        () => go.transform.localScale = Vector3.one * 1.05f);
        AddTriggerEntry(trigger, EventTriggerType.PointerExit,
                        () => go.transform.localScale = Vector3.one);
    }

    static void AddTriggerEntry(EventTrigger trigger, EventTriggerType type, System.Action action)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    IEnumerator FadeIn()
    {
        float t = 0f;
        while (t < fadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            fadeGroup.alpha = Mathf.Clamp01(t / fadeSeconds);
            yield return null;
        }
        fadeGroup.alpha = 1f;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>(); // mouse + touch input for the buttons
    }
}
