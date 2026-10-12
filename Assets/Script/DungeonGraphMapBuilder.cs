using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>Places authored room prefabs, connects them as a graph, and paints separate floor/wall maps.</summary>
public static class DungeonGraphMapBuilder
{
    private const int MaxLayoutRetryDepth = 2;
    private const int MaxMapDimension = 512;
    private const long MaxMapCellCount = (long)MaxMapDimension * MaxMapDimension;
    private const int MaxWindingWaypointAttempts = 6;

    private sealed class PlacedRoom
    {
        public DungeonRoomDefinition template;
        public DungeonRoomSpawnRule rule;
        public bool isGuaranteed;
        public BoundsInt sourceBounds;
        public Vector2Int offset;
        public RectInt bounds;
        public bool[] usedExits;
        public bool[] selectedExits;
        public Vector2 Center => new Vector2(bounds.center.x, bounds.center.y);
    }

    private static int layoutRetryDepth;
    private static int paintedLayoutVersion;

    private struct ConnectionCandidate
    {
        public int roomA;
        public int roomB;
        public int exitA;
        public int exitB;
        public float cost;
    }

    private struct CorridorAttachmentCandidate
    {
        public int exit;
        public Vector2Int target;
        public Vector2Int endDirection;
        public float cost;
    }

    public static bool Generate(int mapWidth, int mapHeight, DungeonRoomLibrary library,
        int placementPadding, Tilemap floorMap, Tilemap wallMap, Tilemap trapMap, TileBase defaultFloor,
        List<GroundTileWeight> groundTileWeights, bool randomizeRoomFloors,
        TileBase defaultWall, int seed, Transform roomObjectsParent, List<GameObject> generatedObjects,
        int targetRoomCount, float roomEncounterMultiplier, float maxRoomDensity, int corridorWidth,
        int maxExtraConnections, float branchChance, int maxRoomLinkDistance,
        int maxCorridorTurns, float windingChance,
        int outsideFloorPadding, int removedRoomRetryThreshold)
    {
        if (library == null || floorMap == null || wallMap == null) return false;
        int width = Mathf.Max(20, mapWidth);
        int height = Mathf.Max(20, mapHeight);
        if (width > MaxMapDimension || height > MaxMapDimension || (long)width * height > MaxMapCellCount)
        {
            Debug.LogError($"マップ生成を中止しました。クラッシュ防止の上限は {MaxMapDimension}×{MaxMapDimension} マスです（指定: {width}×{height}）。", library);
            return false;
        }
        System.Random random = new System.Random(seed);
        LogInvalidTemplates(library);
        List<DungeonRoomSpawnRule> allRules = GetValidRules(library);
        if (allRules.Count == 0)
        {
            Debug.LogError("Room Library に有効な部屋ルールがありません。", library);
            return false;
        }

        int corridorWidthClamped = Mathf.Clamp(corridorWidth, 3, 9);
        int edgePadding = corridorWidthClamped + corridorWidthClamped + 2;
        List<PlacedRoom> rooms = PlaceRooms(width, height, library, placementPadding, edgePadding, random,
            targetRoomCount, roomEncounterMultiplier, maxRoomDensity, allRules);
        if (rooms == null) return false;
        int guaranteedRoomsPlaced = CountGuaranteedRooms(rooms);
        if (rooms.Count < 2)
        {
            Debug.LogError($"2部屋以上を配置できませんでした（{rooms.Count}部屋）。部屋Prefabとマップ寸法を確認してください。", library);
            return false;
        }

        bool[,] blocked = new bool[width, height];
        bool[,] roomClearance = new bool[width, height];
        bool[,] corridorPathBlocked = new bool[width, height];
        int corridorRadius = Mathf.Clamp(corridorWidth, 3, 9) / 2;
        int corridorEdgeMargin = GetCorridorEdgeMargin(corridorWidth);
        bool[,] floor = new bool[width, height];
        bool[,] corridorCenterline = new bool[width, height];
        TileBase[,] roomFloor = new TileBase[width, height];
        TileBase[,] roomWall = new TileBase[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                corridorPathBlocked[x, y] = x < corridorEdgeMargin || y < corridorEdgeMargin ||
                    x >= width - corridorEdgeMargin || y >= height - corridorEdgeMargin;
        foreach (PlacedRoom room in rooms)
        {
            CopyRoom(room, width, height, floor, roomFloor, roomWall);
            SealUnusedRoomExits(room, width, height, roomWall, defaultWall);
            for (int x = room.bounds.xMin; x < room.bounds.xMax; x++)
                for (int y = room.bounds.yMin; y < room.bounds.yMax; y++)
                    if (Inside(x, y, width, height)) blocked[x, y] = true;
            RectInt clearance = Expand(room.bounds, 1);
            for (int x = clearance.xMin; x < clearance.xMax; x++)
                for (int y = clearance.yMin; y < clearance.yMax; y++)
                    if (Inside(x, y, width, height)) roomClearance[x, y] = true;
            RectInt corridorBuffer = Expand(room.bounds, corridorRadius + 1);
            for (int x = corridorBuffer.xMin; x < corridorBuffer.xMax; x++)
                for (int y = corridorBuffer.yMin; y < corridorBuffer.yMax; y++)
                    if (Inside(x, y, width, height)) corridorPathBlocked[x, y] = true;
        }

        // Preserve the complete room layout and retry only corridor routing if a random
        // route order strands many rooms. Keep the attempt that connects the most rooms.
        List<PlacedRoom> plannedRooms = new List<PlacedRoom>(rooms);
        List<bool[]> initialUsedExits = new List<bool[]>(plannedRooms.Count);
        List<bool[]> initialSelectedExits = new List<bool[]>(plannedRooms.Count);
        foreach (PlacedRoom room in plannedRooms)
        {
            initialUsedExits.Add((bool[])room.usedExits.Clone());
            initialSelectedExits.Add((bool[])room.selectedExits.Clone());
        }

        bool[,] baseBlocked = CloneGrid(blocked);
        bool[,] baseRoomClearance = CloneGrid(roomClearance);
        bool[,] baseCorridorPathBlocked = CloneGrid(corridorPathBlocked);
        bool[,] baseFloor = CloneGrid(floor);
        bool[,] baseCorridorCenterline = CloneGrid(corridorCenterline);
        TileBase[,] baseRoomFloor = CloneGrid(roomFloor);
        TileBase[,] baseRoomWall = CloneGrid(roomWall);

        int bestConnectedCount = -1;
        bool bestTopologyComplete = false;
        List<PlacedRoom> bestRooms = null;
        List<bool[]> bestUsedExits = null;
        List<bool[]> bestSelectedExits = null;
        bool[,] bestBlocked = null;
        bool[,] bestRoomClearance = null;
        bool[,] bestCorridorPathBlocked = null;
        bool[,] bestFloor = null;
        bool[,] bestCorridorCenterline = null;
        TileBase[,] bestRoomFloor = null;
        TileBase[,] bestRoomWall = null;
        int connectionAttempts = 0;

        const int maxConnectionAttempts = 3;
        for (int attempt = 0; attempt < maxConnectionAttempts; attempt++)
        {
            if (attempt > 0 && bestTopologyComplete && bestConnectedCount >= plannedRooms.Count - 2) break;

            CopyGrid(baseBlocked, blocked);
            CopyGrid(baseRoomClearance, roomClearance);
            CopyGrid(baseCorridorPathBlocked, corridorPathBlocked);
            CopyGrid(baseFloor, floor);
            CopyGrid(baseCorridorCenterline, corridorCenterline);
            CopyGrid(baseRoomFloor, roomFloor);
            CopyGrid(baseRoomWall, roomWall);
            rooms.Clear();
            rooms.AddRange(plannedRooms);

            System.Random routeRandom = attempt == 0 ? random :
                new System.Random(unchecked(seed + 104729 * attempt));
            for (int i = 0; i < plannedRooms.Count; i++)
            {
                PlacedRoom room = plannedRooms[i];
                if (attempt == 0)
                {
                    Array.Copy(initialUsedExits[i], room.usedExits, room.usedExits.Length);
                    Array.Copy(initialSelectedExits[i], room.selectedExits, room.selectedExits.Length);
                }
                else
                {
                    room.usedExits = CreateExitUsageMask(room.template, routeRandom, out bool[] selected);
                    room.selectedExits = selected;
                    ApplyRoomExitWallMask(room, width, height, roomWall, defaultWall);
                }
            }

            bool topologyComplete = ConnectAllRooms(rooms, blocked, roomClearance, corridorPathBlocked,
                floor, corridorCenterline, width, height, library, routeRandom, roomFloor, defaultFloor,
                defaultWall, corridorWidth, roomWall, maxExtraConnections, branchChance, maxRoomLinkDistance,
                maxCorridorTurns, windingChance);
            connectionAttempts++;
            if (rooms.Count > bestConnectedCount ||
                (rooms.Count == bestConnectedCount && topologyComplete && !bestTopologyComplete))
            {
                bestConnectedCount = rooms.Count;
                bestTopologyComplete = topologyComplete;
                bestRooms = new List<PlacedRoom>(rooms);
                bestUsedExits = new List<bool[]>(plannedRooms.Count);
                bestSelectedExits = new List<bool[]>(plannedRooms.Count);
                foreach (PlacedRoom room in plannedRooms)
                {
                    bestUsedExits.Add((bool[])room.usedExits.Clone());
                    bestSelectedExits.Add((bool[])room.selectedExits.Clone());
                }
                bestBlocked = CloneGrid(blocked);
                bestRoomClearance = CloneGrid(roomClearance);
                bestCorridorPathBlocked = CloneGrid(corridorPathBlocked);
                bestFloor = CloneGrid(floor);
                bestCorridorCenterline = CloneGrid(corridorCenterline);
                bestRoomFloor = CloneGrid(roomFloor);
                bestRoomWall = CloneGrid(roomWall);
            }

            if (bestConnectedCount == plannedRooms.Count && bestTopologyComplete) break;
        }

        rooms.Clear();
        rooms.AddRange(bestRooms);
        for (int i = 0; i < plannedRooms.Count; i++)
        {
            Array.Copy(bestUsedExits[i], plannedRooms[i].usedExits, plannedRooms[i].usedExits.Length);
            Array.Copy(bestSelectedExits[i], plannedRooms[i].selectedExits, plannedRooms[i].selectedExits.Length);
        }
        CopyGrid(bestBlocked, blocked);
        CopyGrid(bestRoomClearance, roomClearance);
        CopyGrid(bestCorridorPathBlocked, corridorPathBlocked);
        CopyGrid(bestFloor, floor);
        CopyGrid(bestCorridorCenterline, corridorCenterline);
        CopyGrid(bestRoomFloor, roomFloor);
        CopyGrid(bestRoomWall, roomWall);

        int retiredRoomCount = plannedRooms.Count - bestConnectedCount;
        bool allRoomsConnected = bestTopologyComplete && retiredRoomCount == 0;
        bool retryNeeded = !bestTopologyComplete || retiredRoomCount > Mathf.Max(0, removedRoomRetryThreshold);
        if (retryNeeded && layoutRetryDepth < MaxLayoutRetryDepth)
        {
            int nextSeed = unchecked(seed + 104729 * (layoutRetryDepth + 1));
            string retryReason = !bestTopologyComplete
                ? "保証部屋の接続に失敗したため"
                : $"削除された部屋がしきい値 {Mathf.Max(0, removedRoomRetryThreshold)} を超えたため";
            Debug.LogWarning($"{retryReason}、部屋配置から別レイアウトを試します（削除 {retiredRoomCount}部屋）。", library);
            PaintGeneratedMap(width, height, floor, roomFloor, roomWall, floorMap, wallMap, trapMap,
                defaultFloor, groundTileWeights, randomizeRoomFloors, defaultWall, random, outsideFloorPadding);
            if (!randomizeRoomFloors)
                PaintRoomTrapTiles(rooms, width, height, trapMap);
            int paintedVersionBeforeRetry = paintedLayoutVersion;
            int generatedObjectCountBeforeRetry = generatedObjects != null ? generatedObjects.Count : 0;
            layoutRetryDepth++;
            bool retryProducedMap;
            bool retryThrew = false;
            try
            {
                retryProducedMap = Generate(mapWidth, mapHeight, library, placementPadding,
                    floorMap, wallMap, trapMap, defaultFloor, groundTileWeights, randomizeRoomFloors,
                    defaultWall, nextSeed, roomObjectsParent, generatedObjects, targetRoomCount,
                    roomEncounterMultiplier, maxRoomDensity, corridorWidth, maxExtraConnections, branchChance,
                    maxRoomLinkDistance, maxCorridorTurns, windingChance, outsideFloorPadding,
                    removedRoomRetryThreshold);
            }
            catch (Exception exception)
            {
                retryThrew = true;
                Debug.LogError($"別レイアウトの再試行に失敗しました。現在の生成結果を使います。\n{exception}", library);
                if (generatedObjects != null)
                {
                    for (int i = generatedObjects.Count - 1; i >= generatedObjectCountBeforeRetry; i--)
                    {
                        if (generatedObjects[i] != null) UnityEngine.Object.Destroy(generatedObjects[i]);
                        generatedObjects.RemoveAt(i);
                    }
                }
                retryProducedMap = false;
            }
            finally
            {
                layoutRetryDepth--;
            }
            if (!retryThrew && paintedLayoutVersion != paintedVersionBeforeRetry) return retryProducedMap;
            // The new room layout could not be placed at all. Keep this attempt's valid map.
        }
        if (retiredRoomCount > 0)
            Debug.LogWarning($"通路接続を{connectionAttempts}回試行し、接続できた部屋が最も多い結果を採用しました（{bestConnectedCount}/{plannedRooms.Count}部屋、未接続 {retiredRoomCount}部屋）。", library);
        if (!bestTopologyComplete)
            Debug.LogError("保証部屋をすべて通路ネットワークへ接続できませんでした。接続試行後も保証部屋は削除せず残しています。", library);
        Debug.Log($"部屋生成: {rooms.Count}部屋 / 保証部屋 {guaranteedRoomsPlaced}個を配置しました。", library);
        foreach (PlacedRoom room in rooms)
            CopyRoomObjects(room, width, height, roomObjectsParent, generatedObjects);
        PaintGeneratedMap(width, height, floor, roomFloor, roomWall, floorMap, wallMap, trapMap,
            defaultFloor, groundTileWeights, randomizeRoomFloors, defaultWall, random, outsideFloorPadding);
        if (!randomizeRoomFloors)
            PaintRoomTrapTiles(rooms, width, height, trapMap);
        return allRoomsConnected;
    }

    private static void PaintGeneratedMap(int width, int height, bool[,] floor,
        TileBase[,] roomFloor, TileBase[,] roomWall, Tilemap floorMap, Tilemap wallMap, Tilemap trapMap,
        TileBase defaultFloor, List<GroundTileWeight> groundTileWeights,
        bool randomizeRoomFloors, TileBase defaultWall, System.Random random, int outsideFloorPadding)
    {
        floorMap.ClearAllTiles();
        wallMap.ClearAllTiles();
        if (trapMap != null) trapMap.ClearAllTiles();
        int padding = Mathf.Max(0, outsideFloorPadding);
        int offsetX = width / 2, offsetY = height / 2;
        int paintWidth = width + padding * 2;
        int paintHeight = height + padding * 2;
        TileBase fallbackFloor = defaultFloor != null ? defaultFloor : defaultWall;
        GroundTilePicker groundPicker = new GroundTilePicker(groundTileWeights);
        TileBase[] floorTiles = new TileBase[paintWidth * paintHeight];
        TileBase[] wallTiles = new TileBase[paintWidth * paintHeight];
        TileBase[] trapTiles = trapMap != null ? new TileBase[paintWidth * paintHeight] : null;

        // Keep the same random-number order as before: paint the generated map first,
        // then choose decorative tiles for the outside padding. SetTilesBlock batches
        // thousands of per-cell Unity calls into one update for each Tilemap.
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                int index = (x + padding) + (y + padding) * paintWidth;
                GroundTileWeight selected = groundPicker.Choose(random);
                TileBase groundTile = selected != null ? selected.tile : fallbackFloor;
                floorTiles[index] = groundTile;
                bool mapBoundary = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                if (mapBoundary)
                {
                    if (selected != null && selected.tilemap == StageTilemapLayer.Trap)
                        floorTiles[index] = fallbackFloor;
                    wallTiles[index] = defaultWall;
                    continue;
                }

                if (floor[x, y])
                {
                    bool preserveRoomFloor = !randomizeRoomFloors && roomFloor[x, y] != null;
                    if (preserveRoomFloor)
                    {
                        floorTiles[index] = roomFloor[x, y];
                    }
                    else if (selected != null && selected.tilemap == StageTilemapLayer.Trap)
                    {
                        floorTiles[index] = fallbackFloor;
                        if (trapTiles != null) trapTiles[index] = selected.tile;
                    }
                    wallTiles[index] = roomWall[x, y];
                }
                else
                {
                    wallTiles[index] = roomWall[x, y] != null ? roomWall[x, y] : defaultWall;
                }
            }

        for (int x = 0; x < paintWidth; x++)
            for (int y = 0; y < paintHeight; y++)
            {
                if (x >= padding && x < padding + width &&
                    y >= padding && y < padding + height) continue;
                int index = x + y * paintWidth;
                GroundTileWeight selected = groundPicker.Choose(random);
                if (selected == null || selected.tilemap == StageTilemapLayer.Floor)
                    floorTiles[index] = selected != null ? selected.tile : fallbackFloor;
                else if (trapTiles != null)
                    trapTiles[index] = selected.tile;
            }

        BoundsInt paintBounds = new BoundsInt(-offsetX - padding, -offsetY - padding, 0,
            paintWidth, paintHeight, 1);
        floorMap.SetTilesBlock(paintBounds, floorTiles);
        wallMap.SetTilesBlock(paintBounds, wallTiles);
        if (trapMap != null) trapMap.SetTilesBlock(paintBounds, trapTiles);
        paintedLayoutVersion++;
    }

