using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Runtime-built mobile touch controls: a virtual joystick (bottom-left, UNCHANGED) plus
// THREE circular image buttons (bottom-right) that swap icon + behaviour with possession,
// and a single PASS OUT button shown only while the player's own goalkeeper holds the ball.
//
//   ATTACK  (we hold the ball, or it's loose):  Sprint (top) / Shoot (bottom-right) / Pass (bottom-left)
//   DEFENSE (the enemy holds the ball):          Switch (top) / Defend (bottom-right) / Block (bottom-left)
//
// Mode is read every frame from MatchContext.Instance.PossessingTeam:
//   PossessingTeam == PlayerTeam  OR  null  -> ATTACK
//   PossessingTeam == BotTeam               -> DEFENSE
//
// Behaviours route through existing systems (keyboard keeps working via || merges):
//   Sprint  -> SetTouchInput sprintHeld     Shoot -> SetTouchInput shoot*      Pass -> SetTouchInput pass*
//   Switch  -> SetTouchInput switchDown (TeamManager)                          Block -> PlayerMovement.TouchBlockSteal()
//   Defend  -> feeds a chase-the-enemy-carrier movement axis (proximity defend animation fires on its own)
//   PassOut -> Goalkeeper.RequestPassOut() on the player's holding keeper (same as keyboard B)
//
// Visible on mobile builds always; in the Editor only when showInEditor is true.
[DefaultExecutionOrder(-100)] // write touch state before PlayerMovement / TeamManager read it
public class TouchControls : MonoBehaviour
{
    [Header("Visibility")]
    [SerializeField] private bool showInEditor = true;

    [Header("Joystick (reference resolution 1920x1080) — unchanged")]
    [SerializeField] private float joystickSize = 300f;
    [SerializeField] private float knobSize = 120f;
    [SerializeField] private Vector2 joystickPos = new Vector2(230f, 230f);
    [SerializeField, Range(0f, 1f)] private float ringAlpha = 0.10f;   // joystick background
    [SerializeField, Range(0f, 1f)] private float knobAlpha = 0.25f;

    [Header("Action buttons")]
    [Tooltip("Size of the two lower buttons (shoot/defend and pass/block).")]
    [SerializeField] private float actionButtonSize = 270f; // 1.5x larger
    [Tooltip("Size of the top button (sprint/switch).")]
    [SerializeField] private float mainButtonSize = 270f;   // 1.5x larger
    [Tooltip("Opacity of the button icons (1 = fully opaque).")]
    [SerializeField, Range(0f, 1f)] private float iconAlpha = 1.0f;

    // Anchored from the bottom-right corner (anchor 1,0): X is negative (left of the right
    // edge), Y is positive (above the bottom edge). A TIGHT triangle (~25px gaps between the
    // 270px buttons) so the cluster reads as one control pad low in the bottom-right corner.
    static readonly Vector2 TopRightPos    = new Vector2(-362f, 415f); // sprint / switch
    static readonly Vector2 BottomRightPos = new Vector2(-215f, 160f); // shoot / defend
    static readonly Vector2 BottomLeftPos  = new Vector2(-510f, 160f); // pass / block

    // PASS OUT button: centre-right (anchor 1,0.5), with its label just below it.
    static readonly Vector2 PassOutPos      = new Vector2(-330f, 60f);
    static readonly Vector2 PassOutLabelPos = new Vector2(-330f, -112.5f);
    const float PassOutSize = 270f; // 1.5x larger

    const float ModeFadeTime = 0.22f; // smooth fade-out / fade-in on an attack<->defense swap

    // Singleton so the Goalkeeper can read the live joystick aim for its directional pass/shot.
    public static TouchControls Instance { get; private set; }

    // Live analog joystick vector (magnitude 0..1). Zero when the canvas is inactive / untouched.
    public Vector2 JoystickAxis => joystick != null ? joystick.Axis : Vector2.zero;

