
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

    [Header("ドロップ位置")]
    [Tooltip("アイテム出現位置の基準。0ならnextStageの順番を1から数えます。明示する場合は階層ごとに連続した数値を設定します。")]
    public float hierarchyPosition;
    [Min(0f), Tooltip("プレイヤーレベルが1上がるごとに進むドロップ位置。0ならプレイヤーレベルでは変化しません。")]
    public float playerLevelPositionPerLevel = 0.1f;
    [Range(0f, 1f), Tooltip("最後に設定した階層から離れたとき、位置1つごとに残る通常アイテム補正の割合。0.5なら100, 50, 25...です。")]
    public float normalItemPositionFalloff = 0.5f;
    [Min(0.01f), Tooltip("アイテムが初めて登録された階層より手前で、補正を0から登録値まで滑らかに上げる距離。2なら2階層前で0%、1階層前で半分です。")]
    public float normalItemEntryRampDistance = 2f;

    [Header("通常アイテム")]
    [Tooltip("この階層での通常アイテム候補と確率補正。アイテム未登録の階層間は滑らかに補間され、全ステージで未登録なら出ません。")]
    public DropItemPool normalItemDrops = new DropItemPool();

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
