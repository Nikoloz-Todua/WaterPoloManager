using UnityEngine;

// Runtime-addressable frame list for BallFlight's loose-ball water ripple.
// Keeping the supplied sheet in Assets/Sprites/Effects while this asset lives in Resources
// lets BallFlight bind the frames even though it is added to the Ball at runtime.
public sealed class BallDropRippleFrameSet : ScriptableObject
{
    public Sprite[] frames = new Sprite[9];
}
