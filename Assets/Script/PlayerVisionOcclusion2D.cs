using System.Collections.Generic;
using UnityEngine;

public sealed class  PlayerVisionOcclusion2D: MonoBehaviour
{
    public enum OcclusionMode
    {
        FadeToTransparent, // 透明にして見えなくする
        FadeToBlack        // 黒にして見えなくする
    }

    [Header("視界の起点")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform eyePoint;

    [Header("遮蔽物")]
    [SerializeField] private LayerMask occluderLayers;
    [SerializeField] private float refreshInterval = 0.05f;
    [SerializeField] private float targetScanInterval = 0.5f;

    [Header("非表示にする対象")]
    [SerializeField] private LayerMask targetLayers;
    [SerializeField] private float rayStartOffset = 0.01f;
    [SerializeField] private float targetContactTolerance = 0.15f;
    [SerializeField] private float visibilitySampleMargin = 0.02f;
    
    [Header("表示設定")]
    [SerializeField] private OcclusionMode occlusionMode = OcclusionMode.FadeToTransparent;
    [SerializeField, Tooltip("フェード（徐々に変化）させるかどうか")] private bool useFade = true;
    [SerializeField, Tooltip("フェードの速度")] private float fadeSpeed = 8f;
    
    [Header("デバッグ")]
    [SerializeField] private bool showDiagnostics;

    private readonly Dictionary<SpriteRenderer, VisionState> states = new();
    private readonly Dictionary<SpriteRenderer, EnemyHealth> enemyByRenderer = new();
    private readonly List<SpriteRenderer> targetRenderers = new();
    private readonly List<SpriteRenderer> removedRenderers = new();
    private readonly HashSet<SpriteRenderer> activeRenderers = new();
    private readonly HashSet<EnemyHealth> visibleEnemies = new();
    private readonly RaycastHit2D[] raycastHits = new RaycastHit2D[16];
    
    private float nextRefreshTime;
    private float nextTargetScanTime;
    private float nextDiagnosticsTime;
    private float nextPlayerSearchTime;
    private bool restoredWithoutPlayer;

    private MaterialPropertyBlock mpb;
    private Camera mainCamera;
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        mainCamera = Camera.main;

        if (occluderLayers.value == 0)
        {
            occluderLayers = LayerMask.GetMask("Wall", "Enemy", "Chest", "Item");
        }

        if (targetLayers.value == 0)
        {
            targetLayers = LayerMask.GetMask("Default", "Enemy", "Chest", "Item");
        }

        if (showDiagnostics)
        {
            Debug.Log(
                $"[PlayerVisionOcclusion2D] 遮蔽物: {LayerMaskToNames(occluderLayers)} / " +
                $"対象: {LayerMaskToNames(targetLayers)}",
                this);
        }
    }

    private void LateUpdate()
    {
        if (!TryFindPlayer())
        {
            if (!restoredWithoutPlayer)
            {
                RestoreAll();
                restoredWithoutPlayer = true;
            }
            return;
        }
        restoredWithoutPlayer = false;

        if (Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.01f, refreshInterval);
            Vector2 origin = eyePoint != null ? eyePoint.position : player.position;
            RefreshTargetRenderersIfNeeded();
            activeRenderers.Clear();
            visibleEnemies.Clear();
            int hiddenCount = 0;

            foreach (SpriteRenderer renderer in targetRenderers)
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                activeRenderers.Add(renderer);
                VisionState state = GetState(renderer);

                if (enemyByRenderer.TryGetValue(renderer, out EnemyHealth enemy) && enemy != null)
                {
                    if (!visibleEnemies.Contains(enemy) &&
                        IsOnScreen(renderer) &&
                        HasAnyVisiblePoint(origin, renderer))
                    {
                        visibleEnemies.Add(enemy);
                    }
                }
                else
                {
                    state.targetVisibility =
                        !IsOnScreen(renderer) || HasAnyVisiblePoint(origin, renderer) ? 1f : 0f;
                }
            }

