using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 現在の床・壁Tilemapからセルの地形を取得します。
/// TerrainTileならTile自身のタイプを優先し、既存の標準Tileなら従来の配置先を使います。
/// </summary>
public sealed class TerrainMap : MonoBehaviour
{
    [SerializeField] private Tilemap floorTilemap;
    [SerializeField] private Tilemap wallTilemap;

    public TerrainType GetTerrainType(Vector3Int cell)
    {
        return ResolveTerrainType(floorTilemap, wallTilemap, cell);
    }

    public static TerrainType ResolveTerrainType(Tilemap floorMap, Tilemap wallMap, Vector3Int cell)
    {
        TileBase wall = wallMap != null ? wallMap.GetTile(cell) : null;
        if (wall is TerrainTile typedWall) return typedWall.Type;
        if (IsWaterTile(wall)) return TerrainType.Water;
        if (wall != null) return TerrainType.Wall;

        TileBase floor = floorMap != null ? floorMap.GetTile(cell) : null;
        if (floor is TerrainTile typedFloor) return typedFloor.Type;
        if (IsWaterTile(floor)) return TerrainType.Water;
        if (floor != null) return TerrainType.Floor;

        return TerrainType.Unknown;
    }

    private static bool IsWaterTile(TileBase tile)
    {
        return tile != null && tile.name == "Water";
    }
}
