using UnityEngine;
using UnityEngine.UI;

// Idle "alive" effects for an active pack card: a gentle sine float plus a periodic diagonal
// shine sweep. Attach with PackCardFX.Attach(cardRect) after the card is positioned — used by
// the shop's pack cards (Packs / Daily Deals / Offers) and the hub reward slots that hold a
// genuinely active pack (Ready / Unlocking). Hand-rolled (no tween package), no per-frame
// allocation; the shine gradient texture is generated ONCE and shared by every instance.
//
// A RectMask2D is added to the card so the sweep clips at the card edges (nested inside the
// shop's ScrollRect mask — RectMask2D nesting intersects, which is what we want). The shine
// Image has raycastTarget = false, so the card and its buttons stay fully tappable.
public class PackCardFX : MonoBehaviour
{
    static Sprite shineSprite; // shared soft vertical band; regenerated after a domain reload

    const float FloatSpeed = 3.0f;      // rad/s → ~2.1s full bob period
    const float SweepDuration = 0.6f;
    const float SweepInterval = 3.5f;   // pause between sweeps (±0.5s jitter)

    RectTransform rt;
    RectTransform shine;
    Vector2 basePos;
    float floatAmp = 5f;
    float phase;          // random offset so side-by-side cards don't bob in sync
    float sweepT = -1f;   // <0 = idle, else seconds into the current sweep
    float nextSweepAt;
    float travel;         // shine X range: ±travel

    public static PackCardFX Attach(RectTransform card, float floatAmplitude = 5f)
    {
        PackCardFX fx = card.gameObject.AddComponent<PackCardFX>();
        fx.floatAmp = floatAmplitude;
        return fx;
    }

    void Awake()
    {
        rt = (RectTransform)transform;
        basePos = rt.anchoredPosition;
        phase = Random.value * Mathf.PI * 2f;

        // If the target is an aspect-preserving Image (shop pack art), first shrink its rect to
        // the sprite's ACTUAL displayed bounds. The alpha-trimmed art doesn't fill the generic
        // square content box, so a mask sized to that box out-sizes the visible image — the
        // float bob then reads as edge clipping. Rect == visible image ⇒ nothing to clip against
        // at any point of the bob. (Pivot is centred, so the displayed image doesn't move.)
        // Hub reward slots use sliced frames (no preserveAspect) and are untouched by this.
        Image target = GetComponent<Image>();
        if (target != null && target.sprite != null && target.preserveAspect
            && target.type == Image.Type.Simple && rt.sizeDelta.x > 0f && rt.sizeDelta.y > 0f)
        {
            float aspect = target.sprite.rect.width / target.sprite.rect.height;
            Vector2 box = rt.sizeDelta;
            rt.sizeDelta = aspect >= box.x / box.y
                ? new Vector2(box.x, box.x / aspect)   // wider than the box → full width
                : new Vector2(box.y * aspect, box.y);  // taller than the box → full height
        }

        if (GetComponent<RectMask2D>() == null) gameObject.AddComponent<RectMask2D>();

        // Shine bar: a soft white band, wider than tall, rotated to sweep diagonally.
        Vector2 size = rt.sizeDelta;
        travel = size.x * 0.9f;
        GameObject go = new GameObject("Shine");
        go.transform.SetParent(transform, false);
        shine = go.AddComponent<RectTransform>();
        shine.anchorMin = shine.anchorMax = new Vector2(0.5f, 0.5f);
        shine.pivot = new Vector2(0.5f, 0.5f);
        shine.sizeDelta = new Vector2(size.x * 0.38f, size.y * 1.8f); // tall enough when rotated
        shine.localRotation = Quaternion.Euler(0f, 0f, 22f);
        Image img = go.AddComponent<Image>();
        img.sprite = ShineSprite();
        img.color = new Color(1f, 1f, 1f, 0.45f);
        img.raycastTarget = false;
        go.SetActive(false);
    }

    void OnEnable()
    {
        // Re-stagger on every (re)activation so cards never sweep in lock-step.
        nextSweepAt = Time.unscaledTime + Random.Range(0.8f, SweepInterval);
        sweepT = -1f;
        if (shine != null) shine.gameObject.SetActive(false);
    }

    void OnDisable()
    {
        if (rt != null) rt.anchoredPosition = basePos; // park at rest while hidden
    }

    void Update()
    {
        // Idle float (unscaled time — hub/shop UI must keep breathing even at timeScale 0).
        Vector2 p = basePos;
        p.y += Mathf.Sin(Time.unscaledTime * FloatSpeed + phase) * floatAmp;
        rt.anchoredPosition = p;

        // Shine sweep state machine.
        if (sweepT < 0f)
        {
            if (Time.unscaledTime < nextSweepAt) return;
            sweepT = 0f;
            shine.gameObject.SetActive(true);
        }
        sweepT += Time.unscaledDeltaTime;
        float k = sweepT / SweepDuration;
        if (k >= 1f)
        {
            sweepT = -1f;
            nextSweepAt = Time.unscaledTime + SweepInterval + Random.Range(-0.5f, 0.5f);
            shine.gameObject.SetActive(false);
            return;
        }
        Vector2 s = shine.anchoredPosition;
        s.x = Mathf.Lerp(-travel, travel, k);
        s.y = 0f;
        shine.anchoredPosition = s;
    }

    // Soft horizontal-gradient band: transparent → white → transparent across X.
    static Sprite ShineSprite()
    {
        if (shineSprite != null) return shineSprite;
        const int w = 64, h = 8;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[w * h];
        for (int x = 0; x < w; x++)
        {
            float t = x / (float)(w - 1);
            byte a = (byte)(Mathf.Sin(t * Mathf.PI) * 255f); // smooth 0→1→0 peak
            for (int y = 0; y < h; y++) px[y * w + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        shineSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0,
                                    SpriteMeshType.FullRect);
        return shineSprite;
    }
}
