using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Code-built World Cup navigation panels. Uses the existing Hub canvas and PoolB match scene.
public sealed class WorldCupUI : MonoBehaviour
{
    static readonly Color Background = new Color(0.018f, 0.04f, 0.08f, 0.985f);
    static readonly Color Panel = new Color(0.045f, 0.085f, 0.145f, 0.98f);
    static readonly Color PanelSoft = new Color(0.07f, 0.12f, 0.19f, 0.98f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.2f, 1f);
    static readonly Color Blue = new Color(0.18f, 0.5f, 1f, 1f);
    static readonly Color Red = new Color(0.88f, 0.24f, 0.3f, 1f);
    static readonly Color Green = new Color(0.2f, 0.72f, 0.32f, 1f);
    static readonly Color Muted = new Color(0.62f, 0.72f, 0.84f, 1f);

    Transform host;
    GameObject root;
    Transform sheet;
    string selectedCountry;
    TMP_InputField searchField;
    RectTransform pickerContent;
    Transform dashboardContent;
    TextMeshProUGUI toast;
    int dashboardTab;

    public void Initialize(Transform canvasRoot)
    {
        host = canvasRoot;
    }

    public void Open()
    {
        if (host == null) return;
        CountryCatalog catalog = CountryCatalog.Instance;
        if (catalog == null || catalog.Countries.Count != 36)
        {
            Debug.LogError("World Cup requires the generated 36-country catalog.");
            return;
        }
        EnsureRoot();
        WorldCupSeason.Ensure();
        if (WorldCupSeason.Current == null) BuildCountryPicker();
        else BuildDashboard();
        root.SetActive(true);
        root.transform.SetAsLastSibling();
    }

    void EnsureRoot()
    {
        if (root != null && sheet != null) return;
        if (root != null) Destroy(root);
        root = new GameObject("Overlay_WORLD_CUP");
        root.transform.SetParent(host, false);
        RectTransform rt = root.AddComponent<RectTransform>();
        Stretch(rt);
        Image backdrop = root.AddComponent<Image>();
        backdrop.color = Background;
        backdrop.raycastTarget = true;
        root.AddComponent<CanvasGroup>();

        GameObject sheetGo = new GameObject("Sheet");
        sheetGo.transform.SetParent(root.transform, false);
        RectTransform sheetRect = sheetGo.AddComponent<RectTransform>();
        sheet = sheetRect;
        Stretch(sheetRect);
    }

    void BuildCountryPicker()
    {
        Clear(sheet);
        BuildTopBar("WORLD CUP", Close);
        MakeImage(sheet, "WorldCupTrophy", CountryCatalog.Instance.WorldCupTrophy,
                  new Vector2(0f, 1f), new Vector2(94f, -124f), new Vector2(96f, 96f));
        MakeText(sheet, "CHOOSE YOUR COUNTRY", 31f, new Vector2(0.5f, 1f),
                 new Vector2(0f, -115f), new Vector2(520f, 44f), Color.white);
        MakeText(sheet, "Search or scroll, then confirm who you will represent.", 15f,
                 new Vector2(0.5f, 1f), new Vector2(0f, -151f),
                 new Vector2(620f, 26f), Muted);

        searchField = MakeSearchField(sheet, new Vector2(0f, -199f), new Vector2(620f, 48f));
        searchField.onValueChanged.AddListener(RebuildCountryTiles);

        ScrollRect scroll = MakeScroll(sheet, "CountryPickerScroll", new Vector2(0f, -10f),
                                       new Vector2(1080f, 380f), false, true, out pickerContent);
        scroll.verticalNormalizedPosition = 1f;
        selectedCountry = null;
        RebuildCountryTiles("");

        MakeButton(sheet, "CONFIRM COUNTRY", new Vector2(0.5f, 0f),
                   new Vector2(0f, 40f), new Vector2(340f, 62f), Green,
                   ConfirmCountrySelection);
        toast = MakeText(sheet, "", 15f, new Vector2(0.5f, 0f),
                         new Vector2(0f, 13f), new Vector2(620f, 24f), Gold);
    }

