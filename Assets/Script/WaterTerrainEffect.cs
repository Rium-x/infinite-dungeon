using UnityEngine;
using UnityEngine.Tilemaps;
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

    private MoveStats moveStats;
    private Transform characterRoot;
    private Transform hitboxRoot;
    private Tilemap[] tilemaps;
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
    private static Shader waterFadeShader;
    private static Material defaultFadeMaterial;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        moveStats = GetComponent<MoveStats>();
        if (moveStats == null) moveStats = GetComponentInChildren<MoveStats>(true);
        characterRoot = FindCharacterRoot();
        hitboxRoot = characterRoot.name == "HB" ? characterRoot : characterRoot.Find("HB");
        colliders = hitboxRoot != null ? hitboxRoot.GetComponents<Collider2D>() : new Collider2D[0];
        tilemaps = FindObjectsByType<Tilemap>();
        RefreshRenderers();
        SetupFollowAnchors();
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
            RefreshRenderers();
    }

    private void Update()
    {
        if (Time.time >= nextWaterCheckTime)
        {
            nextWaterCheckTime = Time.time + waterCheckInterval;
            Vector3 checkPosition = hitboxRoot != null ? hitboxRoot.position : characterRoot.position;
            if (!hasWaterCheckPosition || (checkPosition - lastWaterCheckPosition).sqrMagnitude >= 0.0004f)
            {
                lastWaterCheckPosition = checkPosition;
                hasWaterCheckPosition = true;

                bool wasInWater = isInWater;
                isInWater = IsInWater();
                if (wasInWater != isInWater && moveStats != null)
                    moveStats.SetEnvironmentSpeedMultiplier(isInWater ? waterSpeedMultiplier : 1f);
                targetFollowLowerAmount = isInWater ? waterFollowLowerAmount : 0f;
            }
        }

        float targetWaterFade = isInWater ? 1f : 0f;
        currentWaterFade = Mathf.MoveTowards(
            currentWaterFade,
            targetWaterFade,
            Time.deltaTime / fadeDuration);
        ApplyWaterFade();
    }

    private void SetupFollowAnchors()
    {
        // HBと追従パーツが兄弟オブジェクトでも、同じキャラクター全体に適用します。
        Follow_WithAnimation[] followers = characterRoot.GetComponentsInChildren<Follow_WithAnimation>(true);
        foreach (Follow_WithAnimation follower in followers)
        {
            if (follower == null || follower.FollowTarget == null) continue;

            Transform originalTarget = follower.FollowTarget;
            GameObject anchorObject = new GameObject(follower.name + " Water Follow Anchor");
            anchorObject.transform.position = originalTarget.position;
            followAnchors.Add(new FollowAnchor
            {
                follower = follower,
                originalTarget = originalTarget,
                anchor = anchorObject.transform
            });
            follower.SetFollowTarget(anchorObject.transform);
        }
    }

    private void LateUpdate()
    {
        foreach (FollowAnchor item in followAnchors)
        {
            if (item.follower == null || item.originalTarget == null || item.anchor == null)
                continue;

            item.currentLowerAmount = Mathf.SmoothDamp(
                item.currentLowerAmount,
                targetFollowLowerAmount,
                ref item.lowerAmountVelocity,
                followOffsetSmoothTime);
            item.anchor.position = item.originalTarget.position + Vector3.down * item.currentLowerAmount;
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
        float cutoffY = hitboxRoot != null
            ? hitboxRoot.position.y - 0.6f
            : characterRoot.position.y - 0.6f;

        foreach (RendererState state in renderers)
        {
            if (state.renderer == null) continue;
            if (!isInWater && currentWaterFade <= 0.001f) continue;
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

    private bool IsInWater()
    {
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
        float halfCellDiagonal = Vector2.Distance(
            tilemap.CellToWorld(Vector3Int.zero),
            tilemap.CellToWorld(Vector3Int.one)) * 0.5f;

        for (int x = Mathf.Min(minCell.x, maxCell.x); x <= Mathf.Max(minCell.x, maxCell.x); x++)
        {
            for (int y = Mathf.Min(minCell.y, maxCell.y); y <= Mathf.Max(minCell.y, maxCell.y); y++)
            {
                Vector3Int cell = new Vector3Int(x, y, tilemap.WorldToCell(bounds.center).z);
                if (!IsWaterTile(tilemap.GetTile(cell))) continue;

                Vector2 tileCenter = tilemap.GetCellCenterWorld(cell);
                if (characterCollider.OverlapPoint(tileCenter)) return true;

                Vector2 closestPoint = characterCollider.ClosestPoint(tileCenter);
                if (Vector2.Distance(tileCenter, closestPoint) <= halfCellDiagonal + 0.001f)
                    return true;
            }
        }

        return false;
    }

    private Transform FindCharacterRoot()
    {
        if (GetComponentInChildren<EnemyInitializer>(true) != null)
            return transform;

        for (Transform current = transform; current != null; current = current.parent)
        {
            if (current.GetComponent<EnemyInitializer>() != null)
                return current;

            if (current.name == "Player" && current.CompareTag("Player"))
                return current;
        }

        return transform;
    }

    private static bool IsWaterTile(TileBase tile)
    {
        return tile is TerrainTile terrainTile && terrainTile.Type == TerrainType.Water
            || tile != null && tile.name == "Water";
    }

    private void OnDestroy()
    {
        foreach (FollowAnchor item in followAnchors)
        {
            if (item.follower != null && item.originalTarget != null)
                item.follower.SetFollowTarget(item.originalTarget);
            if (item.anchor != null)
                Destroy(item.anchor.gameObject);
        }

        foreach (RendererState state in renderers)
        {
            if (state.renderer != null)
                state.renderer.sharedMaterial = state.originalMaterial;
        }
    }
}
