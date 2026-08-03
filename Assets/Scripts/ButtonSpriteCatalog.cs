using System;
using System.Collections.Generic;
using UnityEngine;

// Runtime-safe bridge to the button artwork that intentionally lives outside Resources at
// Assets/Sprites/Buttons. ButtonSpriteCatalogBuilder creates the Resources asset and stores direct
// Sprite references plus editor-measured alpha bounds, so player builds never depend on AssetDatabase
// or obsolete Resources/Sprites button copies.
[CreateAssetMenu(fileName = "ButtonSpriteCatalog", menuName = "Water Polo/Button Sprite Catalog")]
public sealed class ButtonSpriteCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string key;
        public Sprite sprite;
        public Rect visibleRect01 = new Rect(0f, 0f, 1f, 1f);
    }

    [SerializeField] int buildRevision;
    [SerializeField] List<Entry> buttons = new List<Entry>();

    static ButtonSpriteCatalog instance;
    readonly Dictionary<string, Sprite> cropped = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

    public int BuildRevision => buildRevision;
    public IReadOnlyList<Entry> Buttons => buttons;

    public static ButtonSpriteCatalog Instance
    {
        get
        {
            if (instance == null) instance = Resources.Load<ButtonSpriteCatalog>("ButtonSpriteCatalog");
            return instance;
        }
    }

    // Enter Play Mode can be configured without a domain reload. Reset the static handle anyway so
    // a catalog rebuilt by the editor is reloaded instead of reusing an earlier empty asset instance.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        if (instance != null) instance.cropped.Clear();
        instance = null;
    }

    public static Sprite SpriteFor(string key, bool cropTransparentMargin = true)
    {
        ButtonSpriteCatalog catalog = Instance;
        return catalog != null ? catalog.Get(key, cropTransparentMargin) : null;
    }

    public static Sprite SpriteForLegacyPath(string path, bool cropTransparentMargin = true)
    {
        string key = KeyForLegacyPath(path);
        return string.IsNullOrEmpty(key) ? null : SpriteFor(key, cropTransparentMargin);
    }

    public Sprite Get(string key, bool cropTransparentMargin = true)
    {
        if (string.IsNullOrEmpty(key)) return null;
        Entry entry = null;
        foreach (Entry candidate in buttons)
        {
            if (candidate == null || !string.Equals(candidate.key, key, StringComparison.OrdinalIgnoreCase))
                continue;
            entry = candidate;
            break;
        }
        if (entry == null || entry.sprite == null)
            return null;
        if (!cropTransparentMargin) return entry.sprite;
        if (cropped.TryGetValue(entry.key, out Sprite cached) && cached != null) return cached;

        Rect source = entry.sprite.rect;
        Rect n = entry.visibleRect01;
        if (n.width <= 0f || n.height <= 0f) n = new Rect(0f, 0f, 1f, 1f);
        Rect visible = new Rect(
            source.x + Mathf.Clamp01(n.x) * source.width,
            source.y + Mathf.Clamp01(n.y) * source.height,
            Mathf.Clamp01(n.width) * source.width,
            Mathf.Clamp01(n.height) * source.height);
        visible.width = Mathf.Clamp(visible.width, 1f, source.width);
        visible.height = Mathf.Clamp(visible.height, 1f, source.height);

        Sprite result = Sprite.Create(entry.sprite.texture, visible, new Vector2(0.5f, 0.5f),
            entry.sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        result.name = entry.key + "_RuntimeCropped";
        cropped[entry.key] = result;
        return result;
    }

    public static string KeyForLegacyPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string name = path.Replace('\\', '/');
        int slash = name.LastIndexOf('/');
        if (slash >= 0) name = name.Substring(slash + 1);
        switch (name.ToLowerInvariant())
        {
            case "play-button": return "Play-Button";
            case "ranking-button": return "Ranking-Button";
            case "season-pass":
            case "season-pass-button": return "Season-Pass";
            case "shop-button": return "Shop-Button";
            case "clubs-button": return "Clubs-Button";
            case "friends-button": return "Friends-Button";
            case "settings-button": return "Settings-Button";
            case "back-button": return "Back-Button";
            case "message-button": return "Message-Button";
            case "gifts-button": return "Gifts-Button";
            case "i-button": return "I-Button";
            case "pause-button": return "Pause-Button";
            case "lock-sign":
            case "lock-button": return "Lock-Button";
            case "defend":
            case "defend-button": return "Defend-Button";
            case "pass":
            case "pass-button": return "Pass-Button";
            case "shoot":
            case "shoot-button": return "Shoot-Button";
            case "sprint":
            case "sprint-button": return "Sprint-Button";
            case "switch":
            case "switch-button": return "Switch-Button";
            case "goalkeeper-button":
            case "keeper-button": return "Keeper-Button";
            case "center-button": return "Center-Button";
            case "defender-button": return "Defender-Button";
            case "wings-button":
            case "wing-button": return "Wing-Button";
            case "team-button": return "Team-Button";
            case "missions-button": return "Missions-Button";
            default: return null;
        }
    }
}
