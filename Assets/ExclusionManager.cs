using System.Collections.Generic;
using UnityEngine;
using TMPro;

// Water-polo fouls + exclusions (plan B16.9). Singleton like MatchContext.
//
// - A FAILED steal is an ordinary foul: the carrier keeps the ball and the offender
//   gets a short steal lockout.
// - `foulsForExclusion` fouls within `foulWindowSeconds` → the offender is EXCLUDED
//   for `exclusionSeconds`: removed from its TeamSide.members (formation + AI auto-
//   adapt — no special man-up/man-down code), parked in its own goal corner, inert.
// - After `maxExclusionsPerPlayer` exclusions the player is removed for good (disabled).
// - If permanent removals drop a team below `minPlayersToContinue`, the match is
//   forfeited via MatchTimer (the other team wins).
public class ExclusionManager : MonoBehaviour
{
    public static ExclusionManager Instance { get; private set; }

    [Header("Foul / exclusion rules")]
    [SerializeField] private float foulWindowSeconds = 10f;   // fouls are counted within this window
    [SerializeField] private int foulsForExclusion = 2;       // this many fouls in the window → exclusion
    [SerializeField] private float exclusionSeconds = 5f;     // time spent excluded before returning
    [SerializeField] private int maxExclusionsPerPlayer = 3;  // this many exclusions → permanent removal
    [SerializeField] private int minPlayersToContinue = 4;    // below this (after removals) → forfeit
    [SerializeField] private float foulStealLockout = 1.5f;   // steal lockout applied to a fouling agent
    [SerializeField] private float penaltyZoneX = 4.28f;      // victim |x| ≥ this (goal-side) → penalty, not exclusion
    [SerializeField] private bool centerFoulBoost = true;     // fouls on an inside-water Centre escalate faster

    [Header("References")]
    [SerializeField] private MatchTimer matchTimer;           // to end the match on a forfeit
    [SerializeField] private TMP_Text exclusionText;          // HUD countdowns, e.g. "YOU EXC: 4.2"

    // cached from MatchContext (so no extra Inspector wiring of teams)
    private TeamSide playerTeam;
    private TeamSide botTeam;

    // one active temporary exclusion (the player returns after endTime)
    private class Exclusion
    {
        public Transform agent;
        public TeamSide team;
        public int memberIndex; // original slot in team.members, restored on return
        public float endTime;
    }

