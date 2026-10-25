using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem; // Input Systemをインポート

public class PlayerCombat : MonoBehaviour
{
    public Transform rightHand;
    public Transform leftHand;

    [Min(0f)]
    public float consumableCooldown = 0.2f;

    private PlayerControls controls;
    private float nextConsumableUseTime;
    
    private enum HandSide { None, Right, Left }
    private HandSide activeHand = HandSide.None;
    private bool isRightHeld = false;
    private bool isLeftHeld = false;

    private void Awake()
    {
        controls = new PlayerControls();

        // Input Systemのアクションを登録
        controls.Player.UseRight.started += ctx =>
        {
            isRightHeld = true;
            HandleUseStarted(rightHand);
        };
        controls.Player.UseRight.canceled += ctx =>
        {
            isRightHeld = false;
            HandleUseCanceled(rightHand);
        };

        controls.Player.UseLeft.started += ctx =>
        {
            isLeftHeld = true;
            HandleUseStarted(leftHand);
        };
        controls.Player.UseLeft.canceled += ctx =>
        {
            isLeftHeld = false;
            HandleUseCanceled(leftHand);
        };
    }

    private void OnEnable() => controls.Player.Enable();

    private void OnDisable()
    {
        controls.Player.Disable();
        ResetActionState();
    }

    public void ResetActionState()
    {
        isRightHeld = false;
        isLeftHeld = false;
        CancelCurrentAction();
    }

    public void CancelCurrentAction()
    {
        StopAllCoroutines();
        activeHand = HandSide.None;

        if (rightHand != null)
        {
            IWeapon rightWeapon = rightHand.GetComponentInChildren<IWeapon>();
            if (rightWeapon is ShieldAction rightShield)
            {
                rightShield.SetGuard(false);
            }
            else if (rightWeapon is NewSwordAction rightSword)
            {
                rightSword.CancelAction();
            }
        }

        if (leftHand != null)
        {
            IWeapon leftWeapon = leftHand.GetComponentInChildren<IWeapon>();
            if (leftWeapon is ShieldAction leftShield)
            {
                leftShield.SetGuard(false);
            }
            else if (leftWeapon is NewSwordAction leftSword)
            {
                leftSword.CancelAction();
            }
        }
    }

    private void HandleUseStarted(Transform hand)
    {
        if (BSPDungeonMaster.IsPlayerInputLocked) return;
        if (hand == null) return;

        if (TryUseConsumable(hand)) return;

        // Determine which hand this is
        HandSide thisHand = hand == rightHand ? HandSide.Right : HandSide.Left;

        // If another hand is currently active, do not start now; the held flag
        // has already been set by the event handler and Monitor coroutine
        // will start this hand when the active one finishes.
        if (activeHand != HandSide.None)
        {
            return;
        }

        IWeapon weapon = hand.GetComponentInChildren<IWeapon>();
        if (weapon is ShieldAction shield)
        {
            // lock this hand and start guarding
            activeHand = thisHand;
            shield.SetGuard(true);
            StartCoroutine(MonitorWeaponUntilIdle(weapon, thisHand));
            return;
        }

        // If there's a weapon, lock and perform its attack. If none, just run PerformAttack (logs).
        if (weapon != null)
        {
            activeHand = thisHand;
            PerformAttack(hand);
            StartCoroutine(MonitorWeaponUntilIdle(weapon, thisHand));
        }
        else
        {
            PerformAttack(hand);
        }
    }

    private ItemData GetEquippedItemData(Transform hand)
    {
        EquipmentManager equipment = EquipmentManager.Instance;
        if (equipment == null) return null;
        return hand == rightHand ? equipment.currentRightItem : equipment.currentLeftItem;
    }

    /// <summary>Called by player weapon hit detection after a confirmed hit.</summary>
    public bool SpendWeaponMPForHit(Transform weaponTransform)
    {
        if (weaponTransform == null || PlayerMP.Instance == null) return true;
        Transform hand;
        if (rightHand != null && weaponTransform.IsChildOf(rightHand)) hand = rightHand;
        else if (leftHand != null && weaponTransform.IsChildOf(leftHand)) hand = leftHand;
        else return true;

        ItemData equippedData = GetEquippedItemData(hand);
        return PlayerMP.Instance.TrySpendAttack(equippedData != null ? equippedData.mpCostPerAttack : 0f);
    }

