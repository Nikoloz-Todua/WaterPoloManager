using UnityEngine;

// Shared compressed-countdown helper (2026-07-09). A countdown whose DISPLAYED number runs
// on a different scale than real time: the HUD shows `displayDuration` draining to 0 while
// only `realDuration` real seconds actually pass — the FIFA-style faster-ticking big clock.
//
// RULE: all gameplay decisions keep reading REAL time (Tick / RealRemaining / IsComplete);
// only text that gets PRINTED uses DisplayValue / DisplayElapsed. Used by MatchTimer
// (shows 8:00 over 90s real), ShotClock (shows 30 over 15s real) and ExclusionManager
// (shows 20 over 7.5s real) — one scale conversion, not three hand-rolled factors.
public struct CompressedTimer
{
    private readonly float displayDuration;
    private readonly float realDuration;
    private float realRemaining;

    public CompressedTimer(float displayDuration, float realDuration)
    {
        this.displayDuration = Mathf.Max(0f, displayDuration);
        this.realDuration = Mathf.Max(0.0001f, realDuration);
        realRemaining = this.realDuration;
    }

    // Back to full. (A freshly constructed timer is already full.)
    public void Reset() { realRemaining = realDuration; }

    // Advance by real elapsed seconds; the owner simply doesn't call this while paused/frozen.
    public void Tick(float deltaTime) { realRemaining = Mathf.Max(0f, realRemaining - deltaTime); }

    public bool IsComplete => realRemaining <= 0f;

    // REAL seconds left — gameplay keys off this scale, never off DisplayValue.
    public float RealRemaining => realRemaining;

    // The number to PRINT: real remaining re-scaled onto the display range
    // (display 30 / real 15 → the HUD sees 2 "seconds" tick away per real second).
    public float DisplayValue => realDuration <= 0f ? 0f : realRemaining * (displayDuration / realDuration);

    // Display-scale seconds already ELAPSED (for count-up readouts / event-feed timestamps).
    public float DisplayElapsed => displayDuration - DisplayValue;
}
