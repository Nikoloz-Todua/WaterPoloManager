using UnityEngine;

[CreateAssetMenu(fileName = "PlayerFlipbookSet", menuName = "Water Polo/Player Flipbook Set")]
public sealed class PlayerFlipbookSet : ScriptableObject
{
    public const int RequiredFrameCount = 6;

    [Header("Directional swimming")]
    [Tooltip("Off keeps the established horizontal swimming.png presentation in every direction. Leave off until the up/down art is approved.")]
    [SerializeField] private bool useDirectionalSwimmingFrames;

    [SerializeField] private Sprite[] idleFrames = new Sprite[RequiredFrameCount];
    [SerializeField] private Sprite[] swimmingFrames = new Sprite[RequiredFrameCount];
    [SerializeField] private Sprite[] swimmingUpFrames = new Sprite[RequiredFrameCount];
    [SerializeField] private Sprite[] swimmingDownFrames = new Sprite[RequiredFrameCount];
    [SerializeField] private Sprite[] holdingFrames = new Sprite[RequiredFrameCount];
    [SerializeField] private Sprite[] throwingFrames = new Sprite[RequiredFrameCount];

    public Sprite[] IdleFrames => ValidFrames(idleFrames) ? idleFrames : null;
    public Sprite[] SwimmingFrames => ValidFrames(swimmingFrames) ? swimmingFrames : null;
    public Sprite[] SwimmingUpFrames => ValidFrames(swimmingUpFrames) ? swimmingUpFrames : null;
    public Sprite[] SwimmingDownFrames => ValidFrames(swimmingDownFrames) ? swimmingDownFrames : null;
    public Sprite[] HoldingFrames => ValidFrames(holdingFrames) ? holdingFrames : null;
    public Sprite[] ThrowingFrames => ValidFrames(throwingFrames) ? throwingFrames : null;
    public bool UseDirectionalSwimmingFrames => useDirectionalSwimmingFrames;

    public static bool ValidFrames(Sprite[] frames)
    {
        if (frames == null || frames.Length != RequiredFrameCount) return false;
        for (int i = 0; i < frames.Length; i++)
            if (frames[i] == null) return false;
        return true;
    }

    public static float Duration(Sprite[] frames, float framesPerSecond)
    {
        return ValidFrames(frames)
            ? frames.Length / Mathf.Max(1f, framesPerSecond)
            : 0f;
    }
}
