using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Shared "pack opened" reveal overlay — used by the hub reward slots and by the shop. Built in
// code onto whatever canvas transform the caller passes. Each card scales+fades in with a small
// stagger (simple by design); tapping anywhere dismisses and fires onClose.
public class PackRevealUI : MonoBehaviour
{
    static Sprite rounded; // local cache; regenerated after a domain reload

    public static void Show(Transform canvasParent, List<CardPack.GrantResult> results, Action onClose)
    {
        GameObject ov = new GameObject("PackReveal");
        ov.transform.SetParent(canvasParent, false);
        ov.transform.SetAsLastSibling();
        RectTransform ort = ov.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        Image dark = ov.AddComponent<Image>();
        dark.color = new Color(0.01f, 0.02f, 0.06f, 0.94f);
        dark.raycastTarget = true;

        PackRevealUI ui = ov.AddComponent<PackRevealUI>();

        Text(ov.transform, "PACK OPENED!", 40f, new Vector2(0f, 250f), new Vector2(700f, 56f),
             new Color(1f, 0.82f, 0.2f));

        // Cards centred in a row.
        int n = Mathf.Max(1, results.Count);
        const float w = 200f, h = 280f, gap = 26f;
        List<RectTransform> cards = new List<RectTransform>();
        for (int i = 0; i < results.Count; i++)
        {
            float cx = (i - (n - 1) * 0.5f) * (w + gap);
            cards.Add(ui.BuildCard(ov.transform, results[i], new Vector2(cx, 10f), new Vector2(w, h)));
        }

        Text(ov.transform, "TAP TO CONTINUE", 20f, new Vector2(0f, -260f), new Vector2(500f, 30f),
             new Color(1f, 1f, 1f, 0.75f));

        Button btn = ov.AddComponent<Button>();
        btn.targetGraphic = dark;
        btn.onClick.AddListener(() => { Destroy(ov); onClose?.Invoke(); });

        ui.StartCoroutine(ui.RevealCards(cards));
    }

    RectTransform BuildCard(Transform parent, CardPack.GrantResult r, Vector2 pos, Vector2 size)
    {
        Color tint = r.player.RarityColor;
        GameObject go = new GameObject("Card_" + r.player.id);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        rt.localScale = Vector3.zero; // scaled in by RevealCards

        Image frame = go.AddComponent<Image>();
        frame.sprite = Rounded();
        frame.type = Image.Type.Sliced;
        frame.color = tint;
        frame.raycastTarget = false;

        Image fill = new GameObject("Fill").AddComponent<Image>();
        fill.transform.SetParent(go.transform, false);
        fill.sprite = Rounded();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.05f, 0.09f, 0.16f, 1f);
        fill.raycastTarget = false;
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);

        Text(go.transform, r.player.rarity.ToString().ToUpper(), 15f, new Vector2(0f, size.y * 0.5f - 26f),
             new Vector2(size.x - 12f, 22f), tint);
        Text(go.transform, r.player.overall.ToString(), 52f, new Vector2(0f, 42f),
             new Vector2(size.x, 60f), Color.white);
        Text(go.transform, r.player.position.ToString(), 18f, new Vector2(0f, 4f),
             new Vector2(size.x, 24f), new Color(0.7f, 0.8f, 0.95f));
        Text(go.transform, r.player.fullName, 19f, new Vector2(0f, -34f),
             new Vector2(size.x - 14f, 46f), Color.white);
        Text(go.transform, r.isNew ? "NEW!" : "+" + r.dupCoins + " COINS", 17f,
             new Vector2(0f, -size.y * 0.5f + 28f), new Vector2(size.x - 12f, 24f),
             r.isNew ? new Color(0.3f, 0.95f, 0.4f) : new Color(1f, 0.82f, 0.2f));
        return rt;
    }

    IEnumerator RevealCards(List<RectTransform> cards)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            RectTransform rt = cards[i];
            float t = 0f;
            const float dur = 0.22f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = 1f - (1f - k) * (1f - k) * (1f - k); // ease-out-cubic
                if (rt != null) rt.localScale = Vector3.one * e;
                yield return null;
            }
            if (rt != null) rt.localScale = Vector3.one;
        }
    }

    static TextMeshProUGUI Text(Transform parent, string s, float size, Vector2 pos, Vector2 box, Color c)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = s;
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.color = c;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        return t;
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
}
