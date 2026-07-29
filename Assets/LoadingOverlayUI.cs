using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Shared loading surface. The menu scenes supply the 12 authored spinner frames from
// Assets/Sprites/Spinner/spinner.png so every delayed action uses the same animation.
public sealed class LoadingOverlayUI : MonoBehaviour
{
    static LoadingOverlayUI instance;
    static Sprite[] configuredSpinnerFrames;
    static Sprite roundedSprite;

    CanvasGroup group;
    Image spinner;
    Image waterFill;
    RectTransform shimmer;
    TextMeshProUGUI messageLabel;
    TextMeshProUGUI percentageLabel;
    Sprite[] spinnerFrames;
    float frameClock;
    int frameIndex;
    int showDepth;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        configuredSpinnerFrames = null;
        roundedSprite = null;
    }

    public static void ConfigureSpinner(Sprite[] frames)
    {
        if (frames == null || frames.Length == 0) return;
        configuredSpinnerFrames = frames;
        if (instance != null) instance.spinnerFrames = frames;
    }

    public static Sprite RoundedSprite() => Rounded();

    public static void ShowSpinner(string message = "LOADING...")
    {
        LoadingOverlayUI ui = Ensure();
        ui.showDepth++;
        ui.SetVisible(true, false, message);
    }

    public static void HideSpinner()
    {
        if (instance == null) return;
        instance.showDepth = Mathf.Max(0, instance.showDepth - 1);
        if (instance.showDepth == 0) instance.SetVisible(false, false, "");
    }

    public static void LoadScene(string sceneName, bool showWaterProgress = false,
                                 string message = "LOADING...")
    {
        LoadingOverlayUI ui = Ensure();
        ui.StartCoroutine(ui.LoadSceneRoutine(sceneName, showWaterProgress, message));
    }

    static LoadingOverlayUI Ensure()
    {
        if (instance != null) return instance;
        GameObject go = new GameObject("GlobalLoadingOverlay");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<LoadingOverlayUI>();
        instance.spinnerFrames = configuredSpinnerFrames;
        instance.Build();
        return instance;
    }

    void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();

        Image scrim = MakeImage(transform, "Scrim", new Color(0.008f, 0.035f, 0.075f, 0.9f));
        Stretch(scrim.rectTransform);

        Image glow = MakeImage(transform, "PanelGlow", new Color(0f, 0.72f, 1f, 0.18f));
        glow.sprite = Rounded();
        glow.type = Image.Type.Sliced;
        SetRect(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(570f, 286f));

        Image panel = MakeImage(transform, "Panel", new Color(0.025f, 0.09f, 0.16f, 0.97f));
        panel.sprite = Rounded();
        panel.type = Image.Type.Sliced;
        SetRect(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(548f, 264f));

        spinner = MakeImage(panel.transform, "Spinner", Color.white);
        spinner.preserveAspect = true;
        spinner.raycastTarget = false;
        SetRect(spinner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 47f), new Vector2(92f, 92f));

        messageLabel = MakeText(panel.transform, "Message", "LOADING...", 22f);
        SetRect(messageLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -17f), new Vector2(480f, 34f));
        messageLabel.characterSpacing = 3f;

        percentageLabel = MakeText(panel.transform, "Percentage", "0%", 26f);
        SetRect(percentageLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 25f), new Vector2(200f, 38f));
        percentageLabel.color = new Color(0.55f, 0.95f, 1f);

        Image trough = MakeImage(panel.transform, "WaterBar", new Color(0.005f, 0.025f, 0.055f, 0.95f));
        trough.sprite = Rounded();
        trough.type = Image.Type.Sliced;
        SetRect(trough.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -75f), new Vector2(454f, 42f));
        Mask mask = trough.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        waterFill = MakeImage(trough.transform, "WaterFill", new Color(0.02f, 0.72f, 0.98f, 1f));
        waterFill.sprite = Rounded();
        waterFill.type = Image.Type.Filled;
        waterFill.fillMethod = Image.FillMethod.Horizontal;
        waterFill.fillOrigin = 0;
        waterFill.fillAmount = 0f;
        Stretch(waterFill.rectTransform, 4f);

        Image shine = MakeImage(waterFill.transform, "SurfaceShine", new Color(0.7f, 1f, 1f, 0.38f));
        shine.raycastTarget = false;
        RectTransform shineRt = shine.rectTransform;
        shineRt.anchorMin = new Vector2(0f, 0.62f);
        shineRt.anchorMax = new Vector2(1f, 0.88f);
        shineRt.offsetMin = shineRt.offsetMax = Vector2.zero;

        Image movingShimmer = MakeImage(waterFill.transform, "MovingShimmer", new Color(0.8f, 1f, 1f, 0.24f));
        movingShimmer.raycastTarget = false;
        shimmer = movingShimmer.rectTransform;
        shimmer.anchorMin = shimmer.anchorMax = new Vector2(0f, 0.5f);
        shimmer.sizeDelta = new Vector2(90f, 70f);

        SetVisible(false, false, "");
    }

    IEnumerator LoadSceneRoutine(string sceneName, bool waterProgress, string message)
    {
        showDepth = 1;
        SetVisible(true, waterProgress, message);
        yield return null; // ensure feedback renders before scene work starts

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        if (operation == null)
        {
            Debug.LogError("LoadingOverlayUI: scene could not be loaded: " + sceneName);
            showDepth = 0;
            SetVisible(false, false, "");
            yield break;
        }

        float displayed = 0f;
        while (!operation.isDone)
        {
            float actual = Mathf.Clamp01(operation.progress / 0.9f);
            displayed = Mathf.MoveTowards(displayed, actual, Time.unscaledDeltaTime * 1.2f);
            SetProgress(displayed);
            yield return null;
        }

        SetProgress(1f);
        showDepth = 0;
        SetVisible(false, false, "");
    }

    void SetVisible(bool visible, bool waterProgress, string message)
    {
        if (group == null) return;
        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
        if (messageLabel != null) messageLabel.text = string.IsNullOrWhiteSpace(message) ? "LOADING..." : message;
        if (percentageLabel != null) percentageLabel.gameObject.SetActive(visible && waterProgress);
        if (waterFill != null) waterFill.transform.parent.gameObject.SetActive(visible && waterProgress);
        if (spinner != null) spinner.gameObject.SetActive(visible && !waterProgress);
        if (visible && !waterProgress && spinner != null && spinnerFrames != null && spinnerFrames.Length > 0)
        {
            frameIndex %= spinnerFrames.Length;
            spinner.sprite = spinnerFrames[frameIndex];
        }
        if (visible && waterProgress) SetProgress(0f);
    }

    void SetProgress(float value)
    {
        value = Mathf.Clamp01(value);
        if (waterFill != null) waterFill.fillAmount = value;
        if (percentageLabel != null) percentageLabel.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    void Update()
    {
        if (group == null || group.alpha <= 0f) return;
        if (spinner != null && spinner.gameObject.activeSelf && spinnerFrames != null && spinnerFrames.Length > 0)
        {
            frameClock += Time.unscaledDeltaTime;
            if (frameClock >= 1f / 18f)
            {
                frameClock %= 1f / 18f;
                frameIndex = (frameIndex + 1) % spinnerFrames.Length;
                spinner.sprite = spinnerFrames[frameIndex];
            }
        }
        if (shimmer != null)
        {
            float x = Mathf.Repeat(Time.unscaledTime * 145f, 560f) - 90f;
            shimmer.anchoredPosition = new Vector2(x, Mathf.Sin(Time.unscaledTime * 5f) * 4f);
        }
    }

    static Image MakeImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    static TextMeshProUGUI MakeText(Transform parent, string name, string value, float size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    static Sprite Rounded()
    {
        if (roundedSprite != null) return roundedSprite;
        const int size = 32;
        const float radius = 9f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "LoadingRoundedRect";
        texture.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
            float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
            pixels[y * size + x] = dx * dx + dy * dy <= radius * radius
                ? new Color32(255, 255, 255, 255)
                : new Color32(255, 255, 255, 0);
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        roundedSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                      100f, 0, SpriteMeshType.FullRect,
                                      new Vector4(radius, radius, radius, radius));
        roundedSprite.name = "LoadingRoundedRect";
        return roundedSprite;
    }
}
