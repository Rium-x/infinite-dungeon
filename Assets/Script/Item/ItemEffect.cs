using UnityEngine;

public abstract class ItemEffect : ScriptableObject
{
    [Header("同種重複禁止")]
    public bool uniqueEffectType = true;

    public abstract void Execute(GameObject user);
}