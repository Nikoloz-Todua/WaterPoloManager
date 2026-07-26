using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Persistent, offline-first championship state. Each competition has exactly ten fixed clubs:
// two groups of five, a five-round schedule with byes, top two semifinals, placement ties and final.
// Gameplay still uses the existing Player/Bot scene sides; this class only owns tournament identity/results.
[Serializable]
public class LeagueSeason
{
    public enum Phase { GroupStage, Semifinal, Final, Completed }

    [Serializable]
    public class Fixture
    {
        public int group = -1;       // 0/1 for group fixture, -1 for knockout/placement
        public int round = -1;       // group calendar round 0..4
        public string label;
        public int teamA = -1;
        public int teamB = -1;
        public int scoreA;
        public int scoreB;
        public bool played;
        public bool simulated;
        public int Winner => !played ? -1 : (scoreA > scoreB ? teamA : scoreB > scoreA ? teamB : -1);
        public bool Has(int team) => team >= 0 && (teamA == team || teamB == team);
    }

    [Serializable]
    class SaveFile { public List<LeagueSeason> seasons = new List<LeagueSeason>(); }

    public const int TeamCount = 10;
    public const int GroupSize = 5;
    public const int GroupRounds = 5; // includes one bye per club
    public const int CurrentSchemaVersion = 3;
    public const string PlayerClubSlot = "__MY_CLUB__";

    public static LeagueSeason Current { get; private set; }
    static readonly Dictionary<int, LeagueSeason> cache = new Dictionary<int, LeagueSeason>();
    static bool loaded;
    const string SaveName = "championships.json";

    public int schemaVersion = CurrentSchemaVersion;
    public int competitionIndex;
    public Phase phase = Phase.GroupStage;
    public int groupRound;
    public int playerIndex = -1;
    public string selectedClubId;
    public string eliminatedIn;
    public uint rngState;
    public bool rewardsGranted;
    public bool promotionGranted;

    public string[] teams = new string[TeamCount];
    public int[] played = new int[TeamCount];
    public int[] won = new int[TeamCount];
    public int[] drawn = new int[TeamCount];
    public int[] lost = new int[TeamCount];
    public int[] gf = new int[TeamCount];
    public int[] ga = new int[TeamCount];
    public int[] stars = new int[TeamCount];
    public int[] finalOrder = new int[TeamCount]; // 1st to 10th; -1 until completion

    public List<Fixture> groupFixtures = new List<Fixture>();
    public Fixture[] semifinals = { new Fixture { label = "SEMIFINAL 1" }, new Fixture { label = "SEMIFINAL 2" } };
    public Fixture thirdPlace = new Fixture { label = "THIRD PLACE" };
    public Fixture final = new Fixture { label = "FINAL" };
    public Fixture placement5 = new Fixture { label = "5TH PLACE" };
    public Fixture placement7 = new Fixture { label = "7TH PLACE" };
    public Fixture placement9 = new Fixture { label = "9TH PLACE" };

    static readonly string[][][] CompetitionGroups =
    {
        new[]
        {
            new[] { PlayerClubSlot, "Arenna", "Didi-Orod", "Ineri", "Locomoco" },
            new[] { "Tbili", "Astinna", "Dinamo", "Poseidon", "Alnguard" }
        },
        new[]
        {
            new[] { PlayerClubSlot, "Aurelio-Posillipo", "Barcelona", "Mularis-Dubonic", "Piranias" },
            new[] { "Randolla", "Red-Star", "Spartakus", "Stu-Bucha", "Apollon" }
        },
        new[]
        {
            new[] { PlayerClubSlot, "Dabrovnik", "Marselo", "Matador", "mlodest" },
            new[] { "Prianik", "Radni", "Saas-Planka", "Vipa-Pospo", "Crab" }
        },
        new[]
        {
            new[] { PlayerClubSlot, "Jordani", "New-Grand", "Olimpi", "Pru-Rico" },
            new[] { "Sebedel", "WP-Lions", "WTC", "Crab", "Matador" }
        }
    };

