using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DungeonRoomLibrary))]
public sealed class DungeonRoomLibraryEditor : Editor
{
    private static readonly Color[] BarColors =
    {
        new Color(0.25f, 0.65f, 0.95f),
        new Color(0.35f, 0.8f, 0.55f),
        new Color(0.95f, 0.65f, 0.25f),
        new Color(0.8f, 0.4f, 0.75f),
        new Color(0.8f, 0.8f, 0.35f)
    };

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        DungeonRoomLibrary library = (DungeonRoomLibrary)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("部屋の抽選確率", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "全ルールのSpawn Weightを合計し、各ルールの抽選割合を表示します。部屋サイズや特殊カテゴリは抽選確率に影響しません。保証配置分はこのグラフに含みません。開始部屋は、中心セルに床があるルールから同じウェイト方式で選びます。",
            MessageType.Info);

        if (library.rooms == null || library.rooms.Count == 0)
        {
            EditorGUILayout.HelpBox("rooms に部屋ルールを追加してください。", MessageType.Warning);
            return;
        }

        float totalWeight = 0f;
        int validRuleCount = 0;
        foreach (DungeonRoomSpawnRule rule in library.rooms)
        {
            if (!IsValidRule(rule)) continue;
            totalWeight += Mathf.Max(0f, rule.spawnWeight);
            validRuleCount++;
        }
        bool uniformFallback = totalWeight <= 0f && validRuleCount > 0;
        if (uniformFallback) totalWeight = validRuleCount;

        for (int i = 0; i < library.rooms.Count; i++)
        {
            DungeonRoomSpawnRule rule = library.rooms[i];
            if (rule == null) continue;
            if (!IsValidRule(rule))
            {
                EditorGUILayout.LabelField($"{rule.prefab?.name ?? "Prefab未設定"}    対象外（Prefab設定を確認）", EditorStyles.miniLabel);
                continue;
            }
            float weight = uniformFallback ? 1f : Mathf.Max(0f, rule.spawnWeight);
            float probability = totalWeight > 0f ? weight / totalWeight : 0f;
            string roomName = rule.prefab != null ? rule.prefab.name : "Prefab未設定";
            string sizeName = rule.size == DungeonRoomSize.Small ? "小" :
                rule.size == DungeonRoomSize.Large ? "大" : "特殊";

            Rect barRect = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(barRect, new Color(0.16f, 0.16f, 0.16f));
            Rect fillRect = barRect;
            fillRect.width *= probability;
            if (fillRect.width > 0f)
                EditorGUI.DrawRect(fillRect, BarColors[i % BarColors.Length]);

            string chanceText = uniformFallback ? "均等抽選" : $"{probability:P1}";
            EditorGUI.LabelField(barRect, $"{roomName}（{sizeName}）    {chanceText}");
        }
    }

    private static bool IsValidRule(DungeonRoomSpawnRule rule)
    {
        DungeonRoomDefinition room = rule != null ? rule.prefab : null;
        return room != null && room.floorTilemap != null && room.wallTilemap != null &&
               room.exits != null && room.exits.Count >= 2 && room.TryGetBounds(out _);
    }
}
