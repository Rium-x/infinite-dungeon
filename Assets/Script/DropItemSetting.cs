using UnityEngine;
using UnityEngine.Serialization;

[System.Serializable]
public class DropItemSetting
{
    public ItemData itemData;

    [Range(0, 100)]
    [Tooltip("このモンスターがこのアイテムを出す最大個数。ItemDataの個数抽選結果より優先します。")]
    public int maxDropCount = 100;

    [Range(-100f, 100f)]
    [Tooltip("このモンスター固有ドロップの補正値。ItemDataのレアリティ(%)に加算します。0=補正なし、+で出やすく、-で出にくくなります。")]
    [FormerlySerializedAs("relativeFrequencyAdjustment")]
    public float monsterModifier;
}
