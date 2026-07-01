using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The SHOP screen — built entirely in code (no prefabs), same runtime-build style as
// NavigationManager / TeamScreenUI. Hosted by NavigationManager's shop overlay; the back arrow
// calls nav.CloseShopScreen().
//
// Layout: 80px top bar (back | SHOP + gear | event badge | gold/gems with [+]) — content area —
// bottom horizontal scroll row of 9 tabs. Tab content is rebuilt on each switch.
//
// Honesty notes (see the task summary): DAILY DEALS / FREE PRIZES cooldowns are local
// PlayerPrefs timers (no server), ADS PACK fakes the ad with a short spinner (TODO: real ad SDK),
// COINS/GEMS/Legendary-$2.99 route through IAPBridge (stub — grants immediately), DRAFT TICKETS
// and EVENT are declared placeholders for systems that don't exist yet.
public class ShopUI : MonoBehaviour
{
    static readonly Color DarkBar = new Color(0.04f, 0.06f, 0.13f, 0.86f);
    static readonly Color Panel = new Color(0.03f, 0.05f, 0.11f, 0.92f);
    static readonly Color CardFill = new Color(0.07f, 0.12f, 0.19f, 0.97f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    static readonly Color Cyan = new Color(0f, 0.85f, 1f);
    static readonly Color Green = new Color(0.2f, 0.72f, 0.32f);
    static readonly Color Grey = new Color(0.55f, 0.6f, 0.68f);

    static Sprite rounded, circle;

    static readonly string[] TabNames =
        { "OFFERS", "PACKS", "DAILY DEALS", "FREE PRIZES", "ADS PACK", "COINS", "GEMS", "DRAFT TICKETS", "EVENT" };

    Transform root;
    NavigationManager nav;
    TextMeshProUGUI goldLabel, gemLabel;
    RectTransform contentArea;
    readonly List<Image> tabFaces = new List<Image>();
    int tab;
    GameObject popup;         // drop-rate info popup (destroyed on close)
    TextMeshProUGUI toastLabel;
    Coroutine toastRoutine;

    public void Build(Transform parent, NavigationManager navigation)
    {
        root = parent;
        nav = navigation;

        Image bg = NewImage("Background", root);
        bg.color = new Color(0.03f, 0.07f, 0.13f, 1f);
        bg.raycastTarget = true; // swallow clicks
        Stretch(bg.rectTransform);
        Sprite art = LoadAnySprite("Sprites/competition-page-background");
        if (art != null)
        {
            Image artImg = NewImage("BgArt", root);
            artImg.sprite = art;
            artImg.color = new Color(0.45f, 0.45f, 0.5f, 1f); // dimmed
            artImg.raycastTarget = false;
            Stretch(artImg.rectTransform);
        }

        BuildTopBar();
        BuildContentArea();
        BuildTabRow();
        SelectTab(0);
    }

    void OnEnable() { RefreshCurrency(); } // overlay re-opened → fresh balances

    public void RefreshCurrency()
    {
        RosterManager rm = RosterManager.Instance;
        if (goldLabel != null) goldLabel.text = rm.Coins.ToString();
        if (gemLabel != null) gemLabel.text = rm.Diamonds.ToString();
        if (nav != null) nav.RefreshCurrency();
    }

    // ------------------------------------------------------------------ top bar

    void BuildTopBar()
    {
        Image bar = NewImage("TopBar", root);
        bar.sprite = Rounded(); bar.type = Image.Type.Sliced;
        bar.color = DarkBar;
        bar.raycastTarget = true;
        RectTransform rt = bar.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 80f);

        // Back arrow → hub.
        Sprite back = LoadAnySprite("Sprites/back-button");
        GameObject bgo = new GameObject("BtnBack");
        bgo.transform.SetParent(bar.transform, false);
        SetRect(bgo.AddComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(64f, 64f));
        Image bimg = bgo.AddComponent<Image>();
        if (back != null) { bimg.sprite = back; bimg.preserveAspect = true; }
        else { bimg.sprite = Rounded(); bimg.type = Image.Type.Sliced; bimg.color = new Color(0.16f, 0.2f, 0.28f, 1f); }
        Button bbtn = bgo.AddComponent<Button>();
        bbtn.targetGraphic = bimg;
        bbtn.onClick.AddListener(() => { if (nav != null) nav.CloseShopScreen(); });

        MakeText(bar.transform, "SHOP", 34f, new Vector2(0f, 0.5f), new Vector2(170f, 0f),
                 new Vector2(160f, 50f), Color.white, TextAlignmentOptions.Center);

        // Settings gear (same circle-placeholder pattern as the hub's).
        GameObject gear = new GameObject("BtnGear");
        gear.transform.SetParent(bar.transform, false);
        SetRect(gear.AddComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(268f, 0f), new Vector2(44f, 44f));
        Image gimg = gear.AddComponent<Image>();
        gimg.sprite = Circle();
        gimg.color = new Color(0.25f, 0.28f, 0.36f, 1f);
        Image ginner = NewImage("Inner", gear.transform);
        ginner.sprite = Circle();
        ginner.color = new Color(0.6f, 0.63f, 0.7f, 1f);
        ginner.raycastTarget = false;
        SetRect(ginner.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
        Button gbtn = gear.AddComponent<Button>();
        gbtn.targetGraphic = gimg;
        gbtn.onClick.AddListener(() => Debug.Log("Shop settings coming soon"));

        // Event badge stub → jumps to the EVENT tab.
        Button ev = MakeButton(bar.transform, "EVENT  02D 10H", 15f, new Vector2(0.5f, 0.5f),
                               new Vector2(-40f, 0f), new Vector2(190f, 46f),
                               new Color(0.45f, 0.2f, 0.55f, 1f), () => SelectTab(8));

        // Currencies (right→left): gold [+], gold, gem [+], gems. [+] opens the buy tabs.
        MakePlus(bar.transform, new Vector2(-30f, 0f), () => SelectTab(5));
        goldLabel = MakeText(bar.transform, "0", 18f, new Vector2(1f, 0.5f), new Vector2(-88f, 0f),
                             new Vector2(66f, 30f), Color.white, TextAlignmentOptions.Right);
        MakeIcon(bar.transform, "Sprites/gold-coin", new Vector2(-140f, 0f), 32f);
        MakePlus(bar.transform, new Vector2(-186f, 0f), () => SelectTab(6));
        gemLabel = MakeText(bar.transform, "0", 18f, new Vector2(1f, 0.5f), new Vector2(-240f, 0f),
                            new Vector2(56f, 30f), Color.white, TextAlignmentOptions.Right);
        MakeIcon(bar.transform, "Sprites/diamond-coin", new Vector2(-288f, 0f), 32f);
    }

    void MakePlus(Transform bar, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("BtnPlus");
        go.transform.SetParent(bar, false);
        SetRect(go.AddComponent<RectTransform>(), new Vector2(1f, 0.5f), pos, new Vector2(28f, 28f));
        Image img = go.AddComponent<Image>();
        img.sprite = Rounded(); img.type = Image.Type.Sliced; img.color = Green;
        Button b = go.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);
        TextMeshProUGUI t = MakeText(go.transform, "+", 20f, new Vector2(0.5f, 0.5f), Vector2.zero,
                                     new Vector2(28f, 28f), Color.white, TextAlignmentOptions.Center);
        Stretch(t.rectTransform);
    }

