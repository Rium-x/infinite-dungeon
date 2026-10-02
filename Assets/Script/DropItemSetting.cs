using UnityEngine;
using UnityEngine.Serialization;

[System.Serializable]
public class DropItemSetting
{
    public ItemData itemData;

    [Range(-100f, 100f)]
    [Tooltip("このモンスター固有ドロップの補正値。ItemDataのレアリティ(%)に加算します。0=補正なし、+で出やすく、-で出にくくなります。")]
    [FormerlySerializedAs("relativeFrequencyAdjustment")]
    public float monsterModifier;
}
