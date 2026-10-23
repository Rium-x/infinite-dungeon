using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Title screen and asynchronous transition to the gameplay scene.</summary>
public sealed class TitleSceneController : MonoBehaviour
{
    [SerializeField] private string gameplaySceneName = "SampleScene";
    [SerializeField] private Font uiFont;

    private Canvas canvas;
    private GameObject loadingPanel;
    private Text progressText;
    private bool loading;

    private void Start()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        var canvasObject = new GameObject("Title Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        AddText("Game Title", "無限ダンジョン", 72, new Vector2(0.5f, 0.68f), new Vector2(900, 120));
        var start = AddButton("Start Button", "冒険を始める", new Vector2(0.5f, 0.38f));
        start.onClick.AddListener(BeginLoading);

        loadingPanel = new GameObject("Loading Panel", typeof(RectTransform), typeof(Image));
        loadingPanel.transform.SetParent(canvas.transform, false);
        var rect = loadingPanel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        loadingPanel.GetComponent<Image>().color = new Color(0.025f, 0.03f, 0.05f, 1f);
        progressText = AddText("Loading Text", "読み込み中... 0%", 36, new Vector2(0.5f, 0.5f), new Vector2(700, 80));
        loadingPanel.SetActive(false);
    }

    private Text AddText(string objectName, string value, int size, Vector2 anchor, Vector2 dimensions)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(canvas.transform, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = dimensions;
        var text = go.GetComponent<Text>();
        text.text = value;
        text.font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        return text;
    }

    private Button AddButton(string objectName, string label, Vector2 anchor)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(canvas.transform, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(420, 92);
        go.GetComponent<Image>().color = new Color(0.28f, 0.2f, 0.1f, 1f);
        AddTextTo(go.transform, label);
        return go.GetComponent<Button>();
    }

    private void AddTextTo(Transform parent, string value)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var text = go.GetComponent<Text>();
        text.text = value;
        text.font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 32;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
    }

    private void BeginLoading()
    {
        if (!loading) StartCoroutine(LoadGameplay());
    }

    private IEnumerator LoadGameplay()
    {
        loading = true;
        loadingPanel.SetActive(true);
        var operation = SceneManager.LoadSceneAsync(gameplaySceneName);
        if (operation == null)
        {
            progressText.text = "シーンを読み込めません: " + gameplaySceneName;
            loading = false;
            yield break;
        }

        while (!operation.isDone)
        {
            var percent = Mathf.Clamp01(operation.progress / 0.9f);
            progressText.text = $"読み込み中... {Mathf.RoundToInt(percent * 100f)}%";
            yield return null;
        }
    }
}
