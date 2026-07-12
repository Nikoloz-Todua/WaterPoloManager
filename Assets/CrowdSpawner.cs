using System.Collections.Generic;
using UnityEngine;

// Fills the stands (B-vision "living poolside"). At scene Start every GameObject tagged
// "FanSeat" (front stands), "FanSeatBack" (bottom stands, rotated 180°) or "FanSeatSide"
// (left/right stands, rotated ±90°) is treated as one grandstand BENCH painted with the same
// 6-row × 18-column seat art. Each bench gets `fansPerBench` fans on randomly-chosen seat
// cells (a partially-full stand, never a rigid sell-out), each with a barely-perceptible
// breathing idle (random phase + speed per fan — the PackCardFX phase-randomization pattern).
// On a goal (detected by polling ScoreManager, no ScoreManager edit) ~half of EACH stand
// briefly swaps to its same-index celebrate pose, then reverts. Hand-rolled, purely cosmetic.
//
// ALL placement/sizing is derived from the sprites' actual bounds + the bench transform — no
// fixed world offsets — so it stays correct if the bench art, scale or rotation changes:
//   • The seat grid is computed in the bench sprite's LOCAL space (seats along the art's X,
//     rows along the art's Y) and each seat point is mapped to world through the bench
//     transform — that is what keeps the grid glued to the painted seats on the rotated
//     back (180°) and side (±90°) benches. For an unrotated bench it is exactly the old
//     bounds math.
//   • One row's world pitch = the art row height carried through the transform; fans are
//     scaled to the stand's `{front,back,side}FanHeightInRows` multiple of that pitch, so each
//     stand has its own base size (back is bumped larger because its source art frames the
//     figure smaller and reads too small at the shared 1.5).
//     (This assumes each fan PNG frames its character at a consistent scale. Where the source
//     art does NOT — e.g. some FanSeatBack PNGs — a per-element `backScaleOverride` can nudge
//     individual back fans; see that field's tooltip. The real fix is consistent art.)
//   • The fan art is a full seated figure — its contact point with a seat is its BUTT at
//     `fanSeatAnchor01` of the sprite's height, not its feet. That anchor lands on its own
//     row's seat-surface line (`{front,back,side}SeatLineInRow01` within the row band); the
//     legs hang below it, drawn over the row behind via the sorting order, like a seated person.
//   • Draw order is a quantized Y-sort per bench: fans lower on screen draw over the row
//     behind/above them, whichever way the bench is rotated.
//
// The three sprite arrays MUST be Inspector-wired: the fan art lives at
// Assets/Sprites/Pool/fans/ (front/, back/, side/) which is NOT a Resources folder, so it
// cannot be loaded by path at runtime. Each tag fails independently — an empty array logs a
// clear warning and skips ONLY that tag's benches; the other stands spawn normally. No crash,
// no placeholder art.
//
// Side stands: the side art is RIGHT-facing. Each FanSeatSide bench compares its world-bounds
// centre against the PoolWater object's rendered-bounds centre at runtime — benches to the
// RIGHT of the pool are mirrored (flipX) so both sides face the water. Scene data, never names.
public class CrowdSpawner : MonoBehaviour
{
    [Header("Fan art (drag from Assets/Sprites/Pool/fans/ — NOT a Resources folder)")]
    [Tooltip("FanSeat benches (front stands): fanFront1..fanFront8 from fans/front/")]
    [SerializeField] private Sprite[] fanVariants;
    [Tooltip("FanSeatBack benches (bottom stands, rotated 180°): fanBack1..fanBack8 from fans/back/")]
    [SerializeField] private Sprite[] fanVariantsBack;
    [Tooltip("FanSeatSide benches (left/right stands, rotated ±90°): fanSideL1..fanSideL8 from fans/side/ — RIGHT-facing art, auto-mirrored on benches to the right of the pool so both sides face the water")]
    [SerializeField] private Sprite[] fanVariantsSide;

