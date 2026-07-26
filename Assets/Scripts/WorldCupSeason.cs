using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// World Cup specialization built on the project's LeagueSeason fixture contract and TournamentCore
// rules: six balanced groups, global round advancement, then a 16-team knockout bracket.
[Serializable]
public sealed class WorldCupSeason
{
    public enum Phase { GroupStage, RoundOf16, Quarterfinal, Semifinal, Final, Completed }

    [Serializable]
    sealed class SaveFile { public WorldCupSeason season; }

    public const int TeamCount = 36;
    public const int GroupCount = 6;
    public const int GroupSize = 6;
    public const int GroupRounds = 5;
    public const int SchemaVersion = 1;
    const string SaveName = "worldcup.json";

    static bool loaded;
    static int seedCounter;
    public static WorldCupSeason Current { get; private set; }

    public int schemaVersion = SchemaVersion;
    public Phase phase = Phase.GroupStage;
    public int groupRound;
    public int playerIndex = -1;
    public string selectedCountry;
    public string eliminatedIn;
    public string drawSignature;
    public uint rngState;

    public string[] teams = new string[TeamCount];
    public int[] winRates = new int[TeamCount];
    public int[] teamGroups = new int[TeamCount];
    public int[] played = new int[TeamCount];
    public int[] won = new int[TeamCount];
    public int[] drawn = new int[TeamCount];
    public int[] lost = new int[TeamCount];
    public int[] gf = new int[TeamCount];
    public int[] ga = new int[TeamCount];

    public List<LeagueSeason.Fixture> groupFixtures = new List<LeagueSeason.Fixture>();
    public LeagueSeason.Fixture[] roundOf16 = NewFixtures(8, "ROUND OF 16");
    public LeagueSeason.Fixture[] quarterfinals = NewFixtures(4, "QUARTERFINAL");
    public LeagueSeason.Fixture[] semifinals = NewFixtures(2, "SEMIFINAL");
    public LeagueSeason.Fixture final = new LeagueSeason.Fixture { label = "FINAL" };

    public bool IsComplete => phase == Phase.Completed;
    public int PlayerIndex => playerIndex;
    public int PlayerGroup => playerIndex >= 0 ? teamGroups[playerIndex] : -1;
    public int Champion => final != null ? final.Winner : -1;
    public int RunnerUp => final == null || !final.played ? -1 : Loser(final);
    public bool PlayerIsChampion => IsComplete && Champion == playerIndex;
    public int GoalDiff(int team) => gf[team] - ga[team];
    public int Points(int team) => won[team] * 3 + drawn[team];
    public bool CanRestart => PlayerMatchWins > 0;

    public int PlayerMatchWins
    {
        get
        {
            int count = 0;
            foreach (LeagueSeason.Fixture fixture in AllFixtures())
                if (fixture != null && fixture.played && !fixture.simulated &&
                    fixture.Has(playerIndex) && fixture.Winner == playerIndex)
                    count++;
            return count;
        }
    }

    public int NextOpponent
    {
        get
        {
            if (IsComplete || playerIndex < 0) return -1;
            LeagueSeason.Fixture fixture = phase == Phase.GroupStage
                ? FindPlayerGroupFixture(groupRound)
                : PlayerFixtureForPhase();
            if (fixture == null || fixture.played) return -1;
            return fixture.teamA == playerIndex ? fixture.teamB : fixture.teamA;
        }
    }

    public string NextOpponentName => NextOpponent >= 0 ? teams[NextOpponent] : null;
    public string MatchLabel => phase == Phase.GroupStage
        ? "GROUP " + GroupLetter(PlayerGroup) + " — MATCH " + (groupRound + 1) + " OF " + GroupRounds
        : PhaseLabel(phase);

    public static bool HasRun()
    {
        Ensure();
        return Current != null;
    }

