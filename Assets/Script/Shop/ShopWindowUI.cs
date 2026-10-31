using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Controls the shop window placed in the active scene.</summary>
public sealed class ShopWindowUI : CommonWindowUI
{
    private static ShopWindowUI instance;

    [Header("シーン上のウィンドウ")]
    [SerializeField] private TMP_FontAsset textFont;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI balanceText;
    [SerializeField] private TextMeshProUGUI modeText;
    [SerializeField] private TextMeshProUGUI detailText;
    [SerializeField] private TextMeshProUGUI portraitPlaceholderText;
    [SerializeField] private Image portraitImage;
    [SerializeField] private Image portraitBackground;
    [SerializeField] private Transform itemListRoot;
    [SerializeField] private Button rowButtonTemplate;
    [SerializeField] private Button buyButton;
    [SerializeField] private Button sellButton;
    [SerializeField] private Button confirmButton;

    private ShopEntity shop;
    private bool buying = true;
    private int selectedIndex = -1;
    private readonly List<GameObject> generatedRows = new List<GameObject>();
    private readonly List<Behaviour> disabledGameplay = new List<Behaviour>();

    public static bool IsShopOpen => instance != null && instance.IsOpen;

    public void ConfigureSceneWindow(
        GameObject root,
        TMP_FontAsset font,
        TextMeshProUGUI title,
        TextMeshProUGUI balance,
        TextMeshProUGUI mode,
        TextMeshProUGUI details,
        TextMeshProUGUI portraitPlaceholder,
        Image portrait,
        Image background,
        Transform listRoot,
        Button rowTemplate,
        Button buy,
        Button sell,
        Button confirm)
    {
        windowRoot = root;
        canvasToHideWhileOpen = null;
        textFont = font;
        titleText = title;
        balanceText = balance;
        modeText = mode;
        detailText = details;
        portraitPlaceholderText = portraitPlaceholder;
        portraitImage = portrait;
        portraitBackground = background;
        itemListRoot = listRoot;
        rowButtonTemplate = rowTemplate;
        buyButton = buy;
        sellButton = sell;
        confirmButton = confirm;
        ApplyFont();
        ValidateReferences();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogError("ShopWindowUI はシーンに1つだけ配置してください。", this);
            enabled = false;
            return;
        }