    [Header("Crowd layout (normalized — derived from each bench sprite's bounds at runtime)")]
    [SerializeField] private int fansPerBench = 70;        // seat cells filled per bench, out of rows × columns (clamped)
    [SerializeField] private int rowsInBenchArt = 6;
    [SerializeField] private int columnsInBenchArt = 18;
    [Header("Bench-art seat-area inset (normalized in the sprite's local orientation)")]
    [Tooltip("Excludes the thin non-seat border at the left/right/top of the art before the 18x6 grid is built.")]
    [Range(0f, 0.45f)] [SerializeField] private float seatInsetLeft01 = 0.005f;
    [Range(0f, 0.45f)] [SerializeField] private float seatInsetRight01 = 0.005f;
    [Range(0f, 0.45f)] [SerializeField] private float seatInsetTop01 = 0.005f;
    [Tooltip("The new bench art's lower ~24% is the solid front wall/rail, not seats. This removes it from the grid regardless of bench rotation.")]
    [Range(0f, 0.45f)] [SerializeField] private float seatInsetBottom01 = 0.24f;
    [Tooltip("Fan height as a multiple of one seat-row's pitch, PER STAND. Front/side keep the " +
             "original 1.5; back is bumped to ~1.95 because its source art frames the figure smaller " +
             "and reads too small at 1.5. Tune per stand here; backScaleOverride still layers on top " +
             "per-sprite for the fanBack art outliers.")]
    [SerializeField] private float frontFanHeightInRows = 1.5f;  // FanSeat  (front stands)
    [SerializeField] private float backFanHeightInRows = 1.95f;  // FanSeatBack (bottom stands) — back art frames the figure small
    [SerializeField] private float sideFanHeightInRows = 1.5f;   // FanSeatSide (left/right stands)
    [Tooltip("Seat-surface line WITHIN one row band (0 = band bottom, 1 = top), PER STAND — where a " +
             "fan's butt anchor lands inside its row. Front/back keep 0.35 (≈ the middle row: " +
             "(3+0.35)/7 of the whole bench, matching the old shared value); side is raised to 0.42 so " +
             "side fans seat deeper into the band instead of floating near its edge. Tune per stand.")]
    [Range(0f, 1f)] [SerializeField] private float frontSeatLineInRow01 = 0.35f; // FanSeat
    [Range(0f, 1f)] [SerializeField] private float backSeatLineInRow01 = 0.35f;  // FanSeatBack
    [Range(0f, 1f)] [SerializeField] private float sideSeatLineInRow01 = 0.42f;  // FanSeatSide — raised so side fans seat deeper
    [Range(0f, 1f)]
    [SerializeField] private float fanSeatAnchor01 = 0.34f; // the seated (butt) point within the fan art, from its bottom

    [Header("Per-fan size fix — BACK stand only (for inconsistent source art)")]
    [Tooltip("Optional size multiplier per fanVariantsBack element, SAME index as that array " +
             "(element 0 = fanBack1, element 7 = fanBack8). Empty, or any element left ≤ 0, means " +
             "1 = no change. Only needed because some back PNGs frame the character at a different " +
             "scale/pose than the others, so a single array-wide height can't equalize them. " +
             "Tune by eye; regenerating the odd PNGs to a consistent framing is the cleaner fix.")]
    [SerializeField] private float[] backScaleOverride;

    [Tooltip("Optional per-fan POSITION nudge for the BACK stand, SAME index as fanVariantsBack " +
             "(element 0 = fanBack1 … element 7 = fanBack8). Default (0,0) = no change. Units are the " +
             "placed fan's own scale on screen X/Y (fans are upright), so a value stays proportional if " +
             "the back height is retuned. E.g. set element 3 (fanBack4) to (0,-0.15) to drop it into its " +
             "seat. Layered on top of grid placement + backScaleOverride, same per-sprite pattern.")]
    [SerializeField] private Vector2[] backOffsetOverride;

