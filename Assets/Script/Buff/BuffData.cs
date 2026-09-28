using UnityEngine;

public abstract class BuffData : ScriptableObject
{
    [Header("基本情報")]
    public string buffName;

    public float duration;

    public bool isDebuff;

    public Sprite icon;

    [Header("レベル")]
    public int level = 1;

    public abstract BuffInstance CreateInstance(
        GameObject target
    );
}