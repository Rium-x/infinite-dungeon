using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ShopWindowSceneBuilder
{
    private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
    private const string JapaneseFontGuid = "2a80d456ce403c04aa6085463cc50a5e";

    [MenuItem("GameObject/UI/Create Shop Window In Current Scene", false, 20)]
    public static void CreateOrUpdateInCurrentScene()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Play Mode を終了してから実行してください。");
            return;
        }

        Transform panel = FindNamedObject("ShopWindow");
        if (panel != null)
            ConfigureExistingWindow(panel);
        else if (Object.FindAnyObjectByType<ShopWindowUI>(FindObjectsInactive.Include) == null)
            CreateWindow();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    [MenuItem("Tools/Shop/Create Or Update Shop Window In SampleScene")]
    public static void CreateOrUpdateInSampleScene()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Play Mode を終了してから実行してください。");
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(SampleScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);

        Transform panel = FindNamedObject("ShopWindow");
        if (panel != null)
            ConfigureExistingWindow(panel);
        else if (Object.FindAnyObjectByType<ShopWindowUI>(FindObjectsInactive.Include) == null)
            CreateWindow();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Shop Window を SampleScene に配置して保存しました。");
    }

    private static Transform FindNamedObject(string objectName)
    {
        foreach (Transform transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (transform.name == objectName && transform.GetComponentInParent<Canvas>() != null)
                return transform;
        return null;
    }

    private static void ConfigureExistingWindow(Transform panel)
    {
        Canvas canvas = panel.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("ShopWindow の親に Canvas がありません。", panel);
            return;
        }

        ShopWindowUI controller = canvas.GetComponent<ShopWindowUI>();
        if (controller == null) controller = Undo.AddComponent<ShopWindowUI>(canvas.gameObject);
        Transform list = panel.Find("ShopItemList");
        Transform content = list != null ? list.Find("Content") : null;
        Button buy = GetButton(panel, "BuyMode");
        Button sell = GetButton(panel, "SellMode");
        Button confirm = GetButton(panel, "ConfirmTrade");
        if (list == null || content == null || buy == null || sell == null || confirm == null)
        {
            Debug.LogError("ShopWindow 内に ShopItemList/Content、BuyMode、SellMode、ConfirmTrade が必要です。", panel);
            return;
        }

        ScrollRect scroll = list.GetComponent<ScrollRect>();
        if (scroll == null) scroll = Undo.AddComponent<ScrollRect>(list.gameObject);
        if (list.GetComponent<Image>() == null) Undo.AddComponent<Image>(list.gameObject);
        if (list.GetComponent<RectMask2D>() == null) Undo.AddComponent<RectMask2D>(list.gameObject);
        VerticalLayoutGroup parentLayout = list.GetComponent<VerticalLayoutGroup>();
        if (parentLayout != null) Undo.DestroyObjectImmediate(parentLayout);
        scroll.viewport = list.GetComponent<RectTransform>();
        scroll.content = content.GetComponent<RectTransform>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        VerticalLayoutGroup itemLayout = content.GetComponent<VerticalLayoutGroup>();
        if (itemLayout == null) itemLayout = Undo.AddComponent<VerticalLayoutGroup>(content.gameObject);
        itemLayout.spacing = 5;
        itemLayout.childControlWidth = true;
        itemLayout.childControlHeight = true;
        itemLayout.childForceExpandWidth = true;
        itemLayout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = Undo.AddComponent<ContentSizeFitter>(content.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Button rowTemplate = GetButton(content, "ItemRowTemplate");
        if (rowTemplate == null)
        {
            rowTemplate = CreateButton("ItemRowTemplate", content, "商品名   価格 G", Vector2.zero, Vector2.one);
            LayoutElement rowLayout = rowTemplate.gameObject.AddComponent<LayoutElement>();
            rowLayout.minHeight = 52;
            rowLayout.preferredHeight = 52;
        }
        rowTemplate.gameObject.SetActive(false);

        TextMeshProUGUI title = GetText(panel, "ShopTitle");
        TextMeshProUGUI balance = GetText(panel, "GBalance");
        TextMeshProUGUI mode = GetText(panel, "ModeLabel");
        TextMeshProUGUI detail = GetText(panel, "SelectionDetails");
        TextMeshProUGUI placeholder = GetText(panel, "PortraitPlaceholder");
        Image portrait = GetImage(panel, "ShopkeeperPortrait");
        Image background = GetImage(panel, "ShopkeeperBackground");
        if (title == null || balance == null || mode == null || detail == null || placeholder == null || portrait == null || background == null)
        {
            Debug.LogError("ShopWindow 内の表示用 Text / Image オブジェクト名を確認してください。", panel);
            return;
        }

        TMP_FontAsset font = title.font != null
            ? title.font
            : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(JapaneseFontGuid));
        controller.ConfigureSceneWindow(
            panel.gameObject, font, title, balance, mode, detail, placeholder,
            portrait, background, content, rowTemplate, buy, sell, confirm);
        panel.gameObject.SetActive(false);
        EditorUtility.SetDirty(controller);
        Selection.activeGameObject = canvas.gameObject;
    }

    private static Button GetButton(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        return child != null ? child.GetComponent<Button>() : null;
    }

    private static TextMeshProUGUI GetText(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    private static Image GetImage(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        return child != null ? child.GetComponent<Image>() : null;
    }

    private static void CreateWindow()
    {
        GameObject canvasObject = new GameObject("Shop Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Create Shop Window");
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        GameObject panel = CreateUIObject("Shop Window", canvasObject.transform);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(.5f, .5f);
        panelRect.sizeDelta = new Vector2(1080, 650);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(.09f, .075f, .055f, .98f);

        TextMeshProUGUI title = CreateText("ShopTitle", panel.transform, "ショップ", 34, TextAlignmentOptions.Left, new Vector2(.04f,.9f), new Vector2(.94f,.99f));
        TextMeshProUGUI balance = CreateText("GBalance", panel.transform, "G: 0", 24, TextAlignmentOptions.Right, new Vector2(.65f,.9f), new Vector2(.94f,.99f));
        Button buy = CreateButton("BuyMode", panel.transform, "買う", new Vector2(.04f,.82f), new Vector2(.21f,.9f));
        Button sell = CreateButton("SellMode", panel.transform, "売る", new Vector2(.22f,.82f), new Vector2(.39f,.9f));
        TextMeshProUGUI mode = CreateText("ModeLabel", panel.transform, "買えるもの", 22, TextAlignmentOptions.Left, new Vector2(.04f,.75f), new Vector2(.6f,.82f));

        GameObject list = CreateUIObject("ShopItemList", panel.transform);
        SetRect(list.GetComponent<RectTransform>(), new Vector2(.04f,.2f), new Vector2(.6f,.75f));
        list.AddComponent<Image>().color = new Color(.16f,.14f,.11f,.95f);
        list.AddComponent<RectMask2D>();
        ScrollRect scroll = list.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        GameObject content = CreateUIObject("Content", list.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0,1); contentRect.anchorMax = Vector2.one; contentRect.pivot = new Vector2(.5f,1);
        contentRect.offsetMin = new Vector2(8,0); contentRect.offsetMax = new Vector2(-8,0);
        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 5; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRect;
        scroll.viewport = list.GetComponent<RectTransform>();

        Button rowTemplate = CreateButton("ItemRowTemplate", content.transform, "商品名   価格 G", Vector2.zero, Vector2.one);
        LayoutElement rowLayout = rowTemplate.gameObject.AddComponent<LayoutElement>();
        rowLayout.minHeight = 52; rowLayout.preferredHeight = 52;
        rowTemplate.gameObject.SetActive(false);

        TextMeshProUGUI detail = CreateText("SelectionDetails", panel.transform, "商品を選択してください", 22, TextAlignmentOptions.TopLeft, new Vector2(.04f,.1f), new Vector2(.6f,.19f));
        Button confirm = CreateButton("ConfirmTrade", panel.transform, "確定", new Vector2(.42f,.08f), new Vector2(.6f,.17f));

        GameObject portraitFrame = CreateUIObject("ShopkeeperBackground", panel.transform);
        SetRect(portraitFrame.GetComponent<RectTransform>(), new Vector2(.64f,.2f), new Vector2(.96f,.84f));
        Image backgroundImage = portraitFrame.AddComponent<Image>();
        backgroundImage.color = new Color(.28f,.23f,.17f,1f);
        GameObject portrait = CreateUIObject("ShopkeeperPortrait", panel.transform);
        SetRect(portrait.GetComponent<RectTransform>(), new Vector2(.69f,.27f), new Vector2(.91f,.78f));
        Image portraitImage = portrait.AddComponent<Image>();
        portraitImage.color = new Color(.55f,.48f,.38f,1f);
        TextMeshProUGUI placeholder = CreateText("PortraitPlaceholder", panel.transform, "店主", 26, TextAlignmentOptions.Center, new Vector2(.69f,.45f), new Vector2(.91f,.56f));

        Button close = CreateButton("CloseButton", panel.transform, "×", new Vector2(1,1), new Vector2(1,1));
        RectTransform closeRect = close.GetComponent<RectTransform>();
        closeRect.anchorMin = closeRect.anchorMax = new Vector2(1,1);
        closeRect.pivot = new Vector2(1,1);
        closeRect.anchoredPosition = new Vector2(-12,-12);
        closeRect.sizeDelta = new Vector2(44,44);
        close.GetComponent<Image>().color = new Color(.55f,.16f,.18f,.96f);
        close.GetComponentInChildren<TMP_Text>().fontSize = 30;

        ShopWindowUI controller = canvasObject.AddComponent<ShopWindowUI>();
        controller.ConfigureSceneWindow(
            panel, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(JapaneseFontGuid)),
            title, balance, mode, detail, placeholder, portraitImage, backgroundImage,
            content.transform, rowTemplate, buy, sell, confirm);
        panel.SetActive(false);
        Selection.activeGameObject = canvasObject;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string value, float size, TextAlignmentOptions alignment, Vector2 min, Vector2 max)
    {
        GameObject go = CreateUIObject(name, parent);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        SetRect(go.GetComponent<RectTransform>(), min, max);
        return text;
    }

    private static Button CreateButton(string name, Transform parent, string label, Vector2 min, Vector2 max)
    {
        GameObject go = CreateUIObject(name, parent);
        Image image = go.AddComponent<Image>();
        image.color = new Color(.27f,.23f,.17f,1f);
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        SetRect(go.GetComponent<RectTransform>(), min, max);
        TextMeshProUGUI text = CreateText("Label", go.transform, label, 19, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
        text.raycastTarget = false;
        return button;
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

}