    [Header("Goal celebration — on a goal, ~50% of EACH stand's fans pop a cheer pose")]
    [Tooltip("FanSeat cheer poses: fanFrontCele1..8 from fans/front/celebrate/ — SAME index/person as Fan Variants (element 0 = the fanFront1 person's cheer). Empty = the front stand just doesn't celebrate (no crash).")]
    [SerializeField] private Sprite[] fanVariantsFrontCele;
    [Tooltip("FanSeatBack cheer poses: fanBackCele1..8 from fans/back/celebrate/ — SAME index as Fan Variants Back.")]
    [SerializeField] private Sprite[] fanVariantsBackCele;
    [Tooltip("FanSeatSide cheer poses: fanSideLCele1..8 from fans/side/celebrate/ — SAME index as Fan Variants Side.")]
    [SerializeField] private Sprite[] fanVariantsSideCele;
    [Tooltip("Seconds a fan holds its cheer pose after a goal. A second goal mid-celebration restarts it cleanly (re-picks a fresh ~half of each stand and resets this timer).")]
    [SerializeField] private float celebrateSeconds = 3.5f;

    // Jitter fractions so the grid reads organic, not rigid — small enough that each fan
    // still clearly sits in its own seat.
    const float XJitterFrac = 0.15f; // ± this fraction of one seat cell's width
    const float YJitterFrac = 0.06f; // ± this fraction of one row's pitch

    float poolCenterX;        // resolved lazily from PoolWater, only if a side bench needs it
    bool poolCenterResolved;

    // Goal celebration: each spawned fan is registered per-stand so a goal can flip ~half of
    // EACH stand independently to its same-index cheer pose, then revert after celebrateSeconds.
    readonly List<Celebrant> celebrantsFront = new List<Celebrant>();
    readonly List<Celebrant> celebrantsBack = new List<Celebrant>();
    readonly List<Celebrant> celebrantsSide = new List<Celebrant>();
    readonly List<Celebrant> activeCelebrants = new List<Celebrant>(); // currently in a cheer pose
    int lastScoreTotal;     // a goal = this rising (polled from ScoreManager — no ScoreManager edit)
    float celebrateEndTime; // unscaled-time deadline to revert
    bool celebrating;

    // Ball X cached ONCE per frame (in Update) for the front/back fans' tracking tilt, so the
    // ~700 FanIdle.Update calls read a cached float instead of hitting MatchContext.Instance each.
    static float sBallX;
    static bool sBallValid;

    // One registered fan: its renderer, its seated sprite, its same-index cheer sprite (null if
    // that stand's celebrate array isn't wired for this index → that fan simply won't cheer), and
    // its FanIdle (flipped to the energetic celebration animation while it cheers).
    class Celebrant
    {
        public SpriteRenderer sr;
        public Sprite seated;
        public Sprite celebrate;
        public FanIdle idle;
    }

    void Start()
    {
        // capWidth: only the FRONT stand keeps the never-overlap-your-neighbour width cap. BACK
        // opts OUT (false) so backFanHeightInRows actually enlarges its ~square fans instead of the
        // cap clamping them to ~one cell; SIDE has no cap anyway (its seats run vertically).
        SpawnCrowd("FanSeat", fanVariants, fanVariantsFrontCele, celebrantsFront, false, true, frontFanHeightInRows, frontSeatLineInRow01, null, null,
                   "fanFront1..fanFront8 from Assets/Sprites/Pool/fans/front/");
        SpawnCrowd("FanSeatBack", fanVariantsBack, fanVariantsBackCele, celebrantsBack, false, false, backFanHeightInRows, backSeatLineInRow01, backScaleOverride, backOffsetOverride,
                   "fanBack1..fanBack8 from Assets/Sprites/Pool/fans/back/");
        SpawnCrowd("FanSeatSide", fanVariantsSide, fanVariantsSideCele, celebrantsSide, true, false, sideFanHeightInRows, sideSeatLineInRow01, null, null,
                   "fanSideL1..fanSideL8 from Assets/Sprites/Pool/fans/side/");

        // Baseline for goal detection (a goal = the score total rising above this).
        lastScoreTotal = ScoreTotal();
    }

