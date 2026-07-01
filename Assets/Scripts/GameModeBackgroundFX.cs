using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Subtle, mobile-friendly ambient motion for the Game Mode background. Attached in code by
// NavigationManager to the overlay's sheet, so Update only ticks while the overlay is open
// (the sheet is a child of the overlay root, which is deactivated when closed → zero idle cost).
// Three cheap effects, ~3 draw calls total for the whole background layer:
//   • Ken-Burns drift  — the pool render very slowly pans + zooms (it is oversized so an edge
//                        never shows).
//   • Vignette breathe — the edge-darkening overlay's alpha gently pulses.
//   • Light specks     — a low count (<30) of soft dots drift upward, sway, and twinkle, all
//                        sharing one sprite so they batch into a single draw call.
[DisallowMultipleComponent]
public class GameModeBackgroundFX : MonoBehaviour
{
    RectTransform bg;
    Image vignette;
    float vignetteBaseA;
    float areaW, areaH;
    float time;

    readonly List<RectTransform> speckRt = new List<RectTransform>();
    readonly List<Image> speckImg = new List<Image>();
    readonly List<float> speckSpeed = new List<float>();
    readonly List<float> speckSway = new List<float>();
    readonly List<float> speckPhase = new List<float>();
    readonly List<float> speckBaseA = new List<float>();

    // Called once at build time (while the overlay is still inactive). Spawns the specks and
    // caches the animated targets. `speckSprite` is reused for every speck so they stay batched.
    public void Init(RectTransform background, Image vignetteImg, Transform speckParent,
                     Sprite speckSprite, int speckCount, float areaWidth, float areaHeight)
    {
        bg = background;
        vignette = vignetteImg;
        areaW = areaWidth;
        areaH = areaHeight;
        if (vignette != null) vignetteBaseA = vignette.color.a;

        for (int i = 0; i < speckCount; i++)
        {
            GameObject go = new GameObject("Speck" + i);
            go.transform.SetParent(speckParent, false);
            Image im = go.AddComponent<Image>();
            im.sprite = speckSprite;
            im.raycastTarget = false;

            RectTransform r = im.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            float sz = Random.Range(4f, 11f);
            r.sizeDelta = new Vector2(sz, sz);
            r.anchoredPosition = new Vector2(Random.Range(-areaW * 0.5f, areaW * 0.5f),
                                             Random.Range(-areaH * 0.5f, areaH * 0.5f));

            float a = Random.Range(0.05f, 0.18f);
            im.color = new Color(0.62f, 0.85f, 1f, a); // faint cool-white bokeh

            speckRt.Add(r);
            speckImg.Add(im);
            speckSpeed.Add(Random.Range(6f, 16f));
            speckSway.Add(Random.Range(4f, 9f));
            speckPhase.Add(Random.Range(0f, 6.2832f));
            speckBaseA.Add(a);
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        time += dt;

        // Ken-Burns: a very slow zoom (1.02–1.06) plus a small elliptical pan.
        if (bg != null)
        {
            float s = 1.04f + 0.02f * Mathf.Sin(time * 0.20f);
            bg.localScale = new Vector3(s, s, 1f);
            bg.anchoredPosition = new Vector2(Mathf.Sin(time * 0.13f) * 18f,
                                              Mathf.Cos(time * 0.10f) * 12f);
        }

        // Vignette breathing — a barely-there ±0.05 alpha pulse on the edge darkening.
        if (vignette != null)
        {
            Color c = vignette.color;
            c.a = Mathf.Max(0f, vignetteBaseA + 0.05f * Mathf.Sin(time));
            vignette.color = c;
        }

        // Specks drift up, sway sideways, twinkle, and wrap back to the bottom.
        for (int i = 0; i < speckRt.Count; i++)
        {
            RectTransform r = speckRt[i];
            if (r == null) continue;

            Vector2 p = r.anchoredPosition;
            p.y += speckSpeed[i] * dt;
            p.x += Mathf.Sin(time * 0.5f + speckPhase[i]) * speckSway[i] * dt;
            if (p.y > areaH * 0.5f + 14f)
            {
                p.y = -areaH * 0.5f - 14f;
                p.x = Random.Range(-areaW * 0.5f, areaW * 0.5f);
            }
            r.anchoredPosition = p;

            Image im = speckImg[i];
            if (im != null)
            {
                Color c = im.color;
                c.a = speckBaseA[i] * (0.6f + 0.4f * Mathf.Sin(time * 1.3f + speckPhase[i]));
                im.color = c;
            }
        }
    }
}
