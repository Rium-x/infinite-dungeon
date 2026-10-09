using UnityEngine;

public class EnemyInitializer : MonoBehaviour
{
    private void Awake()
    {
        // EnemyInitializer is on the HB child in character prefabs. Put the effect
        // on the prefab root so sibling body parts are included as one character.
        GameObject characterRoot = transform.parent != null
            ? transform.parent.gameObject
            : gameObject;
        if (characterRoot.GetComponent<WaterTerrainEffect>() == null)
            characterRoot.AddComponent<WaterTerrainEffect>();
    }

    // Intentionally does not move the enemy on Start. Scene-placed and room-prefab
    // enemies keep their authored position; EnemySpawner explicitly randomizes only
    // enemies it creates through the spawn list.
}