    static int ScoreTotal()
    {
        ScoreManager sm = ScoreManager.Instance;
        return sm != null ? sm.HomeScore + sm.AwayScore : 0;
    }

    void Update()
    {
        // Goal detection WITHOUT touching ScoreManager: when the score total rises, a goal was
        // scored → celebrate. (Scores only climb in play; a drop means a new match reset them.)
        int total = ScoreTotal();
        if (total > lastScoreTotal) { lastScoreTotal = total; StartCelebration(); }
        else if (total < lastScoreTotal) lastScoreTotal = total;

        // Revert once the cheer window elapses. Unscaled time so the goal-hang freeze or a pause
        // can never strand a fan mid-cheer.
        if (celebrating && Time.unscaledTime >= celebrateEndTime) StopCelebration();

        // Cache the ball's world X ONCE this frame for the front/back fans' tracking tilt (read
        // per fan below). MatchContext.Instance is a cheap static getter, hit once — not per fan.
        MatchContext mc = MatchContext.Instance;
        sBallValid = mc != null;
        if (sBallValid) sBallX = mc.BallPosition.x;
    }

    // A goal fired. Overlapping-goal policy = CLEAN RESTART: seat anyone still cheering, then pick
    // a FRESH ~half of each stand and reset the timer (chosen over extend/stack so a flurry of
    // goals can never leave fans stuck or double-counted).
    void StartCelebration()
    {
        RevertActive();
        PickHalf(celebrantsFront);
        PickHalf(celebrantsBack);
        PickHalf(celebrantsSide);
        celebrating = activeCelebrants.Count > 0;
        celebrateEndTime = Time.unscaledTime + Mathf.Max(0.1f, celebrateSeconds);
    }

    // ~50% of THIS stand's fans (independent per stand) flip to their same-index cheer pose.
    void PickHalf(List<Celebrant> stand)
    {
        foreach (Celebrant c in stand)
        {
            if (c.sr == null || c.celebrate == null) continue; // no cheer art for this fan → skip
            if (Random.value < 0.5f)
            {
                c.sr.sprite = c.celebrate;
                if (c.idle != null) c.idle.celebrating = true; // → energetic animation
                activeCelebrants.Add(c);
            }
        }
    }

    void StopCelebration()
    {
        RevertActive();
        celebrating = false;
    }

    // Seat every currently-cheering fan back to its original sprite + calm idle.
    void RevertActive()
    {
        foreach (Celebrant c in activeCelebrants)
        {
            if (c.sr != null) c.sr.sprite = c.seated;
            if (c.idle != null) c.idle.celebrating = false;
        }
        activeCelebrants.Clear();
    }

    // One tag's benches with one variant set. Each tag fails independently: a missing tag,
    // zero tagged benches or a still-empty sprite array warns and skips THIS stand only —
    // the other stands spawn normally. `seatLine` is this stand's seat-surface line within a
    // row band. `perVariantScale` (size ×) and `perVariantOffset` (position nudge) are optional
    // per-element arrays index-matched to `variants`; null = no correction. `celeVariants` +
    // `registry` register each spawned fan for goal celebrations (its same-index cheer sprite +
    // this stand's list); a null registry opts the stand out. `capWidth` applies the width
    // (anti-overlap) cap — true only for the front stand.
    void SpawnCrowd(string benchTag, Sprite[] variants, Sprite[] celeVariants, List<Celebrant> registry, bool faceThePool, bool capWidth, float heightInRows, float seatLine, float[] perVariantScale, Vector2[] perVariantOffset, string wireHint)
    {
        GameObject[] benches = FindBenches(benchTag);
        if (benches.Length == 0)
        {
            Debug.LogWarning("[CrowdSpawner] No GameObjects tagged '" + benchTag + "' in this scene — " +
                             "no fans spawned for that stand. Tag each bench (Inspector -> Tag dropdown).");
            return;
        }
        if (variants == null || variants.Length == 0)
        {
            Debug.LogWarning("[CrowdSpawner] The sprite array for tag '" + benchTag + "' is empty — " +
                             "select the CrowdSpawner object and drag " + wireHint + " into it. " +
                             benches.Length + " bench(es) skipped; other stands are unaffected.");
            return;
        }

        foreach (GameObject bench in benches) SpawnBenchFans(bench, variants, celeVariants, registry, faceThePool, capWidth, heightInRows, seatLine, perVariantScale, perVariantOffset);
    }

