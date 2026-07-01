using System.Collections.Generic;
using UnityEngine;

// Session-persistent state for one division's tournament. A plain static holder (not a MonoBehaviour)
// so it survives scene transitions within a play session without saving to disk. Team 0 is always
// the player.
//
// Format (identical for all four divisions, just a different random draw of clubs):
//   • Group stage: 16 teams in two groups of 8 (player in Group A, index 0). Each team plays the
//     others in its group once — 7 rounds. Recording the player's result also simulates that round's
//     other fixtures in BOTH groups, so both tables evolve in lockstep.
//   • Knockout: top 4 of each group seed a single-elimination bracket —
//     QF: A1 vs B4, A2 vs B3, B1 vs A4, B2 vs A3 → SF (QF1 vs QF2 winners, QF3 vs QF4 winners) → Final.
//     Knockout matches can't draw: level placeholder scores get a sudden-death goal.
//   • If the player misses the top 4 or loses a knockout tie, the rest of the bracket simulates
//     instantly, so the tournament always ends Completed with a champion. Winning the Final makes
//     the player champion (NavigationManager persists the next-division unlock).
public class LeagueSeason
{
    public enum Phase { GroupStage, Quarterfinal, Semifinal, Final, Completed }

    // One knockout tie. Team slots hold global team indices; -1 = not yet decided (bracket TBD).
    public class KnockoutMatch
    {
        public int teamA = -1, teamB = -1;
        public int scoreA, scoreB;
        public bool played;
        public int Winner => !played ? -1 : (scoreA > scoreB ? teamA : teamB);
        public bool Has(int team) => team >= 0 && (teamA == team || teamB == team);
    }

    public const int TeamCount = 16;
    public const int GroupSize = 8;
    public const int PlayerIndex = 0;   // player's group is A (indices 0..7); Group B is 8..15
    public const int GroupRounds = 7;

    // The active season (the one being viewed). Persists across scene loads within the session.
    public static LeagueSeason Current;

    // One retained tournament per competition, so switching competitions and coming back keeps progress.
    static readonly Dictionary<int, LeagueSeason> cache = new Dictionary<int, LeagueSeason>();

    public int competitionIndex;
    public Phase phase = Phase.GroupStage;
    public int groupRound;              // completed group rounds for everyone (0..7)
    public string eliminatedIn;         // null while the player is alive; round name once knocked out

    public readonly string[] teams = new string[TeamCount];   // [0] = player's club
    public readonly int[] played = new int[TeamCount];        // group-stage stats only
    public readonly int[] won = new int[TeamCount];
    public readonly int[] drawn = new int[TeamCount];
    public readonly int[] lost = new int[TeamCount];
    public readonly int[] gf = new int[TeamCount];             // goals for
    public readonly int[] ga = new int[TeamCount];             // goals against
    public readonly int[] stars = new int[TeamCount];          // 1–5 star rating (display only)

    // Group round-robin fixtures [group, round, pair, side] as global team indices, built once with
    // the circle method (slot 0 stays fixed) — so pair 0 of group 0 always contains the player.
    readonly int[,,,] fixtures = new int[2, GroupRounds, GroupSize / 2, 2];

    public readonly KnockoutMatch[] quarterfinals =
        { new KnockoutMatch(), new KnockoutMatch(), new KnockoutMatch(), new KnockoutMatch() };
    public readonly KnockoutMatch[] semifinals = { new KnockoutMatch(), new KnockoutMatch() };
    readonly KnockoutMatch[] finalRound = { new KnockoutMatch() }; // 1-element array so rounds share code
    public KnockoutMatch Final => finalRound[0];

    // 30 water-polo club names; 15 are drawn per division on first load of that competition.
    static readonly string[] ClubPool =
    {
        "Olympiacos WPC", "Pro Recco", "Ferencváros", "Jug Dubrovnik", "Barceloneta", "Szolnoki",
        "Brescia", "Hannover WP", "Spandau 04", "Vouliagmeni", "Primorac", "Jadran Split",
        "Mladost Zagreb", "Wasserball Zürich", "AEK Athens", "Partizan Belgrade", "Dynamo Moscow",
        "CN Marseille", "Savona WP", "Ortigia",
        "Vasas Budapest", "Sabadell", "AN Brescia", "Torino 81", "Stella Rossa",
        "Noisy-le-Sec", "Catania WP", "Montpellier WP", "Crvena Zvezda", "Sintez Kazan"
    };

    public bool IsComplete => phase == Phase.Completed;
    public int Champion => Final.Winner;
    public bool PlayerIsChampion => IsComplete && Champion == PlayerIndex;
    public int GoalDiff(int i) => gf[i] - ga[i];
    public int Points(int i) => won[i] * 3 + drawn[i];
    public static int GroupOf(int team) => team / GroupSize;

