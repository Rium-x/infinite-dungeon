using UnityEngine;

/// <summary>
/// Title scene controller for scene transition, parallax layers, title bobbing, and hover button animation.
/// All behavior is configurable from the inspector.
/// </summary>
public sealed class TitleSceneController : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField, Tooltip("The gameplay scene to load when the player presses the start button.")] private string gameplaySceneName = "SampleScene";
    [SerializeField, Tooltip("ユーザーがUIを配置したロード専用シーン")]
    private string loadingSceneName = "LoadingScene";

    [Header("Parallax")]
    [SerializeField, Tooltip("Back layer. Move it less so it feels farther away.")] private RectTransform backLayer;
    [SerializeField, Tooltip("Front layer. Move it more so it feels closer to the camera.")] private RectTransform frontLayer;
    [SerializeField, Tooltip("How strongly mouse movement affects the background. Higher values make the camera feel more active.")] private float mouseInfluence = 80f;
    [SerializeField, Tooltip("Idle movement when the mouse is not moving. This keeps the background gently alive at all times.")] private Vector2 idleAmplitude = new Vector2(12f, 8f);
    [SerializeField, Tooltip("The back layer's movement strength relative to the front layer.")] private float backLayerStrength = 0.35f;
    [SerializeField, Tooltip("The front layer's movement strength relative to the back layer.")] private float frontLayerStrength = 1f;
    [SerializeField, Tooltip("How smoothly the parallax follows the mouse. Lower values feel more relaxed.")] private float parallaxSmoothing = 0.08f;

    [Header("Title")]
    [SerializeField, Tooltip("The title object that gently floats up and down.")] private RectTransform titleText;
    [SerializeField, Tooltip("The vertical amplitude of the title bobbing motion.")] private float titleFloatAmplitude = 12f;
    [SerializeField, Tooltip("The speed of the title bobbing motion.")] private float titleFloatSpeed = 0.8f;

    [Header("Start Button")]
    [SerializeField, Tooltip("The start button. It expands slightly when the pointer hovers over it.")] private RectTransform startButton;
    [SerializeField, Tooltip("The button's base scale value before hover. Usually keep this at (1, 1, 1). ")] private Vector3 buttonBaseScale = Vector3.one;
    [SerializeField, Tooltip("How much the button grows when the mouse is over it.")] private float hoverScaleAmount = 0.12f;
    [SerializeField, Tooltip("How quickly the button reaches its hover scale.")] private float buttonResponseSpeed = 0.12f;

    private Vector2 backBasePosition;
    private Vector2 frontBasePosition;
    private Vector2 titleBasePosition;
    private Vector3 buttonInitialScale;
    private bool initialized;

    private void Awake()
    {
        CacheInitialValues();
    }

    private void Update()
    {
        if (!initialized)
        {
            CacheInitialValues();
        }

        UpdateBackgroundParallax();
        UpdateTitleMotion();
        UpdateButtonHover();
    }

    private void CacheInitialValues()
    {
        if (backLayer != null)
        {
            backBasePosition = backLayer.anchoredPosition;
        }

        if (frontLayer != null)
        {
            frontBasePosition = frontLayer.anchoredPosition;
        }

        if (titleText != null)
        {
            titleBasePosition = titleText.anchoredPosition;
        }

        if (startButton != null)
        {
            buttonInitialScale = startButton.localScale;
            buttonBaseScale = buttonInitialScale;
        }

        initialized = true;
    }

    private void UpdateBackgroundParallax()
    {
        if (backLayer == null && frontLayer == null)
        {
            return;
        }

        Vector2 mousePosition = Input.mousePosition;
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 mouseNormalized = new Vector2(
            (mousePosition.x - screenCenter.x) / Mathf.Max(1f, screenCenter.x),
            (mousePosition.y - screenCenter.y) / Mathf.Max(1f, screenCenter.y));

        float time = Time.unscaledTime;

        Vector2 idleBack = new Vector2(
            Mathf.Sin(time * 0.9f) * idleAmplitude.x * backLayerStrength,
            Mathf.Cos(time * 0.8f) * idleAmplitude.y * backLayerStrength);

        Vector2 idleFront = new Vector2(
            Mathf.Sin(time * 1.25f + 1.5f) * idleAmplitude.x * frontLayerStrength * 0.8f,
            Mathf.Cos(time * 1.1f + 1.3f) * idleAmplitude.y * frontLayerStrength * 0.8f);

        Vector2 targetBack = backBasePosition + new Vector2(
            -mouseNormalized.x * mouseInfluence * backLayerStrength + idleBack.x,
            -mouseNormalized.y * mouseInfluence * backLayerStrength * 0.7f + idleBack.y);

        Vector2 targetFront = frontBasePosition + new Vector2(
            -mouseNormalized.x * mouseInfluence * frontLayerStrength + idleFront.x,
            -mouseNormalized.y * mouseInfluence * frontLayerStrength * 0.7f + idleFront.y);

        if (backLayer != null)
        {
            backLayer.anchoredPosition = Vector2.Lerp(backLayer.anchoredPosition, targetBack, parallaxSmoothing);
        }

        if (frontLayer != null)
        {
            frontLayer.anchoredPosition = Vector2.Lerp(frontLayer.anchoredPosition, targetFront, parallaxSmoothing);
        }
    }

    private void UpdateTitleMotion()
    {
        if (titleText == null)
        {
            return;
        }

        float bob = Mathf.Sin(Time.unscaledTime * titleFloatSpeed) * titleFloatAmplitude;
        Vector2 targetPosition = titleBasePosition + new Vector2(0f, bob);
        titleText.anchoredPosition = Vector2.Lerp(titleText.anchoredPosition, targetPosition, 0.08f);
    }

    private void UpdateButtonHover()
    {
        if (startButton == null)
        {
            return;
        }

        bool isHovered = RectTransformUtility.RectangleContainsScreenPoint(startButton, Input.mousePosition);
        Vector3 targetScale = isHovered ? buttonBaseScale * (1f + hoverScaleAmount) : buttonBaseScale;
        startButton.localScale = Vector3.Lerp(startButton.localScale, targetScale, buttonResponseSpeed);
    }

    public void BeginGame()
    {
        LoadingScreenController.BeginGame(gameplaySceneName, loadingSceneName);
    }
}