    GameObject[] FindBenches(string benchTag)
    {
        try { return GameObject.FindGameObjectsWithTag(benchTag); }
        catch (UnityException) // tag not registered in Project Settings → Tags and Layers
        {
            Debug.LogWarning("[CrowdSpawner] The '" + benchTag + "' tag is not defined in this project.");
            return new GameObject[0];
        }
    }

    void SpawnBenchFans(GameObject bench, Sprite[] variants, Sprite[] celeVariants, List<Celebrant> registry, bool faceThePool, bool capWidth, float heightInRows, float seatLine, float[] perVariantScale, Vector2[] perVariantOffset)
    {
        SpriteRenderer benchSr = bench.GetComponent<SpriteRenderer>();
        if (benchSr == null) benchSr = bench.GetComponentInParent<SpriteRenderer>();
        Bounds world = benchSr != null ? benchSr.bounds : new Bounds();
        if (benchSr == null || benchSr.sprite == null || world.size.x < 0.001f || world.size.y < 0.001f)
        {
            Debug.LogWarning("[CrowdSpawner] Bench '" + bench.name + "' has no SpriteRenderer with " +
                             "usable bounds — skipped (fans are laid out across the bench art's seat grid).");
            return;
        }

        int rows = Mathf.Max(1, rowsInBenchArt);
        int cols = Mathf.Max(1, columnsInBenchArt);
        int cells = rows * cols;
        int n = Mathf.Clamp(fansPerBench, 0, cells);
        if (n == 0) return;

        // The seat grid in the bench sprite's LOCAL space (art X = seats, art Y = rows); every
        // seat point goes through the bench transform, so rotation/scale/flip are respected.
        Transform bt = benchSr.transform;
        Bounds art = benchSr.sprite.bounds;
        float left = Mathf.Clamp01(seatInsetLeft01);
        float right = Mathf.Clamp01(seatInsetRight01);
        float bottom = Mathf.Clamp01(seatInsetBottom01);
        float top = Mathf.Clamp01(seatInsetTop01);
        if (left + right >= 0.95f) { left = 0f; right = 0f; }
        if (bottom + top >= 0.95f) { bottom = 0f; top = 0f; }

        float gridMinX = art.min.x + art.size.x * left;
        float gridMaxX = art.max.x - art.size.x * right;
        float gridMinY = art.min.y + art.size.y * bottom;
        float gridMaxY = art.max.y - art.size.y * top;
        float cellW = (gridMaxX - gridMinX) / cols;
        float rowH = (gridMaxY - gridMinY) / rows;

        Vector3 rowStep = bt.TransformVector(0f, rowH, 0f);   // one row's offset in world space
        Vector3 seatStep = bt.TransformVector(cellW, 0f, 0f); // one seat's offset in world space
        float rowPitch = rowStep.magnitude;
        float seatPitch = seatStep.magnitude;
        if (rowPitch < 0.0001f || seatPitch < 0.0001f) return; // degenerate scale — nothing to place

        // The seats of a row line up horizontally on screen for the front/back stands, so a
        // never-overlap-your-neighbour width cap would apply — but only the FRONT stand actually
        // uses it (capWidth). The BACK stand opts out so raising backFanHeightInRows genuinely
        // enlarges its ~square fans (the cap was silently clamping them to ~one cell = ~one row
        // tall, which is why the height field "did nothing"); bigger back fans may now overlap
        // horizontally, reading as a packed stand. SIDE benches never cap (seats run VERTICALLY —
        // figures are meant to stack up the screen; the quantized Y-sort below orders them).
        bool rowsRunVertical = Mathf.Abs(rowStep.y) >= Mathf.Abs(seatStep.y);

        // Side stands face the water. The side art faces RIGHT unflipped (verified from the
        // source PNGs — fanSideL1..8 all look right), so a bench to the RIGHT of the pool
        // centre must be mirrored (flipX) to face LEFT/inward, while a bench to the LEFT is
        // left unflipped (already faces right → inward). Pool centre from live scene bounds,
        // never from bench naming.
        bool mirror = faceThePool && world.center.x > PoolCenterX();

        // Pick n DISTINCT seat cells at random — partial Fisher-Yates over the cell ids.
        int[] cellIds = new int[cells];
        for (int i = 0; i < cells; i++) cellIds[i] = i;

        for (int i = 0; i < n; i++)
        {
            int swap = Random.Range(i, cells);
            int cell = cellIds[swap];
            cellIds[swap] = cellIds[i];
            cellIds[i] = cell;

            int row = cell / cols;
            int col = cell % cols;

            int vi = Random.Range(0, variants.Length);
            Sprite sprite = variants[vi];
            if (sprite == null) continue; // empty array element — skip this fan rather than error
            if (sprite.bounds.size.x < 0.0001f || sprite.bounds.size.y < 0.0001f) continue;

            // Seated-person size: `heightInRows` row pitches tall reads as "sized to the
            // bench seats" on this stand, whatever scale/rotation this bench instance uses.
            float scale = rowPitch * heightInRows / sprite.bounds.size.y;
            if (rowsRunVertical && capWidth) scale = Mathf.Min(scale, seatPitch / sprite.bounds.size.x);
            // Optional per-variant size correction (back stand only; default 1). Applied AFTER
            // the width cap so a hand-tuned value visibly resizes the fan, and BEFORE the seat
            // anchor below so the corrected fan still sits on its seat line.
            if (perVariantScale != null && vi < perVariantScale.Length && perVariantScale[vi] > 0f)
                scale *= perVariantScale[vi];
            if (scale <= 0f) continue;

            // This cell's seat point: its slot along the row (jittered inside the slot) on its
            // own row's painted seat line — computed in art space, then pushed to world.
            float lx = gridMinX + (col + 0.5f + Random.Range(-XJitterFrac, XJitterFrac)) * cellW;
            float ly = gridMinY + (row + Mathf.Clamp01(seatLine) + Random.Range(-YJitterFrac, YJitterFrac)) * rowH;
            Vector3 seat = bt.TransformPoint(lx, ly, 0f);

            // Fans stay world-upright (the art set carries the facing); the butt anchor lands
            // exactly on the seat point (any sprite pivot works), the legs hang below it.
            float seatToPivot = (sprite.bounds.min.y + sprite.bounds.size.y * fanSeatAnchor01) * scale;

            // Optional per-variant position nudge (back stand only; default zero). In the fan's
            // own upright units (× its render scale) so it stays proportional if the stand height
            // is retuned; world X/Y since fans are world-upright.
            Vector2 off = (perVariantOffset != null && vi < perVariantOffset.Length) ? perVariantOffset[vi] : Vector2.zero;

            // Child of the spawner, NOT the bench: bench transforms carry scale/rotation that
            // would distort a child sprite; everything needed is baked into the world pose.
            GameObject fan = new GameObject("Fan_" + bench.name + "_" + (i + 1));
            fan.transform.SetParent(transform, false);
            fan.transform.position = new Vector3(seat.x + off.x * scale, seat.y - seatToPivot + off.y * scale, seat.z);
            fan.transform.localScale = Vector3.one * scale;

            SpriteRenderer sr = fan.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            // Side fans all face the pool; front/back fans mirror at random so identical
            // variants read differently (the existing behaviour).
            sr.flipX = faceThePool ? mirror : Random.value < 0.5f;
            sr.sortingLayerID = benchSr.sortingLayerID;
            sr.sortingOrder = benchSr.sortingOrder + 1 +
                              FrontRank(row, col, rows, cols, rowsRunVertical, rowStep, seatStep);

            FanIdle idle = fan.AddComponent<FanIdle>();
            idle.trackBall = !faceThePool; // front + back fans lean toward the ball; side fans don't

            // Register for goal celebrations: this fan's seated sprite + its SAME-INDEX cheer
            // sprite (null if this stand's cele array isn't wired for that index → it won't cheer).
            if (registry != null)
            {
                Sprite cele = (celeVariants != null && vi < celeVariants.Length) ? celeVariants[vi] : null;
                registry.Add(new Celebrant { sr = sr, seated = sprite, celebrate = cele, idle = idle });
            }
        }
    }

