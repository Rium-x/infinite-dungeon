using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class EnemyAI : MonoBehaviour
{
    // All enemies share one player lookup. Searching the whole scene once per enemy
    // quickly becomes expensive in stages with many spawned enemies.
    private static Transform sharedPlayer;
    private static WaterTerrainEffect sharedPlayerWaterEffect;
    private static float nextSharedPlayerSearchTime;
    
private MoveStats moveStats;
    private WaterTerrainEffect waterEffect;
    private WaterTerrainEffect playerWaterEffect;
    [SerializeField] private EnemyAIState currentState = EnemyAIState.Investigate;
    private bool movementPausedByCompanion;
    private readonly List<EnemyAIExtension> extensions = new List<EnemyAIExtension>();
    private EnemyAIExtension activeCustomExtension;
    private EnemyAIExtension commandOwner;
    private string activeCustomStateId;
    private float extensionBaseSpeed = -1f;

    public EnemyAIState CurrentState => currentState;
    public string ActiveCustomStateId => activeCustomStateId;
    public Transform PlayerTransform => player;
    public bool CanSeePlayer => canSeePlayer;
    public bool IsInCustomState => activeCustomExtension != null;
    public Vector2 CenterPosition => CenterPos;
    public Vector2 LastKnownPlayerPosition => lastPlayerPos;
    public Vector2 DirectionToPlayer => player != null && player.gameObject.activeInHierarchy
        ? (((Vector2)player.position + playerPositionOffset) - CenterPos).normalized
        : (((Vector2)lastPlayerPos) - CenterPos).normalized;
    public float DistanceToPlayer => Vector2.Distance(CenterPos,
        player != null && player.gameObject.activeInHierarchy
            ? (Vector2)player.position + playerPositionOffset
            : (Vector2)lastPlayerPos);

    /// <summary>Companion AIs can observe the base AI state without owning its state machine.</summary>
    public bool IsWandering => currentState == EnemyAIState.Wander;
    public bool IsSearching => currentState == EnemyAIState.Search;
    public bool IsInvestigating => currentState == EnemyAIState.Investigate;
    public bool IsChasing => currentState == EnemyAIState.Chase;
    public bool IsFleeing => currentState == EnemyAIState.Flee;

    /// <summary>Temporarily pauses base-AI movement while a companion animation is playing.</summary>
    public void SetMovementPausedByCompanion(bool paused)
    {
        movementPausedByCompanion = paused;
        if (paused && rb != null) rb.linearVelocity = Vector2.zero;
    }

    [Header("ターゲット設定")]
    private Transform player;
    [SerializeField] private float detectRange = 15.0f;
    [SerializeField] private LayerMask detectionMask = 392;
    [SerializeField, Min(0.02f)] private float sightCheckInterval = 0.15f;
    [SerializeField, Min(0.02f)] private float movementDecisionInterval = 0.15f;
    [SerializeField] private Vector2 rayOffset = new Vector2(0, -0.3f);
    [SerializeField] private Vector2 playerPositionOffset = new Vector2(0, -0.3f);

    [Header("移動設定")]
    [SerializeField] private float walkSpeed = 2.0f;
    [SerializeField] private float runSpeed = 3.0f;
    [SerializeField, Range(0f, 100f), Tooltip("追跡開始時にこのHP割合以下なら逃げます。")]
    private float fleeHealthPercent = 15f;
    [SerializeField, Min(0f), Tooltip("プレイヤー方向に近い進路を避ける強さ。")]
    private float fleePlayerDirectionPenalty = 2f;
    [SerializeField] private float acceleration = 3.0f;
    [SerializeField] private float checkRadius = 0.3f; 
    [SerializeField] private float turnAroundWallDistance = 5f;

    [Header("スタック(壁引っ掛かり)対策")]
    [SerializeField] private float stuckThreshold = 1.0f; // 何秒止まったらスタックとみなすか
    [SerializeField] private float minMoveDistance = 0.05f; // 最低限動くべき距離
    private Vector2 lastPosition;
    private float stuckTimer;

    [Header("スキャン設定")]
    [SerializeField] private int rayCount = 8;
    [SerializeField] private float scanInterval = 0.45f;
    [SerializeField] private float fastScanInterval = 0.3f;
    [SerializeField] private float forwardBias = 8.0f;
    [SerializeField] private float rayLaunchRadius = 0.7f; 
    [SerializeField] private float chestBonus = 30.0f; 

    [Header("分離設定 (重なり防止)")]
    [SerializeField] private float separationRadius = 3.0f; 
    [SerializeField] private float separationWeight = 0.2f; 
    [SerializeField, Min(0.02f)] private float separationCheckInterval = 0.2f;
    private readonly List<Collider2D> nearbyEnemyColliders = new List<Collider2D>(64);
    private float nextSeparationCheckTime;
    private Vector2 cachedSeparation;

    [Header("タイムアウト・捜索設定")]
    [SerializeField] private float searchTimeout = 1.0f; 
    [SerializeField] private float alertDuration = 7.0f;
    
    private float currentSearchTimer;
    
    [Header("デバッグ表示設定")]
    [SerializeField] private bool showDebug = false;

    private float scanTimer;
    private Vector2 lastDirection = Vector2.right;
    private Vector3 lastPlayerPos; 
    private float alertTimer;
    private Rigidbody2D rb;
    private Vector2 moveInput;
    private float nextSightCheckTime;
    private float nextMovementDecisionTime;
    [SerializeField, Min(0.1f)] private float playerSearchInterval = 1f;
    private bool canSeePlayer;
    private EnemyHealth enemyHealth;
    [SerializeField, Min(0.1f), Tooltip("逃走判断に使うHP確認の間隔(秒)。")]
    private float healthCheckInterval = 1f;
    private float nextHealthCheckTime;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        enemyHealth = GetComponent<EnemyHealth>();
        if (enemyHealth == null) enemyHealth = GetComponentInParent<EnemyHealth>();
        lastPosition = transform.position;
        moveStats =
        GetComponentInChildren<MoveStats>(true);
        waterEffect = GetComponentInParent<WaterTerrainEffect>();
        extensions.Clear();
        extensions.AddRange(GetComponents<EnemyAIExtension>());
        // Stable priority sort: equal-priority extensions retain their component order.
        for (int i = 1; i < extensions.Count; i++)
        {
            EnemyAIExtension current = extensions[i];
            int insertAt = i;
            while (insertAt > 0 && extensions[insertAt - 1].Priority < current.Priority)
            {
                extensions[insertAt] = extensions[insertAt - 1];
                insertAt--;
            }
            extensions[insertAt] = current;
        }
        foreach (EnemyAIExtension extension in extensions)
            if (extension != null) extension.Initialize(this);
        FindPlayer();
        nextSightCheckTime = Time.time + Random.Range(0f, Mathf.Max(0.02f, sightCheckInterval));
        scanTimer = Random.Range(0f, Mathf.Max(0.02f, scanInterval));
        nextSeparationCheckTime = Time.time + Random.Range(0f, Mathf.Max(0.02f, separationCheckInterval));
        Physics2D.queriesStartInColliders = true;
    }

    void FindPlayer()
    {
        if (sharedPlayer == null && Time.time >= nextSharedPlayerSearchTime)
        {
            nextSharedPlayerSearchTime = Time.time + Mathf.Max(0.1f, playerSearchInterval);

            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            foreach (GameObject obj in players)
            {
                if (obj != null && obj.GetComponent<RandomTeleporter>() != null)
                {
                    sharedPlayer = obj.transform;
                    sharedPlayerWaterEffect = obj.GetComponentInParent<WaterTerrainEffect>();
                    break;
                }
            }
        }

        player = sharedPlayer;
        playerWaterEffect = sharedPlayerWaterEffect;
    }

    void Update()
    {
        extensionBaseSpeed = -1f;
        if (Time.time >= nextSightCheckTime)
        {
            nextSightCheckTime = Time.time + Mathf.Max(0.02f, sightCheckInterval);
            bool playerInWaterAndAvoided = waterEffect != null && waterEffect.AvoidsWater
                && playerWaterEffect != null && playerWaterEffect.IsInWaterNow;
            canSeePlayer = (player != null && player.gameObject.activeInHierarchy)
                && !playerInWaterAndAvoided && CheckLineOfSight();
            if (canSeePlayer)
                lastPlayerPos = player.position + (Vector3)playerPositionOffset;
        }

        if (activeCustomExtension != null)
        {
            if (!activeCustomExtension.isActiveAndEnabled)
                ExitCustomState();
            else
            {
                InvokeCustomStateUpdate(activeCustomExtension, activeCustomStateId);
                return;
            }
        }
        if (TryEnterCustomState()) return;

        switch (currentState)
        {
            case EnemyAIState.Wander: UpdateWander(canSeePlayer); break;
            case EnemyAIState.Chase:  UpdateChase(canSeePlayer);  break;
            case EnemyAIState.Investigate: UpdateInvestigate(canSeePlayer); break;
            case EnemyAIState.Search: UpdateSearch(canSeePlayer); break;
            case EnemyAIState.Flee:   UpdateFlee(); break;
        }
    }

    private Vector2 CenterPos => (Vector2)transform.position + rayOffset;

    private bool TryEnterCustomState()
    {
        foreach (EnemyAIExtension extension in extensions)
        {
            if (extension == null || !extension.isActiveAndEnabled) continue;
            commandOwner = extension;
            bool wantsState = extension.TryEnterState(extension.Context, out string stateId);
            commandOwner = null;
            if (!wantsState || string.IsNullOrWhiteSpace(stateId)) continue;

            activeCustomExtension = extension;
            activeCustomStateId = stateId;
            InvokeCustomStateEntered(extension, stateId);
            if (activeCustomExtension == extension && activeCustomStateId == stateId)
                InvokeCustomStateUpdate(extension, stateId);
            return true;
        }
        return false;
    }

    private bool TryRunStateOverride(EnemyAIState state)
    {
        foreach (EnemyAIExtension extension in extensions)
        {
            if (extension == null || !extension.isActiveAndEnabled || !extension.OverridesState(state)) continue;
            commandOwner = extension;
            extension.OnStandardStateUpdate(extension.Context, state);
            commandOwner = null;
            return true;
        }
        return false;
    }

    private void InvokeCustomStateEntered(EnemyAIExtension extension, string stateId)
    {
        commandOwner = extension;
        extension.OnStateEntered(extension.Context, stateId);
        commandOwner = null;
    }

    private void InvokeCustomStateUpdate(EnemyAIExtension extension, string stateId)
    {
        commandOwner = extension;
        extension.OnCustomStateUpdate(extension.Context, stateId);
        commandOwner = null;
    }

    private void ExitCustomState()
    {
        EnemyAIExtension previous = activeCustomExtension;
        string previousId = activeCustomStateId;
        activeCustomExtension = null;
        activeCustomStateId = null;
        if (previous == null) return;
        commandOwner = previous;
        previous.OnStateExited(previous.Context, previousId);
        commandOwner = null;
    }

    internal void SetExtensionMovement(EnemyAIExtension owner, Vector2 direction, float? baseSpeed)
    {
        if (owner == null || owner != commandOwner) return;
        moveInput = direction.sqrMagnitude > 1f ? direction.normalized : direction;
        if (moveInput != Vector2.zero) lastDirection = moveInput;
        if (baseSpeed.HasValue) extensionBaseSpeed = baseSpeed.Value;
    }

    internal void RequestStandardState(EnemyAIExtension owner, EnemyAIState state)
    {
        if (owner == null || owner != commandOwner) return;
        ExitCustomState();
        currentState = state;
        moveInput = Vector2.zero;
        nextMovementDecisionTime = 0f;
    }

    internal void RequestCustomState(EnemyAIExtension owner, string stateId)
    {
        if (owner == null || owner != commandOwner || string.IsNullOrWhiteSpace(stateId)) return;
        if (activeCustomExtension == owner && activeCustomStateId == stateId) return;
        if (activeCustomExtension != null) ExitCustomState();
        activeCustomExtension = owner;
        activeCustomStateId = stateId;
        InvokeCustomStateEntered(owner, stateId);
    }

    internal Vector2 GetFarthestDirectionForExtension() => GetFarthestDirection();

    internal Vector2 ChooseDirectionForExtension(Vector2 desiredDirection)
    {
        if (desiredDirection.sqrMagnitude <= 0.0001f) return Vector2.zero;
        if (waterEffect != null && !waterEffect.AllowsWaterDirection(desiredDirection))
            return GetFarthestDirection();
        if (!CanTurnAround(desiredDirection)) return GetFarthestDirection();
        Vector2 origin = CenterPos + desiredDirection.normalized * rayLaunchRadius;
        RaycastHit2D hit = Physics2D.CircleCast(origin, checkRadius, desiredDirection.normalized,
            detectRange, detectionMask);
        return hit.collider != null && !hit.collider.CompareTag("Player")
            ? GetFarthestDirection()
            : desiredDirection.normalized;
    }

    private bool CanTurnAround(Vector2 direction)
    {
        if (lastDirection == Vector2.zero || Vector2.Dot(direction.normalized, lastDirection.normalized) > -0.9f)
            return true;

        RaycastHit2D wall = Physics2D.CircleCast(CenterPos, checkRadius, lastDirection.normalized,
            turnAroundWallDistance, detectionMask);
        return wall.collider != null && !wall.collider.CompareTag("Player") && wall.distance <= turnAroundWallDistance;
    }

    void UpdateWander(bool canSeePlayer)
    {
        if (player == null || !player.gameObject.activeInHierarchy) FindPlayer();

        if (canSeePlayer) { BeginChase(); return; }
        if (TryRunStateOverride(EnemyAIState.Wander)) return;
        
        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0)
        {
            RefreshDirection();
        }
    }

    // 移動方向を更新する処理
    void RefreshDirection()
    {
        moveInput = GetFarthestDirection();
        if (moveInput != Vector2.zero) lastDirection = moveInput;
        scanTimer = (currentState == EnemyAIState.Search) ? fastScanInterval : scanInterval;
    }

    void UpdateChase(bool canSeePlayer)
    {
        if (IsHealthCheckDue() && IsBelowFleeHealthThreshold())
        {
            StartFlee();
            return;
        }
        if (!canSeePlayer) { 
            currentState = EnemyAIState.Investigate; 
            currentSearchTimer = 0;
            return;
        }
        if (TryRunStateOverride(EnemyAIState.Chase)) return;
        if (Time.time < nextMovementDecisionTime) return;
        nextMovementDecisionTime = Time.time + Mathf.Max(0.02f, movementDecisionInterval);
        moveInput = ((Vector2)player.position + playerPositionOffset - CenterPos).normalized;
        if (!CanTurnAround(moveInput)) moveInput = lastDirection;
        if (waterEffect != null && !waterEffect.AllowsWaterDirection(moveInput))
            moveInput = GetFarthestDirection();
        lastDirection = moveInput;
    }

    private void UpdateFlee()
    {
        if (!canSeePlayer)
        {
            BeginSearch();
            return;
        }
        if (IsHealthCheckDue() && !IsBelowFleeHealthThreshold())
        {
            BeginSearch();
            return;
        }
        if (TryRunStateOverride(EnemyAIState.Flee)) return;
        if (Time.time >= nextMovementDecisionTime)
        {
            nextMovementDecisionTime = Time.time + Mathf.Max(0.02f, movementDecisionInterval);
            moveInput = GetFleeDirection();
            if (moveInput != Vector2.zero) lastDirection = moveInput;
        }
    }

    private void BeginChase()
    {
        nextHealthCheckTime = Time.time + Mathf.Max(0.1f, healthCheckInterval);
        if (IsBelowFleeHealthThreshold()) StartFlee();
        else currentState = EnemyAIState.Chase;
        nextMovementDecisionTime = 0f;
    }

    private bool IsHealthCheckDue()
    {
        if (Time.time < nextHealthCheckTime) return false;
        nextHealthCheckTime = Time.time + Mathf.Max(0.1f, healthCheckInterval);
        return true;
    }

    private bool IsBelowFleeHealthThreshold()
    {
        if (enemyHealth == null) return false;
        return enemyHealth.HealthRatio * 100f <= fleeHealthPercent;
    }

    private void StartFlee()
    {
        currentState = EnemyAIState.Flee;
        nextMovementDecisionTime = 0f;
    }

    private Vector2 GetFleeDirection()
    {
        Vector2 playerPos = player != null && player.gameObject.activeInHierarchy
            ? (Vector2)player.position + playerPositionOffset
            : (Vector2)lastPlayerPos;
        Vector2 toPlayer = (playerPos - CenterPos).normalized;
        Vector2 bestDir = Vector2.zero;
        float highestScore = float.NegativeInfinity;

        for (int i = 0; i < rayCount; i++)
        {
            float angle = i * (360f / rayCount);
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            if (waterEffect != null && !waterEffect.AllowsWaterDirection(dir)) continue;
            if (!CanTurnAround(dir)) continue;

            Vector2 startPos = CenterPos + dir * rayLaunchRadius;
            RaycastHit2D hit = Physics2D.CircleCast(startPos, checkRadius, dir, detectRange, detectionMask);
            float distance = hit.collider != null ? hit.distance : detectRange;
            if (showDebug)
                Debug.DrawRay(startPos, dir * Mathf.Max(distance, 0.2f), Color.cyan, 0.1f);

            // 壁までの距離を優先し、プレイヤー側を向くほど評価点を下げる。
            float score = distance - Mathf.Max(0f, Vector2.Dot(dir, toPlayer)) * fleePlayerDirectionPenalty * detectRange;
            if (score > highestScore)
            {
                highestScore = score;
                bestDir = dir;
            }
        }
        return bestDir;
    }

    void UpdateInvestigate(bool canSeePlayer)
    {
        if (canSeePlayer) { BeginChase(); return; }
        currentSearchTimer += Time.deltaTime;
        if (currentSearchTimer >= searchTimeout) { BeginSearch(); return; }
        if (TryRunStateOverride(EnemyAIState.Investigate)) return;
        if (Time.time < nextMovementDecisionTime) return;
        nextMovementDecisionTime = Time.time + Mathf.Max(0.02f, movementDecisionInterval);
        Vector2 dirToLastPos = ((Vector2)lastPlayerPos - CenterPos).normalized;
        moveInput = dirToLastPos;
        if (!CanTurnAround(moveInput)) moveInput = lastDirection;
        if (waterEffect != null && !waterEffect.AllowsWaterDirection(moveInput))
            moveInput = GetFarthestDirection();
        if (Vector2.Distance(CenterPos, lastPlayerPos) < 0.2f) BeginSearch();
    }

    void BeginSearch()
    {
        lastDirection = moveInput; 
        alertTimer = alertDuration;
        currentState = EnemyAIState.Search; 
        scanTimer = 0; 
    }

    void UpdateSearch(bool canSeePlayer)
    {
        bool healthBelowThreshold = IsHealthCheckDue() && IsBelowFleeHealthThreshold();
        if (healthBelowThreshold && canSeePlayer)
        {
            StartFlee();
            return;
        }
        if (canSeePlayer) { BeginChase(); return; }
        alertTimer -= Time.deltaTime;
        if (alertTimer <= 0) { currentState = EnemyAIState.Wander; return; }
        if (TryRunStateOverride(EnemyAIState.Search)) return;
        
        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0)
        {
            RefreshDirection();
        }
    }

    Vector2 CalculateSeparation()
    {
        if (Time.time < nextSeparationCheckTime)
            return cachedSeparation;
        nextSeparationCheckTime = Time.time + Mathf.Max(0.02f, separationCheckInterval);

        Vector2 separationVec = Vector2.zero;
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = Physics2D.queriesHitTriggers;
        int nearbyCount = Physics2D.OverlapCircle(
            transform.position, separationRadius, filter, nearbyEnemyColliders);
        int count = 0;
        for (int i = 0; i < nearbyCount; i++)
        {
            Collider2D col = nearbyEnemyColliders[i];
            if (col == null) continue;
            if (col.gameObject != this.gameObject && col.CompareTag("Enemy"))
            {
                Vector2 diff = (Vector2)transform.position - (Vector2)col.transform.position;
                float distSq = diff.sqrMagnitude;
                if (distSq < 0.01f) distSq = 0.01f; 
                separationVec += diff.normalized / distSq;
                count++;
            }
        }
        cachedSeparation = count > 0 ? separationVec : Vector2.zero;
        return cachedSeparation;
    }

    Vector2 GetFarthestDirection()
    {
        Vector2 bestDir = Vector2.zero;
        float highestScore = -1000f;

        for (int i = 0; i < rayCount; i++)
        {
            float angle = i * (360f / rayCount);
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            if (waterEffect != null && !waterEffect.AllowsWaterDirection(dir)) continue;
            if (!CanTurnAround(dir)) continue;
            Vector2 startPos = CenterPos + (dir * rayLaunchRadius);
            
            RaycastHit2D hit = Physics2D.CircleCast(startPos, checkRadius, dir, detectRange, detectionMask);
            float distance = (hit.collider != null) ? hit.distance : detectRange;

            float bonus = 0f;
            if (hit.collider != null && hit.collider.CompareTag("Chest"))
            {
                bonus = chestBonus + (detectRange - distance);
            }

            if (showDebug) 
            {
                Color rayColor = (bonus > 0) ? Color.blue : 
                                (distance <= 0) ? Color.magenta : 
                                Color.Lerp(Color.red, Color.green, distance/detectRange);
                Debug.DrawRay(startPos, dir * (distance > 0 ? distance : 0.2f), rayColor, 0.1f);
            }

            float dot = Vector2.Dot(dir, lastDirection); 
            float weight = Mathf.Lerp(0.2f, forwardBias, (dot + 1f) / 2f); 
            float score = (distance * weight) + bonus; 

            if (score > highestScore) { highestScore = score; bestDir = dir; }
        }
        return bestDir;
    }

    bool CheckLineOfSight()
    {
        if (player == null) return false;

        Vector2 targetPos = (Vector2)player.position + playerPositionOffset;
        Vector2 dir = (targetPos - CenterPos).normalized;
        float dist = Vector2.Distance(CenterPos, targetPos);
        if (dist > detectRange) return false;
        
        Vector2 startPos = CenterPos + (dir * rayLaunchRadius);
        RaycastHit2D hit = Physics2D.CircleCast(startPos, checkRadius, dir, dist, detectionMask);
        
        if (showDebug) Debug.DrawLine(startPos, startPos + dir * dist, hit.collider != null && hit.collider.CompareTag("Player") ? Color.blue : Color.black);
        return hit.collider != null && hit.collider.CompareTag("Player");
    }

    void FixedUpdate()
    {
        if (movementPausedByCompanion)
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            return;
        }
        CheckStuck();

        Vector2 separation = CalculateSeparation() * separationWeight;
        Vector2 finalForce = moveInput;
        if (separation.magnitude > 0.1f) finalForce = Vector2.Lerp(moveInput, (moveInput + separation).normalized, 0.7f);
        if (waterEffect != null && finalForce != Vector2.zero &&
            (finalForce - moveInput).sqrMagnitude > 0.01f &&
            !waterEffect.AllowsWaterDirection(finalForce)) finalForce = Vector2.zero;
        
        float baseSpeed = extensionBaseSpeed >= 0f
    ? extensionBaseSpeed
    : (currentState == EnemyAIState.Wander)
    ? walkSpeed
    : runSpeed;

