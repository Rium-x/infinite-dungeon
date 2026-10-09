using UnityEngine;

public class MoveStats : MonoBehaviour
{
    [Header("速度倍率")]
    public float speedMultiplier = 1f;

    [Header("環境速度倍率（バフとは独立）")]
    [SerializeField, Min(0f)] private float environmentSpeedMultiplier = 1f;

    private void Awake()
    {
        // EnemyInitializer installs one shared effect on the character root.
        if (GetComponent<EnemyInitializer>() != null)
            return;

        if (GetComponent<WaterTerrainEffect>() == null)
            gameObject.AddComponent<WaterTerrainEffect>();
    }

    public void SetEnvironmentSpeedMultiplier(float multiplier)
    {
        environmentSpeedMultiplier = Mathf.Max(0f, multiplier);
    }

    public float ApplySpeed(float baseSpeed)
    {
        return
            baseSpeed
            * speedMultiplier
            * environmentSpeedMultiplier;
    }
}
