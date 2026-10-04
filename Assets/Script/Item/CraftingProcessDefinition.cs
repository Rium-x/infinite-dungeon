using System;
using UnityEngine;

[CreateAssetMenu(fileName = "NewCraftingProcess", menuName = "Inventory/Crafting Process Definition")]
public class CraftingProcessDefinition : ScriptableObject
{
    private static CraftingProcessDefinition[] cachedDefinitions;

    public CraftingProcessMethod method;
    [Tooltip("未設定なら標準の加工方法名を表示します。")]
    public string displayName;
    [Tooltip("この加工方法に必要な道具。所持しているだけでよく、加工では消費しません。CraftingRecipeには設定しません。")]
    public ItemData requiredTool;

    public static CraftingProcessDefinition Find(CraftingProcessMethod method)
    {
        if (cachedDefinitions == null)
            cachedDefinitions = Resources.LoadAll<CraftingProcessDefinition>("Crafting Processes");

        foreach (CraftingProcessDefinition definition in cachedDefinitions)
            if (definition != null && definition.method == method)
                return definition;
        return null;
    }

    public static string GetMethodName(CraftingProcessMethod method)
    {
        CraftingProcessDefinition definition = Find(method);
        if (definition != null && !string.IsNullOrWhiteSpace(definition.displayName))
            return definition.displayName;

        switch (method)
        {
            case CraftingProcessMethod.Crafting: return "加工";
            case CraftingProcessMethod.Baking: return "焼く";
            case CraftingProcessMethod.Dismantling: return "分解";
            case CraftingProcessMethod.Alchemy: return "調合";
            case CraftingProcessMethod.Smelting: return "精錬";
            case CraftingProcessMethod.Cooking: return "調理";
            case CraftingProcessMethod.Carpentry: return "木工";
            default: return "その他";
        }
    }

    public static string GetCompletionText(CraftingProcessMethod method)
    {
        switch (method)
        {
            case CraftingProcessMethod.Baking: return "焼きました。";
            case CraftingProcessMethod.Dismantling: return "分解しました。";
            case CraftingProcessMethod.Alchemy: return "調合しました。";
            case CraftingProcessMethod.Smelting: return "精錬しました。";
            case CraftingProcessMethod.Cooking: return "調理しました。";
            case CraftingProcessMethod.Carpentry: return "木工しました。";
            default: return "加工しました。";
        }
    }

    public static bool HasRequiredTool(CraftingRecipe recipe, out ItemData requiredTool)
    {
        requiredTool = null;
        if (recipe == null) return false;

        CraftingProcessDefinition definition = Find(recipe.processingMethod);
        if (definition == null || definition.requiredTool == null)
            return true;

        requiredTool = definition.requiredTool;
        return Inventory.Instance != null && Inventory.Instance.HasItem(requiredTool);
    }

    public static CraftingProcessMethod ParseMethod(string value)
    {
        if (Enum.TryParse(value, true, out CraftingProcessMethod method))
            return method;

        switch ((value ?? string.Empty).Trim())
        {
            case "焼く": return CraftingProcessMethod.Baking;
            case "分解": return CraftingProcessMethod.Dismantling;
            case "調合": return CraftingProcessMethod.Alchemy;
            case "精錬": return CraftingProcessMethod.Smelting;
            case "調理": return CraftingProcessMethod.Cooking;
            case "木工": return CraftingProcessMethod.Carpentry;
            case "加工": return CraftingProcessMethod.Crafting;
            default: return CraftingProcessMethod.Crafting;
        }
    }
}