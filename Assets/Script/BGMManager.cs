using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Owns the single music AudioSource and reads shared scene music settings.</summary>
public sealed class BGMManager : MonoBehaviour
{
    private const string SettingsResourceName = "BGMSettings";

    [SerializeField] private AudioSource audioSource;
    private BGMSettings settings;
    private static BGMManager instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        settings = Resources.Load<BGMSettings>(SettingsResourceName);
        if (settings == null)
            Debug.LogError($"Assets/Resources/{SettingsResourceName}.asset が見つかりません。BGMを設定できません。", this);

        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.loop = true;
        }
    }

    private void Start()
    {
        if (instance != this) return;
        BSPDungeonMaster dungeon = FindFirstObjectByType<BSPDungeonMaster>();
        if (dungeon != null && dungeon.currentStageData != null)
            PlayStageMusic(dungeon.currentStageData);
        else
            PlayTitleMusic();
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (settings == null || scene.name == settings.loadingSceneName)
            return; // LoadingScreenController starts the loading track after its scene appears.

        AudioClip clip = settings.GetSceneMusic(scene.name);
        if (clip != null)
            Play(clip);
    }

    public static void PlayTitleMusic()
    {
        if (instance != null && instance.settings != null)
            instance.Play(instance.settings.titleMusic);
    }

    public static void PlayLoadingMusic()
    {
        if (instance != null && instance.settings != null)
            instance.Play(instance.settings.loadingMusic);
    }

    public static void PlayStageMusic(StageData stage)
    {
        if (instance != null)
            instance.Play(stage != null ? stage.backgroundMusic : null);
    }

    private void Play(AudioClip clip)
    {
        if (audioSource == null) return;
        if (audioSource.clip == clip && audioSource.isPlaying) return;
        audioSource.Stop();
        audioSource.clip = clip;
        if (clip != null)
            audioSource.Play();
    }
}