    private GameObject canvasRoot;
    private TouchJoystick joystick;

    private GameObject actionGroup;   // holds the 3 mode buttons (hidden during keeper pass-out)
    private GameObject passOutGroup;  // holds the single PASS OUT button + label

    // top, bottom-right, bottom-left buttons + their images (for icon swap / fade)
    private TouchButton topBtn, brBtn, blBtn, passOutBtn;
    private Image topImg, brImg, blImg;

    // icons (loaded once from Resources)
    private Sprite sprSprint, sprShoot, sprPass, sprDefend, sprSwitch, sprBlock;

    private bool attackMode = true;
    private bool modeInitialized;
    private Coroutine modeFade;
    private bool prevTop, prevBR, prevBL; // tap (down/up) edge detection

    // --- Stamina HUD: a panel above the joystick — player number (or "GK" when the human controls
    //     the keeper) + a fill bar. It reads PlayerMovement.StaminaPercent01 /
    //     Goalkeeper.StaminaPercent01 + TeamManager.ActivePlayerIndex, so it carries NO reference to
    //     the StaminaSystem type — remove that system and this HUD simply shows a full bar. ---
    private TMP_Text staminaLabel;
    private Image staminaFill;
    private RectTransform staminaFillRt;
    private float shownStamina = 1f;
    private string shownLabel;
    static readonly Vector2 StaminaPanelPos = new Vector2(230f, 425f); // above the joystick (bottom-left)
    static readonly Vector2 StaminaPanelSize = new Vector2(220f, 55f);
    const float StaminaBarMaxWidth = 144f;   // inner fill width inside the track

    void Awake()
    {
        Instance = this;
        BuildUI();
        canvasRoot.SetActive(Application.isMobilePlatform || (Application.isEditor && showInEditor));
    }

    void Update()
    {
        if (canvasRoot == null || !canvasRoot.activeSelf) return;
        MatchContext ctx = MatchContext.Instance;

        // --- KEEPER CONTROL (Task 5): while the player's OWN keeper holds the ball the human
        //     controls the KEEPER itself with the SAME 3 attack buttons + joystick (routed to
        //     the Goalkeeper), instead of swapping to a single PASS OUT button. The keeper
        //     possesses the ball, so the mode logic below already shows the attack icons. ---
        Goalkeeper keeper = null;
        if (ctx != null && ctx.KeeperHolding && ctx.KeeperHoldTeam == ctx.PlayerTeam && ctx.Ball != null)
        {
            Transform held = ctx.Ball.transform.parent;
            if (held != null) keeper = held.GetComponent<Goalkeeper>();
        }
        bool keeperControl = keeper != null;

        // HUD panel: show the keeper's stamina while the human controls the keeper, else the
        // active field player's (independent of attack/defense mode).
        UpdateStaminaHud(keeperControl ? keeper : null);

        // PASS OUT button is retired — the keeper uses the full action buttons now.
        if (passOutGroup != null && passOutGroup.activeSelf) passOutGroup.SetActive(false);
        if (actionGroup != null && !actionGroup.activeSelf) actionGroup.SetActive(true);

        // --- decide mode from possession ---
        bool attack = true;
        if (ctx != null && ctx.PossessingTeam == ctx.BotTeam) // enemy has it -> defense
            attack = false;                                   // PlayerTeam or loose (null) -> attack

        if (!modeInitialized)
        {
            attackMode = attack;
            ApplySprites(attackMode);
            SetButtonsAlpha(iconAlpha);
            modeInitialized = true;
        }
        else if (attack != attackMode)
        {
            attackMode = attack;
            if (modeFade != null) StopCoroutine(modeFade);
            modeFade = StartCoroutine(ModeTransition(attackMode));
        }

        // --- read buttons + edges ---
        bool topHeld = topBtn.Pressed;
        bool brHeld  = brBtn.Pressed;
        bool blHeld  = blBtn.Pressed;
        bool topDown = topHeld && !prevTop; // switch tap (defense)
        bool brDown  = brHeld && !prevBR;
        bool brUp    = !brHeld && prevBR;
        bool blDown  = blHeld && !prevBL;
        bool blUp    = !blHeld && prevBL;

        PlayerMovement pm = TeamManager.ActivePlayer;
        Vector2 axis = joystick.Axis;

        if (keeperControl)
        {
            // Route the 3 attack buttons + joystick to the KEEPER (Task 5):
            //   top = Sprint (hold), bottom-right = Shoot (tap/hold/release), bottom-left = Pass (tap).
            keeper.SetTouchInput(axis, brHeld, brDown, brUp, blHeld, blDown, blUp, topHeld);

            // the field player is stood down (PlayerMovement yields to the keeper) — make sure
            // touch input never drives it either.
            if (pm != null)
                pm.SetTouchInput(Vector2.zero, false, false, false, false, false, false, false, false);
        }
        else if (pm != null)
        {
            if (attackMode)
            {
                // top = Sprint (hold), bottom-right = Shoot (tap/hold/release), bottom-left = Pass (tap)
                pm.SetTouchInput(axis,
                    brHeld, brDown, brUp,   // shoot
                    blHeld, blDown, blUp,   // pass
                    topHeld,                // sprint
                    false);                 // switch (attack: unused)
            }
            else
            {
                // DEFENSE: top = Switch (tap), bottom-right = Defend press (hold), bottom-left = Block (tap).
                // Defend press chases the enemy carrier (or nearest enemy) unless the joystick steers.
                if (brHeld && axis.sqrMagnitude < 0.04f)
                {
                    Vector2 dir = DirToDefendTarget(pm);
                    if (dir != Vector2.zero) axis = dir;
                }

                pm.SetTouchInput(axis,
                    false, false, false,    // shoot off
                    false, false, false,    // pass off
                    false,                  // sprint off
                    topDown);               // top = Switch (tap)

                if (blDown) pm.TouchBlockSteal(); // bottom-left = Block
            }
        }

        prevTop = topHeld; prevBR = brHeld; prevBL = blHeld;
    }

