using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CraftingRecipeListWindowUI : MonoBehaviour
{
    [Header("UI参照")]
    [SerializeField] private GameObject windowRoot;
    [SerializeField] private Transform recipeContent;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject canvasToHideWhileOpen;

    [Header("レシピ")]
    [SerializeField] private List<CraftingRecipe> recipeAssets = new List<CraftingRecipe>();

    [Header("表示設定")]
    [SerializeField] private Vector2 itemSize = new Vector2(76f, 92f);
    [SerializeField] private float recipeSpacing = 12f;
    [SerializeField] private Sprite itemSlotTexture;
    [SerializeField] private TMP_FontAsset generatedLabelFont;

    private const string RecipeAssetFolder = "Assets/images/Crafting Recipe";
    private readonly List<CraftingRecipe> recipes = new List<CraftingRecipe>();
    private bool isOpen;
    private bool isSubscribed;
    private bool canvasWasActive;
    private bool canvasStateWasSaved;

    private void Awake()
    {
        LoadRecipes();
        SetupOpenButton();
        SetupCloseButton();
        TrySubscribe();
    }

    private void OnEnable()
    {
        CommonWindowEvents.WindowOpened += OnWindowOpened;
    }

    private void Start()
    {
        TrySubscribe();
        SetOpen(false);
    }

    private void OnDisable()
    {
        CommonWindowEvents.WindowOpened -= OnWindowOpened;
    }

    private void OnDestroy()
    {
        CommonWindowEvents.WindowOpened -= OnWindowOpened;
        if (isSubscribed && Inventory.Instance != null)
            Inventory.Instance.InventoryUpdated -= Refresh;
    }

    private void OnWindowOpened(MonoBehaviour openedWindow)
    {
        if (openedWindow != this)
            Close();
    }

    private void TrySubscribe()
    {
        if (isSubscribed || Inventory.Instance == null) return;
        Inventory.Instance.InventoryUpdated += Refresh;
        isSubscribed = true;
    }

    private void LoadRecipes()
    {
        recipes.Clear();
        AddRecipes(recipeAssets);

#if UNITY_EDITOR
        string[] recipeGuids = AssetDatabase.FindAssets("t:CraftingRecipe", new[] { RecipeAssetFolder });
        foreach (string recipeGuid in recipeGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(recipeGuid);
            AddRecipe(AssetDatabase.LoadAssetAtPath<CraftingRecipe>(assetPath));
        }
#else
        AddRecipes(Resources.LoadAll<CraftingRecipe>("Crafting Recipe"));
#endif
    }

    private void AddRecipes(IEnumerable<CraftingRecipe> source)
    {
        if (source == null) return;
        foreach (CraftingRecipe recipe in source)
            AddRecipe(recipe);
    }

    private void AddRecipe(CraftingRecipe recipe)
    {
        if (recipe != null && !recipes.Contains(recipe))
            recipes.Add(recipe);
    }

    private void Refresh()
    {
        TrySubscribe();
        if (isOpen)
            RebuildRecipeList();
    }

    public void Open()
    {
        SetOpen(true);
    }

    public void Close()
    {
        SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        if (open)
        {
            CommonWindowEvents.NotifyWindowOpened(this);
        }

        isOpen = open;

        if (canvasToHideWhileOpen != null)
        {
            if (open)
            {
                canvasWasActive = canvasToHideWhileOpen.activeSelf;
                canvasStateWasSaved = true;
                canvasToHideWhileOpen.SetActive(false);
            }
            else if (canvasStateWasSaved)
            {
                canvasToHideWhileOpen.SetActive(canvasWasActive);
                canvasStateWasSaved = false;
            }
        }

        if (windowRoot != null)
            windowRoot.SetActive(open);

        if (open)
            RebuildRecipeList();
    }

    private void RebuildRecipeList()
    {
        if (recipeContent == null) return;

        ClearContent();
        int visibleRecipeCount = 0;
        foreach (CraftingRecipe recipe in recipes)
        {
            if (!RecipeContainsOwnedInput(recipe)) continue;
            CreateRecipeRow(recipe);
            visibleRecipeCount++;
        }

        if (statusText != null)
        {
            statusText.gameObject.SetActive(visibleRecipeCount == 0);
            statusText.text = "所持アイテムを使うレシピがありません。";
        }
    }

    private bool RecipeContainsOwnedInput(CraftingRecipe recipe)
    {
        if (recipe == null || recipe.inputs == null) return false;

        foreach (CraftingItemAmount input in recipe.inputs)
        {
            if (input != null && input.itemData != null && GetOwnedAmount(input.itemData) > 0)
                return true;
        }

        return false;
    }

    private int GetOwnedAmount(ItemData target)
    {
        if (target == null || Inventory.Instance == null || Inventory.Instance.items == null)
            return 0;

        int ownedAmount = 0;
        foreach (ItemStack stack in Inventory.Instance.items)
        {
            if (stack != null && stack.itemData == target)
                ownedAmount += stack.amount;
        }

        return ownedAmount;
    }

    private void CreateRecipeRow(CraftingRecipe recipe)
    {
        GameObject row = CreateUIObject("RecipeRow", recipeContent);
        Image rowImage = row.AddComponent<Image>();
        rowImage.color = new Color(0.10f, 0.12f, 0.17f, 1f);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.spacing = recipeSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        int itemCount = CountValidItems(recipe.inputs) + CountValidItems(recipe.outputs);
        int separatorCount = CountValidItems(recipe.inputs) + CountValidItems(recipe.outputs) - 1;
        float rowWidth = Mathf.Max(420f, itemCount * itemSize.x + separatorCount * 28f + 190f);
        LayoutElement rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.minHeight = itemSize.y + 16f;
        rowLayout.preferredHeight = itemSize.y + 16f;
        rowLayout.preferredWidth = rowWidth;

        string recipeLabel = string.IsNullOrWhiteSpace(recipe.displayName)
            ? recipe.name
            : recipe.displayName;
        recipeLabel += "\n方法: " + CraftingProcessDefinition.GetMethodName(recipe.processingMethod);

        TextMeshProUGUI title = CreateLabel(
            recipeLabel,
            row.transform,
            14,
            TextAlignmentOptions.Left,
            Vector2.zero,
            new Vector2(170f, itemSize.y));
        title.textWrappingMode = TextWrappingModes.Normal;
        LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
        titleLayout.minWidth = 170f;
        titleLayout.preferredWidth = 170f;
        titleLayout.minHeight = itemSize.y;
        titleLayout.preferredHeight = itemSize.y;

        bool first = true;
        AddItemsToRow(row.transform, recipe.inputs, ref first, 1);
        AddTextSeparator(row.transform, "=");
        first = true;
        AddItemsToRow(row.transform, recipe.outputs, ref first, recipe.outputMultiplier);
    }

    private void AddItemsToRow(Transform row, List<CraftingItemAmount> itemAmounts, ref bool first, int amountMultiplier)
    {
        if (itemAmounts == null) return;

        foreach (CraftingItemAmount itemAmount in itemAmounts)
        {
            if (itemAmount == null || itemAmount.itemData == null || itemAmount.amount <= 0)
                continue;

            if (!first)
                AddTextSeparator(row, "+");

            CreateItemVisual(row, itemAmount, amountMultiplier);
            first = false;
        }
    }

    private void CreateItemVisual(Transform parent, CraftingItemAmount itemAmount, int amountMultiplier)
    {
        GameObject itemObject = CreateUIObject("RecipeItem", parent);
        LayoutElement layout = itemObject.AddComponent<LayoutElement>();
        layout.minWidth = itemSize.x;
        layout.preferredWidth = itemSize.x;
        layout.minHeight = itemSize.y;
        layout.preferredHeight = itemSize.y;

        Image background = itemObject.AddComponent<Image>();
        background.sprite = itemSlotTexture;
        background.type = itemSlotTexture == null ? Image.Type.Simple : Image.Type.Sliced;
        background.color = new Color(0.16f, 0.18f, 0.24f, 1f);

        bool owned = GetOwnedAmount(itemAmount.itemData) > 0;
        if (owned && itemAmount.itemData.icon != null)
        {
            GameObject iconObject = CreateUIObject("Icon", itemObject.transform);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.anchoredPosition = new Vector2(0f, -35f);
            iconRect.sizeDelta = new Vector2(56f, 56f);
            Image icon = iconObject.AddComponent<Image>();
            icon.sprite = itemAmount.itemData.icon;
            icon.preserveAspect = true;
        }
        else if (!owned)
        {
            CreateLabel("?", itemObject.transform, 45, TextAlignmentOptions.Center, new Vector2(0f, 10f), new Vector2(itemSize.x, 48f));
        }

        CreateLabel("x" + itemAmount.amount * Mathf.Max(1, amountMultiplier), itemObject.transform, 24, TextAlignmentOptions.Bottom, new Vector2(0f, -24f), new Vector2(itemSize.x - 8f, 24f));
    }

    private static int CountValidItems(List<CraftingItemAmount> itemAmounts)
    {
        if (itemAmounts == null) return 0;

        int count = 0;
        foreach (CraftingItemAmount itemAmount in itemAmounts)
        {
            if (itemAmount != null && itemAmount.itemData != null && itemAmount.amount > 0)
                count++;
        }

        return count;
    }

    private void AddTextSeparator(Transform parent, string text)
    {
        CreateLabel(text, parent, 24, TextAlignmentOptions.Center, Vector2.zero, new Vector2(24f, itemSize.y));
    }

    private void SetupCloseButton()
    {
        if (closeButton == null) return;
        closeButton.onClick.RemoveListener(Close);
        closeButton.onClick.AddListener(Close);
    }

    private void SetupOpenButton()
    {
        if (openButton == null) return;
        openButton.onClick.RemoveListener(Open);
        openButton.onClick.AddListener(Open);
    }

    private void ClearContent()
    {
        for (int i = recipeContent.childCount - 1; i >= 0; i--)
            Destroy(recipeContent.GetChild(i).gameObject);
    }

    private static GameObject CreateUIObject(string objectName, Transform parent)
    {
        GameObject created = new GameObject(objectName, typeof(RectTransform));
        created.transform.SetParent(parent, false);
        return created;
    }

    private TextMeshProUGUI CreateLabel(string text, Transform parent, int fontSize, TextAlignmentOptions alignment, Vector2 position, Vector2 size)
    {
        GameObject labelObject = CreateUIObject("Label", parent);
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        if (generatedLabelFont != null)
            label.font = generatedLabelFont;
        return label;
    }
}
