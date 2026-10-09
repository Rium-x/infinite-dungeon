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
public enum TemporaryBuffType { Heal, Poison, Speed }

[Serializable]
public class TemporaryBuffSettings
{
    public TemporaryBuffType buffType;
    public string buffName = "一時バフ";
    [Min(0f)] public float duration = 10f;
    public bool isDebuff;
    [Min(1)] public int level = 1;
    public float healPerTick = 1f;
    [Min(0.01f)] public float tickInterval = 1f;
    public float damagePerTick = 1f;
    public float speedMultiplier = 1.5f;

    public BuffData CreateRuntimeData()
    {
        BuffData data;
        switch (buffType)
        {
            case TemporaryBuffType.Heal: data = ScriptableObject.CreateInstance<HealBuffData>(); break;
            case TemporaryBuffType.Poison: data = ScriptableObject.CreateInstance<PoisonBuffData>(); break;
            case TemporaryBuffType.Speed: data = ScriptableObject.CreateInstance<SpeedBuffData>(); break;
            default: return null;
        }
        data.name = buffName;
        data.buffName = buffName;
        data.duration = duration;
        data.isDebuff = isDebuff;
        data.level = level;
        if (data is HealBuffData heal) { heal.healPerTick = healPerTick; heal.tickInterval = tickInterval; }
        if (data is PoisonBuffData poison) { poison.damagePerTick = damagePerTick; poison.tickInterval = tickInterval; }
        if (data is SpeedBuffData speed) speed.speedMultiplier = speedMultiplier;
        return data;
    }
}

[Serializable]
public class ItemEffectData
{
    public ItemEffectType effectType;

    public int healAmount = 20;

    public BuffData buffData;
    public bool createTemporaryBuff;
    public TemporaryBuffSettings temporaryBuff = new TemporaryBuffSettings();

    public List<ItemRandomEffectData> randomEffects = new List<ItemRandomEffectData>();

    public ItemData replacementItem;

    public ItemData Execute(GameObject user)
    {
        switch (effectType)
        {
            case ItemEffectType.Heal:
                ItemEffectDataUtility.Heal(user, healAmount);
                return null;
            case ItemEffectType.Buff:
                ItemEffectDataUtility.ApplyBuff(user, createTemporaryBuff && temporaryBuff != null
                    ? temporaryBuff.CreateRuntimeData() : buffData, createTemporaryBuff);
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
    public ItemEffectType effectType;
    [Range(0f, 100f)] public float chance = 100f;

    public int healAmount = 20;

    public BuffData buffData;
    public bool createTemporaryBuff;
    public TemporaryBuffSettings temporaryBuff = new TemporaryBuffSettings();

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
            ItemEffectDataUtility.ApplyBuff(user, createTemporaryBuff && temporaryBuff != null
                ? temporaryBuff.CreateRuntimeData() : buffData, createTemporaryBuff);
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

    public static void ApplyBuff(GameObject user, BuffData buffData, bool temporary = false)
    {
        if (buffData == null)
        {
            Debug.LogWarning("BuffData が設定されていません。");
            return;
        }

        BuffController buffController = user != null ? user.GetComponent<BuffController>() : null;
        if (buffController != null)
            buffController.AddBuff(buffData, temporary);
        else
        {
            if (temporary) UnityEngine.Object.Destroy(buffData);
            Debug.LogWarning($"{user?.name ?? "使用者"} に BuffController がありません。");
        }
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

    [Header("耐久度")]
    [Min(1)] public int maxDurability = 100;
    public AudioClip breakSound;
    [Header("消費設定")]
    public bool consumeOnUse = true;
    public int DurabilityLimit => maxDurability > 0 ? maxDurability : 100;
    public bool HasDurability => itemType == ItemType.Weapon || itemType == ItemType.Armor ||
        (itemType == ItemType.Consumable && !consumeOnUse);

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