    void RebuildCountryTiles(string query)
    {
        if (pickerContent == null) return;
        Clear(pickerContent);
        string filter = (query ?? "").Trim();
        List<CountryCatalog.Entry> entries = CountryCatalog.Instance.Countries
            .Where(entry => string.IsNullOrEmpty(filter) ||
                            entry.country.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(entry => entry.country, StringComparer.Ordinal).ToList();

        const int columns = 3;
        const float tileW = 330f, tileH = 66f, gapX = 18f, gapY = 12f;
        int rows = Mathf.CeilToInt(entries.Count / (float)columns);
        pickerContent.sizeDelta = new Vector2(0f, Mathf.Max(380f, rows * (tileH + gapY) + 16f));
        for (int i = 0; i < entries.Count; i++)
        {
            CountryCatalog.Entry entry = entries[i];
            int column = i % columns;
            int row = i / columns;
            float x = 18f + tileW * 0.5f + column * (tileW + gapX);
            float y = -(12f + tileH * 0.5f + row * (tileH + gapY));
            Image tile = MakePanel(pickerContent, "Country_" + entry.country,
                                   new Vector2(0f, 1f), new Vector2(x, y),
                                   new Vector2(tileW, tileH),
                                   entry.country == selectedCountry ? Gold : new Color(0.18f, 0.27f, 0.37f, 1f));
            Image fill = MakePanel(tile.transform, "Fill", new Vector2(0.5f, 0.5f), Vector2.zero,
                                   new Vector2(tileW - 6f, tileH - 6f), PanelSoft);
            fill.raycastTarget = false;
            MakeImage(fill.transform, "Flag", entry.flag, new Vector2(0f, 0.5f),
                      new Vector2(45f, 0f), new Vector2(60f, 40f));
            MakeText(fill.transform, entry.country, 18f, new Vector2(0f, 0.5f),
                     new Vector2(177f, 8f), new Vector2(190f, 26f), Color.white,
                     TextAlignmentOptions.Left);
            MakeText(fill.transform, "FORM  " + entry.winRate, 12f, new Vector2(0f, 0.5f),
                     new Vector2(177f, -15f), new Vector2(190f, 18f), Muted,
                     TextAlignmentOptions.Left);
            Button button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile;
            string country = entry.country;
            button.onClick.AddListener(() =>
            {
                selectedCountry = country;
                RebuildCountryTiles(searchField != null ? searchField.text : "");
            });
        }
    }

    void ConfirmCountrySelection()
    {
        if (string.IsNullOrEmpty(selectedCountry))
        {
            if (toast != null) toast.text = "SELECT A COUNTRY FIRST";
            return;
        }
        BuildConfirmModal("REPRESENT " + selectedCountry.ToUpperInvariant() + "?",
            "A fresh six-pot group draw will begin.",
            () =>
            {
                WorldCupSeason.StartNew(selectedCountry);
                BuildDashboard();
            });
    }

    void BuildDashboard()
    {
        Clear(sheet);
        WorldCupSeason season = WorldCupSeason.Current;
        if (season == null) { BuildCountryPicker(); return; }
        BuildTopBar("WORLD CUP", Close);

        MakeImage(sheet, "WorldCupTrophy", CountryCatalog.Instance.WorldCupTrophy,
                  new Vector2(0f, 1f), new Vector2(88f, -126f), new Vector2(86f, 86f));
        MakeText(sheet, season.selectedCountry.ToUpperInvariant(), 26f, new Vector2(0f, 1f),
                 new Vector2(250f, -109f), new Vector2(260f, 34f), Color.white,
                 TextAlignmentOptions.Left);
        MakeText(sheet, WorldCupSeason.PhaseLabel(season.phase), 14f, new Vector2(0f, 1f),
                 new Vector2(250f, -139f), new Vector2(260f, 24f), Gold,
                 TextAlignmentOptions.Left);

        MakeButton(sheet, "GROUPS", new Vector2(0.5f, 1f), new Vector2(-132f, -126f),
                   new Vector2(240f, 48f), dashboardTab == 0 ? Blue : PanelSoft,
                   () => { dashboardTab = 0; BuildDashboard(); });
        MakeButton(sheet, "BRACKET", new Vector2(0.5f, 1f), new Vector2(132f, -126f),
                   new Vector2(240f, 48f), dashboardTab == 1 ? Blue : PanelSoft,
                   () => { dashboardTab = 1; BuildDashboard(); });
        GameObject contentGo = new GameObject("WorldCupDashboardContent");
        contentGo.transform.SetParent(sheet, false);
        RectTransform contentRt = contentGo.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 0f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.offsetMin = new Vector2(18f, 82f);
        contentRt.offsetMax = new Vector2(-18f, -172f);
        dashboardContent = contentGo.transform;

        if (dashboardTab == 0) BuildGroupsView(season);
        else BuildBracketView(season);
        BuildDashboardAction(season);
    }

    void BuildGroupsView(WorldCupSeason season)
    {
        ScrollRect scroll = MakeScroll(dashboardContent, "WorldCupGroupsScroll", Vector2.zero,
                                       Vector2.zero, false, true, out RectTransform content, true);
        content.sizeDelta = new Vector2(0f, 612f);
        List<int> bestThirds = season.groupRound >= WorldCupSeason.GroupRounds
            ? season.BestThirdPlaceTeams().Take(4).ToList()
            : new List<int>();
        for (int group = 0; group < WorldCupSeason.GroupCount; group++)
        {
            int column = group % 3;
            int row = group / 3;
            float x = 194f + column * 386f;
            float y = -(8f + 144f + row * 302f);
            Image card = MakePanel(content, "WorldCupGroup_" + (char)('A' + group),
                                   new Vector2(0f, 1f), new Vector2(x, y),
                                   new Vector2(366f, 282f),
                                   group == season.PlayerGroup ? Gold : new Color(0.18f, 0.32f, 0.48f, 1f));
            Image fill = MakePanel(card.transform, "Fill", new Vector2(0.5f, 0.5f), Vector2.zero,
                                   new Vector2(360f, 276f), Panel);
            fill.raycastTarget = false;
            MakeText(fill.transform, "GROUP " + (char)('A' + group), 19f,
                     new Vector2(0f, 1f), new Vector2(92f, -24f),
                     new Vector2(150f, 26f), Color.white, TextAlignmentOptions.Left);
            MakeText(fill.transform, "P  GD  PTS", 11f, new Vector2(1f, 1f),
                     new Vector2(-79f, -25f), new Vector2(140f, 20f), Muted,
                     TextAlignmentOptions.Right);

            List<int> table = season.GroupStandings(group);
            for (int rank = 0; rank < table.Count; rank++)
            {
                int team = table[rank];
                bool player = team == season.PlayerIndex;
                bool qualification = rank < 2 || bestThirds.Contains(team);
                Color rowColor = player ? new Color(0.45f, 0.35f, 0.08f, 0.95f)
                    : qualification ? new Color(0.08f, 0.22f, 0.18f, 0.88f)
                    : new Color(0.025f, 0.06f, 0.11f, 0.78f);
                Image rowImage = MakePanel(fill.transform, "Team_" + season.teams[team],
                    new Vector2(0.5f, 1f), new Vector2(0f, -(58f + rank * 34f)),
                    new Vector2(340f, 30f), rowColor);
                rowImage.raycastTarget = false;
                MakeImage(rowImage.transform, "Flag", CountryCatalog.Instance.FlagFor(season.teams[team]),
                          new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(34f, 22f));
                TextMeshProUGUI name = MakeText(rowImage.transform,
                    (rank + 1) + "  " + season.teams[team], 13f, new Vector2(0f, 0.5f),
                    new Vector2(135f, 0f), new Vector2(180f, 24f), Color.white,
                    TextAlignmentOptions.Left);
                name.enableAutoSizing = true; name.fontSizeMin = 10f; name.fontSizeMax = 13f;
                MakeText(rowImage.transform,
                    season.played[team] + "   " + Signed(season.GoalDiff(team)) + "   " + season.Points(team),
                    12f, new Vector2(1f, 0.5f), new Vector2(-68f, 0f),
                    new Vector2(130f, 24f), Color.white, TextAlignmentOptions.Right);
            }
        }
        scroll.verticalNormalizedPosition = 1f;
    }

    void BuildBracketView(WorldCupSeason season)
    {
        ScrollRect scroll = MakeScroll(dashboardContent, "WorldCupBracketScroll", Vector2.zero,
                                       Vector2.zero, true, false, out RectTransform content, true);
        content.sizeDelta = new Vector2(1410f, 500f);
        string[] labels = { "ROUND OF 16", "QUARTERFINALS", "SEMIFINALS", "FINAL" };
        float[] xs = { 130f, 455f, 780f, 1105f };
        float[][] ys =
        {
            new[] { 220f, 157f, 94f, 31f, -32f, -95f, -158f, -221f },
            new[] { 188f, 63f, -62f, -187f },
            new[] { 126f, -126f },
            new[] { 0f }
        };
        WorldCupSeason.Phase[] phases =
        {
            WorldCupSeason.Phase.RoundOf16, WorldCupSeason.Phase.Quarterfinal,
            WorldCupSeason.Phase.Semifinal, WorldCupSeason.Phase.Final
        };

        for (int column = 0; column < labels.Length; column++)
        {
            MakeText(content, labels[column], 16f, new Vector2(0f, 1f),
                     new Vector2(xs[column], -20f), new Vector2(250f, 24f), Gold);
            LeagueSeason.Fixture[] fixtures = season.FixturesForPhase(phases[column]);
            if (column < labels.Length - 1)
            {
                for (int i = 0; i < fixtures.Length; i += 2)
                    BuildConnector(content, xs[column], ys[column][i], ys[column][i + 1],
                                   xs[column + 1], ys[column + 1][i / 2]);
            }
            for (int i = 0; i < fixtures.Length; i++)
                BuildBracketFixture(content, season, fixtures[i], xs[column], ys[column][i]);
        }

        MakeImage(content, "FinalTrophy", CountryCatalog.Instance.WorldCupTrophy,
                  new Vector2(0f, 0.5f), new Vector2(1290f, 130f), new Vector2(112f, 112f));
        if (season.IsComplete)
        {
            BuildPodiumSlot(content, season, season.Champion, "1ST", new Vector2(1290f, 20f), Gold);
            BuildPodiumSlot(content, season, season.RunnerUp, "2ND", new Vector2(1290f, -70f),
                            new Color(0.72f, 0.78f, 0.86f, 1f));
        }
        else
            MakeText(content, "THE WORLD CUP", 15f, new Vector2(0f, 0.5f),
                     new Vector2(1290f, 38f), new Vector2(210f, 24f), Muted);
        scroll.horizontalNormalizedPosition = season.phase >= WorldCupSeason.Phase.Semifinal ? 1f : 0f;
    }

    void BuildBracketFixture(Transform parent, WorldCupSeason season, LeagueSeason.Fixture fixture,
                             float x, float y)
    {
        Image card = MakePanel(parent, "BracketFixture", new Vector2(0f, 0.5f),
                               new Vector2(x, y), new Vector2(240f, 56f),
                               fixture != null && fixture.Has(season.PlayerIndex) ? Gold
                                   : new Color(0.19f, 0.3f, 0.43f, 1f));
        Image fill = MakePanel(card.transform, "Fill", new Vector2(0.5f, 0.5f), Vector2.zero,
                               new Vector2(234f, 50f), PanelSoft);
        fill.raycastTarget = false;
        BuildBracketTeamRow(fill.transform, season, fixture, fixture?.teamA ?? -1, true);
        BuildBracketTeamRow(fill.transform, season, fixture, fixture?.teamB ?? -1, false);
    }

    void BuildBracketTeamRow(Transform parent, WorldCupSeason season, LeagueSeason.Fixture fixture,
                             int team, bool top)
    {
        bool loser = fixture != null && fixture.played && team >= 0 && fixture.Winner != team;
        float y = top ? 13f : -13f;
        CanvasGroup group = new GameObject(top ? "TeamA" : "TeamB").AddComponent<CanvasGroup>();
        group.transform.SetParent(parent, false);
        SetRect(group.gameObject.AddComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0f, y), new Vector2(226f, 22f));
        group.alpha = loser ? 0.34f : 1f;
        string name = team >= 0 ? season.teams[team] : "TBD";
        MakeImage(group.transform, "Flag",
                  team >= 0 ? CountryCatalog.Instance.FlagFor(name) : null,
                  new Vector2(0f, 0.5f), new Vector2(17f, 0f), new Vector2(28f, 18f));
        TextMeshProUGUI label = MakeText(group.transform, name, 12f, new Vector2(0f, 0.5f),
                 new Vector2(105f, 0f), new Vector2(140f, 20f), Color.white,
                 TextAlignmentOptions.Left);
        label.enableAutoSizing = true; label.fontSizeMin = 9f; label.fontSizeMax = 12f;
        string score = fixture != null && fixture.played
            ? (top ? fixture.scoreA : fixture.scoreB).ToString() : "–";
        MakeText(group.transform, score, 13f, new Vector2(1f, 0.5f),
                 new Vector2(-16f, 0f), new Vector2(28f, 20f),
                 loser ? Muted : Gold, TextAlignmentOptions.Center);
        if (loser)
        {
            Image strike = MakePanel(group.transform, "LoserStrike", new Vector2(0.5f, 0.5f),
                                     Vector2.zero, new Vector2(185f, 2f),
                                     new Color(0.9f, 0.2f, 0.24f, 0.7f));
            strike.raycastTarget = false;
        }
    }

