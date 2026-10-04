using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.Collections.Generic;

// Kept for importing older JSON values such as "Rare"; ItemData stores numeric rarity.
public enum ItemRarity { Common, Rare, Epic, Legendary }
public enum ItemType { Coin, Weapon, Consumable, Material, Armor, Garbage }
public enum HandLayer { BehindHand, InFrontOfHand }
public enum ArmorSlot { Hands, Body, Face, Feet }
public enum ItemEffectType { Heal, Buff, Random, TransformItem }

[Serializable]
public class ItemEffectData
{
    [Header("効果")]
    public ItemEffectType effectType;

    [Header("回復")]
    public int healAmount = 20;

    [Header("バフ")]
    public BuffData buffData;

    [Header("ランダム効果")]
    public List<ItemRandomEffectData> randomEffects = new List<ItemRandomEffectData>();

    [Header("変化先")]
    public ItemData replacementItem;

    public ItemData Execute(GameObject user)
    {
        switch (effectType)
        {
            case ItemEffectType.Heal:
                ItemEffectDataUtility.Heal(user, healAmount);
                return null;
            case ItemEffectType.Buff:
                ItemEffectDataUtility.ApplyBuff(user, buffData);
                return null;
            case ItemEffectType.Random:
                if (randomEffects == null)
                    return null;

                ItemData replacement = null;
                foreach (ItemRandomEffectData entry in randomEffects)
                {
                    if (entry != null && UnityEngine.Random.Range(0f, 100f) <= entry.chance)
                    {
                        ItemData result = entry.Execute(user);
                        if (result != null)
                            replacement = result;
                    }
                }
                return replacement;
            case ItemEffectType.TransformItem:
                return replacementItem;
            default:
                return null;
        }
    }

}

[Serializable]
public class ItemRandomEffectData
{
    [Header("抽選")]
    public ItemEffectType effectType;
    [Range(0f, 100f)] public float chance = 100f;

    [Header("回復")]
    public int healAmount = 20;

    [Header("バフ")]
    public BuffData buffData;

    [Header("変化先")]
    public ItemData replacementItem;

    public ItemData Execute(GameObject user)
    {
        if (effectType == ItemEffectType.Heal)
        {
            ItemEffectDataUtility.Heal(user, healAmount);
            return null;
        }
        else if (effectType == ItemEffectType.Buff)
        {
            ItemEffectDataUtility.ApplyBuff(user, buffData);
            return null;
        }
        else if (effectType == ItemEffectType.TransformItem)
        {
            return replacementItem;
        }

        return null;
    }

}

internal static class ItemEffectDataUtility
{
    public static void Heal(GameObject user, int amount)
    {
        PlayerHealth health = user != null ? user.GetComponent<PlayerHealth>() : null;
        if (health != null)
        {
            health.Heal(amount);
            Debug.Log($"{user.name} を {amount} 回復させました！");
        }
        else
        {
            Debug.LogWarning("PlayerHealth が見つかりません！");
        }
    }

    public static void ApplyBuff(GameObject user, BuffData buffData)
    {
        if (buffData == null)
        {
            Debug.LogWarning("BuffData が設定されていません。");
            return;
        }

        BuffController buffController = user != null ? user.GetComponent<BuffController>() : null;
        if (buffController != null)
            buffController.AddBuff(buffData);
        else
            Debug.LogWarning($"{user?.name ?? "使用者"} に BuffController がありません。");
    }
}

