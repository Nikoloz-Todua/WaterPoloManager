using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Small persisted handoff between the procedural competition UI and the single shared PoolB scene.
// It gives a generic Player/Bot match its real club presentation and submits its real final score once.
public static class MatchPresentationContext
{
    const string Prefix = "championship_fixture_";
    public enum FixtureIdentity { None = 0, Club = 1, WorldCup = 2 }

    public static FixtureIdentity Identity { get; private set; }
    public static int CompetitionIndex { get; private set; } = -1;
    public static string PlayerClub { get; private set; }
    public static string OpponentClub { get; private set; }
    public static bool ResultWasChampionship { get; private set; }
    public static string ResultPlayerClub { get; private set; }
    public static string ResultOpponentClub { get; private set; }
    public static int ResultPlayerGoals { get; private set; }
    public static int ResultOpponentGoals { get; private set; }
    public static bool ResultWasWorldCup { get; private set; }
    public static bool IsChampionshipFixture => Identity != FixtureIdentity.None &&
        !string.IsNullOrEmpty(PlayerClub) && !string.IsNullOrEmpty(OpponentClub);
    public static bool IsWorldCupFixture => Identity == FixtureIdentity.WorldCup && IsChampionshipFixture;

    public static string ClubAbbreviation(string club)
    {
        if (string.IsNullOrWhiteSpace(club)) return "---";
        System.Text.StringBuilder tag = new System.Text.StringBuilder(3);
        foreach (char c in club)
        {
            if (c == '-' || !char.IsLetterOrDigit(c)) continue;
            tag.Append(char.ToUpperInvariant(c));
            if (tag.Length == 3) break;
        }
        return tag.Length > 0 ? tag.ToString() : "---";
    }

    public static void SetFixture(int competition, string playerClub, string opponentClub)
    {
        ClearResultPresentation();
        Identity = FixtureIdentity.Club;
        CompetitionIndex = competition; PlayerClub = playerClub; OpponentClub = opponentClub;
        PlayerPrefs.SetInt(Prefix + "identity", (int)Identity);
        PlayerPrefs.SetInt(Prefix + "competition", competition);
        PlayerPrefs.SetString(Prefix + "player", playerClub);
        PlayerPrefs.SetString(Prefix + "opponent", opponentClub);
        PlayerPrefs.Save();
    }

    public static void SetWorldCupFixture(string playerCountry, string opponentCountry)
    {
        ClearResultPresentation();
        Identity = FixtureIdentity.WorldCup;
        CompetitionIndex = -1;
        PlayerClub = playerCountry;
        OpponentClub = opponentCountry;
        PlayerPrefs.SetInt(Prefix + "identity", (int)Identity);
        PlayerPrefs.SetInt(Prefix + "competition", -1);
        PlayerPrefs.SetString(Prefix + "player", playerCountry);
        PlayerPrefs.SetString(Prefix + "opponent", opponentCountry);
        PlayerPrefs.Save();
    }

    public static void Restore()
    {
        if (Identity != FixtureIdentity.None) return;
        if (!PlayerPrefs.HasKey(Prefix + "competition") && !PlayerPrefs.HasKey(Prefix + "identity")) return;
        Identity = (FixtureIdentity)PlayerPrefs.GetInt(Prefix + "identity",
            PlayerPrefs.GetInt(Prefix + "competition", -1) >= 0 ? (int)FixtureIdentity.Club : 0);
        CompetitionIndex = PlayerPrefs.GetInt(Prefix + "competition", -1);
        PlayerClub = PlayerPrefs.GetString(Prefix + "player", "");
        OpponentClub = PlayerPrefs.GetString(Prefix + "opponent", "");
    }

