using UnityEngine;

// Fills the stands (B-vision "living poolside"). At scene Start every GameObject tagged
// "FanSeat" is treated as one grandstand BENCH: `fansPerBench` fans (random picks from
// fanVariants) are spread evenly across the bench sprite's rendered width, seated on the
// bench art's seat row, each with a barely-perceptible breathing idle (random phase + speed
// per fan — the PackCardFX phase-randomization pattern). Hand-rolled, purely cosmetic.
//
// ALL placement/sizing is derived from the two sprites' actual bounds — no fixed world
// offsets — so it stays correct if the bench art, bench scale or fan art changes:
//   • The bench art (bench.jpg) paints `rowsInBenchArt` horizontal seat rows, so ONE row's
//     pitch = bounds.height / rows is the true on-screen seat size; fans are scaled to
//     `fanHeightInRows` of that pitch (capped by slot width so they can never overlap).
//   • The fan art is a full seated figure — its contact point with a seat is its BUTT at
//     `fanSeatAnchor01` of the sprite's height, not its feet. That anchor is what lands on
//     the bench's seat-surface line (`seatSurface01` of the bench bounds); the legs hang
//     below it, drawn over the bench via the +1 sorting order, like a real seated person.
//
// fanVariants MUST be Inspector-wired: the fan art lives at Assets/Sprites/Pool/fans/ which
// is NOT a Resources folder, so it cannot be loaded by path at runtime. An empty array logs
// a clear warning and spawns nothing — no crash, no placeholder art.
public class CrowdSpawner : MonoBehaviour
{
    [Header("Fan art (drag fan1..fan8 from Assets/Sprites/Pool/fans/)")]
    [SerializeField] private Sprite[] fanVariants;

    [Header("Crowd layout (normalized — derived from the bench sprite's bounds at runtime)")]
    [SerializeField] private int fansPerBench = 4;        // fans spread across each tagged bench's width
    [SerializeField] private int rowsInBenchArt = 7;      // seat rows painted in the bench art (bench.jpg = 7)
    [SerializeField] private float fanHeightInRows = 1.5f; // fan height as a multiple of one row's pitch
    [Range(0f, 1f)]
    [SerializeField] private float seatSurface01 = 0.48f; // seat-surface line within the bench bounds (0 = bottom; 0.48 = the middle row)
    [Range(0f, 1f)]
    [SerializeField] private float fanSeatAnchor01 = 0.34f; // the seated (butt) point within the fan art, from its bottom

    // Jitter fractions so the row reads organic, not a rigid grid — small enough that the
    // fans still clearly sit in one line.
    const float XJitterFrac = 0.15f; // ± this fraction of one fan slot
    const float YJitterFrac = 0.06f; // ± this fraction of one row pitch

    void Start()
    {
        GameObject[] benches = FindBenches();
        if (benches.Length == 0)
        {
            Debug.LogWarning("[CrowdSpawner] No GameObjects tagged 'FanSeat' in this scene — no fans " +
                             "spawned. Tag each bench with FanSeat (Inspector -> Tag dropdown).");
            return;
        }
        if (fanVariants == null || fanVariants.Length == 0)
        {
            Debug.LogWarning("[CrowdSpawner] fanVariants is empty — select the CrowdSpawner object and " +
                             "drag the 8 sprites from Assets/Sprites/Pool/fans/ into the array. " +
                             "No fans spawned.");
            return;
        }

        foreach (GameObject bench in benches) SpawnBenchFans(bench);
    }

    GameObject[] FindBenches()
    {
        try { return GameObject.FindGameObjectsWithTag("FanSeat"); }
        catch (UnityException) // tag not registered in Project Settings → Tags and Layers
        {
            Debug.LogWarning("[CrowdSpawner] The 'FanSeat' tag is not defined in this project.");
            return new GameObject[0];
        }
    }

