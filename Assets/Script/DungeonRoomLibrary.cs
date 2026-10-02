using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DungeonRoomLibrary", menuName = "Dungeon/Room Library")]
public sealed class DungeonRoomLibrary : ScriptableObject
{
    [Header("Room Rules")]
    public List<DungeonRoomSpawnRule> rooms = new List<DungeonRoomSpawnRule>();

    [Header("Layout")]
    [Min(2)] public int targetRoomCount = 18;
    [Min(1)] public int corridorWidth = 2;
    [Range(0f, 1f)] public float extraConnectionChance = 0.12f;
}

[System.Serializable]
public sealed class DungeonRoomSpawnRule
{
    public DungeonRoomDefinition prefab;
    public DungeonRoomSize size = DungeonRoomSize.Small;
    [Min(0f), Tooltip("全ての部屋ルールをまとめた抽選で使う相対ウェイト。大きさや特殊カテゴリに関係なく、ウェイト / 全ウェイト合計が抽選確率です。0は他に正のウェイトがある場合は選ばれません。")]
    public float spawnWeight = 1f;
    [Min(0), Tooltip("この部屋を最低何個配置するか。保証部屋は先に配置・接続し、接続不能でも削除しません。")]
    public int guaranteedCount;
}