    // Quantized Y-sort inside one bench: rank 0 = the seat line furthest UP the screen (drawn
    // first, behind), highest rank = the screen-lowest line (drawn last, in front) — so a
    // fan's head never draws over the fan seated in front of it, on any bench rotation.
    // Ranks span 1..rows above the bench on front/back stands, 1..columns on side stands.
    static int FrontRank(int row, int col, int rows, int cols, bool rowsRunVertical,
                         Vector3 rowStep, Vector3 seatStep)
    {
        int idx = rowsRunVertical ? row : col;
        int count = rowsRunVertical ? rows : cols;
        float stepY = rowsRunVertical ? rowStep.y : seatStep.y;
        return stepY > 0f ? count - 1 - idx : idx;
    }

    // The pool's horizontal centre, read from the PoolWater object's rendered bounds —
    // resolved once, and only if a side bench actually needs it. No PoolWater in the scene →
    // warn and fall back to world x = 0 (the current pool centre) so side fans still face
    // somewhere sensible instead of crashing.
    float PoolCenterX()
    {
        if (poolCenterResolved) return poolCenterX;
        poolCenterResolved = true;
        poolCenterX = 0f;

        GameObject pool = GameObject.Find("PoolWater");
        if (pool == null)
        {
            Debug.LogWarning("[CrowdSpawner] No 'PoolWater' object in this scene — side-fan facing " +
                             "falls back to world x = 0 as the pool centre.");
            return poolCenterX;
        }
        Renderer r = pool.GetComponent<Renderer>();              // Renderer, not SpriteRenderer:
        if (r == null) r = pool.GetComponentInChildren<Renderer>(); // survives the planned MeshRenderer swap
        poolCenterX = r != null ? r.bounds.center.x : pool.transform.position.x;
        return poolCenterX;
    }

