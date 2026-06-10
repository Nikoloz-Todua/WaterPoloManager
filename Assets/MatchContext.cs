using UnityEngine;

// The single shared "truth" about the match that every AI reads.
// Knows where the ball is and which team currently has possession.
public class MatchContext : MonoBehaviour
{
    public static MatchContext Instance { get; private set; }

    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private TeamSide playerTeam; // your side
    [SerializeField] private TeamSide botTeam;    // the bots' side

    [Header("Ball handling")]
    [Tooltip("After a shot/pass/drop the ball can't be re-grabbed for this long, so it has time to travel.")]
    [SerializeField] private float releaseGrabDelay = 0.35f;

    // who currently holds the ball: null = loose
    public TeamSide PossessingTeam { get; private set; }

    // last time the ball was released (shot/passed/dropped); used for the grab cooldown
    private float lastReleaseTime = -10f;

    public Vector2 BallPosition => ball != null ? ball.position : Vector2.zero;
    public Rigidbody2D Ball => ball;
    public TeamSide PlayerTeam => playerTeam;
    public TeamSide BotTeam => botTeam;

    void Awake()
    {
        Instance = this;
        lastReleaseTime = -10f; // allow an immediate grab at kickoff
    }

    // called by a player/bot when it grabs (team) or releases (null) the ball
    public void SetPossession(TeamSide team)
    {
        PossessingTeam = team;
        if (team == null) lastReleaseTime = Time.time; // ball was just released → start the cooldown
    }

    public bool TeamHasBall(TeamSide team) => PossessingTeam == team;
    public bool BallIsLoose => PossessingTeam == null;

    // Loose AND past the post-release cooldown → safe for anyone to collect.
    // This is what stops a shooter/teammate from instantly snatching back a shot or pass.
    public bool BallGrabbable => PossessingTeam == null && (Time.time - lastReleaseTime) >= releaseGrabDelay;

    // given a team, returns the other team
    public TeamSide EnemyOf(TeamSide team)
    {
        if (team == playerTeam) return botTeam;
        if (team == botTeam) return playerTeam;
        return null;
    }

    // Force whoever currently holds the ball to drop it in place (shot-clock turnover,
    // exclusion, etc.). Reuses the same release path the player/AI use so there's one
    // consistent way the ball comes loose.
    public void ForceDropHeldBall()
    {
        if (ball == null) return;

        Transform carrier = ball.transform.parent;
        if (carrier == null) { SetPossession(null); return; }

        IAgentBody body = carrier.GetComponent<IAgentBody>();
        if (body != null) body.IsHolding = false;

        PlayerMovement pm = carrier.GetComponent<PlayerMovement>();
        if (pm != null) { pm.ReleaseBall(); return; } // detaches the ball + clears possession

        // pure AI body: detach manually
        ball.transform.SetParent(null);
        ball.simulated = true;
        ball.linearVelocity = Vector2.zero;
        SetPossession(null);
    }
}
