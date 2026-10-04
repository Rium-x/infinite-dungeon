using UnityEngine;
using System.Collections.Generic;

public class RandomEffect : ItemEffect
{
    [System.Serializable]
    public class RandomEntry
    {
        public ItemEffect effect;

        [Range(0f, 100f)]
        public float chance = 100f;
    }

    public List<RandomEntry> randomEffects =
        new List<RandomEntry>();

    public override void Execute(GameObject user)
    {
        foreach (RandomEntry entry in randomEffects)
        {
            if (entry.effect == null)
                continue;

            float roll =
                Random.Range(0f, 100f);

            if (roll <= entry.chance)
            {
                entry.effect.Execute(user);
            }
        }
    }
}