    void BuildConnector(Transform parent, float fromX, float yA, float yB, float toX, float toY)
    {
        float jointX = (fromX + toX) * 0.5f;
        MakeLine(parent, new Vector2((fromX + 120f + jointX) * 0.5f, yA),
                 new Vector2(jointX - (fromX + 120f), 2f));
        MakeLine(parent, new Vector2((fromX + 120f + jointX) * 0.5f, yB),
                 new Vector2(jointX - (fromX + 120f), 2f));
        MakeLine(parent, new Vector2(jointX, (yA + yB) * 0.5f),
                 new Vector2(2f, Mathf.Abs(yA - yB)));
        MakeLine(parent, new Vector2((jointX + toX - 120f) * 0.5f, toY),
                 new Vector2(toX - 120f - jointX, 2f));
    }

    void BuildPodiumSlot(Transform parent, WorldCupSeason season, int team, string rank,
                         Vector2 position, Color color)
    {
        Image slot = MakePanel(parent, "Podium_" + rank, new Vector2(0f, 0.5f), position,
                               new Vector2(220f, 68f), color);
        Image fill = MakePanel(slot.transform, "Fill", new Vector2(0.5f, 0.5f), Vector2.zero,
                               new Vector2(214f, 62f), Panel);
        fill.raycastTarget = false;
        if (team < 0) return;
        MakeImage(fill.transform, "Flag", CountryCatalog.Instance.FlagFor(season.teams[team]),
                  new Vector2(0f, 0.5f), new Vector2(43f, 0f), new Vector2(58f, 38f));
        MakeText(fill.transform, rank, 11f, new Vector2(0f, 0.5f),
                 new Vector2(100f, 14f), new Vector2(110f, 18f), color,
                 TextAlignmentOptions.Left);
        MakeText(fill.transform, season.teams[team], 14f, new Vector2(0f, 0.5f),
                 new Vector2(128f, -10f), new Vector2(165f, 24f), Color.white,
                 TextAlignmentOptions.Left);
    }

