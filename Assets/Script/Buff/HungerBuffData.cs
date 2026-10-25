using UnityEngine;

[CreateAssetMenu(fileName = "HungerBuff", menuName = "Buff/飢餓 (MP消費増加)")]
public class HungerBuffData : MPBuffData
{
    [Min(1f)] public float consumptionMultiplier = 2f;
}