    void MakeIcon(Transform bar, string path, Vector2 pos, float size)
    {
        Image img = NewImage("Icon", bar);
        img.sprite = LoadAnySprite(path);
        img.preserveAspect = true;
        img.raycastTarget = false;
        if (img.sprite == null) img.color = Gold;
        SetRect(img.rectTransform, new Vector2(1f, 0.5f), pos, new Vector2(size, size));
    }

    // ------------------------------------------------------------------ tabs

    void BuildContentArea()
    {
        GameObject go = new GameObject("Content");
        go.transform.SetParent(root, false);
        contentArea = go.AddComponent<RectTransform>();
        contentArea.anchorMin = Vector2.zero;
        contentArea.anchorMax = Vector2.one;
        contentArea.offsetMin = new Vector2(0f, 70f);   // above the tab row
        contentArea.offsetMax = new Vector2(0f, -84f);  // below the top bar
    }

    void BuildTabRow()
    {
        const float tabW = 152f, tabH = 52f, gap = 6f;
        GameObject scrollGo = new GameObject("TabScroll");
        scrollGo.transform.SetParent(root, false);
        RectTransform srt = scrollGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(1f, 0f);
        srt.pivot = new Vector2(0.5f, 0f);
        srt.anchoredPosition = Vector2.zero;
        srt.sizeDelta = new Vector2(0f, 66f);
        Image sbg = scrollGo.AddComponent<Image>();
        sbg.color = new Color(0.02f, 0.03f, 0.08f, 0.9f);
        scrollGo.AddComponent<RectMask2D>();

        GameObject contentGo = new GameObject("TabContent");
        contentGo.transform.SetParent(scrollGo.transform, false);
        RectTransform crt = contentGo.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 0f);
        crt.anchorMax = new Vector2(0f, 1f);
        crt.pivot = new Vector2(0f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(TabNames.Length * (tabW + gap) + gap, 0f);

        ScrollRect scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.viewport = srt;
        scroll.content = crt;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;

        tabFaces.Clear();
        for (int i = 0; i < TabNames.Length; i++)
        {
            int idx = i;
            GameObject go = new GameObject("Tab_" + TabNames[i]);
            go.transform.SetParent(contentGo.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(gap + i * (tabW + gap), 0f);
            rt.sizeDelta = new Vector2(tabW, tabH);
            Image face = go.AddComponent<Image>();
            face.sprite = Rounded(); face.type = Image.Type.Sliced;
            tabFaces.Add(face);
            Button b = go.AddComponent<Button>();
            b.targetGraphic = face;
            b.onClick.AddListener(() => SelectTab(idx));
            TextMeshProUGUI t = MakeText(go.transform, TabNames[i], 15f, new Vector2(0.5f, 0.5f),
                                         Vector2.zero, new Vector2(tabW - 6f, tabH), Color.white,
                                         TextAlignmentOptions.Center);
            Stretch(t.rectTransform);
        }
        SyncTabFaces();
    }

    void SyncTabFaces()
    {
        for (int i = 0; i < tabFaces.Count; i++)
            tabFaces[i].color = i == tab ? new Color(0.13f, 0.3f, 0.45f, 1f)
                                         : new Color(0.06f, 0.1f, 0.16f, 1f);
    }

    void SelectTab(int index)
    {
        tab = index;
        SyncTabFaces();
        for (int i = contentArea.childCount - 1; i >= 0; i--) Destroy(contentArea.GetChild(i).gameObject);
        switch (tab)
        {
            case 0: BuildOffersTab(); break;
            case 1: BuildPacksTab(); break;
            case 2: BuildDailyDealsTab(); break;
            case 3: BuildFreePrizesTab(); break;
            case 4: BuildAdsTab(); break;
            case 5: BuildCurrencyTab(true); break;
            case 6: BuildCurrencyTab(false); break;
            case 7: BuildDraftTab(); break;
            case 8: BuildEventTab(); break;
        }
    }

    // ------------------------------------------------------------------ tab: OFFERS

    void BuildOffersTab()
    {
        // "Coach's Choice" featured offer (left): Gold-pack contents + 500 coins at a deal price.
        Image feat = MakeCard(contentArea, new Vector2(-425f, 0f), new Vector2(370f, 470f), Gold);
        MakeText(feat.transform, "COACH'S CHOICE", 24f, new Vector2(0.5f, 1f), new Vector2(0f, -34f),
                 new Vector2(340f, 32f), Gold, TextAlignmentOptions.Center);
        Image fart = NewImage("Art", feat.transform);
        fart.sprite = LoadAnySprite(CardPack.TierSprite(CardTier.Epic));
        fart.preserveAspect = true;
        fart.raycastTarget = false;
        if (fart.sprite == null) fart.color = CardPack.TierColor(CardTier.Epic);
        SetRect(fart.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 55f), new Vector2(190f, 190f));
        MakeText(feat.transform, "GOLD PACK CARDS\n+ 500 COINS", 20f, new Vector2(0.5f, 0.5f),
                 new Vector2(0f, -85f), new Vector2(320f, 60f), Color.white, TextAlignmentOptions.Center);
        MakeText(feat.transform, "250  (was 400)", 16f, new Vector2(0.5f, 0f), new Vector2(0f, 108f),
                 new Vector2(300f, 24f), Grey, TextAlignmentOptions.Center);
        MakeButton(feat.transform, "BUY  250 GEMS", 20f, new Vector2(0.5f, 0f), new Vector2(0f, 56f),
                   new Vector2(280f, 62f), Green, () =>
        {
            if (!RosterManager.Instance.SpendDiamonds(250)) { Toast("NOT ENOUGH GEMS"); return; }
            RosterManager.Instance.AddCoins(500);
            OpenAndReveal(ShopPackType.Gold);
        });

        // The 4 shop packs in a horizontal scroll to the right of the feature.
        BuildPackRow(new Vector2(190f, 0f), new Vector2(830f, 480f), null);
    }

