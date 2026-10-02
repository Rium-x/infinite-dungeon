using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class DungeonRoomAssetBuilder
{
    private const string RoomFolder = "Assets/images/stage/Room Templates";
    private const string LibraryPath = "Assets/images/stage/DungeonRoomLibrary.asset";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/Dungeon/Build Default Room Templates")]
    public static void BuildAndWireDefaultAssets()
    {
        EnsureFolder("Assets/images/stage", "Room Templates");
        TileBase floor = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/images/Texture/tile/tile_Stone Brick.asset");
        TileBase wall = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/images/Texture/tile/tile_Black.asset");
        if (floor == null || wall == null)
        {
            Debug.LogError("初期部屋作成用の石レンガ床タイルまたは黒壁タイルが見つかりません。");
            return;
        }

        DungeonRoomDefinition smallSquare = CreateRoom("Small Square", 9, 9, floor, wall, 0);
        DungeonRoomDefinition smallHall = CreateRoom("Small Hall", 11, 9, floor, wall, 1);
        DungeonRoomDefinition largeHall = CreateRoom("Large Hall", 15, 13, floor, wall, 2);
        DungeonRoomDefinition largeCross = CreateRoom("Large Cross", 17, 15, floor, wall, 3);
        DungeonRoomDefinition archive = CreateRoom("Special Archive", 13, 13, floor, wall, 4);

        DungeonRoomLibrary library = AssetDatabase.LoadAssetAtPath<DungeonRoomLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<DungeonRoomLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        library.rooms = new List<DungeonRoomSpawnRule>
        {
            new DungeonRoomSpawnRule { prefab = smallSquare, size = DungeonRoomSize.Small, spawnWeight = 1f },
            new DungeonRoomSpawnRule { prefab = smallHall, size = DungeonRoomSize.Small, spawnWeight = 1f },
            new DungeonRoomSpawnRule { prefab = largeHall, size = DungeonRoomSize.Large, spawnWeight = 1f },
            new DungeonRoomSpawnRule { prefab = largeCross, size = DungeonRoomSize.Large, spawnWeight = 1f },
            new DungeonRoomSpawnRule { prefab = archive, size = DungeonRoomSize.Special, spawnWeight = 1f }
        };
        library.targetRoomCount = 18;
        library.corridorWidth = 2;
        library.extraConnectionChance = 0.12f;
        EditorUtility.SetDirty(library);

        for (int i = 1; i <= 5; i++)
        {
            StageData stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/images/stage/{i}.asset");
            if (stage == null) continue;
            stage.roomLibrary = library;
            EditorUtility.SetDirty(stage);
        }

        WireScene(floor, wall);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("初期部屋Prefab、Room Library、各ステージとSampleSceneのTilemap参照を作成しました。");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
    }

    private static DungeonRoomDefinition CreateRoom(string name,
        int width, int height, TileBase floorTile, TileBase wallTile, int style)
    {
        string path = RoomFolder + "/" + name + ".prefab";
        DungeonRoomDefinition existing = AssetDatabase.LoadAssetAtPath<DungeonRoomDefinition>(path);
        if (existing != null) return existing;

        GameObject root = new GameObject(name);
        Grid grid = root.AddComponent<Grid>();
        grid.cellSize = Vector3.one;
        Tilemap floorMap = CreateTilemap(root.transform, "Floor", 0);
        Tilemap wallMap = CreateTilemap(root.transform, "Wall", 1);
        DungeonRoomDefinition definition = root.AddComponent<DungeonRoomDefinition>();
        definition.floorTilemap = floorMap;
        definition.wallTilemap = wallMap;

        int minX = -width / 2;
        int minY = -height / 2;
        int maxX = minX + width - 1;
        int maxY = minY + height - 1;
        Vector2Int[] doorCells =
        {
            new Vector2Int(0, maxY), new Vector2Int(maxX, 0),
            new Vector2Int(0, minY), new Vector2Int(minX, 0)
        };
        definition.exits = new List<DungeonRoomExit>
        {
            new DungeonRoomExit { cell = doorCells[0], direction = DungeonExitDirection.North },
            new DungeonRoomExit { cell = doorCells[1], direction = DungeonExitDirection.East },
            new DungeonRoomExit { cell = doorCells[2], direction = DungeonExitDirection.South },
            new DungeonRoomExit { cell = doorCells[3], direction = DungeonExitDirection.West }
        };

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                bool perimeter = x == minX || x == maxX || y == minY || y == maxY;
                Vector3Int cell = new Vector3Int(x, y, 0);
                if (perimeter)
                {
                    if (!IsDoor(doorCells, x, y)) wallMap.SetTile(cell, wallTile);
                    continue;
                }

                if (ShouldLeavePillar(x, y, minX, minY, width, height, style))
                    wallMap.SetTile(cell, wallTile);
                else
                    floorMap.SetTile(cell, floorTile);
            }
        }

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return AssetDatabase.LoadAssetAtPath<DungeonRoomDefinition>(path);
    }

    private static Tilemap CreateTilemap(Transform parent, string name, int sortingOrder)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        Tilemap map = child.AddComponent<Tilemap>();
        TilemapRenderer renderer = child.AddComponent<TilemapRenderer>();
        renderer.sortingOrder = sortingOrder;
        return map;
    }

    private static bool IsDoor(Vector2Int[] doors, int x, int y)
    {
        foreach (Vector2Int door in doors)
            if (door.x == x && door.y == y) return true;
        return false;
    }

    private static bool ShouldLeavePillar(int x, int y, int minX, int minY,
        int width, int height, int style)
    {
        int localX = x - minX;
        int localY = y - minY;
        if (style == 1) return localX == width / 2 && localY == height / 2;
        if (style == 2) return localX == width / 2 && localY == height / 2;
        if (style == 3)
        {
            bool inner = localX > 3 && localX < width - 4 && localY > 3 && localY < height - 4;
            return inner && localX % 5 == 0 && localY % 5 == 0;
        }
        if (style == 4)
            return (localX == 3 || localX == width - 4) && (localY == 3 || localY == height - 4);
        return false;
    }

    private static void WireScene(TileBase floor, TileBase wall)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        BSPDungeonMaster generator = Object.FindAnyObjectByType<BSPDungeonMaster>();
        if (generator == null)
        {
            Debug.LogError("SampleScene に BSPDungeonMaster が見つかりません。");
            return;
        }

        GameObject groundObject = GameObject.Find("Ground");
        Tilemap wallMap = FindWallTilemap();
        if (groundObject == null || wallMap == null)
        {
            Debug.LogError("SampleScene の Ground または wall タグ付き Tilemap が見つかりません。");
            return;
        }

        generator.floorTilemap = groundObject.GetComponent<Tilemap>();
        generator.wallTilemap = wallMap;
        generator.tilemap = generator.wallTilemap;
        generator.floorTile = floor;
        generator.wallTile = wall;
        EditorUtility.SetDirty(generator);
        Debug.Log($"Room setup: floor={generator.floorTilemap}, wall={generator.wallTilemap}, legacy={generator.tilemap}");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static Tilemap FindWallTilemap()
    {
        foreach (Tilemap candidate in Object.FindObjectsByType<Tilemap>())
            if (candidate.CompareTag("wall") && candidate.GetComponent<TilemapCollider2D>() != null)
                return candidate;
        return null;
    }
}
