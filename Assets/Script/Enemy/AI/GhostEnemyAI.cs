using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Ghost behavior: stalk the player, reveal an attack tell, strike, then recover.
/// Attach to the enemy root alongside EnemyHealth and one or more EnemyAttack components.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class GhostEnemyAI : MonoBehaviour
{
    private enum State
    {
        Wander,
        Stalk,
        Materialize,
        Strike,
        Recover,
        Search
    }

    [Header("Detection")]
    [SerializeField, Min(0.1f)] private float detectRange = 15f;
    [SerializeField] private LayerMask obstacleMask = 392;
    [SerializeField] private Vector2 eyeOffset = new Vector2(0f, -0.3f);
    [SerializeField] private Vector2 playerOffset = new Vector2(0f, -0.3f);
    [SerializeField, Min(0.02f)] private float sightCheckInterval = 0.15f;
    [SerializeField, Min(0.1f)] private float playerSearchInterval = 1f;
    [SerializeField, Min(0.1f)] private float searchDuration = 2f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float wanderSpeed = 2f;
    [SerializeField, Min(0f)] private float stalkSpeed = 3f;
    [SerializeField, Min(0f)] private float acceleration = 3f;
    [SerializeField, Min(0.05f)] private float obstacleCheckRadius = 0.3f;
    [SerializeField, Range(4, 32)] private int steeringRayCount = 12;
    [SerializeField, Min(0.1f)] private float steeringDistance = 1.2f;
    [SerializeField, Min(0.1f)] private float attackRange = 1.2f;
    [SerializeField, Min(0f)] private float retreatDistance = 1.8f;
    [SerializeField, Min(0.1f)] private float retreatDuration = 0.35f;

    [Header("Attack timing")]
    [SerializeField, Min(0.05f)] private float materializeDuration = 0.75f;
    [SerializeField, Min(0.05f)] private float strikeDuration = 0.28f;
    [SerializeField, Min(0f)] private float recoveryDuration = 0.85f;
    [SerializeField, Min(0f)] private float attackCooldown = 0.7f;

    [Header("Appearance")]
    [Tooltip("幽霊の見た目としてフェードさせるSpriteRendererを明示的に設定します。HBや親オブジェクトは自動取得しません。")]
    [SerializeField] private SpriteRenderer[] ghostVisuals;
    [Tooltip("この距離より遠いと、設定した透明度まで消えます。")]
    [SerializeField, Min(0f)] private float fullyTransparentDistance = 12f;
    [Tooltip("この距離より近いと完全に見えます。ここから遠ざかるほど消えます。")]
    [SerializeField, Min(0f)] private float fadeStartDistance = 4.5f;
    [Tooltip("完全に消える距離でのAlpha。0なら完全透明です。")]
    [SerializeField, Range(0f, 1f)] private float alphaAtTransparentDistance = 0f;
    [SerializeField, Min(0.01f)] private float alphaSmoothTime = 0.35f;
    [SerializeField, Range(0f, 1f)] private float damageImmunityAlphaThreshold = 0.15f;
    [Tooltip("無敵になる透明度より少し薄い位置を目標にします。")]
    [SerializeField, Range(0f, 0.25f)] private float immunitySafetyMargin = 0.05f;
    [SerializeField, Min(0f)] private float safeDistanceTolerance = 0.15f;

    [Header("マップ境界")]
    [SerializeField, Min(0f)] private float mapEdgePadding = 0.5f;

    [Header("近距離デバフ")]
    [SerializeField, Min(0f)] private float slowDebuffRange = 2.5f;
    [SerializeField, Range(0.05f, 1f)] private float slowSpeedMultiplier = 0.7f;
    [SerializeField, Min(0.1f)] private float slowDebuffDuration = 1.5f;
    [SerializeField] private string slowDebuffName = "幽霊の冷気";
    [SerializeField] private Sprite slowDebuffIcon;

    private Rigidbody2D body;
    private BSPDungeonMaster dungeonMaster;
    private Bounds movementBounds;
    private bool hasMovementBounds;
    private Transform player;
    private EnemyAttack[] attacks;
    private float[] alphaVelocities;
    private State state = State.Wander;
    private Vector2 moveDirection;
    private Vector2 lastKnownPlayerPosition;
    private Vector2 retreatDirection;
    private Vector2 retreatStartPosition;
    private float stateTimer;
    private float wanderDirectionTimer;
    private float nextAttackTime;
    private float nextSightCheckTime;
    private float nextPlayerSearchTime;
    private bool canSeePlayer;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        dungeonMaster = FindAnyObjectByType<BSPDungeonMaster>();
        attacks = GetComponentsInChildren<EnemyAttack>(true);
        SetAttacksEnabled(false);
        alphaVelocities = ghostVisuals != null ? new float[ghostVisuals.Length] : System.Array.Empty<float>();
        moveDirection = Random.insideUnitCircle.normalized;
        if (moveDirection == Vector2.zero) moveDirection = Vector2.right;
        wanderDirectionTimer = Random.Range(1.5f, 3f);
    }

    private void OnEnable()
    {
        BSPDungeonMaster.OnGenerationComplete += RefreshMovementBounds;
    }

    private void Start()
    {
        RefreshMovementBounds();
    }

    private void OnDisable()
    {
        BSPDungeonMaster.OnGenerationComplete -= RefreshMovementBounds;
        if (body != null) body.linearVelocity = Vector2.zero;
        SetAttacksEnabled(false);
    }

    private void Update()
    {
        FindPlayerIfNeeded();
        UpdateProximityEffects();
        if (Time.time >= nextSightCheckTime)
        {
            nextSightCheckTime = Time.time + sightCheckInterval;
            canSeePlayer = CanSeePlayer();
            if (canSeePlayer) lastKnownPlayerPosition = (Vector2)player.position + playerOffset;
        }

        switch (state)
        {
            case State.Wander:
                if (canSeePlayer) ChangeState(State.Stalk);
                else UpdateWander();
                break;
            case State.Stalk:
                if (canSeePlayer)
                {
                    lastKnownPlayerPosition = (Vector2)player.position + playerOffset;
                    if (Vector2.Distance(body.position, lastKnownPlayerPosition) <= attackRange && Time.time >= nextAttackTime)
                        ChangeState(State.Materialize);
                }
                else if (!NeedsSafetyRepositioning()) ChangeState(State.Search);
                break;
            case State.Materialize:
                stateTimer -= Time.deltaTime;
                if (!canSeePlayer) ChangeState(State.Search);
                else if (stateTimer <= 0f) ChangeState(State.Strike);
                break;
            case State.Strike:
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f) ChangeState(State.Recover);
                break;
            case State.Recover:
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    nextAttackTime = Time.time + attackCooldown;
                    ChangeState(State.Stalk);
                }
                break;
            case State.Search:
                stateTimer -= Time.deltaTime;
                if (canSeePlayer) ChangeState(State.Stalk);
                else if (stateTimer <= 0f) ChangeState(State.Wander);
                break;
        }
    }

    private void FixedUpdate()
    {
        Vector2 desiredVelocity = Vector2.zero;
        switch (state)
        {
            case State.Wander:
                desiredVelocity = Steer(moveDirection) * wanderSpeed;
                break;
            case State.Stalk:
                if (player != null && player.gameObject.activeInHierarchy)
                {
                    Vector2 toPlayerVector = (Vector2)player.position - body.position;
                    float distanceToPlayer = toPlayerVector.magnitude;
                    float preferredDistance = GetDamageSafeDistance();
                    // Safe-distance repositioning uses a direct vector. Physical collision settings
                    // decide whether walls block it; AI obstacle casts are intentionally bypassed.
                    if (distanceToPlayer > preferredDistance + safeDistanceTolerance)
                    {
                        moveDirection = toPlayerVector.normalized;
                        desiredVelocity = moveDirection * stalkSpeed;
                    }
                    else if (distanceToPlayer < preferredDistance - safeDistanceTolerance)
                    {
                        moveDirection = -toPlayerVector.normalized;
                        desiredVelocity = moveDirection * stalkSpeed;
                    }
                }
                else if (Vector2.Distance(body.position, lastKnownPlayerPosition) > 0.3f)
                    desiredVelocity = Steer(lastKnownPlayerPosition - body.position) * stalkSpeed;
                break;
            case State.Strike:
                desiredVelocity = moveDirection * stalkSpeed * 1.5f;
                break;
            case State.Recover:
                if (stateTimer > recoveryDuration - retreatDuration
                    && Vector2.Distance(body.position, retreatStartPosition) < retreatDistance)
                    desiredVelocity = retreatDirection * stalkSpeed;
                break;
            case State.Search:
                if (Vector2.Distance(body.position, lastKnownPlayerPosition) > 0.3f)
                    desiredVelocity = Steer(lastKnownPlayerPosition - body.position) * wanderSpeed;
                break;
        }

        Vector2 velocity = Vector2.MoveTowards(body.linearVelocity, desiredVelocity, acceleration * Time.fixedDeltaTime);
        if (hasMovementBounds)
        {
            Vector2 currentPosition = body.position;
            Vector2 boundedCurrent = ClampToMovementBounds(currentPosition);
            if ((boundedCurrent - currentPosition).sqrMagnitude > 0.0001f)
            {
                body.position = boundedCurrent;
                currentPosition = boundedCurrent;
            }

            float fixedDelta = Time.fixedDeltaTime;
            if (fixedDelta > 0f)
            {
                Vector2 nextPosition = ClampToMovementBounds(currentPosition + velocity * fixedDelta);
                velocity = (nextPosition - currentPosition) / fixedDelta;
            }
        }
        body.linearVelocity = velocity;
    }

    private void RefreshMovementBounds()
    {
        if (dungeonMaster == null)
            dungeonMaster = FindAnyObjectByType<BSPDungeonMaster>();
        hasMovementBounds = false;
        if (dungeonMaster == null) return;

        if (dungeonMaster.IsUsingManualMap && TryGetManualMapBounds(out Bounds manualBounds))
        {
            movementBounds = manualBounds;
            hasMovementBounds = true;
            return;
        }

        dungeonMaster.GetEnemySpawnRange(out Vector2 min, out Vector2 max);
        movementBounds = new Bounds((min + max) * 0.5f, max - min);
        hasMovementBounds = true;
    }

    private bool TryGetManualMapBounds(out Bounds bounds)
    {
        bounds = default;
        Tilemap map = dungeonMaster.floorTilemap;
        if (map == null || map.GetUsedTilesCount() == 0)
            map = dungeonMaster.wallTilemap != null && dungeonMaster.wallTilemap.GetUsedTilesCount() > 0
                ? dungeonMaster.wallTilemap
                : dungeonMaster.tilemap;
        if (map == null || map.GetUsedTilesCount() == 0) return false;

        BoundsInt cells = map.cellBounds;
        Vector3[] corners =
        {
            map.CellToWorld(new Vector3Int(cells.xMin, cells.yMin, 0)),
            map.CellToWorld(new Vector3Int(cells.xMax, cells.yMin, 0)),
            map.CellToWorld(new Vector3Int(cells.xMin, cells.yMax, 0)),
            map.CellToWorld(new Vector3Int(cells.xMax, cells.yMax, 0))
        };
        Vector3 min = corners[0];
        Vector3 max = corners[0];
        for (int i = 1; i < corners.Length; i++)
        {
            min = Vector3.Min(min, corners[i]);
            max = Vector3.Max(max, corners[i]);
        }
        bounds = new Bounds((min + max) * 0.5f, max - min);
        return true;
    }

    private Vector2 ClampToMovementBounds(Vector2 position)
    {
        Vector3 min3 = movementBounds.min;
        Vector3 max3 = movementBounds.max;
        Vector2 min = new Vector2(min3.x + mapEdgePadding, min3.y + mapEdgePadding);
        Vector2 max = new Vector2(max3.x - mapEdgePadding, max3.y - mapEdgePadding);
        if (min.x > max.x) min.x = max.x = movementBounds.center.x;
        if (min.y > max.y) min.y = max.y = movementBounds.center.y;
        return new Vector2(Mathf.Clamp(position.x, min.x, max.x), Mathf.Clamp(position.y, min.y, max.y));
    }

    private void FindPlayerIfNeeded()
    {
        if (player != null && player.gameObject.activeInHierarchy) return;
        if (Time.time < nextPlayerSearchTime) return;
        nextPlayerSearchTime = Time.time + playerSearchInterval;
        GameObject[] candidates = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject candidate in candidates)
        {
            if (candidate.GetComponent<RandomTeleporter>() == null) continue;
            Transform hb = FindChildByName(candidate.transform, "HB");
            player = hb != null ? hb : candidate.transform;
            break;
        }
    }

    private bool CanSeePlayer()
    {
        if (player == null || !player.gameObject.activeInHierarchy) return false;
        Vector2 origin = body.position + eyeOffset;
        Vector2 target = (Vector2)player.position + playerOffset;
        Vector2 delta = target - origin;
        // Ghost detection is range-based and intentionally ignores walls.
        return delta.sqrMagnitude <= detectRange * detectRange;
    }

    private void UpdateWander()
    {
        wanderDirectionTimer -= Time.deltaTime;
        if (wanderDirectionTimer <= 0f)
        {
            moveDirection = Random.insideUnitCircle.normalized;
            if (moveDirection == Vector2.zero) moveDirection = Vector2.right;
            wanderDirectionTimer = Random.Range(1.5f, 3f);
        }
    }

    private Vector2 Steer(Vector2 preferredDirection)
    {
        if (preferredDirection.sqrMagnitude < 0.001f) preferredDirection = moveDirection;
        preferredDirection.Normalize();
        Vector2 bestDirection = Vector2.zero;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < steeringRayCount; i++)
        {
            float angle = i * (360f / steeringRayCount);
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            RaycastHit2D hit = Physics2D.CircleCast(body.position, obstacleCheckRadius, direction,
                steeringDistance, obstacleMask);
            float openDistance = hit.collider != null ? hit.distance : steeringDistance;
            float alignment = Vector2.Dot(direction, preferredDirection);
            float score = openDistance + alignment * steeringDistance * 1.5f;
            if (score > bestScore)
            {
                bestScore = score;
                bestDirection = direction;
            }
        }
        return bestDirection == Vector2.zero ? preferredDirection : bestDirection;
    }

    private void ChangeState(State next)
    {
        state = next;
        switch (next)
        {
            case State.Materialize:
                stateTimer = materializeDuration;
                moveDirection = player != null ? ((Vector2)player.position + playerOffset - body.position).normalized : moveDirection;
                SetAttacksEnabled(false);
                break;
            case State.Strike:
                stateTimer = strikeDuration;
                SetAttacksEnabled(true);
                break;
            case State.Recover:
                stateTimer = recoveryDuration;
                retreatDirection = player != null
                    ? (body.position - (Vector2)player.position).normalized
                    : -moveDirection;
                if (retreatDirection == Vector2.zero) retreatDirection = -moveDirection;
                retreatStartPosition = body.position;
                SetAttacksEnabled(false);
                break;
            case State.Search:
                stateTimer = searchDuration;
                SetAttacksEnabled(false);
                break;
            case State.Wander:
                SetAttacksEnabled(false);
                moveDirection = Random.insideUnitCircle.normalized;
                if (moveDirection == Vector2.zero) moveDirection = Vector2.right;
                break;
            case State.Stalk:
                SetAttacksEnabled(false);
                break;
        }
    }

    private void SetAttacksEnabled(bool enabled)
    {
        if (attacks == null) return;
        foreach (EnemyAttack attack in attacks)
            if (attack != null) attack.enabled = enabled;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
            if (child.name == childName) return child;
        return null;
    }

    private void UpdateProximityEffects()
    {
        if (player == null || !player.gameObject.activeInHierarchy)
        {
            SetTargetAlpha(alphaAtTransparentDistance);
            return;
        }

        float distance = Vector2.Distance(body.position, player.position);
        float nearVisibleDistance = Mathf.Min(fullyTransparentDistance, fadeStartDistance);
        float farTransparentDistance = Mathf.Max(fullyTransparentDistance, fadeStartDistance);
        float targetAlpha;
        if (distance <= nearVisibleDistance)
            targetAlpha = 1f;
        else if (distance >= farTransparentDistance)
            targetAlpha = alphaAtTransparentDistance;
        else
        {
            float t = Mathf.InverseLerp(nearVisibleDistance, farTransparentDistance, distance);
            targetAlpha = Mathf.Lerp(1f, alphaAtTransparentDistance, t);
        }

        SetTargetAlpha(targetAlpha);

        BuffController buffController = player.GetComponentInParent<BuffController>();
        if (buffController == null || distance > slowDebuffRange || HasActiveSlowDebuff(buffController))
            return;

        GhostProximitySlowBuffData debuff = ScriptableObject.CreateInstance<GhostProximitySlowBuffData>();
        debuff.buffName = slowDebuffName;
        debuff.icon = slowDebuffIcon;
        debuff.duration = slowDebuffDuration;
        debuff.isDebuff = true;
        debuff.level = 1;
        debuff.speedMultiplier = slowSpeedMultiplier;
        buffController.AddBuff(debuff, true);
    }

    private bool HasActiveSlowDebuff(BuffController buffController)
    {
        if (buffController == null) return false;
        foreach (BuffInstance buff in buffController.GetBuffs())
            if (buff != null && buff.Data is GhostProximitySlowBuffData) return true;
        return false;
    }

    private bool NeedsSafetyRepositioning()
    {
        if (player == null || !player.gameObject.activeInHierarchy) return false;
        float distance = Vector2.Distance(body.position, player.position);
        float preferredDistance = GetDamageSafeDistance();
        return distance > preferredDistance + safeDistanceTolerance
            || distance < preferredDistance - safeDistanceTolerance;
    }

    private float GetDamageSafeDistance()
    {
        float nearVisibleDistance = Mathf.Min(fullyTransparentDistance, fadeStartDistance);
        float farTransparentDistance = Mathf.Max(fullyTransparentDistance, fadeStartDistance);
        float targetAlpha = Mathf.Clamp01(damageImmunityAlphaThreshold - immunitySafetyMargin);
        float alphaRange = 1f - alphaAtTransparentDistance;
        if (alphaRange <= 0.0001f || targetAlpha >= 1f)
            return farTransparentDistance;

        float t = Mathf.Clamp01((1f - targetAlpha) / alphaRange);
        return Mathf.Lerp(nearVisibleDistance, farTransparentDistance, t);
    }

    private void SetTargetAlpha(float targetAlpha)
    {
        if (ghostVisuals == null) return;
        if (alphaVelocities == null || alphaVelocities.Length != ghostVisuals.Length)
            alphaVelocities = new float[ghostVisuals.Length];

        for (int i = 0; i < ghostVisuals.Length; i++)
        {
            SpriteRenderer sprite = ghostVisuals[i];
            if (sprite == null) continue;
            Color color = sprite.color;
            color.a = Mathf.SmoothDamp(color.a, targetAlpha, ref alphaVelocities[i], alphaSmoothTime);
            sprite.color = color;
        }
    }

    /// <summary>EnemyHealth queries this before applying incoming damage.</summary>
    public bool CanReceiveDamage
    {
        get
        {
            if (ghostVisuals == null || ghostVisuals.Length == 0) return true;
            float highestAlpha = 0f;
            bool hasRenderer = false;
            foreach (SpriteRenderer sprite in ghostVisuals)
            {
                if (sprite == null) continue;
                hasRenderer = true;
                highestAlpha = Mathf.Max(highestAlpha, sprite.color.a);
            }
            return !hasRenderer || highestAlpha > damageImmunityAlphaThreshold;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, detectRange);
        Gizmos.color = new Color(1f, 0.3f, 0.4f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}


/// <summary>Runtime-only debuff type so it can coexist with ordinary speed buffs.</summary>
public sealed class GhostProximitySlowBuffData : BuffData
{
    public float speedMultiplier = 0.7f;

    public override BuffInstance CreateInstance(GameObject target)
    {
        return new GhostProximitySlowBuffInstance(target, this);
    }
}

public sealed class GhostProximitySlowBuffInstance : BuffInstance
{
    private readonly GhostProximitySlowBuffData slowData;
    private MoveStats moveStats;

    public GhostProximitySlowBuffInstance(GameObject target, GhostProximitySlowBuffData data)
        : base(target, data)
    {
        slowData = data;
    }

    public override void OnApply()
    {
        moveStats = target.GetComponentInChildren<MoveStats>();
        if (moveStats == null) moveStats = target.GetComponentInParent<MoveStats>();
        if (moveStats != null) moveStats.speedMultiplier *= slowData.speedMultiplier;
    }

    public override void OnRemove()
    {
        if (moveStats != null && slowData.speedMultiplier > 0f)
            moveStats.speedMultiplier /= slowData.speedMultiplier;
    }
}
