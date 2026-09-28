using UnityEngine;
using System.Collections.Generic;
using System;
using UnityEngine.InputSystem; 

[System.Serializable]
public class ItemStack
{
    public ItemData itemData;
    public int amount; 
    public ItemStack(ItemData data, int count) { itemData = data; amount = count; }
}

public class Inventory : MonoBehaviour
{
    public static Inventory Instance;
    public GameObject itemPrefab;
    public int maxSlots = 10;
    public List<ItemStack> items = new List<ItemStack>();
    public event Action InventoryUpdated;

    public int selectedSlot = 0;

    private PlayerControls controls;

    private void Awake()
    {
        if (Instance == null) Instance = this;

        controls = new PlayerControls();

        controls.Player.Drop.performed += ctx => OnDropAction();
        controls.Player.NextSlot.performed += ctx => OnPrevSlotAction();
        controls.Player.PrevSlot.performed += ctx => OnNextSlotAction();
        controls.Player.EquipRight.performed += ctx => OnEquipRightAction();
    }

    private void OnEnable()  => controls.Enable();
    private void OnDisable() => controls.Disable();

    void Start()
    {
        items.Clear();
        for (int i = 0; i < maxSlots; i++) items.Add(null);
    }

    // ===== Input System コールバック =====

    private void OnDropAction()
    {
        if (BSPDungeonMaster.IsPlayerInputLocked) return;
        if (items[selectedSlot] == null) return;
        DropItem(selectedSlot);
    }

    private void OnNextSlotAction()
    {
        if (BSPDungeonMaster.IsPlayerInputLocked) return;
        selectedSlot = (selectedSlot + 1) % maxSlots;
        UpdateUI();
    }

    private void OnPrevSlotAction()
    {
        if (BSPDungeonMaster.IsPlayerInputLocked) return;
        selectedSlot = (selectedSlot - 1 + maxSlots) % maxSlots;
        UpdateUI();
    }

    public void EquipSelectedItemToLeftHand()
    {
        if (EquipmentManager.Instance == null) return;
        if (selectedSlot < 0 || selectedSlot >= items.Count ||
            items[selectedSlot] == null || items[selectedSlot].itemData == null)
        {
            EquipmentManager.Instance.UnequipItem(true);
            return;
        }

        ItemData data = items[selectedSlot].itemData;

        if (EquipmentManager.Instance.currentRightSlot == selectedSlot)
        {
            EquipmentManager.Instance.UnequipItem(true);
            return;
        }

        if (EquipmentManager.Instance.currentLeftSlot == selectedSlot &&
            EquipmentManager.Instance.currentLeftItem == data)
        {
            return;
        }

        EquipmentManager.Instance.EquipItem(data, true, selectedSlot);
    }

    private void OnEquipRightAction()
    {
        if (BSPDungeonMaster.IsPlayerInputLocked) return;
        EquipSelectedItemToRightHand();
    }

    private void EquipSelectedItemToRightHand()
    {
        if (EquipmentManager.Instance == null || selectedSlot < 0 || selectedSlot >= items.Count) return;
        if (items[selectedSlot] == null || items[selectedSlot].itemData == null) return;

        EquipmentManager.Instance.EquipItem(items[selectedSlot].itemData, false, selectedSlot);
    }

    // =====================================================
    // ロジック部分
    // =====================================================

