using UnityEngine;

[CreateAssetMenu(
    fileName = "HealBuff",
    menuName = "Buff/Heal Buff"
)]
public class HealBuffData : BuffData
{
    [Header("回復設定")]
    public float healPerTick = 1f;

    public float tickInterval = 1f;

    public override BuffInstance CreateInstance(
        GameObject target
    )
    {
        return new HealBuffInstance(
            target,
            this
        );
    }
}