using UnityEngine;

public static class FactionUtility
{
    public static bool IsEnemy(
        Faction self,
        Faction other
    )
    {
        if (self == other)
            return false;

        if (self == Faction.Neutral)
            return false;

        if (other == Faction.Neutral)
            return false;

        return true;
    }
}