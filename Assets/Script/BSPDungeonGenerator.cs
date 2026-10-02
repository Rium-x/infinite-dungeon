using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using TMPro;

#if UNITY_EDITOR
using UnityEditor; // Selectionクラスを使うために必要
#endif

public class BSPDungeonMaster : MonoBehaviour
{
    public static event Action OnGenerationComplete;
    public static bool HasCompletedGeneration { get; private set; }
    public static int GenerationVersion { get; private set; }

    [Header("Current Stage")]
    public StageData currentStageData;
    private StageData firstStageData;
    public StageData StartingStageData => firstStageData != null ? firstStageData : currentStageData;

    [Header("Cleaning Settings")]
    public List<string> tagsToClear = new List<string> { "Enemy", "Item", "Projectile" };

    [Header("UI Effects")]
    public Image fadePanel;
    public TextMeshProUGUI stageText;
    public float fadeDuration = 0.5f;

    [Header("Tilemap Settings")]
    [Tooltip("旧シーンとの互換用。wallTilemap 未設定時に使用")]
    public Tilemap tilemap;
    public Tilemap floorTilemap;
    public Tilemap wallTilemap;
    public TileBase floorTile;
    public TileBase wallTile;

    [Header("Map Size")]
    [Tooltip("ONなら下のサイズを使用。OFFならStageDataのマップ寸法を使用します。")]
    public bool overrideMapSize;
    [Min(20)] public int mapWidth = 120;
    [Min(20)] public int mapHeight = 120;

    [Header("Room Layout")]
    [Min(2)] public int roomCount = 18;
    [Tooltip("部屋が占める面積の上限割合。配置に余裕がない場合は部屋数が減ります。")]
    [Range(0.05f, 0.8f)] public float maxRoomDensity = 0.32f;
    [Tooltip("部屋同士の最低間隔。大きくすると通路が長くなり、配置数は減ります。")]
    [Min(1)] public int roomPlacementPadding = 3;

    [Header("Corridors")]
    [Tooltip("通路幅。最小値は3マスです。")]
    [Range(3, 9)] public int corridorWidth = 3;
    [Tooltip("部屋から既存通路への枝道や、部屋同士の追加ルートの本数。")]
    [Range(0, 24)] public int extraBranchCount = 8;
    [Tooltip("追加の部屋接続ごとに通路を作る確率。")]
    [Range(0f, 1f)] public float branchChance = 0.75f;
    [Tooltip("接続済みネットワークがあるとき、部屋同士を直接つなぐ最大距離。これを超える場合は近い既存通路への接続を優先します。")]
    [Min(1)] public int maxRoomLinkDistance = 36;
    [Tooltip("通路1本あたりに許す曲がりの目安。1なら直角に曲がるルートを優先します。")]
    [Range(1, 6)] public int maxCorridorTurns = 4;
    [Tooltip("直角ルートの代わりに中継点を通る長めの曲がり道を選ぶ確率。")]
    [Range(0f, 1f)] public float windingCorridorChance = 0.12f;
    [Tooltip("プレイ可能範囲の外側を床で覆うマス数。外周の壁は維持されます。")]
    [Min(0)] public int outsideFloorPadding = 8;
    [Header("Reproducibility")]
    [Tooltip("-1で毎回ランダム。0以上なら同じレイアウトを再現します。")]
    public int layoutSeed = -1;

    [Header("Generation Safety")]
    [Min(0), Tooltip("削除される通常部屋がこの数以下なら配置を再試行しません。保証部屋の接続失敗時は常に再試行します。")]
    public int removedRoomRetryThreshold = 3;
    private readonly List<GameObject> manualMapObjects = new List<GameObject>();
    private readonly List<GameObject> generatedRoomObjects = new List<GameObject>();
    private readonly List<RandomTeleporter> pausedManualMapTeleporters = new List<RandomTeleporter>();
    private bool isTransitioning = false;
    public static bool IsPlayerInputLocked { get; private set; }
    public bool IsUsingManualMap => currentStageData != null && currentStageData.manualMapPrefab != null;

