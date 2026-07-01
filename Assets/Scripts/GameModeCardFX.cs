using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Per-card interaction feedback for the Game Mode competition cards. Attached in code by
// NavigationManager right after the card is built (no Inspector wiring). Two modes:
//   • Unlocked → smooth eased hover/press scale (1.0 ↔ 1.05) + golden border brightening,
//                and a tap fires the supplied activate action (start the match).
//   • Locked   → a tap plays a short decaying horizontal "locked bounce" shake, flashes the
//                lock-sign brighter, and fades the "WIN … TO UNLOCK" label fully in.
// Everything runs on unscaled time (menus are timescale-independent) and is coroutine-driven,
// so there are no per-frame Update costs when idle.
[DisallowMultipleComponent]
public class GameModeCardFX : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    const float RestScale = 1f, HoverScale = 1.05f, PressScale = 0.98f, Ease = 0.15f;

    RectTransform rt;
    bool locked;
    bool pointerInside;

    // Unlocked state.
    Image frame;
    Color restFrame, hoverFrame;
    System.Action onActivate;
    Coroutine anim;

    // Locked state.
    Image lockIcon;
    TMP_Text unlockText;
    static readonly Color LockRest = new Color(0.88f, 0.88f, 0.92f, 1f); // dimmed so the tap flash reads brighter
    bool bouncing;

    public void InitUnlocked(Image cardFrame, Color baseGold, bool selected, System.Action activate)
    {
        rt = (RectTransform)transform;
        locked = false;
        frame = cardFrame;
        onActivate = activate;
        restFrame = selected ? new Color(1f, 0.9f, 0.42f, 1f) : baseGold;   // selected card sits a touch brighter
        hoverFrame = new Color(1f, 0.97f, 0.62f, 1f);                       // hover/press brightens further
        if (frame != null) frame.color = restFrame;
    }

    public void InitLocked(Image lockSign, TMP_Text unlockLabel)
    {
        rt = (RectTransform)transform;
        locked = true;
        lockIcon = lockSign;
        unlockText = unlockLabel;
        if (lockIcon != null) lockIcon.color = LockRest;
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (locked) return;
        pointerInside = true;
        StartAnim(HoverScale, hoverFrame, Ease);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (locked) return;
        pointerInside = false;
        StartAnim(RestScale, restFrame, Ease);
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (locked) return;
        StartAnim(PressScale, hoverFrame, 0.08f);
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (locked) return;
        StartAnim(pointerInside ? HoverScale : RestScale, pointerInside ? hoverFrame : restFrame, 0.1f);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (locked)
        {
            if (!bouncing) StartCoroutine(LockedBounce());
            return;
        }
        onActivate?.Invoke();
    }

    void StartAnim(float scale, Color col, float dur)
    {
        if (rt == null) return;
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(AnimTo(scale, col, dur));
    }

    IEnumerator AnimTo(float scale, Color col, float dur)
    {
        Vector3 s0 = rt.localScale, s1 = Vector3.one * scale;
        Color c0 = frame != null ? frame.color : Color.white;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur)); // ease-in-out
            rt.localScale = Vector3.LerpUnclamped(s0, s1, k);
            if (frame != null) frame.color = Color.Lerp(c0, col, k);
            yield return null;
        }
        rt.localScale = s1;
        if (frame != null) frame.color = col;
        anim = null;
    }

    IEnumerator LockedBounce()
    {
        bouncing = true;
        Vector2 basePos = rt.anchoredPosition;
        if (unlockText != null) StartCoroutine(FadeTextIn());

        const float dur = 0.3f, amp = 12f, cycles = 3f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            float x = Mathf.Sin(k * cycles * 2f * Mathf.PI) * amp * (1f - k); // decaying wiggle
            rt.anchoredPosition = basePos + new Vector2(x, 0f);

            if (lockIcon != null)
            {
                float f = Mathf.Sin(Mathf.Clamp01(k * 2f) * Mathf.PI); // 0→1→0 across the first half
                lockIcon.color = Color.Lerp(LockRest, Color.white, f);
                lockIcon.rectTransform.localScale = Vector3.one * (1f + 0.18f * f);
            }
            yield return null;
        }

        rt.anchoredPosition = basePos;
        if (lockIcon != null)
        {
            lockIcon.color = LockRest;
            lockIcon.rectTransform.localScale = Vector3.one;
        }
        bouncing = false;
    }

    IEnumerator FadeTextIn()
    {
        float start = unlockText.alpha, t = 0f;
        const float d = 0.18f;
        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            unlockText.alpha = Mathf.Lerp(start, 1f, Mathf.Clamp01(t / d));
            yield return null;
        }
        unlockText.alpha = 1f;
    }
}
