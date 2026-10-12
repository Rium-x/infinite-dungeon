using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>水タイル上にいるキャラクターの移動速度だけを補正します。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class WaterTerrainEffect : MonoBehaviour
{
    [SerializeField, Range(0.1f, 1f)] private float waterSpeedMultiplier = 0.5f;
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.2f;
    [SerializeField, Min(0f)] private float waterFollowLowerAmount = 0.6f;
    [SerializeField, Min(0.01f)] private float followOffsetSmoothTime = 0.25f;
    [SerializeField, Min(0.02f)] private float waterCheckInterval = 0.1f;

    private float monsterWaterSpeedPercent = 50f;
    private bool damageInWater;
    private float waterDamagePerSecond = 5f;
    private bool damageOutsideWater;
    private float outsideWaterDamagePerSecond = 5f;
    private bool avoidWater;
    private bool cannotLeaveWater;
    private readonly List<GameObject> individualWaterFadeObjects = new List<GameObject>();
    private float terrainDamageInterval = 1f;

    private MoveStats moveStats;
    private Transform characterRoot;
    private Transform hitboxRoot;
    private Tilemap[] tilemaps;
    private TerrainMap terrainMap;
    private Collider2D[] colliders;
    private readonly List<RendererState> renderers = new List<RendererState>();
    private readonly List<FollowAnchor> followAnchors = new List<FollowAnchor>();
    private MaterialPropertyBlock propertyBlock;
    private float currentWaterFade;
    private float targetFollowLowerAmount;
    private float nextWaterCheckTime;
    private bool isInWater;
    private bool hasWaterCheckPosition;
    private Vector3 lastWaterCheckPosition;
    private EnemyHealth enemyHealth;
    private float nextTerrainDamageTime;

    private sealed class RendererState
    {
        public SpriteRenderer renderer;
        public Material originalMaterial;
        public Material fadeMaterial;
        public float lastCutoffY = float.NaN;
        public float lastWaterFade = float.NaN;
    }

    private sealed class FollowAnchor
    {
        public Follow_WithAnimation follower;
        public Transform originalTarget;
        public Transform anchor;
        public float currentLowerAmount;
        public float lowerAmountVelocity;
    }

    private static readonly int WaterCutoffYId = Shader.PropertyToID("_WaterCutoffY");
    private static readonly int WaterFadeId = Shader.PropertyToID("_WaterFade");
    private static readonly Dictionary<Material, Material> SharedFadeMaterials = new Dictionary<Material, Material>();
    private static Tilemap[] cachedSceneTilemaps;
    private static TerrainMap cachedSceneTerrainMap;
    private static ulong cachedTerrainSceneHandle = ulong.MaxValue;
    private static int cachedTerrainGenerationVersion = int.MinValue;
    private static Shader waterFadeShader;
    private static Material defaultFadeMaterial;

    private void Awake()
    {
        characterRoot = FindCharacterRoot();
        if (characterRoot != transform)
        {
            // MoveStatsがパーツ上にある場合も、水処理はキャラクター全体に1つだけ置きます。
            EnsureForCharacter(characterRoot);
            Destroy(this);
            return;
        }

        WaterTerrainEffect[] effects = characterRoot.GetComponentsInChildren<WaterTerrainEffect>(true);
        foreach (WaterTerrainEffect effect in effects)
        {
            if (effect != this && effect != null)
            {
                effect.RestoreRuntimeChanges();
                Destroy(effect);
            }
        }

        propertyBlock = new MaterialPropertyBlock();
        moveStats = GetComponent<MoveStats>();
        if (moveStats == null) moveStats = GetComponentInChildren<MoveStats>(true);
        enemyHealth = GetComponentInChildren<EnemyHealth>(true);
        hitboxRoot = characterRoot.name == "HB" ? characterRoot : characterRoot.Find("HB");
        colliders = hitboxRoot != null ? hitboxRoot.GetComponents<Collider2D>() : new Collider2D[0];
        CacheSceneTerrain();
        RefreshRenderers();
        SetupFollowAnchors();
    }

    private void CacheSceneTerrain()
    {
        ulong sceneHandle = SceneManager.GetActiveScene().handle.GetRawData();
        int generationVersion = BSPDungeonMaster.GenerationVersion;
        if (cachedSceneTilemaps == null || cachedTerrainSceneHandle != sceneHandle ||
            cachedTerrainGenerationVersion != generationVersion)
        {
            cachedSceneTilemaps = FindObjectsByType<Tilemap>();
            cachedSceneTerrainMap = FindAnyObjectByType<TerrainMap>();
            cachedTerrainSceneHandle = sceneHandle;
            cachedTerrainGenerationVersion = generationVersion;
        }

        tilemaps = cachedSceneTilemaps;
        terrainMap = cachedSceneTerrainMap;
    }

    public static WaterTerrainEffect EnsureForCharacter(Transform requester)
    {
        if (requester == null) return null;
        Transform root = ResolveCharacterRoot(requester);
        WaterTerrainEffect effect = root.GetComponent<WaterTerrainEffect>();
        if (effect == null)
            effect = root.gameObject.AddComponent<WaterTerrainEffect>();
        return effect;
    }

    private void OnEnable()
    {
        EquipmentManager.EquipmentVisualsChanged += OnEquipmentVisualsChanged;
    }

    private void OnDisable()
    {
        EquipmentManager.EquipmentVisualsChanged -= OnEquipmentVisualsChanged;
    }

    private void OnEquipmentVisualsChanged(Transform slot)
    {
        if (slot == null || characterRoot == null) return;
        if (slot == characterRoot || slot.IsChildOf(characterRoot))
        {
            RefreshRenderers();
            SetupFollowAnchors();
        }
    }

    private void Update()
    {
        if (Time.time >= nextWaterCheckTime)
        {
            nextWaterCheckTime = Time.time + waterCheckInterval;
            Vector3 checkPosition = hitboxRoot != null ? hitboxRoot.position : characterRoot.position;
            if (!hasWaterCheckPosition || (checkPosition - lastWaterCheckPosition).sqrMagnitude >= 0.0004f)
            {
                bool isInitialWaterCheck = !hasWaterCheckPosition;
                lastWaterCheckPosition = checkPosition;
                hasWaterCheckPosition = true;

                bool wasInWater = isInWater;
                isInWater = IsInWater();
                if (isInitialWaterCheck)
                    nextTerrainDamageTime = Time.time + terrainDamageInterval;
                if (moveStats != null && (wasInWater != isInWater || enemyHealth != null))
                    moveStats.SetEnvironmentSpeedMultiplier(GetWaterSpeedMultiplier());
                targetFollowLowerAmount = isInWater ? waterFollowLowerAmount : 0f;
            }
        }

        ApplyTerrainDamage();

        float targetWaterFade = isInWater ? 1f : 0f;
        currentWaterFade = Mathf.MoveTowards(
            currentWaterFade,
            targetWaterFade,
            Time.deltaTime / fadeDuration);
        ApplyWaterFade();
    }

    private float GetWaterSpeedMultiplier()
    {
        return isInWater
            ? (enemyHealth != null ? monsterWaterSpeedPercent / 100f : waterSpeedMultiplier)
            : 1f;
    }

    private void ApplyTerrainDamage()
    {
        if (enemyHealth == null || !hasWaterCheckPosition || Time.time < nextTerrainDamageTime) return;
        nextTerrainDamageTime = Time.time + terrainDamageInterval;
        if (isInWater && damageInWater)
            enemyHealth.TakeDamage(waterDamagePerSecond * terrainDamageInterval);
        else if (!isInWater && damageOutsideWater)
            enemyHealth.TakeDamage(outsideWaterDamagePerSecond * terrainDamageInterval);
    }

    public bool IsInWaterNow => isInWater;
    public bool AvoidsWater => avoidWater;

    public float GetFollowLowerAmountFor(Follow_WithAnimation follower)
    {
        if (follower == null) return 0f;
        foreach (FollowAnchor item in followAnchors)
        {
            if (item.follower == follower)
                return item.currentLowerAmount;
        }
        return 0f;
    }

    public void ConfigureMonsterWater(float speedPercent, bool waterDamageEnabled, float waterDamage,
        bool outsideDamageEnabled, float outsideDamage, bool avoidsWater, bool cannotLeave,
        List<GameObject> individuallyCutObjects)
    {
        monsterWaterSpeedPercent = Mathf.Max(0f, speedPercent);
        damageInWater = waterDamageEnabled;
        waterDamagePerSecond = Mathf.Max(0f, waterDamage);
        damageOutsideWater = outsideDamageEnabled;
        outsideWaterDamagePerSecond = Mathf.Max(0f, outsideDamage);
        avoidWater = avoidsWater;
        cannotLeaveWater = cannotLeave;
        individualWaterFadeObjects.Clear();
        if (individuallyCutObjects != null)
            individualWaterFadeObjects.AddRange(individuallyCutObjects);
        RefreshRenderers();
    }

    public bool AllowsWaterDirection(Vector2 direction)
    {
        // Most characters have no water restriction. Skip every terrain lookup for them.
        if (enemyHealth == null || (!avoidWater && !cannotLeaveWater) ||
            direction.sqrMagnitude < 0.0001f)
            return true;

        Vector2 origin = hitboxRoot != null ? hitboxRoot.position : transform.position;
        // isInWater is refreshed by Update; reusing it avoids another terrain query
        // for every candidate direction tested by the enemy's steering AI.
        bool startsInWater = isInWater;
        if ((!avoidWater || startsInWater) && (!cannotLeaveWater || !startsInWater))
            return true;

        Vector2 normalizedDirection = direction.normalized;
        // Two direct tile lookups cover the next cell and a one-unit lookahead.
        // This replaces a physics raycast plus a loop of tilemap samples per AI ray.
        bool midpointIsWater = IsWaterAtWorldPosition(origin + normalizedDirection * 0.5f);
        bool endpointIsWater = IsWaterAtWorldPosition(origin + normalizedDirection);
        if (avoidWater && !startsInWater && (midpointIsWater || endpointIsWater)) return false;
        if (cannotLeaveWater && startsInWater && (!midpointIsWater || !endpointIsWater)) return false;
        return true;
    }

    private bool IsWaterAtWorldPosition(Vector2 position)
    {
        if (terrainMap != null)
        {
            TerrainType terrain = terrainMap.GetTerrainTypeAtWorldPosition(position);
            if (terrain != TerrainType.Unknown)
                return terrain == TerrainType.Water;
        }

        if (tilemaps == null) return false;
        foreach (Tilemap tilemap in tilemaps)
        {
            if (tilemap == null) continue;
            if (IsWaterTile(tilemap.GetTile(tilemap.WorldToCell(position))))
                return true;
        }
        return false;
    }

    private void SetupFollowAnchors()
    {
        // 追従先だけ記録し、実際に水へ入るまでアンカーのGameObjectは作りません。
        Follow_WithAnimation[] followers = characterRoot.GetComponentsInChildren<Follow_WithAnimation>(true);
        foreach (Follow_WithAnimation follower in followers)
        {
            if (follower == null || follower.FollowTarget == null) continue;
            bool alreadyRegistered = false;
            foreach (FollowAnchor existing in followAnchors)
            {
                if (existing.follower == follower)
                {
                    alreadyRegistered = true;
                    break;
                }
            }
            if (alreadyRegistered) continue;

            followAnchors.Add(new FollowAnchor
            {
                follower = follower,
                originalTarget = follower.FollowTarget
            });
        }
    }

    private void LateUpdate()
    {
        foreach (FollowAnchor item in followAnchors)
        {
            if (item.follower == null || item.originalTarget == null)
                continue;

            item.currentLowerAmount = Mathf.SmoothDamp(
                item.currentLowerAmount,
                targetFollowLowerAmount,
                ref item.lowerAmountVelocity,
                followOffsetSmoothTime);

            if (item.anchor == null && item.currentLowerAmount > 0.001f)
            {
                GameObject anchorObject = new GameObject(item.follower.name + " Water Follow Anchor");
                item.anchor = anchorObject.transform;
                item.anchor.SetParent(characterRoot, true);
                item.anchor.position = item.originalTarget.position;
                item.follower.SetFollowTarget(item.anchor);
            }

            if (item.anchor == null) continue;
            item.anchor.position = item.originalTarget.position + Vector3.down * item.currentLowerAmount;

            if (targetFollowLowerAmount <= 0.001f && item.currentLowerAmount <= 0.001f)
            {
                if (item.follower.FollowTarget == item.anchor)
                    item.follower.SetFollowTarget(item.originalTarget);
                Destroy(item.anchor.gameObject);
                item.anchor = null;
            }
        }
    }

    private void RefreshRenderers()
    {
        SpriteRenderer[] children = characterRoot.GetComponentsInChildren<SpriteRenderer>(true);
        if (waterFadeShader == null)
            waterFadeShader = Shader.Find("Custom/Water Half Fade Sprite Lit");
        if (waterFadeShader == null) return;

        foreach (SpriteRenderer child in children)
        {
            // 手のパーツは水の境界で切らず、元のマテリアルのまま表示します。
            if (IsHandRenderer(child) && !IsIndividuallyCutRenderer(child)) continue;

            bool alreadyRegistered = false;
            foreach (RendererState state in renderers)
            {
                if (state.renderer == child)
                {
                    alreadyRegistered = true;
                    break;
                }
            }
            if (alreadyRegistered) continue;

            Material original = child.sharedMaterial;
            Material fadeMaterial = GetSharedFadeMaterial(original);
            child.sharedMaterial = fadeMaterial;
            renderers.Add(new RendererState
            {
                renderer = child,
                originalMaterial = original,
                fadeMaterial = fadeMaterial
            });
        }
    }

    private static Material GetSharedFadeMaterial(Material original)
    {
        if (original != null && SharedFadeMaterials.TryGetValue(original, out Material cached))
            return cached;
        if (original == null && defaultFadeMaterial != null)
            return defaultFadeMaterial;

        Material fadeMaterial = original != null
            ? new Material(original)
            : new Material(waterFadeShader);
        fadeMaterial.shader = waterFadeShader;

        if (original != null)
            SharedFadeMaterials.Add(original, fadeMaterial);
        else
            defaultFadeMaterial = fadeMaterial;
        return fadeMaterial;
    }

    private void ApplyWaterFade()
    {
        float characterCutoffY = hitboxRoot != null
            ? hitboxRoot.position.y - 0.6f
            : characterRoot.position.y - 0.6f;

        foreach (RendererState state in renderers)
        {
            if (state.renderer == null) continue;
            bool individuallyCut = IsIndividuallyCutRenderer(state.renderer);
            if (IsHandRenderer(state.renderer) && !individuallyCut) continue;
            if (!isInWater && currentWaterFade <= 0.001f) continue;
            float cutoffY = individuallyCut
                ? state.renderer.bounds.center.y
                : characterCutoffY;
            if (Mathf.Abs(state.lastCutoffY - cutoffY) < 0.01f &&
                Mathf.Abs(state.lastWaterFade - currentWaterFade) < 0.001f)
                continue;
            state.renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(WaterCutoffYId, cutoffY);
            propertyBlock.SetFloat(WaterFadeId, currentWaterFade);

            state.renderer.SetPropertyBlock(propertyBlock);
            state.lastCutoffY = cutoffY;
            state.lastWaterFade = currentWaterFade;
        }
    }

    private bool IsIndividuallyCutRenderer(SpriteRenderer spriteRenderer)
    {
        if (spriteRenderer == null) return false;

        Transform rendererTransform = spriteRenderer.transform;
        foreach (GameObject individualObject in individualWaterFadeObjects)
        {
            if (individualObject == null) continue;
            Transform individualTransform = individualObject.transform;
            if (rendererTransform == individualTransform || rendererTransform.IsChildOf(individualTransform))
                return true;
        }

        return false;
    }

    private bool IsHandRenderer(SpriteRenderer spriteRenderer)
    {
        if (spriteRenderer == null) return false;

        // SpriteRendererの子オブジェクトに名前が付いている場合も拾えるよう、
        // キャラクター直下までの親名を確認します。
        for (Transform current = spriteRenderer.transform;
             current != null && (characterRoot == null || current == characterRoot || current.IsChildOf(characterRoot));
             current = current.parent)
        {
            string objectName = current.name.ToLowerInvariant();
            if (objectName.Contains("hand") || objectName.Contains("右手") || objectName.Contains("左手"))
                return true;
            if (current == characterRoot) break;
        }

        return false;
    }

    private bool IsInWater()
    {
        // Enemy water affinity only needs the character's terrain cell. Avoid scanning
        // every collider against every Tilemap for every water-sensitive enemy.
        if (enemyHealth != null && terrainMap != null)
        {
            Vector3 position = hitboxRoot != null ? hitboxRoot.position : characterRoot.position;
            TerrainType terrain = terrainMap.GetTerrainTypeAtWorldPosition(position);
            if (terrain != TerrainType.Unknown)
                return terrain == TerrainType.Water;
        }

        if (tilemaps == null) return false;

        foreach (Tilemap tilemap in tilemaps)
        {
            if (tilemap == null) continue;

            if (colliders != null && colliders.Length > 0)
            {
                foreach (Collider2D characterCollider in colliders)
                {
                    if (characterCollider != null && characterCollider.enabled
                        && characterCollider.gameObject.activeInHierarchy
                        && ColliderTouchesWaterTile(characterCollider, tilemap))
                        return true;
                }
            }
            else if (hitboxRoot != null
                && IsWaterTile(tilemap.GetTile(tilemap.WorldToCell(hitboxRoot.position))))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ColliderTouchesWaterTile(Collider2D characterCollider, Tilemap tilemap)
    {
        Bounds bounds = characterCollider.bounds;
        Vector3Int minCell = tilemap.WorldToCell(bounds.min);
        Vector3Int maxCell = tilemap.WorldToCell(bounds.max);

        for (int x = Mathf.Min(minCell.x, maxCell.x); x <= Mathf.Max(minCell.x, maxCell.x); x++)
        {
            for (int y = Mathf.Min(minCell.y, maxCell.y); y <= Mathf.Max(minCell.y, maxCell.y); y++)
            {
                Vector3Int cell = new Vector3Int(x, y, tilemap.WorldToCell(bounds.center).z);
                if (!IsWaterTile(tilemap.GetTile(cell))) continue;

                Vector2 tileCenter = tilemap.GetCellCenterWorld(cell);
                if (characterCollider.OverlapPoint(tileCenter)) return true;

                Vector2 closestPoint = characterCollider.ClosestPoint(tileCenter);
                if (tilemap.WorldToCell(closestPoint) == cell)
                    return true;
            }
        }

        return false;
    }

    private Transform FindCharacterRoot()
    {
        return ResolveCharacterRoot(transform);
    }

    private static Transform ResolveCharacterRoot(Transform requester)
    {
        Transform taggedPlayer = null;
        for (Transform current = requester; current != null; current = current.parent)
        {
            // PlayerのHBや各パーツにもPlayerタグが付くため、名前付きの全体Rootを優先します。
            if (current.name == "Player")
                return current;

            if (taggedPlayer == null && current.CompareTag("Player"))
                taggedPlayer = current;

            EnemyInitializer initializer = current.GetComponent<EnemyInitializer>();
            if (initializer != null)
                return initializer.transform.parent != null ? initializer.transform.parent : current;

            initializer = current.GetComponentInChildren<EnemyInitializer>(true);
            if (initializer != null && initializer.transform.parent != null)
                return initializer.transform.parent;
        }

        return taggedPlayer != null ? taggedPlayer : requester;
    }

    private static bool IsWaterTile(TileBase tile)
    {
        return tile is TerrainTile terrainTile && terrainTile.Type == TerrainType.Water
            || tile != null && tile.name == "Water";
    }

    private void OnDestroy()
    {
        RestoreRuntimeChanges();
    }

    private void RestoreRuntimeChanges()
    {
        foreach (FollowAnchor item in followAnchors)
        {
            if (item.follower != null && item.originalTarget != null)
                item.follower.SetFollowTarget(item.originalTarget);
            if (item.anchor != null)
                Destroy(item.anchor.gameObject);
        }
        followAnchors.Clear();

        foreach (RendererState state in renderers)
        {
            if (state.renderer != null)
                state.renderer.sharedMaterial = state.originalMaterial;
        }
        renderers.Clear();
    }
}
