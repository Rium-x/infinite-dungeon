using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DungeonRoomLibrary", menuName = "Dungeon/Room Library")]
public sealed class DungeonRoomLibrary : ScriptableObject
{
    [Header("Normal Rooms")]
    public List<DungeonRoomDefinition> smallRooms = new List<DungeonRoomDefinition>();
    public List<DungeonRoomDefinition> largeRooms = new List<DungeonRoomDefinition>();

    [Header("Optional Special Rooms")]
    public List<DungeonRoomDefinition> specialRooms = new List<DungeonRoomDefinition>();
    [Range(0f, 1f)] public float specialRoomChance = 0.08f;

    [Header("Layout")]
    [Min(2)] public int targetRoomCount = 18;
    [Min(1)] public int corridorWidth = 2;
    [Range(0f, 1f)] public float extraConnectionChance = 0.12f;
}
