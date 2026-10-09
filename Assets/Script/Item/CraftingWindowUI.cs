using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CraftingWindowUI : MonoBehaviour
{
    [Header("UI参照")]
    [SerializeField] private GameObject windowRoot;

    [Tooltip("手持ちアイテムのScroll ViewのContent")]
    [SerializeField] private Transform availableRoot;

    [Tooltip("選択した材料のScroll ViewのContent")]
    [SerializeField] private Transform selectedRoot;

    [Tooltip("クラフト結果表示のContent")]
    [SerializeField] private Transform outputRoot;

    [SerializeField] private Button craftButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject canvasToHideWhileOpen;

    private const string RecipeAssetFolder = "Assets/images/Crafting Recipe";
    private readonly List<CraftingRecipe> recipes = new List<CraftingRecipe>();

    [Header("アイテム表示設定")]
    [SerializeField] private Vector2 slotSize = new Vector2(120f, 120f);
    [SerializeField] private Sprite slotTexture;
    [SerializeField] private TMP_FontAsset generatedLabelFont;

    private readonly List<ItemData> selectedItems = new List<ItemData>();
    private readonly List<Behaviour> disabledGameplayComponents = new List<Behaviour>();

    private CraftingItemAmount selectedOutput;

    private bool isOpen;
    private bool isSubscribed;

    private PlayerControls controls;
    private InputAction craftingAction;

    private bool canvasWasActive;
    private bool canvasStateWasSaved;

    private void Awake()
    {
        LoadRecipes();

        controls = new PlayerControls();

        InputActionMap playerMap =
            controls.asset.FindActionMap("Player", true);

        craftingAction =
            playerMap.FindAction("crafting", false);

        if (craftingAction == null)
        {
            craftingAction =
                playerMap.AddAction("crafting", InputActionType.Button);

            craftingAction.AddBinding("<Keyboard>/c");
            craftingAction.AddBinding("<Gamepad>/leftShoulder");
        }

        craftingAction.performed += OnCraftingAction;

        SetupOutputRoot();
        SetupDropTarget();
        SetupCraftButton();
    }

    private void LoadRecipes()
    {
        recipes.Clear();

#if UNITY_EDITOR
        string[] recipeGuids =
            AssetDatabase.FindAssets(
                "t:CraftingRecipe",
                new[] { RecipeAssetFolder });

        foreach (string recipeGuid in recipeGuids)
        {
            string assetPath =
                AssetDatabase.GUIDToAssetPath(recipeGuid);

            CraftingRecipe recipe =
                AssetDatabase.LoadAssetAtPath<CraftingRecipe>(assetPath);

            if (recipe != null)
                recipes.Add(recipe);
        }
#endif
    }

    private void OnEnable()
    {
        CommonWindowEvents.WindowOpened += OnWindowOpened;

        craftingAction?.Enable();

        TrySubscribe();
    }

    private void Start()
    {
        TrySubscribe();
        SetOpen(false);
    }

    private void OnDisable()
    {
        CommonWindowEvents.WindowOpened -= OnWindowOpened;

        craftingAction?.Disable();

        if (isSubscribed && Inventory.Instance != null)
        {
            Inventory.Instance.InventoryUpdated -= Refresh;
        }

        isSubscribed = false;
    }

    private void OnCraftingAction(InputAction.CallbackContext context)
    {
        if (BSPDungeonMaster.IsPlayerInputLocked) return;
        SetOpen(!isOpen);
    }

    private void OnWindowOpened(MonoBehaviour openedWindow)
    {
        if (openedWindow != this)
            Close();
    }

    public void Open()
    {
        SetOpen(true);
    }

    public void Close()
    {
        SetOpen(false);
    }

    private void TrySubscribe()
    {
        if (isSubscribed || Inventory.Instance == null)
            return;

        Inventory.Instance.InventoryUpdated += Refresh;
        isSubscribed = true;
    }

    private void Refresh()
    {
        if (!isOpen)
            return;

        RebuildAvailableItems();
        RebuildSelectedItems();
        RebuildOutputs();
        UpdateRecipeStatus();
    }

    // =========================================================
    // UI設定
    // =========================================================

    private void SetupDropTarget()
    {
        SetupDropTarget(selectedRoot, false);
        SetupDropTarget(availableRoot, true);
    }

    private void SetupDropTarget(
        Transform root,
        bool returnsToInventory)
    {
        if (root == null)
            return;

        ScrollRect scrollRect =
            root.GetComponentInParent<ScrollRect>();

        SetupDropTargetOnObject(root, returnsToInventory);

        if (scrollRect != null && scrollRect.transform != root)
            SetupDropTargetOnObject(scrollRect.transform, returnsToInventory);
    }

    private void SetupDropTargetOnObject(
        Transform dropRoot,
        bool returnsToInventory)
    {
        Image image = dropRoot.GetComponent<Image>();
        if (image == null)
            image = dropRoot.gameObject.AddComponent<Image>();

        image.color =
            new Color(1f, 1f, 1f, 0.01f);
        image.raycastTarget = true;

        CraftDropTarget target =
            dropRoot.GetComponent<CraftDropTarget>();

        if (target == null)
            target =
                dropRoot.gameObject.AddComponent<CraftDropTarget>();

        target.Initialize(
            this,
            returnsToInventory);
    }

    private void SetupCraftButton()
    {
        if (craftButton == null)
            return;

        craftButton.onClick.RemoveListener(
            CraftSelectedItems);

        craftButton.onClick.AddListener(
            CraftSelectedItems);
    }

    // =========================================================
    // 所持アイテム
    // =========================================================

    private void RebuildAvailableItems()
    {
        if (availableRoot == null ||
            Inventory.Instance == null)
            return;

        ClearRoot(availableRoot);

        for (int i = 0;
             i < Inventory.Instance.items.Count;
             i++)
        {
            ItemStack stack =
                Inventory.Instance.items[i];

            if (stack == null ||
                stack.itemData == null ||
                stack.amount <= 0)
                continue;

            int availableAmount =
                stack.amount -
                CountSelected(stack.itemData);

            if (availableAmount > 0)
            {
                CreateItemSlot(
                    availableRoot,
                    stack.itemData,
                    availableAmount,
                    false);
            }
        }

        RefreshScrollLayout(availableRoot);
    }

    // =========================================================
    // 選択した材料
    // =========================================================

    private void RebuildSelectedItems()
    {
        if (selectedRoot == null)
            return;

        ClearRoot(selectedRoot);

        foreach (ItemData data in selectedItems)
        {
            CreateItemSlot(
                selectedRoot,
                data,
                1,
                true);
        }

        RefreshScrollLayout(selectedRoot);
    }

    private void RefreshScrollLayout(Transform root)
    {
        if (root == null)
            return;

        Canvas.ForceUpdateCanvases();

        RectTransform rect =
            root.GetComponent<RectTransform>();

        if (rect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    // =========================================================
    // アイテムスロット
    // =========================================================

    private void CreateItemSlot(
        Transform root,
        ItemData data,
        int amount,
        bool isSelectedItem)
    {
        GameObject slot =
            CreateUIObject(
                "CraftItem",
                root);

        RectTransform slotRect =
            slot.GetComponent<RectTransform>();

        slotRect.sizeDelta =
            slotSize;

        Image background =
            slot.AddComponent<Image>();

        background.sprite =
            slotTexture;

        background.type =
            slotTexture == null
                ? Image.Type.Simple
                : Image.Type.Sliced;

        background.color =
            new Color(
                0.12f,
                0.14f,
                0.19f,
                1f);

        if (data != null)
        {
            // アイコン
            GameObject iconObject =
                CreateUIObject(
                    "Icon",
                    slot.transform);

            RectTransform iconRect =
                iconObject.GetComponent<RectTransform>();

            iconRect.sizeDelta =
                new Vector2(76f, 76f);

            Image icon =
                iconObject.AddComponent<Image>();

            icon.sprite =
                data.icon;

            icon.preserveAspect =
                true;

            icon.enabled =
                data.icon != null;

            // 名前
            TextMeshProUGUI nameLabel =
                CreateLabel(
                    data.itemName,
                    slot.transform,
                    14,
                    TextAlignmentOptions.Center,
                    new Vector2(0f, -43f),
                    new Vector2(110f, 24f));

            nameLabel.color =
                Color.white;

            nameLabel.fontStyle =
                FontStyles.Bold;

            // 個数
            TextMeshProUGUI amountLabel =
                CreateLabel(
                    "x" + amount,
                    slot.transform,
                    16,
                    TextAlignmentOptions.BottomRight,
                    new Vector2(38f, 43f),
                    new Vector2(55f, 25f));

            amountLabel.color =
                Color.white;

            amountLabel.fontStyle =
                FontStyles.Bold;
        }

        Button button = slot.AddComponent<Button>();
        if (isSelectedItem)
        {
            button.onClick.AddListener(
                () => RemoveSelectedItem(data));
        }
        else
        {
            button.onClick.AddListener(
                () => AddSelectedItem(data));
        }

        CraftItemDragHandler dragHandler =
            slot.AddComponent<CraftItemDragHandler>();

        dragHandler.Initialize(
            this,
            data,
            isSelectedItem);

    }

    // =========================================================
    // 材料選択
    // =========================================================

    private void AddSelectedItem(ItemData data)
    {
        if (data == null ||
            Inventory.Instance == null)
            return;

        int selectedAmount = 0;

        foreach (ItemData selectedData in selectedItems)
        {
            if (selectedData == data)
                selectedAmount++;
        }

        if (selectedAmount >=
            GetInventoryAmount(data))
            return;

        selectedItems.Add(data);

        RebuildSelectedItems();
        RebuildAvailableItems();
        UpdateRecipeStatus();
    }

    private void RemoveSelectedItem(ItemData data)
    {
        int index =
            selectedItems.LastIndexOf(data);

        if (index < 0)
            return;

        selectedItems.RemoveAt(index);

        RebuildSelectedItems();
        RebuildAvailableItems();
        UpdateRecipeStatus();
    }

    private int GetInventoryAmount(ItemData data)
    {
        int amount = 0;

        if (Inventory.Instance == null)
            return amount;

        foreach (ItemStack stack
                 in Inventory.Instance.items)
        {
            if (stack != null &&
                stack.itemData == data)
            {
                amount += stack.amount;
            }
        }

        return amount;
    }

    // =========================================================
    // クラフト
    // =========================================================

    private void CraftSelectedItems()
    {
        List<CraftingRecipe> matchingRecipes = FindMatchingRecipes();

        if (matchingRecipes.Count == 0)
        {
            CraftingRecipe missingToolRecipe = FindInputMatchWithoutTool();
            if (missingToolRecipe != null)
            {
                CraftingProcessDefinition.HasRequiredTool(missingToolRecipe, out ItemData requiredTool);
                SetStatus("必要な道具がありません: " + requiredTool.itemName, Color.red);
                return;
            }

            SetStatus(
                "材料に一致するレシピがありません。",
                Color.black);

            return;
        }

        List<CraftingItemAmount> outputs =
            GetValidOutputs(matchingRecipes);

        if (outputs.Count == 0)
        {
            SetStatus(
                "この材料から生成できるアイテムがありません。",
                Color.red);

            return;
        }

        if (outputs.Count > 1 &&
            selectedOutput == null)
        {
            SetStatus(
                "生成アイテムを選択してください。",
                Color.black);

            return;
        }

        CraftingItemAmount outputToCraft =
            selectedOutput ?? outputs[0];

        CraftingRecipe recipe =
            FindRecipeForOutput(
                matchingRecipes,
                outputToCraft);

        if (recipe == null)
        {
            SetStatus(
                "選択した生成アイテムのレシピが見つかりません。",
                Color.red);

            return;
        }

        if (Inventory.Instance == null ||
            !Inventory.Instance.TryCraft(
                recipe,
                outputToCraft))
        {
            SetStatus(
                "空きスロットが足りません。",
                Color.red);

            return;
        }

        selectedItems.Clear();
        selectedOutput = null;

        string recipeName = string.IsNullOrWhiteSpace(recipe.displayName)
            ? recipe.name
            : recipe.displayName;
        SetStatus(recipeName + "：" + CraftingProcessDefinition.GetCompletionText(recipe.processingMethod), Color.green);

        RebuildAvailableItems();
        RebuildSelectedItems();
        RebuildOutputs();
        UpdateRecipeStatus();
    }

    private List<CraftingRecipe> FindMatchingRecipes()
    {
        List<CraftingRecipe> matches =
            new List<CraftingRecipe>();

        foreach (CraftingRecipe recipe in recipes)
        {
            if (RecipeInputsMatchSelectedItems(recipe) &&
                CraftingProcessDefinition.HasRequiredTool(recipe, out _))
                matches.Add(recipe);
        }

        return matches;
    }

    private CraftingRecipe FindInputMatchWithoutTool()
    {
        foreach (CraftingRecipe recipe in recipes)
        {
            if (RecipeInputsMatchSelectedItems(recipe) &&
                !CraftingProcessDefinition.HasRequiredTool(recipe, out _))
                return recipe;
        }
        return null;
    }

    private bool RecipeInputsMatchSelectedItems(
        CraftingRecipe recipe)
    {
        if (recipe == null ||
            recipe.inputs == null)
            return false;

        Dictionary<ItemData, int> required =
            new Dictionary<ItemData, int>();

        foreach (CraftingItemAmount entry
                 in recipe.inputs)
        {
            if (entry == null ||
                entry.itemData == null ||
                entry.amount <= 0)
                continue;

            if (!required.ContainsKey(
                    entry.itemData))
            {
                required[entry.itemData] = 0;
            }

            required[entry.itemData] +=
                entry.amount;
        }

        if (required.Count == 0)
            return false;

        if (required.Count !=
            CountSelectedTypes())
            return false;

        foreach (KeyValuePair<ItemData, int> entry
                 in required)
        {
            if (CountSelected(entry.Key)
                != entry.Value)
            {
                return false;
            }
        }

        return true;
    }

    private CraftingRecipe FindRecipeForOutput(
        List<CraftingRecipe> matchingRecipes,
        CraftingItemAmount output)
    {
        if (matchingRecipes == null ||
            output == null)
            return null;

        foreach (CraftingRecipe recipe
                 in matchingRecipes)
        {
            if (recipe == null ||
                recipe.outputs == null)
                continue;

            foreach (CraftingItemAmount recipeOutput
                     in recipe.outputs)
            {
                if (recipeOutput == output)
                    return recipe;

                if (recipeOutput != null &&
                    recipeOutput.itemData ==
                        output.itemData &&
                    recipeOutput.amount ==
                        output.amount)
                {
                    return recipe;
                }
            }
        }

        return null;
    }

    private int CountSelectedTypes()
    {
        List<ItemData> types =
            new List<ItemData>();

        foreach (ItemData data in selectedItems)
        {
            if (!types.Contains(data))
                types.Add(data);
        }

        return types.Count;
    }

    private int CountSelected(ItemData data)
    {
        int count = 0;

        foreach (ItemData selectedData
                 in selectedItems)
        {
            if (selectedData == data)
                count++;
        }

        return count;
    }

    // =========================================================
    // クラフト結果
    // =========================================================

    private List<CraftingItemAmount> GetValidOutputs(
        List<CraftingRecipe> matchingRecipes)
    {
        List<CraftingItemAmount> validOutputs =
            new List<CraftingItemAmount>();

        if (matchingRecipes == null)
            return validOutputs;

        foreach (CraftingRecipe recipe
                 in matchingRecipes)
        {
            if (recipe == null ||
                recipe.outputs == null)
                continue;

            foreach (CraftingItemAmount output
                     in recipe.outputs)
            {
                if (output == null ||
                    output.itemData == null ||
                    output.amount <= 0)
                    continue;

                if (!ContainsOutput(
                        validOutputs,
                        output))
                {
                    validOutputs.Add(output);
                }
            }
        }

        return validOutputs;
    }

    private bool ContainsOutput(
        List<CraftingItemAmount> outputs,
        CraftingItemAmount target)
    {
        if (outputs == null ||
            target == null)
            return false;

        foreach (CraftingItemAmount output
                 in outputs)
        {
            if (output == target)
                return true;

            if (output != null &&
                output.itemData ==
                    target.itemData &&
                output.amount ==
                    target.amount)
            {
                return true;
            }
        }

        return false;
    }

    private void SetupOutputRoot()
    {
        if (outputRoot == null)
            return;

        GridLayoutGroup grid =
            outputRoot.GetComponent<GridLayoutGroup>();

        if (grid == null)
            grid =
                outputRoot.gameObject.AddComponent<GridLayoutGroup>();

        grid.cellSize =
            new Vector2(120f, 64f);

        grid.spacing =
            new Vector2(8f, 0f);

        grid.constraint =
            GridLayoutGroup.Constraint.FixedRowCount;

        grid.constraintCount = 1;
    }

    private void RebuildOutputs()
    {
        if (outputRoot == null)
            return;

        ClearRoot(outputRoot);

        List<CraftingRecipe> matchingRecipes =
            FindMatchingRecipes();

        List<CraftingItemAmount> outputs =
            GetValidOutputs(matchingRecipes);

        // 選択肢が1つしかない場合は表示しない
        if (outputs.Count <= 1)
            return;

        foreach (CraftingItemAmount output
                 in outputs)
        {
            GameObject slot =
                CreateUIObject(
                    "CraftingOutput",
                    outputRoot);

            Image background =
                slot.AddComponent<Image>();

            background.color =
                ContainsOutput(
                    new List<CraftingItemAmount>
                    {
                        selectedOutput
                    },
                    output)
                    ? new Color(
                        0.2f,
                        0.55f,
                        0.4f,
                        1f)
                    : new Color(
                        0.12f,
                        0.14f,
                        0.19f,
                        1f);

            Button button =
                slot.AddComponent<Button>();

            CraftingItemAmount capturedOutput =
                output;

            button.onClick.AddListener(
                () => SelectOutput(capturedOutput));

            TextMeshProUGUI label =
                CreateLabel(
                    output.itemData.itemName +
                    " x" +
                    output.amount,
                    slot.transform,
                    14,
                    TextAlignmentOptions.Center,
                    Vector2.zero,
                    new Vector2(116f, 58f));

            label.color =
                Color.white;

            label.fontStyle =
                FontStyles.Bold;
        }
    }

    private void SelectOutput(
        CraftingItemAmount output)
    {
        selectedOutput = output;

        RebuildOutputs();
        UpdateRecipeStatus();
    }

    // =========================================================
    // レシピ状態
    // =========================================================

    private void UpdateRecipeStatus()
    {
        List<CraftingRecipe> matchingRecipes =
            FindMatchingRecipes();

        if (matchingRecipes.Count == 0)
        {
            CraftingRecipe missingToolRecipe = FindInputMatchWithoutTool();
            if (missingToolRecipe != null)
            {
                CraftingProcessDefinition.HasRequiredTool(missingToolRecipe, out ItemData requiredTool);
                SetStatus("必要な道具がありません: " + requiredTool.itemName, Color.red);
                selectedOutput = null;
                RebuildOutputs();
                return;
            }

            SetStatus(
                selectedItems.Count == 0
                    ? "アイテムをクリックまたはドラッグしてください。"
                    : "材料に一致するレシピがありません。",
                Color.black);

            selectedOutput = null;

            RebuildOutputs();

            return;
        }

        List<CraftingItemAmount> validOutputs =
            GetValidOutputs(matchingRecipes);

        if (validOutputs.Count == 0)
        {
            SetStatus(
                "この材料から生成できるアイテムがありません。",
                Color.red);

            selectedOutput = null;

            RebuildOutputs();

            return;
        }

        if (validOutputs.Count == 1)
        {
            selectedOutput =
                validOutputs[0];
        }
        else if (!ContainsOutput(
                     validOutputs,
                     selectedOutput))
        {
            selectedOutput = null;
        }

        RebuildOutputs();

        if (selectedOutput == null)
        {
            SetStatus(
                "生成アイテムを選択してください。",
                Color.black);

            return;
        }

        string outputText =
            selectedOutput.itemData.itemName +
            " x" +
            selectedOutput.amount;

        SetStatus(
            "完成: " + outputText,
            Color.black);
    }

    private void SetStatus(
        string message,
        Color color)
    {
        if (statusText == null)
            return;

        statusText.text =
            message;

        statusText.color =
            color;
    }

    // =========================================================
    // ウィンドウ
    // =========================================================

    private void SetOpen(bool open)
    {
        if (open)
            CommonWindowEvents.NotifyWindowOpened(this);

        isOpen = open;

        if (canvasToHideWhileOpen != null)
        {
            if (open)
            {
                canvasWasActive =
                    canvasToHideWhileOpen.activeSelf;

                canvasStateWasSaved = true;

                canvasToHideWhileOpen.SetActive(false);
            }
            else if (canvasStateWasSaved)
            {
                canvasToHideWhileOpen.SetActive(
                    canvasWasActive);

                canvasStateWasSaved = false;
            }
        }

        if (windowRoot != null)
            windowRoot.SetActive(open);

        if (open)
        {
            DisableGameplayComponents();

            EnsureEventSystem();

            SetupOutputRoot();

            RebuildAvailableItems();
            RebuildSelectedItems();
            RebuildOutputs();

            UpdateRecipeStatus();
        }
        else
        {
            selectedItems.Clear();
            selectedOutput = null;

            RestoreGameplayComponents();
        }
    }

    private void DisableGameplayComponents()
    {
        disabledGameplayComponents.Clear();

        if (Inventory.Instance == null)
            return;

        Transform player =
            Inventory.Instance.transform.root;

        AddAndDisable(
            player.GetComponentsInChildren
                <PL_movement>(true));

        AddAndDisable(
            player.GetComponentsInChildren
                <PlayerCombat>(true));

        AddAndDisable(
            player.GetComponentsInChildren
                <NewSwordAction>(true));
    }

    private void AddAndDisable<T>(
        T[] components)
        where T : Behaviour
    {
        foreach (T component in components)
        {
            if (component == null ||
                !component.enabled)
                continue;

            component.enabled = false;

            disabledGameplayComponents.Add(
                component);
        }
    }

    private void RestoreGameplayComponents()
    {
        foreach (Behaviour component
                 in disabledGameplayComponents)
        {
            if (component != null)
                component.enabled = true;
        }

        disabledGameplayComponents.Clear();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        Debug.LogWarning(
            "EventSystemが存在しません。UnityのシーンにEventSystemを追加してください。");
    }

    // =========================================================
    // 共通
    // =========================================================

    private static void ClearRoot(
        Transform root)
    {
        for (int i = root.childCount - 1;
             i >= 0;
             i--)
        {
            Destroy(
                root.GetChild(i).gameObject);
        }
    }

    private static GameObject CreateUIObject(
        string objectName,
        Transform parent)
    {
        GameObject created =
            new GameObject(
                objectName,
                typeof(RectTransform));

        created.transform.SetParent(
            parent,
            false);

        return created;
    }

    private TextMeshProUGUI CreateLabel(
        string text,
        Transform parent,
        int fontSize,
        TextAlignmentOptions alignment,
        Vector2 position,
        Vector2 size)
    {
        GameObject labelObject =
            CreateUIObject(
                "Label",
                parent);

        RectTransform rect =
            labelObject.GetComponent<RectTransform>();

        rect.anchorMin =
            new Vector2(0.5f, 0.5f);

        rect.anchorMax =
            new Vector2(0.5f, 0.5f);

        rect.anchoredPosition =
            position;

        rect.sizeDelta =
            size;

        TextMeshProUGUI label =
            labelObject.AddComponent
                <TextMeshProUGUI>();

        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.black;
        if (generatedLabelFont != null)
            label.font = generatedLabelFont;

        return label;
    }

    // =========================================================
    // ドラッグ
    // =========================================================

    private sealed class CraftItemDragHandler :
        MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        private CraftingWindowUI owner;
        private ItemData data;
        private bool isSelectedItem;

        public void Initialize(
            CraftingWindowUI windowOwner,
            ItemData itemData,
            bool selected)
        {
            owner = windowOwner;
            data = itemData;
            isSelectedItem = selected;
        }

        public void OnBeginDrag(
            PointerEventData eventData)
        {
            eventData.pointerDrag =
                gameObject;
        }

        public void OnDrag(
            PointerEventData eventData)
        {
            // ドラッグ処理は
            // CraftDropTarget側で判定
        }

        public void OnEndDrag(
            PointerEventData eventData)
        {
        }

        public ItemData GetItemData()
        {
            return data;
        }

        public bool IsSelectedItem()
        {
            return isSelectedItem;
        }
    }

    private sealed class CraftDropTarget :
        MonoBehaviour,
        IDropHandler
    {
        private CraftingWindowUI owner;
        private bool returnsToInventory;

        public void Initialize(
            CraftingWindowUI windowOwner,
            bool shouldReturnToInventory)
        {
            owner = windowOwner;
            returnsToInventory =
                shouldReturnToInventory;
        }

        public void OnDrop(
            PointerEventData eventData)
        {
            CraftItemDragHandler source =
                eventData.pointerDrag == null
                    ? null
                    : eventData.pointerDrag
                        .GetComponentInParent
                        <CraftItemDragHandler>();

            if (source == null || owner == null)
                return;

            if (returnsToInventory &&
                source.IsSelectedItem())
            {
                owner.RemoveSelectedItem(
                    source.GetItemData());
            }
            else if (!returnsToInventory &&
                     !source.IsSelectedItem())
            {
                owner.AddSelectedItem(
                    source.GetItemData());
            }
        }
    }
}