    // Per-fan idle animation around the spawn pose. TWO moods, chosen each frame by `celebrating`:
    //
    //   CALM (default) — barely perceptible "alive" twitch. One breath cycle drives BOTH a tiny
    //   vertical position bob AND a Y-SCALE pulse (torso lengthening). The read is carried MOSTLY
    //   by the scale pulse: whole-body vertical translation is what read as "floating on water",
    //   so the bob is small (0.004 of height) and the scale breath leaned on (0.025 of base Y). The
    //   chest visibly rises/falls in place; the body barely translates.
    //
    //   CELEBRATION — while the fan holds its cheer pose (set by CrowdSpawner during the goal
    //   window) it swaps to ENERGETIC values: a big scale bounce (0.10), a real rotation wobble
    //   (7°), a livelier hop (0.03) and ~5× faster oscillation. Reverts to calm on its own when the
    //   window ends. Reads as genuinely excited, not "idle but bigger".
    //
    // ON TOP of either mood, front/back fans (`trackBall`) add a subtle Z-tilt LEANING toward the
    // ball's side — a smooth-clamped ±MaxTiltDeg based on the horizontal ball offset (ball X cached
    // once per frame by CrowdSpawner). An "illusion of following the game", not a head-turn. Side
    // fans don't track (they're ~90°-rotated art; different math, out of scope).
    //
    // Desync note: the per-fan PHASE is fully random (0..2π); the calm band is widened to
    // ~0.18-0.55 Hz and the sway has its own phase, so neighbours breathe out of step (a shared
    // narrow band read as one collective pulse before).
    class FanIdle : MonoBehaviour
    {
        // Calm idle.
        const float BobFraction = 0.004f;    // vertical position bob, of the fan's rendered height
        const float ScalePulseFrac = 0.025f; // Y-scale breath, of the fan's base Y scale (~2.5%)
        const float SwayDegrees = 0.6f;
        // Energetic celebration (active only while celebrating).
        const float CeleBobFraction = 0.03f;    // livelier hop
        const float CeleScalePulseFrac = 0.10f; // big scale bounce
        const float CeleSwayDegrees = 3.5f;      // rotation wobble (dialled back from 7° — was too much)
        const float CeleRateMult = 5f;           // ~5× faster oscillation
        // Ball-tracking tilt (front/back only).
        const float MaxTiltDeg = 6f; // subtle lean cap (dev asked 5-8°)
        const float TiltRef = 6f;    // world units; a ball ~this far off → ~0.71 of the max tilt

