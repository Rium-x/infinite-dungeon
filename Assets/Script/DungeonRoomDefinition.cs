using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public enum DungeonRoomSize
{
    Small,
    Large,
    Special
}

public enum DungeonExitDirection
{
    North,
    East,
    South,
    West
}

[Serializable]
public struct DungeonRoomExit
{
    public Vector2Int cell;
    public DungeonExitDirection direction;

    public Vector2Int DirectionVector
    {
        get
        {
            switch (direction)
            {
                case DungeonExitDirection.North: return Vector2Int.up;
                case DungeonExitDirection.East: return Vector2Int.right;
                case DungeonExitDirection.South: return Vector2Int.down;
                default: return Vector2Int.left;
            }
        }
    }
}

/// <summary>
/// Add to a room prefab root. Floor and wall Tilemaps must use the same local cell coordinates.
/// Exit cells mark gaps in the perimeter wall and point out of the room.
/// </summary>
public sealed class DungeonRoomDefinition : MonoBehaviour
{
    public DungeonRoomSize size = DungeonRoomSize.Small;
    public Tilemap floorTilemap;
    public Tilemap wallTilemap;
    public List<DungeonRoomExit> exits = new List<DungeonRoomExit>();

    public bool TryGetBounds(out BoundsInt bounds)
    {
        bool hasBounds = false;
        bounds = new BoundsInt();

        if (floorTilemap != null && floorTilemap.GetUsedTilesCount() > 0)
        {
            bounds = floorTilemap.cellBounds;
            hasBounds = true;
        }

        if (wallTilemap != null && wallTilemap.GetUsedTilesCount() > 0)
        {
            BoundsInt wallBounds = wallTilemap.cellBounds;
            if (!hasBounds)
            {
                bounds = wallBounds;
                hasBounds = true;
            }
            else
            {
                int minX = Mathf.Min(bounds.xMin, wallBounds.xMin);
                int minY = Mathf.Min(bounds.yMin, wallBounds.yMin);
                int maxX = Mathf.Max(bounds.xMax, wallBounds.xMax);
                int maxY = Mathf.Max(bounds.yMax, wallBounds.yMax);
                bounds = new BoundsInt(minX, minY, 0, maxX - minX, maxY - minY, 1);
            }
        }

        return hasBounds && bounds.size.x > 0 && bounds.size.y > 0;
    }

    private void OnValidate()
    {
        if (floorTilemap == null || wallTilemap == null)
        {
            Tilemap[] maps = GetComponentsInChildren<Tilemap>(true);
            foreach (Tilemap map in maps)
            {
                if (map.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0)
                    floorTilemap = map;
                else if (map.name.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0)
                    wallTilemap = map;
            }
        }
    }
}
