using UnityEngine;
using System.Collections; 

// ★「, IHealth」を追加して、TimeRewindManagerからの命令を受け取れるようにします
public class EquipmentManager : MonoBehaviour, IHealth
{
    public static EquipmentManager Instance;
    public Transform rightHandSlot, leftHandSlot;
    public Transform bodySlot, faceSlot;
    public Transform leftFootSlot, rightFootSlot;
    public GameObject fallbackEquipmentPrefab;
    public ItemData currentRightItem, currentLeftItem;
    public int currentRightSlot = -1, currentLeftSlot = -1;
    public ItemData currentHandsArmor, currentBodyArmor, currentFaceArmor, currentFeetArmor;
    public int currentHandsArmorSlot = -1, currentBodyArmorSlot = -1;
    public int currentFaceArmorSlot = -1, currentFeetArmorSlot = -1;
    public bool suppressAutoEquip;
    private GameObject rightVisual, leftVisual;
    private GameObject bodyVisual, faceVisual, leftFootVisual, rightFootVisual;
    private GameObject leftArmorHandVisual, rightArmorHandVisual;

    private void RefreshCharacterSortingForSlot(Transform slot)
    {
        if (slot == null)
        {
            return;
        }

        CharacterSorting sorting = slot.GetComponentInParent<CharacterSorting>();
        if (sorting != null)
        {
            sorting.RefreshRenderers();
        }
    }
    
    private void Awake() { if (Instance == null) Instance = this; }

    private void OnEnable()
    {
        if (Inventory.Instance != null)
            Inventory.Instance.InventoryUpdated += EquipSelectedInventoryItem;
    }

    private void OnDisable()
    {
        if (Inventory.Instance != null)
            Inventory.Instance.InventoryUpdated -= EquipSelectedInventoryItem;
    }

    private IEnumerator Start()
    {
        while (Inventory.Instance == null || Inventory.Instance.items == null || Inventory.Instance.items.Count == 0)
        {
            yield return null; 
        }

        if (DefaultItemProvider.Instance != null)
        {
            DefaultItemProvider.Instance.GiveDefaultItems();
            yield return null; 
        }

        if (currentRightItem != null) EquipItem(currentRightItem, false, currentRightSlot);
        if (currentLeftItem != null) EquipItem(currentLeftItem, true, currentLeftSlot);

        InitializeInitialEquipment();
        EquipSelectedInventoryItem();

        Debug.Log("EquipmentManager: 全ての初期化と装備が完了しました。");
    }

    private void EquipSelectedInventoryItem()
    {
        if (suppressAutoEquip)
            return;

        Inventory.Instance?.EquipSelectedItemToLeftHand();
    }

    public void InitializeInitialEquipment()
    {
        if (Inventory.Instance == null || Inventory.Instance.items == null) return;

        for (int i = 0; i < Inventory.Instance.items.Count; i++)
        {
            var stack = Inventory.Instance.items[i];
            if (stack == null || stack.itemData == null) continue;

            if (stack.itemData.isDefaultItem && stack.amount > 0)
            {
                if (currentRightSlot == -1)
                {
                    EquipItem(stack.itemData, false, i);
                }
                else if (currentLeftSlot == -1 && currentRightItem != stack.itemData)
                {
                    EquipItem(stack.itemData, true, i);
                }
            }

            if (currentRightSlot != -1 && currentLeftSlot != -1) break;
        }
    }

