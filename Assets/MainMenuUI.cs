using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

// Builds the entire main menu in code at runtime — canvas, background,
// LOGIN / PLAY buttons, hover scaling, and a 1-second
// fade-in. No prefabs, no Inspector wiring: drop it on an empty GameObject in
// the MainMenu scene. The background loads from Assets/Resources/Sprites/background.png.
public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private float fadeSeconds = 1f;
    [SerializeField] private Sprite[] spinnerFrames;

    private static readonly Color PlayColor = new Color(0.01f, 0.62f, 0.91f, 0.96f);
    private static readonly Color LoginColor = new Color(0.025f, 0.10f, 0.19f, 0.94f);

    private CanvasGroup fadeGroup;
    private Transform canvasRoot;

    void Start()
    {
        LoadingOverlayUI.ConfigureSpinner(spinnerFrames);
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

        // The new background already contains the logo. Keep the foreground intentionally clean.
        MakeButton("PLAY", new Vector2(0f, -85f), true,
                   () => LoadingOverlayUI.LoadScene("HubScene", true, "ENTERING THE POOL..."));
        MakeButton("LOGIN", new Vector2(0f, -170f), false, () => { });
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

    void MakeButton(string label, Vector2 pos, bool primary, UnityEngine.Events.UnityAction onClick)
    {
        GameObject shadow = new GameObject("Shadow" + label);
        shadow.transform.SetParent(canvasRoot, false);
        RectTransform shadowRt = shadow.AddComponent<RectTransform>();
        shadowRt.anchorMin = shadowRt.anchorMax = new Vector2(0.5f, 0.5f);
        shadowRt.sizeDelta = new Vector2(344f, 72f);
        shadowRt.anchoredPosition = pos + new Vector2(0f, -7f);
        Image shadowImage = shadow.AddComponent<Image>();
        shadowImage.sprite = LoadingOverlayUI.RoundedSprite();
        shadowImage.type = Image.Type.Sliced;
        shadowImage.color = new Color(0f, 0f, 0f, 0.42f);
        shadowImage.raycastTarget = false;

        GameObject go = new GameObject("Btn" + label);
        go.transform.SetParent(canvasRoot, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(344f, 72f);
        rt.anchoredPosition = pos;

        Image img = go.AddComponent<Image>();
        img.sprite = LoadingOverlayUI.RoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = primary ? PlayColor : LoginColor;
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = primary ? new Color(0.45f, 0.96f, 1f, 0.9f)
                                      : new Color(0.12f, 0.72f, 0.92f, 0.75f);
        outline.effectDistance = new Vector2(2f, -2f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        // Crisp, widely tracked white type with a restrained aqua glow.
        TextMeshProUGUI txt = MakeText("Label", label, 28f, FontStyles.Bold);
        txt.transform.SetParent(go.transform, false);
        RectTransform trt = txt.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        txt.characterSpacing = 4f;
        txt.outlineWidth = 0.12f;
        txt.outlineColor = new Color32(0, 80, 110, 230);

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
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>(); // mouse + touch input for the buttons
    }
}