        instance = this;
        if (buyButton != null) buyButton.onClick.AddListener(() => SetMode(true));
        if (sellButton != null) sellButton.onClick.AddListener(() => SetMode(false));
        if (confirmButton != null) confirmButton.onClick.AddListener(ConfirmTrade);
        if (rowButtonTemplate != null) rowButtonTemplate.gameObject.SetActive(false);
        ApplyFont();
        ValidateReferences();
    }

    private void Start()
    {
        if (windowRoot != null) windowRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void OnValidate()
    {
        ApplyFont();
    }

    public static void OpenShop(ShopEntity entity)
    {
        if (entity == null) return;
        if (instance == null) instance = FindAnyObjectByType<ShopWindowUI>(FindObjectsInactive.Include);
        if (instance == null)
        {
            Debug.LogError("シーンに ShopWindowUI がありません。ShopWindowSceneBuilder でウィンドウを配置してください。");
            return;
        }

        instance.shop = entity;
        instance.buying = true;
        instance.selectedIndex = -1;
        instance.SetOpen(true);
        instance.Refresh();
    }

    private void ValidateReferences()
    {
        if (windowRoot == null || titleText == null || balanceText == null || modeText == null ||
            detailText == null || portraitImage == null || portraitBackground == null ||
            itemListRoot == null || rowButtonTemplate == null || buyButton == null ||
            sellButton == null || confirmButton == null)
        {
            Debug.LogError("ShopWindowUI の参照が不足しています。シーン上の Shop Window を選択し、全項目を割り当ててください。", this);
        }
    }

    private void ApplyFont()
    {
        if (textFont == null || windowRoot == null) return;
        foreach (TMP_Text text in windowRoot.GetComponentsInChildren<TMP_Text>(true))
            text.font = textFont;
        if (rowButtonTemplate != null)
            foreach (TMP_Text text in rowButtonTemplate.GetComponentsInChildren<TMP_Text>(true))
                text.font = textFont;
    }

    private void SetMode(bool buy)
    {
        if (buying == buy) return;
        buying = buy;
        selectedIndex = -1;
        Refresh();
    }

    private void Refresh()
    {
        if (shop == null || windowRoot == null || itemListRoot == null) return;
        titleText.text = shop.ShopName;
        balanceText.text = $"G: {(Inventory.Instance != null ? Inventory.Instance.gBalance : 0)}";
        modeText.text = buying ? "買えるもの" : "売れるもの";
        portraitPlaceholderText.gameObject.SetActive(shop.ShopkeeperPortrait == null);
        portraitImage.sprite = shop.ShopkeeperPortrait;
        portraitImage.color = shop.ShopkeeperPortrait != null ? Color.white : new Color(.55f, .48f, .38f, 1f);
        portraitBackground.sprite = shop.ShopkeeperBackground;
        portraitBackground.color = shop.ShopkeeperBackground != null ? Color.white : new Color(.28f, .23f, .17f, 1f);

        ClearGeneratedRows();
        List<ShopOffer> available = GetAvailableOffers();
        for (int i = 0; i < available.Count; i++)
        {
            ShopOffer offer = available[i];
            int index = i;
            int price = buying ? offer.buyPrice : offer.sellPrice;
            string amount = buying ? string.Empty : $"  ×{Inventory.Instance.GetItemAmount(offer.item)}";
            Button row = Instantiate(rowButtonTemplate, itemListRoot);
            row.gameObject.name = "ShopItem_" + i;
            row.gameObject.SetActive(true);
            TMP_Text label = row.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = $"{offer.item.itemName}{amount}    {price} G";
                if (textFont != null) label.font = textFont;
            }
            row.onClick.RemoveAllListeners();
            row.onClick.AddListener(() => SelectItem(index, offer, price));
            LayoutElement layout = row.GetComponent<LayoutElement>();
            if (layout == null) layout = row.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 52;
            layout.preferredHeight = 52;
            generatedRows.Add(row.gameObject);
        }

        if (available.Count == 0)
            CreateEmptyListLabel(buying ? "商品がありません" : "売れるものがありません");
        detailText.text = selectedIndex < 0 ? "商品を選択してください" : detailText.text;
        confirmButton.interactable = selectedIndex >= 0 && selectedIndex < available.Count;
    }

    private List<ShopOffer> GetAvailableOffers()
    {
        List<ShopOffer> available = new List<ShopOffer>();
        Inventory inventory = Inventory.Instance;
        if (shop == null || shop.Offers == null) return available;
        foreach (ShopOffer offer in shop.Offers)
        {
            if (offer == null || offer.item == null) continue;
            if (buying || (inventory != null && offer.sellPrice > 0 && inventory.GetItemAmount(offer.item) > 0))
                available.Add(offer);
        }
        return available;
    }

    private void SelectItem(int index, ShopOffer offer, int price)
    {
        selectedIndex = index;
        detailText.text = $"{offer.item.itemName}\n{price} G";
        confirmButton.interactable = true;
    }

    private void ConfirmTrade()
    {
        if (shop == null || selectedIndex < 0 || Inventory.Instance == null) return;
        Inventory inventory = Inventory.Instance;
        List<ShopOffer> available = GetAvailableOffers();
        if (selectedIndex >= available.Count)
        {
            selectedIndex = -1;
            Refresh();
            return;
        }

        ShopOffer selected = available[selectedIndex];
        if (buying)
        {
            if (inventory.gBalance < selected.buyPrice) { detailText.text = "Gが足りません"; return; }
            if (!inventory.AddItem(selected.item)) { detailText.text = "所持品に空きがありません"; return; }
            inventory.gBalance -= selected.buyPrice;
        }
        else
        {
            if (!inventory.RemoveItemAmount(selected.item, 1)) { detailText.text = "売却できませんでした"; return; }
            inventory.gBalance += selected.sellPrice;
        }
        selectedIndex = -1;
        Refresh();
    }

    private void CreateEmptyListLabel(string value)
    {
        GameObject labelObject = new GameObject("EmptyList", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(itemListRoot, false);
        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        label.text = value;
        label.fontSize = 20;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        if (textFont != null) label.font = textFont;
        generatedRows.Add(labelObject);
    }

    private void ClearGeneratedRows()
    {
        foreach (GameObject row in generatedRows)
            if (row != null) Destroy(row);
        generatedRows.Clear();
    }

    protected override void OnWindowOpened()
    {
        DisableGameplay();
        if (EventSystem.current == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    protected override void OnWindowClosed()
    {
        RestoreGameplay();
        shop = null;
    }

    private void DisableGameplay()
    {
        RestoreGameplay();
        if (Inventory.Instance == null) return;
        Transform player = Inventory.Instance.transform.root;
        Disable(player.GetComponentsInChildren<PL_movement>(true));
        Disable(player.GetComponentsInChildren<PlayerCombat>(true));
        Disable(player.GetComponentsInChildren<NewSwordAction>(true));
    }

    private void Disable<T>(T[] components) where T : Behaviour
    {
        foreach (T component in components)
            if (component != null && component.enabled)
            {
                component.enabled = false;
                disabledGameplay.Add(component);
            }
    }

    private void RestoreGameplay()
    {
        foreach (Behaviour component in disabledGameplay)
            if (component != null) component.enabled = true;
        disabledGameplay.Clear();
    }
}