    public void EquipItem(ItemData newData, bool isLeft, int slotIndex)
    {
        if (newData == null)
        {
            if (isLeft) UnequipItem(true);
            return;
        }
        if (newData.itemType == ItemType.Armor)
        {
            if (!isLeft)
            {
                EquipArmor(newData, slotIndex);
                return;
            }

            if (IsArmorEquipped(newData.armorSlot))
            {
                UnequipItem(true);
                return;
            }
        }

        PlayerCombat playerCombat = Object.FindAnyObjectByType<PlayerCombat>();
        if (playerCombat != null) playerCombat.CancelCurrentAction();

        if (isLeft)
        {
            if (currentLeftItem == newData && currentLeftSlot == slotIndex)
            {
                UnequipItem(true);
                return;
            }
            if (currentRightSlot == slotIndex) UnequipItem(false);
            currentLeftItem = newData;
            currentLeftSlot = slotIndex;
            UpdateVisual(ref leftVisual, currentLeftItem, leftHandSlot, true);
        }
        else
        {
            if (currentRightItem == newData && currentRightSlot == slotIndex)
            {
                UnequipItem(false);
                return;
            }
            if (currentLeftSlot == slotIndex) UnequipItem(true);
            currentRightItem = newData;
            currentRightSlot = slotIndex;
            UpdateVisual(ref rightVisual, currentRightItem, rightHandSlot, false);
        }
    }

    private void EquipArmor(ItemData data, int slotIndex)
    {
        if (GetArmor(data.armorSlot) == data && GetArmorSlot(data.armorSlot) == slotIndex)
        {
            UnequipArmor(data.armorSlot);
            return;
        }

        if (currentLeftItem == data && currentLeftSlot == slotIndex)
            UnequipItem(true);

        bool previousSuppressAutoEquip = suppressAutoEquip;
        suppressAutoEquip = true;
        UnequipArmor(data.armorSlot);
        suppressAutoEquip = previousSuppressAutoEquip;
        SetArmor(data.armorSlot, data, slotIndex);
        switch (data.armorSlot)
        {
            case ArmorSlot.Hands:
                leftArmorHandVisual = CreateArmorVisual(data, leftHandSlot);
                rightArmorHandVisual = CreateArmorVisual(data, rightHandSlot);
                break;
            case ArmorSlot.Body: bodyVisual = CreateArmorVisual(data, bodySlot); break;
            case ArmorSlot.Face: faceVisual = CreateArmorVisual(data, faceSlot); break;
            case ArmorSlot.Feet:
                leftFootVisual = CreateArmorVisual(data, leftFootSlot);
                rightFootVisual = CreateArmorVisual(data, rightFootSlot);
                break;
        }
    }

    public bool IsArmorEquipped(ArmorSlot slot)
    {
        return GetArmor(slot) != null;
    }

    private ItemData GetArmor(ArmorSlot slot)
    {
        switch (slot)
        {
            case ArmorSlot.Hands: return currentHandsArmor;
            case ArmorSlot.Body: return currentBodyArmor;
            case ArmorSlot.Face: return currentFaceArmor;
            case ArmorSlot.Feet: return currentFeetArmor;
            default: return null;
        }
    }

    private int GetArmorSlot(ArmorSlot slot)
    {
        switch (slot)
        {
            case ArmorSlot.Hands: return currentHandsArmorSlot;
            case ArmorSlot.Body: return currentBodyArmorSlot;
            case ArmorSlot.Face: return currentFaceArmorSlot;
            case ArmorSlot.Feet: return currentFeetArmorSlot;
            default: return -1;
        }
    }

    private void SetArmor(ArmorSlot slot, ItemData data, int itemSlot)
    {
        switch (slot)
        {
            case ArmorSlot.Hands: currentHandsArmor = data; currentHandsArmorSlot = itemSlot; break;
            case ArmorSlot.Body: currentBodyArmor = data; currentBodyArmorSlot = itemSlot; break;
            case ArmorSlot.Face: currentFaceArmor = data; currentFaceArmorSlot = itemSlot; break;
            case ArmorSlot.Feet: currentFeetArmor = data; currentFeetArmorSlot = itemSlot; break;
        }
    }

