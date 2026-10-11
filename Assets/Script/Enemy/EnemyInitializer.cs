using System.Collections.Generic;
using UnityEngine;

public enum WaterSpawnPreference
{
    Neutral,
    WaterLoving,
    LandLoving
}

public class EnemyInitializer : MonoBehaviour
{
    [Header("水の影響")]
    [SerializeField, Min(0f)] private float waterSpeedPercent = 50f;
    [SerializeField] private bool takeDamageInWater;
    [SerializeField, Min(0f)] private float waterDamagePerSecond = 5f;
    [SerializeField] private bool takeDamageOutsideWater;
    [SerializeField, Min(0f)] private float outsideWaterDamagePerSecond = 5f;
    [SerializeField] private bool avoidWater;
    [SerializeField] private bool cannotLeaveWater;

    [Header("個別に下半身を消すオブジェクト")]
    [Tooltip("敵全体の水面カット位置を使わず、各SpriteRendererの中央から下を個別に消します。")]
    [SerializeField] private List<GameObject> individualWaterFadeObjects = new List<GameObject>();

    // Existing water behavior settings define the spawn affinity too:
    // water-bound enemies spawn in water, water-avoiding enemies on land.
    public WaterSpawnPreference WaterSpawnPreference => cannotLeaveWater
        ? WaterSpawnPreference.WaterLoving
        : avoidWater ? WaterSpawnPreference.LandLoving : WaterSpawnPreference.Neutral;

    private void Awake()
    {
        // EnemyInitializer is on the HB child in character prefabs. Put the effect
        // on the prefab root so sibling body parts are included as one character.
        GameObject characterRoot = transform.parent != null
            ? transform.parent.gameObject
            : gameObject;
        if (characterRoot.GetComponentInChildren<EnemyHealth>(true) != null
            && characterRoot.GetComponentInChildren<MoveStats>(true) == null)
            characterRoot.AddComponent<MoveStats>();

        WaterTerrainEffect effect = characterRoot.GetComponent<WaterTerrainEffect>();
        if (effect == null)
            effect = characterRoot.AddComponent<WaterTerrainEffect>();
        effect.ConfigureMonsterWater(waterSpeedPercent, takeDamageInWater, waterDamagePerSecond,
            takeDamageOutsideWater, outsideWaterDamagePerSecond, avoidWater, cannotLeaveWater,
            individualWaterFadeObjects);
    }

    // Intentionally does not move the enemy on Start. Scene-placed and room-prefab
    // enemies keep their authored position; EnemySpawner explicitly randomizes only
    // enemies it creates through the spawn list.
}
