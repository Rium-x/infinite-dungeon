using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BGMSettings", menuName = "Audio/BGM Settings")]
public sealed class BGMSettings : ScriptableObject
{
    [Serializable]
    public sealed class SceneMusic
    {
        [Tooltip("Build Settingsに登録したシーン名")]
        public string sceneName;
        public AudioClip music;
    }

    [Header("基本シーン")]
    public string titleSceneName = "TitleScene";
    public string loadingSceneName = "LoadingScene";
    public AudioClip titleMusic;
    public AudioClip loadingMusic;

    [Header("今後追加するシーン")]
    [Tooltip("シーン名とBGMを追加します。ステージの曲は各StageDataで設定してください。")]
    public List<SceneMusic> otherScenes = new List<SceneMusic>();

    public AudioClip GetSceneMusic(string sceneName)
    {
        if (string.Equals(sceneName, titleSceneName, StringComparison.Ordinal))
            return titleMusic;
        if (string.Equals(sceneName, loadingSceneName, StringComparison.Ordinal))
            return loadingMusic;

        foreach (SceneMusic entry in otherScenes)
            if (entry != null && string.Equals(entry.sceneName, sceneName, StringComparison.Ordinal))
                return entry.music;
        return null;
    }
}