    void BuildDashboardAction(WorldCupSeason season)
    {
        if (season.IsComplete)
        {
            MakeText(sheet, season.PlayerIsChampion ? "WORLD CHAMPIONS!" :
                     "CHAMPION: " + season.teams[season.Champion], 19f,
                     new Vector2(0.5f, 0f), new Vector2(0f, 42f),
                     new Vector2(520f, 34f), season.PlayerIsChampion ? Gold : Color.white);
            return;
        }
        GameObject rowGo = new GameObject("WorldCupActions");
        rowGo.transform.SetParent(sheet, false);
        RectTransform rowRt = rowGo.AddComponent<RectTransform>();
        SetRect(rowRt, new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(616f, 62f));
        HorizontalLayoutGroup row = rowGo.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 16f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true; row.childControlHeight = true;
        row.childForceExpandWidth = true; row.childForceExpandHeight = true;

        MakeUnifiedActionButton(rowGo.transform, "RESTART", RequestRestart);
        if (season.NextOpponent >= 0)
            MakeUnifiedActionButton(rowGo.transform,
                "NEXT MATCH  vs. " + season.NextOpponentName.ToUpperInvariant(), OpenPreMatch);
    }

    void OpenPreMatch()
    {
        WorldCupSeason season = WorldCupSeason.Current;
        if (season == null || season.NextOpponent < 0) return;
        GameObject modal = MakeModalBase("WorldCupPreMatch");
        Image card = MakePanel(modal.transform, "FixtureCard", new Vector2(0.5f, 0.5f),
                               Vector2.zero, new Vector2(920f, 500f), Panel);
        MakeImage(card.transform, "Trophy", CountryCatalog.Instance.WorldCupTrophy,
                  new Vector2(0.5f, 1f), new Vector2(0f, -68f), new Vector2(100f, 100f));
        MakeText(card.transform, season.MatchLabel, 20f, new Vector2(0.5f, 1f),
                 new Vector2(0f, -126f), new Vector2(500f, 30f), Gold);
        BuildCountrySide(card.transform, season.selectedCountry, new Vector2(-260f, 25f), Blue);
        BuildCountrySide(card.transform, season.NextOpponentName, new Vector2(260f, 25f), Red);
        MakeText(card.transform, "VS", 44f, new Vector2(0.5f, 0.5f),
                 new Vector2(0f, 38f), new Vector2(100f, 60f), Color.white);
        UniversalUIStyle.MakeCloseButton(card.transform, Vector2.one,
            new Vector2(-32f, -32f), new Vector2(48f, 48f), () => Destroy(modal));
        MakeButton(card.transform, "PLAY MATCH", new Vector2(0.5f, 0f),
                   new Vector2(0f, 50f), new Vector2(290f, 60f), Green,
                   StartWorldCupMatch);
    }

