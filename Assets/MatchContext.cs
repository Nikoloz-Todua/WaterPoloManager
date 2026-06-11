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
    [Tooltip("On an ordinary foul, an AI free-throw carrier holds the ball this long, then takes its normal decision (pass/shoot/dribble).")]
    [SerializeField] private float freeThrowAIHoldSeconds = 3f;
    [Tooltip("Swimmers can't cross the goal line: their x is clamped to ±this during live play (ball/keepers excluded).")]
    [SerializeField] private float playerLimitX = 6.9f;
    [Tooltip("Counterattack window: winning the ball in your own half starts a fast-break for this long.")]
    [SerializeField] private float counterWindowSeconds = 4f;

    // who currently holds the ball: null = loose
    public TeamSide PossessingTeam { get; private set; }

    // the team that last touched the ball (grab / steal / shoot / pass release) —
    // used by the out-of-bounds rule to award possession to the OTHER team.
    public TeamSide LastTouchTeam { get; private set; }

    // the last SWIMMER to release the ball (shot / pass / drop) — lets ScoreManager
    // credit a goal to a specific player (Centre-goal tracking for the bot's adaptive D).
    public Transform LastReleaser { get; private set; }
    public void NoteRelease(Transform t) { if (t != null) LastReleaser = t; }

    // last time the ball was released (shot/passed/dropped); used for the grab cooldown
    private float lastReleaseTime = -10f;

    // While true, all swimmers freeze (sprint-duel line-up/race, goal settle, etc).
    public bool PlayFrozen { get; private set; }

    // A team banned from grabbing the loose ball until the OTHER team touches it
    // (shot-clock turnover). null = no ban.
    public TeamSide GrabBannedTeam { get; private set; }

    // After a kickoff (duel win / goal restart) the carrying team's AI center makes one
    // pass back to its deepest teammate before normal play. Cleared on possession change.
    public bool KickoffPassPending { get; private set; }
    public TeamSide KickoffPassTeam { get; private set; }
    public float KickoffPassTime { get; private set; } // when the kickoff possession began

    // Free throw (ordinary foul): the fouled carrier is protected from steals and the
    // shot clock pauses until they act / move / the AI hold elapses.
    public bool FreeThrowActive { get; private set; }
    public Transform FreeThrowCarrier { get; private set; }
    public float FreeThrowStartTime { get; private set; }
    public float FreeThrowAIHoldSeconds => freeThrowAIHoldSeconds;

    // Keeper hold (Part 1): a keeper collecting the ball is NOT a possession change — the
    // shot clock keeps ticking for the holding team until the keeper distributes.
    public bool KeeperHolding { get; private set; }
    public TeamSide KeeperHoldTeam { get; private set; }

    // Counterattack window (Part 2): a team that just won the ball in its own half.
    public TeamSide CounterTeam { get; private set; }
    public float CounterUntilTime { get; private set; }

    public Vector2 BallPosition => ball != null ? ball.position : Vector2.zero;
    public Rigidbody2D Ball => ball;
    public TeamSide PlayerTeam => playerTeam;
    public TeamSide BotTeam => botTeam;
    public float PlayerLimitX => playerLimitX;

    void Awake()
    {
        Instance = this;
        lastReleaseTime = -10f; // allow an immediate grab at kickoff
    }

    // called by a player/bot when it grabs (team) or releases (null) the ball
    public void SetPossession(TeamSide team)
    {
        TeamSide prev = PossessingTeam;
        TeamSide prevTouch = LastTouchTeam; // who last touched it BEFORE this grab/release

        // remember the last toucher: a grab/steal = the new team; a release (null) = the
        // team that just let go (read the OLD possessor before overwriting it).
        if (team != null) LastTouchTeam = team;
        else if (prev != null) LastTouchTeam = prev;

        PossessingTeam = team;
        if (team == null) lastReleaseTime = Time.time;       // ball was just released → start the cooldown
        else if (team != GrabBannedTeam) GrabBannedTeam = null; // the OTHER team got it → lift the turnover ban

        // a pending kickoff pass is void once possession leaves the kicking team
        if (KickoffPassPending && team != KickoffPassTeam) ClearKickoffPass();

        // any possession change / release ends a free throw (carrier passed/shot/dropped)
        ClearFreeThrow();

        // counterattack: a real WIN (ball last touched by the OTHER team) inside our own
        // half starts a fast break — NOT a same-team pass reception.
        if (team != null && prevTouch != null && prevTouch != team && !PlayFrozen && !KeeperHolding &&
            ball != null && team.defendGoal != null)
        {
            float sign = Mathf.Sign(team.defendGoal.position.x);
            if (sign * ball.position.x > 0f) StartCounter(team);
        }
    }

    // ---- match-flow gates ----

    public void FreezeAll() { PlayFrozen = true; }
    public void Unfreeze()  { PlayFrozen = false; }

    public void SetGrabBan(TeamSide team) { GrabBannedTeam = team; }
    public void ClearGrabBan() { GrabBannedTeam = null; }

    // A team may grab unless it is the one serving a turnover ban.
    public bool CanGrab(TeamSide team) => GrabBannedTeam == null || team != GrabBannedTeam;

    public void SetKickoffPass(TeamSide team)
    {
        KickoffPassPending = true;
        KickoffPassTeam = team;
        KickoffPassTime = Time.time;
    }

    public void ClearKickoffPass()
    {
        KickoffPassPending = false;
        KickoffPassTeam = null;
    }

    public void StartFreeThrow(Transform carrier)
    {
        FreeThrowActive = true;
        FreeThrowCarrier = carrier;
        FreeThrowStartTime = Time.time;
    }

    public void ClearFreeThrow()
    {
        FreeThrowActive = false;
        FreeThrowCarrier = null;
    }

    // ---- keeper hold (Part 1) ----
    public void SetKeeperHold(TeamSide team) { KeeperHolding = true; KeeperHoldTeam = team; }
    public void ClearKeeperHold() { KeeperHolding = false; KeeperHoldTeam = null; }

    // ---- counterattack window (Part 2) ----
    public void StartCounter(TeamSide team) { CounterTeam = team; CounterUntilTime = Time.time + counterWindowSeconds; }
    public bool CounterActiveFor(TeamSide team) => team != null && team == CounterTeam && Time.time < CounterUntilTime;

    // Physical-touch attribution (used by the out-of-bounds rules so a deflection off an
    // opponent is credited to them). Does NOT change possession.
    public void NoteTouch(TeamSide team)
    {
        if (team != null) LastTouchTeam = team;
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

    // Hand the ball to a specific holder (sprint-duel winner, kickoff centre). Reuses the
    // player/AI hold mechanics so the ball is parented and possession set consistently.
    public void GiveBallTo(Transform holder, TeamSide team)
    {
        if (ball == null || holder == null) return;

        PlayerMovement pm = holder.GetComponent<PlayerMovement>();
        if (pm != null) { pm.TakeOverHeldBall(); return; } // parents ball + sets PlayerTeam possession + isHolding

        IAgentBody body = holder.GetComponent<IAgentBody>();
        ball.simulated = false;
        ball.linearVelocity = Vector2.zero;
        ball.transform.SetParent(holder);
        Vector2 dir = body != null ? body.LastDirection : Vector2.right;
        float off = body != null ? body.HoldOffset : 0.6f;
        ball.transform.localPosition = (Vector3)(dir * off);
        if (body != null) body.IsHolding = true;
        SetPossession(team);
    }

    // Halftime: swap both teams' attack/defend goals so they attack the opposite ends.
    // Keepers (bound to their own physical goal transform) are unaffected.
    public void SwapEnds()
    {
        SwapGoals(playerTeam);
        SwapGoals(botTeam);
    }

    static void SwapGoals(TeamSide t)
    {
        if (t == null) return;
        Transform tmp = t.attackGoal;
        t.attackGoal = t.defendGoal;
        t.defendGoal = tmp;
    }
}
