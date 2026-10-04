using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[System.Serializable]
public sealed class StageNormalItemSetting
{
    public ItemData itemData;

    [Range(-100f, 100f)]
    [Tooltip("このステージ順位置での確率補正(%)。ItemDataのレアリティに加算します。同じアイテムの設定間はステージ順に滑らかに補間されます。")]
    [FormerlySerializedAs("relativeFrequencyAdjustment")]
    public float stageModifier;
}

[System.Serializable]
public sealed class DropItemPool
{
    [Tooltip("通常アイテム候補。空欄のアイテムは全ステージで出現しません。")]
    public List<StageNormalItemSetting> items = new List<StageNormalItemSetting>();
}

[System.Serializable]
public sealed class MonsterItemTypeAdjustment
{
    public ItemType itemType;
    [Range(-100f, 100f)]
    [Tooltip("この種類の通常ドロップ確率の倍率補正。0=標準、-100=出ない、+100=2倍です。")]
    [FormerlySerializedAs("relativeFrequencyAdjustment")]
    public float modifier;
}

/// <summary>The BSP stage order is treated as item probability keyframes.</summary>
public static class NormalItemDropCalculator
{
    public static List<ItemData> GetCandidateItems(IList<StageData> stages)
    {
        var items = new List<ItemData>();
        var seen = new HashSet<ItemData>();
        if (stages == null) return items;
        foreach (StageData stage in stages)
        {
            if (stage == null || stage.normalItemDrops == null || stage.normalItemDrops.items == null) continue;
            foreach (StageNormalItemSetting setting in stage.normalItemDrops.items)
                if (setting != null && setting.itemData != null && seen.Add(setting.itemData))
                    items.Add(setting.itemData);
        }
        return items;
    }

    public static float GetStageProbability(IList<StageData> stages, StageData currentStage, int playerLevel,
        ItemData item, float falloffPerStage, float entryRampStages)
    {
        if (stages == null || stages.Count == 0 || item == null) return 0f;

        int currentStageIndex = -1;
        for (int i = 0; i < stages.Count; i++)
        {
            if (stages[i] == currentStage)
            {
                currentStageIndex = i;
                break;
            }
        }
        if (currentStageIndex < 0) return 0f;

        float perLevel = stages[0] != null ? stages[0].playerLevelOrderPerLevel : 0f;
        float currentOrderPosition = currentStageIndex + 1f + Mathf.Max(0, playerLevel - 1) * perLevel;

        float firstPosition = 0f, firstValue = 0f;
        float previousPosition = 0f, previousValue = 0f;
        float lastPosition = 0f, lastValue = 0f;
        var points = new List<Vector2>();

        for (int stageIndex = 0; stageIndex < stages.Count; stageIndex++)
        {
            StageData stage = stages[stageIndex];
            if (stage == null || stage.normalItemDrops == null || stage.normalItemDrops.items == null) continue;
            float orderPosition = stageIndex + 1f;
            foreach (StageNormalItemSetting setting in stage.normalItemDrops.items)
            {
                if (setting == null || setting.itemData != item) continue;
                float probability = Mathf.Clamp(item.rarity, 0f, 100f) + Mathf.Clamp(setting.stageModifier, -100f, 100f);
                points.Add(new Vector2(orderPosition, Mathf.Clamp(probability, 0f, 100f)));
            }
        }
        if (points.Count == 0) return 0f;

        points.Sort((a, b) => a.x.CompareTo(b.x));
        firstPosition = points[0].x;
        firstValue = points[0].y;
        previousPosition = firstPosition;
        previousValue = firstValue;
        lastPosition = points[points.Count - 1].x;
        lastValue = points[points.Count - 1].y;
        if (currentOrderPosition < firstPosition)
        {
            float ramp = Mathf.Max(0.0001f, entryRampStages);
            float t = Mathf.Clamp01(1f - (firstPosition - currentOrderPosition) / ramp);
            return firstValue * t;
        }

        for (int i = 1; i < points.Count; i++)
        {
            Vector2 next = points[i];
            if (currentOrderPosition <= next.x)
            {
                float distance = next.x - previousPosition;
                float t = distance <= Mathf.Epsilon ? 1f : Mathf.Clamp01((currentOrderPosition - previousPosition) / distance);
                return Mathf.Lerp(previousValue, next.y, t);
            }
            previousPosition = next.x;
            previousValue = next.y;
        }

        if (currentOrderPosition <= lastPosition) return lastValue;
        float decay = Mathf.Clamp01(falloffPerStage);
        return lastValue * Mathf.Pow(decay, currentOrderPosition - lastPosition);
    }
}