    private static void PaintRoomTrapTiles(List<PlacedRoom> rooms, int width, int height, Tilemap trapMap)
    {
        if (trapMap == null || rooms == null) return;
        foreach (PlacedRoom room in rooms)
        {
            Tilemap source = room != null && room.template != null ? room.template.trapTilemap : null;
            if (source == null) continue;
            foreach (Vector3Int sourceCell in source.cellBounds.allPositionsWithin)
            {
                TileBase tile = source.GetTile(sourceCell);
                int x = room.offset.x + sourceCell.x - width / 2;
                int y = room.offset.y + sourceCell.y - height / 2;
                if (tile != null && x >= -width / 2 && x < width - width / 2 &&
                    y >= -height / 2 && y < height - height / 2)
                    trapMap.SetTile(new Vector3Int(x, y, sourceCell.z), tile);
            }
        }
    }

    private sealed class GroundTilePicker
    {
        private readonly List<GroundTileWeight> choices = new List<GroundTileWeight>();
        private float totalWeight;

        public GroundTilePicker(List<GroundTileWeight> source)
        {
            if (source == null) return;
            foreach (GroundTileWeight choice in source)
            {
                if (choice == null || choice.tile == null || choice.weight <= 0f) continue;
                choices.Add(choice);
                totalWeight += choice.weight;
            }
        }

