using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Records the last few seconds of live match presentation and re-poses the existing actors for
// a post-goal replay. Gameplay never runs against the replay poses: ScoreManager starts playback
// only inside its existing PlayFrozen goal window, and every transform/renderer/rigidbody/camera
// state is restored before the normal restart sequence continues.
[DefaultExecutionOrder(10000)]
public sealed class GoalReplaySystem : MonoBehaviour
{
    public static GoalReplaySystem Instance { get; private set; }

    [Header("Rolling recording")]
    [SerializeField, Range(10f, 30f)] private float recordFramesPerSecond = 20f;
    [SerializeField, Range(2f, 6f)] private float recordedSeconds = 4f;

    [Header("Three-pass goal highlight")]
    [Tooltip("Only the final shot-to-net moment is repeated; the full rolling buffer is not shown.")]
    [SerializeField, Range(0.8f, 2f)] private float highlightSourceSeconds = 1.25f;
    [SerializeField, Range(1f, 1.2f)] private float replayZoomOut = 1.04f;
    [SerializeField, Range(0f, 0.6f)] private float finalFrameHoldSeconds = 0.28f;
    [SerializeField, Range(0.04f, 0.3f)] private float transitionSeconds = 0.14f;
    [SerializeField, Range(0.02f, 0.2f)] private float repeatCutSeconds = 0.08f;

    // A compact broadcast progression: real speed, then two increasingly deliberate looks at
    // the same recorded scoring moment. Static storage means no pass array is allocated on goal.
    static readonly float[] ReplayPassSpeeds = { 1f, 0.82f, 0.68f };

    Transform[] trackedRoots;
    SpriteRenderer[] trackedSprites;
    int[] spriteRootIndices;
    bool[] spriteSharesTrackedRoot;
    bool[] suppressSpriteInReplay;
    bool[] canToggleSpriteObject;
    Rigidbody2D[] trackedBodies;
    Renderer[] trackedAuxiliaryRenderers;
    int ballRootIndex = -1;
    Camera replayCamera;
    CameraFollow replayCameraFollow;
    bool replayCameraFollowWasEnabled;

    ReplayFrame[] history;
    int historyWriteIndex;
    int historyCount;
    float nextCaptureAt;
    float sampleInterval;
    bool trackingReady;
    bool recordingInterrupted = true;

    GoalClip latestGoal;
    ReplayFrame liveRestoreFrame;
    BodyRuntimeState[] liveBodyStates;
    RendererRuntimeState[] hiddenRendererStates;
    bool restorePending;

    bool replayPlaying;
    bool applyingReplayFrame;
    bool skipRequested;
    float playbackSourceTime;

    GameObject replayUi;
    Image fadeImage;
    TextMeshProUGUI replayBadge;
    TextMeshProUGUI goalText;
    TextMeshProUGUI scoreText;

    public bool HasCapturedGoalReplay => latestGoal != null && latestGoal.frameCount >= 2;
    public bool IsPlaying => replayPlaying;

