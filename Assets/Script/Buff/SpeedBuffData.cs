using UnityEngine;

[CreateAssetMenu(
    fileName = "SpeedBuff",
    menuName = "Buff/Speed Buff"
)]
public class SpeedBuffData : BuffData
{
    [Header("速度倍率")]
    public float speedMultiplier = 1.5f;

    public override BuffInstance CreateInstance(
        GameObject target
    )
    {
        return new SpeedBuffInstance(
            target,
            this
        );
    }
}