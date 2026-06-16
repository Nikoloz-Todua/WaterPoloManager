using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// The real hub TEAM screen (B12), built entirely in code in the same style as
// NavigationManager / MainMenuUI — no prefabs, no Inspector wiring. NavigationManager attaches
// this to its Team screen and calls Build(); everything else is driven by RosterManager +
// PlayerDatabase. Shows the 7-slot formation with the live starters, a scrollable list of owned
// (bench) + buyable (market) players, the team OVR, and working BUY / SELL / UPGRADE / START
// buttons. Each card has a rarity-coloured border and a silhouette where the portrait will go.
public class TeamScreenUI : MonoBehaviour
{
    static readonly Color Navy = new Color(0.05f, 0.1f, 0.25f, 0.92f);
    static readonly Color Panel = new Color(0.03f, 0.06f, 0.16f, 0.85f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    static readonly Color Cyan = new Color(0f, 0.85f, 1f);
    static readonly Color Grey = new Color(0.55f, 0.55f, 0.58f);

    static Sprite roundedSprite;
    static Sprite silhouetteSprite;

    // Formation slot anchored positions inside the pitch panel; index == starter slot == (int)position.
    static readonly Vector2[] SlotAt =
    {
        new Vector2(110f, -140f),  // 0 GK
        new Vector2(-110f, -140f), // 1 CB
        new Vector2(-150f, 130f),  // 2 LW
        new Vector2(150f, 130f),   // 3 RW
        new Vector2(0f, 0f),       // 4 CF
        new Vector2(-175f, 0f),    // 5 LF
        new Vector2(175f, 0f),     // 6 RF
    };
    static readonly string[] PosName = { "GK", "CB", "LW", "RW", "CF", "LF", "RF" };

    private Transform root;
    private NavigationManager nav;
    private TextMeshProUGUI ovrText, goldText, diamondText, emptyHint;
    private RectTransform formationContainer;
    private RectTransform listContent;

    public void Build(Transform parent, NavigationManager navigation)
    {
        root = parent;
        nav = navigation;

        MakeText(root, "TEAM", 40f, new Vector2(0.5f, 1f), new Vector2(0f, -34f),
                 new Vector2(500f, 50f), Color.white, TextAlignmentOptions.Center);

        // header: gold (left), OVR (centre), diamonds (right)
        goldText = MakeText(root, "Gold: 0", 22f, new Vector2(0.5f, 1f), new Vector2(-340f, -84f),
                            new Vector2(280f, 36f), Gold, TextAlignmentOptions.Left);
        ovrText = MakeText(root, "OVR 0", 30f, new Vector2(0.5f, 1f), new Vector2(0f, -84f),
                           new Vector2(220f, 40f), Gold, TextAlignmentOptions.Center);
        diamondText = MakeText(root, "Diamonds: 0", 22f, new Vector2(0.5f, 1f), new Vector2(340f, -84f),
                               new Vector2(280f, 36f), Cyan, TextAlignmentOptions.Right);

        // left: the pitch with the 7 formation slots
        Image pitch = MakePanel(root, new Vector2(540f, 430f), new Vector2(-350f, -40f), Panel);
        formationContainer = NewRect("Formation", pitch.transform);
        formationContainer.anchorMin = Vector2.zero;
        formationContainer.anchorMax = Vector2.one;
        formationContainer.offsetMin = formationContainer.offsetMax = Vector2.zero;

        // right: scrollable owned + market list
        listContent = BuildScroll(root, new Vector2(600f, 430f), new Vector2(330f, -40f));

        emptyHint = MakeText(root, "No players yet.\nRun Tools → Generate Sample Players, then re-open the hub.",
                             20f, new Vector2(0.5f, 0.5f), new Vector2(330f, -40f), new Vector2(560f, 120f),
                             Color.white, TextAlignmentOptions.Center);
        emptyHint.gameObject.SetActive(false);

        Refresh();
    }

    // Rebuild every dynamic part from the current roster + catalog.
    void Refresh()
    {
        RosterManager rm = RosterManager.Instance;
        PlayerDatabase db = PlayerDatabase.Instance;

        ovrText.text = "OVR " + rm.TeamOverall();
        goldText.text = "Gold: " + rm.Coins;
        diamondText.text = "Diamonds: " + rm.Diamonds;
        emptyHint.gameObject.SetActive(db.Count == 0);

        ClearChildren(formationContainer);
        ClearChildren(listContent);

        // formation
        PlayerData[] starters = rm.GetStarters();
        for (int slot = 0; slot < 7; slot++) BuildFormationCard(slot, starters[slot]);

        // list: owned first (so the bench is on top), then the buyable market
        HashSet<string> ownedSeen = new HashSet<string>();
        foreach (PlayerData p in rm.GetOwnedPlayers())
        {
            if (p == null || !ownedSeen.Add(p.id)) continue;
            BuildListRow(p, owned: true);
        }
        foreach (PlayerData p in db.AllPlayers())
        {
            if (p == null || rm.IsOwned(p.id)) continue;
            BuildListRow(p, owned: false);
        }

        if (nav != null) nav.RefreshCurrency();
    }

    // ---------------------------------------------------------------- formation card

    void BuildFormationCard(int slot, PlayerData player)
    {
        Color border = player != null ? player.RarityColor : Grey;
        RectTransform card = MakeCard(formationContainer, new Vector2(118f, 135f), SlotAt[slot], border);

        MakeText(card, PosName[slot], 16f, new Vector2(0.5f, 1f), new Vector2(0f, -16f),
                 new Vector2(110f, 22f), Cyan, TextAlignmentOptions.Center);

        Image silo = NewImage("Silo", card);
        silo.sprite = (player != null && player.portrait != null) ? player.portrait : Silhouette();
        silo.preserveAspect = true;
        silo.raycastTarget = false;
        SetRect(silo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(58f, 58f));

        if (player == null)
        {
            MakeText(card, "EMPTY", 15f, new Vector2(0.5f, 0f), new Vector2(0f, 22f),
                     new Vector2(110f, 22f), Grey, TextAlignmentOptions.Center);
            return;
        }

        MakeText(card, Short(player.fullName), 15f, new Vector2(0.5f, 0f), new Vector2(0f, 32f),
                 new Vector2(112f, 22f), Color.white, TextAlignmentOptions.Center);
        MakeText(card, "OVR " + player.overall, 15f, new Vector2(0.5f, 0f), new Vector2(0f, 12f),
                 new Vector2(112f, 22f), Gold, TextAlignmentOptions.Center);
    }

    // ---------------------------------------------------------------- list row

    void BuildListRow(PlayerData player, bool owned)
    {
        string id = player.id; // local copy for the button closures
        RosterManager rm = RosterManager.Instance;
        bool starter = owned && rm.IsStarter(id);

        RectTransform row = MakeCard(listContent, new Vector2(0f, 92f), Vector2.zero, player.RarityColor);
        LayoutElement le = row.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 92f; le.preferredHeight = 92f; le.flexibleWidth = 1f;

        Image silo = NewImage("Silo", row);
        silo.sprite = player.portrait != null ? player.portrait : Silhouette();
        silo.preserveAspect = true;
        silo.raycastTarget = false;
        SetRect(silo.rectTransform, new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(60f, 60f));

        MakeText(row, player.fullName, 20f, new Vector2(0f, 0.5f), new Vector2(86f, 16f),
                 new Vector2(270f, 28f), Color.white, TextAlignmentOptions.Left);

        string status = !owned ? player.priceGold + " g" : starter ? "STARTER" : "BENCH";
        Color statusColor = !owned ? Gold : starter ? Cyan : Color.white;
        MakeText(row, PosName[(int)player.position] + "  ·  OVR " + player.overall + "  ·  " + player.rarity,
                 15f, new Vector2(0f, 0.5f), new Vector2(86f, -16f), new Vector2(320f, 22f),
                 new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Left);
        MakeText(row, status, 16f, new Vector2(0f, 0.5f), new Vector2(360f, 16f),
                 new Vector2(140f, 24f), statusColor, TextAlignmentOptions.Left);

        if (!owned)
        {
            int price = player.priceGold;
            Button buy = MakeButton(row, "BUY " + price, new Vector2(120f, 44f), new Vector2(1f, 0.5f),
                                    new Vector2(-66f, 0f), 16f, () => { rm.BuyPlayer(id); Refresh(); });
            SetAffordable(buy, rm.Coins >= price);
        }
        else
        {
            if (!starter)
                MakeButton(row, "START", new Vector2(92f, 40f), new Vector2(1f, 0.5f),
                           new Vector2(-168f, 24f), 15f,
                           () => { rm.SetStarter((int)player.position, id); Refresh(); });

            int upCost = rm.UpgradeCost(player);
            Button up = MakeButton(row, "UP " + upCost, new Vector2(92f, 40f), new Vector2(1f, 0.5f),
                                   new Vector2(-168f, -24f), 15f, () => { rm.UpgradePlayer(id); Refresh(); });
            SetAffordable(up, rm.Coins >= upCost);

            MakeButton(row, "SELL +" + rm.SellValue(player), new Vector2(92f, 40f), new Vector2(1f, 0.5f),
                       new Vector2(-58f, 0f), 14f, () => { rm.SellPlayer(id); Refresh(); });
        }
    }

    static void SetAffordable(Button btn, bool affordable)
    {
        if (affordable) return;
        btn.interactable = false;
        Transform fill = btn.transform.Find("Fill");
        if (fill != null) fill.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.28f, 1f);
    }

