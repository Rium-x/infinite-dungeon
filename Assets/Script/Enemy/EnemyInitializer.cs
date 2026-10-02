using UnityEngine;

public class EnemyInitializer : MonoBehaviour
{
    // Intentionally does not move the enemy on Start. Scene-placed and room-prefab
    // enemies keep their authored position; EnemySpawner explicitly randomizes only
    // enemies it creates through the spawn list.
}