            foreach (SpriteRenderer renderer in targetRenderers)
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (enemyByRenderer.TryGetValue(renderer, out EnemyHealth enemy) && enemy != null)
                {
                    VisionState state = GetState(renderer);
                    state.targetVisibility = visibleEnemies.Contains(enemy) ? 1f : 0f;
                    if (state.targetVisibility <= 0f)
                    {
                        hiddenCount++;
                    }
                }
                else if (GetState(renderer).targetVisibility <= 0f)
                {
                    hiddenCount++;
                }
            }

            RestoreMissingRenderers(activeRenderers);

            if (showDiagnostics && Time.unscaledTime >= nextDiagnosticsTime)
            {
                nextDiagnosticsTime = Time.unscaledTime + 1f;
                Debug.Log(
                    $"[PlayerVisionOcclusion2D] 対象Sprite: {activeRenderers.Count}, " +
                    $"非表示: {hiddenCount}, Player: {player.name}, " +
                    $"Eye: {origin}",
                    this);
            }
        }

        UpdateVisibilityStates();
    }

    private bool TryFindPlayer()
    {
        if (player == null)
        {
            if (Time.unscaledTime < nextPlayerSearchTime)
                return false;
            nextPlayerSearchTime = Time.unscaledTime + 0.5f;
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        if (player == null)
        {
            return false;
        }

        if (eyePoint == null)
        {
            eyePoint = player.Find("HB");
        }

        return true;
    }

    private void RefreshTargetRenderersIfNeeded()
    {
        if (Time.unscaledTime < nextTargetScanTime)
        {
            return;
        }

        nextTargetScanTime = Time.unscaledTime + Mathf.Max(0.1f, targetScanInterval);
        targetRenderers.Clear();
        enemyByRenderer.Clear();

        // Instance-ID sorting is unnecessary here: visibility checks are independent
        // per renderer, and sorting every renderer in the scene adds avoidable CPU work.
        SpriteRenderer[] renderers = FindObjectsByType<SpriteRenderer>();
        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer != null && IsTarget(renderer) && !IsInPlayer(renderer.transform))
            {
                targetRenderers.Add(renderer);
                enemyByRenderer.Add(renderer, renderer.GetComponentInParent<EnemyHealth>());
            }
        }
    }

    private bool IsTarget(SpriteRenderer renderer)
    {
        return (targetLayers.value & (1 << renderer.gameObject.layer)) != 0;
    }

    private bool IsInPlayer(Transform target)
    {
        return target == player || target.IsChildOf(player);
    }

    private bool IsOnScreen(SpriteRenderer renderer)
    {
        if (mainCamera == null) return true;

        Bounds bounds = renderer.bounds;
        Vector3 center = mainCamera.WorldToViewportPoint(bounds.center);
        Vector3 upperRight = mainCamera.WorldToViewportPoint(bounds.max);
        float extentX = Mathf.Abs(upperRight.x - center.x);
        float extentY = Mathf.Abs(upperRight.y - center.y);
        return center.z >= 0f &&
               center.x + extentX >= 0f && center.x - extentX <= 1f &&
               center.y + extentY >= 0f && center.y - extentY <= 1f;
    }

    private bool HasAnyVisiblePoint(Vector2 origin, SpriteRenderer renderer)
    {
        Bounds bounds = renderer.bounds;
        Vector2 center = bounds.center;
        Vector2 extent = bounds.extents;
        float margin = Mathf.Clamp(visibilitySampleMargin, 0f, Mathf.Min(extent.x, extent.y));
        Vector2 xOffset = new(Mathf.Max(0f, extent.x - margin), 0f);
        Vector2 yOffset = new(0f, Mathf.Max(0f, extent.y - margin));

        return HasLineOfSight(origin, center, renderer.transform) ||
               HasLineOfSight(origin, center + xOffset + yOffset, renderer.transform) ||
               HasLineOfSight(origin, center + xOffset - yOffset, renderer.transform) ||
               HasLineOfSight(origin, center - xOffset + yOffset, renderer.transform) ||
               HasLineOfSight(origin, center - xOffset - yOffset, renderer.transform);
    }

    private bool HasLineOfSight(Vector2 origin, Vector2 target, Transform targetTransform)
    {
        Vector2 difference = target - origin;
        float distance = difference.magnitude;

        if (distance <= rayStartOffset)
        {
            return true;
        }

        Vector2 direction = difference / distance;
        Vector2 rayOrigin = origin + direction * rayStartOffset;
        int hitCount = Physics2D.RaycastNonAlloc(
            rayOrigin,
            direction,
            raycastHits,
            distance - rayStartOffset,
            occluderLayers);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = raycastHits[i];
            if (hit.collider == null)
            {
                continue;
            }

            Transform hitTransform = hit.collider.transform;
            if (IsPartOfTarget(hitTransform, targetTransform))
            {
                continue;
            }

            if (player != null && (hitTransform == player || hitTransform.IsChildOf(player)))
            {
                continue;
            }

            float blockingDistance = distance - rayStartOffset - targetContactTolerance;
            if (hit.distance < blockingDistance)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsPartOfTarget(Transform hitTransform, Transform targetTransform)
    {
        return hitTransform == targetTransform ||
               hitTransform.IsChildOf(targetTransform) ||
               targetTransform.IsChildOf(hitTransform);
    }

    private VisionState GetState(SpriteRenderer renderer)
    {
        if (!states.TryGetValue(renderer, out VisionState state))
        {
            state = new VisionState(renderer.enabled);
            states.Add(renderer, state);
        }

        return state;
    }

    private void RestoreMissingRenderers(HashSet<SpriteRenderer> activeRenderers)
    {
        removedRenderers.Clear();

        foreach (KeyValuePair<SpriteRenderer, VisionState> pair in states)
        {
            SpriteRenderer renderer = pair.Key;
            VisionState state = pair.Value;

            if (renderer == null)
            {
                removedRenderers.Add(renderer);
                continue;
            }

            if (!activeRenderers.Contains(renderer))
            {
                state.targetVisibility = 1f;

                if (Mathf.Approximately(state.currentVisibility, 1f))
                {
                    if (renderer.HasPropertyBlock())
                    {
                        renderer.SetPropertyBlock(null);
                    }
                    removedRenderers.Add(renderer);
                }
            }
        }

        foreach (SpriteRenderer renderer in removedRenderers)
        {
            states.Remove(renderer);
        }
    }

    private void UpdateVisibilityStates()
    {
        foreach (KeyValuePair<SpriteRenderer, VisionState> pair in states)
        {
            if (pair.Key != null)
            {
                UpdateVisibility(pair.Key, pair.Value);
            }
        }
    }

    private void UpdateVisibility(SpriteRenderer renderer, VisionState state)
    {
        if (useFade)
        {
            state.currentVisibility = Mathf.MoveTowards(
                state.currentVisibility,
                state.targetVisibility,
                Mathf.Max(0.01f, fadeSpeed) * Time.unscaledDeltaTime);
        }
        else
        {
            state.currentVisibility = state.targetVisibility;
        }

        if (Mathf.Approximately(state.currentVisibility, state.lastAppliedVisibility))
            return;

        renderer.GetPropertyBlock(mpb);
        Color overlayColor = Color.white;

        if (occlusionMode == OcclusionMode.FadeToTransparent)
        {
            overlayColor.a = state.currentVisibility;
        }
        else if (occlusionMode == OcclusionMode.FadeToBlack)
        {
            overlayColor.r = state.currentVisibility;
            overlayColor.g = state.currentVisibility;
            overlayColor.b = state.currentVisibility;
        }

        mpb.SetColor(ColorPropertyId, overlayColor);
        renderer.SetPropertyBlock(mpb);
        state.lastAppliedVisibility = state.currentVisibility;
    }

    private void RestoreAll()
    {
        foreach (KeyValuePair<SpriteRenderer, VisionState> pair in states)
        {
            if (pair.Key != null)
            {
                pair.Key.enabled = pair.Value.originalEnabled;
                if (pair.Key.HasPropertyBlock())
                {
                    pair.Key.SetPropertyBlock(null);
                }
                pair.Value.lastAppliedVisibility = float.NaN;
            }
        }
    }

    private void OnDestroy()
    {
        RestoreAll();
    }

    private static string LayerMaskToNames(LayerMask mask)
    {
        List<string> names = new();

        for (int layer = 0; layer < 32; layer++)
        {
            if ((mask.value & (1 << layer)) != 0)
            {
                names.Add(LayerMask.LayerToName(layer));
            }
        }

        return names.Count == 0 ? "なし" : string.Join(", ", names);
    }

    private sealed class VisionState
    {
        public readonly bool originalEnabled;
        public float currentVisibility = 1f;
        public float targetVisibility = 1f;
        public float lastAppliedVisibility = float.NaN;

        public VisionState(bool originalEnabled)
        {
            this.originalEnabled = originalEnabled;
        }
    }
}
