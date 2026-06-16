using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Quarter-start sprint duel (plan B16.2), completely rebuilt. Singleton. Called by MatchTimer
// at the start of every quarter (incl. Q1). All players freeze on their own goal lines; a
// "3 → 2 → 1 → GO!" countdown plays; then only the two sprinters race to the centre ball.
//
//   Pre-duel : ball forced to EXACT (0,0,0), physics OFF, everyone frozen at the goal lines,
//              big centred countdown (scale-pulse on each number) + a "TAP ... FOR SPEED" hint.
//   Race     : the ball goes live; only the two sprinters move. The bot swims at a fixed speed;
//              the human swims at a base speed that each Space / Sprint TAP (keyboard or an
//              on-screen tap anywhere) boosts toward a cap. A tall vertical SPEED bar on the
//              left fills with that speed (red→orange→green) under a pulsing "TAP FASTER!".
//   Post-duel: the first sprinter within grabDistance grabs the ball, ALL duel UI disappears,
//              the normal joystick / buttons / stamina HUD return, and play resumes.
//
// The whole duel UI is built in code (no prefabs / no Inspector wiring) and the gameplay touch
// controls are hidden for its duration (Feature 4). During the duel everyone is frozen via
// MatchContext.PlayFrozen, so this script moves the two sprinters directly by Rigidbody2D
// position — no fight with the frozen bodies, and the AI brain never runs for them.
public class SprintDuel : MonoBehaviour
{
    public static SprintDuel Instance { get; private set; }

    [Header("Timing")]
    [Tooltip("Seconds each countdown number (5 / 4 / 3 / 2 / 1 / GO!) stays on screen.")]
    [SerializeField] private float countdownStep = 1f;
    [Tooltip("The countdown starts from this number (5 → 4 → 3 → 2 → 1 → GO!).")]
    [SerializeField] private int countdownStart = 5;

    [Header("Sprinter speeds")]
    [SerializeField] private float sprintSpeed = 4f;     // bot sprinter (fixed)
    [SerializeField] private float baseHumanSprint = 3f; // human sprinter base (no taps)
    [SerializeField] private float maxHumanSprint = 6f;  // human sprinter cap (mashing)
    [SerializeField] private float tapBoost = 0.45f;     // speed gained per Space / Sprint tap
    [SerializeField] private float boostDecay = 2f;      // speed bleeds back toward base (/sec)

    [Header("Geometry")]
    [SerializeField] private float grabDistance = 1f;    // first sprinter within this wins
    [SerializeField] private float lineInset = 1f;       // how far inward from the goal line
    [SerializeField] private float lineupYSpread = 3f;   // vertical spread of the line-up
    [Tooltip("The designated sprinter starts this much further inward (toward centre) than the rest of its line, so it's clearly the sprinter and not the goalkeeper sitting behind it (Task 1).")]
    [SerializeField] private float sprinterForwardOffset = 1f;

    [Header("Off-sprinter formation (at GO!)")]
    [Tooltip("Speed (~60% of a normal swim) at which every NON-sprinter swims into its tactical formation once the race starts — both teams, identically. They never freeze / wait for possession (Tasks 1 & 5).")]
    [SerializeField] private float formationMoveSpeed = 3f;

    // race + countdown state
    private enum State { Idle, Countdown, Racing }
    private State state = State.Idle;

    private Transform humanSprinter;
    private Transform botSprinter;
    private float humanSpeed;
    private int countdownNumber;   // 3 → 2 → 1 → 0(GO!)
    private float countdownTimer;

    // duel UI (built once in Awake, shown only while a duel runs)
    private GameObject duelCanvas;
    private TMP_Text countdownText;
    private TMP_Text hintText;
    private GameObject speedBarGroup;
    private Image speedBarFill;
    private TMP_Text tapFasterText;
    private DuelTapCatcher tapCatcher;
    private float countdownPulse;  // 0..1 scale-pulse value, reset on each number
    private float shownFill;       // smoothed speed-bar fill

    const float ReferenceWidth = 1920f, ReferenceHeight = 1080f;

    void Awake()
    {
        Instance = this;
        BuildDuelUI();
        duelCanvas.SetActive(false);
    }

