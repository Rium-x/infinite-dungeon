using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Gameplay transition used by the title scene's serialized UI button.</summary>
public sealed class TitleSceneController : MonoBehaviour
{
    [SerializeField] private string gameplaySceneName = "SampleScene";

    public void BeginGame()
    {
        SceneManager.LoadScene(gameplaySceneName);
    }
}
