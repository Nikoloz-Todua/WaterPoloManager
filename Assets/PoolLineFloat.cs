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

    private Vector3 basePos;     // captured on Start; all motion offsets from these
    private Quaternion baseRot;
    private float bobOmega;      // radians/second
    private float swayOmega;
    private float bobPhase;
    private float swayPhase;

    void Start()
    {
        basePos = transform.localPosition;
        baseRot = transform.localRotation;

        // Independent speed + phase per axis per object → no two lines sync up.
        bobOmega  = Random.Range(minFrequency, maxFrequency) * 2f * Mathf.PI;
        swayOmega = Random.Range(minFrequency, maxFrequency) * 2f * Mathf.PI;
        bobPhase  = Random.Range(0f, 2f * Mathf.PI);
        swayPhase = Random.Range(0f, 2f * Mathf.PI);
    }

    void Update()
    {
        float bob  = Mathf.Sin(Time.time * bobOmega + bobPhase) * bobAmplitude;
        float sway = Mathf.Sin(Time.time * swayOmega + swayPhase) * swayDegrees;

        transform.localPosition = basePos + new Vector3(0f, bob, 0f);
        transform.localRotation = baseRot * Quaternion.Euler(0f, 0f, sway);
    }
}
