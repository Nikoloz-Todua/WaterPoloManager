using UnityEngine;

public class WaterScroller : MonoBehaviour
{
    [SerializeField] private float scrollX = 0.005f;
    [SerializeField] private float scrollY = 0.002f;

    private static readonly int ScrollOffsetId = Shader.PropertyToID("_ScrollOffset");

    private SpriteRenderer sr;
    private MaterialPropertyBlock mpb;
    private Vector2 offset;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        mpb = new MaterialPropertyBlock();
    }

    void Update()
    {
        offset += new Vector2(scrollX, scrollY) * Time.deltaTime;
        // Keep the offset in [0,1) so it never loses float precision in long sessions.
        offset.x = Mathf.Repeat(offset.x, 1f);
        offset.y = Mathf.Repeat(offset.y, 1f);

        sr.GetPropertyBlock(mpb);
        mpb.SetVector(ScrollOffsetId, offset);
        sr.SetPropertyBlock(mpb);
    }
}