    void BuildCountrySide(Transform parent, string country, Vector2 position, Color accent)
    {
        Image side = MakePanel(parent, "Country_" + country, new Vector2(0.5f, 0.5f),
                               position, new Vector2(300f, 220f), accent);
        Image fill = MakePanel(side.transform, "Fill", new Vector2(0.5f, 0.5f),
                               Vector2.zero, new Vector2(294f, 214f), PanelSoft);
        fill.raycastTarget = false;
        MakeImage(fill.transform, "Flag", CountryCatalog.Instance.FlagFor(country),
                  new Vector2(0.5f, 0.5f), new Vector2(0f, 24f), new Vector2(170f, 110f));
        MakeText(fill.transform, country.ToUpperInvariant(), 22f,
                 new Vector2(0.5f, 0f), new Vector2(0f, 34f),
                 new Vector2(270f, 36f), Color.white);
    }

    void StartWorldCupMatch()
    {
        WorldCupSeason season = WorldCupSeason.Current;
        if (season == null || season.NextOpponent < 0) return;
        MatchPresentationContext.SetWorldCupFixture(season.selectedCountry, season.NextOpponentName);
        LoadingOverlayUI.LoadScene(NavigationManager.MatchScene, false, "PREPARING WORLD CUP MATCH...");
    }

