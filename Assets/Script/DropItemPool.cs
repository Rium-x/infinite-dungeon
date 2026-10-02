using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[System.Serializable]
public sealed class StageNormalItemSetting
{
    public ItemData itemData;

    [Range(-100f, 100f)]
    [Tooltip("この階層での確率補正(%)。ItemDataのレアリティに加算します。別の階層の同じアイテム設定との間は滑らかに補間されます。")]
    [FormerlySerializedAs("relativeFrequencyAdjustment")]
    public float stageModifier;
}

[System.Serializable]
public sealed class DropItemPool
{
    [Tooltip("通常アイテム候補。空欄のアイテムは全階層で出現しません。")]
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

/// <summary>Linked stage data is treated as item probability keyframes.</summary>
public static class NormalItemDropCalculator
{
    public static List<StageData> GetStageChain(StageData first)
    {
        var stages = new List<StageData>();
        var visited = new HashSet<StageData>();
        for (StageData stage = first; stage != null && visited.Add(stage); stage = stage.nextStage)
            stages.Add(stage);
        return stages;
    }

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

    public static float GetStageProbability(IList<StageData> stages, ItemData item, float currentPosition,
        float falloffPerPosition, float entryRampDistance)
    {
        if (stages == null || item == null) return 0f;

        float firstPosition = 0f, firstValue = 0f;
        float previousPosition = 0f, previousValue = 0f;
        float lastPosition = 0f, lastValue = 0f;
        var points = new List<Vector2>();

        foreach (StageData stage in stages)
        {
            if (stage == null || stage.normalItemDrops == null || stage.normalItemDrops.items == null) continue;
            float position = GetPosition(stage, stages);
            foreach (StageNormalItemSetting setting in stage.normalItemDrops.items)
            {
                if (setting == null || setting.itemData != item) continue;
                float probability = Mathf.Clamp(item.rarity, 0f, 100f) + Mathf.Clamp(setting.stageModifier, -100f, 100f);
                points.Add(new Vector2(position, Mathf.Clamp(probability, 0f, 100f)));
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
        if (currentPosition < firstPosition)
        {
            float ramp = Mathf.Max(0.0001f, entryRampDistance);
            float t = Mathf.Clamp01(1f - (firstPosition - currentPosition) / ramp);
            return firstValue * t;
        }

        for (int i = 1; i < points.Count; i++)
        {
            Vector2 next = points[i];
            if (currentPosition <= next.x)
            {
                float distance = next.x - previousPosition;
                float t = distance <= Mathf.Epsilon ? 1f : Mathf.Clamp01((currentPosition - previousPosition) / distance);
                return Mathf.Lerp(previousValue, next.y, t);
            }
            previousPosition = next.x;
            previousValue = next.y;
        }

        if (currentPosition <= lastPosition) return lastValue;
        float decay = Mathf.Clamp01(falloffPerPosition);
        return lastValue * Mathf.Pow(decay, currentPosition - lastPosition);
    }

    public static float GetPosition(StageData stage, IList<StageData> chain)
    {
        if (stage.hierarchyPosition != 0f) return stage.hierarchyPosition;
        for (int i = 0; i < chain.Count; i++)
            if (chain[i] == stage) return i + 1f;
        return 1f;
    }
}