    private readonly List<Exclusion> activeExclusions = new List<Exclusion>();
    private readonly Dictionary<Transform, List<float>> foulTimes = new Dictionary<Transform, List<float>>();
    private readonly Dictionary<Transform, int> exclusionCount = new Dictionary<Transform, int>();
    private readonly HashSet<Transform> excludedNow = new HashSet<Transform>();    // temporarily out
    private readonly HashSet<Transform> permanentlyOut = new HashSet<Transform>(); // gone for good
    private readonly Dictionary<TeamSide, Transform[]> originalRoster = new Dictionary<TeamSide, Transform[]>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx != null)
        {
            playerTeam = ctx.PlayerTeam;
            botTeam = ctx.BotTeam;
            Snapshot(playerTeam);
            Snapshot(botTeam);
        }
        if (exclusionText != null) exclusionText.enabled = false;
    }

    void Snapshot(TeamSide team)
    {
        if (team != null && team.members != null)
            originalRoster[team] = (Transform[])team.members.Clone();
    }

    void Update()
    {
        // Return temporarily-excluded players when their time is up.
        for (int i = activeExclusions.Count - 1; i >= 0; i--)
        {
            Exclusion e = activeExclusions[i];
            if (Time.time < e.endTime) continue;

            // restore to its ORIGINAL slot so roster order is preserved
            if (e.agent != null && e.team != null && e.team.members != null &&
                e.memberIndex >= 0 && e.memberIndex < e.team.members.Length)
                e.team.members[e.memberIndex] = e.agent;

            if (e.agent != null) excludedNow.Remove(e.agent);
            activeExclusions.RemoveAt(i);
        }

        UpdateHud();
    }

    // ---------- public API ----------

    // True while an agent is excluded (temporarily) or permanently removed.
    public bool IsExcluded(Transform t)
        => t != null && (excludedNow.Contains(t) || permanentlyOut.Contains(t));

    // How many of `team`'s original roster are currently out — used by the brain for
    // man-up (enemy short) / man-down (we're short) tactical shapes.
    public int ExcludedCount(TeamSide team)
    {
        if (team == null || !originalRoster.TryGetValue(team, out Transform[] roster)) return 0;
        int n = 0;
        foreach (Transform t in roster)
            if (t != null && (excludedNow.Contains(t) || permanentlyOut.Contains(t))) n++;
        return n;
    }

    // Called on EVERY failed steal. `victim` = the carrier that was fouled. Carrier keeps
    // the ball; offender is locked out. An ordinary foul gives the victim a FREE THROW;
    // enough fouls escalate to an exclusion — or a PENALTY if the victim was in the 2m zone.
    public void ReportFoul(Transform offender, TeamSide team, Transform victim)
    {
        if (offender == null) return;

        ApplyStealLockout(offender);

        if (!foulTimes.TryGetValue(offender, out List<float> times))
        {
            times = new List<float>();
            foulTimes[offender] = times;
        }
        times.Add(Time.time);
        times.RemoveAll(t => Time.time - t > foulWindowSeconds);

        // Feature 5: fouling the enemy CENTRE while he holds inside water counts as an
        // extra (virtual) foul, so Centres draw exclusions/penalties faster — the payoff
        // for fighting for (and feeding) inside position.
        if (centerFoulBoost && victim != null)
        {
            MatchContext mctx = MatchContext.Instance;
            TeamSide victimTeam = mctx != null ? mctx.EnemyOf(team) : null;
            if (victimTeam != null && victimTeam.Contains(victim) &&
                victimTeam.RoleOf(victim) == TeamSide.Role.Center &&
                TeamSide.IsInsideTwoMeter(victim, victimTeam))
                times.Add(Time.time - 0.1f); // a virtual foul just inside the window
        }

        if (times.Count >= foulsForExclusion)
            Escalate(offender, team, victim); // exclusion, or penalty if in the 2m zone
        else
            FreeThrow(team, victim);           // ordinary foul
    }

    // Ordinary foul → free throw to the fouled (victim's) team: shot clock pauses and the
    // carrier can't be stolen from until they act.
    void FreeThrow(TeamSide offenderTeam, Transform victim)
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx != null && victim != null) ctx.StartFreeThrow(victim);

        TeamSide victimTeam = ctx != null ? ctx.EnemyOf(offenderTeam) : null;
        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Foul - free throw " + (victimTeam == playerTeam ? "YOU" : "BOT"));
    }

    // Exclusion-level foul: a PENALTY if the victim was inside the attacking 2m zone,
    // otherwise the usual temporary/permanent exclusion.
    void Escalate(Transform offender, TeamSide team, Transform victim)
    {
        bool penalty = false;
        if (victim != null && team != null && team.defendGoal != null)
        {
            float sign = Mathf.Sign(team.defendGoal.position.x);
            if (sign == 0f) sign = 1f;
            penalty = victim.position.x * sign >= penaltyZoneX;
        }

        if (penalty) AwardPenalty(offender, team, victim);
        else Exclude(offender, team);
    }

    // Penalty: the offender does NOT sit out (the penalty shot is the punishment), but the
    // exclusion bookkeeping — count, foul reset, permanent-removal-at-max + forfeit — still
    // applies exactly as a normal exclusion would.
    void AwardPenalty(Transform offender, TeamSide team, Transform victim)
    {
        foulTimes.Remove(offender);

        int count = (exclusionCount.TryGetValue(offender, out int c) ? c : 0) + 1;
        exclusionCount[offender] = count;

        if (count >= maxExclusionsPerPlayer)
        {
            // max reached → permanent removal still applies (roster slot null + disable + forfeit)
            permanentlyOut.Add(offender);
            int idx = MemberIndex(team, offender);
            if (idx >= 0) team.members[idx] = null;
            offender.gameObject.SetActive(false);
            if (ActiveCount(team) < minPlayersToContinue) Forfeit(team);
        }
        // else: NO temporary exclusion — no roster null, no corner, no excludedNow entry.

        MatchContext ctx = MatchContext.Instance;
        TeamSide attackingTeam = ctx != null ? ctx.EnemyOf(team) : null;

        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("PENALTY - " + (attackingTeam == playerTeam ? "YOU" : "BOT"));

        if (PenaltyManager.Instance != null && attackingTeam != null && victim != null)
            PenaltyManager.Instance.StartPenalty(victim, attackingTeam);
    }

    // ---------- internals ----------

    void ApplyStealLockout(Transform offender)
    {
        IAgentBody body = offender.GetComponent<IAgentBody>();
        if (body != null) body.NextStealTime = Time.time + foulStealLockout;

        PlayerMovement pm = offender.GetComponent<PlayerMovement>();
        if (pm != null) pm.ApplyStealLockout(foulStealLockout);
    }

    void Exclude(Transform agent, TeamSide team)
    {
        if (agent == null || team == null) return;
        if (excludedNow.Contains(agent) || permanentlyOut.Contains(agent)) return; // already out

        int idx = MemberIndex(team, agent);

        DropBallHeldBy(agent);                  // drop the ball in place if carrying
        if (idx >= 0) team.members[idx] = null; // leave the roster (AI + formation auto-adapt)
        PlaceAtCorner(agent, team);             // park in the goal corner, stop moving
        foulTimes.Remove(agent);                // fresh foul slate after serving

        int count = (exclusionCount.TryGetValue(agent, out int c) ? c : 0) + 1;
        exclusionCount[agent] = count;

        if (count >= maxExclusionsPerPlayer)
        {
            // permanent removal: never returns, fully disabled
            permanentlyOut.Add(agent);
            agent.gameObject.SetActive(false);

            if (ActiveCount(team) < minPlayersToContinue)
                Forfeit(team);
        }
        else
        {
            excludedNow.Add(agent);
            activeExclusions.Add(new Exclusion
            {
                agent = agent,
                team = team,
                memberIndex = idx,
                endTime = Time.time + exclusionSeconds
            });
        }

        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Exclusion - " + team.teamName);

        // An exclusion by the DEFENDING team (the team without the ball) gives the
        // attacking team a fresh shot clock.
        MatchContext mc = MatchContext.Instance;
        if (mc != null && mc.PossessingTeam != null && mc.PossessingTeam != team &&
            ShotClock.Instance != null)
            ShotClock.Instance.ResetClock();
    }

    void DropBallHeldBy(Transform agent)
    {
        MatchContext ctx = MatchContext.Instance;
        if (ctx == null || ctx.Ball == null) return;
        if (ctx.Ball.transform.parent != agent) return; // not carrying

        IAgentBody body = agent.GetComponent<IAgentBody>();
        if (body != null) body.IsHolding = false;

        PlayerMovement pm = agent.GetComponent<PlayerMovement>();
        if (pm != null) { pm.ReleaseBall(); return; } // detaches the ball + clears possession

        // pure AI body: detach manually
        ctx.Ball.transform.SetParent(null);
        ctx.Ball.simulated = true;
        ctx.Ball.linearVelocity = Vector2.zero;
        ctx.SetPossession(null);
    }

    void PlaceAtCorner(Transform agent, TeamSide team)
    {
        float sign = (team != null && team.defendGoal != null) ? Mathf.Sign(team.defendGoal.position.x) : 1f;
        if (sign == 0f) sign = 1f;
        agent.position = new Vector3(sign * 7f, -4f, agent.position.z);

        Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;
    }

    int MemberIndex(TeamSide team, Transform agent)
    {
        if (team == null || team.members == null) return -1;
        for (int i = 0; i < team.members.Length; i++)
            if (team.members[i] == agent) return i;
        return -1;
    }

    // Players still available to a team = original roster minus permanent removals.
    int ActiveCount(TeamSide team)
    {
        if (team == null || !originalRoster.TryGetValue(team, out Transform[] roster)) return int.MaxValue;
        int n = 0;
        foreach (Transform t in roster)
            if (t != null && !permanentlyOut.Contains(t)) n++;
        return n;
    }

    void Forfeit(TeamSide losingTeam)
    {
        if (EventFeed.Instance != null)
            EventFeed.Instance.AddEvent("Forfeit - " + (losingTeam != null ? losingTeam.teamName : "?"));

        if (matchTimer == null) return;
        bool playerWins = losingTeam != playerTeam; // the OTHER team wins
        matchTimer.ForfeitMatch(playerWins);
    }

    void UpdateHud()
    {
        if (exclusionText == null) return;

        float youMax = -1f, botMax = -1f;
        foreach (Exclusion e in activeExclusions)
        {
            float rem = e.endTime - Time.time;
            if (rem < 0f) continue;
            if (e.team == playerTeam) { if (rem > youMax) youMax = rem; }
            else if (e.team == botTeam) { if (rem > botMax) botMax = rem; }
        }

        if (youMax < 0f && botMax < 0f) { exclusionText.enabled = false; return; }

        string s = "";
        if (youMax >= 0f) s += "YOU EXC: " + youMax.ToString("0.0");
        if (botMax >= 0f) { if (s.Length > 0) s += "   "; s += "BOT EXC: " + botMax.ToString("0.0"); }

        exclusionText.enabled = true;
        exclusionText.text = s;
    }
}