[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item")]
public class ItemData : ScriptableObject
{
    [Header("基本情報")]
    public string itemName;
    public Sprite icon;
    public ItemType itemType;

    [Header("ドロップ設定")]
    [Range(0f, 100f), Tooltip("基本のドロップ確率(%)。0～100で設定し、大きいほど出やすくなります。通常ドロップではステージ補正を加算します。")]
    public float rarity;
    [Header("ドロップ個数")]
    [Min(1), Tooltip("ドロップ個数グラフの横軸上限です。")]
    public int maxDropCount = 10;
    [Tooltip("横軸は落ちる個数(1～上限)、縦軸はその個数の確率です。個数ごとのグラフ値を合計100%になるよう正規化して抽選します。整数個数の点をベル型に並べればガウス分布のような形になります。通常・モンスター固有の両ドロップに適用します。")]
    public AnimationCurve dropCountProbability = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(1f, 100f), new Keyframe(2f, 0f), new Keyframe(10f, 0f));

    [Header("初期所持")]
    public bool isDefaultItem = false;   // 初期配布するかどうか
    public int defaultAmount = 1; // 配布する個数

    [Header("落ちているアイテム")]
    [Tooltip("地面に落ちてから自動消滅するまでの秒数")]
    public float baseLifeTime = 180f;

    [Tooltip("落下時の物理質量")]
    public float mass = 1.0f;
    [Tooltip("落下時の衝突判定半径")]
    public float collisionRadius = 0.5f;

    [Header("装備設定")]
    public bool equipOnStart = false; // Whether the item should be equipped on start
    [FormerlySerializedAs("weaponPrefab")]
    [Tooltip("装備時に使うプレハブ。未指定の場合はアイテム用プレハブを使います。")]
    public GameObject equipmentPrefab;
    [Tooltip("ItemTypeがArmorの時だけ表示されます。")]
    public ArmorSlot armorSlot = ArmorSlot.Body;
    public HandLayer handLayer = HandLayer.InFrontOfHand;
    public Vector3 equipPositionOffset = new Vector3(0f, 0.05f, 0f);
    public Vector3 equipRotationOffset;
    public float visualScale = 3.0f; // 落ちている時
    public float equipScale = 2.0f;  // 持っている時

    [Header("スタック設定")]
    public bool canStack = true; // ★ここをチェックすると重なる

    [Min(1)]
    public int maxStackSize = 99;

    [Header("サウンド")]
    public AudioClip pickupSound;
    public AudioClip dropSound;
    public AudioClip useSound;    // ★追加：使った時の音（回復薬など）

    [Header("説明")]
    [TextArea]
    public string description;

    [HideInInspector] public bool isConsumable; // 旧アセット互換用。使用可否はItemTypeで判定。
    public bool IsConsumable => itemType == ItemType.Consumable;

    [Header("使用効果")]
    public List<ItemEffectData> useEffects = new List<ItemEffectData>();

    [SerializeField, HideInInspector, FormerlySerializedAs("itemEffects")]
    private List<ItemEffect> legacyItemEffects = new List<ItemEffect>();

    public ItemData Use(GameObject user)
    {
        if (useEffects == null)
            return null;

        ItemData replacement = null;
        foreach (ItemEffectData effect in useEffects)
        {
            if (effect != null)
            {
                ItemData result = effect.Execute(user);
                if (result != null)
                    replacement = result;
            }
        }
        return replacement;
    }

    public bool MigrateLegacyEffects()
    {
        if (legacyItemEffects == null || legacyItemEffects.Count == 0)
            return false;

        if (useEffects == null)
            useEffects = new List<ItemEffectData>();

        foreach (ItemEffect legacyEffect in legacyItemEffects)
        {
            ItemEffectData converted = ConvertLegacyEffect(legacyEffect);
            if (converted != null)
                useEffects.Add(converted);
        }

        legacyItemEffects.Clear();
        return true;
    }

    private static ItemEffectData ConvertLegacyEffect(ItemEffect effect)
    {
        if (effect is HealEffect healEffect)
        {
            return new ItemEffectData
            {
                effectType = ItemEffectType.Heal,
                healAmount = healEffect.healAmount
            };
        }

        if (effect is BuffEffect buffEffect)
        {
            return new ItemEffectData
            {
                effectType = ItemEffectType.Buff,
                buffData = buffEffect.buffData
            };
        }

        if (effect is RandomEffect randomEffect)
        {
            ItemEffectData converted = new ItemEffectData { effectType = ItemEffectType.Random };
            if (randomEffect.randomEffects == null)
                return converted;

            foreach (RandomEffect.RandomEntry entry in randomEffect.randomEffects)
            {
                if (entry?.effect is HealEffect childHeal)
                {
                    converted.randomEffects.Add(new ItemRandomEffectData
                    {
                        effectType = ItemEffectType.Heal,
                        chance = entry.chance,
                        healAmount = childHeal.healAmount
                    });
                }
                else if (entry?.effect is BuffEffect childBuff)
                {
                    converted.randomEffects.Add(new ItemRandomEffectData
                    {
                        effectType = ItemEffectType.Buff,
                        chance = entry.chance,
                        buffData = childBuff.buffData
                    });
                }
            }
            return converted;
        }

        return null;
    }
}
