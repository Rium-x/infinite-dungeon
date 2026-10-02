using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;
using System.Linq;

// Kept for importing older JSON values such as "Rare"; ItemData stores numeric rarity.
public enum ItemRarity { Common, Rare, Epic, Legendary }
public enum ItemType { Coin, Weapon, Consumable, Material, Armor, Garbage }
public enum HandLayer { BehindHand, InFrontOfHand }
public enum ArmorSlot { Hands, Body, Face, Feet }

[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;
    public ItemType itemType;
    [Range(0f, 100f), Tooltip("基本のドロップ確率(%)。0～100で設定し、大きいほど出やすくなります。通常ドロップではステージ補正を加算します。")]
    public float rarity;
    [Header("ドロップ個数")]
    [Min(1), Tooltip("ドロップ個数グラフの横軸上限です。")]
    public int maxDropCount = 10;
    [Tooltip("横軸は落ちる個数(1～上限)、縦軸はその個数の確率です。個数ごとのグラフ値を合計100%になるよう正規化して抽選します。整数個数の点をベル型に並べればガウス分布のような形になります。通常・モンスター固有の両ドロップに適用します。")]
    public AnimationCurve dropCountProbability = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(1f, 100f), new Keyframe(2f, 0f), new Keyframe(10f, 0f));

    [Header("初期設定")]
    public bool isDefaultItem = false;   // 初期配布するかどうか
    public int defaultAmount = 1; // 配布する個数

    [Header("Life Time Settings")]
    public float baseLifeTime = 180f;

    [Header("Physics Settings")]
    public float mass = 1.0f;
    public float collisionRadius = 0.5f;

    [Header("Equipment Settings")]
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

    [Header("Stack Settings")]
    public bool canStack = true; // ★ここをチェックすると重なる

    [Min(1)]
    public int maxStackSize = 99;

    [Header("Audio Settings")]
    public AudioClip pickupSound;
    public AudioClip dropSound;
    public AudioClip useSound;    // ★追加：使った時の音（回復薬など）

    [TextArea]
    public string description;

    [Header("Use Settings")]
    public bool isConsumable = false; // 使うと減るか
    [Header("Use Effects")]
public List<ItemEffect> itemEffects =
    new List<ItemEffect>();

    public void Use(GameObject user)
{
    foreach (ItemEffect effect in itemEffects)
    {
        if (effect != null)
        {
            effect.Execute(user);
        }
    }
}
}