    // ------------------------------------------------------------------ tab: PACKS

    void BuildPacksTab() => BuildPackRow(new Vector2(0f, 0f), new Vector2(1220f, 490f), null);

    // Horizontal scroll of the 4 shop pack cards. `discounts` (nullable, per ShopPackType index)
    // shows a % badge + reduced gem price — used by DAILY DEALS.
    void BuildPackRow(Vector2 center, Vector2 size, int[] discounts)
    {
        const float cardW = 250f, cardH = 400f, gap = 20f;
        GameObject scrollGo = new GameObject("PackScroll");
        scrollGo.transform.SetParent(contentArea, false);
        RectTransform srt = scrollGo.AddComponent<RectTransform>();
        SetRect(srt, new Vector2(0.5f, 0.5f), center, size);
        Image sbg = scrollGo.AddComponent<Image>();
        sbg.color = new Color(0f, 0f, 0f, 0f);
        scrollGo.AddComponent<RectMask2D>();

        GameObject contentGo = new GameObject("PackContent");
        contentGo.transform.SetParent(scrollGo.transform, false);
        RectTransform crt = contentGo.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 0f);
        crt.anchorMax = new Vector2(0f, 1f);
        crt.pivot = new Vector2(0f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        int count = 4;
        crt.sizeDelta = new Vector2(count * (cardW + gap) + gap, 0f);

        ScrollRect scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.viewport = srt;
        scroll.content = crt;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;

        for (int i = 0; i < count; i++)
        {
            ShopPackType type = (ShopPackType)i;
            int disc = discounts != null ? discounts[i] : 0;
            if (discounts != null && disc <= 0) continue; // deals tab: only discounted packs
            BuildShopPackCard(contentGo.transform, type,
                new Vector2(gap + cardW * 0.5f + i * (cardW + gap), 0f), new Vector2(cardW, cardH), disc);
        }
    }

