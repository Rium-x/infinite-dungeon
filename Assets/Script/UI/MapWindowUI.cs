using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>床・壁Tilemapを表示する簡易ダンジョンマップウィンドウ。</summary>
public sealed class MapWindowUI : MonoBehaviour
{
    [Header("UI参照")]
    [SerializeField] private GameObject windowRoot;
    [SerializeField] private RawImage mapImage;
    [SerializeField] private GameObject canvasToHideWhileOpen;

    [Header("マップ参照（未設定ならBSPDungeonMasterから取得）")]
    [SerializeField] private BSPDungeonMaster dungeon;
    [SerializeField] private Transform player;
    [SerializeField] private Tilemap floorTilemap;
    [SerializeField] private Tilemap wallTilemap;

    [Header("表示範囲")]
    [Min(2), SerializeField] private int visibleRadius = 16;
    [SerializeField, Min(1f)] private float maxZoom = 4f;
    [SerializeField, Min(1f)] private float zoomStep = 1.2f;
    [Tooltip("開拓済み範囲の外側へ移動できる余白。地形は未開拓のまま隠されます")]
    [Min(0), SerializeField] private int exploredPanMargin = 8;

    [Header("探索範囲")]
    [Tooltip("現在地から見通せる探索半径（マス数）")]
    [Min(0), SerializeField] private int explorationRadius = 8;
    [Tooltip("このレイヤーのColliderが視界を遮ります")]
    [SerializeField] private LayerMask wallLayerMask = 1 << 3;

    [Header("色")]
    [SerializeField] private Color32 unknownColor = new Color32(18, 20, 25, 255);
    [SerializeField] private Color32 floorColor = new Color32(155, 160, 166, 255);
    [SerializeField] private Color32 wallColor = new Color32(65, 72, 82, 255);
    [SerializeField] private Color32 playerColor = new Color32(255, 190, 60, 255);

    private bool isOpen;
    private bool canvasWasActive;
    private bool canvasStateWasSaved;
    private readonly List<Behaviour> disabledGameplayComponents = new List<Behaviour>();
    private Texture2D mapTexture;
    private Color32[] mapPixels;
    private Vector3Int mapOrigin;
    private readonly HashSet<Vector3Int> visitedCells = new HashSet<Vector3Int>();
    private int observedGenerationVersion = -1;
    private bool mapDirty = true;
    private float zoom = 1f;
    private Vector2 mapCenterUv = new Vector2(0.5f, 0.5f);
    private bool isDraggingMap;
    private Vector2 previousDragPosition;
    private bool hasExplorationCenter;
    private Vector3Int lastExplorationCenter;
    private int lastExplorationRadius = -1;
    private int exploredMinX = int.MaxValue;
    private int exploredMinY = int.MaxValue;
    private int exploredMaxX = int.MinValue;
    private int exploredMaxY = int.MinValue;

    private void Awake()
    {
        ResolveReferences();
        if (windowRoot == null || mapImage == null)
            Debug.LogError("MapWindowUI: シーン上の Window Root / Map Image が未設定です。", this);
    }

    private void OnEnable()
    {
        CommonWindowEvents.WindowOpened += OnWindowOpened;
        BSPDungeonMaster.OnGenerationComplete += OnDungeonGenerated;
    }

    private void Start() => SetOpen(false);

    private void Update()
    {
        ResolveReferences();
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame &&
            !BSPDungeonMaster.IsPlayerInputLocked)
            SetOpen(!isOpen);

        if (observedGenerationVersion != BSPDungeonMaster.GenerationVersion)
        {
            observedGenerationVersion = BSPDungeonMaster.GenerationVersion;
            visitedCells.Clear();
            ResetExploredBounds();
            mapDirty = true;
            hasExplorationCenter = false;
        }

        Tilemap coordinateMap = floorTilemap != null ? floorTilemap : wallTilemap;
        if (player != null && coordinateMap != null)
        {
            Vector3Int center = coordinateMap.WorldToCell(player.position);
            if (!hasExplorationCenter || center != lastExplorationCenter || lastExplorationRadius != explorationRadius)
            {
                RevealVisibleCells(player.position, coordinateMap, center);
                lastExplorationCenter = center;
                lastExplorationRadius = explorationRadius;
                hasExplorationCenter = true;
            }
        }

        if (isOpen && mapDirty)
            DrawMap();