    // ---------------------------------------------------------------- scroll view

    RectTransform BuildScroll(Transform parent, Vector2 size, Vector2 pos)
    {
        GameObject scrollGo = new GameObject("PlayerScroll");
        scrollGo.transform.SetParent(parent, false);
        RectTransform srt = scrollGo.AddComponent<RectTransform>();
        SetRect(srt, new Vector2(0.5f, 0.5f), pos, size);
        Image bg = scrollGo.AddComponent<Image>();
        bg.sprite = Rounded(); bg.type = Image.Type.Sliced; bg.color = Panel;

        ScrollRect sr = scrollGo.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true; sr.scrollSensitivity = 24f;
        sr.movementType = ScrollRect.MovementType.Clamped;

        GameObject vp = new GameObject("Viewport");
        vp.transform.SetParent(scrollGo.transform, false);
        RectTransform vrt = vp.AddComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(6f, 6f); vrt.offsetMax = new Vector2(-6f, -6f);
        vrt.pivot = new Vector2(0.5f, 1f);
        Image vpImg = vp.AddComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.01f); // near-invisible, but a raycast target so empty space scrolls
        vp.AddComponent<RectMask2D>();
        sr.viewport = vrt;

        GameObject content = new GameObject("Content");
        content.transform.SetParent(vp.transform, false);
        RectTransform crt = content.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.offsetMin = crt.offsetMax = Vector2.zero;
        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f; vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        sr.content = crt;

