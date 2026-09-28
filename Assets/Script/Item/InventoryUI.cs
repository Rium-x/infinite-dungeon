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
    }

    public List<SlotUI> displaySlots = new List<SlotUI>();

    [Header("説明UI")]
public TextMeshProUGUI descriptionText;

    [Header("円の設定")]
    public float radius = 300f;
    public float smoothSpeed = 10f;
    [Range(0, 360)] public float baseAngle = 90f;

    [Header("装備ハイライトの色")]
    public Color rightHandColor = new Color(1f, 0.2f, 0.2f, 0.6f);
    public Color leftHandColor = new Color(0.2f, 0.2f, 1f, 0.6f);

    private float currentVisualAngle = 0f;



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
            ui.nameText.text =
                (isSelected && hasItem)
                ? $"{itemStack.itemData.itemName} x{itemStack.amount}"
                : "";

            ui.nameText.enabled = isSelected && hasItem;
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