using System.Collections.Generic;
using UnityEngine;

public enum CraftingProcessMethod
{
    Crafting,
    Baking,
    Dismantling,
    Alchemy,
    Smelting,
    Cooking,
    Carpentry,
    Other
}

[System.Serializable]
public class CraftingItemAmount
{
    public ItemData itemData;

    [Min(1)]
    public int amount = 1;
}

[CreateAssetMenu(fileName = "NewCraftingRecipe", menuName = "Inventory/Crafting Recipe")]
public class CraftingRecipe : ScriptableObject
{
    public string displayName;
    public string category;
    public CraftingProcessMethod processingMethod = CraftingProcessMethod.Crafting;
    [Range(0f, 100f)]
    public float successChance = 100f;
    public bool consumeInputs = true;
    [Min(1)]
    public int outputMultiplier = 1;
    public List<CraftingItemAmount> inputs = new List<CraftingItemAmount>();
    public List<CraftingItemAmount> outputs = new List<CraftingItemAmount>();

    private void OnValidate()
    {
        if (inputs == null) inputs = new List<CraftingItemAmount>();
        if (outputs == null) outputs = new List<CraftingItemAmount>();
        successChance = Mathf.Clamp(successChance, 0f, 100f);
        outputMultiplier = Mathf.Max(1, outputMultiplier);

        foreach (CraftingItemAmount entry in inputs)
        {
            if (entry != null) entry.amount = Mathf.Max(1, entry.amount);
        }

        foreach (CraftingItemAmount entry in outputs)
        {
            if (entry != null) entry.amount = Mathf.Max(1, entry.amount);
        }
    }
}