        if (isOpen && Mouse.current != null)
        {
            HandleMapMouseInput();
        }
    }

    private void OnDisable()
    {
        CommonWindowEvents.WindowOpened -= OnWindowOpened;
        BSPDungeonMaster.OnGenerationComplete -= OnDungeonGenerated;
        RestoreGameplayComponents();
    }

    private void OnDestroy()
    {
        if (mapTexture != null) Destroy(mapTexture);
    }

    private void ResolveReferences()
    {
        if (dungeon == null) dungeon = FindAnyObjectByType<BSPDungeonMaster>();
        if (dungeon != null)
        {
            if (floorTilemap == null) floorTilemap = dungeon.floorTilemap;
            if (wallTilemap == null) wallTilemap = dungeon.wallTilemap != null ? dungeon.wallTilemap : dungeon.tilemap;
        }
        // Playerタグは実際に移動するオブジェクトに付いているため、親Transformより優先します。
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null) player = playerObject.transform;
            else if (Inventory.Instance != null) player = Inventory.Instance.transform;
        }
        if (player == null)
        {
            PL_movement movement = FindAnyObjectByType<PL_movement>();
            if (movement != null) player = movement.transform;
        }
    }

    private void OnWindowOpened(MonoBehaviour openedWindow)
    {
        if (openedWindow != this) Close();
    }

    private void OnDungeonGenerated()
    {
        visitedCells.Clear();
        ResetExploredBounds();
        mapDirty = true;
        hasExplorationCenter = false;
    }

    public void Open() => SetOpen(true);
    public void Close() => SetOpen(false);

    private void SetOpen(bool open)
    {
        if (isOpen == open) return;
        if (open) CommonWindowEvents.NotifyWindowOpened(this);
        isOpen = open;

        if (canvasToHideWhileOpen != null)
        {
            if (open)
            {
                canvasWasActive = canvasToHideWhileOpen.activeSelf;
                canvasStateWasSaved = true;
                canvasToHideWhileOpen.SetActive(false);
            }
            else if (canvasStateWasSaved)
            {
                canvasToHideWhileOpen.SetActive(canvasWasActive);
                canvasStateWasSaved = false;
            }
        }

        if (windowRoot != null) windowRoot.SetActive(open);
        if (open)
        {
            ResolveReferences();
            isDraggingMap = false;
            DisableGameplayComponents();
            DrawMap();
            CenterMapOnPlayer();
            UpdateMapZoom();
        }
        else
        {
            isDraggingMap = false;
            RestoreGameplayComponents();
        }
    }

    private void DrawMap()
    {
        if (mapImage == null) return;
        ResolveReferences();
        if (floorTilemap == null && wallTilemap == null)
        {
            Debug.LogWarning("MapWindowUI: 床または壁Tilemapを設定してください。", this);
            return;
        }

        BoundsInt bounds = new BoundsInt();
        bool hasBounds = false;
        IncludeBounds(floorTilemap, ref bounds, ref hasBounds);
        IncludeBounds(wallTilemap, ref bounds, ref hasBounds);
        if (!hasBounds || bounds.size.x <= 0 || bounds.size.y <= 0) return;

        Tilemap coordinateMap = floorTilemap != null ? floorTilemap : wallTilemap;
        if (player == null)
        {
            Debug.LogWarning("MapWindowUI: Playerタグのオブジェクトが見つかりません。", this);
            return;
        }

        mapOrigin = bounds.min;
        int width = bounds.size.x;
        int height = bounds.size.y;
        if (width <= 0 || height <= 0) return;
        if (mapTexture == null || mapTexture.width != width || mapTexture.height != height)
        {
            if (mapTexture != null) Destroy(mapTexture);
            mapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            mapPixels = new Color32[width * height];
        }

        if (mapPixels == null || mapPixels.Length != width * height)
            mapPixels = new Color32[width * height];
        Color32[] pixels = mapPixels;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Vector3Int cell = new Vector3Int(mapOrigin.x + x, mapOrigin.y + y, 0);
            if (!visitedCells.Contains(cell))
            {
                pixels[y * width + x] = unknownColor;
                continue;
            }

            bool hasFloor = floorTilemap != null && floorTilemap.HasTile(cell);
            bool hasWall = wallTilemap != null && wallTilemap.HasTile(cell);
            pixels[y * width + x] = hasWall ? wallColor : hasFloor ? floorColor : unknownColor;
        }

        Vector3Int playerCell = coordinateMap.WorldToCell(player.position);
        int playerX = playerCell.x - mapOrigin.x;
        int playerY = playerCell.y - mapOrigin.y;
        if (playerX >= 0 && playerX < width && playerY >= 0 && playerY < height)
            pixels[playerY * width + playerX] = playerColor;

        mapTexture.SetPixels32(pixels);
        mapTexture.Apply(false, false);
        mapImage.texture = mapTexture;
        mapDirty = false;
        UpdateMapZoom();
    }

    private void UpdateMapZoom()
    {
        if (mapImage == null) return;
        int viewRadius = Mathf.Max(2, visibleRadius);
        int width = mapTexture != null ? mapTexture.width : 1;
        int height = mapTexture != null ? mapTexture.height : 1;
        float sizeX = Mathf.Min(1f, (viewRadius * 2f + 1f) / width / Mathf.Max(1f, zoom));
        float sizeY = Mathf.Min(1f, (viewRadius * 2f + 1f) / height / Mathf.Max(1f, zoom));
        GetExploredUvBounds(out Vector2 exploredMin, out Vector2 exploredMax);
        exploredMin.x = Mathf.Max(0f, exploredMin.x - exploredPanMargin / (float)width);
        exploredMin.y = Mathf.Max(0f, exploredMin.y - exploredPanMargin / (float)height);
        exploredMax.x = Mathf.Min(1f, exploredMax.x + exploredPanMargin / (float)width);
        exploredMax.y = Mathf.Min(1f, exploredMax.y + exploredPanMargin / (float)height);
        mapCenterUv.x = ClampCenterToExplored(mapCenterUv.x, sizeX, exploredMin.x, exploredMax.x);
        mapCenterUv.y = ClampCenterToExplored(mapCenterUv.y, sizeY, exploredMin.y, exploredMax.y);
        mapImage.uvRect = new Rect(mapCenterUv.x - sizeX * 0.5f, mapCenterUv.y - sizeY * 0.5f, sizeX, sizeY);
    }

    private static float ClampCenterToExplored(float center, float viewSize, float exploredMin, float exploredMax)
    {
        float exploredSize = exploredMax - exploredMin;
        if (exploredSize <= viewSize)
            return Mathf.Clamp(center, exploredMin, exploredMax);

        return Mathf.Clamp(center, exploredMin + viewSize * 0.5f, exploredMax - viewSize * 0.5f);
    }

    private void GetExploredUvBounds(out Vector2 min, out Vector2 max)
    {
        if (visitedCells.Count == 0 || mapTexture == null)
        {
            min = Vector2.zero;
            max = Vector2.one;
            return;
        }

        min = new Vector2(
            Mathf.Clamp01((exploredMinX - mapOrigin.x) / (float)mapTexture.width),
            Mathf.Clamp01((exploredMinY - mapOrigin.y) / (float)mapTexture.height));
        max = new Vector2(
            Mathf.Clamp01((exploredMaxX - mapOrigin.x + 1f) / mapTexture.width),
            Mathf.Clamp01((exploredMaxY - mapOrigin.y + 1f) / mapTexture.height));
    }

    private void CenterMapOnPlayer()
    {
        Tilemap coordinateMap = floorTilemap != null ? floorTilemap : wallTilemap;
        if (coordinateMap == null || player == null || mapTexture == null) return;
        Vector3Int cell = coordinateMap.WorldToCell(player.position);
        mapCenterUv = new Vector2(
            (cell.x - mapOrigin.x + 0.5f) / mapTexture.width,
            (cell.y - mapOrigin.y + 0.5f) / mapTexture.height);
    }

    private void HandleMapMouseInput()
    {
        Mouse mouse = Mouse.current;
        RectTransform imageRect = mapImage != null ? mapImage.rectTransform : null;
        if (mouse == null || imageRect == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            zoom = scroll > 0f ? Mathf.Min(maxZoom, zoom * zoomStep) : Mathf.Max(1f, zoom / zoomStep);
            UpdateMapZoom();
        }

        Vector2 pointer = mouse.position.ReadValue();
        if (mouse.leftButton.wasPressedThisFrame &&
            RectTransformUtility.RectangleContainsScreenPoint(imageRect, pointer, null))
        {
            isDraggingMap = true;
            previousDragPosition = pointer;
        }

        if (isDraggingMap && mouse.leftButton.isPressed)
        {
            Vector2 delta = pointer - previousDragPosition;
            Vector2 size = imageRect.rect.size;
            if (size.x > 0f && size.y > 0f)
            {
                Rect uv = mapImage.uvRect;
                mapCenterUv.x -= delta.x / size.x * uv.width;
                mapCenterUv.y -= delta.y / size.y * uv.height;
                UpdateMapZoom();
            }
            previousDragPosition = pointer;
        }

        if (mouse.leftButton.wasReleasedThisFrame)
            isDraggingMap = false;
    }

    private bool HasLineOfSight(Vector3 origin, Tilemap coordinateMap, Vector3Int originCell, Vector3Int targetCell)
    {
        Vector3 target = coordinateMap.GetCellCenterWorld(targetCell);
        Vector2 delta = (Vector2)(target - origin);
        float distance = delta.magnitude;
        if (distance <= 0.001f) return true;

        Vector2 direction = delta / distance;
        Vector3 cellCenter = coordinateMap.GetCellCenterWorld(originCell);
        float cellWidth = (coordinateMap.GetCellCenterWorld(originCell + Vector3Int.right) - cellCenter).magnitude;
        float cellHeight = (coordinateMap.GetCellCenterWorld(originCell + Vector3Int.up) - cellCenter).magnitude;
        float targetCellInset = 0.5f * (Mathf.Abs(direction.x) * cellWidth + Mathf.Abs(direction.y) * cellHeight);
        float rayDistance = Mathf.Max(0f, distance - targetCellInset - 0.01f);
        if (rayDistance <= 0f) return true;

        return !Physics2D.Raycast(origin, direction, rayDistance, wallLayerMask).collider;
    }

    private void RevealVisibleCells(Vector3 origin, Tilemap coordinateMap, Vector3Int center)
    {
        for (int y = -explorationRadius; y <= explorationRadius; y++)
        for (int x = -explorationRadius; x <= explorationRadius; x++)
        {
            if (x * x + y * y > explorationRadius * explorationRadius)
                continue;

            Vector3Int cell = new Vector3Int(center.x + x, center.y + y, center.z);
            if (visitedCells.Contains(cell)) continue;

            bool exists = (floorTilemap != null && floorTilemap.HasTile(cell)) ||
                          (wallTilemap != null && wallTilemap.HasTile(cell));
            if (exists && HasLineOfSight(origin, coordinateMap, center, cell) && visitedCells.Add(cell))
            {
                exploredMinX = Mathf.Min(exploredMinX, cell.x);
                exploredMinY = Mathf.Min(exploredMinY, cell.y);
                exploredMaxX = Mathf.Max(exploredMaxX, cell.x);
                exploredMaxY = Mathf.Max(exploredMaxY, cell.y);
                mapDirty = true;
            }
        }
    }

    private void ResetExploredBounds()
    {
        exploredMinX = int.MaxValue;
        exploredMinY = int.MaxValue;
        exploredMaxX = int.MinValue;
        exploredMaxY = int.MinValue;
    }

    private static void IncludeBounds(Tilemap tilemap, ref BoundsInt bounds, ref bool hasBounds)
    {
        if (tilemap == null || tilemap.GetUsedTilesCount() == 0) return;
        if (!hasBounds) { bounds = tilemap.cellBounds; hasBounds = true; }
        else
        {
            BoundsInt next = tilemap.cellBounds;
            Vector3Int min = Vector3Int.Min(bounds.min, next.min);
            Vector3Int max = Vector3Int.Max(bounds.max, next.max);
            bounds.SetMinMax(min, max);
        }
    }

    private void DisableGameplayComponents()
    {
        disabledGameplayComponents.Clear();
        if (player == null) return;
        Transform gameplayRoot = Inventory.Instance != null
            ? Inventory.Instance.transform.root
            : player.root;
        AddAndDisable(gameplayRoot.GetComponentsInChildren<PL_movement>(true));
        AddAndDisable(gameplayRoot.GetComponentsInChildren<PlayerCombat>(true));
        AddAndDisable(gameplayRoot.GetComponentsInChildren<NewSwordAction>(true));
    }

    private void AddAndDisable<T>(T[] components) where T : Behaviour
    {
        foreach (T component in components)
        {
            if (component == null || !component.enabled) continue;
            component.enabled = false;
            disabledGameplayComponents.Add(component);
        }
    }

    private void RestoreGameplayComponents()
    {
        foreach (Behaviour component in disabledGameplayComponents)
            if (component != null) component.enabled = true;
        disabledGameplayComponents.Clear();
    }
}
