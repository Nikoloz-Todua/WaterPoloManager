using UnityEngine;

// Gentle floating animation for the pool lane lines: a subtle sine bob (up/down)
// plus a tiny left/right sway. Each instance picks its own random phase AND speed
// on Start, so the lines drift independently instead of moving in lockstep.
// Standalone — no Inspector wiring needed; drop it on any object. Offsets are
// always applied from the position/rotation captured on Start, so it never drifts.
public class PoolLineFloat : MonoBehaviour
{
    [SerializeField] private float bobAmplitude = 0.04f; // units up/down (keep subtle)
    [SerializeField] private float swayDegrees = 1.5f;   // max tilt each way
    [SerializeField] private float minFrequency = 0.6f;  // Hz — slow and calming
    [SerializeField] private float maxFrequency = 0.9f;  // Hz

    [Header("Waterline")]
    [SerializeField, Range(0.5f, 1f)] private float troughAlpha = 0.72f;
    [SerializeField, Range(0.5f, 1f)] private float crestAlpha = 0.94f;
    [SerializeField, Range(0f, 0.5f)] private float waterTintStrength = 0.16f;
    [SerializeField, Range(0f, 0.5f)] private float refractedLayerAlpha = 0.22f;
    [SerializeField, Range(0f, 0.08f)] private float refractionOffset = 0.025f;

    private Vector3 basePos;     // captured on Start; all motion offsets from these
    private Quaternion baseRot;
    private float bobOmega;      // radians/second
    private float swayOmega;
    private float bobPhase;
    private float swayPhase;
    private SpriteRenderer surfaceRenderer;
    private SpriteRenderer refractedRenderer;
    private Color baseColor;
    private Color waterColor = new Color(0.05f, 0.74f, 1f, 1f);

    void Start()
    {
        basePos = transform.localPosition;
        baseRot = transform.localRotation;

        // Independent speed + phase per axis per object → no two lines sync up.
        bobOmega  = Random.Range(minFrequency, maxFrequency) * 2f * Mathf.PI;
        swayOmega = Random.Range(minFrequency, maxFrequency) * 2f * Mathf.PI;
        bobPhase  = Random.Range(0f, 2f * Mathf.PI);
        swayPhase = Random.Range(0f, 2f * Mathf.PI);

        // This component also exists on both goal structures for their legacy gentle
        // motion.  Only the actual lane/divider line art should receive the submerged
        // tint/refraction treatment; applying it to a Goal makes the whole net fade.
        if (IsPoolDividerLine())
            SetupWaterlineVisual();
    }

    void Update()
    {
        float bobWave = Mathf.Sin(Time.time * bobOmega + bobPhase);
        float bob  = bobWave * bobAmplitude;
        float sway = Mathf.Sin(Time.time * swayOmega + swayPhase) * swayDegrees;

        transform.localPosition = basePos + new Vector3(0f, bob, 0f);
        transform.localRotation = baseRot * Quaternion.Euler(0f, 0f, sway);

        UpdateWaterlineVisual(bobWave);
    }

    void OnDisable()
    {
        if (surfaceRenderer != null)
            surfaceRenderer.color = baseColor;
    }

    private void SetupWaterlineVisual()
    {
        surfaceRenderer = GetComponent<SpriteRenderer>();
        if (surfaceRenderer == null || surfaceRenderer.sprite == null)
            return;

        baseColor = surfaceRenderer.color;

        GameObject poolWater = GameObject.Find("PoolWater");
        SpriteRenderer waterRenderer = poolWater != null
            ? poolWater.GetComponent<SpriteRenderer>()
            : null;
        Material waterMaterial = waterRenderer != null ? waterRenderer.sharedMaterial : null;
        if (waterMaterial != null && waterMaterial.HasProperty("_BaseColor"))
            waterColor = waterMaterial.GetColor("_BaseColor");

        GameObject layer = new GameObject("SubmergedRefraction");
        layer.hideFlags = HideFlags.DontSave;
        layer.transform.SetParent(transform, false);

        refractedRenderer = layer.AddComponent<SpriteRenderer>();
        CopyRendererPresentation(surfaceRenderer, refractedRenderer);
        refractedRenderer.sortingOrder = surfaceRenderer.sortingOrder - 1;
    }

    private bool IsPoolDividerLine()
    {
        // GoalRight/GoalLeft currently carry PoolLineFloat too.  The component may be
        // useful for their subtle bob, but goal geometry must always render normally.
        if (GetComponent<Goal>() != null)
            return false;

        // Current scene art is named horizontal-line_* / vertical-line_* and may also
        // be grouped below PoolLines.  Supporting both makes the scope survive harmless
        // hierarchy or clone renames without turning this into a broad sprite effect.
        Transform cursor = transform;
        while (cursor != null)
        {
            string objectName = cursor.name.ToLowerInvariant();
            if (objectName.Contains("poollines") || objectName.Contains("-line") ||
                objectName.Contains("line_"))
                return true;
            cursor = cursor.parent;
        }

        return false;
    }

    private void UpdateWaterlineVisual(float bobWave)
    {
        if (surfaceRenderer == null)
            return;

        // The line becomes slightly water-tinted and translucent at the bottom
        // of its bob, then clears as it rises. A second wave keeps the result
        // from reading as one rigid opacity pulse.
        float height01 = bobWave * 0.5f + 0.5f;
        float ripple = Mathf.Sin(Time.time * swayOmega * 1.37f + swayPhase) * 0.5f + 0.5f;
        float submerged01 = Mathf.Clamp01((1f - height01) * 0.75f + ripple * 0.25f);

        Color tint = Color.Lerp(baseColor, waterColor, waterTintStrength * submerged01);
        tint.a = baseColor.a * Mathf.Lerp(troughAlpha, crestAlpha, height01);
        surfaceRenderer.color = tint;

        if (refractedRenderer == null)
            return;

        CopyRendererPresentation(surfaceRenderer, refractedRenderer);
        refractedRenderer.sortingOrder = surfaceRenderer.sortingOrder - 1;
        Color refractedColor = Color.Lerp(baseColor, waterColor, 0.72f);
        refractedColor.a = baseColor.a * refractedLayerAlpha * Mathf.Lerp(1f, 0.45f, height01);
        refractedRenderer.color = refractedColor;

        float xOffset = Mathf.Sin(Time.time * bobOmega * 1.19f + swayPhase) * refractionOffset;
        float yOffset = -Mathf.Lerp(refractionOffset * 0.25f, refractionOffset, submerged01);
        refractedRenderer.transform.localPosition = new Vector3(xOffset, yOffset, 0f);
        refractedRenderer.transform.localScale = new Vector3(
            1f + Mathf.Sin(Time.time * swayOmega + bobPhase) * 0.008f,
            1f - submerged01 * 0.035f,
            1f);
    }

    private static void CopyRendererPresentation(SpriteRenderer source, SpriteRenderer target)
    {
        target.sprite = source.sprite;
        target.flipX = source.flipX;
        target.flipY = source.flipY;
        target.drawMode = source.drawMode;
        target.size = source.size;
        target.maskInteraction = source.maskInteraction;
        target.spriteSortPoint = source.spriteSortPoint;
        target.sortingLayerID = source.sortingLayerID;
    }
}
