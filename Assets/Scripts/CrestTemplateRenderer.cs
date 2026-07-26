using UnityEngine;
using UnityEngine.UI;

// Shared My Club crest presentation. Every screen uses the same normalized mask geometry;
// club names belong to the surrounding screen UI and are never baked into the crest.
public sealed class CrestTemplateView : MonoBehaviour
{
    public const float ContentScale = 0.90f;

    Image maskImage;
    Material materialInstance;

    public Image MaskImage => maskImage;

    public static CrestTemplateView Create(Transform parent, string objectName, Vector2 size,
                                           Vector2 anchor, Vector2 position)
    {
        GameObject root = new GameObject(objectName);
        root.transform.SetParent(parent, false);
        RectTransform rt = root.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        CrestTemplateView view = root.AddComponent<CrestTemplateView>();
        view.Build();
        return view;
    }

    void Build()
    {
        GameObject maskGo = new GameObject("TintedMask");
        maskGo.transform.SetParent(transform, false);
        maskImage = maskGo.AddComponent<Image>();
        maskImage.preserveAspect = true;
        maskImage.raycastTarget = false;
        RectTransform maskRt = maskImage.rectTransform;
        maskRt.anchorMin = maskRt.anchorMax = maskRt.pivot = new Vector2(0.5f, 0.5f);
        maskRt.anchoredPosition = Vector2.zero;
        maskRt.sizeDelta = Vector2.zero;

    }

    public void SetIdentity(ClubProfile profile)
    {
        if (profile == null) return;
        SetIdentity(profile.logoId,
                    ClubCustomizationUI.ParseHex(profile.primaryColorHex, ClubCustomizationUI.Palette[0]),
                    ClubCustomizationUI.ParseHex(profile.secondaryColorHex, Color.white),
                    ClubCustomizationUI.ParseHex(profile.tertiaryColorHex, ClubCustomizationUI.Palette[3]));
    }

    public void SetIdentity(int templateIndex, Color primary, Color secondary, Color tertiary)
    {
        if (maskImage == null) Build();
        CrestTemplateCatalog catalog = CrestTemplateCatalog.Instance;
        CrestTemplateCatalog.Entry entry = catalog != null ? catalog.Get(templateIndex) : null;
        if (entry == null || !entry.valid || entry.mask == null)
        {
            int fallback = catalog != null ? catalog.FirstValidIndex() : 0;
            entry = catalog != null ? catalog.Get(fallback) : null;
        }

        maskImage.sprite = entry != null ? entry.mask : null;
        maskImage.enabled = maskImage.sprite != null;
        EnsureMaterial(catalog);
        if (materialInstance != null)
        {
            materialInstance.SetColor("_PrimaryColor", primary);
            materialInstance.SetColor("_SecondaryColor", secondary);
            materialInstance.SetColor("_TertiaryColor", tertiary);
        }

        Layout();
    }

    void EnsureMaterial(CrestTemplateCatalog catalog)
    {
        Material source = catalog != null ? catalog.TintMaterial : null;
        if (source == null)
        {
            Shader shader = Shader.Find("UI/Crest Mask Tint");
            if (shader != null) source = new Material(shader);
        }
        if (source == null) return;
        if (materialInstance == null || materialInstance.shader != source.shader)
        {
            if (materialInstance != null) Destroy(materialInstance);
            materialInstance = new Material(source) { name = "CrestTint_Runtime" };
            maskImage.material = materialInstance;
        }
    }

    void OnRectTransformDimensionsChange()
    {
        if (maskImage != null) Layout();
    }

    void Layout()
    {
        RectTransform root = transform as RectTransform;
        if (root == null) return;
        float side = Mathf.Min(root.rect.width, root.rect.height) * ContentScale;
        if (side <= 0f) return;
        maskImage.rectTransform.sizeDelta = new Vector2(side, side);
    }

    void OnDestroy()
    {
        if (materialInstance != null) Destroy(materialInstance);
    }
}