    static readonly Dictionary<string, int> Strength = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        { "Arenna", 1 }, { "Didi-Orod", 1 }, { "Ineri", 1 }, { "Locomoco", 1 }, { "Tbili", 1 },
        { "Astinna", 2 }, { "Dinamo", 2 }, { "Poseidon", 2 },
        { "Alnguard", 3 }, { "Aurelio-Posillipo", 3 }, { "Barcelona", 3 }, { "Mularis-Dubonic", 3 },
        { "Piranias", 3 }, { "Red-Star", 3 }, { "Spartakus", 3 }, { "Randolla", 3 },
        { "Apollon", 4 }, { "Dabrovnik", 4 }, { "Marselo", 4 }, { "Matador", 4 }, { "mlodest", 4 },
        { "Prianik", 4 }, { "Radni", 4 }, { "Saas-Planka", 4 }, { "Vipa-Pospo", 4 },
        { "Crab", 5 }, { "Jordani", 5 }, { "New-Grand", 5 }, { "Olimpi", 5 }, { "Pru-Rico", 5 },
        { "Sebedel", 5 }, { "WP-Lions", 5 }, { "WTC", 5 }
    };

    public int PlayerIndex => playerIndex;
    public bool IsComplete => phase == Phase.Completed;
    public Fixture Final => final;
    public int Champion => final != null ? final.Winner : -1;
    public bool PlayerIsChampion => IsComplete && Champion == playerIndex;
    public int PlayerMatchWins
    {
        get
        {
            if (playerIndex < 0) return 0;
            int count = 0;
            foreach (Fixture fixture in groupFixtures)
                if (fixture != null && fixture.played && fixture.Has(playerIndex) && fixture.Winner == playerIndex)
                    count++;
            foreach (Fixture fixture in semifinals)
                if (fixture != null && fixture.played && fixture.Has(playerIndex) && fixture.Winner == playerIndex)
                    count++;
            if (final != null && final.played && final.Has(playerIndex) && final.Winner == playerIndex)
                count++;
            return count;
        }
    }
    public int GoalDiff(int i) => gf[i] - ga[i];
    public int Points(int i) => won[i] * 3 + drawn[i];
    public bool PlayerHasBye => phase == Phase.GroupStage && FindPlayerGroupFixture(groupRound) == null;

    public static IReadOnlyList<string> ClubsForCompetition(int competition)
    {
        int c = Mathf.Clamp(competition, 0, CompetitionGroups.Length - 1);
        List<string> clubs = new List<string>(TeamCount);
        clubs.AddRange(CompetitionGroups[c][0]);
        clubs.AddRange(CompetitionGroups[c][1]);
        return clubs;
    }

    public static IReadOnlyList<string> ClubsForGroup(int competition, int group)
    {
        int c = Mathf.Clamp(competition, 0, CompetitionGroups.Length - 1);
        return CompetitionGroups[c][Mathf.Clamp(group, 0, 1)];
    }

    public static bool IsPlayerClubSlot(string clubId) => clubId == PlayerClubSlot;

    public static bool HasRun(int competition)
    {
        LoadAll();
        return cache.ContainsKey(competition);
    }

    // Used by information-only locked competition screens. A saved run is never allowed to make a
    // currently locked competition playable (for example after preferences were cleared/restored).
    public static void ClearCurrentSelection()
    {
        Current = null;
    }

    // Loads an existing run only. Starting a new run automatically injects the saved My Club.
    public static void Ensure(int competitionIndex, string playerTeamName = null)
    {
        LoadAll();
        cache.TryGetValue(competitionIndex, out LeagueSeason season);
        if (season != null && season.playerIndex >= 0 && season.playerIndex < TeamCount &&
            !string.IsNullOrWhiteSpace(playerTeamName))
        {
            season.teams[season.playerIndex] = playerTeamName.Trim();
            season.selectedClubId = PlayerClubSlot;
            SaveAll();
        }
        Current = season;
    }

    public static LeagueSeason StartNew(int competitionIndex, string playerTeamName)
    {
        LoadAll();
        if (competitionIndex < 0 || competitionIndex >= CompetitionGroups.Length)
            throw new ArgumentOutOfRangeException(nameof(competitionIndex));
        if (string.IsNullOrWhiteSpace(playerTeamName))
            throw new ArgumentException("The player's saved club needs a name.", nameof(playerTeamName));

        LeagueSeason s = new LeagueSeason
        {
            competitionIndex = competitionIndex,
            selectedClubId = PlayerClubSlot,
            rngState = (uint)(DateTime.UtcNow.Ticks ^ (competitionIndex + 1) * 2654435761L)
        };
        if (s.rngState == 0) s.rngState = 0x6D2B79F5u;
        Array.Fill(s.finalOrder, -1);

        for (int g = 0; g < 2; g++)
            for (int i = 0; i < GroupSize; i++)
            {
                int index = g * GroupSize + i;
                string slot = CompetitionGroups[competitionIndex][g][i];
                bool playerSlot = IsPlayerClubSlot(slot);
                s.teams[index] = playerSlot ? playerTeamName.Trim() : slot;
                s.stars[index] = playerSlot ? 3 : StrengthOf(slot);
                if (playerSlot) s.playerIndex = index;
            }

        s.BuildGroupFixtures();
        cache[competitionIndex] = s;
        Current = s;
        SaveAll();
        return s;
    }

    public static void ResetCompletedRun(int competitionIndex)
    {
        LoadAll();
        if (cache.TryGetValue(competitionIndex, out LeagueSeason s) && s.IsComplete)
        {
            cache.Remove(competitionIndex);
            if (Current == s) Current = null;
            SaveAll();
        }
    }

    // Mid-run restart is deliberately gated behind one real win. It replaces only this competition
    // save; currencies, unlock PlayerPrefs, and every other competition remain untouched.
    public static bool RestartCurrent(int competitionIndex, string playerTeamName)
    {
        LoadAll();
        if (!cache.TryGetValue(competitionIndex, out LeagueSeason current) ||
            current == null || current.IsComplete || current.PlayerMatchWins < 1)
            return false;
        StartNew(competitionIndex, playerTeamName);
        return true;
    }

    public int NextOpponent
    {
        get
        {
            if (IsComplete || playerIndex < 0) return -1;
            if (phase == Phase.GroupStage)
            {
                Fixture f = FindPlayerGroupFixture(groupRound);
                return f == null ? -1 : (f.teamA == playerIndex ? f.teamB : f.teamA);
            }
            Fixture mine = PlayerFixtureForPhase();
            return mine == null || mine.played ? -1 : (mine.teamA == playerIndex ? mine.teamB : mine.teamA);
        }
    }

    public string NextOpponentName => NextOpponent >= 0 ? teams[NextOpponent] : null;
    public string MatchLabel
    {
        get
        {
            if (phase == Phase.GroupStage)
                return PlayerHasBye ? "GROUP STAGE — BYE ROUND" : "GROUP MATCH " + (groupRound + 1) + " OF " + GroupRounds;
            if (phase == Phase.Semifinal) return "SEMIFINAL";
            if (phase == Phase.Final) return "FINAL";
            return "CHAMPIONSHIP COMPLETE";
        }
    }

    public static string RoundName(Phase p)
    {
        switch (p)
        {
            case Phase.GroupStage: return "GROUP STAGE";
            case Phase.Semifinal: return "SEMIFINAL";
            case Phase.Final: return "FINAL";
            default: return "CHAMPIONSHIP";
        }
    }

    public void RecordPlayerResult(int playerGoals, int opponentGoals)
    {
        if (IsComplete || playerIndex < 0) return;
        if (phase == Phase.GroupStage) PlayGroupRound(playerGoals, opponentGoals);
        else PlayPlayerKnockout(playerGoals, opponentGoals);
        SaveAll();
    }

    public void SimulateByeRound()
    {
        if (phase != Phase.GroupStage || !PlayerHasBye) return;
        foreach (Fixture f in FixturesForGroupRound(groupRound)) if (!f.played) Simulate(f, false);
        groupRound++;
        if (groupRound >= GroupRounds) SetupFinalStage();
        SaveAll();
    }

    public List<int> GroupStandings(int group)
    {
        int start = group * GroupSize;
        List<int> order = new List<int>(GroupSize);
        for (int i = 0; i < GroupSize; i++) order.Add(start + i);
        order.Sort((x, y) => TournamentCore.CompareTable(x, y, won, drawn, gf, ga, teams));
        return order;
    }

    public IEnumerable<Fixture> PlacementFixtures()
    {
        yield return placement5;
        yield return placement7;
        yield return placement9;
        yield return thirdPlace;
    }

    public bool TryGrantCompletionRewards()
    {
        if (!IsComplete || rewardsGranted || playerIndex < 0) return false;
        int rank = Array.IndexOf(finalOrder, playerIndex) + 1;
        GetRewardForRank(competitionIndex, rank, out int gold, out int diamonds);
        if (gold > 0) RosterManager.Instance.AddCoins(gold);
        if (diamonds > 0) RosterManager.Instance.AddDiamonds(diamonds);
        rewardsGranted = true;
        if (rank == 1 && competitionIndex < 3) { promotionGranted = true; PlayerPrefs.SetInt(UnlockKey(competitionIndex + 1), 1); PlayerPrefs.Save(); }
        SaveAll();
        return true;
    }

    public static void GetRewardForRank(int competition, int rank, out int gold, out int diamonds)
    {
        gold = diamonds = 0;
        if (competition < 0 || competition >= CompetitionGroups.Length || rank < 1 || rank > 3) return;

        int[,] goldByCompetition =
        {
            { 3000, 2000, 1000 },
            { 5000, 3500, 2000 },
            { 8000, 5000, 3000 },
            { 15000, 8000, 5000 }
        };
        int[,] diamondsByCompetition =
        {
            { 30, 20, 10 },
            { 50, 35, 20 },
            { 80, 50, 30 },
            { 150, 80, 50 }
        };
        gold = goldByCompetition[competition, rank - 1];
        diamonds = diamondsByCompetition[competition, rank - 1];
    }

    void BuildGroupFixtures()
    {
        groupFixtures.Clear();
        for (int g = 0; g < 2; g++)
        {
            List<int> rotation = new List<int> { 0, 1, 2, 3, 4, -1 }; // -1 is the BYE slot
            // A fresh run draws a fresh calendar once, then the stored fixture list remains fixed.
            // Teams stay in their known groups; only opponent/bye order changes.
            for (int i = rotation.Count - 1; i > 0; i--)
            {
                int swap = NextInt(0, i + 1);
                int value = rotation[i];
                rotation[i] = rotation[swap];
                rotation[swap] = value;
            }
            for (int round = 0; round < GroupRounds; round++)
            {
                for (int pair = 0; pair < 3; pair++)
                {
                    int a = rotation[pair], b = rotation[rotation.Count - 1 - pair];
                    if (a >= 0 && b >= 0)
                        groupFixtures.Add(new Fixture { group = g, round = round, label = "GROUP " + (g == 0 ? "A" : "B") + " — ROUND " + (round + 1), teamA = g * GroupSize + a, teamB = g * GroupSize + b });
                }
                int last = rotation[rotation.Count - 1];
                rotation.RemoveAt(rotation.Count - 1);
                rotation.Insert(1, last);
            }
        }
    }

    void PlayGroupRound(int playerGoals, int opponentGoals)
    {
        foreach (Fixture f in FixturesForGroupRound(groupRound))
        {
            if (f.Has(playerIndex))
            {
                if (f.teamA == playerIndex) ApplyGroupResult(f, playerGoals, opponentGoals, false);
                else ApplyGroupResult(f, opponentGoals, playerGoals, false);
            }
            else Simulate(f, false);
        }
        groupRound++;
        if (groupRound >= GroupRounds) SetupFinalStage();
    }

    void SetupFinalStage()
    {
        List<int> a = GroupStandings(0), b = GroupStandings(1);
        SetTie(semifinals[0], a[0], b[1], "SEMIFINAL 1");
        SetTie(semifinals[1], b[0], a[1], "SEMIFINAL 2");
        SetTie(placement5, a[2], b[2], "5TH PLACE");
        SetTie(placement7, a[3], b[3], "7TH PLACE");
        SetTie(placement9, a[4], b[4], "9TH PLACE");
        phase = Phase.Semifinal;

        if (!semifinals[0].Has(playerIndex) && !semifinals[1].Has(playerIndex))
        {
            eliminatedIn = "GROUP STAGE";
            SimulateToEnd();
        }
    }

    void PlayPlayerKnockout(int playerGoals, int opponentGoals)
    {
        Fixture mine = PlayerFixtureForPhase();
        if (mine == null || mine.played) return;
        if (mine.teamA == playerIndex) ApplyKnockoutResult(mine, playerGoals, opponentGoals, false);
        else ApplyKnockoutResult(mine, opponentGoals, playerGoals, false);

        if (phase == Phase.Semifinal)
        {
            foreach (Fixture semi in semifinals) if (!semi.played) Simulate(semi, true);
            foreach (Fixture p in new[] { placement5, placement7, placement9 }) if (!p.played) Simulate(p, true);
            SetTie(thirdPlace, semifinals[0].Winner == semifinals[0].teamA ? semifinals[0].teamB : semifinals[0].teamA,
                              semifinals[1].Winner == semifinals[1].teamA ? semifinals[1].teamB : semifinals[1].teamA, "THIRD PLACE");
            SetTie(final, semifinals[0].Winner, semifinals[1].Winner, "FINAL");
            if (!final.Has(playerIndex))
            {
                eliminatedIn = "SEMIFINAL";
                Simulate(thirdPlace, true);
                Simulate(final, true);
                Complete();
            }
            else
            {
                Simulate(thirdPlace, true);
                phase = Phase.Final;
            }
        }
        else if (phase == Phase.Final)
        {
            Complete();
        }
    }

    void SimulateToEnd()
    {
        foreach (Fixture semi in semifinals) if (!semi.played) Simulate(semi, true);
        foreach (Fixture p in new[] { placement5, placement7, placement9 }) if (!p.played) Simulate(p, true);
        SetTie(thirdPlace, Loser(semifinals[0]), Loser(semifinals[1]), "THIRD PLACE");
        SetTie(final, semifinals[0].Winner, semifinals[1].Winner, "FINAL");
        Simulate(thirdPlace, true);
        Simulate(final, true);
        Complete();
    }

    void Complete()
    {
        phase = Phase.Completed;
        finalOrder = new[]
        {
            final.Winner, Loser(final), thirdPlace.Winner, Loser(thirdPlace),
            placement5.Winner, Loser(placement5), placement7.Winner, Loser(placement7), placement9.Winner, Loser(placement9)
        };
    }

    Fixture PlayerFixtureForPhase()
    {
        if (phase == Phase.Semifinal)
        {
            foreach (Fixture f in semifinals) if (f.Has(playerIndex)) return f;
        }
        return phase == Phase.Final && final.Has(playerIndex) ? final : null;
    }

    Fixture FindPlayerGroupFixture(int round)
    {
        foreach (Fixture f in FixturesForGroupRound(round)) if (f.Has(playerIndex)) return f;
        return null;
    }

    IEnumerable<Fixture> FixturesForGroupRound(int round)
    {
        foreach (Fixture f in groupFixtures) if (f.round == round) yield return f;
    }

    void Simulate(Fixture f, bool knockout)
    {
        if (f == null || f.played) return;
        int a = SimGoals(f.teamA), b = SimGoals(f.teamB);
        if (knockout && a == b) { if (Next01() < 0.5f) a++; else b++; }
        if (f.group >= 0) ApplyGroupResult(f, a, b, true); else ApplyKnockoutResult(f, a, b, true);
    }

    int SimGoals(int team)
    {
        int strength = team >= 0 && team < teams.Length ? StrengthOf(teams[team]) : 3;
        int roll = NextInt(0, 8); // 0..7, keeps results plausible and varied
        return Mathf.Clamp(2 + roll + strength / 2, 0, 13);
    }

    void ApplyGroupResult(Fixture f, int a, int b, bool simulated)
    {
        TournamentCore.ApplyGroupResult(f, a, b, simulated, played, won, drawn, lost, gf, ga);
    }

    void ApplyKnockoutResult(Fixture f, int a, int b, bool simulated)
    {
        if (a == b) { if (Next01() < 0.5f) a++; else b++; }
        f.scoreA = a; f.scoreB = b; f.played = true; f.simulated = simulated;
    }

    static int Loser(Fixture f) => f == null || !f.played ? -1 : f.Winner == f.teamA ? f.teamB : f.teamA;
    static void SetTie(Fixture f, int a, int b, string label) { f.teamA = a; f.teamB = b; f.label = label; f.played = false; f.simulated = false; f.scoreA = f.scoreB = 0; }
    static bool Contains(IReadOnlyList<string> values, string value) { for (int i = 0; i < values.Count; i++) if (values[i] == value) return true; return false; }
    static int StrengthOf(string club) => !string.IsNullOrEmpty(club) && Strength.TryGetValue(club, out int value) ? value : 3;
    static string UnlockKey(int index) => index == 1 ? "div1_won" : index == 2 ? "pl_won" : index == 3 ? "cc_won" : "";

    int NextInt(int min, int maxExclusive) => min + Mathf.FloorToInt(Next01() * (maxExclusive - min));
    float Next01() { rngState = rngState * 1664525u + 1013904223u; return (rngState & 0x00FFFFFFu) / 16777216f; }

    static string SavePath => Path.Combine(Application.persistentDataPath, SaveName);
    static void LoadAll()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            if (!File.Exists(SavePath)) return;
            SaveFile file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(SavePath));
            if (file == null || file.seasons == null) return;
            foreach (LeagueSeason s in file.seasons)
            {
                // Schema 3 changes the roster contract from "pick an official club" to the saved
                // My Club occupying a dedicated slot. Old runs cannot be migrated without silently
                // dropping/replacing a different AI club, so they intentionally restart clean.
                if (s == null || s.schemaVersion != CurrentSchemaVersion ||
                    s.teams == null || s.teams.Length != TeamCount) continue;
                s.RepairAfterLoad();
                cache[s.competitionIndex] = s;
            }
        }
        catch (Exception e) { Debug.LogWarning("LeagueSeason: championship save could not be read. " + e.Message); }
    }

    void RepairAfterLoad()
    {
        if (played == null || played.Length != TeamCount) played = new int[TeamCount];
        if (won == null || won.Length != TeamCount) won = new int[TeamCount];
        if (drawn == null || drawn.Length != TeamCount) drawn = new int[TeamCount];
        if (lost == null || lost.Length != TeamCount) lost = new int[TeamCount];
        if (gf == null || gf.Length != TeamCount) gf = new int[TeamCount];
        if (ga == null || ga.Length != TeamCount) ga = new int[TeamCount];
        if (stars == null || stars.Length != TeamCount) stars = new int[TeamCount];
        if (finalOrder == null || finalOrder.Length != TeamCount) { finalOrder = new int[TeamCount]; Array.Fill(finalOrder, -1); }
        if (groupFixtures == null) groupFixtures = new List<Fixture>();
        if (semifinals == null || semifinals.Length != 2) semifinals = new[] { new Fixture { label = "SEMIFINAL 1" }, new Fixture { label = "SEMIFINAL 2" } };
        if (thirdPlace == null) thirdPlace = new Fixture { label = "THIRD PLACE" };
        if (final == null) final = new Fixture { label = "FINAL" };
        if (placement5 == null) placement5 = new Fixture { label = "5TH PLACE" };
        if (placement7 == null) placement7 = new Fixture { label = "7TH PLACE" };
        if (placement9 == null) placement9 = new Fixture { label = "9TH PLACE" };
        if (rngState == 0) rngState = 0x6D2B79F5u;
        // Schema 3 always reserves the first Group A slot for the player's saved My Club.
        selectedClubId = PlayerClubSlot;
        playerIndex = 0;
        for (int i = 0; i < TeamCount; i++)
            if (stars[i] <= 0) stars[i] = StrengthOf(teams[i]);

        // The fixture list is authoritative. Re-derive the duplicated table counters on every load
        // so an interrupted/partially written save cannot leave fixtures and standings disagreeing.
        RebuildGroupStats();
    }

    void RebuildGroupStats()
    {
        Array.Clear(played, 0, played.Length);
        Array.Clear(won, 0, won.Length);
        Array.Clear(drawn, 0, drawn.Length);
        Array.Clear(lost, 0, lost.Length);
        Array.Clear(gf, 0, gf.Length);
        Array.Clear(ga, 0, ga.Length);

        foreach (Fixture f in groupFixtures)
        {
            if (f == null || !f.played || f.teamA < 0 || f.teamA >= TeamCount || f.teamB < 0 || f.teamB >= TeamCount) continue;
            played[f.teamA]++; played[f.teamB]++;
            gf[f.teamA] += f.scoreA; ga[f.teamA] += f.scoreB;
            gf[f.teamB] += f.scoreB; ga[f.teamB] += f.scoreA;
            if (f.scoreA > f.scoreB) { won[f.teamA]++; lost[f.teamB]++; }
            else if (f.scoreB > f.scoreA) { won[f.teamB]++; lost[f.teamA]++; }
            else { drawn[f.teamA]++; drawn[f.teamB]++; }
        }
    }

    static void SaveAll()
    {
        LoadAll();
        try { File.WriteAllText(SavePath, JsonUtility.ToJson(new SaveFile { seasons = new List<LeagueSeason>(cache.Values) }, true)); }
        catch (Exception e) { Debug.LogWarning("LeagueSeason: championship save could not be written. " + e.Message); }
    }
}
