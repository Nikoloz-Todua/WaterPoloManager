using System;
using System.Collections.Generic;

// Shared tournament mechanics used by LeagueSeason-style competitions and WorldCupSeason.
// Fixtures remain LeagueSeason.Fixture so presentation code has one score/identity contract.
public static class TournamentCore
{
    public static List<(int a, int b, int round)> BuildEvenRoundRobin(int teamCount)
    {
        if (teamCount < 2 || teamCount % 2 != 0)
            throw new ArgumentException("An even team count is required.", nameof(teamCount));
        List<int> rotation = new List<int>(teamCount);
        for (int i = 0; i < teamCount; i++) rotation.Add(i);
        List<(int, int, int)> result = new List<(int, int, int)>();
        for (int round = 0; round < teamCount - 1; round++)
        {
            for (int pair = 0; pair < teamCount / 2; pair++)
                result.Add((rotation[pair], rotation[teamCount - 1 - pair], round));
            int last = rotation[teamCount - 1];
            rotation.RemoveAt(teamCount - 1);
            rotation.Insert(1, last);
        }
        return result;
    }

    public static int CompareTable(int x, int y, int[] won, int[] drawn, int[] gf, int[] ga,
                                   string[] names)
    {
        int pointsX = won[x] * 3 + drawn[x];
        int pointsY = won[y] * 3 + drawn[y];
        int comparison = pointsY.CompareTo(pointsX);
        if (comparison != 0) return comparison;
        comparison = (gf[y] - ga[y]).CompareTo(gf[x] - ga[x]);
        if (comparison != 0) return comparison;
        comparison = gf[y].CompareTo(gf[x]);
        return comparison != 0 ? comparison : string.CompareOrdinal(names[x], names[y]);
    }

    public static void ApplyGroupResult(LeagueSeason.Fixture fixture, int scoreA, int scoreB,
                                        bool simulated, int[] played, int[] won, int[] drawn,
                                        int[] lost, int[] gf, int[] ga)
    {
        fixture.scoreA = scoreA;
        fixture.scoreB = scoreB;
        fixture.played = true;
        fixture.simulated = simulated;
        played[fixture.teamA]++; played[fixture.teamB]++;
        gf[fixture.teamA] += scoreA; ga[fixture.teamA] += scoreB;
        gf[fixture.teamB] += scoreB; ga[fixture.teamB] += scoreA;
        if (scoreA > scoreB) { won[fixture.teamA]++; lost[fixture.teamB]++; }
        else if (scoreB > scoreA) { won[fixture.teamB]++; lost[fixture.teamA]++; }
        else { drawn[fixture.teamA]++; drawn[fixture.teamB]++; }
    }

    // Rates bias a probabilistic result; they never deterministically select the stronger side.
    public static void SimulateBiased(int rateA, int rateB, bool knockout, Func<float> next01,
                                      out int scoreA, out int scoreB)
    {
        float total = Math.Max(1f, rateA + rateB);
        float winA = rateA / total;
        bool draw = !knockout && next01() < 0.12f;
        int baseGoals = 3 + (int)(next01() * 6f);
        if (draw)
        {
            scoreA = scoreB = baseGoals;
            return;
        }

        bool aWins = next01() < winA;
        int margin = 1 + (int)(next01() * 4f);
        int winnerGoals = Math.Min(15, baseGoals + margin);
        if (aWins) { scoreA = winnerGoals; scoreB = baseGoals; }
        else { scoreA = baseGoals; scoreB = winnerGoals; }
    }
}
