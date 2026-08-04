using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// The SHOP screen — built entirely in code (no prefabs), same runtime-build style as
// NavigationManager / TeamScreenUI. Hosted by NavigationManager's shop overlay; the close control
// calls nav.CloseShopScreen().
//
// Layout: 80px top bar (close | SHOP + gear | event badge | gold/gems with [+]) — ONE continuous
// horizontal ScrollRect ("shelf") holding all 9 sections side by side — bottom row of 9 text
// tabs that jump-scroll to their section (highlight follows free drags in real time).
//
// Pack identity: the 4 CardTier packs (Common/Rare/Epic/Legendary Card) are the ONLY packs —
// same defs, sprites and odds as the post-match reward slots (see CardPack.TierPackDef).
//
// Honesty notes: DAILY DEALS / ad-watch caps are local PlayerPrefs (no server), every WATCH
// fakes the ad with a short spinner (TODO: real ad SDK), COINS/GEMS/Legendary-$2.99 route
// through IAPBridge (stub — grants immediately), DRAFT TICKETS and EVENT are declared
// placeholders for systems that don't exist yet.
public class ShopUI : MonoBehaviour
{
    static readonly Color DarkBar = new Color(0.04f, 0.06f, 0.13f, 0.86f);
    static readonly Color Panel = new Color(0.03f, 0.05f, 0.11f, 0.92f);
    static readonly Color CardFill = new Color(0.07f, 0.12f, 0.19f, 0.97f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    static readonly Color Cyan = new Color(0f, 0.85f, 1f);
    static readonly Color Green = new Color(0.2f, 0.72f, 0.32f);
    static readonly Color BrightGreen = new Color(0.35f, 0.95f, 0.45f);
    static readonly Color Grey = new Color(0.55f, 0.6f, 0.68f);
    static readonly Color Blue = new Color(0.18f, 0.5f, 1f);
    static readonly Color GreyBtn = new Color(0.3f, 0.34f, 0.4f, 1f);

    static Sprite rounded, circle, playTriangle;

    static readonly string[] TabNames =
        { "OFFERS", "PACKS", "DAILY DEALS", "FREE PRIZES", "ADS PACK", "COINS", "GEMS", "DRAFT TICKETS", "EVENT" };
    static readonly string[] SectionTitles =
        { "OFFERS", "PACKS", "DAILY DEALS", "FREE PRIZES", "ADS PACK", "COIN PACKS", "GEM PACKS", "DRAFT TICKETS", "EVENT" };
    // Tabs that carry a small "FREE" pill badge (reference-image style).
    static readonly bool[] TabFreeBadge = { false, false, true, true, true, false, false, true, true };
    // Fixed section widths — the shelf's total width is their sum + gaps.
    static readonly float[] SectionWidths = { 460f, 1132f, 858f, 1068f, 844f, 1212f, 1212f, 700f, 820f };
    const float SectionGap = 24f;
    const float PackCardW = 250f, PackCardH = 400f; // ALL 4 pack cards share this exact size

    Transform root;
    NavigationManager nav;
    ScrollRect shelf;
    RectTransform shelfViewport, shelfContent;
    readonly List<RectTransform> sectionRects = new List<RectTransform>();
    readonly List<float> sectionCenters = new List<float>();
    readonly List<TextMeshProUGUI> tabLabels = new List<TextMeshProUGUI>();
    readonly List<Image> tabUnderlines = new List<Image>();
    int activeTab;
    Coroutine scrollRoutine;
    TextMeshProUGUI toastLabel;
    Coroutine toastRoutine;

    public void Build(Transform parent, NavigationManager navigation)
    {
        root = parent;
        nav = navigation;

        Image bg = NewImage("Background", root);
        bg.sprite = UniversalUIStyle.LoadBackground("Regular-Background");
        bg.preserveAspect = false;
        bg.color = bg.sprite != null ? Color.white : new Color(0.03f, 0.07f, 0.13f, 1f);
        bg.raycastTarget = true; // swallow clicks
        Stretch(bg.rectTransform);

        BuildTopBar();
        BuildShelf();
        BuildTabRow();
        HighlightTab(0);
    }

    void OnEnable() { RefreshCurrency(); } // overlay re-opened → fresh balances

    public void RefreshCurrency()
    {
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

        // Universal close control → hub.
        UniversalUIStyle.MakeCloseButton(bar.transform, new Vector2(0f, 0.5f),
            new Vector2(52f, 0f), new Vector2(60f, 60f),
            () => { if (nav != null) nav.CloseShopScreen(); });

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
        gbtn.onClick.AddListener(() => { if (nav != null) nav.OpenSettingsScreen(); }); // same settings overlay as the hub gear

        // Event badge → jumps to the EVENT section; countdown = the real season timer.
        MakeButton(bar.transform, "EVENT  " + SeasonPassManager.Instance.CountdownLabel(), 15f,
                   new Vector2(0.5f, 0.5f), new Vector2(-40f, 0f), new Vector2(190f, 46f),
                   new Color(0.45f, 0.2f, 0.55f, 1f), () => SelectTab(8));

        // Currencies (right→left): gold [+], gold, gem [+], gems. [+] jumps to the buy sections.
        if (nav != null) nav.AddCurrencyDisplay(bar.transform);
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

    // ------------------------------------------------------------------ shelf (one horizontal scroll)

    void BuildShelf()
    {
        GameObject vp = new GameObject("Shelf");
        vp.transform.SetParent(root, false);
        shelfViewport = vp.AddComponent<RectTransform>();
        shelfViewport.anchorMin = Vector2.zero;
        shelfViewport.anchorMax = Vector2.one;
        shelfViewport.offsetMin = new Vector2(0f, 70f);   // above the tab row
        shelfViewport.offsetMax = new Vector2(0f, -84f);  // below the top bar
        Image vbg = vp.AddComponent<Image>();
        vbg.color = new Color(0f, 0f, 0f, 0f); // invisible but raycastable → drags work anywhere
        vp.AddComponent<RectMask2D>();

        GameObject ct = new GameObject("ShelfContent");
        ct.transform.SetParent(vp.transform, false);
        shelfContent = ct.AddComponent<RectTransform>();
        shelfContent.anchorMin = new Vector2(0f, 0f);
        shelfContent.anchorMax = new Vector2(0f, 1f);
        shelfContent.pivot = new Vector2(0f, 0.5f);
        shelfContent.anchoredPosition = Vector2.zero;

        sectionRects.Clear();
        sectionCenters.Clear();
        float x = SectionGap;
        for (int i = 0; i < TabNames.Length; i++)
        {
            GameObject sec = new GameObject("Section_" + TabNames[i]);
            sec.transform.SetParent(ct.transform, false);
            RectTransform srt = sec.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.anchoredPosition = new Vector2(x, 0f);
            srt.sizeDelta = new Vector2(SectionWidths[i], 0f);
            sectionRects.Add(srt);
            sectionCenters.Add(x + SectionWidths[i] * 0.5f);
            BuildSectionContent(i);
            x += SectionWidths[i] + SectionGap;
        }
        shelfContent.sizeDelta = new Vector2(x, 0f);

        shelf = vp.AddComponent<ScrollRect>();
        shelf.viewport = shelfViewport;
        shelf.content = shelfContent;
        shelf.horizontal = true;
        shelf.vertical = false;
        shelf.movementType = ScrollRect.MovementType.Clamped;
        shelf.scrollSensitivity = 25f;
        shelf.onValueChanged.AddListener(OnShelfScrolled);

        // A manual drag cancels a running tab-jump animation immediately.
        EventTrigger trig = vp.AddComponent<EventTrigger>();
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = EventTriggerType.BeginDrag };
        entry.callback.AddListener(_ => CancelAutoScroll());
        trig.triggers.Add(entry);
    }

    // Section shell (panel + title) then the per-section content. Called again by
    // RebuildSection after state changes (e.g. a deals refresh).
    void BuildSectionContent(int i)
    {
        RectTransform sec = sectionRects[i];
        Image panel = NewImage("Panel", sec);
        CrestUITheme.ApplyFrame(panel, CrestUITheme.Frame, Panel, 2f);
        panel.raycastTarget = false;
        RectTransform prt = panel.rectTransform;
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(0f, 6f); prt.offsetMax = new Vector2(0f, -6f);

        MakeText(sec, SectionTitles[i], 24f, new Vector2(0.5f, 1f), new Vector2(0f, -32f),
                 new Vector2(SectionWidths[i] - 40f, 32f), Color.white, TextAlignmentOptions.Center);

        switch (i)
        {
            case 0: BuildOffersSection(sec); break;
            case 1: BuildPacksSection(sec); break;
            case 2: BuildDailyDealsSection(sec); break;
            case 3: BuildFreePrizesSection(sec); break;
            case 4: BuildAdsSection(sec); break;
            case 5: BuildCurrencySection(sec, true); break;
            case 6: BuildCurrencySection(sec, false); break;
            case 7: BuildDraftSection(sec); break;
            case 8: BuildEventSection(sec); break;
        }
    }

    void RebuildSection(int i)
    {
        RectTransform sec = sectionRects[i];
        for (int c = sec.childCount - 1; c >= 0; c--) Destroy(sec.GetChild(c).gameObject);
        BuildSectionContent(i);
    }

    // ------------------------------------------------------------------ tab row

    void BuildTabRow()
    {
        Image barBg = NewImage("TabBar", root);
        barBg.color = new Color(0.02f, 0.03f, 0.08f, 0.9f);
        RectTransform brt = barBg.rectTransform;
        brt.anchorMin = new Vector2(0f, 0f);
        brt.anchorMax = new Vector2(1f, 0f);
        brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(0f, 66f);

        tabLabels.Clear();
        tabUnderlines.Clear();
        int n = TabNames.Length;
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            GameObject go = new GameObject("Tab_" + TabNames[i]);
            go.transform.SetParent(barBg.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2((float)i / n, 0f);
            rt.anchorMax = new Vector2((float)(i + 1) / n, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            Image hit = go.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f); // invisible click area — no button box (reference style)
            Button b = go.AddComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => SelectTab(idx));

            // Plain text label; wraps to two lines for long names ("DAILY DEALS" etc.).
            TextMeshProUGUI t = MakeText(go.transform, TabNames[i], 15f, new Vector2(0.5f, 0.5f),
                                         new Vector2(0f, -3f), new Vector2(120f, 50f), Grey,
                                         TextAlignmentOptions.Center);
            tabLabels.Add(t);

            // Active underline (subtle, only visible on the selected tab).
            Image ul = NewImage("Underline", go.transform);
            ul.sprite = Rounded(); ul.type = Image.Type.Sliced;
            ul.color = BrightGreen;
            ul.raycastTarget = false;
            SetRect(ul.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(52f, 4f));
            ul.enabled = false;
            tabUnderlines.Add(ul);

            // "FREE" pill floating above-right of the label.
            if (TabFreeBadge[i])
            {
                Image pill = NewImage("Free", go.transform);
                pill.sprite = Rounded(); pill.type = Image.Type.Sliced;
                pill.color = Green;
                pill.raycastTarget = false;
                SetRect(pill.rectTransform, new Vector2(0.5f, 1f), new Vector2(36f, -2f), new Vector2(46f, 19f));
                TextMeshProUGUI pt = MakeText(pill.transform, "FREE", 11f, new Vector2(0.5f, 0.5f),
                                              Vector2.zero, new Vector2(46f, 19f), Color.white,
                                              TextAlignmentOptions.Center);
                Stretch(pt.rectTransform);
            }
        }
    }

