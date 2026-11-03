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
    [Tooltip("開始時に stageOrder の何番目から始めるか。0 が最初。")]
    [Min(0)] public int startStageIndex = 0;
    [Tooltip("ダンジョンの進行順。通常アイテムの確率補間にもこの順番を使用します。")]
    public List<StageData> stageOrder = new List<StageData>();
    [Tooltip("ONなら順序リストの最後から先頭へ戻ります。")]
    public bool loopStageOrder;

    [Header("Cleaning Settings")]
    public List<string> tagsToClear = new List<string> { "Enemy", "Item", "Projectile" };

    [Header("UI Effects")]
    public Image fadePanel;
    public TextMeshProUGUI stageText;
    public float fadeDuration = 0.5f;
    [SerializeField, Tooltip("ステージ生成時に加算表示するロード画面シーン名")]
    private string loadingScreenSceneName = "LoadingScene";

    [Header("Tilemap Settings")]
    [Tooltip("旧シーンとの互換用。wallTilemap 未設定時に使用")]
    public Tilemap tilemap;
    public Tilemap floorTilemap;
    public Tilemap wallTilemap;
    public Tilemap trapTilemap;
    public TileBase floorTile;
    public TileBase wallTile;

    [Header("Map Size")]
    [Tooltip("ONなら下のサイズを使用。OFFならStageDataのマップ寸法を使用します。")]
    public bool overrideMapSize;
    [Min(20)] public int mapWidth = 120;
    [Min(20)] public int mapHeight = 120;

    [Header("Room Layout")]
    [Min(2)] public int roomCount = 18;
    [Range(1f, 2f), Tooltip("部屋数設定に掛ける倍率です。1.4なら指定数の約1.4倍の部屋を配置し、歩行中に部屋へ出会いやすくします。最終数はマップ密度と保証部屋数に従います。")]
    public float roomEncounterMultiplier = 1.4f;
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

    /// <summary>現在のセルに置かれている地形タイプを返します。</summary>
    public TerrainType GetTerrainTypeAt(Vector3Int cell)
    {
        return TerrainMap.ResolveTerrainType(floorTilemap, wallTilemap, cell);
    }

    public List<StageData> GetOrderedStages()
    {
        var orderedStages = new List<StageData>();
        if (stageOrder != null)
        {
            var seen = new HashSet<StageData>();
            foreach (StageData stage in stageOrder)
                if (stage != null && seen.Add(stage))
                    orderedStages.Add(stage);
        }
        if (orderedStages.Count == 0 && currentStageData != null)
        {
            var visited = new HashSet<StageData>();
            StageData stage = currentStageData;
            while (stage != null && visited.Add(stage))
            {
                orderedStages.Add(stage);
                stage = stage.LegacyNextStage;
            }
            if (stage != null)
                loopStageOrder = true;
        }
        return orderedStages;
    }

    private StageData GetNextStageInOrder()
    {
        List<StageData> orderedStages = GetOrderedStages();
        int currentIndex = orderedStages.IndexOf(currentStageData);
        if (currentIndex < 0) return null;
        if (currentIndex + 1 < orderedStages.Count) return orderedStages[currentIndex + 1];
        return loopStageOrder && orderedStages.Count > 0 ? orderedStages[0] : null;
    }

    public void SetCurrentStageFromListIndex(int stageIndex)
    {
        if (stageOrder == null || stageOrder.Count == 0)
            return;

        int clampedIndex = Mathf.Clamp(stageIndex, 0, stageOrder.Count - 1);
        currentStageData = stageOrder[clampedIndex];
        ExplorationManager.Instance?.SetCurrentStage(currentStageData);
        BGMManager.PlayStageMusic(currentStageData);

        if (stageText != null)
            stageText.text = currentStageData != null ? currentStageData.stageName : stageText.text;
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

        // If this gameplay scene was opened directly, show the same authored loading
        // scene used by the title flow before starting the expensive map generation.
        // When the title flow already owns a loading scene, it remains responsible
        // for closing it after generation completes.
        bool openedStartupLoadingScreen = !LoadingScreenController.HasActiveOverlay;
        if (openedStartupLoadingScreen)
            yield return StartCoroutine(LoadingScreenController.OpenOverlayRoutine(loadingScreenSceneName));

        // 1. 最初は真っ暗＆文字が表示された状態にする
        if (fadePanel != null) fadePanel.color = Color.black;
        if (stageText != null)
        {
            stageText.text = $"{currentStageData.stageName}\nマップ生成中...";
            stageText.color = Color.white;
            // 明示的に不透明度を1にする（念のため）
            fadePanel.canvasRenderer.SetAlpha(1);
            stageText.canvasRenderer.SetAlpha(1);
        }

        // 2. ダンジョン生成
        yield return StartCoroutine(GenerateDungeonRoutine());
        BGMManager.PlayStageMusic(currentStageData);

        if (openedStartupLoadingScreen)
            yield return StartCoroutine(LoadingScreenController.CloseOverlayRoutine());
        else
            while (LoadingScreenController.HasActiveOverlay)
                yield return null;

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
        if (trapTilemap == null)
        {
            foreach (Tilemap candidate in FindObjectsByType<Tilemap>())
                if (candidate.name.IndexOf("trap", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    trapTilemap = candidate;
                    break;
                }
        }
        List<StageData> orderedStages = GetOrderedStages();
        if (stageOrder == null) stageOrder = new List<StageData>();
        stageOrder.Clear();
        stageOrder.AddRange(orderedStages);
        if (stageOrder.Count > 0)
        {
            int selectedIndex = Mathf.Clamp(startStageIndex, 0, stageOrder.Count - 1);
            currentStageData = stageOrder[selectedIndex];
        }
        Instance = this;
        ExplorationManager.Instance?.SetCurrentStage(currentStageData);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (stageOrder != null && stageOrder.Count > 0) return;
        List<StageData> migratedOrder = GetOrderedStages();
        if (migratedOrder.Count == 0) return;
        if (stageOrder == null) stageOrder = new List<StageData>();
        stageOrder.AddRange(migratedOrder);
        if (gameObject.scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }
#endif

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

        StageData nextStage = GetNextStageInOrder();
        if (nextStage != null)
            stageText.text = nextStage.stageName;
        else
            stageText.text = "Final Stage Clear!";

        yield return StartCoroutine(Fade(1));

        if (nextStage != null)
        {
            yield return StartCoroutine(LoadingScreenController.OpenOverlayRoutine(loadingScreenSceneName));
            currentStageData = nextStage;
            ExplorationManager.Instance?.SetCurrentStage(currentStageData);
            yield return StartCoroutine(GenerateDungeonRoutine());
            BGMManager.PlayStageMusic(currentStageData);
            yield return StartCoroutine(LoadingScreenController.CloseOverlayRoutine());
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
        StartCoroutine(GenerateDungeonRoutine());
    }

    private IEnumerator GenerateDungeonRoutine()
    {
        HasCompletedGeneration = false;
        AlignOutputTilemaps();
        ClearManualMapObjects();
        ClearGeneratedRoomObjects();
        ClearStageObjects();

        if (!IsUsingManualMap)
        {
            yield return StartCoroutine(GenerateAutomaticMapRoutine());
        }
        else
        {
            LoadManualMap();
            yield return null;
        }

        if (stageText != null && currentStageData != null)
            stageText.text = currentStageData.stageName;

        var col = wallTilemap != null ? wallTilemap.GetComponent<TilemapCollider2D>() : null;
        if (col != null) col.ProcessTilemapChanges();
        var trapCollider = trapTilemap != null ? trapTilemap.GetComponent<TilemapCollider2D>() : null;
        if (trapCollider != null) trapCollider.ProcessTilemapChanges();

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
        AlignTilemapTransform(floorTransform, wallTransform);
        if (trapTilemap != null) AlignTilemapTransform(trapTilemap.transform, wallTransform);
    }

    private static void AlignTilemapTransform(Transform target, Transform reference)
    {
        if (target.parent == reference.parent)
        {
            Vector3 localPosition = target.localPosition;
            localPosition.x = reference.localPosition.x;
            localPosition.y = reference.localPosition.y;
            target.localPosition = localPosition;
        }
        else
        {
            Vector3 targetPosition = target.position;
            Vector3 referencePosition = reference.position;
            targetPosition.x = referencePosition.x;
            targetPosition.y = referencePosition.y;
            target.position = targetPosition;
        }
    }

    private IEnumerator GenerateAutomaticMapRoutine()
    {
        ClearOutputTilemaps();
        if (currentStageData.roomLibrary == null)
        {
            Debug.LogError("StageData に DungeonRoomLibrary が設定されていません。", currentStageData);
            yield break;
        }

        if (floorTilemap == null || wallTilemap == null)
        {
            Debug.LogError("自動生成には床用・壁用の Tilemap が必要です。", this);
            yield break;
        }

        int generationWidth = overrideMapSize ? mapWidth : currentStageData.mapWidth;
        int generationHeight = overrideMapSize ? mapHeight : currentStageData.mapHeight;
        int seed = layoutSeed < 0 ? UnityEngine.Random.Range(0, int.MaxValue) : layoutSeed;
        TileBase stageFloor = currentStageData.groundTile != null ? currentStageData.groundTile : floorTile;
        TileBase stageWall = currentStageData.wallTile != null ? currentStageData.wallTile : wallTile;
        bool generationSucceeded = false;
        Action<string, float> updateProgress = (message, progress) =>
        {
            if (stageText != null)
                stageText.text = $"{currentStageData.stageName}\n{message}";
            LoadingScreenController.ReportMapProgress(message, progress);
        };
        yield return DungeonGraphMapBuilder.GenerateRoutine(generationWidth, generationHeight,
                currentStageData.roomLibrary, roomPlacementPadding, floorTilemap, wallTilemap, trapTilemap,
                stageFloor, currentStageData.groundTileWeights, currentStageData.randomizeRoomFloors,
                stageWall, seed, transform, generatedRoomObjects, roomCount,
                roomEncounterMultiplier, maxRoomDensity,
                Mathf.Max(3, corridorWidth), extraBranchCount, branchChance,
                maxRoomLinkDistance, maxCorridorTurns, windingCorridorChance,
                outsideFloorPadding, removedRoomRetryThreshold,
                result => generationSucceeded = result, updateProgress);
        if (!generationSucceeded)
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
            Tilemap destination = IsTrapMap(sourceTilemap) ? trapTilemap :
                IsFloorMap(sourceTilemap) ? floorTilemap : wallTilemap;
            if (destination == null) continue;
            BoundsInt bounds = sourceTilemap.cellBounds;
            if (bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0) continue;
            destination.SetTilesBlock(bounds, sourceTilemap.GetTilesBlock(bounds));
        }

        ExtractManualObjects(mapInstance);
        Destroy(mapInstance);
    }

    private static bool IsFloorMap(Tilemap source)
    {
        return source != null && source.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsTrapMap(Tilemap source)
    {
        return source != null && source.name.IndexOf("trap", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ClearOutputTilemaps()
    {
        if (floorTilemap != null) floorTilemap.ClearAllTiles();
        if (wallTilemap != null) wallTilemap.ClearAllTiles();
        if (trapTilemap != null) trapTilemap.ClearAllTiles();
        if (wallTilemap == null && tilemap != null) tilemap.ClearAllTiles();
    }

    private void ExtractManualObjects(GameObject mapInstance)
    {
        List<Transform> objectsToExtract = new List<Transform>();
        CollectNonTilemapSubtrees(mapInstance.transform, objectsToExtract);

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

    private static void CollectNonTilemapSubtrees(Transform parent, List<Transform> results)
    {
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            Transform child = parent.GetChild(index);
            if (child.GetComponentInChildren<Tilemap>(true) == null)
                results.Add(child);
            else
                CollectNonTilemapSubtrees(child, results);
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