    public float GetCurrentDropPosition(int playerLevel)
    {
        StageData start = StartingStageData;
        StageData current = currentStageData != null ? currentStageData : start;
        List<StageData> chain = NormalItemDropCalculator.GetStageChain(start);
        float stagePosition = NormalItemDropCalculator.GetPosition(current, chain);
        float perLevel = start != null ? start.playerLevelPositionPerLevel : 0f;
        return stagePosition + Mathf.Max(0, playerLevel - 1) * perLevel;
    }

    public void GetEnemySpawnRange(out Vector2 min, out Vector2 max)
    {
        int width = currentStageData == null ? mapWidth :
            (overrideMapSize ? mapWidth : currentStageData.mapWidth);
        int height = currentStageData == null ? mapHeight :
            (overrideMapSize ? mapHeight : currentStageData.mapHeight);
        width = Mathf.Max(20, width);
        height = Mathf.Max(20, height);

        // Generated maps are centered around world origin. Keep destinations one tile
        // inside the edge so enemies don't spawn on the outer wall.
        min = new Vector2(-width * 0.5f + 1f, -height * 0.5f + 1f);
        max = new Vector2(width * 0.5f - 1f, height * 0.5f - 1f);
    }

    IEnumerator Start()
    {
        if (currentStageData == null) yield break;

        Time.timeScale = 0;
        IsPlayerInputLocked = true;

        // 1. 最初は真っ暗＆文字が表示された状態にする
        if (fadePanel != null) fadePanel.color = Color.black;
        if (stageText != null)
        {
            stageText.text = currentStageData.stageName;
            stageText.color = Color.white;
            // 明示的に不透明度を1にする（念のため）
            fadePanel.canvasRenderer.SetAlpha(1);
            stageText.canvasRenderer.SetAlpha(1);
        }

        // 2. ダンジョン生成
        GenerateDungeon();

        // 3. 画面が真っ暗な状態で少し待機（ロード待ちやプレイヤーに心の準備をさせる）
        yield return new WaitForSecondsRealtime(3.5f);



        // 4. 徐々に透明にしていく（fadeDuration秒かけて消える）
        yield return StartCoroutine(Fade(0));

        Time.timeScale = 1;
        IsPlayerInputLocked = false;
    }

    public static BSPDungeonMaster Instance { get; private set; }
    public global::System.Boolean IsTransitioning { get => isTransitioning; set => isTransitioning = value; }

    private void Awake()
    {
        HasCompletedGeneration = false;
        if (Instance != null && Instance != this)
        {
            Debug.LogError("BSPDungeonMaster が複数存在しています。");
            Destroy(gameObject);
            return;
        }

        if (wallTilemap == null) wallTilemap = tilemap;
        firstStageData = currentStageData;
        Instance = this;
    }

    public void GoToNextStage()
{
    if (IsTransitioning)
        return;

    StartCoroutine(ChangeStageRoutine());
}

    private IEnumerator ChangeStageRoutine()
    {
        IsTransitioning = true;

        // --- エラー対策：オブジェクトが消える前に選択を強制解除 ---
        #if UNITY_EDITOR
        Selection.activeGameObject = null;
        #endif

        Time.timeScale = 0;
        IsPlayerInputLocked = true;

        if (currentStageData.nextStage != null)
            stageText.text = currentStageData.nextStage.stageName;
        else
            stageText.text = "Final Stage Clear!";

        yield return StartCoroutine(Fade(1));

        if (currentStageData.nextStage != null)
        {
            currentStageData = currentStageData.nextStage;
            ExplorationManager.Instance?.ResetProgress();
            GenerateDungeon();
        }

        yield return new WaitForSecondsRealtime(3.5f);

        yield return StartCoroutine(Fade(0));

        Time.timeScale = 1;
        IsPlayerInputLocked = false;
        IsTransitioning = false;
    }

