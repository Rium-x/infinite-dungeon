using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>Places authored room prefabs, connects them as a graph, and paints separate floor/wall maps.</summary>
public static class DungeonGraphMapBuilder
{
    private sealed class PlacedRoom
    {
        public DungeonRoomDefinition template;
        public BoundsInt sourceBounds;
        public Vector2Int offset;
        public RectInt bounds;
        public bool[] usedExits;
        public Vector2 Center => new Vector2(bounds.center.x, bounds.center.y);
    }

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
        int placementPadding, Tilemap floorMap, Tilemap wallMap, TileBase defaultFloor,
        List<GroundTileWeight> groundTileWeights, bool randomizeRoomFloors,
        TileBase defaultWall, int seed, Transform roomObjectsParent, List<GameObject> generatedObjects,
        int targetRoomCount, float maxRoomDensity, float smallRoomChance, int corridorWidth,
        int maxExtraConnections, float branchChance, int maxRoomLinkDistance,
        int maxCorridorTurns, float windingChance,
        float specialRoomChance, int outsideFloorPadding)
    {
        if (library == null || floorMap == null || wallMap == null) return false;
        int width = Mathf.Max(20, mapWidth);
        int height = Mathf.Max(20, mapHeight);
        System.Random random = new System.Random(seed);
        List<DungeonRoomDefinition> normalTemplates = GetValidTemplates(library.smallRooms, library.largeRooms);
        if (normalTemplates.Count == 0) return false;

        int corridorWidthClamped = Mathf.Clamp(corridorWidth, 3, 9);
        int edgePadding = corridorWidthClamped + corridorWidthClamped + 2;
        List<PlacedRoom> rooms = PlaceRooms(width, height, library, placementPadding, edgePadding, random,
            targetRoomCount, maxRoomDensity, smallRoomChance, specialRoomChance);
        if (rooms.Count < 2)
        {
            Debug.LogError($"2部屋以上を配置できませんでした（{rooms.Count}部屋）。部屋Prefabとマップ寸法を確認してください。", library);
            return false;
        }

        bool[,] blocked = new bool[width, height];
        bool[,] roomClearance = new bool[width, height];
        bool[,] corridorPathBlocked = new bool[width, height];
        int corridorRadius = Mathf.Clamp(corridorWidth, 3, 9) / 2;
        bool[,] floor = new bool[width, height];
        bool[,] corridorCenterline = new bool[width, height];
        TileBase[,] roomFloor = new TileBase[width, height];
        TileBase[,] roomWall = new TileBase[width, height];
        for (int x = 0; x < width; x++)
        {
            corridorPathBlocked[x, 0] = true;
            corridorPathBlocked[x, height - 1] = true;
        }
        for (int y = 0; y < height; y++)
        {
            corridorPathBlocked[0, y] = true;
            corridorPathBlocked[width - 1, y] = true;
        }
        foreach (PlacedRoom room in rooms)
        {
            CopyRoom(room, width, height, floor, roomFloor, roomWall);
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

        bool allRoomsConnected = ConnectAllRooms(rooms, blocked, roomClearance, corridorPathBlocked,
            floor, corridorCenterline, width, height, library, random, roomFloor, defaultFloor, corridorWidth,
            roomWall, maxExtraConnections, branchChance, maxRoomLinkDistance, maxCorridorTurns, windingChance);
        foreach (PlacedRoom room in rooms)
            CopyRoomObjects(room, width, height, roomObjectsParent, generatedObjects);
        Paint(width, height, floor, roomFloor, roomWall, floorMap, wallMap, defaultFloor,
            groundTileWeights, randomizeRoomFloors, defaultWall, random);
        PaintOutsideFloor(width, height, Mathf.Max(0, outsideFloorPadding), floorMap,
            defaultFloor, groundTileWeights, random);
        return allRoomsConnected;
    }

    private static void PaintOutsideFloor(int width, int height, int padding,
        Tilemap floorMap, TileBase floorTile, List<GroundTileWeight> groundTileWeights,
        System.Random random)
    {
        if (padding == 0) return;
        int offsetX = width / 2, offsetY = height / 2;
        for (int x = -offsetX - padding; x < width - offsetX + padding; x++)
            for (int y = -offsetY - padding; y < height - offsetY + padding; y++)
            {
                bool insideGeneratedBounds = x >= -offsetX && x < width - offsetX &&
                                             y >= -offsetY && y < height - offsetY;
                if (!insideGeneratedBounds)
                {
                    TileBase tile = ChooseGroundTile(groundTileWeights, floorTile, random);
                    if (tile != null) floorMap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
    }

    private static void CopyRoomObjects(PlacedRoom room, int width, int height,
        Transform parent, List<GameObject> generatedObjects)
    {
        if (parent == null || generatedObjects == null) return;
        bool hasObjects = false;
        for (int i = 0; i < room.template.transform.childCount; i++)
        {
            Transform child = room.template.transform.GetChild(i);
            if (child.GetComponentInChildren<Tilemap>(true) == null)
            {
                hasObjects = true;
                break;
            }
        }
        if (!hasObjects) return;

        Vector3 position = new Vector3(room.offset.x - width / 2, room.offset.y - height / 2, 0f);
        GameObject instance = UnityEngine.Object.Instantiate(room.template.gameObject, position, Quaternion.identity);
        List<Transform> objectsToKeep = new List<Transform>();
        for (int i = 0; i < instance.transform.childCount; i++)
        {
            Transform child = instance.transform.GetChild(i);
            if (child.GetComponentInChildren<Tilemap>(true) == null) objectsToKeep.Add(child);
        }

        foreach (Transform roomObject in objectsToKeep)
        {
            roomObject.SetParent(parent, true);
            generatedObjects.Add(roomObject.gameObject);
        }
        UnityEngine.Object.Destroy(instance);
    }

    private static List<DungeonRoomDefinition> GetValidTemplates(params List<DungeonRoomDefinition>[] sources)
    {
        List<DungeonRoomDefinition> result = new List<DungeonRoomDefinition>();
        foreach (List<DungeonRoomDefinition> source in sources)
        {
            if (source == null) continue;
            foreach (DungeonRoomDefinition template in source)
                if (IsValid(template)) result.Add(template);
        }
        return result;
    }

    private static bool IsValid(DungeonRoomDefinition template)
    {
        return template != null && template.floorTilemap != null && template.wallTilemap != null &&
               template.exits != null && template.exits.Count >= 2 && template.TryGetBounds(out _);
    }

    private static List<PlacedRoom> PlaceRooms(int width, int height, DungeonRoomLibrary library,
        int padding, int edgePadding, System.Random random, int targetRoomCount,
        float maxRoomDensity, float smallRoomChance, float specialRoomChance)
    {
        List<PlacedRoom> placed = new List<PlacedRoom>();
        List<DungeonRoomDefinition> normals = GetValidTemplates(library.smallRooms, library.largeRooms);
        int target = Mathf.Clamp(targetRoomCount, 2, 60);
        int maximumRoomArea = Mathf.RoundToInt(width * height * Mathf.Clamp(maxRoomDensity, 0.05f, 0.8f));
        int currentRoomArea = 0;
        padding = Mathf.Max(1, padding);
        edgePadding = Mathf.Max(padding, edgePadding);

        for (int index = 0; index < target; index++)
        {
            DungeonRoomDefinition template = index == 0
                ? ChooseSpawnTemplate(library, normals)
                : ChooseTemplate(library, normals, random, smallRoomChance, specialRoomChance);
            if (!template.TryGetBounds(out BoundsInt local)) continue;
            bool success = false;

            for (int attempt = 0; attempt < (index == 0 ? 1 : 240); attempt++)
            {
                int minX = edgePadding - local.xMin;
                int maxX = width - edgePadding - local.xMax;
                int minY = edgePadding - local.yMin;
                int maxY = height - edgePadding - local.yMax;
                if (maxX < minX || maxY < minY) break;
                Vector2Int offset = index == 0
                    ? new Vector2Int(width / 2, height / 2)
                    : new Vector2Int(random.Next(minX, maxX + 1), random.Next(minY, maxY + 1));
                if (offset.x < minX || offset.x > maxX || offset.y < minY || offset.y > maxY) break;
                RectInt bounds = new RectInt(offset.x + local.xMin, offset.y + local.yMin,
                    local.size.x, local.size.y);
                if (currentRoomArea + bounds.width * bounds.height > maximumRoomArea && placed.Count >= 2)
                    break;
                RectInt padded = Expand(bounds, padding);
                bool overlaps = false;
                foreach (PlacedRoom previous in placed)
                    if (padded.Overlaps(previous.bounds)) { overlaps = true; break; }
                if (overlaps) continue;

                placed.Add(new PlacedRoom
                {
                    template = template,
                    sourceBounds = local,
                    offset = offset,
                    bounds = bounds,
                    usedExits = new bool[template.exits.Count]
                });
                currentRoomArea += bounds.width * bounds.height;
                success = true;
                break;
            }

            if (!success && placed.Count >= 2) break;
        }

        if (placed.Count < target)
            Debug.LogWarning($"部屋配置数を {placed.Count}/{target} に減らしました。マップが狭いか、部屋Prefabが大きすぎます。", library);
        return placed;
    }

    private static DungeonRoomDefinition ChooseSpawnTemplate(DungeonRoomLibrary library,
        List<DungeonRoomDefinition> normals)
    {
        List<DungeonRoomDefinition> small = GetValidTemplates(library.smallRooms);
        foreach (DungeonRoomDefinition template in small)
            if (template.floorTilemap.HasTile(Vector3Int.zero)) return template;
        foreach (DungeonRoomDefinition template in normals)
            if (template.floorTilemap.HasTile(Vector3Int.zero)) return template;
        return normals[0];
    }

    private static DungeonRoomDefinition ChooseTemplate(DungeonRoomLibrary library,
        List<DungeonRoomDefinition> normals, System.Random random, float smallRoomChance,
        float specialRoomChance)
    {
        if (library.specialRooms != null && library.specialRooms.Count > 0 &&
            random.NextDouble() < specialRoomChance)
        {
            List<DungeonRoomDefinition> specials = GetValidTemplates(library.specialRooms);
            if (specials.Count > 0) return specials[random.Next(specials.Count)];
        }

        bool preferSmall = random.NextDouble() < Mathf.Clamp01(smallRoomChance);
        List<DungeonRoomDefinition> preferred = GetValidTemplates(preferSmall ? library.smallRooms : library.largeRooms);
        if (preferred.Count > 0) return preferred[random.Next(preferred.Count)];
        List<DungeonRoomDefinition> alternate = GetValidTemplates(preferSmall ? library.largeRooms : library.smallRooms);
        return alternate.Count > 0 ? alternate[random.Next(alternate.Count)] : normals[random.Next(normals.Count)];
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
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                blocked[x, y] = false;
                roomClearance[x, y] = false;
                corridorPathBlocked[x, y] = x == 0 || y == 0 || x == width - 1 || y == height - 1;
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

    private static bool ConnectAllRooms(List<PlacedRoom> rooms, bool[,] blocked, bool[,] roomClearance,
        bool[,] corridorPathBlocked, bool[,] floor, bool[,] corridorCenterline,
        int width, int height, DungeonRoomLibrary library, System.Random random,
        TileBase[,] roomFloor, TileBase defaultFloor, int corridorWidth, TileBase[,] roomWall,
        int maxExtraConnections,
        float branchChance, int maxRoomLinkDistance, int maxCorridorTurns, float windingChance)
    {
        HashSet<int> connected = new HashSet<int> { 0 };
        HashSet<long> treeEdges = new HashSet<long>();
        bool[] activeRooms = new bool[rooms.Count];
        for (int i = 0; i < activeRooms.Length; i++) activeRooms[i] = true;
        int activeRoomCount = rooms.Count;
        int corridorRadius = Mathf.Clamp(corridorWidth, 3, 9) / 2;
        while (connected.Count < activeRoomCount)
        {
            List<ConnectionCandidate> candidates = new List<ConnectionCandidate>();
            foreach (int a in connected)
            {
                if (!HasFreeExit(rooms[a])) continue;
                for (int b = 0; b < rooms.Count; b++)
                {
                    if (!activeRooms[b] || connected.Contains(b) || !HasFreeExit(rooms[b])) continue;
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
                        corridorPathBlocked, floor, corridorCenterline, width, height, roomFloor, defaultFloor,
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
                    if (!TryConnectRoomToCorridor(rooms[roomIndex], blocked, roomClearance,
                            corridorPathBlocked, floor, corridorCenterline, width, height,
                            roomFloor, defaultFloor, corridorWidth, maxCorridorTurns,
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
                        if (activeRooms[i] && !connected.Contains(i))
                        {
                            roomToRemove = i;
                            break;
                        }
                    if (roomToRemove < 0) break;

                    ClearRoomFootprint(rooms[roomToRemove], floor, roomFloor, roomWall, width, height);
                    activeRooms[roomToRemove] = false;
                    activeRoomCount--;
                    Debug.LogWarning($"部屋 {roomToRemove + 1} は近くの部屋・通路に接続できなかったため生成対象から除外しました。空いた場所を通路探索に使います。", library);
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
                corridorPathBlocked, floor, corridorCenterline, width, height,
                roomFloor, defaultFloor, corridorWidth, maxCorridorTurns,
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
                blocked, roomClearance, corridorPathBlocked, floor, corridorCenterline, width, height,
                roomFloor, defaultFloor, corridorWidth, maxCorridorTurns, windingChance, random))
                addedConnections++;
        }

        for (int i = rooms.Count - 1; i >= 0; i--)
            if (!activeRooms[i]) rooms.RemoveAt(i);
        return true;
    }

    private static bool TryConnect(PlacedRoom roomA, int exitA, PlacedRoom roomB, int exitB,
        bool[,] blocked, bool[,] roomClearance, bool[,] corridorPathBlocked, bool[,] floor,
        bool[,] corridorCenterline,
        int width, int height, TileBase[,] roomFloor, TileBase defaultFloor, int corridorWidth,
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

        Carve(floor, roomFloor, doorA, a.DirectionVector, path, corridorWidth,
            start, roomA.bounds, blocked, roomClearance, defaultFloor, width, height);
        Carve(floor, roomFloor, doorB, b.DirectionVector, path, corridorWidth,
            end, roomB.bounds, blocked, roomClearance, defaultFloor, width, height);
        MarkCorridorCenterline(path, corridorCenterline);
        MarkDoorwayCenterline(doorA, a.DirectionVector, start, corridorCenterline);
        MarkDoorwayCenterline(doorB, b.DirectionVector, end, corridorCenterline);
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
        bool[,] corridorCenterline, int width, int height, TileBase[,] roomFloor,
        TileBase defaultFloor, int corridorWidth, int maxCorridorTurns,
        float windingChance, System.Random random)
    {
        List<CorridorAttachmentCandidate> candidates = new List<CorridorAttachmentCandidate>();
        int corridorSize = Mathf.Clamp(corridorWidth, 3, 9);
        for (int exit = 0; exit < room.usedExits.Length; exit++)
        {
            if (room.usedExits[exit]) continue;
            DungeonRoomExit roomExit = room.template.exits[exit];
            Vector2Int door = room.offset + roomExit.cell;
            Vector2Int start = FindExitPathStart(door, roomExit.DirectionVector, room.bounds,
                corridorPathBlocked, corridorWidth, width, height);
            if (!Inside(start.x, start.y, width, height)) continue;

            for (int x = 1; x < width - 1; x++)
                for (int y = 1; y < height - 1; y++)
                {
                    if (!corridorCenterline[x, y]) continue;
                    Vector2Int corridorCell = new Vector2Int(x, y);
                    foreach (Vector2Int outward in CardinalDirections)
                    {
                        // Place the new corridor centerline one corridor-width away. Its
                        // footprint then meets the existing corridor edge without overlap.
                        Vector2Int target = corridorCell + outward * corridorSize;
                        if (!Inside(target.x, target.y, width, height) ||
                            target.x == 0 || target.y == 0 || target.x == width - 1 || target.y == height - 1 ||
                            !CorridorFootprintClearOfRooms(target, corridorSize, roomBlocked,
                                roomClearance, width, height) ||
                            !CorridorFootprintSeparatedFromFloor(target, corridorSize, floor,
                                width, height, 0)) continue;
                        float cost = Mathf.Abs(target.x - (int)room.Center.x) +
                                     Mathf.Abs(target.y - (int)room.Center.y) +
                                     (float)random.NextDouble() * 0.1f;
                        candidates.Add(new CorridorAttachmentCandidate
                        {
                            exit = exit,
                            target = target,
                            endDirection = -outward,
                            cost = cost
                        });
                    }
                }
        }

        candidates.Sort((left, right) => left.cost.CompareTo(right.cost));
        int candidatesToTry = Mathf.Min(candidates.Count, 128);
        int minimumStraightCells = corridorSize + 1;
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
            bool[,] attachmentBlocked = (bool[,])corridorPathBlocked.Clone();
            bool approachIsClear = true;
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
                attachmentBlocked[approachCell.x, approachCell.y] = false;
            }
            if (!approachIsClear) continue;
            List<Vector2Int> path = FindPath(start, candidate.target, roomExit.DirectionVector,
                candidate.endDirection, attachmentBlocked, width, height, maxCorridorTurns,
                windingChance, minimumStraightCells, random);
            if (path == null) continue;

            Carve(floor, roomFloor, door, roomExit.DirectionVector, path, corridorWidth,
                start, room.bounds, roomBlocked, roomClearance, defaultFloor, width, height);
            MarkCorridorCenterline(path, corridorCenterline);
            MarkDoorwayCenterline(door, roomExit.DirectionVector, start, corridorCenterline);
            MarkCorridorKeepout(path, corridorPathBlocked, corridorWidth, width, height);
            MarkDoorwayKeepout(door, roomExit.DirectionVector, start, corridorPathBlocked,
                corridorWidth, width, height);
            room.usedExits[candidate.exit] = true;
            return true;
        }
        return false;
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
                if (!Inside(x, y, mapWidth, mapHeight) || roomBlocked[x, y] || roomClearance[x, y])
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

    private static void MarkCorridorCenterline(List<Vector2Int> path, bool[,] corridorCenterline)
    {
        foreach (Vector2Int point in path)
            corridorCenterline[point.x, point.y] = true;
    }

    private static void MarkDoorwayCenterline(Vector2Int door, Vector2Int direction,
        Vector2Int pathEndpoint, bool[,] corridorCenterline)
    {
        int distance = Mathf.Abs(pathEndpoint.x - door.x) + Mathf.Abs(pathEndpoint.y - door.y);
        for (int step = 0; step <= distance; step++)
        {
            Vector2Int point = door + direction * step;
            corridorCenterline[point.x, point.y] = true;
        }
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
        if (maxTurns >= 2 && random.NextDouble() < windingChance &&
            TryBuildWindingPath(start, end, blocked, width, height, random, out List<Vector2Int> winding) &&
            IsCorridorPathValid(winding, startDirection, endDirection, maxTurns, minimumStraightCells))
            return winding;

        // Prefer a single straight run with one right-angle turn. Use constrained grid search
        // when room buffers or existing corridor keep-outs block both simple routes.
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
        if (maxTurns >= 2 && TryBuildWindingPath(start, end, blocked, width, height, random, out winding) &&
            IsCorridorPathValid(winding, startDirection, endDirection, maxTurns, minimumStraightCells))
            return winding;

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
        for (int attempt = 0; attempt < 24; attempt++)
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

    private static void Carve(bool[,] floor, TileBase[,] roomFloor, Vector2Int door,
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
                    SetDoorFloor(floor, roomFloor, aperture, doorwayTile, mapWidth, mapHeight);
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

    private static void SetDoorFloor(bool[,] floor, TileBase[,] roomFloor, Vector2Int point,
        TileBase doorwayTile, int width, int height)
    {
        if (!Inside(point.x, point.y, width, height)) return;
        floor[point.x, point.y] = true;
        if (roomFloor[point.x, point.y] == null) roomFloor[point.x, point.y] = doorwayTile;
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

    private static void Paint(int width, int height, bool[,] floor, TileBase[,] roomFloor,
        TileBase[,] roomWall, Tilemap floorMap, Tilemap wallMap, TileBase defaultFloor,
        List<GroundTileWeight> groundTileWeights, bool randomizeRoomFloors,
        TileBase defaultWall, System.Random random)
    {
        int offsetX = width / 2, offsetY = height / 2;
        TileBase fallbackFloor = defaultFloor != null ? defaultFloor : defaultWall;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                Vector3Int cell = new Vector3Int(x - offsetX, y - offsetY, 0);
                TileBase groundTile = ChooseGroundTile(groundTileWeights, fallbackFloor, random);
                // Lay a continuous ground layer under every generated cell, including walls.
                // This prevents the scene background from showing through tiny sprite seams.
                if (groundTile != null) floorMap.SetTile(cell, groundTile);
                bool mapBoundary = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                if (mapBoundary)
                {
                    // Keep the decorative floor extension outside the playable bounds,
                    // while the wall tilemap's collider seals every route out of the map.
                    wallMap.SetTile(cell, defaultWall);
                    continue;
                }
                if (floor[x, y])
                {
                    TileBase tile = !randomizeRoomFloors && roomFloor[x, y] != null
                        ? roomFloor[x, y]
                        : groundTile;
                    if (tile != null) floorMap.SetTile(cell, tile);
                    wallMap.SetTile(cell, null);
                }
                else
                {
                    TileBase tile = roomWall[x, y] != null ? roomWall[x, y] : defaultWall;
                    if (tile != null) wallMap.SetTile(cell, tile);
                }
            }
    }

    private static TileBase ChooseGroundTile(List<GroundTileWeight> choices,
        TileBase fallback, System.Random random)
    {
        if (choices == null || choices.Count == 0) return fallback;
        float totalWeight = 0f;
        foreach (GroundTileWeight choice in choices)
            if (choice != null && choice.tile != null && choice.weight > 0f)
                totalWeight += choice.weight;
        if (totalWeight <= 0f) return fallback;

        double selection = random.NextDouble() * totalWeight;
        foreach (GroundTileWeight choice in choices)
        {
            if (choice == null || choice.tile == null || choice.weight <= 0f) continue;
            selection -= choice.weight;
            if (selection < 0d) return choice.tile;
        }
        return fallback;
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