    public bool AddItem(ItemData data, int amount)
    {
        if (amount <= 0) return true;

        if (!data.canStack)
        {
            for (int a = 0; a < amount; a++)
            {
                bool added = false;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] == null)
                    {
                        items[i] = new ItemStack(data, 1);
                        added = true;
                        break;
                    }
                }
                if (!added)
                {
                    UpdateUI();
                    return false;
                }
            }
            UpdateUI();
            return true;
        }

        for (int i = 0; i < items.Count && amount > 0; i++)
        {
            if (items[i] != null && items[i].itemData == data)
            {
                int space = data.maxStackSize - items[i].amount;
                if (space <= 0) continue;

                int add = Mathf.Min(space, amount);
                items[i].amount += add;
                amount -= add;
            }
        }

        for (int i = 0; i < items.Count && amount > 0; i++)
        {
            if (items[i] == null)
            {
                int add = Mathf.Min(data.maxStackSize, amount);
                items[i] = new ItemStack(data, add);
                amount -= add;
            }
        }

        UpdateUI();
        return amount <= 0;
    }

    public bool AddItem(ItemData data)
    {
        if (data.canStack)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].itemData == data && items[i].amount < data.maxStackSize)
                {
                    items[i].amount++;
                    UpdateUI();
                    return true;
                }
            }
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null || items[i].itemData == null)
            {
                items[i] = new ItemStack(data, 1);
                UpdateUI();
                return true;
            }
        }

        NotificationManager.Instance.Show("空きスロットが見つかりませんでした。", Color.red, NotificationManager.NotificationSound.Warning);
        return false;
    }

    public bool HasItem(ItemData data)
    {
        foreach (ItemStack stack in items)
        {
            if (stack != null && stack.itemData == data) return true;
        }
        return false;
    }

    public bool TryCraft(CraftingRecipe recipe)
    {
        if (recipe == null || recipe.outputs == null || recipe.outputs.Count == 0)
            return false;
        return TryCraft(recipe, recipe.outputs);
    }

    public bool TryCraft(CraftingRecipe recipe, CraftingItemAmount selectedOutput)
    {
        if (recipe == null || recipe.outputs == null || selectedOutput == null || !recipe.outputs.Contains(selectedOutput))
            return false;
        return TryCraft(recipe, new List<CraftingItemAmount> { selectedOutput });
    }

    private bool TryCraft(CraftingRecipe recipe, List<CraftingItemAmount> outputs)
    {
        if (recipe == null || recipe.inputs == null || outputs == null)
            return false;

        if (recipe.successChance < 100f &&
            UnityEngine.Random.Range(0f, 100f) > recipe.successChance)
        {
            return false;
        }

        ItemData equippedRightItem = null;
        int equippedRightSlot = -1;
        if (EquipmentManager.Instance != null)
        {
            equippedRightItem = EquipmentManager.Instance.currentRightItem;
            equippedRightSlot = EquipmentManager.Instance.currentRightSlot;
            EquipmentManager.Instance.suppressAutoEquip = true;
        }

        Dictionary<ItemData, int> required = recipe.consumeInputs
            ? BuildAmounts(recipe.inputs)
            : new Dictionary<ItemData, int>();
        Dictionary<ItemData, int> available = BuildInventoryAmounts();

        foreach (KeyValuePair<ItemData, int> entry in required)
        {
            if (entry.Key == null || !available.TryGetValue(entry.Key, out int amount) || amount < entry.Value)
            {
                if (EquipmentManager.Instance != null)
                    EquipmentManager.Instance.suppressAutoEquip = false;
                return false;
            }
        }

        List<ItemStack> simulatedItems = CloneItems();
        RemoveAmounts(simulatedItems, required);

        List<CraftingItemAmount> effectiveOutputs =
            BuildOutputAmounts(outputs, recipe.outputMultiplier);

        if (!TryAddAmounts(simulatedItems, effectiveOutputs))
        {
            if (EquipmentManager.Instance != null)
                EquipmentManager.Instance.suppressAutoEquip = false;
            return false;
        }

        for (int i = 0; i < items.Count; i++)
        {
            items[i] = simulatedItems[i];
        }

        if (EquipmentManager.Instance != null)
        {
            if (equippedRightSlot >= 0 &&
                (equippedRightSlot >= items.Count || items[equippedRightSlot] == null ||
                 items[equippedRightSlot].itemData != equippedRightItem))
            {
                EquipmentManager.Instance.UnequipItem(false);
            }
            UnequipIfSlotIsEmpty(EquipmentManager.Instance.currentLeftSlot, true);
            EquipmentManager.Instance.suppressAutoEquip = false;
        }

        UpdateUI();
        return true;
    }

    private static List<CraftingItemAmount> BuildOutputAmounts(
        List<CraftingItemAmount> outputs,
        int multiplier)
    {
        List<CraftingItemAmount> scaledOutputs =
            new List<CraftingItemAmount>();

        foreach (CraftingItemAmount output in outputs)
        {
            if (output == null)
                continue;

            scaledOutputs.Add(new CraftingItemAmount
            {
                itemData = output.itemData,
                amount = output.amount * Mathf.Max(1, multiplier)
            });
        }

        return scaledOutputs;
    }

    private Dictionary<ItemData, int> BuildInventoryAmounts()
    {
        Dictionary<ItemData, int> amounts = new Dictionary<ItemData, int>();
        foreach (ItemStack stack in items)
        {
            if (stack == null || stack.itemData == null) continue;
            if (!amounts.ContainsKey(stack.itemData)) amounts[stack.itemData] = 0;
            amounts[stack.itemData] += stack.amount;
        }
        return amounts;
    }

    private static Dictionary<ItemData, int> BuildAmounts(List<CraftingItemAmount> entries)
    {
        Dictionary<ItemData, int> amounts = new Dictionary<ItemData, int>();
        foreach (CraftingItemAmount entry in entries)
        {
            if (entry == null || entry.itemData == null || entry.amount <= 0) continue;
            if (!amounts.ContainsKey(entry.itemData)) amounts[entry.itemData] = 0;
            amounts[entry.itemData] += entry.amount;
        }
        return amounts;
    }

    private List<ItemStack> CloneItems()
    {
        List<ItemStack> clone = new List<ItemStack>(items.Count);
        foreach (ItemStack stack in items)
        {
            clone.Add(stack == null ? null : new ItemStack(stack.itemData, stack.amount));
        }
        return clone;
    }

    private static void RemoveAmounts(List<ItemStack> target, Dictionary<ItemData, int> amounts)
    {
        foreach (KeyValuePair<ItemData, int> entry in amounts)
        {
            int remaining = entry.Value;
            for (int i = 0; i < target.Count && remaining > 0; i++)
            {
                if (target[i] == null || target[i].itemData != entry.Key) continue;
                int removed = Mathf.Min(target[i].amount, remaining);
                target[i].amount -= removed;
                remaining -= removed;
                if (target[i].amount <= 0) target[i] = null;
            }
        }
    }

    private static bool TryAddAmounts(List<ItemStack> target, List<CraftingItemAmount> entries)
    {
        foreach (CraftingItemAmount entry in entries)
        {
            if (entry == null || entry.itemData == null || entry.amount <= 0) continue;
            int remaining = entry.amount;

            if (entry.itemData.canStack)
            {
                for (int i = 0; i < target.Count && remaining > 0; i++)
                {
                    if (target[i] == null || target[i].itemData != entry.itemData) continue;
                    int space = entry.itemData.maxStackSize - target[i].amount;
                    int added = Mathf.Min(Mathf.Max(0, space), remaining);
                    target[i].amount += added;
                    remaining -= added;
                }
            }

            while (remaining > 0)
            {
                int emptyIndex = target.FindIndex(stack => stack == null || stack.itemData == null);
                if (emptyIndex < 0) return false;

                int added = entry.itemData.canStack
                    ? Mathf.Min(entry.itemData.maxStackSize, remaining)
                    : 1;
                target[emptyIndex] = new ItemStack(entry.itemData, added);
                remaining -= added;
            }
        }
        return true;
    }

    private void UnequipIfSlotIsEmpty(int slotIndex, bool isLeft)
    {
        if (slotIndex < 0 || slotIndex >= items.Count || items[slotIndex] == null)
            EquipmentManager.Instance.UnequipItem(isLeft);
    }

    public void ConsumeItem(int index)
    {
        if (index < 0 || index >= items.Count || items[index] == null || items[index].itemData == null) return;

        ItemData data = items[index].itemData;

        PlayerHealth playerHealth = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
        if (playerHealth == null)
        {
            Debug.LogError("シーン内に PlayerHealth が見つからないため、アイテムを使用できません！");
            return;
        }

        if (playerHealth.IsAtFullHealth && ContainsHealEffect(data))
            return;

        if (data.useSound != null)
        {
            AudioSource.PlayClipAtPoint(data.useSound, Camera.main.transform.position);
        }

        data.Use(playerHealth.gameObject);
        NotificationManager.Instance.Show($"{data.itemName} を使用しました。");

        if (data.isConsumable)
        {
            items[index].amount--;
            if (items[index].amount <= 0) items[index] = null;
        }

        UpdateUI();
    }

    public bool MoveItem(int sourceIndex, int destinationIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= items.Count ||
            destinationIndex < 0 || destinationIndex >= items.Count ||
            sourceIndex == destinationIndex)
        {
            return false;
        }

        ItemStack sourceItem = items[sourceIndex];
        items[sourceIndex] = items[destinationIndex];
        items[destinationIndex] = sourceItem;

        if (EquipmentManager.Instance != null)
        {
            EquipmentManager.Instance.SwapEquippedSlots(sourceIndex, destinationIndex);
        }

        UpdateUI();
        return true;
    }

    private bool ContainsHealEffect(ItemData data)
    {
        foreach (ItemEffect effect in data.itemEffects)
        {
            if (effect is HealEffect)
                return true;
        }

        return false;
    }

    public void DropItem(int index)
    {
        if (index < 0 || items[index] == null) return;

        ItemData dataToDrop = items[index].itemData;

        if (dataToDrop.dropSound != null)
        {
            Vector3 soundPos = Camera.main.transform.position;
            AudioSource.PlayClipAtPoint(dataToDrop.dropSound, soundPos);
        }

        Transform spawnPoint = transform.Find("HB");
        Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : transform.position;

        GameObject droppedObj = Instantiate(itemPrefab, spawnPos, Quaternion.identity);

        Item itemScript = droppedObj.GetComponent<Item>();
        if (itemScript != null)
        {
            itemScript.SetItemData(dataToDrop);
            itemScript.isPermanent = true;
        }

        Rigidbody2D rb = droppedObj.GetComponent<Rigidbody2D>();
        if (rb != null) rb.AddForce(UnityEngine.Random.insideUnitCircle * 0.0005f, ForceMode2D.Impulse);

        items[index].amount--;
        if (items[index].amount <= 0) items[index] = null;

        if (items[index] == null && EquipmentManager.Instance != null)
        {
            if (EquipmentManager.Instance.currentRightSlot == index) EquipmentManager.Instance.UnequipItem(false);
            if (EquipmentManager.Instance.currentLeftSlot == index) EquipmentManager.Instance.UnequipItem(true);
            EquipmentManager.Instance.UnequipArmorAtSlot(index);
        }

        UpdateUI();
    }

    public List<ItemStack> GetAndClearAllItems()
    {
        List<ItemStack> allItems = new List<ItemStack>(items);
        for (int i = 0; i < items.Count; i++) items[i] = null;
        UpdateUI();
        return allItems;
    }

    private void UpdateUI()
    {
        InventoryUpdated?.Invoke();
    }

    public void SetupDefaultItems(List<ItemData> allItems)
    {
        ClearInventory();
        foreach (var data in allItems)
        {
            if (data != null && data.isDefaultItem) AddItem(data, data.defaultAmount);
        }
        Debug.Log("初期装備を配布しました。");
    }

    public void ClearInventory()
    {
        for (int i = 0; i < items.Count; i++) items[i] = null;
    }
}