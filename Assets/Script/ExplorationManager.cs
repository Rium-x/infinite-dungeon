using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class ExplorationManager : MonoBehaviour
{
    public static ExplorationManager Instance;

    private readonly Dictionary<Light2D, float> originalLightRadiusMap = new Dictionary<Light2D, float>();

    [SerializeField] private StageData currentStage;
    [SerializeField, Min(0f)] private float currentProgress;
    [SerializeField, Min(0f)] private float requiredProgress = 100f;

    public float CurrentProgress => currentProgress;
    public float RequiredProgress => requiredProgress;

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

    public float GetRequiredExplorationValue(StageData stage)
    {
        if (stage == null)
            return requiredProgress;

        return stage.GetRequiredExplorationValueForNextStage();
    }

    private float nextLightRetryTime;
    private Transform cachedPlayerHb;
    private Light2D[] cachedPlayerLights;

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
        Light2D[] lights = cachedPlayerLights;
        if (lights == null || lights.Length == 0 || cachedPlayerHb != hb)
            lights = cachedPlayerLights = hb.GetComponentsInChildren<Light2D>(true);
        cachedPlayerHb = hb;

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

        if ((cachedPlayerHb == null || cachedPlayerLights == null || cachedPlayerLights.Length == 0) &&
            Time.unscaledTime >= nextLightRetryTime)
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

    public void AddProgress(float value)
    {
        if (value <= 0f) return;
        currentProgress += value;

        Debug.Log(
            $"攻略率 {currentProgress:F2}/{requiredProgress:F2}"
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