    // The player's next opponent (global team index), across every phase. -1 once the run is over.
    public int NextOpponent
    {
        get
        {
            if (IsComplete) return -1;
            if (phase == Phase.GroupStage)
            {
                int a = fixtures[0, groupRound, 0, 0], b = fixtures[0, groupRound, 0, 1];
                return a == PlayerIndex ? b : a;
            }
            KnockoutMatch m = PlayerMatch(RoundMatches(phase));
            if (m == null || m.played) return -1;
            return m.teamA == PlayerIndex ? m.teamB : m.teamA;
        }
    }

    public string NextOpponentName
    {
        get { int o = NextOpponent; return o >= 0 ? teams[o] : null; }
    }

    // Pre-match header line: which match of the tournament the player is about to play.
    public string MatchLabel
    {
        get
        {
            switch (phase)
            {
                case Phase.GroupStage: return "GROUP MATCH " + (groupRound + 1) + " OF " + GroupRounds;
                case Phase.Quarterfinal: return "QUARTERFINAL";
                case Phase.Semifinal: return "SEMIFINAL";
                case Phase.Final: return "FINAL";
                default: return "TOURNAMENT COMPLETE";
            }
        }
    }

    public static string RoundName(Phase p)
    {
        switch (p)
        {
            case Phase.GroupStage: return "GROUP STAGE";
            case Phase.Quarterfinal: return "QUARTERFINALS";
            case Phase.Semifinal: return "SEMIFINALS";
            default: return "FINAL";
        }
    }

    // Make sure a tournament exists for this competition. Reuses the running one (so progress is
    // kept when re-opening the same competition); builds a fresh draw the first time.
    public static void Ensure(int competitionIndex, string playerTeamName)
    {
        string player = string.IsNullOrEmpty(playerTeamName) ? "MY TEAM" : playerTeamName;
        if (!cache.TryGetValue(competitionIndex, out LeagueSeason s))
        {
            s = Create(competitionIndex, player);
            cache[competitionIndex] = s;
        }
        s.teams[PlayerIndex] = player; // keep the club name fresh
        Current = s;
    }

    static LeagueSeason Create(int competitionIndex, string player)
    {
        LeagueSeason s = new LeagueSeason { competitionIndex = competitionIndex };
        s.teams[PlayerIndex] = player;
        s.stars[PlayerIndex] = 3;

        // 15 distinct opponents drawn from the pool.
        List<string> pool = new List<string>(ClubPool);
        for (int i = 1; i < TeamCount; i++)
        {
            int r = Random.Range(0, pool.Count);
            s.teams[i] = pool[r];
            pool.RemoveAt(r);
            s.stars[i] = Random.Range(2, 6); // 2–5
        }

        s.BuildGroupFixtures(0, 0);
        s.BuildGroupFixtures(1, GroupSize);
        return s;
    }

    // Circle-method round robin for one group: local slot 0 stays fixed, slots 1..7 rotate each
    // round, so every team meets every other exactly once across the 7 rounds.
    void BuildGroupFixtures(int group, int baseIndex)
    {
        const int n = GroupSize;
        for (int r = 0; r < GroupRounds; r++)
        {
            int[] arr = new int[n];
            arr[0] = 0;
            for (int i = 0; i < n - 1; i++) arr[i + 1] = (i + r) % (n - 1) + 1;
            for (int p = 0; p < n / 2; p++)
            {
                fixtures[group, r, p, 0] = baseIndex + arr[p];
                fixtures[group, r, p, 1] = baseIndex + arr[n - 1 - p];
            }
        }
    }

    // Record the player's match for the current phase, then move the tournament along (simulate the
    // rest of the round, advance the phase when the round is done). Called with a placeholder score
    // for now (real match reporting is wired later).
    public void RecordPlayerResult(int playerGoals, int opponentGoals)
    {
        switch (phase)
        {
            case Phase.GroupStage: PlayGroupRound(playerGoals, opponentGoals); break;
            case Phase.Quarterfinal:
            case Phase.Semifinal:
            case Phase.Final: PlayKnockoutRound(playerGoals, opponentGoals); break;
        }
    }

    // One full group round: the player's fixture gets the real score, every other fixture in both
    // groups gets a random one. After round 7, seed the knockout bracket.
    void PlayGroupRound(int pg, int og)
    {
        for (int g = 0; g < 2; g++)
            for (int p = 0; p < GroupSize / 2; p++)
            {
                int a = fixtures[g, groupRound, p, 0], b = fixtures[g, groupRound, p, 1];
                if (a == PlayerIndex) ApplyGroupResult(a, b, pg, og);
                else if (b == PlayerIndex) ApplyGroupResult(a, b, og, pg);
                else ApplyGroupResult(a, b, Random.Range(0, 13), Random.Range(0, 13));
            }
        groupRound++;
        if (groupRound >= GroupRounds) SetupKnockout();
    }