    // Defend-press target: the enemy ball carrier if they have it (matches the proximity
    // defend trigger in PlayerAnimator), otherwise the nearest enemy swimmer.
    static Vector2 DirToDefendTarget(PlayerMovement pm)
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null) return Vector2.zero;
        TeamSide enemy = ctx.EnemyOf(ctx.PlayerTeam);
        if (enemy == null) return Vector2.zero;

        Transform target = null;
        if (ctx.TeamHasBall(enemy) && ctx.Ball != null)
        {
            Transform carrier = ctx.Ball.transform.parent; // the held ball is parented to the carrier
            if (carrier != null && carrier.GetComponent<Goalkeeper>() == null) target = carrier;
        }
        if (target == null) target = enemy.ClosestMemberTo(pm.transform.position);
        if (target == null) return Vector2.zero;

        Vector2 d = (Vector2)target.position - (Vector2)pm.transform.position;
        return d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.zero;
    }

    // ---- mode visuals ----

    void ApplySprites(bool attack)
    {
        // top: Sprint (attack) / Switch (defense)
        // br:  Shoot  (attack) / Defend (defense)
        // bl:  Pass   (attack) / Block  (defense)
        if (topImg != null) topImg.sprite = attack ? sprSprint : sprSwitch;
        if (brImg  != null) brImg.sprite  = attack ? sprShoot  : sprDefend;
        if (blImg  != null) blImg.sprite  = attack ? sprPass   : sprBlock;
    }

    // Fade the three icons out, swap to the new mode's sprites, fade back in.
    IEnumerator ModeTransition(bool toAttack)
    {
        float t = 0f;
        while (t < ModeFadeTime)
        {
            t += Time.unscaledDeltaTime;
            SetButtonsAlpha(Mathf.Lerp(iconAlpha, 0f, Mathf.SmoothStep(0f, 1f, t / ModeFadeTime)));
            yield return null;
        }
        ApplySprites(toAttack);
        t = 0f;
        while (t < ModeFadeTime)
        {
            t += Time.unscaledDeltaTime;
            SetButtonsAlpha(Mathf.Lerp(0f, iconAlpha, Mathf.SmoothStep(0f, 1f, t / ModeFadeTime)));
            yield return null;
        }
        SetButtonsAlpha(iconAlpha);
        modeFade = null;
    }

    void SetButtonsAlpha(float a)
    {
        SetImgAlpha(topImg, a);
        SetImgAlpha(brImg, a);
        SetImgAlpha(blImg, a);
    }

    static void SetImgAlpha(Image img, float a)
    {
        if (img == null) return;
        Color c = img.color; c.a = a; img.color = c;
    }

    // ---- build ----

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

        // --- Joystick, anchored bottom-left (UNCHANGED) ---
        RectTransform bg = MakeImage("JoystickBG", canvasRoot.transform, circle,
                                     new Vector2(0f, 0f), joystickPos, joystickSize, ringAlpha);
        RectTransform knob = MakeImage("JoystickKnob", bg, circle,
                                       new Vector2(0.5f, 0.5f), Vector2.zero, knobSize, knobAlpha);
        knob.GetComponent<Image>().raycastTarget = false; // the BG receives all pointer events
        joystick = bg.gameObject.AddComponent<TouchJoystick>();
        joystick.Init(bg, knob, (joystickSize - knobSize) * 0.5f);

        // --- load icons ---
        sprSprint = LoadButtonSprite("sprint");
        sprShoot  = LoadButtonSprite("shoot");
        sprPass   = LoadButtonSprite("pass");
        sprDefend = LoadButtonSprite("Defend"); // capital D on disk
        sprSwitch = LoadButtonSprite("switch");
        sprBlock  = LoadButtonSprite("block");

        // --- 3 action buttons (own group so we can hide them as a set); start in ATTACK icons ---
        actionGroup = MakeFullStretchGroup("ActionButtons");
        topBtn = MakeImageButton(actionGroup.transform, "BtnTop",         TopRightPos,    mainButtonSize,   sprSprint, out topImg);
        brBtn  = MakeImageButton(actionGroup.transform, "BtnBottomRight", BottomRightPos, actionButtonSize, sprShoot,  out brImg);
        blBtn  = MakeImageButton(actionGroup.transform, "BtnBottomLeft",  BottomLeftPos,  actionButtonSize, sprPass,   out blImg);

        // --- single PASS OUT button (hidden until the player's keeper holds the ball) ---
        passOutGroup = MakeFullStretchGroup("PassOutButton");
        RectTransform poRt = MakeImage("BtnPassOut", passOutGroup.transform, sprPass,
                                       new Vector2(1f, 0.5f), PassOutPos, PassOutSize, 1f);
        Image poImg = poRt.GetComponent<Image>();
        poImg.type = Image.Type.Simple;
        poImg.preserveAspect = true;
        passOutBtn = poRt.gameObject.AddComponent<TouchButton>();
        passOutBtn.Init(poRt);
        MakeLabel(passOutGroup.transform, "PASS!", new Vector2(1f, 0.5f), PassOutLabelPos,
                  new Vector2(240f, 60f), 40f);
        passOutGroup.SetActive(false);

        BuildStaminaHud();
    }

    // ---- Stamina HUD (Task 3) ----

    // Panel above the joystick: dark rounded rect holding the player number ("P1") on the left
    // and a green→yellow→red fill bar (with a subtle dark border) filling the rest. Plus a
    // full-screen red vignette, hidden until the active player's stamina hits 0%.
    void BuildStaminaHud()
    {
        Sprite panelSpr = MakeRoundedRectSprite((int)StaminaPanelSize.x, (int)StaminaPanelSize.y, 16);
        Sprite barSpr   = MakeRoundedRectSprite(80, 24, 6);

        // panel background (anchored bottom-left, above the joystick)
        RectTransform panel = MakeUIImage("StaminaPanel", canvasRoot.transform, panelSpr,
            new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), StaminaPanelPos, StaminaPanelSize,
            new Color(0.05f, 0.05f, 0.15f, 0.75f));

        // "P1" label (white, bold) on the left
        GameObject lblGo = new GameObject("StaminaLabel");
        lblGo.transform.SetParent(panel, false);
        RectTransform lblRt = lblGo.AddComponent<RectTransform>();
        lblRt.anchorMin = lblRt.anchorMax = new Vector2(0.5f, 0.5f);
        lblRt.pivot = new Vector2(0.5f, 0.5f);
        lblRt.anchoredPosition = new Vector2(-76f, 0f);
        lblRt.sizeDelta = new Vector2(54f, 46f);
        staminaLabel = lblGo.AddComponent<TextMeshProUGUI>();
        staminaLabel.text = "P1";
        staminaLabel.fontSize = 32f;
        staminaLabel.fontStyle = FontStyles.Bold;
        staminaLabel.alignment = TextAlignmentOptions.Center;
        staminaLabel.color = Color.white;
        staminaLabel.raycastTarget = false;

        // bar track + subtle dark border, to the right of the label
        RectTransform track = MakeUIImage("StaminaBarBG", panel, barSpr, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(28f, 0f), new Vector2(150f, 26f),
            new Color(0.12f, 0.12f, 0.12f, 0.92f));

        // fill, anchored to the track's LEFT edge so it grows rightward
        GameObject fillGo = new GameObject("StaminaBarFill");
        fillGo.transform.SetParent(track, false);
        staminaFillRt = fillGo.AddComponent<RectTransform>();
        staminaFillRt.anchorMin = staminaFillRt.anchorMax = new Vector2(0f, 0.5f);
        staminaFillRt.pivot = new Vector2(0f, 0.5f);
        staminaFillRt.anchoredPosition = new Vector2(3f, 0f);
        staminaFillRt.sizeDelta = new Vector2(StaminaBarMaxWidth, 20f);
        staminaFill = fillGo.AddComponent<Image>();
        staminaFill.sprite = barSpr;
        staminaFill.color = HudStaminaColor(1f);
        staminaFill.raycastTarget = false;
    }

    // keeper != null → the human is controlling the goalkeeper, so show ITS stamina (labelled "GK");
    // otherwise show the active field player's ("P{n}").
    void UpdateStaminaHud(Goalkeeper keeper)
    {
        if (staminaFillRt == null) return;

        float pct;
        string label;
        if (keeper != null)
        {
            pct = Mathf.Clamp01(keeper.StaminaPercent01);
            label = "GK";
        }
        else
        {
            PlayerMovement act = TeamManager.ActivePlayer;
            pct = act != null ? Mathf.Clamp01(act.StaminaPercent01) : 1f;
            label = "P" + (TeamManager.ActivePlayerIndex + 1);
        }

        // label — updates instantly on a switch (only re-set when it changes)
        if (staminaLabel != null && label != shownLabel)
        {
            staminaLabel.text = label;
            shownLabel = label;
        }

        // smooth fill width (Lerp speed 5 — never snaps)
        shownStamina = Mathf.Lerp(shownStamina, pct, Time.unscaledDeltaTime * 5f);
        Vector2 sz = staminaFillRt.sizeDelta;
        sz.x = StaminaBarMaxWidth * Mathf.Clamp01(shownStamina);
        staminaFillRt.sizeDelta = sz;
        if (staminaFill != null) staminaFill.color = HudStaminaColor(shownStamina);

        // below 20% the label pulses red↔white (~1s cycle)
        if (staminaLabel != null)
            staminaLabel.color = pct < 0.20f
                ? Color.Lerp(Color.white, new Color(1f, 0.2f, 0.2f), Mathf.PingPong(Time.unscaledTime / 0.5f, 1f))
                : Color.white;
    }

    // Same green→yellow→red ramp as the world bar (kept local so TouchControls has no
    // dependency on the StaminaSystem type).
    static Color HudStaminaColor(float pct)
    {
        Color green  = new Color(0f, 0.8f, 0.2f);
        Color yellow = new Color(1f, 0.85f, 0f);
        Color red    = new Color(0.9f, 0.1f, 0.1f);
        if (pct >= 0.5f) return Color.Lerp(yellow, green, (pct - 0.5f) / 0.5f);
        return Color.Lerp(red, yellow, Mathf.Clamp01(Mathf.InverseLerp(0.2f, 0.5f, pct)));
    }

    static Sprite LoadButtonSprite(string file)
    {
        Sprite s = Resources.Load<Sprite>("Sprites/" + file);
        if (s == null) s = Resources.Load<Sprite>("Sprites/" + file.ToLowerInvariant()); // case-safe fallback
        if (s == null)
            Debug.LogWarning("TouchControls: button sprite 'Sprites/" + file + "' not found in a Resources folder.");
        return s;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>(); // handles both mouse and multi-touch
    }

    // Full-screen-stretched empty RectTransform child of the canvas; children anchored to a
    // corner behave exactly as if parented to the canvas, but the whole set toggles together.
    GameObject MakeFullStretchGroup(string name)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(canvasRoot.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return go;
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

    // A circular image button: no background panel, just the icon. Press feedback (scale
    // pulse) lives in TouchButton.
    TouchButton MakeImageButton(Transform parent, string name, Vector2 pos, float size, Sprite sprite, out Image img)
    {
        RectTransform rt = MakeImage(name, parent, sprite, new Vector2(1f, 0f), pos, size, iconAlpha);
        img = rt.GetComponent<Image>();
        img.type = Image.Type.Simple;
        img.preserveAspect = true;
        TouchButton b = rt.gameObject.AddComponent<TouchButton>();
        b.Init(rt);
        return b;
    }

    // A non-interactive TMP label (e.g. "PASS!" under the pass-out button).
    void MakeLabel(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, float fontSize)
    {
        GameObject go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = fontSize;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.raycastTarget = false;
    }

    // Soft antialiased white circle, tinted via Image.color. (Joystick only.)
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

    // A rectangular UI image with explicit anchor / pivot / position / size / tint.
    // (MakeImage forces a square; the stamina HUD needs rectangles.)
    RectTransform MakeUIImage(string name, Transform parent, Sprite sprite, Vector2 anchor,
                              Vector2 pivot, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false; // HUD readout — never eats touches meant for the buttons below
        return rt;
    }

    // Soft white rounded rectangle (tinted via Image.color). Drawn at its display size so a
    // Simple-mode Image keeps crisp corners; the thin fill bar stretches with negligible distortion.
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
                float a = d <= r - 1f ? 1f : Mathf.Clamp01(r - d); // 1px AA on the rounded corners
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
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

// Press-state holder for one on-screen button. Visual feedback is a quick scale pulse to
// 0.9x while held (returning to 1x on release); alpha is driven by TouchControls so the
// mode-fade and the press feedback never fight over the same channel.
class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public bool Pressed { get; private set; }

    private RectTransform rt;
    const float PressedScale = 0.9f;
    const float ScaleLerp = 18f;

    public void Init(RectTransform rt) { this.rt = rt; }

    public void OnPointerDown(PointerEventData e) { Pressed = true; }
    public void OnPointerUp(PointerEventData e) { Pressed = false; }

    void Update()
    {
        if (rt == null) return;
        float target = Pressed ? PressedScale : 1f;
        float k = 1f - Mathf.Exp(-ScaleLerp * Time.unscaledDeltaTime);
        float v = Mathf.Lerp(rt.localScale.x, target, k);
        rt.localScale = new Vector3(v, v, 1f);
    }
}