    void HighlightTab(int index)
    {
        activeTab = index;
        for (int i = 0; i < tabLabels.Count; i++)
        {
            tabLabels[i].color = i == index ? BrightGreen : Grey;
            tabUnderlines[i].enabled = i == index;
        }
    }

    // Tab tapped (or a top-bar shortcut): highlight it and glide the shelf to its section.
    public void SelectTab(int index)
    {
        HighlightTab(index);
        CancelAutoScroll();
        shelf.velocity = Vector2.zero;
        scrollRoutine = StartCoroutine(ScrollShelfTo(SectionTargetNorm(index)));
    }

    float SectionTargetNorm(int index)
    {
        float vw = shelfViewport.rect.width;
        float scrollable = shelfContent.rect.width - vw;
        if (scrollable <= 0f) return 0f;
        return Mathf.Clamp01((sectionCenters[index] - vw * 0.5f) / scrollable);
    }

    IEnumerator ScrollShelfTo(float target)
    {
        float start = shelf.horizontalNormalizedPosition;
        const float dur = 0.4f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            shelf.horizontalNormalizedPosition = Mathf.Lerp(start, target, k);
            yield return null;
        }
        shelf.horizontalNormalizedPosition = target;
        scrollRoutine = null;
    }

    void CancelAutoScroll()
    {
        if (scrollRoutine != null) { StopCoroutine(scrollRoutine); scrollRoutine = null; }
    }

    // Free drag → the tab highlight follows whichever section's centre is nearest the
    // viewport centre (no snapping). Suppressed while a tab-jump animation drives the scroll.
    void OnShelfScrolled(Vector2 _)
    {
        if (scrollRoutine != null) return;
        float vw = shelfViewport.rect.width;
        float scrollable = Mathf.Max(1f, shelfContent.rect.width - vw);
        float viewCenter = Mathf.Clamp01(shelf.horizontalNormalizedPosition) * scrollable + vw * 0.5f;
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < sectionCenters.Count; i++)
        {
            float d = Mathf.Abs(sectionCenters[i] - viewCenter);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        if (best != activeTab) HighlightTab(best);
    }

    // ------------------------------------------------------------------ section: OFFERS

    void BuildOffersSection(RectTransform sec)
    {
        // "Coach's Choice" featured offer: Epic Card contents + 500 coins at a deal price.
        Image feat = MakeCard(sec, new Vector2(0.5f, 0.5f), new Vector2(0f, -14f), new Vector2(370f, 440f), Gold);
        MakeText(feat.transform, "COACH'S CHOICE", 24f, new Vector2(0.5f, 1f), new Vector2(0f, -32f),
                 new Vector2(340f, 32f), Gold, TextAlignmentOptions.Center);
        Image fart = NewImage("Art", feat.transform);
        fart.sprite = CardPack.TierArtSprite(CardTier.Epic);
        fart.preserveAspect = true;
        fart.raycastTarget = false;
        if (fart.sprite == null) fart.color = CardPack.TierColor(CardTier.Epic);
        SetRect(fart.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 50f), new Vector2(180f, 180f));
        MakeText(feat.transform, "EPIC CARD\n+ 500 COINS", 20f, new Vector2(0.5f, 0.5f),
                 new Vector2(0f, -85f), new Vector2(320f, 60f), Color.white, TextAlignmentOptions.Center);
        MakeText(feat.transform, "250  (was 400)", 16f, new Vector2(0.5f, 0f), new Vector2(0f, 102f),
                 new Vector2(300f, 24f), Grey, TextAlignmentOptions.Center);
        MakeButton(feat.transform, "BUY  250 GEMS", 20f, new Vector2(0.5f, 0f), new Vector2(0f, 52f),
                   new Vector2(280f, 58f), Green, () =>
        {
            if (!RosterManager.Instance.SpendDiamonds(250)) { Toast("NOT ENOUGH GEMS"); return; }
            RosterManager.Instance.AddCoins(500);
            OpenAndReveal(CardTier.Epic);
        });

        PackCardFX.Attach(fart.rectTransform); // art-only float/shine (text + button stay still)
    }

    // ------------------------------------------------------------------ section: PACKS

    void BuildPacksSection(RectTransform sec)
    {
        for (int t = 0; t < 4; t++)
            BuildPackCard(sec, (CardTier)t, 30f + PackCardW * 0.5f + t * (PackCardW + 24f), -14f, 0, "packs");
    }

    // One uniform pack card — the SAME size/layout for all 4 tiers (and in DAILY DEALS).
    // `capIdPrefix` keeps the Common card's WATCH counter independent per section.
    void BuildPackCard(Transform parent, CardTier tier, float xCenter, float yCenter,
                       int discountPct, string capIdPrefix)
    {
        CardPack.TierPackDef def = CardPack.GetTierPack(tier);
        Color tint = CardPack.TierColor(tier);

        GameObject go = new GameObject("Pack_" + def.name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(xCenter, yCenter);
        rt.sizeDelta = new Vector2(PackCardW, PackCardH);
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

        // Fixed 145x145 content box + preserveAspect + centred anchor on EVERY tier — art is
        // alpha-trimmed by TierArtSprite, so mismatched source padding still renders uniform.
        Image art = NewImage("Art", go.transform);
        art.sprite = CardPack.TierArtSprite(tier);
        art.preserveAspect = true;
        art.raycastTarget = false;
        if (art.sprite == null) art.color = tint;
        SetRect(art.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(145f, 145f));

        MakeText(go.transform, def.name, 19f, new Vector2(0.5f, 1f), new Vector2(0f, -192f),
                 new Vector2(PackCardW - 16f, 26f), tint, TextAlignmentOptions.Center);
        MakeText(go.transform, "UP TO " + def.maxCards + " PLAYERS", 14f, new Vector2(0.5f, 1f),
                 new Vector2(0f, -216f), new Vector2(PackCardW - 16f, 20f), Color.white,
                 TextAlignmentOptions.Center);

        // "i" info → the shared drop-rate popup (same table as the reward-slot popup).
        GameObject info = new GameObject("BtnInfo");
        info.transform.SetParent(go.transform, false);
        SetRect(info.AddComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-25f, -25f), new Vector2(40f, 40f));
        Image iimg = info.AddComponent<Image>();
        Sprite isp = LoadAnySprite("Sprites/i-button");
        if (isp != null) { iimg.sprite = isp; iimg.preserveAspect = true; }
        else { iimg.sprite = Circle(); iimg.color = Cyan; }
        Button ibtn = info.AddComponent<Button>();
        ibtn.targetGraphic = iimg;
        ibtn.onClick.AddListener(() => PackInfoPopup.Show(root, tier));

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

        // Buy buttons: gems always; plus WATCH ▶ (Common, ad-capped) or $ (Legendary).
        int price = gemPrice;
        bool twoButtons = def.watchAdOption || def.realMoney != null;
        float mainY = twoButtons ? 84f : 56f;
        MakeButton(go.transform, price + " GEMS", 17f, new Vector2(0.5f, 0f), new Vector2(0f, mainY),
                   new Vector2(PackCardW - 40f, 46f), Green, () =>
        {
            if (!RosterManager.Instance.SpendDiamonds(price)) { Toast("NOT ENOUGH GEMS"); return; }
            OpenAndReveal(tier);
        });
        if (def.watchAdOption)
            MakeWatchButton(go.transform, capIdPrefix + "_" + tier.ToString().ToLower(), "WATCH",
                            new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(PackCardW - 40f, 42f),
                            () => OpenAndReveal(tier));
        if (def.realMoney != null)
            MakeButton(go.transform, def.realMoney, 16f, new Vector2(0.5f, 0f), new Vector2(0f, 34f),
                       new Vector2(PackCardW - 40f, 42f), Gold,
                       () => IAPBridge.PurchaseProduct("pack_" + tier.ToString().ToLower(),
                                                       () => OpenAndReveal(tier)));

        // Float + shine on the pack ART only — title, player-count and buy buttons stay still.
        PackCardFX.Attach(art.rectTransform);
    }

    // Buy succeeded → open, grant (dupes → coins), reveal.
    void OpenAndReveal(CardTier tier)
    {
        List<CardPack.GrantResult> results = CardPack.GrantAll(CardPack.OpenTierPack(tier));
        RefreshCurrency();
        PackRevealUI.Show(root, results, RefreshCurrency);
    }

    // ------------------------------------------------------------------ section: DAILY DEALS

    void BuildDailyDealsSection(RectTransform sec)
    {
        // Local rotation (no server): the deal set derives from the UTC day number plus the
        // ad-watched refresh count, and the countdown targets the next UTC midnight.
        int seed = Mathf.Abs((int)(AdWatchCap.UtcDay() % 100000)) + AdWatchCap.Used("deals_refresh") * 31;
        int skip = seed % 4;
        int[] pcts = { 30, 40, 50 };

        TimeSpan left = DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;
        MakeText(sec, "REFRESH IN " + (int)left.TotalHours + "H " + left.Minutes + "M", 15f,
                 new Vector2(0f, 1f), new Vector2(130f, -32f), new Vector2(240f, 24f), Cyan,
                 TextAlignmentOptions.Left);
        // Ad-watched reroll, capped 3/day like every WATCH button.
        MakeWatchButton(sec, "deals_refresh", "REFRESH", new Vector2(1f, 1f), new Vector2(-110f, -34f),
                        new Vector2(180f, 42f), () => RebuildSection(2));

        int col = 0;
        for (int t = 0; t < 4; t++)
        {
            if (t == skip) continue;
            int pct = pcts[(col + seed) % pcts.Length];
            BuildPackCard(sec, (CardTier)t, 30f + PackCardW * 0.5f + col * (PackCardW + 24f), -24f, pct, "deals");
            col++;
        }
    }

    // ------------------------------------------------------------------ section: FREE PRIZES

    void BuildFreePrizesSection(RectTransform sec)
    {
        BuildFreePrizeCard(sec, 0, "100 COINS", 30f + 160f,
            () => { RosterManager.Instance.AddCoins(100); RefreshCurrency(); Toast("+100 COINS"); });
        BuildFreePrizeCard(sec, 1, "10 GEMS", 30f + 160f + 344f,
            () => { RosterManager.Instance.AddDiamonds(10); RefreshCurrency(); Toast("+10 GEMS"); });
        BuildFreePrizeCard(sec, 2, "COMMON CARD", 30f + 160f + 688f,
            () => OpenAndReveal(CardTier.Common));
    }

    // Watch-to-claim prize card; each claim button has its own 3/day ad cap.
    void BuildFreePrizeCard(RectTransform sec, int index, string label, float xCenter, Action grant)
    {
        Image card = MakeCard(sec, new Vector2(0f, 0.5f), new Vector2(xCenter, -14f),
                              new Vector2(320f, 300f), new Color(0.227f, 0.353f, 0.478f, 1f));
        MakeText(card.transform, "FREE PRIZE", 16f, new Vector2(0.5f, 1f), new Vector2(0f, -32f),
                 new Vector2(280f, 24f), Cyan, TextAlignmentOptions.Center);
        MakeText(card.transform, label, 24f, new Vector2(0.5f, 0.5f), new Vector2(0f, 16f),
                 new Vector2(280f, 60f), Gold, TextAlignmentOptions.Center);
        MakeWatchButton(card.transform, "free_" + index, "FREE", new Vector2(0.5f, 0f),
                        new Vector2(0f, 46f), new Vector2(210f, 50f), grant);
    }

    // ------------------------------------------------------------------ section: ADS PACK

    void BuildAdsSection(RectTransform sec)
    {
        // TODO(ads): integrate a real rewarded-ad SDK (AdMob or similar). For now WATCH fakes a
        // short loading pause and grants immediately so the whole flow is testable in-game.
        BuildAdCard(sec, "WATCH AD\n30 GEMS", 30f + 190f, "ads_gems",
                    () => { RosterManager.Instance.AddDiamonds(30); RefreshCurrency(); Toast("+30 GEMS"); });
        BuildAdCard(sec, "WATCH AD\nCOMMON CARD", 30f + 190f + 404f, "ads_common",
                    () => OpenAndReveal(CardTier.Common));
    }

    void BuildAdCard(RectTransform sec, string label, float xCenter, string capId, Action grant)
    {
        Image card = MakeCard(sec, new Vector2(0f, 0.5f), new Vector2(xCenter, -14f),
                              new Vector2(380f, 310f), Blue);
        MakeText(card.transform, label, 24f, new Vector2(0.5f, 0.5f), new Vector2(0f, 34f),
                 new Vector2(340f, 80f), Color.white, TextAlignmentOptions.Center);
        MakeWatchButton(card.transform, capId, "WATCH", new Vector2(0.5f, 0f), new Vector2(0f, 46f),
                        new Vector2(220f, 52f), grant);
    }

    // ------------------------------------------------------------------ sections: COINS / GEMS

    void BuildCurrencySection(RectTransform sec, bool coins)
    {
        (string price, int amount)[] offers = coins
            ? new[] { ("$0.99", 1000), ("$2.99", 3500), ("$4.99", 7000), ("$9.99", 16000) }
            : new[] { ("$0.99", 80), ("$2.99", 300), ("$4.99", 550), ("$9.99", 1200) };
        string icon = coins ? "Sprites/gold-coin" : "Sprites/diamond-coin";
        string unit = coins ? " COINS" : " GEMS";

        for (int i = 0; i < offers.Length; i++)
        {
            int amount = offers[i].amount;
            string price = offers[i].price;
            float cx = 30f + 135f + i * 294f;
            Image card = MakeCard(sec, new Vector2(0f, 0.5f), new Vector2(cx, -14f), new Vector2(270f, 330f),
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

    // ------------------------------------------------------------------ section: DRAFT TICKETS

    void BuildDraftSection(RectTransform sec)
    {
        // Honest placeholder: no draft game mode exists yet, so there's nothing to buy or spend
        // tickets on. This section just states that instead of faking a system with no destination.
        Image card = MakeCard(sec, new Vector2(0.5f, 0.5f), new Vector2(0f, -14f), new Vector2(620f, 320f),
                              new Color(0.227f, 0.353f, 0.478f, 1f));
        MakeText(card.transform, "DRAFT TICKETS: 0", 28f, new Vector2(0.5f, 1f), new Vector2(0f, -50f),
                 new Vector2(560f, 36f), Cyan, TextAlignmentOptions.Center);
        MakeText(card.transform,
                 "The Draft game mode hasn't been built yet.\nTickets will be earnable from events and " +
                 "missions once it exists — nothing to spend here for now.",
                 18f, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(560f, 120f),
                 Color.white, TextAlignmentOptions.Center);
    }

    // ------------------------------------------------------------------ section: EVENT

    void BuildEventSection(RectTransform sec)
    {
        // Honest stub: no live-event backend exists. The countdown is the REAL season timer
        // (SeasonPassManager) so it converges with the hub/missions/league season.
        Image card = MakeCard(sec, new Vector2(0.5f, 0.5f), new Vector2(0f, -14f), new Vector2(740f, 340f), Gold);
        MakeText(card.transform, "GLOBAL CUP", 40f, new Vector2(0.5f, 1f), new Vector2(0f, -70f),
                 new Vector2(700f, 50f), Gold, TextAlignmentOptions.Center);
        MakeText(card.transform, "SEASON ENDS IN " + SeasonPassManager.Instance.CountdownLabel(), 22f,
                 new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(600f, 30f), Color.white,
                 TextAlignmentOptions.Center);
        MakeText(card.transform, "Event rewards aren't built yet — this is a placeholder banner.",
                 16f, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(640f, 26f), Grey,
                 TextAlignmentOptions.Center);
    }

    // ------------------------------------------------------------------ watch-ad buttons

    // The dedicated green ad art is kept at its native aspect ratio. It contains its own video
    // icon, so it must not be nine-sliced or decorated with a second play triangle.
    // Tracks its own AdWatchCap counter under `id`; at 3 uses it greys out until UTC midnight.
    Button MakeWatchButton(Transform parent, string id, string label, Vector2 anchor, Vector2 pos,
                           Vector2 size, Action onReward)
    {
        bool capped = AdWatchCap.Used(id) >= AdWatchCap.DailyCap;

        GameObject go = new GameObject("BtnWatch_" + id);
        go.transform.SetParent(parent, false);
        SetRect(go.AddComponent<RectTransform>(), anchor, pos, size);
        Image img = go.AddComponent<Image>();
        Sprite adSprite = ButtonSpriteCatalog.SpriteFor("Ad-Button");
        bool usesAdArtwork = adSprite != null;
        img.sprite = usesAdArtwork ? adSprite : Rounded();
        img.type = usesAdArtwork ? Image.Type.Simple : Image.Type.Sliced;
        img.preserveAspect = usesAdArtwork;
        img.color = usesAdArtwork ? Color.white : (capped ? GreyBtn : Blue);
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock buttonColors = btn.colors;
        buttonColors.disabledColor = Color.white;
        btn.colors = buttonColors;
        btn.interactable = !capped;

        float labelWidth = usesAdArtwork ? Mathf.Min(size.x - 18f, size.y * 2.55f) : size.x - 30f;
        TextMeshProUGUI txt = MakeText(go.transform, capped ? AdWatchCap.ResetLabel() : label, 15f,
                                       new Vector2(0.5f, 0.5f),
                                       usesAdArtwork ? new Vector2(0f, -size.y * 0.08f) : new Vector2(-8f, 0f),
                                       new Vector2(labelWidth, size.y * 0.72f), Color.white,
                                       TextAlignmentOptions.Center);
        txt.enableAutoSizing = true;
        txt.fontSizeMin = 9f;
        txt.fontSizeMax = 15f;
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        Image tri = NewImage("Play", go.transform);
        tri.sprite = PlayTriangle();
        tri.color = Color.white;
        tri.raycastTarget = false;
        SetRect(tri.rectTransform, new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(13f, 15f));
        tri.gameObject.SetActive(!usesAdArtwork && !capped);

        if (!capped) btn.onClick.AddListener(() =>
        {
            btn.interactable = false;
            txt.text = "LOADING...";
            tri.gameObject.SetActive(false);
            StartCoroutine(FakeAdThen(() =>
            {
                AdWatchCap.Record(id);
                if (btn != null) // section may get rebuilt by onReward — update in place first
                {
                    if (AdWatchCap.Used(id) >= AdWatchCap.DailyCap)
                    {
                        if (!usesAdArtwork) img.color = GreyBtn;
                        txt.text = AdWatchCap.ResetLabel();
                    }
                    else
                    {
                        btn.interactable = true;
                        txt.text = label;
                        tri.gameObject.SetActive(!usesAdArtwork);
                    }
                }
                onReward?.Invoke();
            }));
        });
        return btn;
    }

    // Fake ad: ~0.8s "loading" pause, then the reward. TODO(ads): replace with the real SDK call.
    IEnumerator FakeAdThen(Action grant)
    {
        LoadingOverlayUI.ShowSpinner("LOADING VIDEO...");
        yield return new WaitForSecondsRealtime(0.8f);
        LoadingOverlayUI.HideSpinner();
        grant?.Invoke();
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

    Image MakeCard(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color border)
    {
        Image frame = NewImage("Card", parent);
        frame.sprite = Rounded(); frame.type = Image.Type.Sliced;
        frame.color = border;
        SetRect(frame.rectTransform, anchor, pos, size);
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
        CrestUITheme.ApplyButton(img, color);
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);
        LocalizedButtonStyler.AddLabel(go.transform, label, fontSize, size,
            LocalizedButtonStyler.TextZone.NativeCenter, 1.3f);
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
        string buttonKey = ButtonSpriteCatalog.KeyForLegacyPath(path);
        Sprite s = !string.IsNullOrEmpty(buttonKey)
            ? ButtonSpriteCatalog.SpriteFor(buttonKey)
            : Resources.Load<Sprite>(path);
        if (s == null && string.IsNullOrEmpty(buttonKey))
        {
            Texture2D tex = Resources.Load<Texture2D>(path);
            if (tex != null)
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
        if (s != null) spriteCache[path] = s;
        else Debug.LogWarning(!string.IsNullOrEmpty(buttonKey)
            ? "ShopUI: button '" + buttonKey + "' is missing from ButtonSpriteCatalog; using fallback art."
            : "ShopUI: sprite not found at Resources/" + path);
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

    // Small right-pointing play triangle (text-height ▶ icon for watch buttons).
    static Sprite PlayTriangle()
    {
        if (playTriangle != null) return playTriangle;
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // inside when x within [0, size) and |y - centre| <= half-height at that x
                float progress = x / (float)(size - 1);            // 0 at left edge → 1 at tip
                float halfH = (1f - progress) * (size * 0.5f - 1f);
                float dy = Mathf.Abs(y - (size * 0.5f - 0.5f));
                float a = Mathf.Clamp01(halfH - dy + 1f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        playTriangle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return playTriangle;
    }
}

// The ONE watch-ad daily cap: every WATCH button in the game (shop packs, deals refresh, free
// prizes, ads pack, hub FREE +100) tracks its own PlayerPrefs counter under a unique id, capped
// at 3 uses per UTC day. Same UTC-day-number pattern as the daily-deals rotation.
public static class AdWatchCap
{
    public const int DailyCap = 3;

    public static long UtcDay() => (long)(DateTime.UtcNow - new DateTime(2026, 1, 1)).TotalDays;

    public static int Used(string id)
    {
        if (PlayerPrefs.GetInt("adwatch_day_" + id, -1) != (int)UtcDay()) return 0; // stale day → fresh cap
        return PlayerPrefs.GetInt("adwatch_n_" + id, 0);
    }

    public static void Record(string id)
    {
        int used = Used(id); // read BEFORE stamping today's day
        PlayerPrefs.SetInt("adwatch_day_" + id, (int)UtcDay());
        PlayerPrefs.SetInt("adwatch_n_" + id, used + 1);
        PlayerPrefs.Save();
    }

    public static string ResetLabel()
    {
        TimeSpan left = DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;
        if (left.TotalHours >= 1) return "RESETS IN " + (int)Math.Ceiling(left.TotalHours) + "H";
        return "RESETS IN " + Math.Max(1, left.Minutes) + "M";
    }
}
