using UnityEngine;

// Shared runtime path for both human-controlled swimmers and bots. Every caller receives its own
// material instance; renderers belonging to one swimmer may share that one per-swimmer instance.
public static class PlayerPaletteSwapRuntime
{
    private const string MaterialResourcePath = "Materials/PlayerPaletteSwap";
    private const string ShaderName = "WaterPolo/Player Palette Swap";
    private static readonly int CapTintId = Shader.PropertyToID("_CapTint");
    private static readonly int SwimwearTintId = Shader.PropertyToID("_SwimwearTint");

    public static Material CreateInstance(Component owner, Color capTint, Color swimwearTint,
                                          params SpriteRenderer[] renderers)
    {
        Material template = Resources.Load<Material>(MaterialResourcePath);
        Material instance = null;

        if (template != null)
        {
            instance = new Material(template);
        }
        else
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader != null) instance = new Material(shader);
        }

        if (instance == null)
        {
            Debug.LogError($"{owner.name}: palette-swap material/shader is missing.", owner);
            return null;
        }

        instance.name = $"{owner.name} Palette Swap (Instance)";
        SetTints(instance, capTint, swimwearTint);

        if (renderers != null)
        {
            foreach (SpriteRenderer renderer in renderers)
                if (renderer != null) renderer.sharedMaterial = instance;
        }

        return instance;
    }

    public static void SetTints(Material instance, Color capTint, Color swimwearTint)
    {
        if (instance == null) return;
        instance.SetColor(CapTintId, capTint);
        instance.SetColor(SwimwearTintId, swimwearTint);
    }
}

// The flipbook state currently owning the SpriteRenderer. Keeping this presentation-only enum in
// one place lets PlayerAnimator select a matching FPS/size without duplicating gameplay detection.
public enum PlayerFlipbookVisualState
{
    Legacy,
    Idle,
    Swimming,
    Holding,
    Throwing
}

// Direction is separate from the broad Swimming state: it lets each authored movement sheet have
// its own playback speed and visual scale without changing gameplay movement or state selection.
public enum PlayerSwimmingDirection
{
    Horizontal,
    Up,
    Down
}

// Tiny non-MonoBehaviour playback helper shared by PlayerAnimator and BotAnimator.
public sealed class PlayerFlipbookPlayback
{
    private Sprite[] frames;
    private float startedAt;
    private bool loop;

    public bool Active => PlayerFlipbookSet.ValidFrames(frames);

    public void Select(Sprite[] nextFrames, bool shouldLoop)
    {
        if (!PlayerFlipbookSet.ValidFrames(nextFrames)) nextFrames = null;
        if (ReferenceEquals(frames, nextFrames) && loop == shouldLoop) return;

        frames = nextFrames;
        loop = shouldLoop;
        startedAt = Time.time;
    }

    public void Apply(SpriteRenderer renderer, float framesPerSecond)
    {
        if (renderer == null || !Active) return;

        float fps = Mathf.Max(1f, framesPerSecond);
        int rawFrame = Mathf.FloorToInt(Mathf.Max(0f, Time.time - startedAt) * fps);
        int frame = loop ? rawFrame % frames.Length : Mathf.Min(rawFrame, frames.Length - 1);
        renderer.sprite = frames[frame];
    }
}
