using System.Collections.Generic;
using UnityEngine;

// Session-persistent state for one competition's league season. A plain static holder (not a
// MonoBehaviour) so it survives scene transitions within a play session without saving to disk —
// exactly what the standings/pre-match flow needs for now. Team 0 is always the player.
//
// Simulation is deliberately light: the player has a 14-match schedule (each of the 7 opponents
// home + away). Recording the player's result also simulates one round of random results for the
// other teams, so the table keeps evolving. No promotion/relegation engine yet.
public class LeagueSeason
{
    public const int TeamCount = 8;
    public const int PlayerIndex = 0;
    public const int MatchesTotal = 14;

    // The active season (the one being viewed). Persists across scene loads within the session.
    public static LeagueSeason Current;

    // One retained season per competition, so switching competitions and coming back keeps progress.
    static readonly Dictionary<int, LeagueSeason> cache = new Dictionary<int, LeagueSeason>();

    public int competitionIndex;
    public int matchesPlayed;

    public readonly string[] teams = new string[TeamCount];   // [0] = player's club
    public readonly int[] played = new int[TeamCount];
    public readonly int[] won = new int[TeamCount];
    public readonly int[] drawn = new int[TeamCount];
    public readonly int[] lost = new int[TeamCount];
    public readonly int[] gf = new int[TeamCount];            // goals for
    public readonly int[] ga = new int[TeamCount];            // goals against
    public readonly int[] stars = new int[TeamCount];         // 1–5 star rating (display only)
    public readonly int[] scheduleOpponent = new int[MatchesTotal]; // opponent team-index per player match

    // 20 water-polo club names; 7 are drawn per league on first load of that competition.
    static readonly string[] ClubPool =
    {
        "Olympiacos WPC", "Pro Recco", "Ferencváros", "Jug Dubrovnik", "Barceloneta", "Szolnoki",
        "Brescia", "Hannover WP", "Spandau 04", "Vouliagmeni", "Primorac", "Jadran Split",
        "Mladost Zagreb", "Wasserball Zürich", "AEK Athens", "Partizan Belgrade", "Dynamo Moscow",
        "CN Marseille", "Savona WP", "Ortigia"
    };

    public bool IsComplete => matchesPlayed >= MatchesTotal;
    public int GoalDiff(int i) => gf[i] - ga[i];
    public int Points(int i) => won[i] * 3 + drawn[i];
    public int NextOpponent => IsComplete ? -1 : scheduleOpponent[matchesPlayed];
    public string NextOpponentName => IsComplete ? null : teams[NextOpponent];

    // Make sure a season exists for this competition. Reuses the running one (so progress is kept
    // when re-opening the same competition); rebuilds when the competition changes.
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

        // 7 distinct opponents drawn from the pool.
        List<string> pool = new List<string>(ClubPool);
        for (int i = 1; i < TeamCount; i++)
        {
            int r = Random.Range(0, pool.Count);
            s.teams[i] = pool[r];
            pool.RemoveAt(r);
            s.stars[i] = Random.Range(2, 6); // 2–5
        }

        // Schedule: each opponent (1..7) twice, then shuffled.
        for (int i = 0; i < 7; i++) { s.scheduleOpponent[i] = i + 1; s.scheduleOpponent[i + 7] = i + 1; }
        Shuffle(s.scheduleOpponent);
        return s;
    }

    // Record the player's match, then simulate one round for the other six teams. Called with a
    // placeholder score for now (real match reporting is wired later).
    public void RecordPlayerResult(int playerGoals, int opponentGoals)
    {
        if (IsComplete) return;
        int opp = scheduleOpponent[matchesPlayed];
        ApplyResult(PlayerIndex, playerGoals, opponentGoals);
        ApplyResult(opp, opponentGoals, playerGoals);
        SimulateOthers(PlayerIndex, opp);
        matchesPlayed++;
    }

    void ApplyResult(int team, int forGoals, int againstGoals)
    {
        played[team]++;
        gf[team] += forGoals;
        ga[team] += againstGoals;
        if (forGoals > againstGoals) won[team]++;
        else if (forGoals == againstGoals) drawn[team]++;
        else lost[team]++;
    }

    // Pair up the teams not involved in the player's match and play random fixtures between them.
    void SimulateOthers(int a, int b)
    {
        List<int> others = new List<int>();
        for (int i = 0; i < TeamCount; i++) if (i != a && i != b) others.Add(i);
        for (int i = others.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (others[i], others[j]) = (others[j], others[i]);
        }
        for (int i = 0; i + 1 < others.Count; i += 2)
        {
            int home = others[i], away = others[i + 1];
            int hs = Random.Range(0, 13), gs = Random.Range(0, 13);
            ApplyResult(home, hs, gs);
            ApplyResult(away, gs, hs);
        }
    }

    // Team indices ordered by Points, then Goal Difference, then Goals For (all descending).
    public List<int> Standings()
    {
        List<int> order = new List<int>(TeamCount);
        for (int i = 0; i < TeamCount; i++) order.Add(i);
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

    static void Shuffle(int[] a)
    {
        for (int i = a.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }
}