    public static void Ensure()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            if (!File.Exists(SavePath)) return;
            SaveFile file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(SavePath));
            if (file?.season == null || file.season.schemaVersion != SchemaVersion) return;
            file.season.RepairAfterLoad();
            Current = file.season;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("WorldCupSeason: save could not be read. " + exception.Message);
        }
    }

    public static WorldCupSeason StartNew(string selectedCountry)
    {
        Ensure();
        CountryCatalog catalog = CountryCatalog.Instance;
        if (catalog == null || catalog.Get(selectedCountry) == null)
            throw new ArgumentException("A catalog country must be selected.", nameof(selectedCountry));

        string previousDraw = Current?.drawSignature;
        WorldCupSeason season = null;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            season = new WorldCupSeason
            {
                selectedCountry = selectedCountry,
                rngState = (uint)(DateTime.UtcNow.Ticks ^ (++seedCounter * 2654435761L) ^ attempt)
            };
            if (season.rngState == 0) season.rngState = 0xA341316Cu;
            season.DrawGroups(catalog);
            if (season.drawSignature != previousDraw) break;
        }
        season.BuildGroupFixtures();
        Current = season;
        Save();
        return season;
    }

    public static bool Restart()
    {
        Ensure();
        if (Current == null || !Current.CanRestart) return false;
        string country = Current.selectedCountry;
        StartNew(country);
        return true;
    }

    public static void DeleteRun()
    {
        Current = null;
        loaded = true;
        if (File.Exists(SavePath)) File.Delete(SavePath);
    }

    public void RecordPlayerResult(int playerGoals, int opponentGoals)
    {
        if (IsComplete || playerIndex < 0 || NextOpponent < 0) return;
        if (phase == Phase.GroupStage) PlayGroupRound(playerGoals, opponentGoals);
        else PlayKnockoutRound(playerGoals, opponentGoals);
        Save();
    }

    public List<int> GroupStandings(int group)
    {
        List<int> order = new List<int>(GroupSize);
        for (int i = 0; i < TeamCount; i++) if (teamGroups[i] == group) order.Add(i);
        order.Sort((x, y) => TournamentCore.CompareTable(x, y, won, drawn, gf, ga, teams));
        return order;
    }

    public List<int> BestThirdPlaceTeams()
    {
        List<int> thirds = new List<int>(GroupCount);
        for (int group = 0; group < GroupCount; group++) thirds.Add(GroupStandings(group)[2]);
        thirds.Sort((x, y) => TournamentCore.CompareTable(x, y, won, drawn, gf, ga, teams));
        return thirds;
    }

    public List<int> Qualifiers()
    {
        List<int> qualifiers = new List<int>(16);
        for (int group = 0; group < GroupCount; group++)
        {
            List<int> table = GroupStandings(group);
            qualifiers.Add(table[0]);
            qualifiers.Add(table[1]);
        }
        qualifiers.AddRange(BestThirdPlaceTeams().Take(4));
        return qualifiers;
    }

    public LeagueSeason.Fixture[] FixturesForPhase(Phase value)
    {
        switch (value)
        {
            case Phase.RoundOf16: return roundOf16;
            case Phase.Quarterfinal: return quarterfinals;
            case Phase.Semifinal: return semifinals;
            case Phase.Final: return new[] { final };
            default: return Array.Empty<LeagueSeason.Fixture>();
        }
    }

    void DrawGroups(CountryCatalog catalog)
    {
        List<CountryCatalog.Entry> ordered = new List<CountryCatalog.Entry>(catalog.Countries);
        ordered.Sort((a, b) => b.winRate.CompareTo(a.winRate));
        for (int pot = 0; pot < GroupCount; pot++)
        {
            List<CountryCatalog.Entry> tier = ordered.GetRange(pot * GroupSize, GroupSize);
            Shuffle(tier);
            for (int group = 0; group < GroupCount; group++)
            {
                int team = group * GroupSize + pot;
                teams[team] = tier[group].country;
                winRates[team] = tier[group].winRate;
                teamGroups[team] = group;
                if (teams[team] == selectedCountry) playerIndex = team;
            }
        }
        drawSignature = string.Join("|", teams);
    }

    void BuildGroupFixtures()
    {
        groupFixtures.Clear();
        List<(int a, int b, int round)> schedule = TournamentCore.BuildEvenRoundRobin(GroupSize);
        for (int group = 0; group < GroupCount; group++)
            foreach ((int a, int b, int round) in schedule)
                groupFixtures.Add(new LeagueSeason.Fixture
                {
                    group = group,
                    round = round,
                    label = "GROUP " + GroupLetter(group) + " — ROUND " + (round + 1),
                    teamA = group * GroupSize + a,
                    teamB = group * GroupSize + b
                });
    }

    void PlayGroupRound(int playerGoals, int opponentGoals)
    {
        foreach (LeagueSeason.Fixture fixture in groupFixtures)
        {
            if (fixture.round != groupRound || fixture.played) continue;
            if (fixture.Has(playerIndex))
            {
                if (fixture.teamA == playerIndex)
                    ApplyGroup(fixture, playerGoals, opponentGoals, false);
                else
                    ApplyGroup(fixture, opponentGoals, playerGoals, false);
            }
            else Simulate(fixture, false);
        }
        groupRound++;
        if (groupRound >= GroupRounds) SetupRoundOf16();
    }

    void SetupRoundOf16()
    {
        List<int> winners = new List<int>(GroupCount);
        List<int> lowerSeeds = new List<int>(10);
        for (int group = 0; group < GroupCount; group++)
        {
            List<int> table = GroupStandings(group);
            winners.Add(table[0]);
            lowerSeeds.Add(table[1]);
        }
        lowerSeeds.AddRange(BestThirdPlaceTeams().Take(4));
        Shuffle(winners);
        Shuffle(lowerSeeds);

        int tie = 0;
        foreach (int winner in winners)
        {
            List<int> eligible = lowerSeeds.Where(team => teamGroups[team] != teamGroups[winner]).ToList();
            if (eligible.Count == 0) eligible.AddRange(lowerSeeds);
            int opponent = eligible[NextInt(0, eligible.Count)];
            lowerSeeds.Remove(opponent);
            SetTie(roundOf16[tie], winner, opponent, "ROUND OF 16 — MATCH " + (tie + 1));
            tie++;
        }
        while (lowerSeeds.Count > 0)
        {
            int a = lowerSeeds[0];
            lowerSeeds.RemoveAt(0);
            int differentGroup = lowerSeeds.FindIndex(team => teamGroups[team] != teamGroups[a]);
            int pick = differentGroup >= 0 ? differentGroup : 0;
            int b = lowerSeeds[pick];
            lowerSeeds.RemoveAt(pick);
            SetTie(roundOf16[tie], a, b, "ROUND OF 16 — MATCH " + (tie + 1));
            tie++;
        }
        phase = Phase.RoundOf16;
        if (!roundOf16.Any(fixture => fixture.Has(playerIndex)))
        {
            eliminatedIn = "GROUP STAGE";
            SimulateToEnd();
        }
    }

    void PlayKnockoutRound(int playerGoals, int opponentGoals)
    {
        Phase playedPhase = phase;
        LeagueSeason.Fixture mine = PlayerFixtureForPhase();
        if (mine == null || mine.played) return;
        if (mine.teamA == playerIndex)
            ApplyKnockout(mine, playerGoals, opponentGoals, false);
        else
            ApplyKnockout(mine, opponentGoals, playerGoals, false);

        foreach (LeagueSeason.Fixture fixture in FixturesForPhase(playedPhase))
            if (!fixture.played) Simulate(fixture, true);

        bool playerAdvanced = mine.Winner == playerIndex;
        AdvanceRound();
        if (!playerAdvanced)
        {
            eliminatedIn = PhaseLabel(playedPhase);
            SimulateToEnd();
        }
    }

    void AdvanceRound()
    {
        if (phase == Phase.Final)
        {
            phase = Phase.Completed;
            return;
        }
        LeagueSeason.Fixture[] current = FixturesForPhase(phase);
        LeagueSeason.Fixture[] next;
        Phase nextPhase;
        switch (phase)
        {
            case Phase.RoundOf16: next = quarterfinals; nextPhase = Phase.Quarterfinal; break;
            case Phase.Quarterfinal: next = semifinals; nextPhase = Phase.Semifinal; break;
            case Phase.Semifinal: next = new[] { final }; nextPhase = Phase.Final; break;
            default: return;
        }
        for (int i = 0; i < next.Length; i++)
            SetTie(next[i], current[i * 2].Winner, current[i * 2 + 1].Winner,
                   PhaseLabel(nextPhase) + (next.Length > 1 ? " — MATCH " + (i + 1) : ""));
        phase = nextPhase;
    }

    void SimulateToEnd()
    {
        while (!IsComplete)
        {
            foreach (LeagueSeason.Fixture fixture in FixturesForPhase(phase))
                if (!fixture.played) Simulate(fixture, true);
            AdvanceRound();
        }
    }

    LeagueSeason.Fixture FindPlayerGroupFixture(int round)
        => groupFixtures.FirstOrDefault(fixture => fixture.round == round && fixture.Has(playerIndex));

    LeagueSeason.Fixture PlayerFixtureForPhase()
        => FixturesForPhase(phase).FirstOrDefault(fixture => fixture.Has(playerIndex));

    void Simulate(LeagueSeason.Fixture fixture, bool knockout)
    {
        TournamentCore.SimulateBiased(winRates[fixture.teamA], winRates[fixture.teamB],
                                      knockout, Next01, out int scoreA, out int scoreB);
        if (fixture.group >= 0) ApplyGroup(fixture, scoreA, scoreB, true);
        else ApplyKnockout(fixture, scoreA, scoreB, true);
    }

    void ApplyGroup(LeagueSeason.Fixture fixture, int scoreA, int scoreB, bool simulated)
        => TournamentCore.ApplyGroupResult(fixture, scoreA, scoreB, simulated,
                                           played, won, drawn, lost, gf, ga);

    void ApplyKnockout(LeagueSeason.Fixture fixture, int scoreA, int scoreB, bool simulated)
    {
        if (scoreA == scoreB)
        {
            if (Next01() < 0.5f) scoreA++;
            else scoreB++;
        }
        fixture.scoreA = scoreA;
        fixture.scoreB = scoreB;
        fixture.played = true;
        fixture.simulated = simulated;
    }

    IEnumerable<LeagueSeason.Fixture> AllFixtures()
    {
        foreach (LeagueSeason.Fixture fixture in groupFixtures) yield return fixture;
        foreach (LeagueSeason.Fixture fixture in roundOf16) yield return fixture;
        foreach (LeagueSeason.Fixture fixture in quarterfinals) yield return fixture;
        foreach (LeagueSeason.Fixture fixture in semifinals) yield return fixture;
        yield return final;
    }

    void RepairAfterLoad()
    {
        if (teams == null || teams.Length != TeamCount) throw new InvalidDataException("Bad team data.");
        if (winRates == null || winRates.Length != TeamCount) winRates = new int[TeamCount];
        if (teamGroups == null || teamGroups.Length != TeamCount) teamGroups = new int[TeamCount];
        played = RepairArray(played); won = RepairArray(won); drawn = RepairArray(drawn);
        lost = RepairArray(lost); gf = RepairArray(gf); ga = RepairArray(ga);
        groupFixtures ??= new List<LeagueSeason.Fixture>();
        roundOf16 = RepairFixtures(roundOf16, 8, "ROUND OF 16");
        quarterfinals = RepairFixtures(quarterfinals, 4, "QUARTERFINAL");
        semifinals = RepairFixtures(semifinals, 2, "SEMIFINAL");
        final ??= new LeagueSeason.Fixture { label = "FINAL" };
        if (rngState == 0) rngState = 0xA341316Cu;
        playerIndex = Array.IndexOf(teams, selectedCountry);
        for (int i = 0; i < TeamCount; i++)
        {
            teamGroups[i] = i / GroupSize;
            if (winRates[i] <= 0) winRates[i] = CountryCatalog.Instance.WinRateFor(teams[i]);
        }
        RebuildGroupStats();
    }

    void RebuildGroupStats()
    {
        Array.Clear(played, 0, TeamCount); Array.Clear(won, 0, TeamCount);
        Array.Clear(drawn, 0, TeamCount); Array.Clear(lost, 0, TeamCount);
        Array.Clear(gf, 0, TeamCount); Array.Clear(ga, 0, TeamCount);
        foreach (LeagueSeason.Fixture fixture in groupFixtures)
            if (fixture != null && fixture.played)
                TournamentCore.ApplyGroupResult(new LeagueSeason.Fixture
                {
                    teamA = fixture.teamA, teamB = fixture.teamB
                }, fixture.scoreA, fixture.scoreB, fixture.simulated,
                played, won, drawn, lost, gf, ga);
    }

    static int[] RepairArray(int[] value) => value != null && value.Length == TeamCount
        ? value : new int[TeamCount];

    static LeagueSeason.Fixture[] RepairFixtures(LeagueSeason.Fixture[] value, int count, string label)
        => value != null && value.Length == count ? value : NewFixtures(count, label);

    static LeagueSeason.Fixture[] NewFixtures(int count, string label)
    {
        LeagueSeason.Fixture[] fixtures = new LeagueSeason.Fixture[count];
        for (int i = 0; i < count; i++)
            fixtures[i] = new LeagueSeason.Fixture { label = label + " — MATCH " + (i + 1) };
        return fixtures;
    }

    static int Loser(LeagueSeason.Fixture fixture)
        => fixture.Winner == fixture.teamA ? fixture.teamB : fixture.teamA;

    static void SetTie(LeagueSeason.Fixture fixture, int teamA, int teamB, string label)
    {
        fixture.group = -1; fixture.round = -1; fixture.label = label;
        fixture.teamA = teamA; fixture.teamB = teamB;
        fixture.scoreA = fixture.scoreB = 0;
        fixture.played = fixture.simulated = false;
    }

    void Shuffle<T>(IList<T> values)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int swap = NextInt(0, i + 1);
            T value = values[i]; values[i] = values[swap]; values[swap] = value;
        }
    }

    int NextInt(int min, int maxExclusive)
        => min + Mathf.FloorToInt(Next01() * (maxExclusive - min));

    float Next01()
    {
        rngState = rngState * 1664525u + 1013904223u;
        return (rngState & 0x00FFFFFFu) / 16777216f;
    }

    static string GroupLetter(int group) => ((char)('A' + Mathf.Clamp(group, 0, 5))).ToString();

    public static string PhaseLabel(Phase value)
    {
        switch (value)
        {
            case Phase.GroupStage: return "GROUP STAGE";
            case Phase.RoundOf16: return "ROUND OF 16";
            case Phase.Quarterfinal: return "QUARTERFINAL";
            case Phase.Semifinal: return "SEMIFINAL";
            case Phase.Final: return "FINAL";
            default: return "WORLD CUP COMPLETE";
        }
    }

    static string SavePath => Path.Combine(Application.persistentDataPath, SaveName);

    static void Save()
    {
        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(new SaveFile { season = Current }, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("WorldCupSeason: save could not be written. " + exception.Message);
        }
    }
}
