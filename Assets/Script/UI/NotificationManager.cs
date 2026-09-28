using UnityEngine;
using TMPro;
using System.Collections.Generic;
using UnityEngine.UI;

public class NotificationManager : MonoBehaviour
{
    public static NotificationManager Instance;

    [Header("スクロール")]
    public ScrollRect scrollRect;

    [Header("ログ")]
    public GameObject logTextPrefab;
    public Transform logContent;

    [Header("設定")]
    public int maxLogCount = 50;

    [Header("サウンド")]
    [SerializeField]
    private AudioSource audioSource;

    [SerializeField]
    private AudioClip pickupSound;

    [SerializeField]
    private AudioClip warningSound;

    [SerializeField]
    private AudioClip bossSound;

    private List<GameObject> logs =
        new List<GameObject>();

    public enum NotificationSound
    {
        None,
        Pickup,
        Warning,
        Boss
    }

    void Awake()
    {
        Instance = this;
    }

    public void Show(
        string message,
        Color? textColor = null,
        NotificationSound soundType =
            NotificationSound.None
    )
    {
        AddLog(message, textColor);

        PlaySound(soundType);
    }

    private void PlaySound(
        NotificationSound soundType
    )
    {
        if (audioSource == null) return;

        AudioClip clip = null;

        switch (soundType)
        {
            case NotificationSound.Pickup:
                clip = pickupSound;
                break;

            case NotificationSound.Warning:
                clip = warningSound;
                break;

            case NotificationSound.Boss:
                clip = bossSound;
                break;
        }

        if (clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    void AddLog(
        string message,
        Color? textColor = null
    )
    {
        GameObject log =
            Instantiate(logTextPrefab, logContent);

        TMP_Text text =
            log.GetComponentInChildren<TMP_Text>();

        text.text = message;

        if (textColor.HasValue)
        {
            text.color = textColor.Value;
        }

        logs.Add(log);

        if (logs.Count > maxLogCount)
        {
            Destroy(logs[0]);
            logs.RemoveAt(0);
        }

        LayoutRebuilder
            .ForceRebuildLayoutImmediate(
                logContent.GetComponent<RectTransform>()
            );

        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}