    public void UnequipArmor(ArmorSlot slot)
    {
        switch (slot)
        {
            case ArmorSlot.Hands:
                DestroyVisual(ref leftArmorHandVisual); DestroyVisual(ref rightArmorHandVisual);
                currentHandsArmor = null; currentHandsArmorSlot = -1; break;
            case ArmorSlot.Body:
                DestroyVisual(ref bodyVisual); currentBodyArmor = null; currentBodyArmorSlot = -1; break;
            case ArmorSlot.Face:
                DestroyVisual(ref faceVisual); currentFaceArmor = null; currentFaceArmorSlot = -1; break;
            case ArmorSlot.Feet:
                DestroyVisual(ref leftFootVisual); DestroyVisual(ref rightFootVisual);
                currentFeetArmor = null; currentFeetArmorSlot = -1; break;
        }

        if (!suppressAutoEquip)
            Inventory.Instance?.EquipSelectedItemToLeftHand();
    }

    public void UnequipArmorAtSlot(int itemSlot)
    {
        if (currentHandsArmorSlot == itemSlot) UnequipArmor(ArmorSlot.Hands);
        if (currentBodyArmorSlot == itemSlot) UnequipArmor(ArmorSlot.Body);
        if (currentFaceArmorSlot == itemSlot) UnequipArmor(ArmorSlot.Face);
        if (currentFeetArmorSlot == itemSlot) UnequipArmor(ArmorSlot.Feet);
    }

    public void SwapEquippedSlots(int sourceIndex, int destinationIndex)
    {
        SwapSlot(ref currentRightSlot, sourceIndex, destinationIndex);
        SwapSlot(ref currentLeftSlot, sourceIndex, destinationIndex);
        SwapSlot(ref currentHandsArmorSlot, sourceIndex, destinationIndex);
        SwapSlot(ref currentBodyArmorSlot, sourceIndex, destinationIndex);
        SwapSlot(ref currentFaceArmorSlot, sourceIndex, destinationIndex);
        SwapSlot(ref currentFeetArmorSlot, sourceIndex, destinationIndex);
    }

    private void SwapSlot(ref int slot, int sourceIndex, int destinationIndex)
    {
        if (slot == sourceIndex) slot = destinationIndex;
        else if (slot == destinationIndex) slot = sourceIndex;
    }

    private GameObject CreateArmorVisual(ItemData data, Transform slot)
    {
        if (slot == null || data == null) return null;
        GameObject prefab = data.equipmentPrefab;
        if (prefab == null) prefab = fallbackEquipmentPrefab;
        if (prefab == null) return null;

        GameObject visual = Instantiate(prefab, slot);
        RefreshCharacterSortingForSlot(slot);
        return visual;
    }

    private void DestroyVisual(ref GameObject visual)
    {
        if (visual != null)
        {
            Transform parent = visual.transform.parent;
            if (parent != null)
            {
                RefreshCharacterSortingForSlot(parent);
            }
            Destroy(visual);
        }
        visual = null;
    }

    private void UpdateVisual(
        ref GameObject visualObj,
        ItemData data,
        Transform slot,
        bool isLeft
        )
    {
        if (visualObj != null) Destroy(visualObj);
        if (slot == null || data == null) return;

        bool useFallbackPrefab = data.itemType == ItemType.Armor && isLeft;
        GameObject prefab = useFallbackPrefab
            ? fallbackEquipmentPrefab
            : data.equipmentPrefab != null ? data.equipmentPrefab : fallbackEquipmentPrefab;
        if (prefab == null) return;

        visualObj = Instantiate(prefab, slot);

        if (useFallbackPrefab || data.equipmentPrefab == null)
        {
            SpriteRenderer spriteRenderer = visualObj.GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = data.icon;
                spriteRenderer.sortingOrder = data.handLayer == HandLayer.BehindHand ? 7 : 10;
            }
        }

        // ItemData の手前/後ろ設定を反映した後で、これを元の値として登録する。
        RefreshCharacterSortingForSlot(slot);

        Vector3 baseScale = visualObj.transform.localScale * data.equipScale;
        Vector3 pos = data.equipPositionOffset;

        if (isLeft) { pos.x *= -1; baseScale.x *= -1; }

