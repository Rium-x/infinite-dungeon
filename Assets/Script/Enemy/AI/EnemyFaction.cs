using UnityEngine;

public class EnemyFaction : MonoBehaviour
{
    public Faction faction = Faction.Neutral;
}

public enum Faction
{
    Player,

    Slime,
    Goblin,
    Skeleton,
    Orc,

    Plant,

    Neutral
}
