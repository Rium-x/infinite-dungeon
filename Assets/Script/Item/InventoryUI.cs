using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    [System.Serializable]
    public struct SlotUI
    {
        public RectTransform rect;
        public Image iconImage;
        public TextMeshProUGUI nameText;
        public Image highlightImage;
        public Slider durabilitySlider;
    }

    public List<SlotUI> displaySlots = new List<SlotUI>();

    [Header("スロット複製元（親と全ての子を複製）")]
    [Tooltip("複製する階層のルート。ルート自身と全ての子オブジェクトを複製します。")]
    public Transform slotParent;
    public RectTransform slotRect;
    public Image slotIconImage;
    public TextMeshProUGUI slotNameText;
    public Image slotHighlightImage;
    public Slider slotDurabilitySlider;

    [Header("説明UI")]
public TextMeshProUGUI descriptionText;

    [Header("円の設定")]
    public float radius = 300f;
    public float smoothSpeed = 10f;
    [Range(0, 360)] public float baseAngle = 90f;

    [Header("装備ハイライトの色")]
    public Color rightHandColor = new Color(1f, 0.2f, 0.2f, 0.6f);
    public Color leftHandColor = new Color(0.2f, 0.2f, 1f, 0.6f);

    [Header("耐久度ゲージの色")]
    [SerializeField] private Color lowDurabilityColor = new Color(0.76f, 0.39f, 0.41f);
    [SerializeField] private Color mediumDurabilityColor = new Color(0.79f, 0.69f, 0.39f);
    [SerializeField] private Color highDurabilityColor = new Color(0.39f, 0.66f, 0.45f);

    private float currentVisualAngle = 0f;

    private void Start()
    {
        BuildDisplaySlots();
    }

    private void BuildDisplaySlots()
    {
        if (slotParent == null || slotRect == null)
            return;

        int count = Inventory.Instance != null ? Inventory.Instance.maxSlots : 0;
        if (count <= 0) return;

        Transform templateRoot = slotParent;
        Transform parent = templateRoot.parent;
        var slots = new List<SlotUI>(count);
        slots.Add(new SlotUI
        {
            rect = slotRect,
            iconImage = slotIconImage,
            nameText = slotNameText,
            highlightImage = slotHighlightImage,
            durabilitySlider = slotDurabilitySlider
        });

        for (int i = 1; i < count; i++)
        {
            Transform clone = Instantiate(templateRoot, parent);
            clone.name = templateRoot.name + "_" + i;
            slots.Add(new SlotUI
            {
                rect = FindCloneComponent<RectTransform>(templateRoot, slotRect, clone),
                iconImage = FindCloneComponent<Image>(templateRoot, slotIconImage, clone),
                nameText = FindCloneComponent<TextMeshProUGUI>(templateRoot, slotNameText, clone),
                highlightImage = FindCloneComponent<Image>(templateRoot, slotHighlightImage, clone),
                durabilitySlider = FindCloneComponent<Slider>(templateRoot, slotDurabilitySlider, clone)
            });
            if (slots[i].durabilitySlider != null)
            {
                slots[i].durabilitySlider.gameObject.SetActive(false);
                Destroy(slots[i].durabilitySlider.gameObject);
            }
        }

        // Remove the manually authored extra slots; the template itself remains as slot zero.
        for (int i = 0; i < displaySlots.Count; i++)
        {
            if (displaySlots[i].rect != null && displaySlots[i].rect != slotRect &&
                displaySlots[i].rect != templateRoot)
                Destroy(displaySlots[i].rect.gameObject);
        }

        displaySlots = slots;
    }

    private static T FindCloneComponent<T>(Transform sourceRoot, T source, Transform cloneRoot) where T : Component
    {
        if (source == null) return null;
        Transform current = source.transform;
        string path = "";
        while (current != sourceRoot && current != null)
        {
            path = path.Length == 0 ? current.name : current.name + "/" + path;
            current = current.parent;
        }
        if (current != sourceRoot) return null;
        Transform target = path.Length == 0 ? cloneRoot : cloneRoot.Find(path);
        return target != null ? target.GetComponent<T>() : null;
    }



    void Update()
    {
        if (Inventory.Instance == null || displaySlots.Count == 0) return;

        int totalSlots = Inventory.Instance.maxSlots;
        int current = Inventory.Instance.selectedSlot;
        float angleStep = 360f / totalSlots;

        currentVisualAngle = Mathf.LerpAngle(
            currentVisualAngle,
            current * angleStep,
            Time.deltaTime * smoothSpeed
        );

        for (int i = 0; i < displaySlots.Count; i++)
        {
            if (displaySlots[i].rect == null) continue;

            float slotAngle = currentVisualAngle - (i * angleStep) + baseAngle;
            float radian = slotAngle * Mathf.Deg2Rad;

            displaySlots[i].rect.localPosition =
                new Vector3(
                    Mathf.Cos(radian) * radius,
                    Mathf.Sin(radian) * radius,
                    0
                );

            float angleDiff =
                Mathf.Abs(Mathf.DeltaAngle(slotAngle, baseAngle));

            displaySlots[i].rect.localScale =
                Vector3.one * Mathf.Max(0.6f, 1.3f - (angleDiff / 100f));

            UpdateEquipmentHighlight(displaySlots[i], i);

            UpdateSlotData(displaySlots[i], i, angleDiff < 20f);
        }

        UpdateDurabilityGauge(current);
        UpdateDescription();
    }

    private void UpdateEquipmentHighlight(SlotUI ui, int index)
    {
        if (ui.highlightImage == null || EquipmentManager.Instance == null)
            return;

        int rSlot = EquipmentManager.Instance.currentRightSlot;
        int lSlot = EquipmentManager.Instance.currentLeftSlot;

        if (index == rSlot)
        {
            ui.highlightImage.enabled = true;
            ui.highlightImage.color = rightHandColor;
        }
        else if (index == lSlot)
        {
            ui.highlightImage.enabled = true;
            ui.highlightImage.color = leftHandColor;
        }
        else
        {
            ui.highlightImage.enabled = false;
        }
    }

    private void UpdateSlotData(SlotUI ui, int index, bool isSelected)
    {
        var itemStack = Inventory.Instance.items[index];

        bool hasItem =
            itemStack != null &&
            itemStack.itemData != null;

        if (ui.iconImage != null)
        {
            ui.iconImage.enabled = hasItem;

            if (hasItem)
            {
                ui.iconImage.sprite = itemStack.itemData.icon;
            }
        }

        if (ui.nameText != null)
        {
            if (isSelected && hasItem)
            {
                ui.nameText.text = $"{itemStack.itemData.itemName} x{itemStack.amount}";
            }
            else ui.nameText.text = "";

            ui.nameText.enabled = isSelected && hasItem;
        }

    }

    private void UpdateDurabilityGauge(int selectedIndex)
    {
        if (slotDurabilitySlider == null || Inventory.Instance == null)
            return;

        bool hasSelectedItem =
            selectedIndex >= 0 &&
            selectedIndex < Inventory.Instance.items.Count &&
            Inventory.Instance.items[selectedIndex] != null &&
            Inventory.Instance.items[selectedIndex].itemData != null;
        ItemStack selectedStack = hasSelectedItem ? Inventory.Instance.items[selectedIndex] : null;
        bool show =
            selectedStack != null &&
            selectedStack.itemData.HasDurability &&
            selectedIndex < displaySlots.Count &&
            displaySlots[selectedIndex].rect != null;

        slotDurabilitySlider.gameObject.SetActive(show);
        if (!show)
            return;

        slotDurabilitySlider.transform.SetParent(displaySlots[selectedIndex].rect, false);
        int maxDurability = selectedStack.itemData.DurabilityLimit;
        float ratio = Mathf.Clamp01((float)selectedStack.durability / maxDurability);
        slotDurabilitySlider.minValue = 0f;
        slotDurabilitySlider.maxValue = maxDurability;
        slotDurabilitySlider.wholeNumbers = true;
        slotDurabilitySlider.SetValueWithoutNotify(selectedStack.durability);

        Image fill = slotDurabilitySlider.fillRect != null
            ? slotDurabilitySlider.fillRect.GetComponent<Image>()
            : null;
        if (fill != null)
        {
            Color color = ratio < 0.5f
                ? Color.Lerp(lowDurabilityColor, mediumDurabilityColor, ratio * 2f)
                : Color.Lerp(mediumDurabilityColor, highDurabilityColor, (ratio - 0.5f) * 2f);
            fill.color = color;
        }
    }

private void UpdateDescription()
{
    if (descriptionText == null) return;

    int selected = Inventory.Instance.selectedSlot;

    var itemStack = Inventory.Instance.items[selected];

    bool hasItem =
        itemStack != null &&
        itemStack.itemData != null;

    if (hasItem)
    {
        descriptionText.text =
            itemStack.itemData.description;

    }
    else
    {
        descriptionText.text = "";
    }
}
}
