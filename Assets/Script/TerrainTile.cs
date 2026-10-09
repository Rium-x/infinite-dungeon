using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>見た目に加えてゲーム上の地形タイプを持つTile asset。</summary>
[CreateAssetMenu(fileName = "New Terrain Tile", menuName = "Dungeon/Terrain Tile")]
public sealed class TerrainTile : Tile
{
    [SerializeField] private TerrainType terrainType = TerrainType.Floor;

    public TerrainType Type => terrainType;
}
