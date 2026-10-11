using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Tilemaps;

public class RandomTeleporter : MonoBehaviour
{
    private struct SpawnCandidate
    {
        public Vector2 position;
        public bool isWater;

        public SpawnCandidate(Vector2 position, bool isWater)
        {
            this.position = position;
            this.isWater = isWater;
        }
    }

    [Header("移動範囲の設定")]
    [SerializeField] private Vector2 minRange = new Vector2(56f, 56f);
    [SerializeField] private Vector2 maxRange = new Vector2(-56f, -56f);

    [Header("判定の設定")]
    [SerializeField] private LayerMask wallLayer = 8;
    [SerializeField] private Vector2 checkSize = new Vector2(0.5f, 0.5f);

    [Header("追加の安全設定")]
    [SerializeField] private bool useEnemyCheck = true;
    [SerializeField] private float enemySafetyRadius = 15f;
    [SerializeField] private LayerMask enemyLayer = 128;

    [Header("演出設定")]
    [SerializeField] private float fadeDuration = 0.7f;

    private List<SpriteRenderer> targetRenderers = new List<SpriteRenderer>();
    private List<EnemyAttack> targetAttacks = new List<EnemyAttack>(); 
    private List<BoxCollider2D> targetColliders = new List<BoxCollider2D>();
    private List<CanvasGroup> targetCanvasGroups = new List<CanvasGroup>();
    private List<MonoBehaviour> targetHealths = new List<MonoBehaviour>();
    
    private Rigidbody2D rb;
    private bool isTeleporting = false;
    private float lastEnabledTime;
    private bool hasWaterSpawnPreference;
    private WaterSpawnPreference waterSpawnPreference = WaterSpawnPreference.Neutral;

    public bool IsTeleporting => isTeleporting;

    /// <summary>Sets the random destination bounds. The vectors are normalized so callers can pass either order.</summary>
    public void SetSpawnRange(Vector2 firstCorner, Vector2 secondCorner)
    {
        minRange = Vector2.Min(firstCorner, secondCorner);
        maxRange = Vector2.Max(firstCorner, secondCorner);
    }

    public void SetWaterSpawnPreference(WaterSpawnPreference preference)
    {
        waterSpawnPreference = preference;
        hasWaterSpawnPreference = true;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        // unscaledTime を使うことでタイムスケール停止中の生成にも対応
        lastEnabledTime = Time.unscaledTime;
    }

    /// <summary>
    /// フェード演出を伴うテレポート
    /// useVisualFade を false にすると演出をスキップして即時移動します
    /// </summary>
    public void TeleportWithFade(Action onComplete = null, bool useVisualFade = true)
    {
        if (isTeleporting) return;

        // 生成直後（0.1秒以内）またはフェード不要フラグが立っている場合は即時移動
        if (Time.unscaledTime - lastEnabledTime < 0.1f || !useVisualFade)
        {
            ExecuteSpawnTeleport();
            Physics2D.SyncTransforms();
            onComplete?.Invoke();
        }
        else
        {
            StartCoroutine(TeleportSequence(onComplete));
        }
    }

    public void TeleportToRandomPosition()
    {
        ExecuteSpawnTeleport();
        Physics2D.SyncTransforms();
    }

    private void ExecuteSpawnTeleport()
    {
        if (hasWaterSpawnPreference)
        {
            // If the preferred terrain is absent, leave the enemy where it is;
            // never silently place it on the opposite terrain.
            TryTeleportToRandomPosition(waterSpawnPreference, out _);
            return;
        }

        ExecuteTeleportLogic();
    }

    public bool TryTeleportToRandomPosition(WaterSpawnPreference preference, out bool hasPreferredTerrain)
    {
        List<SpawnCandidate> candidates = CollectSpawnCandidates(preference);
        hasPreferredTerrain = candidates.Count > 0;
        if (!hasPreferredTerrain) return false;

        // タイル候補から無作為に選び、壁や敵の近くを避けます。
        int attempts = Mathf.Min(candidates.Count, 500);
        for (int i = 0; i < attempts; i++)
        {
            int index = UnityEngine.Random.Range(0, candidates.Count);
            SpawnCandidate spawnCandidate = candidates[index];
            candidates[index] = candidates[candidates.Count - 1];
            candidates.RemoveAt(candidates.Count - 1);

            Vector2 candidate = spawnCandidate.position;
            if (IsBlockedByWall(candidate, spawnCandidate.isWater)) continue;
            if (useEnemyCheck && CheckEnemyDanger(candidate)) continue;

            transform.position = new Vector3(candidate.x, candidate.y, transform.position.z);
            Physics2D.SyncTransforms();
            return true;
        }

        return false;
    }

