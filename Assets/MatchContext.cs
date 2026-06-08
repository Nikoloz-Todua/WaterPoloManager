using UnityEngine;

// The single shared "truth" about the match that every AI reads.
// Knows where the ball is and which team currently has possession.
public class MatchContext : MonoBehaviour
{
    public static MatchContext Instance { get; private set; }

    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private TeamSide playerTeam; // your side
    [SerializeField] private TeamSide botTeam;    // the bots' side

    // who currently holds the ball: null = loose
    public TeamSide PossessingTeam { get; private set; }

    public Vector2 BallPosition => ball != null ? ball.position : Vector2.zero;
    public Rigidbody2D Ball => ball;
    public TeamSide PlayerTeam => playerTeam;
    public TeamSide BotTeam => botTeam;

    void Awake()
    {
        Instance = this;
    }

    // called by a player/bot when it grabs or releases the ball
    public void SetPossession(TeamSide team)
    {
        PossessingTeam = team;
    }

    public bool TeamHasBall(TeamSide team) => PossessingTeam == team;
    public bool BallIsLoose => PossessingTeam == null;
}