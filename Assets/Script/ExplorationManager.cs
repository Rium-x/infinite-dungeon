using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class ExplorationManager : MonoBehaviour
{
    public static ExplorationManager Instance;

    private readonly Dictionary<Light2D, float> originalLightRadiusMap = new Dictionary<Light2D, float>();

    [SerializeField] private StageData currentStage;
    [SerializeField] private int currentProgress;
    [SerializeField] private int requiredProgress = 100;

    public int CurrentProgress => currentProgress;
    public int RequiredProgress => requiredProgress;

    private void Awake()
    {
        Instance = this;
    }

    public void SetCurrentStage(StageData stage)
    {
        currentStage = stage;
        requiredProgress = stage != null ? stage.GetRequiredExplorationValueForNextStage() : 0;
        ResetProgress();
        ApplyStageLightRadius();
    }

    public int GetRequiredExplorationValue(StageData stage)
    {
        if (stage == null)
            return requiredProgress;

        return stage.GetRequiredExplorationValueForNextStage();
    }

    private float nextLightRetryTime;

    public void ApplyStageLightRadius()
    {
        if (currentStage == null)
            return;

        Transform hb = FindPlayerHbRoot();
        if (hb == null)
        {
            nextLightRetryTime = Time.unscaledTime + 0.25f;
            return;
        }

        float scale = currentStage.GetPlayerLightRadiusScale();
        Light2D[] lights = hb.GetComponentsInChildren<Light2D>(true);
        if (lights == null || lights.Length == 0)
        {
            lights = FindSceneLightsUnderHb(hb);
        }

        if (lights == null || lights.Length == 0)
        {
            nextLightRetryTime = Time.unscaledTime + 0.25f;
            return;
        }

        foreach (Light2D light in lights)
        {
            if (light == null)
                continue;

            if (!originalLightRadiusMap.ContainsKey(light))
            {
                originalLightRadiusMap[light] = light.pointLightOuterRadius;
            }

            float originalRadius = originalLightRadiusMap[light];
            light.pointLightOuterRadius = originalRadius * scale;
        }
    }

    private void Update()
    {
        if (currentStage == null)
            return;

        if (Time.unscaledTime >= nextLightRetryTime)
        {
            ApplyStageLightRadius();
            nextLightRetryTime = Time.unscaledTime + 0.25f;
        }
    }

    private static Transform FindPlayerHbRoot()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject candidate in players)
        {
            if (candidate == null)
                continue;

            Transform hb = GetPlayerHbTransform(candidate.transform);
            if (hb != null)
                return hb;
        }

        foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate == null || candidate.hideFlags == HideFlags.NotEditable || candidate.hideFlags == HideFlags.HideAndDontSave)
                continue;

            Transform hb = GetPlayerHbTransform(candidate.transform);
            if (hb != null)
                return hb;
        }

        return null;
    }

    private static Transform GetPlayerHbTransform(Transform playerTransform)
    {
        if (playerTransform == null)
            return null;

        if (playerTransform.name == "HB")
            return playerTransform;

        Transform hb = playerTransform.Find("HB");
        if (hb != null)
            return hb;

        foreach (Transform child in playerTransform.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == "HB")
                return child;
        }

        return null;
    }

    private static Light2D[] FindSceneLightsUnderHb(Transform hb)
    {
        if (hb == null)
            return null;

        List<Light2D> matches = new List<Light2D>();
        Light2D[] allLights = Resources.FindObjectsOfTypeAll<Light2D>();
        foreach (Light2D light in allLights)
        {
            if (light == null)
                continue;

            Transform lightTransform = light.transform;
            if (lightTransform == hb || lightTransform.IsChildOf(hb))
                matches.Add(light);
        }

        return matches.ToArray();
    }

    public void AddProgress(int value)
    {
        currentProgress += value;


        Debug.Log(
            $"攻略率 {currentProgress}/{requiredProgress}"
        );

        if (currentProgress >= requiredProgress)
        {
            NotificationManager.Instance?.Show(
                "次の階層へ進めるようになった！",
                            Color.yellow,
                            NotificationManager.NotificationSound.Pickup
            );
        }
    }

    public bool CanGoNextStage()
    {
        return currentProgress >= requiredProgress;
    }

    public void ResetProgress()
    {
        currentProgress = 0;
    }
}