    private bool IsBlockedByWall(Vector2 position, bool isWater)
    {
        Collider2D[] overlaps = Physics2D.OverlapBoxAll(position, checkSize, 0f, wallLayer);
        foreach (Collider2D overlap in overlaps)
        {
            if (isWater)
            {
                TilemapCollider2D tilemapCollider = overlap.GetComponent<TilemapCollider2D>();
                Tilemap tilemap = tilemapCollider != null
                    ? tilemapCollider.GetComponent<Tilemap>()
                    : null;

                // The water tile itself may have a collider on the HB Tilemap.
                // Ignore that collider only when no solid, collidable tile occupies
                // any part of the spawn footprint.
                if (tilemapCollider != null && tilemap != null &&
                    !HasSolidTileInSpawnFootprint(tilemap, position))
                    continue;
            }

            return true;
        }

        return false;
    }

    private bool HasSolidTileInSpawnFootprint(Tilemap tilemap, Vector2 position)
    {
        Vector2 halfSize = checkSize * 0.5f;
        Vector3Int minCell = tilemap.WorldToCell(position - halfSize);
        Vector3Int maxCell = tilemap.WorldToCell(position + halfSize);
        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                TileBase tile = tilemap.GetTile(cell);
                if (tile != null && !IsWaterTile(tile) &&
                    tilemap.GetColliderType(cell) != Tile.ColliderType.None)
                    return true;
            }
        }

        return false;
    }

    private List<SpawnCandidate> CollectSpawnCandidates(WaterSpawnPreference preference)
    {
        List<SpawnCandidate> candidates = new List<SpawnCandidate>();
        Tilemap[] maps = FindObjectsByType<Tilemap>();
        TerrainMap terrainMap = FindAnyObjectByType<TerrainMap>();
        Vector2 rangeMin = Vector2.Min(minRange, maxRange);
        Vector2 rangeMax = Vector2.Max(minRange, maxRange);

        foreach (Tilemap map in maps)
        {
            if (map == null) continue;
            foreach (Vector3Int cell in map.cellBounds.allPositionsWithin)
            {
                TileBase tile = map.GetTile(cell);
                if (tile == null) continue;
                Vector3 center = map.GetCellCenterWorld(cell);
                if (center.x < rangeMin.x || center.x > rangeMax.x ||
                    center.y < rangeMin.y || center.y > rangeMax.y)
                    continue;

                bool isWater = IsWaterTile(tile);
                bool isLand = !isWater && IsLandPosition(center, map, terrainMap);
                bool matches = preference switch
                {
                    WaterSpawnPreference.WaterLoving => isWater,
                    WaterSpawnPreference.LandLoving => isLand,
                    _ => isWater || isLand
                };
                if (matches) candidates.Add(new SpawnCandidate(center, isWater));
            }
        }

        return candidates;
    }

    private static bool IsLandPosition(Vector3 position, Tilemap sourceMap, TerrainMap terrainMap)
    {
        if (terrainMap != null)
            return terrainMap.GetTerrainTypeAtWorldPosition(position) == TerrainType.Floor;

        string mapName = sourceMap.name;
        return mapName.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0
            || mapName.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsWaterTile(TileBase tile)
    {
        return tile is TerrainTile terrainTile && terrainTile.Type == TerrainType.Water
            || tile != null && tile.name == "Water";
    }

    private IEnumerator TeleportSequence(Action onComplete)
    {
        isTeleporting = true;
        
        UpdateTargets();

        StopMovement(true);
        ToggleAttacks(false);
        ToggleHealths(false); 
        ToggleColliders(false);

        // 1. 消えるフェード（タイムスケール無視）
        yield return StartCoroutine(Fade(1f, 0f));

        // 2. 移動実行
        ExecuteSpawnTeleport();
        Physics2D.SyncTransforms();

        // 移動完了後のコールバック
        onComplete?.Invoke();

        // 3. 現れるフェード（タイムスケール無視）
        yield return StartCoroutine(Fade(0f, 1f));

        // --- 復活処理 ---
        ToggleColliders(true);
        Physics2D.SyncTransforms();
        
        ToggleHealths(true); 
        ToggleAttacks(true);
        StopMovement(false);
        
        isTeleporting = false;
    }

    private void UpdateTargets()
    {
        targetRenderers.Clear();
        targetAttacks.Clear();
        targetColliders.Clear();
        targetCanvasGroups.Clear();
        targetHealths.Clear();

        Transform characterRoot = transform;
        var h = GetComponentInParent<EnemyHealth>();
        if (h != null) characterRoot = h.transform;
        else
        {
            var ph = GetComponentInParent<PlayerHealth>();
            if (ph != null) characterRoot = ph.transform;
        }

        targetRenderers.AddRange(characterRoot.GetComponentsInChildren<SpriteRenderer>());
        targetAttacks.AddRange(characterRoot.GetComponentsInChildren<EnemyAttack>());
        
        // Health系コンポーネントの取得
        var enemyHealths = characterRoot.GetComponentsInChildren<EnemyHealth>();
        foreach(var eh in enemyHealths) targetHealths.Add(eh);
        var playerHealths = characterRoot.GetComponentsInChildren<PlayerHealth>();
        foreach(var phh in playerHealths) targetHealths.Add(phh);

        targetColliders.AddRange(characterRoot.GetComponentsInChildren<BoxCollider2D>());

        Slider[] sliders = characterRoot.GetComponentsInChildren<Slider>();
        foreach (var s in sliders)
        {
            CanvasGroup cg = s.GetComponent<CanvasGroup>();
            if (cg == null) cg = s.gameObject.AddComponent<CanvasGroup>();
            if (!targetCanvasGroups.Contains(cg)) targetCanvasGroups.Add(cg);
        }
    }

    private void ToggleColliders(bool state)
    {
        foreach (var col in targetColliders)
        {
            if (col != null) col.enabled = state;
        }
    }

    private void ToggleHealths(bool state)
    {
        foreach (var h in targetHealths)
        {
            if (h != null) h.enabled = state;
        }
    }

    private void StopMovement(bool stop)
    {
        if (rb == null) return;
        if (stop)
        {
            rb.linearVelocity = Vector2.zero;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
        }
        else
        {
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
    }

    private void ToggleAttacks(bool state)
    {
        foreach (var attack in targetAttacks)
        {
            if (attack != null) attack.enabled = state; 
        }
    }

    private IEnumerator Fade(float startAlpha, float endAlpha)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            // Time.timeScale = 0 でも動くように unscaledDeltaTime を使用
            elapsed += Time.unscaledDeltaTime;
            float currentAlpha = Mathf.Lerp(startAlpha, endAlpha, elapsed / fadeDuration);
            
            foreach (var sr in targetRenderers)
            {
                if (sr != null) { Color c = sr.color; c.a = currentAlpha; sr.color = c; }
            }
            foreach (var cg in targetCanvasGroups)
            {
                if (cg != null) cg.alpha = currentAlpha;
            }
            yield return null;
        }
        SetFinalAlpha(endAlpha);
    }

    private void SetFinalAlpha(float alpha)
    {
        foreach (var sr in targetRenderers)
        {
            if (sr != null) { Color c = sr.color; c.a = alpha; sr.color = c; }
        }
        foreach (var cg in targetCanvasGroups)
        {
            if (cg != null) cg.alpha = alpha;
        }
    }

    private void ExecuteTeleportLogic()
    {
        Vector2 randomPos;
        bool isInvalid;
        int safetyNet = 0;

        do
        {
            float randomX = UnityEngine.Random.Range(minRange.x, maxRange.x);
            float randomY = UnityEngine.Random.Range(minRange.y, maxRange.y);
            randomPos = new Vector2(randomX, randomY);

            bool isInsideWall = Physics2D.OverlapBox(randomPos, checkSize, 0f, wallLayer);
            bool isEnemyNearby = false;
            if (useEnemyCheck && !isInsideWall) isEnemyNearby = CheckEnemyDanger(randomPos);

            isInvalid = isInsideWall || isEnemyNearby;
            safetyNet++;
            if (safetyNet > 100) break;

        } while (isInvalid);

        if (!isInvalid)
        {
            transform.position = new Vector3(randomPos.x, randomPos.y, transform.position.z);
        }
    }

    private bool CheckEnemyDanger(Vector2 targetPos)
    {
        Collider2D[] enemies = Physics2D.OverlapCircleAll(targetPos, enemySafetyRadius, enemyLayer);
        foreach (var enemy in enemies)
        {
            if (enemy.transform == this.transform || enemy.transform.IsChildOf(this.transform)) continue;
            
            RaycastHit2D hit = Physics2D.Linecast(targetPos, enemy.transform.position, wallLayer);
            if (hit.collider == null) return true; 
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!enabled || isTeleporting) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, enemySafetyRadius);
    }
}
