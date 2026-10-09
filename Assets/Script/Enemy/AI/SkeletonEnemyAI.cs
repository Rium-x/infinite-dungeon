using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class SkeletonEnemyAI : MonoBehaviour
{
    private MoveStats moveStats;
    enum State { Idle, Chase, Alert, Search }
    [SerializeField, HideInInspector] private State currentState = State.Idle;

    [Header("プレイヤー検出")]
    private Transform player;
    [SerializeField] private float detectRange = 15.0f;
    [SerializeField] private LayerMask detectionMask;
    [SerializeField, Min(0.02f)] private float sightCheckInterval = 0.1f;
    [SerializeField] private Vector2 rayOffset = new Vector2(0, -0.3f);
    [SerializeField] private Vector2 playerPositionOffset = new Vector2(0, -0.3f);

    [Header("移動設定")]
    [SerializeField] private float walkSpeed = 2.0f;
    [SerializeField] private float runSpeed = 3.0f;
    [SerializeField] private float acceleration = 3.0f;
    [SerializeField] private float checkRadius = 0.3f; 
    [SerializeField] private float turnAroundWallDistance = 5f;

    [Header("十字移動・硬直設定")]
    [SerializeField] private float turnStallDuration = 0.15f; 
    private float stallTimer = 0f;
    private Vector2 previousSnapDirection = Vector2.zero;
    private Vector2 currentAxis = Vector2.right; 

    [Header("スタック(壁引っ掛かり)対策")]
    [SerializeField] private float stuckThreshold = 1.0f; 
    [SerializeField] private float minMoveDistance = 0.05f; 
    [SerializeField] private float stuckAvoidanceTime = 0.5f; 
    private Vector2 lastPosition;
    private float stuckTimer;
    private float avoidanceTimer = 0f;

    [Header("スキャン設定")]
    [SerializeField] private int rayCount = 8; // ← 復活させました！
    [SerializeField] private float scanInterval = 0.3f; 
    [SerializeField] private float fastScanInterval = 0.2f;
    [SerializeField] private float forwardBias = 8.0f; 
    [SerializeField] private float rayLaunchRadius = 0.7f; 
    [SerializeField] private float chestBonus = 30.0f; 

    [Header("分離設定 (重なり防止)")]
    [SerializeField] private float separationRadius = 3.0f; 
    [SerializeField] private float separationWeight = 0.2f; 
    [SerializeField, Min(0.02f)] private float separationCheckInterval = 0.1f;
    private readonly List<Collider2D> nearbyEnemyColliders = new List<Collider2D>(64);
    private float nextSeparationCheckTime;
    private Vector2 cachedSeparation;

    [Header("タイムアウト・捜索設定")]
    [SerializeField] private float searchTimeout = 1.0f; 
    [SerializeField] private float alertDuration = 7.0f;
    
    private float currentSearchTimer;
    
    [Header("デバッグ表示設定")]
    [SerializeField] private bool showDebug = true;

    private float scanTimer;
    private Vector2 lastDirection = Vector2.right;
    private Vector3 lastPlayerPos; 
    private float alertTimer;
    private Rigidbody2D rb;
    private Vector2 moveInput;
    private float nextSightCheckTime;
    private bool canSeePlayer;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        lastPosition = transform.position;
        moveStats = GetComponent<MoveStats>();
        FindPlayer();
        Physics2D.queriesStartInColliders = true;
    }

    void FindPlayer()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject obj in players)
        {
            if (obj.GetComponent<RandomTeleporter>() != null)
            {
                player = obj.transform;
                break;
            }
        }
    }

    void Update()
    {
        if (Time.time >= nextSightCheckTime)
        {
            nextSightCheckTime = Time.time + Mathf.Max(0.02f, sightCheckInterval);
            canSeePlayer = (player != null && player.gameObject.activeInHierarchy) && CheckLineOfSight();
            if (canSeePlayer)
                lastPlayerPos = player.position + (Vector3)playerPositionOffset;
        }

        switch (currentState)
        {
            case State.Idle:   UpdateIdle(canSeePlayer);   break;
            case State.Chase:  UpdateChase(canSeePlayer);  break;
            case State.Search: UpdateSearch(canSeePlayer); break;
            case State.Alert:  UpdateAlert(canSeePlayer);  break;
        }
    }

    private Vector2 CenterPos => (Vector2)transform.position + rayOffset;

    private Vector2 SnapToCross(Vector2 dir)
    {
        if (dir == Vector2.zero) return Vector2.zero;

        Vector2 requestedAxis = Mathf.Abs(dir.x) > Mathf.Abs(dir.y)
            ? new Vector2(Mathf.Sign(dir.x), 0f)
            : new Vector2(0f, Mathf.Sign(dir.y));
        if (currentAxis != Vector2.zero && Vector2.Dot(requestedAxis, currentAxis) < -0.9f)
        {
            RaycastHit2D wall = Physics2D.CircleCast(CenterPos, checkRadius, currentAxis,
                turnAroundWallDistance, detectionMask);
            if (wall.collider == null || wall.collider.CompareTag("Player") || wall.distance > turnAroundWallDistance)
                return currentAxis;
        }
        
        float xWeight = Mathf.Abs(dir.x) + (currentAxis.x != 0 ? 0.3f : 0f);
        float yWeight = Mathf.Abs(dir.y) + (currentAxis.y != 0 ? 0.3f : 0f);

        float signX = dir.x < 0 ? -1f : 1f;
        float signY = dir.y < 0 ? -1f : 1f;

        if (xWeight > yWeight)
        {
            currentAxis = new Vector2(signX, 0f);
        }
        else
        {
            currentAxis = new Vector2(0f, signY);
        }
        return currentAxis;
    }

    void UpdateIdle(bool canSeePlayer)
    {
        if (player == null || !player.gameObject.activeInHierarchy) FindPlayer();

        if (canSeePlayer) { currentState = State.Chase; return; }
        
        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0)
        {
            RefreshDirection();
        }
    }

    void RefreshDirection()
    {
        moveInput = SnapToCross(GetFarthestDirection()); // 一番良い斜め方向などを探し、それを十字にスナップ
        if (moveInput != Vector2.zero) lastDirection = moveInput;
        scanTimer = (currentState == State.Alert) ? fastScanInterval : scanInterval;
    }

    void UpdateChase(bool canSeePlayer)
    {
        if (!canSeePlayer) { 
            currentState = State.Search; 
            currentSearchTimer = 0;
            return; 
        }

        if (avoidanceTimer > 0) return; 

        Vector2 dirToPlayer = ((Vector2)player.position + playerPositionOffset - CenterPos).normalized;
        moveInput = SnapToCross(dirToPlayer);
        lastDirection = moveInput;
    }

    void UpdateSearch(bool canSeePlayer)
    {
        if (canSeePlayer) { currentState = State.Chase; return; }
        currentSearchTimer += Time.deltaTime;
        if (currentSearchTimer >= searchTimeout) { ForceChangeToAlert(); return; }
        
        Vector2 dirToLastPos = ((Vector2)lastPlayerPos - CenterPos).normalized;
        moveInput = SnapToCross(dirToLastPos);
        
        if (Vector2.Distance(CenterPos, lastPlayerPos) < 0.2f) ForceChangeToAlert();
    }

    void ForceChangeToAlert()
    {
        lastDirection = moveInput; 
        alertTimer = alertDuration;
        currentState = State.Alert; 
        scanTimer = 0; 
    }

    void UpdateAlert(bool canSeePlayer)
    {
        if (canSeePlayer) { currentState = State.Chase; return; }
        alertTimer -= Time.deltaTime;
        if (alertTimer <= 0) { currentState = State.Idle; return; }
        
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
        float angleStep = 360f / rayCount; // レイの数に応じて角度を分割

        for (int i = 0; i < rayCount; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
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
        return bestDir; // ここでは一番良い「斜め」を含む方向を返す
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
        if (avoidanceTimer > 0) avoidanceTimer -= Time.fixedDeltaTime;

        CheckStuck();

        Vector2 separation = CalculateSeparation() * separationWeight;
        Vector2 finalForce = moveInput;

        if (separation.magnitude > 0.1f) 
        {
            finalForce = (moveInput + separation).normalized;
        }
        finalForce = SnapToCross(finalForce);

        if (finalForce != previousSnapDirection && finalForce != Vector2.zero)
        {
            stallTimer = turnStallDuration;
        }
        previousSnapDirection = finalForce;

        float baseSpeed = (currentState == State.Idle) ? walkSpeed : runSpeed;
        float speed = baseSpeed;

        if (moveStats != null)
        {
            speed = moveStats.ApplySpeed(baseSpeed);
        }
        
        if (stallTimer > 0)
        {
            stallTimer -= Time.fixedDeltaTime;
            rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, Vector2.zero, acceleration * Time.fixedDeltaTime);
        }
        else
        {
            if (finalForce != Vector2.zero)
                rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, finalForce.normalized * speed, acceleration * Time.fixedDeltaTime);
            else
                rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, Vector2.zero, acceleration * Time.fixedDeltaTime);
        }
    }

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
                    if (currentAxis.x != 0) 
                    {
                        float signY = (player != null && player.position.y < transform.position.y) ? -1f : 1f;
                        moveInput = new Vector2(0, signY);
                    }
                    else 
                    {
                        float signX = (player != null && player.position.x < transform.position.x) ? -1f : 1f;
                        moveInput = new Vector2(signX, 0);
                    }

                    currentAxis = moveInput;
                    lastDirection = moveInput;
                    avoidanceTimer = stuckAvoidanceTime; 
                    stuckTimer = 0;
                    
                    // スタック回避時はスキャンを上書きしないようコメントアウト、または不要なら削除
                    // RefreshDirection(); 
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
        if (currentState == State.Search || currentState == State.Alert)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(lastPlayerPos, 0.3f);
        }
    }
}