    public static GoalReplaySystem EnsureExists(GameObject host)
    {
        if (Instance != null) return Instance;
        if (host == null) return null;

        GoalReplaySystem replay = host.GetComponent<GoalReplaySystem>();
        if (replay == null) replay = host.AddComponent<GoalReplaySystem>();
        return replay;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        InitializeTracking();
        BuildReplayUi();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        recordingInterrupted = true;
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) recordingInterrupted = true;
    }

    void OnDisable()
    {
        if (restorePending) RestoreLivePresentation();
    }

    void Update()
    {
        if (!replayPlaying) return;
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape) ||
            Input.GetKeyDown(KeyCode.Return))
            RequestSkip();
    }

    void LateUpdate()
    {
        if (replayPlaying)
        {
            if (applyingReplayFrame && latestGoal != null)
            {
                ApplyClipAtTime(latestGoal, playbackSourceTime);
                HideNonReplayRenderers();
            }
            return;
        }

        MatchContext ctx = MatchContext.Instance;
        if (!trackingReady || ctx == null) return;
        if (ctx.PlayFrozen || Time.timeScale <= 0f)
        {
            recordingInterrupted = true;
            return;
        }

        // A quarter break, pause, sprint duel, penalty setup or previous goal must never be
        // stitched invisibly to later action. Begin a fresh continuous clip on the first live
        // frame after any interruption; the already-copied latest goal remains available.
        if (recordingInterrupted)
        {
            ClearHistory();
            recordingInterrupted = false;
            nextCaptureAt = Time.unscaledTime;
        }
        if (Time.unscaledTime < nextCaptureAt) return;

        CaptureHistoryFrame();
        // Advance from the intended cadence instead of "now". At 30/60 fps this avoids silently
        // dropping to ~15 fps because the render frame rarely lands on an exact 0.05s boundary.
        nextCaptureAt += sampleInterval;
        if (nextCaptureAt <= Time.unscaledTime) nextCaptureAt = Time.unscaledTime + sampleInterval;
    }

    void InitializeTracking()
    {
        if (trackingReady) return;

        List<Transform> roots = new List<Transform>();
        HashSet<Transform> rootSet = new HashSet<Transform>();

        PlayerMovement[] players = Object.FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include);
        for (int i = 0; i < players.Length; i++) AddRoot(players[i] != null ? players[i].transform : null, roots, rootSet);

        BotMovement[] bots = Object.FindObjectsByType<BotMovement>(FindObjectsInactive.Include);
        for (int i = 0; i < bots.Length; i++) AddRoot(bots[i] != null ? bots[i].transform : null, roots, rootSet);

        Goalkeeper[] keepers = Object.FindObjectsByType<Goalkeeper>(FindObjectsInactive.Include);
        for (int i = 0; i < keepers.Length; i++) AddRoot(keepers[i] != null ? keepers[i].transform : null, roots, rootSet);

        MatchContext ctx = MatchContext.Instance;
        if (ctx != null && ctx.Ball != null) AddRoot(ctx.Ball.transform, roots, rootSet);

        trackedRoots = roots.ToArray();

        List<SpriteRenderer> sprites = new List<SpriteRenderer>();
        List<int> owners = new List<int>();
        List<bool> sharesRoot = new List<bool>();
        List<bool> suppressed = new List<bool>();
        List<bool> toggleObjects = new List<bool>();
        HashSet<SpriteRenderer> spriteSet = new HashSet<SpriteRenderer>();
        HashSet<Rigidbody2D> bodySet = new HashSet<Rigidbody2D>();
        List<Rigidbody2D> bodies = new List<Rigidbody2D>();
        HashSet<Renderer> auxiliarySet = new HashSet<Renderer>();
        List<Renderer> auxiliaryRenderers = new List<Renderer>();

        for (int rootIndex = 0; rootIndex < trackedRoots.Length; rootIndex++)
        {
            Transform root = trackedRoots[rootIndex];
            if (root == null) continue;

            SpriteRenderer[] foundSprites = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < foundSprites.Length; i++)
            {
                SpriteRenderer sr = foundSprites[i];
                if (sr == null || !spriteSet.Add(sr)) continue;
                sprites.Add(sr);
                owners.Add(rootIndex);
                sharesRoot.Add(sr.transform == root);
                string objectName = sr.gameObject.name;
                suppressed.Add(objectName == "PlayerIndicator" || objectName == "KeeperIndicator");
                // Never activate/deactivate an actor root; visual-only children (notably the
                // BallFlight shadow) are safe and need their activeSelf state for an exact arc.
                toggleObjects.Add(!rootSet.Contains(sr.transform));
            }

            Rigidbody2D[] foundBodies = root.GetComponentsInChildren<Rigidbody2D>(true);
            for (int i = 0; i < foundBodies.Length; i++)
                if (foundBodies[i] != null && bodySet.Add(foundBodies[i])) bodies.Add(foundBodies[i]);

            Renderer[] foundRenderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < foundRenderers.Length; i++)
            {
                Renderer renderer = foundRenderers[i];
                if (renderer == null || renderer is SpriteRenderer || !auxiliarySet.Add(renderer)) continue;
                auxiliaryRenderers.Add(renderer);
            }
        }

        trackedSprites = sprites.ToArray();
        spriteRootIndices = owners.ToArray();
        spriteSharesTrackedRoot = sharesRoot.ToArray();
        suppressSpriteInReplay = suppressed.ToArray();
        canToggleSpriteObject = toggleObjects.ToArray();
        trackedBodies = bodies.ToArray();
        trackedAuxiliaryRenderers = auxiliaryRenderers.ToArray();

        if (ctx != null && ctx.Ball != null)
            for (int i = 0; i < trackedRoots.Length; i++)
                if (trackedRoots[i] == ctx.Ball.transform) { ballRootIndex = i; break; }

        replayCamera = Camera.main;
        if (replayCamera == null) replayCamera = Object.FindAnyObjectByType<Camera>();
        if (replayCamera != null) replayCameraFollow = replayCamera.GetComponent<CameraFollow>();

        float fps = Mathf.Clamp(recordFramesPerSecond, 10f, 30f);
        sampleInterval = 1f / fps;
        int capacity = Mathf.Max(2, Mathf.CeilToInt(Mathf.Clamp(recordedSeconds, 2f, 6f) * fps) + 2);
        history = new ReplayFrame[capacity];
        for (int i = 0; i < capacity; i++) history[i] = new ReplayFrame(trackedRoots.Length, trackedSprites.Length);

        // Everything needed by the goal path is allocated here at match startup. Capturing a
        // goal now only stores references to already-recorded frames; it does not manufacture a
        // second tree of pose arrays on the most timing-sensitive frame of the match.
        latestGoal = new GoalClip(capacity);
        liveRestoreFrame = new ReplayFrame(trackedRoots.Length, trackedSprites.Length);
        liveBodyStates = new BodyRuntimeState[trackedBodies.Length];
        hiddenRendererStates = new RendererRuntimeState[trackedAuxiliaryRenderers.Length];
        for (int i = 0; i < hiddenRendererStates.Length; i++)
            hiddenRendererStates[i].renderer = trackedAuxiliaryRenderers[i];

        historyWriteIndex = 0;
        historyCount = 0;
        nextCaptureAt = Time.unscaledTime;
        recordingInterrupted = true;
        trackingReady = trackedRoots.Length > 0;

        if (!trackingReady)
            Debug.LogWarning("GoalReplaySystem: no match actors were found; the normal goal hold will be used.");
    }

    static void AddRoot(Transform root, List<Transform> roots, HashSet<Transform> seen)
    {
        if (root != null && seen.Add(root)) roots.Add(root);
    }

    void CaptureHistoryFrame()
    {
        if (!trackingReady || history == null || history.Length == 0) return;
        ReplayFrame destination = history[historyWriteIndex];
        CaptureInto(destination);
        destination.capturedAt = Time.unscaledTime;
        historyWriteIndex = (historyWriteIndex + 1) % history.Length;
        if (historyCount < history.Length) historyCount++;
    }

    void ClearHistory()
    {
        historyWriteIndex = 0;
        historyCount = 0;
    }

    // Called from ScoreManager after the goal is validated but before it freezes/repositions the
    // ball. The forced final sample is therefore the real scoring frame, not the later net anchor.
    public void CaptureGoalReplay(bool playerScored, Transform shooter, int homeScore, int awayScore,
                                  float goalSign)
    {
        InitializeTracking();
        if (!trackingReady || latestGoal == null) return;
        latestGoal.frameCount = 0;

        MatchContext ctx = MatchContext.Instance;
        if (recordingInterrupted && ctx != null && !ctx.PlayFrozen && Time.timeScale > 0f)
        {
            ClearHistory();
            recordingInterrupted = false;
        }

        CaptureHistoryFrame();
        if (historyCount < 2) return;

        int oldest = (historyWriteIndex - historyCount + history.Length) % history.Length;
        int newest = (historyWriteIndex - 1 + history.Length) % history.Length;
        float cutoff = history[newest].capturedAt - Mathf.Clamp(recordedSeconds, 2f, 6f);

        // Keep one frame immediately before the cutoff so interpolation begins smoothly, while
        // still bounding the clip by real elapsed time rather than an assumed sample count.
        int firstOffset = 0;
        while (firstOffset < historyCount - 1)
        {
            int next = (oldest + firstOffset + 1) % history.Length;
            if (history[next].capturedAt >= cutoff) break;
            firstOffset++;
        }

        int clipCount = historyCount - firstOffset;
        if (clipCount < 2) return;
        for (int i = 0; i < clipCount; i++)
        {
            int source = (oldest + firstOffset + i) % history.Length;
            latestGoal.frames[i] = history[source];
        }

        latestGoal.frameCount = clipCount;
        latestGoal.playerScored = playerScored;
        latestGoal.homeScore = homeScore;
        latestGoal.awayScore = awayScore;
        latestGoal.shooter = shooter;
        latestGoal.goalSign = goalSign >= 0f ? 1f : -1f;
    }

    public IEnumerator PlayCapturedGoalReplay()
    {
        if (replayPlaying || !HasCapturedGoalReplay) yield break;

        if (replayUi == null) BuildReplayUi();
        if (replayCamera == null) replayCamera = Camera.main;
        if (replayCamera != null && replayCameraFollow == null)
            replayCameraFollow = replayCamera.GetComponent<CameraFollow>();

        replayPlaying = true;
        applyingReplayFrame = true;
        skipRequested = false;

        SnapshotLivePresentation();
        PrepareReplayPresentation(latestGoal);
        float duration = ClipDuration(latestGoal);
        float highlightStart = Mathf.Max(0f, duration - Mathf.Max(0.1f, highlightSourceSeconds));
        playbackSourceTime = highlightStart;
        SetFade(1f);
        replayUi.SetActive(true);

        yield return Fade(1f, 0f, transitionSeconds);

        // Repeat only the decisive final approach, never the whole rolling buffer. Every pass
        // reads the same immutable recorded frames, so it cannot re-simulate into a different
        // shot. The progressively slower passes remain compact while making the net crossing
        // easy to read.
        for (int pass = 0; pass < ReplayPassSpeeds.Length && !skipRequested; pass++)
        {
            if (replayBadge != null)
            {
                if (pass == 0) replayBadge.text = "<color=#FF3B4D>●</color>  REPLAY  1/3";
                else if (pass == 1) replayBadge.text = "<color=#FF3B4D>●</color>  REPLAY  2/3";
                else replayBadge.text = "<color=#FF3B4D>●</color>  REPLAY  3/3";
            }

            playbackSourceTime = highlightStart;
            float speed = ReplayPassSpeeds[pass];
            while (playbackSourceTime < duration && !skipRequested)
            {
                playbackSourceTime = Mathf.Min(duration,
                    playbackSourceTime + Time.unscaledDeltaTime * speed);
                yield return null;
            }

            if (skipRequested) break;
            playbackSourceTime = duration;

            bool finalPass = pass == ReplayPassSpeeds.Length - 1;
            float holdDuration = finalPass ? finalFrameHoldSeconds : 0.06f;
            float hold = 0f;
            while (hold < holdDuration && !skipRequested)
            {
                hold += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!finalPass && !skipRequested)
            {
                yield return Fade(0f, 1f, repeatCutSeconds);
                playbackSourceTime = highlightStart;
                yield return null; // pose the first highlight frame while the cut is black
                yield return Fade(1f, 0f, repeatCutSeconds);
            }
        }

        float fadeFrom = fadeImage != null ? fadeImage.color.a : 0f;
        yield return Fade(fadeFrom, 1f, transitionSeconds);

        applyingReplayFrame = false;
        RestoreLivePresentation();
        yield return Fade(1f, 0f, transitionSeconds);

        replayUi.SetActive(false);
        replayPlaying = false;
    }

    public void RequestSkip()
    {
        if (replayPlaying) skipRequested = true;
    }

    void SnapshotLivePresentation()
    {
        CaptureInto(liveRestoreFrame);

        // Pause CameraFollow itself instead of fighting it every LateUpdate. This preserves its
        // private SmoothDamp velocity and zoom state, so returning from replay cannot introduce a
        // one-frame camera kick or poison the next live follow calculation.
        replayCameraFollowWasEnabled = replayCameraFollow != null && replayCameraFollow.enabled;
        if (replayCameraFollowWasEnabled) replayCameraFollow.enabled = false;

        for (int i = 0; i < trackedBodies.Length; i++)
        {
            Rigidbody2D body = trackedBodies[i];
            if (body == null) continue;
            liveBodyStates[i] = new BodyRuntimeState
            {
                body = body,
                simulated = body.simulated,
                position = body.position,
                rotation = body.rotation,
                velocity = body.linearVelocity,
                angularVelocity = body.angularVelocity,
                wasAwake = body.IsAwake()
            };
            body.simulated = false;
        }

        // World-space gameplay helpers (power bars, trails and other non-sprite renderers) are
        // deliberately absent from a broadcast replay. They were discovered and their state
        // slots allocated at startup, so entering replay does not perform hierarchy scans.
        for (int i = 0; i < hiddenRendererStates.Length; i++)
        {
            RendererRuntimeState state = hiddenRendererStates[i];
            Renderer renderer = state.renderer;
            if (renderer == null) continue;
            state.enabled = renderer.enabled;
            hiddenRendererStates[i] = state;
            renderer.enabled = false;
        }
        restorePending = true;
    }

    void RestoreLivePresentation()
    {
        if (!restorePending) return;

        if (liveRestoreFrame != null) ApplyFrame(liveRestoreFrame, liveRestoreFrame, 0f, false, false);

        if (liveBodyStates != null)
        {
            for (int i = 0; i < liveBodyStates.Length; i++)
            {
                BodyRuntimeState state = liveBodyStates[i];
                Rigidbody2D body = state.body;
                if (body == null) continue;
                body.position = state.position;
                body.rotation = state.rotation;
                body.linearVelocity = state.velocity;
                body.angularVelocity = state.angularVelocity;
                body.simulated = state.simulated;
                if (state.simulated)
                {
                    if (state.wasAwake) body.WakeUp();
                    else body.Sleep();
                }
            }
        }

        if (hiddenRendererStates != null)
            for (int i = 0; i < hiddenRendererStates.Length; i++)
                if (hiddenRendererStates[i].renderer != null)
                    hiddenRendererStates[i].renderer.enabled = hiddenRendererStates[i].enabled;

        if (replayCameraFollow != null) replayCameraFollow.enabled = replayCameraFollowWasEnabled;

        restorePending = false;
    }

    void HideNonReplayRenderers()
    {
        if (hiddenRendererStates == null) return;
        for (int i = 0; i < hiddenRendererStates.Length; i++)
            if (hiddenRendererStates[i].renderer != null) hiddenRendererStates[i].renderer.enabled = false;
    }

    void CaptureInto(ReplayFrame frame)
    {
        for (int i = 0; i < trackedRoots.Length; i++)
        {
            Transform root = trackedRoots[i];
            if (root == null) continue;
            frame.roots[i] = new RootPose
            {
                position = root.position,
                rotation = root.rotation,
                worldScale = root.lossyScale,
                activeSelf = root.gameObject.activeSelf
            };
        }

        for (int i = 0; i < trackedSprites.Length; i++)
        {
            SpriteRenderer sr = trackedSprites[i];
            if (sr == null) continue;
            Transform visual = sr.transform;
            frame.sprites[i] = new SpritePose
            {
                sprite = sr.sprite,
                enabled = sr.enabled,
                gameObjectActiveSelf = sr.gameObject.activeSelf,
                color = sr.color,
                flipX = sr.flipX,
                flipY = sr.flipY,
                localPosition = visual.localPosition,
                localRotation = visual.localRotation,
                localScale = visual.localScale,
                sortingLayerId = sr.sortingLayerID,
                sortingOrder = sr.sortingOrder
            };
        }

        Camera cam = replayCamera != null ? replayCamera : Camera.main;
        if (cam != null)
        {
            frame.hasCamera = true;
            frame.cameraPosition = cam.transform.position;
            frame.cameraRotation = cam.transform.rotation;
            frame.orthographicSize = cam.orthographicSize;
        }
    }

    void ApplyClipAtTime(GoalClip clip, float sourceTime)
    {
        if (clip == null || clip.frames == null || clip.frameCount == 0) return;
        ReplayFrame[] frames = clip.frames;
        if (clip.frameCount == 1)
        {
            ApplyFrame(frames[0], frames[0], 0f, true, true);
            return;
        }

        float targetTime = frames[0].capturedAt + Mathf.Clamp(sourceTime, 0f, ClipDuration(clip));
        int low = 0;
        int high = clip.frameCount - 1;
        while (low + 1 < high)
        {
            int middle = (low + high) >> 1;
            if (frames[middle].capturedAt <= targetTime) low = middle;
            else high = middle;
        }

        int a = low;
        int b = Mathf.Min(low + 1, clip.frameCount - 1);
        float span = frames[b].capturedAt - frames[a].capturedAt;
        float blend = span > 0.0001f ? Mathf.Clamp01((targetTime - frames[a].capturedAt) / span) : 1f;
        ApplyFrame(frames[a], frames[b], blend, true, true);
    }

    static float ClipDuration(GoalClip clip)
    {
        if (clip == null || clip.frames == null || clip.frameCount < 2) return 0f;
        return Mathf.Max(0f, clip.frames[clip.frameCount - 1].capturedAt - clip.frames[0].capturedAt);
    }

    void ApplyFrame(ReplayFrame a, ReplayFrame b, float blend, bool cinematicCamera, bool suppressHud)
    {
        for (int i = 0; i < trackedRoots.Length; i++)
        {
            Transform root = trackedRoots[i];
            if (root == null) continue;
            RootPose pa = a.roots[i];
            RootPose pb = b.roots[i];
            root.position = Vector3.LerpUnclamped(pa.position, pb.position, blend);
            root.rotation = Quaternion.SlerpUnclamped(pa.rotation, pb.rotation, blend);
            SetWorldScale(root, Vector3.LerpUnclamped(pa.worldScale, pb.worldScale, blend));
        }

        bool useB = blend >= 0.5f;
        for (int i = 0; i < trackedSprites.Length; i++)
        {
            SpriteRenderer sr = trackedSprites[i];
            if (sr == null) continue;
            SpritePose pa = a.sprites[i];
            SpritePose pb = b.sprites[i];
            SpritePose chosen = useB ? pb : pa;
            int owner = spriteRootIndices[i];
            bool rootWasActive = owner < 0 || owner >= a.roots.Length ||
                                 (useB ? b.roots[owner].activeSelf : a.roots[owner].activeSelf);

            if (canToggleSpriteObject[i] && sr.gameObject.activeSelf != chosen.gameObjectActiveSelf)
                sr.gameObject.SetActive(chosen.gameObjectActiveSelf);
            sr.sprite = chosen.sprite;
            sr.enabled = chosen.enabled && rootWasActive && !(suppressHud && suppressSpriteInReplay[i]);
            sr.color = Color.LerpUnclamped(pa.color, pb.color, blend);
            sr.flipX = chosen.flipX;
            sr.flipY = chosen.flipY;
            sr.sortingLayerID = chosen.sortingLayerId;
            sr.sortingOrder = chosen.sortingOrder;

            // A SpriteRenderer on the tracked root shares that root's transform. Reapplying its
            // captured local transform would overwrite the world pose above (and was especially
            // destructive for the tiny Ball root). Child visual transforms remain independent.
            if (!spriteSharesTrackedRoot[i])
            {
                Transform visual = sr.transform;
                visual.localPosition = Vector3.LerpUnclamped(pa.localPosition, pb.localPosition, blend);
                visual.localRotation = Quaternion.SlerpUnclamped(pa.localRotation, pb.localRotation, blend);
                visual.localScale = Vector3.LerpUnclamped(pa.localScale, pb.localScale, blend);
            }
        }

        if (replayCamera != null && a.hasCamera && b.hasCamera)
        {
            Vector3 cameraPosition = Vector3.LerpUnclamped(a.cameraPosition, b.cameraPosition, blend);
            if (cinematicCamera && ballRootIndex >= 0 && ballRootIndex < a.roots.Length)
            {
                Vector3 ballPosition = Vector3.LerpUnclamped(a.roots[ballRootIndex].position,
                                                              b.roots[ballRootIndex].position, blend);
                float goalSign = latestGoal != null ? latestGoal.goalSign : Mathf.Sign(ballPosition.x);
                if (Mathf.Abs(goalSign) < 0.5f) goalSign = 1f;
                Vector3 goalFocus = new Vector3(
                    Mathf.Clamp(ballPosition.x - goalSign * 1.15f, -5.65f, 5.65f),
                    Mathf.Clamp(ballPosition.y * 0.62f, -2.35f, 2.35f),
                    cameraPosition.z);
                cameraPosition = Vector3.LerpUnclamped(cameraPosition, goalFocus, 0.9f);
            }

            replayCamera.transform.position = cameraPosition;
            replayCamera.transform.rotation = Quaternion.SlerpUnclamped(a.cameraRotation, b.cameraRotation, blend);
            float size = Mathf.LerpUnclamped(a.orthographicSize, b.orthographicSize, blend);
            replayCamera.orthographicSize = cinematicCamera
                ? Mathf.Clamp(size * replayZoomOut, 4.35f, 5.15f)
                : size;
        }
    }

    // Root poses are captured in world scale because the ball changes parents while held and
    // released. Applying a carrier-relative local scale after the replay detaches the ball can
    // multiply its visual size. Convert the recorded world size back through the current parent.
    static void SetWorldScale(Transform target, Vector3 worldScale)
    {
        if (target.parent == null)
        {
            target.localScale = worldScale;
            return;
        }

        Vector3 parentScale = target.parent.lossyScale;
        Vector3 fallback = target.localScale;
        target.localScale = new Vector3(
            Mathf.Abs(parentScale.x) > 0.0001f ? worldScale.x / parentScale.x : fallback.x,
            Mathf.Abs(parentScale.y) > 0.0001f ? worldScale.y / parentScale.y : fallback.y,
            Mathf.Abs(parentScale.z) > 0.0001f ? worldScale.z / parentScale.z : fallback.z);
    }

    void PrepareReplayPresentation(GoalClip clip)
    {
        MatchPresentationContext.Restore();
        bool championship = MatchPresentationContext.IsChampionshipFixture;
        string playerClub = championship ? MatchPresentationContext.PlayerClub : "YOU";
        string opponentClub = championship ? MatchPresentationContext.OpponentClub : "BOT";
        string side = clip.playerScored ? playerClub : opponentClub;
        string shooterName = CleanDisplayName(clip.shooter != null ? clip.shooter.name : string.Empty);
        goalText.text = string.IsNullOrEmpty(shooterName)
            ? "GOAL  •  " + side
            : "GOAL  •  " + side + "  •  " + shooterName.ToUpperInvariant();
        scoreText.text = playerClub + "  " + clip.homeScore + "    —    " +
                         clip.awayScore + "  " + opponentClub;
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        if (fadeImage == null) yield break;
        if (duration <= 0f)
        {
            SetFade(to);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetFade(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        SetFade(to);
    }

    void SetFade(float alpha)
    {
        if (fadeImage != null) fadeImage.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
    }

    void BuildReplayUi()
    {
        if (replayUi != null) return;

        GameObject canvasObject = new GameObject("GoalReplayCanvas");
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 130;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        replayUi = NewUiObject("ReplayPresentation", canvasObject.transform);
        Stretch(replayUi.GetComponent<RectTransform>());

        Image blocker = replayUi.AddComponent<Image>();
        blocker.color = new Color(0f, 0f, 0f, 0.001f);
        blocker.raycastTarget = true;

        GameObject topBar = NewUiObject("TopLetterbox", replayUi.transform);
        RectTransform topRt = topBar.GetComponent<RectTransform>();
        topRt.anchorMin = new Vector2(0f, 1f);
        topRt.anchorMax = Vector2.one;
        topRt.pivot = new Vector2(0.5f, 1f);
        topRt.sizeDelta = new Vector2(0f, 78f);
        topRt.anchoredPosition = Vector2.zero;
        Image topImage = topBar.AddComponent<Image>();
        topImage.color = new Color(0.015f, 0.025f, 0.055f, 0.96f);
        topImage.raycastTarget = false;

        GameObject bottomBar = NewUiObject("BottomLetterbox", replayUi.transform);
        RectTransform bottomRt = bottomBar.GetComponent<RectTransform>();
        bottomRt.anchorMin = Vector2.zero;
        bottomRt.anchorMax = new Vector2(1f, 0f);
        bottomRt.pivot = new Vector2(0.5f, 0f);
        bottomRt.sizeDelta = new Vector2(0f, 78f);
        bottomRt.anchoredPosition = Vector2.zero;
        Image bottomImage = bottomBar.AddComponent<Image>();
        bottomImage.color = new Color(0.015f, 0.025f, 0.055f, 0.96f);
        bottomImage.raycastTarget = false;

        replayBadge = MakeText(replayUi.transform, "ReplayBadge",
            "<color=#FF3B4D>●</color>  REPLAY", 25f, TextAlignmentOptions.Left);
        RectTransform badgeRt = replayBadge.rectTransform;
        badgeRt.anchorMin = badgeRt.anchorMax = new Vector2(0f, 1f);
        badgeRt.pivot = new Vector2(0f, 1f);
        badgeRt.anchoredPosition = new Vector2(34f, -16f);
        badgeRt.sizeDelta = new Vector2(230f, 48f);

        goalText = MakeText(replayUi.transform, "GoalReplayTitle", string.Empty, 24f, TextAlignmentOptions.Center);
        RectTransform goalRt = goalText.rectTransform;
        goalRt.anchorMin = goalRt.anchorMax = new Vector2(0.5f, 1f);
        goalRt.pivot = new Vector2(0.5f, 1f);
        goalRt.anchoredPosition = new Vector2(0f, -16f);
        goalRt.sizeDelta = new Vector2(620f, 48f);

        scoreText = MakeText(replayUi.transform, "ReplayScore", string.Empty, 25f, TextAlignmentOptions.Left);
        RectTransform scoreRt = scoreText.rectTransform;
        scoreRt.anchorMin = scoreRt.anchorMax = Vector2.zero;
        scoreRt.pivot = Vector2.zero;
        scoreRt.anchoredPosition = new Vector2(34f, 15f);
        scoreRt.sizeDelta = new Vector2(520f, 48f);

        GameObject skipObject = NewUiObject("SkipReplayButton", replayUi.transform);
        RectTransform skipRt = skipObject.GetComponent<RectTransform>();
        skipRt.anchorMin = skipRt.anchorMax = new Vector2(1f, 0f);
        skipRt.pivot = new Vector2(1f, 0f);
        skipRt.anchoredPosition = new Vector2(-34f, 13f);
        skipRt.sizeDelta = new Vector2(176f, 52f);
        Image skipImage = skipObject.AddComponent<Image>();
        skipImage.color = new Color(0.08f, 0.32f, 0.62f, 0.98f);
        Button skipButton = skipObject.AddComponent<Button>();
        skipButton.targetGraphic = skipImage;
        ColorBlock colors = skipButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.82f, 0.92f, 1f, 1f);
        colors.pressedColor = new Color(0.62f, 0.78f, 0.95f, 1f);
        skipButton.colors = colors;
        skipButton.onClick.AddListener(RequestSkip);

        TextMeshProUGUI skipText = MakeText(skipObject.transform, "SkipLabel", "SKIP  >", 23f,
                                             TextAlignmentOptions.Center);
        Stretch(skipText.rectTransform);
        skipText.fontStyle = FontStyles.Bold;

        GameObject fadeObject = NewUiObject("ReplayFade", replayUi.transform);
        Stretch(fadeObject.GetComponent<RectTransform>());
        fadeImage = fadeObject.AddComponent<Image>();
        fadeImage.color = Color.black;
        fadeImage.raycastTarget = false;

        replayUi.SetActive(false);
    }

    static GameObject NewUiObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI MakeText(Transform parent, string name, string content, float size,
                                    TextAlignmentOptions alignment)
    {
        GameObject go = NewUiObject(name, parent);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    static string CleanDisplayName(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        string result = value.Replace("(Clone)", string.Empty).Replace('_', ' ').Trim();
        return result.Length > 28 ? result.Substring(0, 28) : result;
    }

    sealed class GoalClip
    {
        public readonly ReplayFrame[] frames;
        public int frameCount;
        public bool playerScored;
        public int homeScore;
        public int awayScore;
        public Transform shooter;
        public float goalSign;

        public GoalClip(int capacity)
        {
            frames = new ReplayFrame[capacity];
        }
    }

    sealed class ReplayFrame
    {
        public readonly RootPose[] roots;
        public readonly SpritePose[] sprites;
        public float capturedAt;
        public bool hasCamera;
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
        public float orthographicSize;

        public ReplayFrame(int rootCount, int spriteCount)
        {
            roots = new RootPose[rootCount];
            sprites = new SpritePose[spriteCount];
        }

    }

    struct RootPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 worldScale;
        public bool activeSelf;
    }

    struct SpritePose
    {
        public Sprite sprite;
        public bool enabled;
        public bool gameObjectActiveSelf;
        public Color color;
        public bool flipX;
        public bool flipY;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public int sortingLayerId;
        public int sortingOrder;
    }

    struct BodyRuntimeState
    {
        public Rigidbody2D body;
        public bool simulated;
        public Vector2 position;
        public float rotation;
        public Vector2 velocity;
        public float angularVelocity;
        public bool wasAwake;
    }

    struct RendererRuntimeState
    {
        public Renderer renderer;
        public bool enabled;
    }
}