    void RequestRestart()
    {
        WorldCupSeason season = WorldCupSeason.Current;
        if (season == null || season.IsComplete) return;
        BuildConfirmModal("RESTART WORLD CUP?",
            "Current World Cup tournament progress will be erased and a new tournament draw " +
            "will be created. Your other progress/currencies remain.",
            () =>
            {
                WorldCupSeason.Restart();
                dashboardTab = 0;
                BuildDashboard();
            }, "CANCEL", "RESTART");
    }

    void BuildConfirmModal(string title, string body, Action confirm,
                           string cancelLabel = "NO", string confirmLabel = "YES")
    {
        GameObject modal = MakeModalBase("WorldCupConfirmation");
        Image card = MakePanel(modal.transform, "ConfirmCard", new Vector2(0.5f, 0.5f),
                               Vector2.zero, new Vector2(650f, 310f), Panel);
        MakeText(card.transform, title, 26f, new Vector2(0.5f, 1f),
                 new Vector2(0f, -60f), new Vector2(580f, 40f), Color.white);
        MakeText(card.transform, body, 16f, new Vector2(0.5f, 0.5f),
                 new Vector2(0f, 24f), new Vector2(560f, 60f), Muted);
        MakeButton(card.transform, cancelLabel, new Vector2(0.5f, 0f),
                   new Vector2(-150f, 52f), new Vector2(240f, 58f),
                   new Color(0.25f, 0.33f, 0.44f, 1f), () => Destroy(modal));
        MakeButton(card.transform, confirmLabel, new Vector2(0.5f, 0f),
                   new Vector2(150f, 52f), new Vector2(240f, 58f), Red,
                   () => { Destroy(modal); confirm(); });
    }

