using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Poolside cameraman camera-flashes, purely cosmetic. Self-bootstrapping (the StaminaSystem /
// PoolThemeApplier pattern — no scene object, no wiring): after every scene load it finds all
// GameObjects tagged "Cameraman" (by tag only, names don't matter) and gives each one an
// independent flash loop — every 4-10 s (re-rolled per cameraman per flash) a small white glow
// pops at its position: near-instant fade-in, ~0.18 s fade-out with a slight expanding scale.
// Scenes with no tagged cameramen (hub, main menu) install nothing.
public class CameramanFX : MonoBehaviour
{
    const float MinIntervalSeconds = 4f;
    const float MaxIntervalSeconds = 10f;
    const float FadeInSeconds = 0.04f;
    const float FadeOutSeconds = 0.18f;
    const float FlashWorldSize = 0.45f;   // glow diameter at full scale (world units)

    static Sprite glowSprite; // shared radial glow; regenerated after a domain reload

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) => Install();
        Install(); // the first scene already loaded before the callback was registered
    }

    static void Install()
    {
        GameObject[] cameramen;
        try { cameramen = GameObject.FindGameObjectsWithTag("Cameraman"); }
        catch (UnityException) { return; } // tag not defined in this project — nothing to do
        if (cameramen.Length == 0) return;
        if (FindAnyObjectByType<CameramanFX>() != null) return; // already installed this scene

        GameObject go = new GameObject("CameramanFX");
        CameramanFX fx = go.AddComponent<CameramanFX>();
        foreach (GameObject cam in cameramen)
            fx.StartCoroutine(fx.FlashLoop(cam.transform));
    }

    IEnumerator FlashLoop(Transform cameraman)
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(MinIntervalSeconds, MaxIntervalSeconds));
            if (cameraman == null || !cameraman.gameObject.activeInHierarchy) continue;
            yield return Flash(cameraman);
        }
    }

    IEnumerator Flash(Transform cameraman)
    {
        // Sit the flash a touch above the sprite's visual centre — reads as the camera, not the feet.
        SpriteRenderer camSr = cameraman.GetComponentInChildren<SpriteRenderer>();
        Vector3 pos = camSr != null
            ? camSr.bounds.center + new Vector3(0f, camSr.bounds.extents.y * 0.4f, 0f)
            : cameraman.position;

        GameObject go = new GameObject("Flash");
        go.transform.position = pos;
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GlowSprite();
        if (camSr != null)
        {
            sr.sortingLayerID = camSr.sortingLayerID;
            sr.sortingOrder = camSr.sortingOrder + 1; // just over the cameraman art
        }
        float fullScale = FlashWorldSize / sr.sprite.bounds.size.x;

        // Fade in fast...
        float t = 0f;
        while (t < FadeInSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / FadeInSeconds);
            sr.color = new Color(1f, 1f, 1f, k);
            go.transform.localScale = Vector3.one * (fullScale * Mathf.Lerp(0.7f, 1f, k));
            yield return null;
        }

        // ...out over ~0.18 s, expanding slightly as it dies.
        t = 0f;
        while (t < FadeOutSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / FadeOutSeconds);
            sr.color = new Color(1f, 1f, 1f, 1f - k);
            go.transform.localScale = Vector3.one * (fullScale * Mathf.Lerp(1f, 1.25f, k));
            yield return null;
        }
        Destroy(go);
    }

    // Soft white radial glow: bright core, quadratic falloff to transparent at the rim.
    static Sprite GlowSprite()
    {
        if (glowSprite != null) return glowSprite;
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = Mathf.Clamp01(1f - r);
                a *= a; // quadratic falloff — hot centre, soft rim
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        glowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                   SpriteMeshType.FullRect);
        return glowSprite;
    }
}