    // Begin a fresh duel: ball dead-centre + frozen, everyone lined up, countdown pending.
    public void StartDuel()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null) return;

        TeamSide pt = ctx.PlayerTeam;
        TeamSide bt = ctx.BotTeam;

        // Ball to EXACT centre and OFF (physics disabled) so nothing can nudge it off (0,0)
        // during the countdown; it goes live again the instant the race starts.
        CenterBall(ctx, live: false);
        ctx.SetPossession(null);
        ctx.ClearGrabBan();
        ctx.ResetBallTouch(); // camera holds the wide overview until a sprinter grabs (Task 1)

        LineUp(pt);
        LineUp(bt);

        humanSprinter = FirstMember(pt);
        botSprinter = FirstMember(bt);
        PlaceSprinterCentre(pt, humanSprinter);
        PlaceSprinterCentre(bt, botSprinter);

        // nobody to race → just play on (no UI, no freeze)
        if (humanSprinter == null && botSprinter == null) { state = State.Idle; return; }

        // camera + control follow the human sprinter through the duel
        if (humanSprinter != null) TeamManager.ActivatePlayer(humanSprinter);

        humanSpeed = baseHumanSprint;
        countdownNumber = Mathf.Max(1, countdownStart);
        countdownTimer = countdownStep;
        countdownPulse = 1f;
        shownFill = 0f;
        state = State.Countdown;
        ctx.FreezeAll();

        BeginDuelUI();
    }

    void Update()
    {
        if (state == State.Idle) return;

        // animate the countdown number's scale pulse (eases back to rest)
        countdownPulse = Mathf.MoveTowards(countdownPulse, 0f, Time.unscaledDeltaTime / 0.35f);
        if (countdownText != null && countdownText.enabled)
            countdownText.transform.localScale = Vector3.one * (1f + 0.45f * countdownPulse);

        if (state == State.Countdown)
        {
            countdownTimer -= Time.unscaledDeltaTime;
            if (countdownTimer <= 0f) AdvanceCountdown();
            return;
        }

        if (state == State.Racing)
        {
            // GO! lingers for one step, then clears so it doesn't cover the race
            if (countdownText != null && countdownText.enabled)
            {
                countdownTimer -= Time.unscaledDeltaTime;
                if (countdownTimer <= 0f) countdownText.enabled = false;
            }

            // taps: Space / LeftShift (keyboard) or a tap anywhere on screen (touch/mouse)
            int taps = (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.LeftShift)) ? 1 : 0;
            if (tapCatcher != null) taps += tapCatcher.ConsumeTaps();
            if (taps > 0) humanSpeed = Mathf.Min(humanSpeed + tapBoost * taps, maxHumanSprint);

            UpdateSpeedBar();

            // pulse the "TAP FASTER!" prompt
            if (tapFasterText != null)
            {
                float a = 0.55f + 0.45f * Mathf.PingPong(Time.unscaledTime * 2f, 1f);
                Color c = tapFasterText.color; c.a = a; tapFasterText.color = c;
            }
        }
    }

    // 3 → 2 → 1 → GO! (each shown for countdownStep seconds). GO! starts the race.
    void AdvanceCountdown()
    {
        countdownNumber--;
        countdownPulse = 1f;             // re-pulse the new number
        countdownTimer = countdownStep;

        if (countdownNumber >= 1)
        {
            if (countdownText != null) countdownText.text = countdownNumber.ToString();
        }
        else
        {
            // GO! — release the ball and start the race
            if (countdownText != null) countdownText.text = "GO!";
            if (hintText != null) hintText.enabled = false;
            if (speedBarGroup != null) speedBarGroup.SetActive(true);
            if (tapCatcher != null) tapCatcher.enabled = true;
            if (humanSprinter != null) TeamManager.ActivatePlayer(humanSprinter); // camera on the sprinter

            MatchContext ctx = MatchContext.Instance;
            if (ctx != null) CenterBall(ctx, live: true); // ball goes live, still dead-centre
            state = State.Racing;
        }
    }

    void FixedUpdate()
    {
        if (state != State.Racing) return;

        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || ctx.Ball == null) return;

        Vector2 ballPos = ctx.Ball.position;

        humanSpeed = Mathf.MoveTowards(humanSpeed, baseHumanSprint, boostDecay * Time.fixedDeltaTime);

        // the two designated sprinters race to the centre ball (human is tap-driven, bot fixed)
        MoveTowardsTarget(humanSprinter, ballPos, humanSpeed);
        MoveTowardsTarget(botSprinter, ballPos, sprintSpeed);

        // everyone ELSE immediately swims into formation at ~60% speed — both teams alike, no
        // freezing / waiting for possession (Tasks 1 & 5). Position-based, so it's immune to the
        // PlayFrozen flag (which still suppresses the brain, control, and possession logic).
        MoveTeamToFormation(ctx.PlayerTeam);
        MoveTeamToFormation(ctx.BotTeam);

        float dH = Dist(humanSprinter, ballPos);
        float dB = Dist(botSprinter, ballPos);
        bool humanWins = dH <= grabDistance && (dB > grabDistance || dH <= dB);
        bool botWins = !humanWins && dB <= grabDistance;

        if (humanWins) Finish(ctx, humanSprinter, ctx.PlayerTeam);
        else if (botWins) Finish(ctx, botSprinter, ctx.BotTeam);
    }

    void Finish(MatchContext ctx, Transform winner, TeamSide team)
    {
        ctx.GiveBallTo(winner, team);
        ctx.Unfreeze();
        ctx.SetKickoffPass(team); // winner's AI center passes back to its deepest teammate first
        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock();
        state = State.Idle;
        EndDuelUI();
    }

    // ---- ball / sprinters ----

    // Force the ball to exact (0,0,0), zeroed velocity, no parent. live=false also turns its
    // physics OFF so nothing can move it during the countdown; live=true turns it back on.
    void CenterBall(MatchContext ctx, bool live)
    {
        if (ctx.Ball == null) return;
        ctx.Ball.transform.SetParent(null);
        ctx.Ball.linearVelocity = Vector2.zero;
        ctx.Ball.angularVelocity = 0f;
        ctx.Ball.position = Vector2.zero;
        ctx.Ball.transform.position = Vector3.zero;
        ctx.Ball.simulated = live;
    }

    // Move a transform toward a world target at `speed` via its Rigidbody2D position — immune
    // to the PlayFrozen freeze (which the bodies' own FixedUpdate honours by zeroing velocity,
    // never fighting this teleport). Used for BOTH the sprinters and the formation jog.
    void MoveTowardsTarget(Transform s, Vector2 target, float speed)
    {
        if (s == null) return;
        Rigidbody2D rb = s.GetComponent<Rigidbody2D>();
        Vector2 cur = rb != null ? rb.position : (Vector2)s.position;
        Vector2 next = Vector2.MoveTowards(cur, target, speed * Time.fixedDeltaTime);
        if (rb != null) rb.position = next; else s.position = next;
    }

    // Every available member EXCEPT this team's sprinter jogs to its natural formation spot.
    void MoveTeamToFormation(TeamSide team)
    {
        if (team == null || team.members == null) return;
        foreach (Transform m in team.members)
        {
            if (m == null || m == humanSprinter || m == botSprinter) continue;
            // possession is undecided during the race → both teams jog to the neutral sat-back
            // (home) shape; whoever wins the ball transitions from there into normal play.
            MoveTowardsTarget(m, team.RestartFormationSpot(m, false), formationMoveSpeed);
        }
    }

    float Dist(Transform s, Vector2 p) => s == null ? Mathf.Infinity : Vector2.Distance(s.position, p);

    // Line every available member up along their own (defended) goal line, spread on y.
    void LineUp(TeamSide team)
    {
        if (team == null || team.members == null || team.defendGoal == null) return;

        float x = GoalLineX(team);
        int n = team.members.Length;
        for (int i = 0; i < n; i++)
        {
            Transform m = team.members[i];
            if (m == null) continue; // excluded/empty slot
            float t = n > 1 ? ((float)i / (n - 1)) * 2f - 1f : 0f;
            m.position = new Vector3(x, t * lineupYSpread, m.position.z);

            Rigidbody2D rb = m.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }
    }

    // Put the sprinter centred on its goal axis for a fair straight race, pulled slightly
    // INWARD (toward centre) of the rest of the line-up so it reads clearly as the sprinter
    // and never overlaps the goalkeeper sitting on the line behind it (Task 1).
    void PlaceSprinterCentre(TeamSide team, Transform sprinter)
    {
        if (team == null || sprinter == null || team.defendGoal == null) return;
        float goalX = team.defendGoal.position.x;
        float sign = goalX == 0f ? 1f : Mathf.Sign(goalX);
        float x = goalX - sign * (lineInset + sprinterForwardOffset);
        sprinter.position = new Vector3(x, 0f, sprinter.position.z);
    }

    float GoalLineX(TeamSide team)
    {
        float goalX = team.defendGoal.position.x;
        float sign = goalX == 0f ? 1f : Mathf.Sign(goalX);
        return goalX - sign * lineInset; // pulled slightly inward toward centre
    }

    Transform FirstMember(TeamSide team)
    {
        if (team == null || team.members == null) return null;
        foreach (Transform m in team.members)
            if (m != null) return m; // excluded members are null → first non-excluded
        return null;
    }

    // ---- UI ----

    // Show the duel overlay + hide the gameplay touch controls. Reset the per-duel UI state.
    void BeginDuelUI()
    {
        if (countdownText != null)
        {
            countdownText.enabled = true;
            countdownText.text = Mathf.Max(1, countdownStart).ToString();
            countdownText.transform.localScale = Vector3.one;
        }
        if (hintText != null) hintText.enabled = true;
        if (speedBarGroup != null) speedBarGroup.SetActive(false); // race only
        if (tapCatcher != null) { tapCatcher.enabled = false; tapCatcher.ConsumeTaps(); }
        shownFill = 0f;
        UpdateSpeedBar();

        if (duelCanvas != null) duelCanvas.SetActive(true);
        if (TouchControls.Instance != null) TouchControls.Instance.SetGameplayVisible(false);
    }

    // Hide the duel overlay + restore the gameplay touch controls instantly.
    void EndDuelUI()
    {
        if (duelCanvas != null) duelCanvas.SetActive(false);
        if (tapCatcher != null) tapCatcher.enabled = false;
        if (TouchControls.Instance != null) TouchControls.Instance.SetGameplayVisible(true);
    }

    void UpdateSpeedBar()
    {
        if (speedBarFill == null) return;
        float target = Mathf.Clamp01((humanSpeed - baseHumanSprint) / Mathf.Max(0.01f, maxHumanSprint - baseHumanSprint));
        shownFill = Mathf.Lerp(shownFill, target, Time.unscaledDeltaTime * 12f); // never snaps
        speedBarFill.fillAmount = shownFill;
    }

    void BuildDuelUI()
    {
        EnsureEventSystem();

        duelCanvas = new GameObject("SprintDuelCanvas");
        duelCanvas.transform.SetParent(transform, false);
        Canvas canvas = duelCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110; // above the HUD + touch controls, below the result screen (120)
        CanvasScaler scaler = duelCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.matchWidthOrHeight = 0.5f;
        duelCanvas.AddComponent<GraphicRaycaster>();

        // full-screen invisible tap-catcher (taps anywhere = a sprint tap during the race)
        GameObject catchGo = new GameObject("TapCatcher");
        catchGo.transform.SetParent(duelCanvas.transform, false);
        Image catchImg = catchGo.AddComponent<Image>();
        catchImg.color = new Color(0f, 0f, 0f, 0f); // transparent but raycast-able
        RectTransform catchRt = catchImg.rectTransform;
        catchRt.anchorMin = Vector2.zero; catchRt.anchorMax = Vector2.one;
        catchRt.offsetMin = catchRt.offsetMax = Vector2.zero;
        tapCatcher = catchGo.AddComponent<DuelTapCatcher>();
        tapCatcher.enabled = false;

        // big centred countdown number
        countdownText = MakeText("Countdown", "5", 240f, new Vector2(0f, 70f), FontStyles.Bold);
        countdownText.outlineWidth = 0.25f;
        countdownText.outlineColor = new Color32(0, 90, 160, 255);

        // hint under the countdown
        hintText = MakeText("Hint", "TAP SPACE / TAP SPRINT FOR SPEED", 46f, new Vector2(0f, -150f), FontStyles.Bold);
        hintText.color = new Color(1f, 1f, 1f, 0.85f);

        BuildSpeedBar();
    }

    // Tall vertical SPEED bar on the left: "SPEED" label, a dark track with a bottom-up
    // red→orange→green gradient fill, and a pulsing "TAP FASTER!" under it.
    void BuildSpeedBar()
    {
        speedBarGroup = new GameObject("SpeedBar");
        RectTransform groupRt = speedBarGroup.AddComponent<RectTransform>();
        groupRt.SetParent(duelCanvas.transform, false);
        groupRt.anchorMin = groupRt.anchorMax = new Vector2(0f, 0.5f); // left-centre
        groupRt.pivot = new Vector2(0.5f, 0.5f);
        groupRt.anchoredPosition = new Vector2(170f, 0f);
        groupRt.sizeDelta = new Vector2(140f, 760f);

        // "SPEED" label above the track
        TMP_Text label = MakeText("SpeedLabel", "SPEED", 50f, new Vector2(0f, 330f), FontStyles.Bold);
        label.rectTransform.SetParent(groupRt, false);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        label.rectTransform.anchoredPosition = new Vector2(0f, 330f);
        label.rectTransform.sizeDelta = new Vector2(260f, 70f);

        // dark track
        Sprite rounded = MakeRoundedRectSprite(120, 560, 24);
        RectTransform track = MakeUIImage("SpeedTrack", groupRt, rounded,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f),
            new Vector2(110f, 560f), new Color(0.05f, 0.05f, 0.12f, 0.85f));

        // gradient fill (revealed bottom→top by fillAmount)
        GameObject fillGo = new GameObject("SpeedFill");
        fillGo.transform.SetParent(track, false);
        RectTransform fillRt = fillGo.AddComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(8f, 8f); fillRt.offsetMax = new Vector2(-8f, -8f);
        speedBarFill = fillGo.AddComponent<Image>();
        speedBarFill.sprite = MakeVerticalGradientSprite();
        speedBarFill.type = Image.Type.Filled;
        speedBarFill.fillMethod = Image.FillMethod.Vertical;
        speedBarFill.fillOrigin = (int)Image.OriginVertical.Bottom;
        speedBarFill.fillAmount = 0f;
        speedBarFill.raycastTarget = false;

        // pulsing "TAP FASTER!" under the track
        tapFasterText = MakeText("TapFaster", "TAP FASTER!", 40f, new Vector2(0f, -330f), FontStyles.Bold);
        tapFasterText.rectTransform.SetParent(groupRt, false);
        tapFasterText.rectTransform.anchorMin = tapFasterText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        tapFasterText.rectTransform.anchoredPosition = new Vector2(0f, -330f);
        tapFasterText.rectTransform.sizeDelta = new Vector2(260f, 60f);
        tapFasterText.color = new Color(1f, 0.85f, 0.2f, 1f);

        speedBarGroup.SetActive(false);
    }

    TMP_Text MakeText(string name, string content, float size, Vector2 pos, FontStyles style)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(duelCanvas.transform, false);
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = content;
        txt.fontSize = size;
        txt.fontStyle = style;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        RectTransform rt = txt.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1100f, 300f);
        rt.anchoredPosition = pos;
        return txt;
    }

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
        img.raycastTarget = false;
        return rt;
    }

    // Vertical gradient: RED at the bottom → ORANGE in the middle → GREEN at the top.
    static Sprite MakeVerticalGradientSprite()
    {
        const int h = 256, w = 8;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color red = new Color(0.95f, 0.2f, 0.15f);
        Color orange = new Color(1f, 0.6f, 0.1f);
        Color green = new Color(0.2f, 1f, 0.3f);
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float t = (float)y / (h - 1);
            Color c = t < 0.5f ? Color.Lerp(red, orange, t * 2f) : Color.Lerp(orange, green, (t - 0.5f) * 2f);
            for (int x = 0; x < w; x++) px[y * w + x] = c;
        }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    // Soft white rounded rectangle (tinted via Image.color).
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
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }
}

// Counts taps (pointer-downs) anywhere on the duel overlay so the human can boost by tapping
// the screen on mobile (or clicking) — the keyboard reads Space / LeftShift separately.
class DuelTapCatcher : MonoBehaviour, IPointerDownHandler
{
    private int taps;
    public void OnPointerDown(PointerEventData e) { if (enabled) taps++; }
    public int ConsumeTaps() { int t = taps; taps = 0; return t; }
}