    void BuildShopPackCard(Transform parent, ShopPackType type, Vector2 pos, Vector2 size, int discountPct)
    {
        CardPack.ShopPackDef def = CardPack.GetShopPack(type);
        Color tint = CardPack.TierColor(def.TierForArt);

        GameObject go = new GameObject("Pack_" + def.name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image frame = go.AddComponent<Image>();
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced;
        frame.color = tint;
        Image fill = NewImage("Fill", go.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced;
        fill.color = CardFill;
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);

        Image art = NewImage("Art", go.transform);
        art.sprite = LoadAnySprite(def.SpritePath);
        art.preserveAspect = true;
        art.raycastTarget = false;
        if (art.sprite == null) art.color = tint;
        SetRect(art.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -105f), new Vector2(150f, 150f));

        MakeText(go.transform, def.name, 20f, new Vector2(0.5f, 1f), new Vector2(0f, -202f),
                 new Vector2(size.x - 16f, 28f), tint, TextAlignmentOptions.Center);
        MakeText(go.transform, "UP TO " + def.maxCards + " PLAYERS", 15f, new Vector2(0.5f, 1f),
                 new Vector2(0f, -228f), new Vector2(size.x - 16f, 22f), Color.white, TextAlignmentOptions.Center);

        // "i" info → drop-rate popup (image-2 layout).
        GameObject info = new GameObject("BtnInfo");
        info.transform.SetParent(go.transform, false);
        SetRect(info.AddComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-26f, -26f), new Vector2(44f, 44f));
        Image iimg = info.AddComponent<Image>();
        Sprite isp = LoadAnySprite("Sprites/i-button");
        if (isp != null) { iimg.sprite = isp; iimg.preserveAspect = true; }
        else { iimg.sprite = Circle(); iimg.color = Cyan; }
        Button ibtn = info.AddComponent<Button>();
        ibtn.targetGraphic = iimg;
        ibtn.onClick.AddListener(() => ShowDropPopup(def));

        // Discount badge (DAILY DEALS).
        int gemPrice = def.priceGems;
        if (discountPct > 0)
        {
            gemPrice = Mathf.Max(10, Mathf.RoundToInt(def.priceGems * (100 - discountPct) / 100f / 10f) * 10);
            Image badge = NewImage("Discount", go.transform);
            badge.sprite = Rounded(); badge.type = Image.Type.Sliced;
            badge.color = new Color(0.85f, 0.2f, 0.2f, 1f);
            badge.raycastTarget = false;
            SetRect(badge.rectTransform, new Vector2(0f, 1f), new Vector2(44f, -26f), new Vector2(72f, 30f));
            TextMeshProUGUI bt = MakeText(badge.transform, "-" + discountPct + "%", 16f,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(72f, 30f), Color.white, TextAlignmentOptions.Center);
            Stretch(bt.rectTransform);
        }

