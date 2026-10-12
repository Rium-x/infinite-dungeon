
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Tilemaps;

[System.Serializable]
public sealed class GroundTileWeight
{
    public TileBase tile;
    [Tooltip("このタイルを描画するタイルマップ。Floorは床の上書き、Trapは床の上に重ねて描画します。")]
    public StageTilemapLayer tilemap = StageTilemapLayer.Floor;
    [Min(0f), Tooltip("他の床タイルと比べた抽選ウェイト。ウェイト合計に対する割合で選ばれます。")]
    public float weight = 1f;
}

public enum StageTilemapLayer
{
    Floor,
    Trap
}

[CreateAssetMenu(fileName = "NewStageData", menuName = "Dungeon/StageData")]
public class StageData : ScriptableObject
{
    public string stageName;

    [Header("ステージクリア条件")]
    [Min(0), Tooltip("このステージで次のステージへ進むために必要な探索値。0で即時に進めます。")]
    public int requiredExplorationValue = 100;

    [Header("ステージの明るさ")]
    [Range(0f, 200f), Tooltip("プレイヤーの HB 配下の Light2D の半径をパーセンテージで調整します。50=半分、120=1.2倍、100=標準です。")]
    public float playerLightRadiusPercent = 100f;

    public int GetRequiredExplorationValueForNextStage()
    {
        return Mathf.Max(0, requiredExplorationValue);
    }

    public float GetPlayerLightRadiusScale()
    {
        return Mathf.Max(0f, playerLightRadiusPercent) / 100f;
    }

    [Header("通常アイテムの順序補正")]
    [Range(0f, 1f), Tooltip("最後に設定したステージ順から離れたとき、順番1つごとに残る通常アイテム補正の割合。0.5なら100, 50, 25...です。")]
    [UnityEngine.Serialization.FormerlySerializedAs("normalItemPositionFalloff")]
    public float normalItemOrderFalloff = 0.5f;

    [Header("通常アイテム")]
    [Tooltip("このステージでの通常アイテム候補と確率補正。初登場ステージより前には出ず、登場後はステージ順に補間されます。")]
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
    [Tooltip("ONなら部屋Prefab内の床・Trapレイヤーを、上の候補から抽選して置き換えます。各候補のTilemap設定も適用します。OFFならPrefabの床とTrapデザインを維持します。")]
    public bool randomizeRoomFloors;
    [Tooltip("設定すると壁の基本タイルをこのステージだけ差し替えます。")]
    public TileBase wallTile;

    // このステージに出現するエネミーとその設定
    public List<EnemySpawnSettings> spawnList;

    [Header("手作りマップの敵スポーン")]
    [Tooltip("手作りマップでも spawnList の敵をランダムにスポーンさせます")]
    public bool spawnEnemiesOnManualMap;

    // 旧アセットからBSPの順序リストを移行するためだけに保持します。
    [SerializeField, HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("nextStage")]
    private StageData legacyNextStage;
    internal StageData LegacyNextStage => legacyNextStage;

}
