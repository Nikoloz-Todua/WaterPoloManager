using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(Animator))]
public sealed class RefereeController : MonoBehaviour
{
    public static RefereeController Instance { get; private set; }

    private static readonly int IsMovingId = Animator.StringToHash("isMoving");
    private static readonly int BlowWhistleId = Animator.StringToHash("BlowWhistle");
    private static readonly Vector2 DefaultStartingPosition = new Vector2(-0.01f, 5.5f);

    [Header("Spawn & Position")]
    [SerializeField] private Vector2 startingPosition = new Vector2(-0.01f, 5.5f);
    [SerializeField] private float minDeckX = -10f;
    [SerializeField] private float maxDeckX = 10f;

    [Header("Deck Pacing")]
    [SerializeField, Min(0f)] private float movementSpeed = 2.5f;
    [SerializeField, Min(0f)] private float followDeadZone = 0.12f;
    [Tooltip("Optional explicit target. When empty, MatchContext's authoritative ball position is used.")]
    [SerializeField] private Transform ballTarget;

    [Header("Whistle")]
    [SerializeField] private AudioSource whistleAudioSource;
    [Tooltip("Used only if the whistle clip cannot be found on the assigned Animator Controller.")]
    [SerializeField, Min(0.01f)] private float whistleDurationFallback = 0.6f;

    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private Coroutine whistleRoutine;
    private float whistleDuration;
    private bool isWhistling;

    private void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("Multiple RefereeController instances are active; the newest instance will receive foul calls.", this);

        Instance = this;
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();

        if (whistleAudioSource == null)
            TryGetComponent(out whistleAudioSource);

        whistleDuration = ResolveWhistleDuration();
    }

    private void Start()
    {
        // An untouched default means the scene Transform is authoritative. This prevents a
        // designer-positioned referee from snapping back to a stale serialized coordinate,
        // while a changed startingPosition remains an intentional Inspector override.
        if (Mathf.Approximately(startingPosition.x, DefaultStartingPosition.x) &&
            Mathf.Approximately(startingPosition.y, DefaultStartingPosition.y))
            startingPosition = transform.position;

        transform.position = new Vector3(startingPosition.x, startingPosition.y, transform.position.z);
        animator.SetBool(IsMovingId, false);
        ResolveBallTarget();
    }

    private void Update()
    {
        if (isWhistling)
        {
            animator.SetBool(IsMovingId, false);
            return;
        }

        if (!TryGetPlayX(out float playX))
        {
            animator.SetBool(IsMovingId, false);
            return;
        }

        float targetX = Mathf.Clamp(playX, MinDeckX, MaxDeckX);
        float deltaX = targetX - transform.position.x;
        bool shouldMove = Mathf.Abs(deltaX) > followDeadZone && movementSpeed > 0f;
        animator.SetBool(IsMovingId, shouldMove);

        if (!shouldMove) return;

        float nextX = Mathf.MoveTowards(transform.position.x, targetX, movementSpeed * Time.deltaTime);
        transform.position = new Vector3(nextX, startingPosition.y, transform.position.z);
        spriteRenderer.flipX = deltaX < 0f;
    }

    public void TriggerFoul()
    {
        if (!isActiveAndEnabled) return;

        if (whistleRoutine != null)
            StopCoroutine(whistleRoutine);

        whistleRoutine = StartCoroutine(WhistleRoutine());
    }

    private IEnumerator WhistleRoutine()
    {
        isWhistling = true;
        animator.SetBool(IsMovingId, false);
        animator.ResetTrigger(BlowWhistleId);
        animator.SetTrigger(BlowWhistleId);

        if (whistleAudioSource != null && whistleAudioSource.clip != null)
            whistleAudioSource.Play();

        yield return new WaitForSeconds(whistleDuration);

        isWhistling = false;
        whistleRoutine = null;
    }

    private bool TryGetPlayX(out float playX)
    {
        MatchContext context = MatchContext.Instance;
        if (context != null && context.Ball != null)
        {
            playX = context.BallPosition.x;
            return true;
        }

        ResolveBallTarget();
        if (ballTarget != null)
        {
            playX = ballTarget.position.x;
            return true;
        }

        playX = transform.position.x;
        return false;
    }

    private void ResolveBallTarget()
    {
        if (ballTarget != null) return;

        GameObject ballObject = GameObject.FindGameObjectWithTag("Ball");
        if (ballObject != null)
            ballTarget = ballObject.transform;
    }

    private float ResolveWhistleDuration()
    {
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        if (controller != null)
        {
            foreach (AnimationClip clip in controller.animationClips)
                if (clip != null && clip.name == "Referee_Whistle")
                    return Mathf.Max(clip.length, 0.01f);
        }

        return Mathf.Max(whistleDurationFallback, 0.01f);
    }

    private float MinDeckX => Mathf.Min(minDeckX, maxDeckX);
    private float MaxDeckX => Mathf.Max(minDeckX, maxDeckX);

    private void OnValidate()
    {
        movementSpeed = Mathf.Max(0f, movementSpeed);
        followDeadZone = Mathf.Max(0f, followDeadZone);
        whistleDurationFallback = Mathf.Max(0.01f, whistleDurationFallback);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