        public bool celebrating; // driven by CrowdSpawner during the cheer window
        public bool trackBall;   // front + back fans lean toward the ball; side fans don't

        Vector3 basePos;
        Vector3 baseScale;
        Quaternion baseRot;
        float heightWorld; // this fan's rendered height (world units)
        float speed;       // rad/s
        float phase;       // random breath offset so neighbours never sync
        float swayPhase;   // independent sway offset

        void Start()
        {
            basePos = transform.position;
            baseRot = transform.rotation;
            baseScale = transform.localScale;
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            heightWorld = sr != null ? sr.bounds.size.y : 0.5f;
            speed = Random.Range(0.18f, 0.55f) * Mathf.PI * 2f; // calm breath-rate band
            phase = Random.value * Mathf.PI * 2f;
            swayPhase = Random.value * Mathf.PI * 2f;
        }

        void Update()
        {
            // Energetic while celebrating, calm otherwise.
            float rate   = celebrating ? CeleRateMult : 1f;
            float bobF   = celebrating ? CeleBobFraction : BobFraction;
            float scaleF = celebrating ? CeleScalePulseFrac : ScalePulseFrac;
            float swayD  = celebrating ? CeleSwayDegrees : SwayDegrees;

            float t = Time.time * speed * rate;
            float breath = Mathf.Sin(t + phase);          // one in/out cycle, -1..1
            float sway = Mathf.Sin(t * 0.8f + swayPhase) * swayD;

            // Subtle ball-tracking lean (front/back only), layered ON the sway. Reads the ball X
            // cached once per frame by CrowdSpawner — no per-fan MatchContext lookup.
            float tilt = 0f;
            if (trackBall && sBallValid)
            {
                float dx = sBallX - basePos.x;                                     // ball right of fan → dx > 0
                tilt = -MaxTiltDeg * dx / Mathf.Sqrt(dx * dx + TiltRef * TiltRef); // lean toward it, smooth-clamped ±Max
            }

            transform.position = basePos + new Vector3(0f, breath * heightWorld * bobF, 0f);
            transform.localScale = new Vector3(baseScale.x, baseScale.y * (1f + breath * scaleF), baseScale.z);
            transform.rotation = baseRot * Quaternion.Euler(0f, 0f, sway + tilt);
        }
    }
}