    void ApplyGroupResult(int teamA, int teamB, int goalsA, int goalsB)
    {
        played[teamA]++; played[teamB]++;
        gf[teamA] += goalsA; ga[teamA] += goalsB;
        gf[teamB] += goalsB; ga[teamB] += goalsA;
        if (goalsA > goalsB) { won[teamA]++; lost[teamB]++; }
        else if (goalsA < goalsB) { won[teamB]++; lost[teamA]++; }
        else { drawn[teamA]++; drawn[teamB]++; }
    }

    void SetupKnockout()
    {
        List<int> a = GroupStandings(0), b = GroupStandings(1);
        SetTie(quarterfinals[0], a[0], b[3]); // A1 vs B4
        SetTie(quarterfinals[1], a[1], b[2]); // A2 vs B3
        SetTie(quarterfinals[2], b[0], a[3]); // B1 vs A4
        SetTie(quarterfinals[3], b[1], a[2]); // B2 vs A3
        phase = Phase.Quarterfinal;

        if (PlayerMatch(quarterfinals) == null) // missed the top 4 → the bracket plays out without us
        {
            eliminatedIn = RoundName(Phase.GroupStage);
            SimulateToEnd();
        }
    }

    static void SetTie(KnockoutMatch m, int teamA, int teamB) { m.teamA = teamA; m.teamB = teamB; }

    // The player's knockout tie: record their (draw-proofed) score, simulate the round's other ties,
    // advance the bracket. A loss fast-forwards the rest of the tournament to Completed.
    void PlayKnockoutRound(int pg, int og)
    {
        if (pg == og) { if (Random.value < 0.5f) pg++; else og++; } // sudden death — no knockout draws

        KnockoutMatch[] round = RoundMatches(phase);
        KnockoutMatch mine = PlayerMatch(round);
        if (mine == null) return; // shouldn't happen — eliminated seasons are already Completed

        if (mine.teamA == PlayerIndex) { mine.scoreA = pg; mine.scoreB = og; }
        else { mine.scoreA = og; mine.scoreB = pg; }
        mine.played = true;

        foreach (KnockoutMatch m in round) if (!m.played) Simulate(m);

        bool playerWon = mine.Winner == PlayerIndex;
        if (!playerWon) eliminatedIn = RoundName(phase);
        AdvancePhase();
        if (!playerWon) SimulateToEnd();
    }

    // Current round is fully played → fill the next round's slots from the winners and move on.
    void AdvancePhase()
    {
        if (phase == Phase.Quarterfinal)
        {
            SetTie(semifinals[0], quarterfinals[0].Winner, quarterfinals[1].Winner);
            SetTie(semifinals[1], quarterfinals[2].Winner, quarterfinals[3].Winner);
            phase = Phase.Semifinal;
        }
        else if (phase == Phase.Semifinal)
        {
            SetTie(Final, semifinals[0].Winner, semifinals[1].Winner);
            phase = Phase.Final;
        }
        else if (phase == Phase.Final) phase = Phase.Completed;
    }

    // Plays out every remaining knockout round instantly (used once the player is eliminated).
    void SimulateToEnd()
    {
        while (phase != Phase.Completed)
        {
            foreach (KnockoutMatch m in RoundMatches(phase)) if (!m.played) Simulate(m);
            AdvancePhase();
        }
    }

    static void Simulate(KnockoutMatch m)
    {
        m.scoreA = Random.Range(0, 13);
        m.scoreB = Random.Range(0, 13);
        if (m.scoreA == m.scoreB) { if (Random.value < 0.5f) m.scoreA++; else m.scoreB++; }
        m.played = true;
    }

    public KnockoutMatch[] RoundMatches(Phase p) =>
        p == Phase.Quarterfinal ? quarterfinals : p == Phase.Semifinal ? semifinals : finalRound;

    static KnockoutMatch PlayerMatch(KnockoutMatch[] round)
    {
        foreach (KnockoutMatch m in round) if (m.Has(PlayerIndex)) return m;
        return null;
    }

    // Team indices of one group (0 = A, 1 = B) ordered by Points, then Goal Difference, then Goals
    // For (all descending).
    public List<int> GroupStandings(int group)
    {
        int start = group * GroupSize;
        List<int> order = new List<int>(GroupSize);
        for (int i = 0; i < GroupSize; i++) order.Add(start + i);
        order.Sort((x, y) =>
        {
            int c = Points(y).CompareTo(Points(x));
            if (c != 0) return c;
            c = GoalDiff(y).CompareTo(GoalDiff(x));
            if (c != 0) return c;
            return gf[y].CompareTo(gf[x]);
        });
        return order;
    }
}
