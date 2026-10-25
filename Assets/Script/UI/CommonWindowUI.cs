using TMPro;
using UnityEngine;
using UnityEngine.UI;

public abstract class CommonWindowUI : MonoBehaviour
{
    [Header("共通ウィンドウ設定")]
    [Tooltip("開閉時に表示を切り替えるウィンドウのルート")]
    [SerializeField] protected GameObject windowRoot;
    [Tooltip("ウィンドウ表示中に非表示にするCanvas（任意）")]
    [SerializeField] protected GameObject canvasToHideWhileOpen;

    protected bool IsOpen { get; private set; }

    private bool canvasWasActive;
    private bool canvasStateWasSaved;
    private Button closeButton;
    private bool closeButtonInitialized;
    private bool warnedAboutWindowCanvasHierarchy;

    protected virtual void OnEnable()
    {
        CommonWindowEvents.WindowOpened += OnWindowOpened;
    }

    protected virtual void OnDisable()
    {
        CommonWindowEvents.WindowOpened -= OnWindowOpened;
    }

    public void Open() => SetOpen(true);
    public void Close() => SetOpen(false);

    protected void SetOpen(bool open)
    {
        if (IsOpen == open)
        {
            if (windowRoot != null)
                windowRoot.SetActive(open);

            if (open)
            {
                SetupCloseButton();
                CommonWindowEvents.NotifyWindowOpened(this);
                OnWindowOpened();
            }

            return;
        }

        if (open)
            CommonWindowEvents.NotifyWindowOpened(this);

        if (open)
            SetupCloseButton();

        IsOpen = open;
        SetCanvasVisibility(open);

        if (windowRoot != null)
            windowRoot.SetActive(open);

        if (open)
        {
            if (windowRoot != null && !windowRoot.activeInHierarchy)
            {
                Debug.LogWarning(
                    $"{GetType().Name}: Window Root は有効化しましたが、親オブジェクトが非アクティブのため表示できません。シーン上の親階層を確認してください。",
                    this);
            }

            OnWindowOpened();
        }
        else
            OnWindowClosed();
    }

    protected virtual void OnWindowOpened()
    {
    }

    protected virtual void OnWindowClosed()
    {
    }

    protected void SetupCloseButton()
    {
        if (closeButtonInitialized)
            return;

        if (windowRoot == null)
        {
            Debug.LogError($"{GetType().Name}: ×ボタンを作成する Window Root が設定されていません。", this);
            return;
        }

        closeButton = CreateCloseButton(windowRoot.transform);
        closeButton.onClick.RemoveListener(Close);
        closeButton.onClick.AddListener(Close);
        closeButtonInitialized = true;
    }

    private void OnWindowOpened(MonoBehaviour openedWindow)
    {
        if (openedWindow != this)
            SetOpen(false);
    }

    private Button CreateCloseButton(Transform parent)
    {
        GameObject buttonObject = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        buttonObject.transform.SetAsLastSibling();

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1f, 1f);
        buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.pivot = new Vector2(1f, 1f);
        buttonRect.anchoredPosition = new Vector2(-12f, -12f);
        buttonRect.sizeDelta = new Vector2(44f, 44f);

        Image background = buttonObject.GetComponent<Image>();
        background.color = new Color(0.55f, 0.16f, 0.18f, 0.96f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = background;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = "×";
        label.fontSize = 30f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;

        return button;
    }

    private void SetCanvasVisibility(bool open)
    {
        if (canvasToHideWhileOpen == null)
            return;

        if (open)
        {
            Transform canvasTransform = canvasToHideWhileOpen.transform;
            bool windowIsInsideCanvas =
                transform == canvasTransform || transform.IsChildOf(canvasTransform) ||
                (windowRoot != null &&
                 (windowRoot.transform == canvasTransform || windowRoot.transform.IsChildOf(canvasTransform)));

            if (windowIsInsideCanvas)
            {
                if (!warnedAboutWindowCanvasHierarchy)
                {
                    Debug.LogWarning(
                        $"{GetType().Name}: ウィンドウまたはその制御スクリプトが非表示対象Canvasの内側にあります。表示を保つため、そのCanvasは非表示にしません。",
                        this);
                    warnedAboutWindowCanvasHierarchy = true;
                }

                return;
            }

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
}
