using UnityEngine;

public class MoveStats : MonoBehaviour
{
    [Header("速度倍率")]
    public float speedMultiplier = 1f;

    public float ApplySpeed(float baseSpeed)
    {
        return
            baseSpeed
            * speedMultiplier;
    }
}