    public static bool SubmitResult(int homeGoals, int awayGoals)
    {
        Restore();
        if (!IsChampionshipFixture) return false;
        if (IsWorldCupFixture)
        {
            WorldCupSeason.Ensure();
            WorldCupSeason worldCup = WorldCupSeason.Current;
            if (worldCup == null || worldCup.IsComplete || worldCup.PlayerIndex < 0 ||
                worldCup.teams[worldCup.PlayerIndex] != PlayerClub ||
                worldCup.NextOpponentName != OpponentClub)
            {
                Clear();
                return false;
            }
            int recordedHome = homeGoals;
            int recordedAway = awayGoals;
            if (worldCup.phase != WorldCupSeason.Phase.GroupStage && recordedHome == recordedAway)
            {
                if (UnityEngine.Random.value < 0.5f) recordedHome++;
                else recordedAway++;
            }
            SetResultPresentation(recordedHome, recordedAway, true);
            worldCup.RecordPlayerResult(recordedHome, recordedAway);
            Clear();
            return true;
        }

        LeagueSeason.Ensure(CompetitionIndex);
        LeagueSeason season = LeagueSeason.Current;
        if (season == null || season.IsComplete || season.PlayerIndex < 0 ||
            season.teams[season.PlayerIndex] != PlayerClub || season.NextOpponentName != OpponentClub)
        {
            Clear();
            return false;
        }
        SetResultPresentation(homeGoals, awayGoals, false);
        season.RecordPlayerResult(homeGoals, awayGoals);
        if (season.IsComplete) season.TryGrantCompletionRewards();
        Clear();
        return true;
    }

    public static void Clear()
    {
        Identity = FixtureIdentity.None;
        CompetitionIndex = -1; PlayerClub = null; OpponentClub = null;
        PlayerPrefs.DeleteKey(Prefix + "identity");
        PlayerPrefs.DeleteKey(Prefix + "competition");
        PlayerPrefs.DeleteKey(Prefix + "player");
        PlayerPrefs.DeleteKey(Prefix + "opponent");
        PlayerPrefs.Save();
    }

    public static void ClearResultPresentation()
    {
        ResultWasChampionship = false;
        ResultWasWorldCup = false;
        ResultPlayerClub = null;
        ResultOpponentClub = null;
        ResultPlayerGoals = 0;
        ResultOpponentGoals = 0;
    }

    static void SetResultPresentation(int homeGoals, int awayGoals, bool worldCup)
    {
        ResultWasChampionship = true;
        ResultWasWorldCup = worldCup;
        ResultPlayerClub = PlayerClub;
        ResultOpponentClub = OpponentClub;
        ResultPlayerGoals = homeGoals;
        ResultOpponentGoals = awayGoals;
    }
}

