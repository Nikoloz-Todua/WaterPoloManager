using UnityEngine;
using TMPro;

// Quarter-start sprint duel (plan B16.2). Singleton. Called by MatchTimer at the start
// of every quarter (incl. Q1). Lines all players up on their own goal lines and freezes
// play; after a short whistle delay only the two sprinters (each team's first available
// member) race to the centre ball. The bot sprinter swims at a fixed speed; the human
// sprinter auto-swims at a lower base speed that each Space tap boosts up to a cap.
// First to reach the ball grabs it; play + shot clock then resume.
//
// During the duel everyone is frozen via MatchContext.PlayFrozen (their own FixedUpdate
// goes inert), so this script moves the two sprinters directly by Rigidbody2D.position
// — no fight with the frozen bodies, and the AI brain never runs for them.
public class SprintDuel : MonoBehaviour
{
    public static SprintDuel Instance { get; private set; }

    [Header("Timing")]
    [SerializeField] private float whistleDelay = 1f;   // lineup freeze before "GO!"

    [Header("Sprinter speeds")]
    [SerializeField] private float sprintSpeed = 4f;     // bot sprinter (fixed)
    [SerializeField] private float baseHumanSprint = 3f; // human sprinter base
    [SerializeField] private float maxHumanSprint = 5f;  // human sprinter cap
    [SerializeField] private float tapBoost = 0.4f;      // speed gained per Space tap
    [SerializeField] private float boostDecay = 2f;      // speed decays back toward base (/sec)

    [Header("Geometry")]
    [SerializeField] private float grabDistance = 1f;    // first sprinter within this wins
    [SerializeField] private float lineInset = 1f;       // how far inward from the goal line
    [SerializeField] private float lineupYSpread = 3f;   // vertical spread of the line-up

    [Header("Optional UI")]
    [SerializeField] private TMP_Text duelText;          // "READY" / "GO!" (optional)

    private enum State { Idle, Lineup, Racing }
    private State state = State.Idle;

    private Transform humanSprinter;
    private Transform botSprinter;
    private float humanSpeed;
    private float timer;

    void Awake() { Instance = this; }

    // Begin a fresh duel: ball to centre, everyone lined up + frozen, whistle pending.
    public void StartDuel()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null) return;

        TeamSide pt = ctx.PlayerTeam;
        TeamSide bt = ctx.BotTeam;

        // ball to centre, loose
        if (ctx.Ball != null)
        {
            ctx.Ball.transform.SetParent(null);
            ctx.Ball.simulated = true;
            ctx.Ball.linearVelocity = Vector2.zero;
            ctx.Ball.position = Vector2.zero;
        }
        ctx.SetPossession(null);
        ctx.ClearGrabBan();

        LineUp(pt);
        LineUp(bt);

        humanSprinter = FirstMember(pt);
        botSprinter = FirstMember(bt);
        PlaceSprinterCentre(pt, humanSprinter);
        PlaceSprinterCentre(bt, botSprinter);

        // nobody to race → just play on
        if (humanSprinter == null && botSprinter == null) { state = State.Idle; return; }

        humanSpeed = baseHumanSprint;
        timer = whistleDelay;
        state = State.Lineup;
        ctx.FreezeAll();

        if (duelText != null) { duelText.enabled = true; duelText.text = "READY"; }
    }

    void Update()
    {
        if (state == State.Idle) return;

        if (state == State.Lineup)
        {
            timer -= Time.deltaTime;
            if (duelText != null) duelText.text = "READY " + Mathf.CeilToInt(Mathf.Max(0f, timer));
            if (timer <= 0f)
            {
                state = State.Racing;
                if (duelText != null) duelText.text = "GO!";
            }
            return;
        }

        // Racing: read Space taps here (Update is reliable for key-down) → speed bursts.
        if (state == State.Racing && Input.GetKeyDown(KeyCode.Space))
            humanSpeed = Mathf.Min(humanSpeed + tapBoost, maxHumanSprint);
    }

    void FixedUpdate()
    {
        if (state != State.Racing) return;

        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || ctx.Ball == null) return;

        Vector2 ballPos = ctx.Ball.position;

        humanSpeed = Mathf.MoveTowards(humanSpeed, baseHumanSprint, boostDecay * Time.fixedDeltaTime);

        MoveSprinter(humanSprinter, ballPos, humanSpeed);
        MoveSprinter(botSprinter, ballPos, sprintSpeed);

        float dH = Dist(humanSprinter, ballPos);
        float dB = Dist(botSprinter, ballPos);
        bool humanWins = dH <= grabDistance && (dB > grabDistance || dH <= dB);
        bool botWins = !humanWins && dB <= grabDistance;

        if (humanWins) Finish(ctx, humanSprinter, ctx.PlayerTeam);
        else if (botWins) Finish(ctx, botSprinter, ctx.BotTeam);
    }

    void Finish(MatchContext ctx, Transform winner, TeamSide team)
    {
        ctx.GiveBallTo(winner, team);
        ctx.Unfreeze();
        if (ShotClock.Instance != null) ShotClock.Instance.ResetClock();
        if (duelText != null) duelText.enabled = false;
        state = State.Idle;
    }

    void MoveSprinter(Transform s, Vector2 ballPos, float speed)
    {
        if (s == null) return;
        Rigidbody2D rb = s.GetComponent<Rigidbody2D>();
        Vector2 cur = rb != null ? rb.position : (Vector2)s.position;
        Vector2 next = Vector2.MoveTowards(cur, ballPos, speed * Time.fixedDeltaTime);
        if (rb != null) rb.position = next; else s.position = next; // position-based: immune to the freeze
    }

    float Dist(Transform s, Vector2 p) => s == null ? Mathf.Infinity : Vector2.Distance(s.position, p);

    // Line every available member up along their own (defended) goal line, spread on y.
    void LineUp(TeamSide team)
    {
        if (team == null || team.members == null || team.defendGoal == null) return;

        float x = GoalLineX(team);
        int n = team.members.Length;
        for (int i = 0; i < n; i++)
        {
            Transform m = team.members[i];
            if (m == null) continue; // excluded/empty slot
            float t = n > 1 ? ((float)i / (n - 1)) * 2f - 1f : 0f;
            m.position = new Vector3(x, t * lineupYSpread, m.position.z);

            Rigidbody2D rb = m.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }
    }

    // Put the sprinter at the centre of its goal line for a fair straight race.
    void PlaceSprinterCentre(TeamSide team, Transform sprinter)
    {
        if (team == null || sprinter == null || team.defendGoal == null) return;
        sprinter.position = new Vector3(GoalLineX(team), 0f, sprinter.position.z);
    }

    float GoalLineX(TeamSide team)
    {
        float goalX = team.defendGoal.position.x;
        float sign = goalX == 0f ? 1f : Mathf.Sign(goalX);
        return goalX - sign * lineInset; // pulled slightly inward toward centre
    }

    Transform FirstMember(TeamSide team)
    {
        if (team == null || team.members == null) return null;
        foreach (Transform m in team.members)
            if (m != null) return m; // excluded members are null → first non-excluded
        return null;
    }
}
