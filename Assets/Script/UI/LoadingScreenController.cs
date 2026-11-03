using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// UI attachment point for a hand-authored loading scene. No UI is created at runtime.
/// </summary>
public sealed class LoadingScreenController : MonoBehaviour
{
    [Header("手作りUIの参照")]
    [SerializeField, Tooltip("現在のロード内容を表示するTextMeshProUGUI")]
    private TMP_Text statusText;
    [SerializeField, Tooltip("進行状況を表示するSlider。0から1の範囲を使用")]
    private Slider progressBar;
    [SerializeField, Tooltip("進行率の数値表示。不要なら未設定で構いません")]
    private TMP_Text progressText;
    [SerializeField, Tooltip("ヒントを表示するTextMeshProUGUI")]
    private TMP_Text tipText;

    [Header("ロード中のヒント")]
    [SerializeField, TextArea(2, 4)]
    private List<string> tips = new List<string>();
    [SerializeField, Min(1f), Tooltip("ヒントが切り替わる間隔（秒）")]
    private float tipChangeInterval = 4f;

    [Header("シーン切替")]
    [SerializeField, Min(10f), Tooltip("ゲーム側のマップ生成が完了しない場合にロード画面へエラーを表示する秒数")]
    private float generationTimeoutSeconds = 180f;

    private static string requestedGameplayScene;
    private static bool isTitleLoad;
    private static LoadingScreenController active;
    private float progressBase;
    private float displayedProgress;
    private int lastTipIndex = -1;

    public static string SceneName => "LoadingScene";
    public static bool HasActiveOverlay => active != null;

    private void Awake()
    {
        active = this;

        // The gameplay scene keeps its own camera and EventSystem while this scene
        // is additive. Overlay UI needs neither a camera nor a second EventSystem.
        foreach (Camera sceneCamera in FindObjectsByType<Camera>(FindObjectsInactive.Include))
            if (sceneCamera.gameObject.scene == gameObject.scene)
                sceneCamera.enabled = false;
        foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
            if (listener.gameObject.scene == gameObject.scene)
                listener.enabled = false;
        foreach (EventSystem eventSystem in FindObjectsByType<EventSystem>(FindObjectsInactive.Include))
            if (eventSystem.gameObject.scene == gameObject.scene)
                eventSystem.enabled = false;
    }

    private IEnumerator Start()
    {
        ShowRandomTip();
        StartCoroutine(TipRotationRoutine());
        BGMManager.PlayLoadingMusic();

        string targetScene = requestedGameplayScene;
        bool loadFromTitle = isTitleLoad;
        requestedGameplayScene = null;
        isTitleLoad = false;

        if (string.IsNullOrWhiteSpace(targetScene))
        {
            SetStatus("ステージを準備しています...");
            yield break;
        }
        SetStatus("ゲームシーンを読み込んでいます...");
        progressBase = loadFromTitle ? 0.25f : 0f;
        AsyncOperation sceneLoad = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Additive);
        if (sceneLoad == null)
        {
            SetStatus($"シーン「{targetScene}」を読み込めませんでした。");
            yield break;
}
        while (!sceneLoad.isDone)
        {
            float sceneProgress = Mathf.Clamp01(sceneLoad.progress / 0.9f);
            if (loadFromTitle)
                SetProgress(sceneProgress * progressBase);
            yield return null;
        }

        Scene gameplayScene = SceneManager.GetSceneByName(targetScene);
        if (!gameplayScene.IsValid() || !gameplayScene.isLoaded)
        {
            SetStatus($"ゲームシーン「{targetScene}」の読み込みに失敗しました。");
            yield break;
        }

        SetStatus("ダンジョン生成を待っています...");
        float elapsed = 0f;
        while (!BSPDungeonMaster.HasCompletedGeneration && elapsed < generationTimeoutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (!BSPDungeonMaster.HasCompletedGeneration)
        {
            SetStatus("生成が完了しませんでした。Unity Consoleを確認してください。");
            yield break;
        }

        SetProgress(1f);
        SetStatus("準備完了");
        SceneManager.SetActiveScene(gameplayScene);
        yield return null;
        BGMManager.PlayStageMusic(BSPDungeonMaster.Instance != null
            ? BSPDungeonMaster.Instance.currentStageData
            : null);
        Scene loadingScene = gameObject.scene;
        if (loadingScene.IsValid() && loadingScene.isLoaded)
            yield return SceneManager.UnloadSceneAsync(loadingScene);
    }

    public static void BeginGame(string gameplaySceneName, string loadingSceneName)
    {
        string targetScene = string.IsNullOrWhiteSpace(gameplaySceneName) ? "SampleScene" : gameplaySceneName;
        string sceneName = string.IsNullOrWhiteSpace(loadingSceneName) ? SceneName : loadingSceneName;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning($"ロード画面シーン「{sceneName}」がBuild Settingsにありません。ゲームシーンを直接読み込みます。");
            SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
            return;
        }

        requestedGameplayScene = targetScene;
        isTitleLoad = true;
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    public static IEnumerator OpenOverlayRoutine(string loadingSceneName)
    {
        string sceneName = string.IsNullOrWhiteSpace(loadingSceneName) ? SceneName : loadingSceneName;
        if (active != null || !Application.CanStreamedLevelBeLoaded(sceneName))
            yield break;
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (load == null) yield break;
        while (!load.isDone) yield return null;
        yield return null;
    }

    public static IEnumerator CloseOverlayRoutine()
    {
        if (active == null) yield break;
        LoadingScreenController overlay = active;
        Scene overlayScene = overlay.gameObject.scene;
        if (overlayScene.IsValid() && overlayScene.isLoaded)
            yield return SceneManager.UnloadSceneAsync(overlayScene);
        if (active == overlay)
            active = null;
    }

    public static void ReportMapProgress(string message, float progress)
    {
        if (active == null) return;
        active.SetStatus(message);
        active.SetProgress(active.progressBase + (1f - active.progressBase) * Mathf.Clamp01(progress));
    }

    private IEnumerator TipRotationRoutine()
    {
        if (tipText == null || tips == null || tips.Count <= 1)
            yield break;

        float elapsed = 0f;
        while (true)
        {
            elapsed += Time.unscaledDeltaTime;
            if (elapsed >= tipChangeInterval)
            {
                elapsed = 0f;
                ShowRandomTip();
            }
            yield return null;
        }
    }

    public void ShowRandomTip()
    {
        if (tipText == null || tips == null || tips.Count == 0) return;
        List<string> availableTips = tips.FindAll(tip => !string.IsNullOrWhiteSpace(tip));
        if (availableTips.Count == 0) return;

        int nextIndex = Random.Range(0, availableTips.Count);
        if (availableTips.Count > 1)
        {
            while (nextIndex == lastTipIndex)
                nextIndex = Random.Range(0, availableTips.Count);
        }

        lastTipIndex = nextIndex;
        tipText.text = availableTips[nextIndex];
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    private void SetProgress(float value)
    {
        displayedProgress = Mathf.Max(displayedProgress, Mathf.Clamp01(value));
        if (progressBar != null) progressBar.value = displayedProgress;
        if (progressText != null) progressText.text = $"{Mathf.RoundToInt(displayedProgress * 100f)}%";
    }

    private void OnDestroy()
    {
        if (active == this) active = null;
    }

}