    private IEnumerator Fade(float targetAlpha)
    {
        if (fadePanel == null || stageText == null) yield break;

        float startAlpha = fadePanel.color.a;
        float timer = 0;

        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(startAlpha, targetAlpha, timer / fadeDuration);
            fadePanel.color = new Color(0, 0, 0, a);
            stageText.color = new Color(1, 1, 1, a);
            yield return null;
        }

        fadePanel.color = new Color(0, 0, 0, targetAlpha);
        stageText.color = new Color(1, 1, 1, targetAlpha);
    }

    public void GenerateDungeon()
    {
        HasCompletedGeneration = false;
        AlignOutputTilemaps();
        ClearManualMapObjects();
        ClearGeneratedRoomObjects();
        ClearStageObjects();

        if (!IsUsingManualMap)
        {
            GenerateAutomaticMap();
        }
        else
        {
            LoadManualMap();
        }

        var col = wallTilemap != null ? wallTilemap.GetComponent<TilemapCollider2D>() : null;
        if (col != null) col.ProcessTilemapChanges();

        Physics2D.SyncTransforms();
        GenerationVersion++;
        HasCompletedGeneration = true;
        OnGenerationComplete?.Invoke();

        if (IsUsingManualMap)
            StartCoroutine(RestoreManualMapTeleporters());
    }

    private void AlignOutputTilemaps()
    {
        if (floorTilemap == null || wallTilemap == null) return;

        Transform floorTransform = floorTilemap.transform;
        Transform wallTransform = wallTilemap.transform;
        if (floorTransform.parent == wallTransform.parent)
        {
            Vector3 localPosition = floorTransform.localPosition;
            localPosition.x = wallTransform.localPosition.x;
            localPosition.y = wallTransform.localPosition.y;
            floorTransform.localPosition = localPosition;
        }
        else
        {
            Vector3 floorPosition = floorTransform.position;
            Vector3 wallPosition = wallTransform.position;
            floorPosition.x = wallPosition.x;
            floorPosition.y = wallPosition.y;
            floorTransform.position = floorPosition;
        }
    }

    private void GenerateAutomaticMap()
    {
        ClearOutputTilemaps();
        if (currentStageData.roomLibrary == null)
        {
            Debug.LogError("StageData に DungeonRoomLibrary が設定されていません。", currentStageData);
            return;
        }

        if (floorTilemap == null || wallTilemap == null)
        {
            Debug.LogError("自動生成には床用・壁用の Tilemap が必要です。", this);
            return;
        }

        int generationWidth = overrideMapSize ? mapWidth : currentStageData.mapWidth;
        int generationHeight = overrideMapSize ? mapHeight : currentStageData.mapHeight;
        int seed = layoutSeed < 0 ? UnityEngine.Random.Range(0, int.MaxValue) : layoutSeed;
        TileBase stageFloor = currentStageData.groundTile != null ? currentStageData.groundTile : floorTile;
        TileBase stageWall = currentStageData.wallTile != null ? currentStageData.wallTile : wallTile;
        if (!DungeonGraphMapBuilder.Generate(generationWidth, generationHeight,
                currentStageData.roomLibrary, roomPlacementPadding, floorTilemap, wallTilemap,
                stageFloor, currentStageData.groundTileWeights, currentStageData.randomizeRoomFloors,
                stageWall, seed, transform, generatedRoomObjects, roomCount, maxRoomDensity,
                Mathf.Max(3, corridorWidth), extraBranchCount, branchChance,
                maxRoomLinkDistance, maxCorridorTurns, windingCorridorChance,
                outsideFloorPadding, removedRoomRetryThreshold))
            Debug.LogError("ダンジョン生成に失敗しました。Room Libraryと部屋Prefabを確認してください。", currentStageData);
    }

    private void LoadManualMap()
    {
        ClearOutputTilemaps();

        if (currentStageData.manualMapPrefab == null)
        {
            Debug.LogError("手作りマップを使用するステージに manualMapPrefab が設定されていません。", currentStageData);
            return;
        }

        GameObject mapInstance = Instantiate(currentStageData.manualMapPrefab, Vector3.zero, Quaternion.identity);
        Tilemap[] sourceTilemaps = mapInstance.GetComponentsInChildren<Tilemap>(true);
        if (sourceTilemaps.Length == 0)
        {
            Debug.LogError("manualMapPrefab に Tilemap が見つかりません。", mapInstance);
            Destroy(mapInstance);
            return;
        }

        foreach (Tilemap sourceTilemap in sourceTilemaps)
        {
            Tilemap destination = IsFloorMap(sourceTilemap) ? floorTilemap : wallTilemap;
            if (destination == null) continue;
            foreach (Vector3Int cell in sourceTilemap.cellBounds.allPositionsWithin)
            {
                TileBase tile = sourceTilemap.GetTile(cell);
                if (tile != null) destination.SetTile(cell, tile);
            }
        }

        ExtractManualObjects(mapInstance, sourceTilemaps);
        Destroy(mapInstance);
    }

    private static bool IsFloorMap(Tilemap source)
    {
        return source != null && source.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ClearOutputTilemaps()
    {
        if (floorTilemap != null) floorTilemap.ClearAllTiles();
        if (wallTilemap != null) wallTilemap.ClearAllTiles();
        else if (tilemap != null) tilemap.ClearAllTiles();
    }

    private void ExtractManualObjects(GameObject mapInstance, Tilemap[] sourceTilemaps)
    {
        List<Transform> objectsToExtract = new List<Transform>();

        for (int index = 0; index < mapInstance.transform.childCount; index++)
        {
            Transform child = mapInstance.transform.GetChild(index);
            if (Array.Exists(sourceTilemaps, source => source.transform == child) ||
                child.GetComponentInChildren<Tilemap>(true) != null)
                continue;

            objectsToExtract.Add(child);
        }

        foreach (Transform objectRoot in objectsToExtract)
        {
            objectRoot.SetParent(transform, true);
            manualMapObjects.Add(objectRoot.gameObject);

            RandomTeleporter[] randomTeleporters = objectRoot.GetComponentsInChildren<RandomTeleporter>(true);
            foreach (RandomTeleporter randomTeleporter in randomTeleporters)
            {
                if (randomTeleporter.enabled)
                {
                    randomTeleporter.enabled = false;
                    pausedManualMapTeleporters.Add(randomTeleporter);
                }
            }
        }
    }

    private IEnumerator RestoreManualMapTeleporters()
    {
        yield return null;

        foreach (RandomTeleporter randomTeleporter in pausedManualMapTeleporters)
        {
            if (randomTeleporter != null)
                randomTeleporter.enabled = true;
        }

        pausedManualMapTeleporters.Clear();
    }

    private void ClearManualMapObjects()
    {
        pausedManualMapTeleporters.Clear();

        foreach (GameObject manualObject in manualMapObjects)
        {
            if (manualObject != null)
                Destroy(manualObject);
        }

        manualMapObjects.Clear();
    }

    private void ClearGeneratedRoomObjects()
    {
        foreach (GameObject generatedObject in generatedRoomObjects)
            if (generatedObject != null) Destroy(generatedObject);
        generatedRoomObjects.Clear();
    }

    private void ClearStageObjects()
    {
        foreach (string tagName in tagsToClear)
        {
            try {
                GameObject[] targets = GameObject.FindGameObjectsWithTag(tagName);
                foreach (GameObject obj in targets) Destroy(obj);
            } catch {
                // タグが存在しない場合の警告を無視、または表示
            }
        }
    }

}