        return crt;
    }

    // ---------------------------------------------------------------- UI helpers

    static void ClearChildren(Transform t)
    {
        if (t == null) return;
        for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    Image NewImage(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<Image>();
    }

    Image MakePanel(Transform parent, Vector2 size, Vector2 pos, Color color)
    {
        Image img = NewImage("Panel", parent);
        img.sprite = Rounded(); img.type = Image.Type.Sliced; img.color = color;
        SetRect(img.rectTransform, new Vector2(0.5f, 0.5f), pos, size);
        return img;
    }

    // A rarity-bordered card: a coloured rounded frame with a navy fill inset. Returns the
    // frame's RectTransform — children added to it render above the fill.
    RectTransform MakeCard(Transform parent, Vector2 size, Vector2 pos, Color border)
    {
        Image frame = NewImage("Card", parent);
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced; frame.color = border;
        SetRect(frame.rectTransform, new Vector2(0.5f, 0.5f), pos, size);

        Image fill = NewImage("Fill", frame.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced; fill.color = Navy;
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);

        return frame.rectTransform;
    }

    TextMeshProUGUI MakeText(Transform parent, string content, float size, Vector2 anchor,
                             Vector2 pos, Vector2 box, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = content; txt.fontSize = size; txt.fontStyle = FontStyles.Bold;
        txt.color = color; txt.alignment = align; txt.raycastTarget = false;
        txt.overflowMode = TextOverflowModes.Ellipsis;
        SetRect(txt.rectTransform, anchor, pos, box);
        return txt;
    }

    // Cyan-bordered navy button with a 1.05x hover — same look as NavigationManager's buttons.
    Button MakeButton(Transform parent, string label, Vector2 size, Vector2 anchor, Vector2 pos,
                      float fontSize, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, pos, size);

        Image border = go.AddComponent<Image>();
        border.sprite = Rounded(); border.type = Image.Type.Sliced; border.color = Cyan;

        Image fill = NewImage("Fill", go.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced; fill.color = new Color(0.05f, 0.1f, 0.25f, 1f);
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = border;
        if (onClick != null) btn.onClick.AddListener(onClick);

        TextMeshProUGUI txt = MakeText(go.transform, label, fontSize, new Vector2(0.5f, 0.5f),
                                       Vector2.zero, Vector2.zero, Color.white, TextAlignmentOptions.Center);
        RectTransform trt = txt.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        AddHover(go);
        return btn;
    }

    static void AddHover(GameObject go)
    {
        EventTrigger trigger = go.AddComponent<EventTrigger>();
        EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => { if (go != null) go.transform.localScale = Vector3.one * 1.05f; });
        trigger.triggers.Add(enter);
        EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => { if (go != null) go.transform.localScale = Vector3.one; });
        trigger.triggers.Add(exit);
    }

    static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static string Short(string s)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= 12 ? s : s.Substring(0, 12));

    // ---------------------------------------------------------------- generated sprites

    static Sprite Rounded()
    {
        if (roundedSprite != null) return roundedSprite;
        const int size = 64, corner = 14;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        float half = size * 0.5f - 0.5f, inner = half - corner;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Max(Mathf.Abs(x - half) - inner, 0f);
                float qy = Mathf.Max(Mathf.Abs(y - half) - inner, 0f);
                float d = Mathf.Sqrt(qx * qx + qy * qy);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(corner - d) * 255f));
            }
        tex.SetPixels32(px); tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
        roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                      100f, 0, SpriteMeshType.FullRect,
                                      new Vector4(corner + 2, corner + 2, corner + 2, corner + 2));
        return roundedSprite;
    }

    // A neutral head-and-shoulders silhouette placeholder (until real portraits exist).
    static Sprite Silhouette()
    {
        if (silhouetteSprite != null) return silhouetteSprite;
        const int s = 96;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[s * s];
        Color32 body = new Color32(150, 162, 184, 255);
        Color32 clear = new Color32(0, 0, 0, 0);
        Vector2 head = new Vector2(s * 0.5f, s * 0.64f);
        float headR = s * 0.19f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool inHead = Vector2.Distance(new Vector2(x, y), head) <= headR;
                float bx = Mathf.Abs(x - s * 0.5f);
                float tNeck = Mathf.InverseLerp(0.46f * s, 0.10f * s, y); // 0 at neck, 1 at bottom
                float halfW = Mathf.Lerp(s * 0.20f, s * 0.34f, Mathf.Clamp01(tNeck));
                bool inBody = y >= 0.10f * s && y <= 0.46f * s && bx <= halfW;
                px[y * s + x] = (inHead || inBody) ? body : clear;
            }
        tex.SetPixels32(px); tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
        silhouetteSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        return silhouetteSprite;
    }
}
