using UnityEngine;

[CreateAssetMenu(menuName = "Buff/Poison Buff")]
public class PoisonBuffData : BuffData
{
    public float damagePerTick = 1;

    public float tickInterval = 1f;

    public override BuffInstance CreateInstance(GameObject target)
    {
        return new PoisonBuffInstance(target, this);
    }
}