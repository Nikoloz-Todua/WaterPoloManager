using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shared My Club crest presentation. Every screen uses the same normalized mask/name geometry;
// only the outer RectTransform size changes.
public sealed class CrestTemplateView : MonoBehaviour
{
    public const float ContentScale = 0.90f;
    const float NameWidth = 0.66f;
    const float NameHeight = 0.19f;
    const float NameY = -0.02f;

    Image maskImage;
    TextMeshProUGUI nameText;
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

        GameObject nameGo = new GameObject("ClubName");
        nameGo.transform.SetParent(transform, false);
        nameText = nameGo.AddComponent<TextMeshProUGUI>();
        nameText.fontStyle = FontStyles.Bold;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.color = Color.white;
        nameText.enableAutoSizing = true;
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        nameText.raycastTarget = false;
        Shadow shadow = nameGo.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        shadow.useGraphicAlpha = true;
    }

    public void SetIdentity(ClubProfile profile)
    {
        if (profile == null) return;
        SetIdentity(profile.logoId,
                    ClubCustomizationUI.ParseHex(profile.primaryColorHex, ClubCustomizationUI.Palette[0]),
                    ClubCustomizationUI.ParseHex(profile.secondaryColorHex, Color.white),
                    ClubCustomizationUI.ParseHex(profile.tertiaryColorHex, ClubCustomizationUI.Palette[3]),
                    profile.clubName);
    }

    public void SetIdentity(int templateIndex, Color primary, Color secondary, Color tertiary, string clubName)
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

        string cleanName = string.IsNullOrWhiteSpace(clubName) ? "MY CLUB" : clubName.Trim();
        if (cleanName.Length > 9) cleanName = cleanName.Substring(0, 9);
        nameText.text = cleanName.ToUpperInvariant();
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
        if (maskImage != null && nameText != null) Layout();
    }

    void Layout()
    {
        RectTransform root = transform as RectTransform;
        if (root == null) return;
        float side = Mathf.Min(root.rect.width, root.rect.height) * ContentScale;
        if (side <= 0f) return;
        maskImage.rectTransform.sizeDelta = new Vector2(side, side);

        RectTransform nameRt = nameText.rectTransform;
        nameRt.anchorMin = nameRt.anchorMax = nameRt.pivot = new Vector2(0.5f, 0.5f);
        nameRt.anchoredPosition = new Vector2(0f, side * NameY);
        nameRt.sizeDelta = new Vector2(side * NameWidth, side * NameHeight);
        nameText.fontSizeMax = Mathf.Max(3f, side * 0.135f);
        nameText.fontSizeMin = Mathf.Max(2f, side * 0.055f);
    }

    void OnDestroy()
    {
        if (materialInstance != null) Destroy(materialInstance);
    }
}
