using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class InventoryWindowUI : CommonWindowUI
{
    [Header("UI参照")]
    [SerializeField] private Transform gridRoot;
    [SerializeField] private GameObject descriptionObject;

    [Header("表示設定")]
    [SerializeField] private int columns = 5;
    [SerializeField] private Vector2 slotSize = new Vector2(120f, 120f);
    [SerializeField] private Vector2 panelSize = new Vector2(700f, 560f);
    [SerializeField] private Vector2 gridSize = new Vector2(640f, 400f);
    [SerializeField] private Vector2 gridSpacing = new Vector2(8f, 8f);
    [SerializeField] private Sprite slotTexture;
    [SerializeField] private Sprite windowBackgroundTexture;

    [Header("右手装備表示")]
    [SerializeField] private string rightHandMarker = "右手";
    [SerializeField] private Color rightHandMarkerColor = new Color(1f, 0.35f, 0.25f, 1f);
    [SerializeField] private Vector2 rightHandMarkerPosition = new Vector2(0f, 48f);
    [SerializeField] private Vector2 rightHandMarkerSize = new Vector2(110f, 22f);

    [Header("耐久度ゲージの色")]
    [SerializeField] private Color lowDurabilityColor = new Color(0.76f, 0.39f, 0.41f);
    [SerializeField] private Color mediumDurabilityColor = new Color(0.79f, 0.69f, 0.39f);
    [SerializeField] private Color highDurabilityColor = new Color(0.39f, 0.66f, 0.45f);

    [Header("個数表示")]
    [SerializeField] private int countFontSize = 16;
    [SerializeField] private Vector2 countPosition = new Vector2(43f, 43f);
    [SerializeField] private Vector2 countSize = new Vector2(55f, 25f);
    [SerializeField] private TMP_FontAsset generatedLabelFont;

    private static InventoryWindowUI instance;
    public static InventoryWindowUI Instance => instance;

    private readonly List<Behaviour> disabledGameplayComponents =
        new List<Behaviour>();

    private bool isSubscribed;
    private PlayerControls controls;
    private InputAction inventoryAction;
    private readonly List<TextMeshProUGUI> rightHandMarkers =
        new List<TextMeshProUGUI>();
    private GameObject durabilityGaugeObject;
    private Image durabilityGaugeImage;
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        controls = new PlayerControls();
        InputActionMap playerMap = controls.asset.FindActionMap("Player", true);
        inventoryAction = playerMap.FindAction("inventory", false);

        if (inventoryAction == null)
        {
            inventoryAction = playerMap.AddAction("inventory", InputActionType.Button);
            inventoryAction.AddBinding("<Keyboard>/i");
            inventoryAction.AddBinding("<Gamepad>/buttonEast");
        }

        inventoryAction.performed += OnInventoryAction;

        ValidateReferences();
        SetupGrid();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        inventoryAction?.Enable();
        TrySubscribe();
    }

    private void Start()
    {
        TrySubscribe();
        SetOpen(false);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        inventoryAction?.Disable();

        if (isSubscribed && Inventory.Instance != null)
        {
            Inventory.Instance.InventoryUpdated -= Refresh;
        }

        isSubscribed = false;
    }

    private void Update()
    {
        if (IsOpen)
        {
            RefreshSelection();
        }
    }

    private void OnInventoryAction(InputAction.CallbackContext context)
    {
        if (BSPDungeonMaster.IsPlayerInputLocked) return;
        SetOpen(!IsOpen);
    }

    // =========================================================
    // UI設定
    // =========================================================

    private void ValidateReferences()
    {
        if (windowRoot == null)
        {
            Debug.LogError(
                "InventoryWindowUI: Window Root が設定されていません。",
                this
            );
        }

        if (gridRoot == null)
        {
            Debug.LogError(
                "InventoryWindowUI: Grid Root が設定されていません。",
                this
            );
        }
    }

    private void SetupGrid()
    {
        if (gridRoot == null)
            return;

        GridLayoutGroup grid =
            gridRoot.GetComponent<GridLayoutGroup>();

        if (grid == null)
        {
            grid = gridRoot.gameObject.AddComponent<GridLayoutGroup>();
        }

        grid.cellSize = slotSize;
        grid.spacing = gridSpacing;

        grid.constraint =
            GridLayoutGroup.Constraint.FixedColumnCount;

        grid.constraintCount = Mathf.Max(1, columns);

        grid.childAlignment = TextAnchor.UpperCenter;

        RectTransform gridRect =
            gridRoot.GetComponent<RectTransform>();

        if (gridRect != null)
        {
            gridRect.sizeDelta = gridSize;
        }

        RectTransform windowRect =
            windowRoot != null
                ? windowRoot.GetComponent<RectTransform>()
                : null;

        if (windowRect != null)
        {
            windowRect.sizeDelta = panelSize;

            Image windowBackground = windowRoot.GetComponent<Image>();
            if (windowBackground != null && windowBackgroundTexture != null)
            {
                windowBackground.sprite = windowBackgroundTexture;
                windowBackground.type = Image.Type.Sliced;
            }
        }
    }

    // =========================================================
    // Inventory
    // =========================================================

    private void Refresh()
    {
        if (!IsOpen)
            return;

        RebuildGrid();
        RefreshDescription();
    }

    private void TrySubscribe()
    {
        if (isSubscribed)
            return;

        if (Inventory.Instance == null)
            return;

        Inventory.Instance.InventoryUpdated += Refresh;
        isSubscribed = true;
    }

    private void RebuildGrid()
    {
        if (gridRoot == null)
            return;

        if (Inventory.Instance == null)
            return;

        rightHandMarkers.Clear();
        durabilityGaugeObject = null;
        durabilityGaugeImage = null;

        // 既存のスロットを削除
        for (int i = gridRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(gridRoot.GetChild(i).gameObject);
        }

        // 所持アイテムを表示
        for (int i = 0; i < Inventory.Instance.items.Count; i++)
        {
            CreateSlot(
                i,
                Inventory.Instance.items[i]
            );
        }

        if (gridRoot.childCount > 0)
        {
            durabilityGaugeObject = CreateUIObject("DurabilityGauge", gridRoot.GetChild(0));
            RectTransform gaugeRect = durabilityGaugeObject.GetComponent<RectTransform>();
            gaugeRect.anchorMin = new Vector2(0.1f, 0.08f);
            gaugeRect.anchorMax = new Vector2(0.9f, 0.08f);
            gaugeRect.pivot = new Vector2(0.5f, 0f);
            gaugeRect.sizeDelta = new Vector2(0f, 7f);
            durabilityGaugeImage = durabilityGaugeObject.AddComponent<Image>();
            durabilityGaugeImage.type = Image.Type.Filled;
            durabilityGaugeImage.fillMethod = Image.FillMethod.Horizontal;
            durabilityGaugeImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            RefreshDurabilityGauge();
        }
    }

    // =========================================================
    // Slot
    // =========================================================

    private void CreateSlot(int index, ItemStack stack)
    {
        GameObject slot =
            CreateUIObject(
                "Slot_" + index,
                gridRoot
            );

        RectTransform slotRect =
            slot.GetComponent<RectTransform>();

        slotRect.sizeDelta = slotSize;

        // 背景
        Image background =
            slot.AddComponent<Image>();

        background.sprite = slotTexture;
        background.type = slotTexture != null
            ? Image.Type.Sliced
            : Image.Type.Simple;

        background.color =
            new Color(
                0.12f,
                0.14f,
                0.19f,
                1f
            );

        // ボタン
        Button button =
            slot.AddComponent<Button>();

        int capturedIndex = index;

        button.onClick.AddListener(() =>
        {
            if (Inventory.Instance == null)
                return;

            Inventory.Instance.selectedSlot =
                capturedIndex;

            RefreshSelection();
            RefreshDescription();
        });

        SlotDragHandler dragHandler =
            slot.AddComponent<SlotDragHandler>();
        dragHandler.Initialize(this, capturedIndex);

        TextMeshProUGUI rightHandLabel =
            CreateLabel(
                rightHandMarker,
                slot.transform,
                14,
                TextAlignmentOptions.Center,
                rightHandMarkerPosition,
                rightHandMarkerSize
            );
        rightHandLabel.color = rightHandMarkerColor;
        rightHandLabel.fontStyle = FontStyles.Bold;
        rightHandMarkers.Add(rightHandLabel);

        // アイテムデータがない場合
        if (stack == null ||
            stack.itemData == null)
        {
            return;
        }

        // =====================================================
        // アイコン
        // =====================================================

        GameObject iconObject =
            CreateUIObject(
                "Icon",
                slot.transform
            );

        RectTransform iconRect =
            iconObject.GetComponent<RectTransform>();

        iconRect.anchorMin =
            new Vector2(0.5f, 0.5f);

        iconRect.anchorMax =
            new Vector2(0.5f, 0.5f);

        iconRect.anchoredPosition =
            Vector2.zero;

        iconRect.sizeDelta =
            new Vector2(76f, 76f);

        Image icon =
            iconObject.AddComponent<Image>();

        icon.sprite =
            stack.itemData.icon;

        icon.preserveAspect = true;

        icon.enabled =
            icon.sprite != null;

        // =====================================================
        // アイテム名
        // =====================================================

        TextMeshProUGUI name =
            CreateLabel(
                stack.itemData.itemName,
                slot.transform,
                14,
                TextAlignmentOptions.Center,
                new Vector2(0f, -43f),
                new Vector2(110f, 24f)
            );

        name.textWrappingMode =
            TextWrappingModes.Normal;

        // =====================================================
        // 個数
        // =====================================================

        CreateLabel(
            "x" + stack.amount,
            slot.transform,
            countFontSize,
            TextAlignmentOptions.BottomRight,
            countPosition,
            countSize
        );
    }

    // =========================================================
    // 選択状態
    // =========================================================

    private void RefreshSelection()
    {
        if (gridRoot == null)
            return;

        if (Inventory.Instance == null)
            return;

        int selectedIndex =
            Inventory.Instance.selectedSlot;

        for (int i = 0; i < gridRoot.childCount; i++)
        {
            GameObject slot =
                gridRoot.GetChild(i).gameObject;

            Image background =
                slot.GetComponent<Image>();

            if (background == null)
                continue;

            if (i < rightHandMarkers.Count)
            {
                bool isRightHandSlot =
                    EquipmentManager.Instance != null &&
                    EquipmentManager.Instance.currentRightSlot == i;
                rightHandMarkers[i].enabled = isRightHandSlot;
            }

            if (i == selectedIndex)
            {
                background.color =
                    new Color(
                        0.3f,
                        0.4f,
                        0.65f,
                        1f
                    );
            }
            else
            {
                background.color =
                    new Color(
                        0.12f,
                        0.14f,
                        0.19f,
                        1f
                    );
            }
        }

        RefreshDurabilityGauge();
    }

    private void RefreshDurabilityGauge()
    {
        if (durabilityGaugeObject == null || durabilityGaugeImage == null || Inventory.Instance == null)
            return;

        int selectedIndex = Inventory.Instance.selectedSlot;
        bool validSelection =
            selectedIndex >= 0 &&
            selectedIndex < Inventory.Instance.items.Count &&
            selectedIndex < gridRoot.childCount;
        ItemStack selectedStack = validSelection ? Inventory.Instance.items[selectedIndex] : null;
        bool show = selectedStack != null && selectedStack.itemData != null && selectedStack.itemData.HasDurability;

        durabilityGaugeObject.SetActive(show);
        if (!show)
            return;

        durabilityGaugeObject.transform.SetParent(gridRoot.GetChild(selectedIndex), false);
        int maxDurability = selectedStack.itemData.DurabilityLimit;
        float ratio = Mathf.Clamp01((float)selectedStack.durability / maxDurability);
        durabilityGaugeImage.fillAmount = ratio;
        durabilityGaugeImage.color = ratio < 0.5f
            ? Color.Lerp(lowDurabilityColor, mediumDurabilityColor, ratio * 2f)
            : Color.Lerp(mediumDurabilityColor, highDurabilityColor, (ratio - 0.5f) * 2f);
    }

    private void RefreshDescription()
    {
        if (Inventory.Instance == null)
            return;

        ItemStack selectedStack = null;
        int selectedIndex = Inventory.Instance.selectedSlot;

        if (selectedIndex >= 0 && selectedIndex < Inventory.Instance.items.Count)
        {
            selectedStack = Inventory.Instance.items[selectedIndex];
        }

        string description =
            selectedStack != null && selectedStack.itemData != null
                ? selectedStack.itemData.description
                : "";

        SetDescriptionText(descriptionObject, description);
    }

    private static void SetDescriptionText(GameObject descriptionObject, string text)
    {
        if (descriptionObject == null)
            return;

        TextMeshProUGUI descriptionText =
            descriptionObject.GetComponent<TextMeshProUGUI>();

        if (descriptionText == null)
            descriptionText =
                descriptionObject.GetComponentInChildren<TextMeshProUGUI>(true);

        if (descriptionText != null)
            descriptionText.text = text;
    }

    // =========================================================
    // Open / Close
    // =========================================================

    protected override void OnWindowOpened()
    {
        DisableGameplayComponents();
        SetupGrid();
        RebuildGrid();
        EnsureEventSystem();
        RefreshSelection();
        RefreshDescription();
    }

    protected override void OnWindowClosed()
    {
        RestoreGameplayComponents();
    }

    // =========================================================
    // Player停止
    // =========================================================

    private void DisableGameplayComponents()
    {
        disabledGameplayComponents.Clear();

        if (Inventory.Instance == null)
            return;

        Transform player =
            Inventory.Instance.transform.root;

        AddAndDisable(
            player.GetComponentsInChildren<PL_movement>(true)
        );

        AddAndDisable(
            player.GetComponentsInChildren<PlayerCombat>(true)
        );

        AddAndDisable(
            player.GetComponentsInChildren<NewSwordAction>(true)
        );
    }

    private void AddAndDisable<T>(T[] components)
        where T : Behaviour
    {
        foreach (T component in components)
        {
            if (component == null)
                continue;

            if (!component.enabled)
                continue;

            component.enabled = false;

            disabledGameplayComponents.Add(
                component
            );
        }
    }

    private void RestoreGameplayComponents()
    {
        foreach (
            Behaviour component
            in disabledGameplayComponents
        )
        {
            if (component != null)
            {
                component.enabled = true;
            }
        }

        disabledGameplayComponents.Clear();
    }

    // =========================================================
    // EventSystem
    // =========================================================

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        GameObject eventSystem =
            new GameObject(
                "InventoryEventSystem"
            );

        eventSystem.AddComponent<EventSystem>();

        eventSystem.AddComponent<
            InputSystemUIInputModule
        >();
    }

    // =========================================================
    // UI生成補助
    // =========================================================

    private static GameObject CreateUIObject(
        string objectName,
        Transform parent)
    {
        GameObject created =
            new GameObject(
                objectName,
                typeof(RectTransform)
            );

        created.transform.SetParent(
            parent,
            false
        );

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
                parent
            );

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
            labelObject.AddComponent<
                TextMeshProUGUI
            >();

        label.text = text;

        label.fontSize =
            fontSize;

        label.alignment =
            alignment;

        label.color =
            Color.white;
        if (generatedLabelFont != null)
            label.font = generatedLabelFont;

        return label;
    }

    private sealed class SlotDragHandler : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IDropHandler
    {
        private InventoryWindowUI owner;
        private int slotIndex;

        public void Initialize(InventoryWindowUI slotOwner, int index)
        {
            owner = slotOwner;
            slotIndex = index;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            eventData.pointerDrag = gameObject;
        }

        public void OnDrag(PointerEventData eventData)
        {
        }

        public void OnEndDrag(PointerEventData eventData)
        {
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (owner == null || Inventory.Instance == null)
                return;

            SlotDragHandler source =
                eventData.pointerDrag != null
                    ? eventData.pointerDrag.GetComponent<SlotDragHandler>()
                    : null;

            if (source != null)
            {
                Inventory.Instance.MoveItem(source.slotIndex, slotIndex);
            }
        }
    }
}
