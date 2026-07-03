using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Data-driven pool theming (architecture pass, 2026-07): WHICH background / ambient dressing a
// match pool shows is configuration here, not scene edits or new code. Follows the project's
// CardPack pattern (static C# defs + Resources sprite paths — the pool art is a scene-side
// concern, so a ScriptableObject asset per theme would add editor churn for no gain) and the
// StaminaSystem pattern for the applier (self-bootstrapping, zero scene wiring).
//
// Adding a themed pool later = register one PoolTheme entry (sprite paths + tint + ambient
// layers) and map a division to its id in DivisionThemeIds. No other code changes. Ambient
// ANIMATION (crowd reactions, bench behavior) plugs into the spawned ambient layer objects
// once that art exists — deliberately NOT built yet.
public class PoolTheme
{
    public string id;
    public string displayName;

    // Resources path of a sprite to swap onto the PoolWater renderer. null/empty = keep the
    // scene's existing art untouched (the current Voronoi ShaderWaterMaterial stays as-is).
    public string backgroundSpritePath;

    // Multiplied into the PoolWater SpriteRenderer color. White = leave the scene color alone.
    public Color waterTint = Color.white;

    // Static dressing sprites (bleachers / crowd base / banners) spawned around the pool.
    // Empty for the classic theme — future themes list their art here.
    public AmbientLayer[] ambientLayers = System.Array.Empty<AmbientLayer>();

    public class AmbientLayer
    {
        public string spritePath;      // Resources path
        public Vector2 position;       // world position
        public Vector2 scale = Vector2.one;
        public int sortingOrder = -10; // behind the pool water by default
    }
}

// The theme catalog + division mapping. Read-only at runtime, like PlayerDatabase/CardPack.
public static class PoolThemes
{
    public const string DefaultThemeId = "classic";

    static readonly Dictionary<string, PoolTheme> byId = new Dictionary<string, PoolTheme>();

    static PoolThemes()
    {
        // The ONE real theme: the scene's current art, expressed as "no overrides". It proves
        // the lookup + apply pipeline end-to-end while changing nothing visually.
        Register(new PoolTheme
        {
            id = DefaultThemeId,
            displayName = "Classic Pool",
            backgroundSpritePath = null,   // keep the scene's ShaderWaterMaterial water
            waterTint = Color.white,       // keep the scene's color
        });
    }

    static void Register(PoolTheme t) => byId[t.id] = t;

    public static PoolTheme Get(string id) =>
        id != null && byId.TryGetValue(id, out PoolTheme t) ? t : byId[DefaultThemeId];

    // LeagueSeason.competitionIndex (0 = Division 1 … 3 = World Champions League) → theme id.
    // Only Division 1 is genuinely mapped; the other three fall back to classic until their
    // themed art exists (no fake placeholder recolors — the water is a shader, not a sprite,
    // so an honest recolor isn't trivially safe).
    static readonly string[] DivisionThemeIds =
        { DefaultThemeId, DefaultThemeId, DefaultThemeId, DefaultThemeId };

    public static PoolTheme ForDivision(int competitionIndex) =>
        Get(competitionIndex >= 0 && competitionIndex < DivisionThemeIds.Length
            ? DivisionThemeIds[competitionIndex] : DefaultThemeId);
}

// Applies the active theme whenever a scene containing a "PoolWater" object loads. Hub/menu
// scenes have no PoolWater → no-op. Self-bootstrapping (RuntimeInitializeOnLoadMethod), so no
// scene object or Inspector slot exists for this.
public static class PoolThemeApplier
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        Apply(); // the scene that booted first
        SceneManager.sceneLoaded += (_, _) => Apply();
    }

    static void Apply()
    {
        GameObject pool = GameObject.Find("PoolWater");
        if (pool == null) return; // not a match scene

        int division = LeagueSeason.Current != null ? LeagueSeason.Current.competitionIndex : 0;
        PoolTheme theme = PoolThemes.ForDivision(division);

        SpriteRenderer sr = pool.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            if (!string.IsNullOrEmpty(theme.backgroundSpritePath))
            {
                Sprite bg = Resources.Load<Sprite>(theme.backgroundSpritePath);
                if (bg != null) sr.sprite = bg;
                else Debug.LogWarning("PoolTheme '" + theme.id + "': background sprite missing at Resources/"
                                      + theme.backgroundSpritePath);
            }
            if (theme.waterTint != Color.white) sr.color = theme.waterTint;
        }

        // Static ambient dressing (none for classic). Grouped under one parent so a future
        // ambient-animation pass has a single hook point per theme.
        if (theme.ambientLayers.Length > 0 && GameObject.Find("PoolThemeAmbient") == null)
        {
            GameObject group = new GameObject("PoolThemeAmbient");
            foreach (PoolTheme.AmbientLayer layer in theme.ambientLayers)
            {
                Sprite s = Resources.Load<Sprite>(layer.spritePath);
                if (s == null) continue;
                GameObject go = new GameObject("Ambient_" + s.name);
                go.transform.SetParent(group.transform, false);
                go.transform.position = layer.position;
                go.transform.localScale = layer.scale;
                SpriteRenderer asr = go.AddComponent<SpriteRenderer>();
                asr.sprite = s;
                asr.sortingOrder = layer.sortingOrder;
            }
        }
    }
}
