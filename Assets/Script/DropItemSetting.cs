using UnityEngine; // これが必要！

[System.Serializable]
public class DropItemSetting
{
    public ItemData itemData;
    [Range(0, 100)]
    public float dropProbability;
}