        public GroundTileWeight Choose(System.Random random)
        {
            if (choices.Count == 0 || totalWeight <= 0f) return null;
            double selection = random.NextDouble() * totalWeight;
            foreach (GroundTileWeight choice in choices)
            {
                selection -= choice.weight;
                if (selection < 0f) return choice;
            }
            return null;
        }
    }

    private static void CopyRoomObjects(PlacedRoom room, int width, int height,
        Transform parent, List<GameObject> generatedObjects)
    {
        if (parent == null || generatedObjects == null) return;

        Vector3 position = new Vector3(room.offset.x - width / 2, room.offset.y - height / 2, 0f);
        GameObject instance = UnityEngine.Object.Instantiate(room.template.gameObject, position, Quaternion.identity);
        List<Transform> objectsToKeep = new List<Transform>();
        CollectNonTilemapSubtrees(instance.transform, objectsToKeep);

        foreach (Transform roomObject in objectsToKeep)
        {
            roomObject.SetParent(parent, true);
            generatedObjects.Add(roomObject.gameObject);
        }
        UnityEngine.Object.Destroy(instance);
    }

    private static void CollectNonTilemapSubtrees(Transform parent, List<Transform> results)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child.GetComponentInChildren<Tilemap>(true) == null)
                results.Add(child);
            else
                CollectNonTilemapSubtrees(child, results);
        }
    }

    private static List<DungeonRoomSpawnRule> GetValidRules(DungeonRoomLibrary library)
    {
        List<DungeonRoomSpawnRule> result = new List<DungeonRoomSpawnRule>();
        if (library == null || library.rooms == null) return result;
        foreach (DungeonRoomSpawnRule rule in library.rooms)
            if (IsValid(rule)) result.Add(rule);
        return result;
    }

    private static void LogInvalidTemplates(DungeonRoomLibrary library)
    {
        if (library.rooms == null) return;
        for (int i = 0; i < library.rooms.Count; i++)
        {
            DungeonRoomSpawnRule rule = library.rooms[i];
            string reason = null;
            if (rule == null) reason = "部屋ルールが空です";
            else if (rule.prefab == null) reason = "Prefab参照が空です";
            else if (rule.prefab.floorTilemap == null) reason = "floorTilemapが未設定です";
            else if (rule.prefab.wallTilemap == null) reason = "wallTilemapが未設定です";
            else if (rule.prefab.exits == null || rule.prefab.exits.Count < 2) reason = "出入口アンカーが2個未満です";
            else if (!rule.prefab.TryGetBounds(out _)) reason = "床・壁Tilemapにタイルがありません";
            if (reason != null)
            {
                string name = rule != null && rule.prefab != null ? rule.prefab.name : $"index {i}";
                Debug.LogWarning($"Room Library の rooms[{i}]「{name}」は使用できません: {reason}。", library);
            }
        }
    }

    private static bool IsValid(DungeonRoomSpawnRule rule)
    {
        DungeonRoomDefinition template = rule != null ? rule.prefab : null;
        return template != null && template.floorTilemap != null && template.wallTilemap != null &&
               template.exits != null && template.exits.Count >= 2 && template.TryGetBounds(out _);
    }

    private static int CountGuaranteedRooms(List<PlacedRoom> rooms)
    {
        int count = 0;
        foreach (PlacedRoom room in rooms) if (room.isGuaranteed) count++;
        return count;
    }

    private static List<PlacedRoom> PlaceRooms(int width, int height, DungeonRoomLibrary library,
        int padding, int edgePadding, System.Random random, int targetRoomCount,
        float roomEncounterMultiplier, float maxRoomDensity,
        List<DungeonRoomSpawnRule> rules)
    {
        List<PlacedRoom> placed = new List<PlacedRoom>();
        int maximumRoomArea = Mathf.RoundToInt(width * height * Mathf.Clamp(maxRoomDensity, 0.05f, 0.8f));
        int currentRoomArea = 0;
        padding = Mathf.Max(1, padding);
        edgePadding = Mathf.Max(padding, edgePadding);

        List<DungeonRoomSpawnRule> required = new List<DungeonRoomSpawnRule>();
        foreach (DungeonRoomSpawnRule rule in rules)
            for (int i = 0; i < Mathf.Max(0, rule.guaranteedCount); i++) required.Add(rule);

        int baseTarget = Mathf.Clamp(targetRoomCount, 2, 60);
        float encounterMultiplier = Mathf.Clamp(roomEncounterMultiplier, 1f, 2f);
        int target = Mathf.Max(Mathf.Clamp(Mathf.CeilToInt(baseTarget * encounterMultiplier), 2, 60),
            required.Count);
        HashSet<DungeonRoomDefinition> guaranteedPrefabs = new HashSet<DungeonRoomDefinition>();
        foreach (DungeonRoomSpawnRule rule in required) guaranteedPrefabs.Add(rule.prefab);

        // Reserve the center start room for a non-guaranteed room type whenever possible.
        // Required rooms are placed afterward at random map positions.
        List<DungeonRoomSpawnRule> startCandidates = new List<DungeonRoomSpawnRule>();
        foreach (DungeonRoomSpawnRule rule in rules)
            if (!guaranteedPrefabs.Contains(rule.prefab)) startCandidates.Add(rule);
        DungeonRoomSpawnRule startRule = ChooseStartRule(startCandidates, random);
        if (startRule == null)
            startRule = ChooseStartRule(rules, random);
        if (startRule == null) return placed;

        if (!TryPlaceRoom(startRule, true, true, width, height, padding, edgePadding,
                random, placed, ref currentRoomArea, maximumRoomArea))
        {
            Debug.LogError($"開始部屋「{startRule.prefab.name}」をマップ中央に配置できません。", library);
            return null;
        }
        int requiredStartIndex = required.IndexOf(startRule);
        bool startIsGuaranteed = requiredStartIndex >= 0;
        placed[0].isGuaranteed = startIsGuaranteed;
        if (startIsGuaranteed) required.RemoveAt(requiredStartIndex);

        foreach (DungeonRoomSpawnRule rule in required)
        {
            if (TryPlaceRoom(rule, false, false, width, height, padding, edgePadding,
                    random, placed, ref currentRoomArea, maximumRoomArea))
            {
                placed[placed.Count - 1].isGuaranteed = true;
                continue;
            }

            Debug.LogError($"保証部屋「{rule.prefab.name}」を必要数配置できませんでした。マップサイズ・部屋間隔・保証数を確認してください。", library);
            return null;
        }

        // Do not stop the entire fill because one randomly selected large room no
        // longer fits. Exhaust room types individually and keep trying smaller layouts.
        List<DungeonRoomSpawnRule> remainingRules = new List<DungeonRoomSpawnRule>(rules);
        while (placed.Count < target && remainingRules.Count > 0)
        {
            DungeonRoomSpawnRule rule = ChooseWeighted(remainingRules, random);
            if (rule == null) break;
            if (TryPlaceRoom(rule, false, true, width, height, padding, edgePadding,
                    random, placed, ref currentRoomArea, maximumRoomArea)) continue;
            remainingRules.Remove(rule);
        }

        if (placed.Count < target)
        {
            Debug.LogWarning($"部屋配置数を {placed.Count}/{target} に減らしました。マップが狭いか、部屋Prefabが大きすぎます。保証部屋はすべて配置されています。", library);
        }
        return placed;
    }

    private static bool TryPlaceRoom(DungeonRoomSpawnRule rule, bool center, bool enforceDensity,
        int width, int height, int padding, int edgePadding, System.Random random,
        List<PlacedRoom> placed, ref int currentRoomArea, int maximumRoomArea)
    {
        DungeonRoomDefinition template = rule.prefab;
        if (!template.TryGetBounds(out BoundsInt local)) return false;
        int minX = edgePadding - local.xMin;
        int maxX = width - edgePadding - local.xMax;
        int minY = edgePadding - local.yMin;
        int maxY = height - edgePadding - local.yMax;
        if (maxX < minX || maxY < minY) return false;

        for (int attempt = 0; attempt < (center ? 1 : 240); attempt++)
        {
            Vector2Int offset = center
                ? new Vector2Int(width / 2, height / 2)
                : new Vector2Int(random.Next(minX, maxX + 1), random.Next(minY, maxY + 1));
            if (offset.x < minX || offset.x > maxX || offset.y < minY || offset.y > maxY) return false;
            RectInt bounds = new RectInt(offset.x + local.xMin, offset.y + local.yMin,
                local.size.x, local.size.y);
            int area = bounds.width * bounds.height;
            if (enforceDensity && currentRoomArea + area > maximumRoomArea && placed.Count >= 2) return false;
            RectInt padded = Expand(bounds, padding);
            bool overlaps = false;
            foreach (PlacedRoom previous in placed)
                if (padded.Overlaps(previous.bounds)) { overlaps = true; break; }
            if (overlaps) continue;

            bool[] usedExitMask = CreateExitUsageMask(template, random, out bool[] selectedExitMask);
            placed.Add(new PlacedRoom
            {
                template = template,
                rule = rule,
                sourceBounds = local,
                offset = offset,
                bounds = bounds,
                usedExits = usedExitMask,
                selectedExits = selectedExitMask
            });
            currentRoomArea += area;
            return true;
        }
        return false;
    }

    private static bool[] CreateExitUsageMask(DungeonRoomDefinition template, System.Random random, out bool[] selected)
    {
        int exitCount = template.exits != null ? template.exits.Count : 0;
        bool[] used = new bool[exitCount];
        selected = new bool[exitCount];
        List<int> exitOrder = new List<int>(exitCount);
        for (int i = 0; i < exitCount; i++) exitOrder.Add(i);
        for (int i = exitOrder.Count - 1; i > 0; i--)
        {
            int swap = random.Next(i + 1);
            int value = exitOrder[i];
            exitOrder[i] = exitOrder[swap];
            exitOrder[swap] = value;
        }

        int availableExitCount = Mathf.Min(Mathf.Clamp(template.usableExitCount, 1, 4), exitOrder.Count);
        for (int i = 0; i < exitOrder.Count; i++)
        {
            int exit = exitOrder[i];
            selected[exit] = i < availableExitCount;
            used[exit] = !selected[exit];
        }
        return used;
    }

    private static DungeonRoomSpawnRule ChooseStartRule(List<DungeonRoomSpawnRule> allRules,
        System.Random random)
    {
        List<DungeonRoomSpawnRule> spawnable = new List<DungeonRoomSpawnRule>();
        foreach (DungeonRoomSpawnRule rule in allRules)
            if (rule.prefab.floorTilemap.HasTile(Vector3Int.zero)) spawnable.Add(rule);
        return ChooseWeighted(spawnable.Count > 0 ? spawnable : allRules, random);
    }

    private static DungeonRoomSpawnRule ChooseWeighted(List<DungeonRoomSpawnRule> rules, System.Random random)
    {
        if (rules == null || rules.Count == 0) return null;
        float totalWeight = 0f;
        foreach (DungeonRoomSpawnRule rule in rules)
            if (rule != null) totalWeight += Mathf.Max(0f, rule.spawnWeight);
        float roll = (float)random.NextDouble();
        if (totalWeight <= 0f)
            return rules[Mathf.Clamp(Mathf.FloorToInt(roll * rules.Count), 0, rules.Count - 1)];

        float choice = roll * totalWeight;
        foreach (DungeonRoomSpawnRule rule in rules)
        {
            if (rule == null) continue;
            float weight = Mathf.Max(0f, rule.spawnWeight);
            if (weight <= 0f) continue;
            choice -= weight;
            if (choice < 0f) return rule;
        }
        for (int i = rules.Count - 1; i >= 0; i--)
            if (rules[i] != null && rules[i].spawnWeight > 0f) return rules[i];
        return null;
    }

    private static RectInt Expand(RectInt rectangle, int distance)
    {
        return new RectInt(rectangle.xMin - distance, rectangle.yMin - distance,
            rectangle.width + distance * 2, rectangle.height + distance * 2);
    }

    private static void CopyRoom(PlacedRoom room, int width, int height, bool[,] floor,
        TileBase[,] roomFloor, TileBase[,] roomWall)
    {
        CopyLayer(room.template.floorTilemap, room.offset, width, height, floor, roomFloor, true);
        CopyLayer(room.template.wallTilemap, room.offset, width, height, floor, roomWall, false);
    }

    private static void SealUnusedRoomExits(PlacedRoom room, int width, int height,
        TileBase[,] roomWall, TileBase defaultWall)
    {
        TileBase sealingTile = defaultWall;
        if (sealingTile == null)
        {
            foreach (Vector3Int sourceCell in room.template.wallTilemap.cellBounds.allPositionsWithin)
            {
                sealingTile = room.template.wallTilemap.GetTile(sourceCell);
                if (sealingTile != null) break;
            }
        }
        if (sealingTile == null) return;

        for (int i = 0; i < room.selectedExits.Length; i++)
        {
            if (room.selectedExits[i]) continue;
            Vector2Int cell = room.offset + room.template.exits[i].cell;
            if (Inside(cell.x, cell.y, width, height)) roomWall[cell.x, cell.y] = sealingTile;
        }
    }

    private static void CopyLayer(Tilemap source, Vector2Int offset, int width, int height,
        bool[,] floor, TileBase[,] destination, bool isFloor)
    {
        foreach (Vector3Int cell in source.cellBounds.allPositionsWithin)
        {
            TileBase tile = source.GetTile(cell);
            int x = offset.x + cell.x;
            int y = offset.y + cell.y;
            if (tile == null || !Inside(x, y, width, height)) continue;
            destination[x, y] = tile;
            if (isFloor) floor[x, y] = true;
        }
    }

    private static void ClearRoomFootprint(PlacedRoom room, bool[,] floor,
        TileBase[,] roomFloor, TileBase[,] roomWall, int width, int height)
    {
        for (int x = room.bounds.xMin; x < room.bounds.xMax; x++)
            for (int y = room.bounds.yMin; y < room.bounds.yMax; y++)
            {
                if (!Inside(x, y, width, height)) continue;
                floor[x, y] = false;
                roomFloor[x, y] = null;
                roomWall[x, y] = null;
            }
    }

    private static void RebuildPathMasks(List<PlacedRoom> rooms, bool[] activeRooms,
        bool[,] corridorCenterline, bool[,] blocked, bool[,] roomClearance,
        bool[,] corridorPathBlocked, int corridorRadius, int corridorWidth,
        int width, int height)
    {
        int edgeMargin = GetCorridorEdgeMargin(corridorWidth);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                blocked[x, y] = false;
                roomClearance[x, y] = false;
                corridorPathBlocked[x, y] = x < edgeMargin || y < edgeMargin ||
                    x >= width - edgeMargin || y >= height - edgeMargin;
            }

        for (int i = 0; i < rooms.Count; i++)
        {
            if (!activeRooms[i]) continue;
            RectInt bounds = rooms[i].bounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                    if (Inside(x, y, width, height)) blocked[x, y] = true;

            RectInt clearance = Expand(bounds, 1);
            for (int x = clearance.xMin; x < clearance.xMax; x++)
                for (int y = clearance.yMin; y < clearance.yMax; y++)
                    if (Inside(x, y, width, height)) roomClearance[x, y] = true;

            RectInt corridorBuffer = Expand(bounds, corridorRadius + 1);
            for (int x = corridorBuffer.xMin; x < corridorBuffer.xMax; x++)
                for (int y = corridorBuffer.yMin; y < corridorBuffer.yMax; y++)
                    if (Inside(x, y, width, height)) corridorPathBlocked[x, y] = true;
        }

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (corridorCenterline[x, y])
                    MarkCorridorKeepoutCell(new Vector2Int(x, y), corridorPathBlocked,
                        corridorWidth, width, height);
    }

    private static T[,] CloneGrid<T>(T[,] source)
    {
        T[,] clone = new T[source.GetLength(0), source.GetLength(1)];
        Array.Copy(source, clone, source.Length);
        return clone;
    }

    private static void CopyGrid<T>(T[,] source, T[,] destination)
    {
        Array.Copy(source, destination, source.Length);
    }

    private static bool ConnectAllRooms(List<PlacedRoom> rooms, bool[,] blocked, bool[,] roomClearance,
        bool[,] corridorPathBlocked, bool[,] floor, bool[,] corridorCenterline,
        int width, int height, DungeonRoomLibrary library, System.Random random,
        TileBase[,] roomFloor, TileBase defaultFloor, TileBase defaultWall, int corridorWidth, TileBase[,] roomWall,
        int maxExtraConnections,
        float branchChance, int maxRoomLinkDistance, int maxCorridorTurns, float windingChance)
    {
        HashSet<int> connected = new HashSet<int> { 0 };
        HashSet<long> treeEdges = new HashSet<long>();
        bool[] activeRooms = new bool[rooms.Count];
        for (int i = 0; i < activeRooms.Length; i++) activeRooms[i] = true;
        int activeRoomCount = rooms.Count;
        int corridorRadius = Mathf.Clamp(corridorWidth, 3, 9) / 2;
        bool failedToConnectGuaranteedRoom = false;
        List<Vector2Int> corridorCells = GetCorridorCells(corridorCenterline, width, height);
        while (connected.Count < activeRoomCount)
        {
            bool hasUnconnectedGuaranteedRoom = false;
            for (int i = 0; i < rooms.Count; i++)
                if (activeRooms[i] && rooms[i].isGuaranteed && !connected.Contains(i))
                {
                    hasUnconnectedGuaranteedRoom = true;
                    break;
                }

            // Prefer direct branches from nearby existing hallways. Connecting a room
            // to the corridor network spends only that room's selected entrance and avoids
            // using entrances to chain rooms together when a hallway junction is available.
            if (corridorCells.Count > 0)
            {
                List<int> attachmentOrder = new List<int>();
                Dictionary<int, int> corridorDistances = new Dictionary<int, int>();
                for (int roomIndex = 0; roomIndex < rooms.Count; roomIndex++)
                {
                    if (!activeRooms[roomIndex] || connected.Contains(roomIndex) ||
                        !HasFreeExit(rooms[roomIndex])) continue;
                    if (hasUnconnectedGuaranteedRoom && !rooms[roomIndex].isGuaranteed) continue;
                    attachmentOrder.Add(roomIndex);
                    corridorDistances[roomIndex] = DistanceToNearestCorridor(rooms[roomIndex], corridorCells);
                }
                attachmentOrder.Sort((a, b) => corridorDistances[a].CompareTo(corridorDistances[b]));

                bool attachedToCorridor = false;
                foreach (int roomIndex in attachmentOrder)
                {
                    if (!TryConnectRoomToCorridorAnyExit(rooms[roomIndex], blocked, roomClearance,
                            corridorPathBlocked, floor, corridorCenterline, corridorCells, width, height,
                            roomFloor, roomWall, defaultFloor, defaultWall, corridorWidth, maxCorridorTurns,
                            windingChance, random)) continue;
                    connected.Add(roomIndex);
                    attachedToCorridor = true;
                    break;
                }
                if (attachedToCorridor) continue;
            }

            List<ConnectionCandidate> candidates = new List<ConnectionCandidate>();
            foreach (int a in connected)
            {
                if (!HasFreeExit(rooms[a])) continue;
                for (int b = 0; b < rooms.Count; b++)
                {
                    if (!activeRooms[b] || connected.Contains(b) || !HasFreeExit(rooms[b])) continue;
                    if (hasUnconnectedGuaranteedRoom && !rooms[b].isGuaranteed) continue;
                    float roomDistance = Vector2.Distance(rooms[a].Center, rooms[b].Center);
                    if (connected.Count > 1 && roomDistance > maxRoomLinkDistance) continue;
                    Vector2 direction = (rooms[b].Center - rooms[a].Center).normalized;
                    for (int exitA = 0; exitA < rooms[a].usedExits.Length; exitA++)
                    {
                        if (rooms[a].usedExits[exitA]) continue;
                        for (int exitB = 0; exitB < rooms[b].usedExits.Length; exitB++)
                        {
                            if (rooms[b].usedExits[exitB]) continue;
                            float alignment = Vector2.Dot(rooms[a].template.exits[exitA].DirectionVector, direction) +
                                              Vector2.Dot(rooms[b].template.exits[exitB].DirectionVector, -direction);
                            candidates.Add(new ConnectionCandidate
                            {
                                roomA = a,
                                roomB = b,
                                exitA = exitA,
                                exitB = exitB,
                                cost = roomDistance +
                                       (2f - alignment) * 3f + (float)random.NextDouble() * 0.5f
                            });
                        }
                    }
                }
            }

            candidates.Sort((left, right) => left.cost.CompareTo(right.cost));
            bool linked = false;
            foreach (ConnectionCandidate candidate in candidates)
            {
                if (!TryConnect(rooms[candidate.roomA], candidate.exitA,
                        rooms[candidate.roomB], candidate.exitB, blocked, roomClearance,
                        corridorPathBlocked, floor, corridorCenterline, corridorCells, width, height,
                        roomFloor, roomWall, defaultFloor,
                        corridorWidth, maxCorridorTurns, windingChance, random)) continue;

                connected.Add(candidate.roomB);
                treeEdges.Add(EdgeKey(candidate.roomA, candidate.roomB));
                linked = true;
                break;
            }

            if (!linked)
            {
                // Attach the nearest remaining room to an existing corridor network.
                // This avoids forcing a long room-to-room route when a nearby hallway exists.
                for (int roomIndex = 0; roomIndex < rooms.Count; roomIndex++)
                {
                    if (!activeRooms[roomIndex] || connected.Contains(roomIndex)) continue;
                    if (hasUnconnectedGuaranteedRoom && !rooms[roomIndex].isGuaranteed) continue;
                    if (!TryConnectRoomToCorridorAnyExit(rooms[roomIndex], blocked, roomClearance,
                            corridorPathBlocked, floor, corridorCenterline, corridorCells, width, height,
                            roomFloor, roomWall, defaultFloor, defaultWall, corridorWidth, maxCorridorTurns,
                            windingChance, random)) continue;

                    connected.Add(roomIndex);
                    linked = true;
                    break;
                }

                if (!linked)
                {
                    // Drop one unreachable room, clear its footprint, then rebuild the free
                    // space so later corridors can use the newly opened area.
                    int roomToRemove = -1;
                    for (int i = rooms.Count - 1; i >= 0; i--)
                        if (activeRooms[i] && !connected.Contains(i) && !rooms[i].isGuaranteed)
                        {
                            roomToRemove = i;
                            break;
                        }
                    if (roomToRemove < 0)
                    {
                        failedToConnectGuaranteedRoom = hasUnconnectedGuaranteedRoom;
                        break;
                    }

                    ClearRoomFootprint(rooms[roomToRemove], floor, roomFloor, roomWall, width, height);
                    activeRooms[roomToRemove] = false;
                    activeRoomCount--;
                    RebuildPathMasks(rooms, activeRooms, corridorCenterline, blocked,
                        roomClearance, corridorPathBlocked, corridorRadius, corridorWidth, width, height);
                }
            }
        }

        int addedConnections = 0;
        // Prioritize T-junctions from rooms into nearby halls. This gives long passages
        // useful branches before optional room-to-room loops consume the branch budget.
        List<int> branchRooms = new List<int>();
        for (int i = 0; i < rooms.Count; i++)
            if (activeRooms[i] && HasFreeExit(rooms[i])) branchRooms.Add(i);
        Shuffle(branchRooms, random);
        foreach (int roomIndex in branchRooms)
        {
            if (addedConnections >= maxExtraConnections) break;
            if (random.NextDouble() >= branchChance) continue;
            if (TryConnectRoomToCorridor(rooms[roomIndex], blocked, roomClearance,
                corridorPathBlocked, floor, corridorCenterline, corridorCells, width, height,
                roomFloor, roomWall, defaultFloor, corridorWidth, maxCorridorTurns,
                windingChance, random))
                addedConnections++;
        }

        List<Vector2Int> extras = new List<Vector2Int>();
        for (int a = 0; a < rooms.Count; a++)
        {
            if (!activeRooms[a]) continue;
            for (int b = a + 1; b < rooms.Count; b++)
                if (activeRooms[b] && !treeEdges.Contains(EdgeKey(a, b)) &&
                    Vector2.Distance(rooms[a].Center, rooms[b].Center) <= maxRoomLinkDistance)
                    extras.Add(new Vector2Int(a, b));
        }
        Shuffle(extras, random);

        foreach (Vector2Int extra in extras)
        {
            if (addedConnections >= maxExtraConnections) break;
            if (random.NextDouble() >= branchChance) continue;
            if (!ChooseExits(rooms[extra.x], rooms[extra.y], out int exitA, out int exitB)) continue;
            if (TryConnect(rooms[extra.x], exitA, rooms[extra.y], exitB,
                blocked, roomClearance, corridorPathBlocked, floor, corridorCenterline, corridorCells,
                width, height, roomFloor, roomWall, defaultFloor, corridorWidth,
                maxCorridorTurns, windingChance, random))
                addedConnections++;
        }

        for (int i = rooms.Count - 1; i >= 0; i--)
            if (!activeRooms[i]) rooms.RemoveAt(i);
        return !failedToConnectGuaranteedRoom;
    }

    private static List<Vector2Int> GetCorridorCells(bool[,] corridorCenterline, int width, int height)
    {
        List<Vector2Int> cells = new List<Vector2Int>();
        for (int x = 1; x < width - 1; x++)
            for (int y = 1; y < height - 1; y++)
                if (corridorCenterline[x, y]) cells.Add(new Vector2Int(x, y));
        return cells;
    }

    private static int DistanceToNearestCorridor(PlacedRoom room, List<Vector2Int> corridorCells)
    {
        int roomX = Mathf.RoundToInt(room.Center.x);
        int roomY = Mathf.RoundToInt(room.Center.y);
        int nearest = int.MaxValue;
        foreach (Vector2Int cell in corridorCells)
            nearest = Mathf.Min(nearest, Mathf.Abs(roomX - cell.x) + Mathf.Abs(roomY - cell.y));
        return nearest;
    }

    private static bool TryConnect(PlacedRoom roomA, int exitA, PlacedRoom roomB, int exitB,
        bool[,] blocked, bool[,] roomClearance, bool[,] corridorPathBlocked, bool[,] floor,
        bool[,] corridorCenterline, List<Vector2Int> corridorCells,
        int width, int height, TileBase[,] roomFloor, TileBase[,] roomWall,
        TileBase defaultFloor, int corridorWidth,
        int maxCorridorTurns, float windingChance, System.Random random)
    {
        DungeonRoomExit a = roomA.template.exits[exitA];
        DungeonRoomExit b = roomB.template.exits[exitB];
        Vector2Int doorA = roomA.offset + a.cell;
        Vector2Int doorB = roomB.offset + b.cell;
        // Leave a one-cell buffer around rooms, then widen the corridor away from the doorway.
        // Exit anchors can sit several cells inside a prefab's tilemap bounds. Walk the
        // doorway outward until the corridor centerline clears the room's protected buffer;
        // a fixed offset can leave the search starting inside a blocked region.
        Vector2Int start = FindExitPathStart(doorA, a.DirectionVector, roomA.bounds,
            corridorPathBlocked, corridorWidth, width, height);
        Vector2Int end = FindExitPathStart(doorB, b.DirectionVector, roomB.bounds,
            corridorPathBlocked, corridorWidth, width, height);
        if (!Inside(start.x, start.y, width, height) || !Inside(end.x, end.y, width, height)) return false;
        int minimumStraightCells = Mathf.Clamp(corridorWidth, 3, 9) + 1;
        List<Vector2Int> path = FindPath(start, end, a.DirectionVector, -b.DirectionVector,
            corridorPathBlocked, width, height, maxCorridorTurns, windingChance,
            minimumStraightCells, random);
        if (path == null) return false;

        Carve(floor, roomFloor, roomWall, doorA, a.DirectionVector, path, corridorWidth,
            start, roomA.bounds, blocked, roomClearance, defaultFloor, width, height);
        Carve(floor, roomFloor, roomWall, doorB, b.DirectionVector, path, corridorWidth,
            end, roomB.bounds, blocked, roomClearance, defaultFloor, width, height);
        MarkCorridorCenterline(path, corridorCenterline, corridorCells);
        MarkDoorwayCenterline(doorA, a.DirectionVector, start, corridorCenterline, corridorCells);
        MarkDoorwayCenterline(doorB, b.DirectionVector, end, corridorCenterline, corridorCells);
        MarkCorridorKeepout(path, corridorPathBlocked, corridorWidth, width, height);
        MarkDoorwayKeepout(doorA, a.DirectionVector, start, corridorPathBlocked,
            corridorWidth, width, height);
        MarkDoorwayKeepout(doorB, b.DirectionVector, end, corridorPathBlocked,
            corridorWidth, width, height);
        roomA.usedExits[exitA] = true;
        roomB.usedExits[exitB] = true;
        return true;
    }

    private static bool TryConnectRoomToCorridor(PlacedRoom room, bool[,] roomBlocked,
        bool[,] roomClearance, bool[,] corridorPathBlocked, bool[,] floor,
        bool[,] corridorCenterline, List<Vector2Int> corridorCells, int width, int height,
        TileBase[,] roomFloor, TileBase[,] roomWall,
        TileBase defaultFloor, int corridorWidth, int maxCorridorTurns,
        float windingChance, System.Random random, int maxCandidateAttempts = 128)
    {
        List<CorridorAttachmentCandidate> candidates = new List<CorridorAttachmentCandidate>();
        int candidateLimit = Mathf.Max(1, maxCandidateAttempts);
        int corridorSize = Mathf.Clamp(corridorWidth, 3, 9);
        for (int exit = 0; exit < room.usedExits.Length; exit++)
        {
            if (room.usedExits[exit]) continue;
            DungeonRoomExit roomExit = room.template.exits[exit];
            Vector2Int door = room.offset + roomExit.cell;
            Vector2Int start = FindExitPathStart(door, roomExit.DirectionVector, room.bounds,
                corridorPathBlocked, corridorWidth, width, height);
            if (!Inside(start.x, start.y, width, height)) continue;

            foreach (Vector2Int corridorCell in corridorCells)
            {
                foreach (Vector2Int outward in CardinalDirections)
                {
                    // Place the new corridor centerline one corridor-width away. Its
                    // footprint then meets the existing corridor edge without overlap.
                    Vector2Int target = corridorCell + outward * corridorSize;
                    if (!Inside(target.x, target.y, width, height) ||
                        target.x == 0 || target.y == 0 || target.x == width - 1 || target.y == height - 1)
                        continue;

                    // Only the cheapest candidates are ever routed below. Once the
                    // bounded list is full, skip distant cells before the relatively
                    // expensive footprint checks. This avoids building and sorting a
                    // potentially huge list for every room attached to the corridor net.
                    float distanceCost = Mathf.Abs(target.x - (int)room.Center.x) +
                                         Mathf.Abs(target.y - (int)room.Center.y);
                    if (candidates.Count == candidateLimit &&
                        distanceCost > candidates[candidates.Count - 1].cost + 0.1f) continue;
                    if (!CorridorFootprintClearOfRooms(target, corridorSize, roomBlocked,
                            roomClearance, width, height) ||
                        !CorridorFootprintSeparatedFromFloor(target, corridorSize, floor,
                            width, height, 0)) continue;

                    CorridorAttachmentCandidate attachment = new CorridorAttachmentCandidate
                    {
                        exit = exit,
                        target = target,
                        endDirection = -outward,
                        cost = distanceCost + (float)random.NextDouble() * 0.1f
                    };
                    InsertAttachmentCandidate(candidates, attachment, candidateLimit);
                }
            }
        }

        int candidatesToTry = candidates.Count;
        int minimumStraightCells = corridorSize + 1;
        // Only the final straight approach is opened temporarily, so reuse the source
        // grid instead of cloning up to 512x512 cells for every room attachment attempt.
        bool[,] attachmentBlocked = corridorPathBlocked;
        Vector2Int[] temporarilyOpened = new Vector2Int[minimumStraightCells + 1];
        bool[] previousBlockedState = new bool[minimumStraightCells + 1];
        for (int i = 0; i < candidatesToTry; i++)
        {
            CorridorAttachmentCandidate candidate = candidates[i];
            DungeonRoomExit roomExit = room.template.exits[candidate.exit];
            Vector2Int door = room.offset + roomExit.cell;
            Vector2Int start = FindExitPathStart(door, roomExit.DirectionVector, room.bounds,
                corridorPathBlocked, corridorWidth, width, height);
            if (!Inside(start.x, start.y, width, height)) continue;

            // Open only the final straight approach to this deliberate junction. Its cells
            // remain one tile from the old corridor until the terminal footprint touches it.
            bool approachIsClear = true;
            int openedCount = 0;
            Vector2Int approachStep = -candidate.endDirection;
            for (int step = 0; step <= minimumStraightCells; step++)
            {
                Vector2Int approachCell = candidate.target + approachStep * step;
                int allowedGap = step == 0 ? 0 : 1;
                if (!Inside(approachCell.x, approachCell.y, width, height) ||
                    !CorridorFootprintClearOfRooms(approachCell, corridorSize, roomBlocked,
                        roomClearance, width, height) ||
                    !CorridorFootprintSeparatedFromFloor(approachCell, corridorSize, floor,
                        width, height, allowedGap))
                {
                    approachIsClear = false;
                    break;
                }
                temporarilyOpened[openedCount] = approachCell;
                previousBlockedState[openedCount] = attachmentBlocked[approachCell.x, approachCell.y];
                openedCount++;
                attachmentBlocked[approachCell.x, approachCell.y] = false;
            }
            List<Vector2Int> path = approachIsClear
                ? FindPath(start, candidate.target, roomExit.DirectionVector,
                    candidate.endDirection, attachmentBlocked, width, height, maxCorridorTurns,
                    windingChance, minimumStraightCells, random)
                : null;
            for (int opened = 0; opened < openedCount; opened++)
            {
                Vector2Int point = temporarilyOpened[opened];
                attachmentBlocked[point.x, point.y] = previousBlockedState[opened];
            }

            if (path == null)
            {
                continue;
            }

            Carve(floor, roomFloor, roomWall, door, roomExit.DirectionVector, path, corridorWidth,
                start, room.bounds, roomBlocked, roomClearance, defaultFloor, width, height);
            MarkCorridorCenterline(path, corridorCenterline, corridorCells);
            MarkDoorwayCenterline(door, roomExit.DirectionVector, start, corridorCenterline, corridorCells);
            MarkCorridorKeepout(path, corridorPathBlocked, corridorWidth, width, height);
            MarkDoorwayKeepout(door, roomExit.DirectionVector, start, corridorPathBlocked,
                corridorWidth, width, height);
            room.usedExits[candidate.exit] = true;
            return true;
        }
        return false;
    }

    private static void InsertAttachmentCandidate(List<CorridorAttachmentCandidate> candidates,
        CorridorAttachmentCandidate candidate, int limit)
    {
        int low = 0;
        int high = candidates.Count;
        while (low < high)
        {
            int middle = (low + high) >> 1;
            if (candidates[middle].cost <= candidate.cost) low = middle + 1;
            else high = middle;
        }

        if (low >= limit) return;
        candidates.Insert(low, candidate);
        if (candidates.Count > limit)
            candidates.RemoveAt(candidates.Count - 1);
    }

    private static bool TryConnectRoomToCorridorAnyExit(PlacedRoom room, bool[,] roomBlocked,
        bool[,] roomClearance, bool[,] corridorPathBlocked, bool[,] floor,
        bool[,] corridorCenterline, List<Vector2Int> corridorCells, int width, int height,
        TileBase[,] roomFloor, TileBase[,] roomWall,
        TileBase defaultFloor, TileBase defaultWall, int corridorWidth, int maxCorridorTurns,
        float windingChance, System.Random random)
    {
        if (TryConnectRoomToCorridor(room, roomBlocked, roomClearance, corridorPathBlocked,
                floor, corridorCenterline, corridorCells, width, height, roomFloor, roomWall, defaultFloor,
                corridorWidth, maxCorridorTurns, windingChance, random, 64)) return true;

        bool[] originalSelected = (bool[])room.selectedExits.Clone();
        bool[] originalUsed = (bool[])room.usedExits.Clone();
        List<int> selected = new List<int>();
        List<int> alternatives = new List<int>();
        for (int i = 0; i < originalSelected.Length; i++)
        {
            if (originalSelected[i]) selected.Add(i);
            else alternatives.Add(i);
        }
        if (selected.Count == 0 || alternatives.Count == 0) return false;
        Shuffle(selected, random);
        Shuffle(alternatives, random);

        foreach (int oldExit in selected)
        foreach (int newExit in alternatives)
        {
            Array.Copy(originalSelected, room.selectedExits, originalSelected.Length);
            Array.Copy(originalUsed, room.usedExits, originalUsed.Length);
            room.selectedExits[oldExit] = false;
            room.usedExits[oldExit] = true;
            room.selectedExits[newExit] = true;
            room.usedExits[newExit] = false;
            ApplyRoomExitWallMask(room, width, height, roomWall, defaultWall);

            if (TryConnectRoomToCorridor(room, roomBlocked, roomClearance, corridorPathBlocked,
                    floor, corridorCenterline, corridorCells, width, height, roomFloor, roomWall, defaultFloor,
                    corridorWidth, maxCorridorTurns, windingChance, random, 32)) return true;
        }

        Array.Copy(originalSelected, room.selectedExits, originalSelected.Length);
        Array.Copy(originalUsed, room.usedExits, originalUsed.Length);
        ApplyRoomExitWallMask(room, width, height, roomWall, defaultWall);
        return false;
    }

    private static void ApplyRoomExitWallMask(PlacedRoom room, int width, int height,
        TileBase[,] roomWall, TileBase defaultWall)
    {
        TileBase sealingTile = defaultWall;
        if (sealingTile == null)
        {
            foreach (Vector3Int sourceCell in room.template.wallTilemap.cellBounds.allPositionsWithin)
            {
                sealingTile = room.template.wallTilemap.GetTile(sourceCell);
                if (sealingTile != null) break;
            }
        }
        if (sealingTile == null) return;

        for (int i = 0; i < room.selectedExits.Length; i++)
        {
            Vector2Int cell = room.offset + room.template.exits[i].cell;
            if (!Inside(cell.x, cell.y, width, height)) continue;
            roomWall[cell.x, cell.y] = room.selectedExits[i] ? null : sealingTile;
        }
    }

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
    };

    private static bool CorridorFootprintClearOfRooms(Vector2Int center, int corridorWidth,
        bool[,] roomBlocked, bool[,] roomClearance, int mapWidth, int mapHeight)
    {
        int min = -(corridorWidth - 1) / 2;
        int max = corridorWidth / 2;
        for (int dx = min; dx <= max; dx++)
            for (int dy = min; dy <= max; dy++)
            {
                int x = center.x + dx, y = center.y + dy;
                if (!Inside(x, y, mapWidth, mapHeight) || x == 0 || y == 0 ||
                    x == mapWidth - 1 || y == mapHeight - 1 ||
                    roomBlocked[x, y] || roomClearance[x, y])
                    return false;
            }
        return true;
    }

    private static bool CorridorFootprintSeparatedFromFloor(Vector2Int center, int corridorWidth,
        bool[,] existingFloor, int mapWidth, int mapHeight, int minimumGap)
    {
        int min = -(corridorWidth - 1) / 2 - minimumGap;
        int max = corridorWidth / 2 + minimumGap;
        for (int dx = min; dx <= max; dx++)
            for (int dy = min; dy <= max; dy++)
            {
                int x = center.x + dx, y = center.y + dy;
                if (!Inside(x, y, mapWidth, mapHeight) || existingFloor[x, y]) return false;
            }
        return true;
    }

    private static void MarkCorridorCenterline(List<Vector2Int> path, bool[,] corridorCenterline,
        List<Vector2Int> corridorCells)
    {
        foreach (Vector2Int point in path)
            AddCorridorCell(point, corridorCenterline, corridorCells);
    }

    private static void MarkDoorwayCenterline(Vector2Int door, Vector2Int direction,
        Vector2Int pathEndpoint, bool[,] corridorCenterline, List<Vector2Int> corridorCells)
    {
        int distance = Mathf.Abs(pathEndpoint.x - door.x) + Mathf.Abs(pathEndpoint.y - door.y);
        for (int step = 0; step <= distance; step++)
        {
            Vector2Int point = door + direction * step;
            AddCorridorCell(point, corridorCenterline, corridorCells);
        }
    }

    private static void AddCorridorCell(Vector2Int point, bool[,] corridorCenterline,
        List<Vector2Int> corridorCells)
    {
        if (corridorCenterline[point.x, point.y]) return;
        corridorCenterline[point.x, point.y] = true;
        corridorCells.Add(point);
    }

    private static Vector2Int FindExitPathStart(Vector2Int door, Vector2Int direction,
        RectInt roomBounds, bool[,] corridorPathBlocked, int corridorWidth, int width, int height)
    {
        RectInt ownRoomBuffer = Expand(roomBounds, Mathf.Clamp(corridorWidth, 3, 9) / 2 + 1);
        int maxDistance = roomBounds.width + roomBounds.height + corridorWidth * 2 + 4;
        for (int distance = 1; distance <= maxDistance; distance++)
        {
            Vector2Int point = door + direction * distance;
            if (!Inside(point.x, point.y, width, height)) return new Vector2Int(-1, -1);
            bool inOwnBuffer = ownRoomBuffer.Contains(point);
            if (inOwnBuffer) continue;
            // Do not tunnel through another room's protection to reach open space.
            return corridorPathBlocked[point.x, point.y] ? new Vector2Int(-1, -1) : point;
        }
        return new Vector2Int(-1, -1);
    }

    private static void MarkDoorwayKeepout(Vector2Int door, Vector2Int direction,
        Vector2Int pathEndpoint, bool[,] blocked, int corridorWidth, int mapWidth, int mapHeight)
    {
        int distance = Mathf.Abs(pathEndpoint.x - door.x) + Mathf.Abs(pathEndpoint.y - door.y);
        for (int step = 0; step <= distance; step++)
            MarkCorridorKeepoutCell(door + direction * step, blocked, corridorWidth, mapWidth, mapHeight);
    }

    private static void MarkCorridorKeepout(List<Vector2Int> path, bool[,] blocked,
        int corridorWidth, int mapWidth, int mapHeight)
    {
        foreach (Vector2Int center in path)
            MarkCorridorKeepoutCell(center, blocked, corridorWidth, mapWidth, mapHeight);
    }

    private static void MarkCorridorKeepoutCell(Vector2Int center, bool[,] blocked,
        int corridorWidth, int mapWidth, int mapHeight)
    {
        // Reserve each corridor's full width plus one empty tile before another may pass.
        int width = Mathf.Clamp(corridorWidth, 3, 9);
        int footprintRadius = Mathf.Max((width - 1) / 2, width / 2);
        int keepoutRadius = footprintRadius * 2 + 1;
        for (int x = center.x - keepoutRadius; x <= center.x + keepoutRadius; x++)
            for (int y = center.y - keepoutRadius; y <= center.y + keepoutRadius; y++)
                if (Inside(x, y, mapWidth, mapHeight)) blocked[x, y] = true;
    }

    private static int GetCorridorEdgeMargin(int corridorWidth)
    {
        int width = Mathf.Clamp(corridorWidth, 3, 9);
        int footprintRadius = Mathf.Max((width - 1) / 2, width / 2);
        // Keep the full corridor footprint away from the perimeter wall so it cannot
        // be clipped by the map edge or the boundary collision tile.
        return footprintRadius + 1;
    }

    private static bool ChooseExits(PlacedRoom a, PlacedRoom b, out int exitA, out int exitB)
    {
        exitA = -1;
        exitB = -1;
        Vector2 direction = (b.Center - a.Center).normalized;
        float best = float.MinValue;
        for (int i = 0; i < a.template.exits.Count; i++)
        {
            if (a.usedExits[i]) continue;
            float aScore = Vector2.Dot(a.template.exits[i].DirectionVector, direction);
            for (int j = 0; j < b.template.exits.Count; j++)
            {
                if (b.usedExits[j]) continue;
                float score = aScore + Vector2.Dot(b.template.exits[j].DirectionVector, -direction);
                if (score <= best) continue;
                best = score;
                exitA = i;
                exitB = j;
            }
        }
        return exitA >= 0 && exitB >= 0;
    }

    private static bool HasFreeExit(PlacedRoom room)
    {
        for (int i = 0; i < room.usedExits.Length; i++)
            if (!room.usedExits[i]) return true;
        return false;
    }

    private static long EdgeKey(int a, int b)
    {
        int low = Mathf.Min(a, b);
        int high = Mathf.Max(a, b);
        return ((long)low << 32) | (uint)high;
    }

    private static List<Vector2Int> FindPath(Vector2Int start, Vector2Int end,
        Vector2Int startDirection, Vector2Int endDirection,
        bool[,] blocked, int width, int height, int maxTurns, float windingChance,
        int minimumStraightCells, System.Random random)
    {
        // Prefer the direct one-turn routes first. Winding routes are optional variety and
        // must not replace a valid simple route just because their chance roll succeeded.
        bool horizontalFirst = random.Next(2) == 0;
        Vector2Int corner = horizontalFirst
            ? new Vector2Int(end.x, start.y)
            : new Vector2Int(start.x, end.y);
        if (maxTurns >= 1 && TryBuildAxisPath(start, corner, end, blocked, width, height, out List<Vector2Int> direct) &&
            IsCorridorPathValid(direct, startDirection, endDirection, maxTurns, minimumStraightCells))
            return direct;
        Vector2Int alternateCorner = horizontalFirst
            ? new Vector2Int(start.x, end.y)
            : new Vector2Int(end.x, start.y);
        if (maxTurns >= 1 && TryBuildAxisPath(start, alternateCorner, end, blocked, width, height, out direct) &&
            IsCorridorPathValid(direct, startDirection, endDirection, maxTurns, minimumStraightCells))
            return direct;

        if (maxTurns >= 2 && random.NextDouble() < windingChance &&
            TryBuildWindingPath(start, end, blocked, width, height, random, out List<Vector2Int> winding) &&
            IsCorridorPathValid(winding, startDirection, endDirection, maxTurns, minimumStraightCells))
            return winding;

        // Use constrained grid search only after both direct routes and the optional
        // winding attempt fail their obstacle and minimum-straight-run checks.
        List<Vector2Int> constrained = FindConstrainedPath(start, end, startDirection,
            endDirection, blocked, width, height, maxTurns, minimumStraightCells);
        return constrained;
    }

    private static List<Vector2Int> FindConstrainedPath(Vector2Int start, Vector2Int end,
        Vector2Int startDirection, Vector2Int endDirection, bool[,] blocked,
        int width, int height, int maxTurns, int minimumStraightCells)
    {
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        int turnStateCount = Mathf.Max(0, maxTurns) + 1;
        int runStateCount = minimumStraightCells + 1;
        int startState = EncodePathState(start, 4, 0, 0, width, turnStateCount, runStateCount);
        Dictionary<int, int> previous = new Dictionary<int, int> { [startState] = startState };
        Queue<int> queue = new Queue<int>();
        queue.Enqueue(startState);
        int endState = -1;

        while (queue.Count > 0 && endState < 0)
        {
            int state = queue.Dequeue();
            DecodePathState(state, width, turnStateCount, runStateCount, out Vector2Int point,
                out int previousDirection, out int straightRun, out int turns);

            for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
            {
                if (previousDirection == 4 && directions[directionIndex] != startDirection) continue;

                int nextTurns = turns;
                int nextRun;
                if (previousDirection == 4 || directionIndex == previousDirection)
                {
                    nextRun = Mathf.Min(minimumStraightCells, straightRun + 1);
                }
                else
                {
                    bool isReverse = (directionIndex + 2) % 4 == previousDirection;
                    if (isReverse || straightRun < minimumStraightCells || turns >= maxTurns) continue;
                    nextTurns++;
                    nextRun = 1;
                }

                Vector2Int next = point + directions[directionIndex];
                if (!Inside(next.x, next.y, width, height) || blocked[next.x, next.y]) continue;
                bool reachedEnd = next == end;
                if (reachedEnd && (directions[directionIndex] != endDirection ||
                                   nextRun < minimumStraightCells)) continue;

                int nextState = EncodePathState(next, directionIndex, nextRun, nextTurns,
                    width, turnStateCount, runStateCount);
                if (previous.ContainsKey(nextState)) continue;
                previous[nextState] = state;
                if (reachedEnd)
                {
                    endState = nextState;
                    break;
                }
                queue.Enqueue(nextState);
            }
        }

        if (endState < 0) return null;
        List<Vector2Int> path = new List<Vector2Int>();
        int cursor = endState;
        while (cursor != startState)
        {
            DecodePathState(cursor, width, turnStateCount, runStateCount,
                out Vector2Int point, out _, out _, out _);
            path.Add(point);
            cursor = previous[cursor];
        }
        path.Add(start);
        path.Reverse();
        return path;
    }

    private static int EncodePathState(Vector2Int point, int direction, int straightRun,
        int turns, int width, int turnStateCount, int runStateCount)
    {
        int cell = point.y * width + point.x;
        return (((cell * 5 + direction) * runStateCount + straightRun) * turnStateCount) + turns;
    }

    private static void DecodePathState(int state, int width, int turnStateCount, int runStateCount,
        out Vector2Int point, out int direction, out int straightRun, out int turns)
    {
        turns = state % turnStateCount;
        state /= turnStateCount;
        straightRun = state % runStateCount;
        state /= runStateCount;
        direction = state % 5;
        int cell = state / 5;
        point = new Vector2Int(cell % width, cell / width);
    }

    private static bool TryBuildWindingPath(Vector2Int start, Vector2Int end, bool[,] blocked,
        int width, int height, System.Random random, out List<Vector2Int> winding)
    {
        for (int attempt = 0; attempt < MaxWindingWaypointAttempts; attempt++)
        {
            int minX = Mathf.Min(start.x, end.x), maxX = Mathf.Max(start.x, end.x);
            int minY = Mathf.Min(start.y, end.y), maxY = Mathf.Max(start.y, end.y);
            if (maxX == minX || maxY == minY) break;
            Vector2Int waypoint = new Vector2Int(random.Next(minX, maxX + 1), random.Next(minY, maxY + 1));
            if (blocked[waypoint.x, waypoint.y]) continue;
            Vector2Int firstCorner = random.Next(2) == 0
                ? new Vector2Int(waypoint.x, start.y)
                : new Vector2Int(start.x, waypoint.y);
            Vector2Int secondCorner = random.Next(2) == 0
                ? new Vector2Int(end.x, waypoint.y)
                : new Vector2Int(waypoint.x, end.y);
            List<Vector2Int> candidate = new List<Vector2Int>();
            if (TryBuildAxisPath(start, firstCorner, waypoint, blocked, width, height, out List<Vector2Int> first) &&
                TryBuildAxisPath(waypoint, secondCorner, end, blocked, width, height, out List<Vector2Int> second))
            {
                candidate.AddRange(first);
                foreach (Vector2Int point in second)
                    if (candidate.Count == 0 || candidate[candidate.Count - 1] != point) candidate.Add(point);
                if (!HasMinimumStraightRunAfterTurns(candidate, 3)) continue;
                winding = candidate;
                return true;
            }
        }
        winding = null;
        return false;
    }

    private static bool TryBuildAxisPath(Vector2Int start, Vector2Int corner, Vector2Int end,
        bool[,] blocked, int width, int height, out List<Vector2Int> path)
    {
        path = new List<Vector2Int>();
        if (!AppendStraightSegment(start, corner, blocked, width, height, path) ||
            !AppendStraightSegment(corner, end, blocked, width, height, path))
        {
            path = null;
            return false;
        }
        if (!HasMinimumStraightRunAfterTurns(path, 3))
        {
            path = null;
            return false;
        }
        return path.Count > 0;
    }

    private static bool HasMinimumStraightRunAfterTurns(List<Vector2Int> path, int minimumCells)
    {
        if (path == null || path.Count < 2) return false;

        Vector2Int previousDirection = path[1] - path[0];
        int straightCells = 1;
        bool hasTurned = false;
        for (int i = 2; i < path.Count; i++)
        {
            Vector2Int direction = path[i] - path[i - 1];
            if (direction == previousDirection)
            {
                straightCells++;
                continue;
            }

            // Each segment that ends at a corner must be at least three cells long.
            if (straightCells < minimumCells) return false;
            hasTurned = true;
            previousDirection = direction;
            straightCells = 1;
        }

        // Also prevent a route from making a turn and immediately ending at the room.
        return !hasTurned || straightCells >= minimumCells;
    }

    private static bool IsCorridorPathValid(List<Vector2Int> path, Vector2Int startDirection,
        Vector2Int endDirection, int maximumTurns, int minimumStraightCells)
    {
        if (path == null || path.Count < 2 || path[1] - path[0] != startDirection ||
            path[path.Count - 1] - path[path.Count - 2] != endDirection)
            return false;

        int turns = 0;
        for (int i = 2; i < path.Count; i++)
            if (path[i] - path[i - 1] != path[i - 1] - path[i - 2]) turns++;

        return turns <= maximumTurns && HasMinimumStraightRunAfterTurns(path, minimumStraightCells);
    }

    private static bool AppendStraightSegment(Vector2Int from, Vector2Int to, bool[,] blocked,
        int width, int height, List<Vector2Int> path)
    {
        Vector2Int delta = to - from;
        if (delta.x != 0 && delta.y != 0) return false;
        Vector2Int step = new Vector2Int(Math.Sign(delta.x), Math.Sign(delta.y));
        Vector2Int current = from;
        while (current != to)
        {
            current += step;
            if (!Inside(current.x, current.y, width, height) || blocked[current.x, current.y]) return false;
            if (path.Count == 0 || path[path.Count - 1] != current) path.Add(current);
        }
        return true;
    }

    private static void Carve(bool[,] floor, TileBase[,] roomFloor, TileBase[,] roomWall, Vector2Int door,
        Vector2Int direction, List<Vector2Int> path, int corridorWidth,
        Vector2Int pathEndpoint, RectInt roomBounds, bool[,] roomBlocked, bool[,] roomClearance,
        TileBase defaultFloor, int mapWidth, int mapHeight)
    {
        int width = Mathf.Clamp(corridorWidth, 3, 9);
        int min = -(width - 1) / 2;
        int max = width / 2;
        Vector2Int across = direction.x == 0 ? Vector2Int.right : Vector2Int.up;
        TileBase doorwayTile = roomFloor[door.x, door.y] != null ? roomFloor[door.x, door.y] : defaultFloor;

        // Keep the doorway and its short exit throat at the configured corridor width.
        // The room's one-cell gap is enforced everywhere else; inside the throat, open the
        // prefab wall deliberately but never carve through another room's occupied bounds.
        int distanceToPath = Mathf.Abs(pathEndpoint.x - door.x) + Mathf.Abs(pathEndpoint.y - door.y);
        for (int distance = 0; distance <= distanceToPath; distance++)
        {
            Vector2Int doorwayRow = door + direction * distance;
            if (!Inside(doorwayRow.x, doorwayRow.y, mapWidth, mapHeight)) break;
            for (int offset = min; offset <= max; offset++)
            {
                Vector2Int aperture = doorwayRow + across * offset;
                if (!Inside(aperture.x, aperture.y, mapWidth, mapHeight)) continue;
                if (distance == 0 || roomBounds.Contains(doorwayRow))
                    SetDoorFloor(floor, roomFloor, roomWall, aperture, doorwayTile, mapWidth, mapHeight);
                else
                    SetThroatFloor(floor, aperture, roomBlocked, mapWidth, mapHeight);
            }
        }

        foreach (Vector2Int point in path)
            for (int dx = min; dx <= max; dx++)
                for (int dy = min; dy <= max; dy++)
                    SetFloor(floor, point + new Vector2Int(dx, dy), roomBlocked,
                        roomClearance, mapWidth, mapHeight);
    }

    private static void SetDoorFloor(bool[,] floor, TileBase[,] roomFloor, TileBase[,] roomWall,
        Vector2Int point, TileBase doorwayTile, int width, int height)
    {
        if (!Inside(point.x, point.y, width, height)) return;
        floor[point.x, point.y] = true;
        if (roomFloor[point.x, point.y] == null) roomFloor[point.x, point.y] = doorwayTile;
        // Door apertures intentionally replace prefab wall tiles; interior wall tiles
        // (such as pillars) remain untouched everywhere else.
        roomWall[point.x, point.y] = null;
    }

    private static void SetThroatFloor(bool[,] floor, Vector2Int point, bool[,] roomBlocked,
        int width, int height)
    {
        if (!Inside(point.x, point.y, width, height) || roomBlocked[point.x, point.y]) return;
        floor[point.x, point.y] = true;
    }

    private static void SetFloor(bool[,] floor, Vector2Int point, bool[,] roomBlocked,
        bool[,] roomClearance, int width, int height)
    {
        int x = point.x, y = point.y;
        if (!Inside(x, y, width, height) || roomBlocked[x, y] || roomClearance[x, y]) return;
        floor[x, y] = true;
    }

    private static bool Inside(int x, int y, int width, int height)
    {
        return x >= 0 && y >= 0 && x < width && y < height;
    }

    private static void Shuffle<T>(IList<T> values, System.Random random)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            T temporary = values[i];
            values[i] = values[j];
            values[j] = temporary;
        }
    }
}
