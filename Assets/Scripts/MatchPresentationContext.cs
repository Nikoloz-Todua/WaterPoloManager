using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Small persisted handoff between the procedural competition UI and the single shared PoolB scene.
// It gives a generic Player/Bot match its real club presentation and submits its real final score once.
public static class MatchPresentationContext
{
    const string Prefix = "championship_fixture_";
    public static int CompetitionIndex { get; private set; } = -1;
    public static string PlayerClub { get; private set; }
    public static string OpponentClub { get; private set; }
    public static bool ResultWasChampionship { get; private set; }
    public static string ResultPlayerClub { get; private set; }
    public static string ResultOpponentClub { get; private set; }
    public static int ResultPlayerGoals { get; private set; }
    public static int ResultOpponentGoals { get; private set; }
    public static bool IsChampionshipFixture => CompetitionIndex >= 0 && !string.IsNullOrEmpty(PlayerClub) && !string.IsNullOrEmpty(OpponentClub);

    public static void SetFixture(int competition, string playerClub, string opponentClub)
    {
        ClearResultPresentation();
        CompetitionIndex = competition; PlayerClub = playerClub; OpponentClub = opponentClub;
        PlayerPrefs.SetInt(Prefix + "competition", competition);
        PlayerPrefs.SetString(Prefix + "player", playerClub);
        PlayerPrefs.SetString(Prefix + "opponent", opponentClub);
        PlayerPrefs.Save();
    }

    public static void Restore()
    {
        if (CompetitionIndex >= 0) return;
        if (!PlayerPrefs.HasKey(Prefix + "competition")) return;
        CompetitionIndex = PlayerPrefs.GetInt(Prefix + "competition", -1);
        PlayerClub = PlayerPrefs.GetString(Prefix + "player", "");
        OpponentClub = PlayerPrefs.GetString(Prefix + "opponent", "");
    }

    public static bool SubmitResult(int homeGoals, int awayGoals)
    {
        Restore();
        if (!IsChampionshipFixture) return false;
        LeagueSeason.Ensure(CompetitionIndex);
        LeagueSeason season = LeagueSeason.Current;
        if (season == null || season.IsComplete || season.PlayerIndex < 0 ||
            season.teams[season.PlayerIndex] != PlayerClub || season.NextOpponentName != OpponentClub)
        {
            Clear();
            return false;
        }
        ResultWasChampionship = true;
        ResultPlayerClub = PlayerClub;
        ResultOpponentClub = OpponentClub;
        ResultPlayerGoals = homeGoals;
        ResultOpponentGoals = awayGoals;
        season.RecordPlayerResult(homeGoals, awayGoals);
        if (season.IsComplete) season.TryGrantCompletionRewards();
        Clear();
        return true;
    }

    public static void Clear()
    {
        CompetitionIndex = -1; PlayerClub = null; OpponentClub = null;
        PlayerPrefs.DeleteKey(Prefix + "competition");
        PlayerPrefs.DeleteKey(Prefix + "player");
        PlayerPrefs.DeleteKey(Prefix + "opponent");
        PlayerPrefs.Save();
    }

    public static void ClearResultPresentation()
    {
        ResultWasChampionship = false;
        ResultPlayerClub = null;
        ResultOpponentClub = null;
        ResultPlayerGoals = 0;
        ResultOpponentGoals = 0;
    }
}

// Runtime-only PoolB presentation wiring: no scene layout coordinates are required from the user.
public sealed class ChampionshipHudBinder : MonoBehaviour
{
    static Sprite badgeSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        MatchPresentationContext.Restore();
        if (!MatchPresentationContext.IsChampionshipFixture || SceneManager.GetActiveScene().name != NavigationManager.MatchScene) return;
        new GameObject("ChampionshipHudBinder").AddComponent<ChampionshipHudBinder>();
    }

    void Start()
    {
        Bind("PlayerNameText", MatchPresentationContext.PlayerClub, true);
        Bind("BotNameText", MatchPresentationContext.OpponentClub, false);
    }

    static void Bind(string objectName, string club, bool playerSide)
    {
        GameObject go = GameObject.Find(objectName);
        if (go == null) return;
        TMP_Text name = go.GetComponent<TMP_Text>();
        if (name != null)
        {
            name.text = club;
            name.fontSizeMax = Mathf.Max(14f, name.fontSize);
            name.enableAutoSizing = true;
            name.fontSizeMin = 12f;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform nameRect = name.rectTransform;
            nameRect.sizeDelta = new Vector2(Mathf.Max(260f, nameRect.sizeDelta.x), nameRect.sizeDelta.y);
        }
        if (go.transform.Find("ClubLogo") != null) return;

        GameObject holder = new GameObject("ClubLogo");
        holder.transform.SetParent(go.transform, false);
        RectTransform hrt = holder.AddComponent<RectTransform>();
        hrt.anchorMin = hrt.anchorMax = new Vector2(playerSide ? 0f : 1f, 0.5f);
        hrt.pivot = new Vector2(playerSide ? 0f : 1f, 0.5f);
        hrt.anchoredPosition = new Vector2(playerSide ? -68f : 68f, 0f);
        hrt.sizeDelta = new Vector2(56f, 56f);

        MakeLayer(holder.transform, "Shadow", new Color(0f, 0f, 0f, 0.42f), 54f, new Vector2(0f, -2f));
        MakeLayer(holder.transform, "Rim", playerSide ? new Color(1f, 0.82f, 0.2f, 1f)
                                                       : new Color(0.95f, 0.24f, 0.30f, 1f),
                  51f, Vector2.zero);
        MakeLayer(holder.transform, "Plate", new Color(0.98f, 0.99f, 1f, 1f), 45f, Vector2.zero);

        GameObject crest = new GameObject("Crest");
        crest.transform.SetParent(holder.transform, false);
        Image image = crest.AddComponent<Image>();
        if (playerSide)
        {
            ClubProfile profile = RosterManager.Instance.Club;
            image.sprite = ClubCustomizationUI.CrestSprite(profile.logoId);
            image.color = ClubCustomizationUI.ParseHex(profile.secondaryColorHex, Color.white);
        }
        else
        {
            ClubCatalog catalog = ClubCatalog.Instance;
            image.sprite = catalog != null ? catalog.LogoFor(club) : null;
            image.color = Color.white;
        }
        image.preserveAspect = true;
        image.raycastTarget = false;
        RectTransform irt = image.rectTransform;
        irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
        irt.pivot = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition = Vector2.zero;
        irt.sizeDelta = new Vector2(53f, 53f);
    }

    static Image MakeLayer(Transform parent, string name, Color color, float size, Vector2 offset)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = BadgeSprite();
        image.color = color;
        image.raycastTarget = false;
        RectTransform rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(size, size);
        return image;
    }

    static Sprite BadgeSprite()
    {
        if (badgeSprite != null) return badgeSprite;
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[size * size];
        float radius = size * 0.5f - 1f;
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float edge = radius - Vector2.Distance(new Vector2(x, y), center);
                pixels[y * size + x] = new Color32(255, 255, 255,
                    (byte)(Mathf.Clamp01(edge) * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        badgeSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return badgeSprite;
    }
}
