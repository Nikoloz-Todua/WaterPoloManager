using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Runtime-built mobile touch controls: a virtual joystick (bottom-left) and four
// buttons — SHOOT / PASS / SPRINT / SWITCH — (bottom-right). Everything (canvas,
// sprites, pointer handlers) is created in code; no prefabs, no Inspector wiring.
//
// Each frame the collected state is pushed into the active player via
// PlayerMovement.SetTouchInput(); PlayerMovement merges it into its keyboard checks
// with ||, so keyboard controls keep working unchanged. SWITCH is consumed by
// TeamManager (merged with the C key) through PlayerMovement.TouchSwitchDown.
//
// Visible on mobile builds always; in the Editor only when showInEditor is true.
[DefaultExecutionOrder(-100)] // write touch state before PlayerMovement / TeamManager read it
public class TouchControls : MonoBehaviour
{
    [Header("Visibility")]
    [SerializeField] private bool showInEditor = true;

    [Header("Layout (reference resolution 1920x1080)")]
    [SerializeField] private float joystickSize = 300f;
    [SerializeField] private float knobSize = 120f;
    [SerializeField] private Vector2 joystickPos = new Vector2(230f, 230f);
    [SerializeField] private float buttonSize = 170f;

    [Header("Transparency (labels stay fully opaque)")]
    [SerializeField, Range(0f, 1f)] private float ringAlpha = 0.10f;   // joystick background
    [SerializeField, Range(0f, 1f)] private float knobAlpha = 0.25f;
    [SerializeField, Range(0f, 1f)] private float buttonAlpha = 0.15f;

    private GameObject canvasRoot;
    private TouchJoystick joystick;
    private TouchButton shootBtn, passBtn, sprintBtn, switchBtn;
    private bool prevShoot, prevPass, prevSwitch; // for down/up edge detection

    void Awake()
    {
        BuildUI();
        canvasRoot.SetActive(Application.isMobilePlatform || (Application.isEditor && showInEditor));
    }

    void Update()
    {
        if (canvasRoot == null || !canvasRoot.activeSelf) return;

        bool shootHeld = shootBtn.Pressed;
        bool passHeld = passBtn.Pressed;
        bool shootDown = shootHeld && !prevShoot;
        bool shootUp = !shootHeld && prevShoot;
        bool passDown = passHeld && !prevPass;
        bool passUp = !passHeld && prevPass;
        bool switchDown = switchBtn.Pressed && !prevSwitch;

        PlayerMovement pm = TeamManager.ActivePlayer;
        if (pm != null)
            pm.SetTouchInput(joystick.Axis, shootHeld, shootDown, shootUp,
                             passHeld, passDown, passUp,
                             sprintBtn.Pressed, switchDown);

        prevShoot = shootHeld;
        prevPass = passHeld;
        prevSwitch = switchBtn.Pressed;
    }

    void BuildUI()
    {
        EnsureEventSystem();

        canvasRoot = new GameObject("TouchCanvas");
        canvasRoot.transform.SetParent(transform, false);
        Canvas canvas = canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // above the gameplay HUD
        CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRoot.AddComponent<GraphicRaycaster>();

        Sprite circle = MakeCircleSprite(256);
        Sprite rounded = MakeRoundedRectSprite(128, 28);

        // --- Joystick, anchored bottom-left ---
        RectTransform bg = MakeImage("JoystickBG", canvasRoot.transform, circle,
                                     new Vector2(0f, 0f), joystickPos, joystickSize, ringAlpha);
        RectTransform knob = MakeImage("JoystickKnob", bg, circle,
                                       new Vector2(0.5f, 0.5f), Vector2.zero, knobSize, knobAlpha);
        knob.GetComponent<Image>().raycastTarget = false; // the BG receives all pointer events
        joystick = bg.gameObject.AddComponent<TouchJoystick>();
        joystick.Init(bg, knob, (joystickSize - knobSize) * 0.5f);

        // --- Buttons, anchored bottom-right in a 2x2 grid ---
        shootBtn  = MakeButton("SHOOT",  new Vector2(-150f, 150f), rounded);
        passBtn   = MakeButton("PASS",   new Vector2(-350f, 150f), rounded);
        sprintBtn = MakeButton("SPRINT", new Vector2(-150f, 350f), rounded);
        switchBtn = MakeButton("SWITCH", new Vector2(-350f, 350f), rounded);
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>(); // handles both mouse and multi-touch
    }

    RectTransform MakeImage(string name, Transform parent, Sprite sprite,
                            Vector2 anchor, Vector2 pos, float size, float alpha)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(size, size);
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = new Color(1f, 1f, 1f, alpha);
        return rt;
    }

    TouchButton MakeButton(string label, Vector2 pos, Sprite sprite)
    {
        RectTransform rt = MakeImage("Btn" + label, canvasRoot.transform, sprite,
                                     new Vector2(1f, 0f), pos, buttonSize, buttonAlpha);
        Image img = rt.GetComponent<Image>();
        img.type = Image.Type.Sliced; // 9-slice keeps the corners round at any size

        GameObject t = new GameObject("Label");
        t.transform.SetParent(rt, false);
        RectTransform trt = t.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        TextMeshProUGUI txt = t.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.fontSize = 34f;
        txt.fontStyle = FontStyles.Bold;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = Color.white; // labels stay fully visible
        txt.raycastTarget = false;

        TouchButton b = rt.gameObject.AddComponent<TouchButton>();
        b.Init(img, buttonAlpha);
        return b;
    }

    // Soft antialiased white circle, tinted via Image.color.
    static Sprite MakeCircleSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        float r = size * 0.5f - 1f;
        Vector2 c = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // Rounded white square with a 9-slice border so buttons can be any size.
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
}

// Drag handler for the joystick background: knob follows the finger, clamped to the
// radius; Axis is the normalized offset (magnitude 0..1, analog).
class TouchJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public Vector2 Axis { get; private set; }

    private RectTransform area;
    private RectTransform knob;
    private float radius;

    public void Init(RectTransform area, RectTransform knob, float radius)
    {
        this.area = area;
        this.knob = knob;
        this.radius = Mathf.Max(radius, 1f);
    }

    public void OnPointerDown(PointerEventData e) { Move(e); }
    public void OnDrag(PointerEventData e) { Move(e); }

    public void OnPointerUp(PointerEventData e)
    {
        Axis = Vector2.zero;
        knob.anchoredPosition = Vector2.zero;
    }

    void Move(PointerEventData e)
    {
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, e.position,
                                                                     e.pressEventCamera, out local))
            return;
        Vector2 clamped = Vector2.ClampMagnitude(local, radius);
        knob.anchoredPosition = clamped;
        Axis = clamped / radius;
    }
}

// Press-state holder for one on-screen button; brightens while held.
class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public bool Pressed { get; private set; }

    private Image img;
    private float restAlpha;

    public void Init(Image img, float restAlpha)
    {
        this.img = img;
        this.restAlpha = restAlpha;
    }

    public void OnPointerDown(PointerEventData e)
    {
        Pressed = true;
        SetAlpha(Mathf.Clamp01(restAlpha + 0.3f));
    }

    public void OnPointerUp(PointerEventData e)
    {
        Pressed = false;
        SetAlpha(restAlpha);
    }

    void SetAlpha(float a)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = a;
        img.color = c;
    }
}