    private bool TryUseConsumable(Transform hand)
    {
        if (Inventory.Instance == null || EquipmentManager.Instance == null) return false;

        bool isRightHand = hand == rightHand;
        int slotIndex = isRightHand
            ? EquipmentManager.Instance.currentRightSlot
            : EquipmentManager.Instance.currentLeftSlot;
        if (slotIndex < 0 || slotIndex >= Inventory.Instance.items.Count) return false;

        ItemStack heldStack = Inventory.Instance.items[slotIndex];
        if (heldStack == null || heldStack.itemData == null || !heldStack.itemData.IsConsumable)
            return false;

        if (Time.time < nextConsumableUseTime) return true;

        Inventory.Instance.ConsumeItem(slotIndex);
        nextConsumableUseTime = Time.time + consumableCooldown;
        return true;
    }

    private void HandleUseCanceled(Transform hand)
    {
        if (hand == null) return;

        IWeapon weapon = hand.GetComponentInChildren<IWeapon>();
        if (weapon is ShieldAction shield)
        {
            //解除は任せ、MonitorCoroutine が完了を検出してロックを解除します
            shield.SetGuard(false);
        }
    }

    // 攻撃処理を共通化
    private void PerformAttack(Transform hand)
    {
        if (hand == null) return;

        // 手そのもの、またはその子供から IWeapon を探す
        IWeapon weapon = hand.GetComponentInChildren<IWeapon>();

        if (weapon is not ShieldAction && PlayerMP.Instance != null && PlayerMP.Instance.IsExhausted)
            return;
        
        if (weapon != null)
        {
            if (weapon is ShieldAction)
            {
                // 盾は長押しで防御継続、通常の Attack はここでは使わない
                return;
            }

            weapon.Attack();
        }
        else
        {
            Debug.Log(hand.name + " に武器が装備されていません。");
        }
    }

    public void DamageWeaponDurability(Transform weaponTransform)
    {
        if (weaponTransform == null || Inventory.Instance == null || EquipmentManager.Instance == null) return;

        int slot;
        if (rightHand != null && weaponTransform.IsChildOf(rightHand))
            slot = EquipmentManager.Instance.currentRightSlot;
        else if (leftHand != null && weaponTransform.IsChildOf(leftHand))
            slot = EquipmentManager.Instance.currentLeftSlot;
        else
            return;

        Inventory.Instance.DamageDurability(slot);
    }

    private IEnumerator MonitorWeaponUntilIdle(IWeapon weapon, HandSide hand)
    {
        if (weapon == null)
        {
            // no weapon to monitor: release immediately
            if (activeHand == hand) activeHand = HandSide.None;
            yield break;
        }

        // Poll until the weapon indicates it's no longer busy.
        while (true)
        {
            bool busy = IsWeaponBusy(weapon);
            if (!busy) break;
            yield return null;
        }
        if (activeHand == hand) activeHand = HandSide.None;

        // After releasing, if the opposite hand's input is still held, start it immediately.
        HandSide opposite = hand == HandSide.Right ? HandSide.Left : HandSide.Right;

        if (opposite == HandSide.Right && isRightHeld && rightHand != null)
        {
            IWeapon nextWeapon = rightHand.GetComponentInChildren<IWeapon>();
            if (nextWeapon != null)
            {
                activeHand = HandSide.Right;
                if (nextWeapon is ShieldAction s)
                {
                    s.SetGuard(true);
                }
                else
                {
                    PerformAttack(rightHand);
                }
                StartCoroutine(MonitorWeaponUntilIdle(nextWeapon, HandSide.Right));
                yield break;
            }
        }

        if (opposite == HandSide.Left && isLeftHeld && leftHand != null)
        {
            IWeapon nextWeapon = leftHand.GetComponentInChildren<IWeapon>();
            if (nextWeapon != null)
            {
                activeHand = HandSide.Left;
                if (nextWeapon is ShieldAction s)
                {
                    s.SetGuard(true);
                }
                else
                {
                    PerformAttack(leftHand);
                }
                StartCoroutine(MonitorWeaponUntilIdle(nextWeapon, HandSide.Left));
                yield break;
            }
        }
    }

    private bool IsWeaponBusy(IWeapon weapon)
    {
        if (weapon == null) return false;

        // Prefer known concrete types
        if (weapon is ShieldAction shield) return shield.IsBusy;
        if (weapon is NewSwordAction sword) return sword.IsBusy;

        // Fallback: try reflection for common property names
        var mb = weapon as MonoBehaviour;
        if (mb != null)
        {
            var type = mb.GetType();
            var prop = type.GetProperty("IsBusy");
            if (prop != null && prop.PropertyType == typeof(bool))
                return (bool)prop.GetValue(mb);

            prop = type.GetProperty("IsAttacking");
            if (prop != null && prop.PropertyType == typeof(bool))
                return (bool)prop.GetValue(mb);

            prop = type.GetProperty("IsGuarding");
            if (prop != null && prop.PropertyType == typeof(bool))
                return (bool)prop.GetValue(mb);
        }

        return false;
    }
}