// Runtime-only PoolB presentation wiring: no scene layout coordinates are required from the user.
public sealed class ChampionshipHudBinder : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterForSceneLoads()
    {
        // RuntimeInitializeOnLoadMethod runs once per Play session, not once for every later scene.
        // Subscribe before the initial scene so Hub -> PoolB always installs the live HUD binder.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != NavigationManager.MatchScene) return;
        MatchPresentationContext.Restore();
        if (Object.FindAnyObjectByType<ChampionshipHudBinder>() != null) return;
        new GameObject("ChampionshipHudBinder").AddComponent<ChampionshipHudBinder>();
    }

    void Start()
    {
        ClubProfile profile = RosterManager.Instance.Club;
        string playerClub = MatchPresentationContext.IsChampionshipFixture
            ? MatchPresentationContext.PlayerClub
            : profile.clubName;
        string opponentClub = MatchPresentationContext.IsChampionshipFixture
            ? MatchPresentationContext.OpponentClub
            : "Opponent";
        Bind("PlayerNameText", playerClub, true);
        Bind("BotNameText", opponentClub, false);
    }

    static void Bind(string objectName, string club, bool playerSide)
    {
        GameObject go = GameObject.Find(objectName);
        if (go == null) return;
        TMP_Text name = go.GetComponent<TMP_Text>();
        if (name != null)
        {
            name.text = MatchPresentationContext.ClubAbbreviation(club);
            name.fontSize = 20f;
            name.fontStyle = FontStyles.Bold;
            name.color = Color.white;
            name.alignment = TextAlignmentOptions.Center;
            name.enableAutoSizing = false;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform nameRect = name.rectTransform;
            nameRect.anchoredPosition = new Vector2(playerSide ? -78f : 238f, -40f);
            nameRect.sizeDelta = new Vector2(62f, 34f);

            Shadow shadow = go.GetComponent<Shadow>();
            if (shadow == null) shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.92f);
            shadow.effectDistance = new Vector2(2f, -2f);
            shadow.useGraphicAlpha = true;

            BuildTagPlate(go, nameRect, playerSide);
            BuildHudCrest(go.transform.parent, club, playerSide);
        }

        // Older versions added a full crest under these live-HUD labels. Keep crests confined to
        // pre-match, competition, standings and quarter-break presentation.
        Transform oldLogo = go.transform.Find("ClubLogo");
        if (oldLogo != null) oldLogo.gameObject.SetActive(false);
    }

    static void BuildTagPlate(GameObject label, RectTransform labelRect, bool playerSide)
    {
        Transform parent = label.transform.parent;
        Transform existing = parent.Find(label.name + "_TagPlate");
        if (existing != null) existing.gameObject.SetActive(false);

        GameObject frameGo = new GameObject(label.name + "_TagPlate");
        frameGo.transform.SetParent(parent, false);
        frameGo.transform.SetSiblingIndex(label.transform.GetSiblingIndex());
        Image frame = frameGo.AddComponent<Image>();
        frame.sprite = ClubCustomizationUI.ClubBadgeBackgroundSprite();
        frame.type = Image.Type.Sliced;
        frame.raycastTarget = false;
        frame.color = playerSide
            ? MatchPresentationContext.IsWorldCupFixture
                ? new Color(0.18f, 0.5f, 1f, 1f)
                : ClubCustomizationUI.ParseHex(RosterManager.Instance.Club.primaryColorHex,
                                                new Color(0.18f, 0.5f, 1f, 1f))
            : new Color(0.92f, 0.24f, 0.30f, 1f);
        RectTransform frameRect = frame.rectTransform;
        frameRect.anchorMin = labelRect.anchorMin;
        frameRect.anchorMax = labelRect.anchorMax;
        frameRect.pivot = labelRect.pivot;
        frameRect.anchoredPosition = labelRect.anchoredPosition + new Vector2(0f, -1f);
        frameRect.sizeDelta = new Vector2(68f, 36f);

        GameObject fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(frameGo.transform, false);
        Image fill = fillGo.AddComponent<Image>();
        fill.sprite = frame.sprite;
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.025f, 0.055f, 0.10f, 0.96f);
        fill.raycastTarget = false;
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);
    }

    static void BuildHudCrest(Transform parent, string club, bool playerSide)
    {
        string objectName = playerSide ? "PlayerHudCrest" : "OpponentHudCrest";
        Transform existing = parent.Find(objectName);
        if (existing != null) Object.Destroy(existing.gameObject);

        GameObject holder = new GameObject(objectName);
        holder.transform.SetParent(parent, false);
        RectTransform holderRect = holder.AddComponent<RectTransform>();
        holderRect.anchorMin = holderRect.anchorMax = new Vector2(0.5f, 1f);
        holderRect.pivot = new Vector2(0.5f, 0.5f);
        holderRect.anchoredPosition = new Vector2(playerSide ? -28f : 188f, -40f);
        holderRect.sizeDelta = new Vector2(30f, 30f);

        if (MatchPresentationContext.IsWorldCupFixture)
        {
            Sprite flag = CountryCatalog.Instance != null ? CountryCatalog.Instance.FlagFor(club) : null;
            if (flag == null) { holder.SetActive(false); return; }
            Image flagImage = holder.AddComponent<Image>();
            flagImage.sprite = flag;
            flagImage.color = Color.white;
            flagImage.preserveAspect = true;
            flagImage.raycastTarget = false;
            return;
        }

        if (playerSide)
        {
            CrestTemplateView crest = CrestTemplateView.Create(holder.transform, "SavedClubCrest",
                new Vector2(30f, 30f), new Vector2(0.5f, 0.5f), Vector2.zero);
            crest.SetIdentity(RosterManager.Instance.Club);
            return;
        }

        ClubCatalog catalog = ClubCatalog.Instance;
        Sprite sprite = catalog != null ? catalog.LogoFor(club) : null;
        if (sprite == null) { holder.SetActive(false); return; }
        Image image = holder.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }
}