        // Buy buttons: gems always; plus WATCH (Basic) or $ (Legendary).
        int price = gemPrice;
        bool twoButtons = def.watchAdOption || def.realMoney != null;
        float mainY = twoButtons ? 88f : 60f;
        MakeButton(go.transform, price + " GEMS", 18f, new Vector2(0.5f, 0f), new Vector2(0f, mainY),
                   new Vector2(size.x - 40f, 50f), Green, () =>
        {
            if (!RosterManager.Instance.SpendDiamonds(price)) { Toast("NOT ENOUGH GEMS"); return; }
            OpenAndReveal(type);
        });
        if (def.watchAdOption)
            MakeButton(go.transform, "WATCH", 17f, new Vector2(0.5f, 0f), new Vector2(0f, 34f),
                       new Vector2(size.x - 40f, 44f), new Color(0.18f, 0.5f, 1f, 1f),
                       () => StartCoroutine(FakeAdThen(() => OpenAndReveal(type))));
        if (def.realMoney != null)
            MakeButton(go.transform, def.realMoney, 17f, new Vector2(0.5f, 0f), new Vector2(0f, 34f),
                       new Vector2(size.x - 40f, 44f), Gold,
                       () => IAPBridge.PurchaseProduct("pack_" + def.type.ToString().ToLower(),
                                                       () => OpenAndReveal(type)));
    }

    // Buy succeeded → open, grant (dupes → coins), reveal.
    void OpenAndReveal(ShopPackType type)
    {
        List<CardPack.GrantResult> results = CardPack.GrantAll(CardPack.OpenShopPack(type));
        RefreshCurrency();
        PackRevealUI.Show(root, results, RefreshCurrency);
    }

    // Drop-rate popup: "chance of at least one card of X rarity" rows (image-2 layout).
    void ShowDropPopup(CardPack.ShopPackDef def)
    {
        ClosePopup();
        popup = new GameObject("DropPopup");
        popup.transform.SetParent(root, false);
        popup.transform.SetAsLastSibling();
        Stretch(popup.AddComponent<RectTransform>());
        Image dark = popup.AddComponent<Image>();
        dark.color = new Color(0.02f, 0.03f, 0.08f, 0.9f);
        Button db = popup.AddComponent<Button>();
        db.targetGraphic = dark;
        db.onClick.AddListener(ClosePopup);

        Image sheet = MakeCard(popup.transform, Vector2.zero, new Vector2(560f, 430f),
                               CardPack.TierColor(def.TierForArt));
        MakeText(sheet.transform, def.name, 26f, new Vector2(0.5f, 1f), new Vector2(0f, -40f),
                 new Vector2(500f, 34f), CardPack.TierColor(def.TierForArt), TextAlignmentOptions.Center);
        MakeText(sheet.transform, "UP TO " + def.maxCards + " PLAYERS", 17f, new Vector2(0.5f, 1f),
                 new Vector2(0f, -76f), new Vector2(500f, 24f), Color.white, TextAlignmentOptions.Center);
        MakeText(sheet.transform, "CHANCE OF AT LEAST ONE CARD OF:", 15f, new Vector2(0.5f, 1f),
                 new Vector2(0f, -112f), new Vector2(500f, 22f), Grey, TextAlignmentOptions.Center);

        float y = -152f;
        foreach (var (rarity, chance) in def.dropTable)
        {
            Image dot = NewImage("Dot", sheet.transform);
            dot.sprite = Circle();
            dot.color = PlayerData.RarityTint(rarity);
            dot.raycastTarget = false;
            SetRect(dot.rectTransform, new Vector2(0.5f, 1f), new Vector2(-150f, y), new Vector2(22f, 22f));
            MakeText(sheet.transform, rarity.ToString().ToUpper(), 18f, new Vector2(0.5f, 1f),
                     new Vector2(-20f, y), new Vector2(220f, 26f), Color.white, TextAlignmentOptions.Left);
            MakeText(sheet.transform, chance >= 1f ? "GUARANTEED" : (chance * 100f).ToString("0.#") + "%",
                     18f, new Vector2(0.5f, 1f), new Vector2(140f, y), new Vector2(180f, 26f), Gold,
                     TextAlignmentOptions.Right);
            y -= 40f;
        }
        MakeButton(sheet.transform, "OK", 20f, new Vector2(0.5f, 0f), new Vector2(0f, 44f),
                   new Vector2(180f, 54f), Green, ClosePopup);
    }

    void ClosePopup() { if (popup != null) { Destroy(popup); popup = null; } }

    // ------------------------------------------------------------------ tab: DAILY DEALS

    void BuildDailyDealsTab()
    {
        // Session-simple rotation: the deal set + discounts derive from the UTC day number, and the
        // countdown targets the next UTC midnight. Local-only (no server) — resets are per-device.
        long day = (long)(DateTime.UtcNow - new DateTime(2026, 1, 1)).TotalDays;
        int[] discounts = new int[4];
        for (int i = 0; i < 4; i++) discounts[i] = 0;
        // 3 of the 4 packs are on sale each day; which one sits out rotates daily.
        int skip = (int)(day % 4);
        int[] pcts = { 30, 40, 50 };
        int pi = 0;
        for (int i = 0; i < 4; i++)
            if (i != skip) discounts[i] = pcts[(pi++ + (int)day) % pcts.Length];

        TimeSpan left = DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;
        MakeText(contentArea, "DEALS REFRESH IN " + (int)left.TotalHours + "H " + left.Minutes + "M",
                 18f, new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(600f, 26f), Cyan,
                 TextAlignmentOptions.Center);
        BuildPackRow(new Vector2(0f, -20f), new Vector2(1220f, 440f), discounts);
    }

    // ------------------------------------------------------------------ tab: FREE PRIZES

    void BuildFreePrizesTab()
    {
        // Local 6h cooldowns via PlayerPrefs ticks (same pattern as the division unlocks) —
        // no server, so clearing PlayerPrefs resets them.
        BuildFreePrize(0, "100 COINS", new Vector2(-380f, 20f), () => RosterManager.Instance.AddCoins(100));
        BuildFreePrize(1, "10 GEMS", new Vector2(0f, 20f), () => RosterManager.Instance.AddDiamonds(10));
        BuildFreePrize(2, "FREE BASIC PACK", new Vector2(380f, 20f), () => OpenAndReveal(ShopPackType.Basic));
    }

    void BuildFreePrize(int index, string label, Vector2 pos, Action grant)
    {
        const double cooldownHours = 6;
        string key = "shop_free_" + index;
        Image card = MakeCard(contentArea, pos, new Vector2(340f, 300f), new Color(0.227f, 0.353f, 0.478f, 1f));
        MakeText(card.transform, "FREE PRIZE", 17f, new Vector2(0.5f, 1f), new Vector2(0f, -34f),
                 new Vector2(300f, 24f), Cyan, TextAlignmentOptions.Center);
        MakeText(card.transform, label, 24f, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f),
                 new Vector2(300f, 60f), Gold, TextAlignmentOptions.Center);

        long last = long.TryParse(PlayerPrefs.GetString(key, "0"), out long v) ? v : 0;
        double remaining = cooldownHours * 3600 -
                           (DateTime.UtcNow - new DateTime(last, DateTimeKind.Utc)).TotalSeconds;
        if (remaining > 0)
        {
            MakeText(card.transform, "NEXT IN " + PostMatchRewardManager.FormatRemaining(remaining), 17f,
                     new Vector2(0.5f, 0f), new Vector2(0f, 62f), new Vector2(300f, 26f), Grey,
                     TextAlignmentOptions.Center);
        }
        else
        {
            MakeButton(card.transform, "CLAIM", 20f, new Vector2(0.5f, 0f), new Vector2(0f, 48f),
                       new Vector2(220f, 56f), Green, () =>
            {
                PlayerPrefs.SetString(key, DateTime.UtcNow.Ticks.ToString());
                PlayerPrefs.Save();
                grant();
                RefreshCurrency();
                Toast("CLAIMED: " + label);
                SelectTab(3); // rebuild → shows the cooldown
            });
        }
    }

    // ------------------------------------------------------------------ tab: ADS PACK

    void BuildAdsTab()
    {
        // TODO(ads): integrate a real rewarded-ad SDK (AdMob or similar). For now WATCH fakes a
        // short loading pause and grants immediately so the whole flow is testable in-game.
        BuildAdCard("WATCH AD\n30 GEMS", new Vector2(-250f, 20f),
                    () => { RosterManager.Instance.AddDiamonds(30); RefreshCurrency(); Toast("+30 GEMS"); });
        BuildAdCard("WATCH AD\nBASIC PACK", new Vector2(250f, 20f),
                    () => OpenAndReveal(ShopPackType.Basic));
    }

    void BuildAdCard(string label, Vector2 pos, Action grant)
    {
        Image card = MakeCard(contentArea, pos, new Vector2(380f, 320f), new Color(0.18f, 0.5f, 1f, 1f));
        MakeText(card.transform, label, 24f, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f),
                 new Vector2(340f, 80f), Color.white, TextAlignmentOptions.Center);
        Button watch = MakeButton(card.transform, "WATCH", 20f, new Vector2(0.5f, 0f), new Vector2(0f, 52f),
                                  new Vector2(240f, 58f), Green, null);
        TextMeshProUGUI wl = watch.GetComponentInChildren<TextMeshProUGUI>();
        watch.onClick.AddListener(() =>
        {
            watch.interactable = false;
            if (wl != null) wl.text = "LOADING...";
            StartCoroutine(FakeAdThen(() =>
            {
                grant();
                if (watch != null) watch.interactable = true;
                if (wl != null) wl.text = "WATCH";
            }));
        });
    }

    // Fake ad: ~0.8s "loading" pause, then the reward. TODO(ads): replace with the real SDK call.
    IEnumerator FakeAdThen(Action grant)
    {
        yield return new WaitForSecondsRealtime(0.8f);
        grant?.Invoke();
    }

    // ------------------------------------------------------------------ tabs: COINS / GEMS

    void BuildCurrencyTab(bool coins)
    {
        (string price, int amount)[] offers = coins
            ? new[] { ("$0.99", 1000), ("$2.99", 3500), ("$4.99", 7000), ("$9.99", 16000) }
            : new[] { ("$0.99", 80), ("$2.99", 300), ("$4.99", 550), ("$9.99", 1200) };
        string icon = coins ? "Sprites/gold-coin" : "Sprites/diamond-coin";
        string unit = coins ? " COINS" : " GEMS";

        MakeText(contentArea, coins ? "BUY COINS" : "BUY GEMS", 26f, new Vector2(0.5f, 1f),
                 new Vector2(0f, -24f), new Vector2(500f, 34f), Gold, TextAlignmentOptions.Center);

        for (int i = 0; i < offers.Length; i++)
        {
            int amount = offers[i].amount;
            string price = offers[i].price;
            float cx = (i - (offers.Length - 1) * 0.5f) * 300f;
            Image card = MakeCard(contentArea, new Vector2(cx, -10f), new Vector2(270f, 330f),
                                  new Color(0.227f, 0.353f, 0.478f, 1f));
            Image ic = NewImage("Icon", card.transform);
            ic.sprite = LoadAnySprite(icon);
            ic.preserveAspect = true;
            ic.raycastTarget = false;
            if (ic.sprite == null) ic.color = Gold;
            SetRect(ic.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(90f, 90f));
            MakeText(card.transform, amount.ToString("N0") + unit, 21f, new Vector2(0.5f, 0.5f),
                     new Vector2(0f, -20f), new Vector2(240f, 30f), Color.white, TextAlignmentOptions.Center);
            MakeButton(card.transform, price, 20f, new Vector2(0.5f, 0f), new Vector2(0f, 46f),
                       new Vector2(190f, 56f), Gold, () =>
                IAPBridge.PurchaseProduct((coins ? "coins_" : "gems_") + amount, () =>
                {
                    if (coins) RosterManager.Instance.AddCoins(amount);
                    else RosterManager.Instance.AddDiamonds(amount);
                    RefreshCurrency();
                    Toast("+" + amount.ToString("N0") + unit);
                }));
        }
    }

    // ------------------------------------------------------------------ tab: DRAFT TICKETS

    void BuildDraftTab()
    {
        // Honest placeholder: no draft game mode exists yet, so there's nothing to buy or spend
        // tickets on. This tab just states that instead of faking a system with no destination.
        Image card = MakeCard(contentArea, new Vector2(0f, 20f), new Vector2(640f, 320f),
                              new Color(0.227f, 0.353f, 0.478f, 1f));
        MakeText(card.transform, "DRAFT TICKETS: 0", 28f, new Vector2(0.5f, 1f), new Vector2(0f, -50f),
                 new Vector2(560f, 36f), Cyan, TextAlignmentOptions.Center);
        MakeText(card.transform,
                 "The Draft game mode hasn't been built yet.\nTickets will be earnable from events and " +
                 "missions once it exists — nothing to spend here for now.",
                 18f, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(560f, 120f),
                 Color.white, TextAlignmentOptions.Center);
    }

    // ------------------------------------------------------------------ tab: EVENT

    void BuildEventTab()
    {
        // Honest stub: no live-event backend exists. Static banner + countdown to a placeholder date.
        DateTime target = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        TimeSpan left = target - DateTime.UtcNow;
        string cd = left.TotalSeconds > 0 ? (int)left.TotalDays + "D " + left.Hours + "H" : "ENDED";

        Image card = MakeCard(contentArea, new Vector2(0f, 20f), new Vector2(760f, 340f), Gold);
        MakeText(card.transform, "GLOBAL CUP", 40f, new Vector2(0.5f, 1f), new Vector2(0f, -70f),
                 new Vector2(700f, 50f), Gold, TextAlignmentOptions.Center);
        MakeText(card.transform, "STARTS IN " + cd, 22f, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f),
                 new Vector2(600f, 30f), Color.white, TextAlignmentOptions.Center);
        MakeText(card.transform, "Live events aren't built yet — this is a placeholder banner.",
                 16f, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(640f, 26f), Grey,
                 TextAlignmentOptions.Center);
    }

    // ------------------------------------------------------------------ toast

    void Toast(string message)
    {
        if (toastLabel == null)
        {
            toastLabel = MakeText(root, "", 22f, new Vector2(0.5f, 0f), new Vector2(0f, 110f),
                                  new Vector2(700f, 34f), Gold, TextAlignmentOptions.Center);
            toastLabel.transform.SetAsLastSibling();
        }
        toastLabel.transform.SetAsLastSibling();
        toastLabel.text = message;
        toastLabel.alpha = 1f;
        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(FadeToast());
    }

    IEnumerator FadeToast()
    {
        yield return new WaitForSecondsRealtime(1.1f);
        float t = 0f;
        while (t < 0.4f)
        {
            t += Time.unscaledDeltaTime;
            if (toastLabel != null) toastLabel.alpha = 1f - t / 0.4f;
            yield return null;
        }
    }

    // ------------------------------------------------------------------ helpers

    Image MakeCard(Transform parent, Vector2 pos, Vector2 size, Color border)
    {
        Image frame = NewImage("Card", parent);
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced;
        frame.color = border;
        SetRect(frame.rectTransform, new Vector2(0.5f, 0.5f), pos, size);
        Image fill = NewImage("Fill", frame.transform);
        fill.sprite = Rounded(); fill.type = Image.Type.Sliced;
        fill.color = CardFill;
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);
        return frame;
    }

    Button MakeButton(Transform parent, string label, float fontSize, Vector2 anchor, Vector2 pos,
                      Vector2 size, Color color, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        SetRect(go.AddComponent<RectTransform>(), anchor, pos, size);
        Image img = go.AddComponent<Image>();
        img.sprite = Rounded(); img.type = Image.Type.Sliced;
        img.color = color;
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);
        TextMeshProUGUI t = MakeText(go.transform, label, fontSize, new Vector2(0.5f, 0.5f), Vector2.zero,
                                     size, Color.white, TextAlignmentOptions.Center);
        Stretch(t.rectTransform);
        return btn;
    }

    Image NewImage(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<Image>();
    }

    TextMeshProUGUI MakeText(Transform parent, string content, float size, Vector2 anchor,
                             Vector2 pos, Vector2 box, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = content;
        txt.fontSize = size;
        txt.fontStyle = FontStyles.Bold;
        txt.color = color;
        txt.alignment = align;
        txt.raycastTarget = false;
        SetRect(txt.rectTransform, anchor, pos, box);
        return txt;
    }

    static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // Sprite loader that works whether or not the PNG is imported in sprite mode (same trick as
    // NavigationManager.TextureSprite).
    static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    static Sprite LoadAnySprite(string path)
    {
        if (spriteCache.TryGetValue(path, out Sprite cached) && cached != null) return cached;
        Sprite s = Resources.Load<Sprite>(path);
        if (s == null)
        {
            Texture2D tex = Resources.Load<Texture2D>(path);
            if (tex != null)
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
        if (s != null) spriteCache[path] = s;
        else Debug.LogWarning("ShopUI: sprite not found at Resources/" + path);
        return s;
    }

    static Sprite Rounded()
    {
        if (rounded != null) return rounded;
        const int size = 128, corner = 20;
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
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                SpriteMeshType.FullRect, new Vector4(corner + 2, corner + 2, corner + 2, corner + 2));
        return rounded;
    }

    static Sprite Circle()
    {
        if (circle != null) return circle;
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        float r = size * 0.5f - 1f;
        Vector2 c = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return circle;
    }
}
