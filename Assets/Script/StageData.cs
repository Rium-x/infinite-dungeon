
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Tilemaps;

[System.Serializable]
public sealed class GroundTileWeight
{
    public TileBase tile;
    [Min(0f), Tooltip("他の床タイルと比べた抽選ウェイト。ウェイト合計に対する割合で選ばれます。")]
    public float weight = 1f;
}

[CreateAssetMenu(fileName = "NewStageData", menuName = "Dungeon/StageData")]
public class StageData : ScriptableObject
{
    public string stageName;

    [Header("プレイヤースポーン")]
    [Tooltip("ON: RandomTeleporter の範囲へランダムスポーン、OFF: 固定座標へスポーン")]
    public bool randomPlayerSpawn = true;
    [Tooltip("ランダムスポーンをOFFにしたときのワールド座標")]
    public Vector2 playerSpawnPosition;

    [Header("Map")]
    [Tooltip("設定されている場合は自動生成せず、この prefab の Tilemap を使用します")]
    public GameObject manualMapPrefab;
    public int mapWidth = 60;
    public int mapHeight = 60;

    [Header("自動生成の部屋（ステージごとにライブラリを選択）")]
    public DungeonRoomLibrary roomLibrary;

    [Header("自動生成の床")]
    [Tooltip("床の基本タイル。未設定なら BSPDungeonMaster の基本床タイルを使います。")]
    public TileBase groundTile;
    [Tooltip("空欄なら基本床タイルだけを使います。複数登録するとウェイト比で各セルを抽選します。")]
    public List<GroundTileWeight> groundTileWeights = new List<GroundTileWeight>();
    [Tooltip("ONなら部屋Prefab内の床も、上の床タイル候補から抽選します。OFFなら部屋Prefabの床デザインを維持します。")]
    public bool randomizeRoomFloors;
    [Tooltip("設定すると壁の基本タイルをこのステージだけ差し替えます。")]
    public TileBase wallTile;

    // このステージに出現するエネミーとその設定
    public List<EnemySpawnSettings> spawnList;

    [Header("手作りマップの敵スポーン")]
    [Tooltip("手作りマップでも spawnList の敵をランダムにスポーンさせます")]
    public bool spawnEnemiesOnManualMap;
    
    // 次のステージのデータ（nullなら最終ステージ）
    public StageData nextStage;
    
}