    GameObject MakeModalBase(string name)
    {
        GameObject modal = new GameObject(name);
        modal.transform.SetParent(root.transform, false);
        Image dim = modal.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.78f);
        dim.raycastTarget = true;
        Stretch(dim.rectTransform);
        modal.transform.SetAsLastSibling();
        return modal;
    }

    void ShowToast(string message)
    {
        if (toast == null || toast.transform.parent != sheet)
            toast = MakeText(sheet, "", 15f, new Vector2(0.5f, 0f),
                             new Vector2(0f, 14f), new Vector2(700f, 24f), Gold);
        toast.text = message;
    }

    void BuildTopBar(string title, Action back)
    {
        Image bar = MakePanel(sheet, "WorldCupTopBar", new Vector2(0.5f, 1f),
                              Vector2.zero, new Vector2(0f, 80f),
                              new Color(0.035f, 0.055f, 0.11f, 0.98f));
        RectTransform rt = bar.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f); rt.sizeDelta = new Vector2(0f, 80f);
        UniversalUIStyle.MakeCloseButton(bar.transform, new Vector2(0f, 0.5f),
            new Vector2(48f, 0f), new Vector2(56f, 56f), () => back());
        MakeText(bar.transform, title, 34f, new Vector2(0.5f, 0.5f),
                 Vector2.zero, new Vector2(520f, 50f), Color.white);
        NavigationManager navigation = GetComponent<NavigationManager>();
        if (navigation != null) navigation.AddCurrencyDisplay(bar.transform);
    }

    void Close() => root.SetActive(false);

    ScrollRect MakeScroll(Transform parent, string name, Vector2 position, Vector2 size,
                          bool horizontal, bool vertical, out RectTransform content,
                          bool stretch = false)
    {
        GameObject viewportGo = new GameObject(name);
        viewportGo.transform.SetParent(parent, false);
        Image viewportImage = viewportGo.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.12f);
        viewportGo.AddComponent<RectMask2D>();
        RectTransform viewport = viewportImage.rectTransform;
        if (stretch)
        {
            Stretch(viewport);
        }
        else SetRect(viewport, new Vector2(0.5f, 0.5f), position, size);

        GameObject contentGo = new GameObject("Content");
        contentGo.transform.SetParent(viewportGo.transform, false);
        content = contentGo.AddComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = horizontal ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = horizontal ? new Vector2(size.x, 0f) : new Vector2(0f, size.y);

        ScrollRect scroll = viewportGo.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = horizontal;
        scroll.vertical = vertical;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.12f;
        return scroll;
    }

    TMP_InputField MakeSearchField(Transform parent, Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject("CountrySearch");
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = Rounded(); image.type = Image.Type.Sliced; image.color = PanelSoft;
        SetRect(image.rectTransform, new Vector2(0.5f, 1f), position, size);
        TMP_InputField input = go.AddComponent<TMP_InputField>();
        input.targetGraphic = image;

        TextMeshProUGUI text = MakeText(go.transform, "", 18f, new Vector2(0.5f, 0.5f),
                                        new Vector2(16f, 0f), new Vector2(size.x - 56f, size.y - 8f),
                                        Color.white, TextAlignmentOptions.Left);
        TextMeshProUGUI placeholder = MakeText(go.transform, "SEARCH COUNTRIES", 18f,
            new Vector2(0.5f, 0.5f), new Vector2(16f, 0f),
            new Vector2(size.x - 56f, size.y - 8f), new Color(1f, 1f, 1f, 0.35f),
            TextAlignmentOptions.Left);
        input.textComponent = text;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        return input;
    }

    Button MakeButton(Transform parent, string label, Vector2 anchor, Vector2 position,
                      Vector2 size, Color color, Action action, float fontSize = 18f)
    {
        Image image = MakePanel(parent, "Btn_" + label, anchor, position, size, color);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        if (action != null) button.onClick.AddListener(() => action());
        MakeText(image.transform, label, fontSize, new Vector2(0.5f, 0.5f),
                 Vector2.zero, size - new Vector2(12f, 8f), Color.white);
        return button;
    }

    Button MakeUnifiedActionButton(Transform parent, string label, Action action)
    {
        GameObject go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = 300f;
        layout.minHeight = layout.preferredHeight = 62f;
        layout.flexibleWidth = 1f;

        Image image = go.AddComponent<Image>();
        CrestUITheme.ApplyButton(image, Green);
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        if (action != null) button.onClick.AddListener(() => action());
        LocalizedButtonStyler.AddLabel(go.transform, label, 17f, new Vector2(300f, 62f),
            LocalizedButtonStyler.TextZone.NativeCenter, 1f);
        return button;
    }

    Image MakePanel(Transform parent, string name, Vector2 anchor, Vector2 position,
                    Vector2 size, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = Rounded();
        image.type = Image.Type.Sliced;
        image.color = color;
        SetRect(image.rectTransform, anchor, position, size);
        return image;
    }

    Image MakeImage(Transform parent, string name, Sprite sprite, Vector2 anchor,
                    Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        SetRect(image.rectTransform, anchor, position, size);
        return image;
    }

    TextMeshProUGUI MakeText(Transform parent, string value, float fontSize, Vector2 anchor,
                             Vector2 position, Vector2 size, Color color,
                             TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = value; text.fontSize = fontSize; text.fontStyle = FontStyles.Bold;
        text.color = color; text.alignment = alignment; text.raycastTarget = false;
        SetRect(text.rectTransform, anchor, position, size);
        return text;
    }

    void MakeLine(Transform parent, Vector2 position, Vector2 size)
    {
        Image line = MakePanel(parent, "BracketConnector", new Vector2(0f, 0.5f),
                               position, size, new Color(0.36f, 0.54f, 0.72f, 0.58f));
        line.raycastTarget = false;
        line.transform.SetAsFirstSibling();
    }

    static void Clear(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
    }

    static string Signed(int value) => value > 0 ? "+" + value : value.ToString();
    static Sprite Rounded() => ClubCustomizationUI.ClubBadgeBackgroundSprite();

    static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