float speed =
    baseSpeed;

if (moveStats != null)
{
    speed =
        moveStats.ApplySpeed(
            baseSpeed
        );
}
        
        if (finalForce != Vector2.zero)
            rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, finalForce.normalized * speed, acceleration * Time.fixedDeltaTime);
        else
            rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, Vector2.zero, acceleration * Time.fixedDeltaTime);
    }

    // スタック検知ロジック
    void CheckStuck()
    {
        if (moveInput != Vector2.zero)
        {
            float dist = Vector2.Distance(transform.position, lastPosition);
            if (dist < minMoveDistance * Time.fixedDeltaTime)
            {
                stuckTimer += Time.fixedDeltaTime;
                if (stuckTimer >= stuckThreshold)
                {
                    // ランダムな方向を「前回の方向」としてセットし、再スキャンさせる
                    float randomAngle = Random.Range(0f, 360f);
                    lastDirection = new Vector2(Mathf.Cos(randomAngle), Mathf.Sin(randomAngle));
                    RefreshDirection();
                    stuckTimer = 0;
                }
            }
            else
            {
                stuckTimer = 0;
            }
        }
        else
        {
            stuckTimer = 0;
        }
        lastPosition = transform.position;
    }

    private void OnDrawGizmos()
    {
        if (!showDebug) return;
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(CenterPos, rayLaunchRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(CenterPos, checkRadius);
        if (currentState == EnemyAIState.Investigate || currentState == EnemyAIState.Search)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(lastPlayerPos, 0.3f);
        }
    }
}