        visualObj.transform.localPosition = pos;
        visualObj.transform.localRotation = Quaternion.Euler(data.equipRotationOffset);
        visualObj.transform.localScale = baseScale;
    }

    public void UnequipItem(bool isLeft)
    {
        PlayerCombat playerCombat = Object.FindAnyObjectByType<PlayerCombat>();
        if (playerCombat != null) playerCombat.CancelCurrentAction();

        if (isLeft) { currentLeftItem = null; currentLeftSlot = -1; if (leftVisual != null) Destroy(leftVisual); }
        else
        {
            currentRightItem = null;
            currentRightSlot = -1;
            if (rightVisual != null) Destroy(rightVisual);
            if (!suppressAutoEquip)
                Inventory.Instance?.EquipSelectedItemToLeftHand();
        }
    }

    // ─── ★【追加】タイムリワインド用の装備データ保存＆復元処理 ───

    // 現在の装備データをひとまとめにした構造体
    public struct EquipmentSnapshot
    {
        public ItemData rightItem;
        public int rightSlot;
        public ItemData leftItem;
        public int leftSlot;
        public ItemData handsArmor, bodyArmor, faceArmor, feetArmor;
        public int handsArmorSlot, bodyArmorSlot, faceArmorSlot, feetArmorSlot;
    }

    // 現在の左右の装備状態をスナップショットとして返す
    public EquipmentSnapshot CreateEquipmentSnapshot()
    {
        return new EquipmentSnapshot
        {
            rightItem = this.currentRightItem,
            rightSlot = this.currentRightSlot,
            leftItem = this.currentLeftItem,
            leftSlot = this.currentLeftSlot,
            handsArmor = this.currentHandsArmor,
            handsArmorSlot = this.currentHandsArmorSlot,
            bodyArmor = this.currentBodyArmor,
            bodyArmorSlot = this.currentBodyArmorSlot,
            faceArmor = this.currentFaceArmor,
            faceArmorSlot = this.currentFaceArmorSlot,
            feetArmor = this.currentFeetArmor,
            feetArmorSlot = this.currentFeetArmorSlot
        };
    }

    // マネージャーから指定された過去の装備状態に完全に復元する
    public void RestoreEquipmentSnapshot(EquipmentSnapshot snapshot)
    {
        // 1. いったん現在の見た目のゲームオブジェクトを削除
        if (rightVisual != null) Destroy(rightVisual);
        if (leftVisual != null) Destroy(leftVisual);

        // 2. データを過去の値に上書き
        this.currentRightItem = snapshot.rightItem;
        this.currentRightSlot = snapshot.rightSlot;
        this.currentLeftItem = snapshot.leftItem;
        this.currentLeftSlot = snapshot.leftSlot;

        // 3. 過去に持っていた武器の見た目（Prefab）を再生成して手に持たせる
        if (this.currentRightItem != null)
        {
            UpdateVisual(ref rightVisual, currentRightItem, rightHandSlot, false);
        }
        if (this.currentLeftItem != null)
        {
            UpdateVisual(ref leftVisual, currentLeftItem, leftHandSlot, true);
        }

        UnequipArmor(ArmorSlot.Hands);
        UnequipArmor(ArmorSlot.Body);
        UnequipArmor(ArmorSlot.Face);
        UnequipArmor(ArmorSlot.Feet);
        if (snapshot.handsArmor != null) EquipArmor(snapshot.handsArmor, snapshot.handsArmorSlot);
        if (snapshot.bodyArmor != null) EquipArmor(snapshot.bodyArmor, snapshot.bodyArmorSlot);
        if (snapshot.faceArmor != null) EquipArmor(snapshot.faceArmor, snapshot.faceArmorSlot);
        if (snapshot.feetArmor != null) EquipArmor(snapshot.feetArmor, snapshot.feetArmorSlot);
    }

    // IHealthインターフェースを動かすためのダミー関数（空でOK）
    public void ForceRevive(float dummyHealthValue) { }
}