    void SpawnBenchFans(GameObject bench)
    {
        SpriteRenderer benchSr = bench.GetComponent<SpriteRenderer>();
        if (benchSr == null) benchSr = bench.GetComponentInParent<SpriteRenderer>();
        Bounds b = benchSr != null ? benchSr.bounds : new Bounds();
        if (benchSr == null || benchSr.sprite == null || b.size.x < 0.001f || b.size.y < 0.001f)
        {
            Debug.LogWarning("[CrowdSpawner] FanSeat '" + bench.name + "' has no SpriteRenderer with " +
                             "usable bounds — skipped (fans are laid out across the bench art's width).");
            return;
        }

        int n = Mathf.Max(1, fansPerBench);
        float slotW = b.size.x / n;                                   // one fan's share of the width
        float rowPitch = b.size.y / Mathf.Max(1, rowsInBenchArt);     // one painted seat row's height
        float surfaceY = b.min.y + b.size.y * Mathf.Clamp01(seatSurface01);

        for (int i = 0; i < n; i++)
        {
            Sprite sprite = fanVariants[Random.Range(0, fanVariants.Length)];
            if (sprite == null) continue; // empty array element — skip this fan rather than error
            if (sprite.bounds.size.x < 0.0001f || sprite.bounds.size.y < 0.0001f) continue;

            // Seated-person size: 1.5 row pitches tall reads as "sized to the bench seats",
            // whatever scale this bench instance uses; the slot-width cap guarantees a high
            // fansPerBench packs tighter instead of overlapping neighbours.
            float scale = rowPitch * fanHeightInRows / sprite.bounds.size.y;
            scale = Mathf.Min(scale, slotW / sprite.bounds.size.x);
            if (scale <= 0f) continue;

            // Even slots across the full width, with jitter inside each slot.
            float x = b.min.x + (i + 0.5f) * slotW + Random.Range(-XJitterFrac, XJitterFrac) * slotW;
            // Seat the figure: its butt anchor lands exactly on the seat-surface line (the
            // sprite's own pivot/bounds relationship is accounted for, so any pivot works).
            float seatToPivot = (sprite.bounds.min.y + sprite.bounds.size.y * fanSeatAnchor01) * scale;
            float y = surfaceY - seatToPivot + Random.Range(-YJitterFrac, YJitterFrac) * rowPitch;

            // Child of the spawner, NOT the bench: bench transforms carry their own scale that
            // would distort a child sprite; everything we need is baked into x/y/scale already.
            GameObject fan = new GameObject("Fan_" + bench.name + "_" + (i + 1));
            fan.transform.SetParent(transform, false);
            fan.transform.position = new Vector3(x, y, bench.transform.position.z);
            fan.transform.localScale = Vector3.one * scale;

            SpriteRenderer sr = fan.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.flipX = Random.value < 0.5f; // face either way so identical variants read differently
            sr.sortingLayerID = benchSr.sortingLayerID;
            sr.sortingOrder = benchSr.sortingOrder + 1; // over the bench art (seated legs overlap it)

            fan.AddComponent<FanIdle>();
        }
    }

    // Breathing idle around the spawn pose — deliberately barely perceptible (a living twitch,
    // not a hover): bob = 1.5% of the fan's own rendered height at a resting-breath rate, sway
    // well under a degree of visible lean. Random phase per fan (PackCardFX pattern) so the
    // crowd never moves in lock-step. Offsets from the Start pose — never drifts.
    class FanIdle : MonoBehaviour
    {
        const float BobFraction = 0.015f; // of the fan's rendered height
        const float SwayDegrees = 0.6f;

        Vector3 basePos;
        Quaternion baseRot;
        float bobAmp; // world units, resolved from this fan's size
        float speed;  // rad/s
        float phase;  // random offset so neighbours never sync

        void Start()
        {
            basePos = transform.position;
            baseRot = transform.rotation;
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            bobAmp = (sr != null ? sr.bounds.size.y : 0.5f) * BobFraction;
            speed = Random.Range(0.25f, 0.45f) * Mathf.PI * 2f; // resting breathing rate
            phase = Random.value * Mathf.PI * 2f;
        }

        void Update()
        {
            float t = Time.time * speed + phase;
            transform.position = basePos + new Vector3(0f, Mathf.Sin(t) * bobAmp, 0f);
            transform.rotation = baseRot * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.8f + 1.3f) * SwayDegrees);
        }
    }
}
