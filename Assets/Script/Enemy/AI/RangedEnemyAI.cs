using UnityEngine;

/// <summary>EnemyAIの追跡行動を距離維持と投射物攻撃に置き換える拡張。</summary>
[RequireComponent(typeof(EnemyAI))]
public class RangedEnemyAI : EnemyAIExtension
{
    [Header("距離維持")]
    [SerializeField, Min(0f)] private float preferredDistance = 5f;
    [SerializeField, Min(0f)] private float distanceTolerance = 0.75f;
    [SerializeField, Min(0f)] private float moveSpeed = 3.5f;
    [SerializeField, Min(0.05f)] private float movementDecisionInterval = 0.2f;
    [SerializeField, Min(0f)] private float checkRadius = 0.3f;
    [SerializeField] private float rayLaunchRadius = 0.7f;
    [SerializeField, Range(4, 8)] private int rayCount = 8;
    [SerializeField, Min(0.1f)] private float avoidanceRange = 3f;
    [SerializeField, Min(0f), Tooltip("後退時にプレイヤー方向へ進む候補を減点します。")]
    private float playerDirectionPenalty = 2f;
    [SerializeField] private LayerMask obstacleMask = 8;

    [Header("射撃")]
    [SerializeField, Tooltip("発射する投射物プレハブ。ArrowProjectile を付けた矢などを指定します。")]
    private GameObject projectilePrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField, Min(0.05f)] private float fireInterval = 1.5f;
    [SerializeField] private bool randomizeFireInterval;
    [SerializeField, Min(0.05f), Tooltip("ランダム射撃間隔の最短秒数")]
    private float minimumFireInterval = 1f;
    [SerializeField, Min(0.05f), Tooltip("ランダム射撃間隔の最長秒数")]
    private float maximumFireInterval = 2f;
    [SerializeField, Min(1), Tooltip("1回の射撃動作で発射する弾数")]
    private int shotsPerBurst = 1;
    [SerializeField, Min(0f), Tooltip("連射中の弾と弾の間隔")]
    private float burstShotInterval = 0.12f;
    [SerializeField, Range(0f, 45f), Tooltip("複数弾の扇状の広がり角度")]
    private float spreadAngle = 0f;
    [SerializeField, Range(0f, 45f), Tooltip("照準に加えるランダムな左右の角度誤差")]
    private float aimInaccuracy = 0f;
    [SerializeField, Min(0f), Tooltip("プレイヤーの移動先を予測する倍率。0なら現在位置を狙います")]
    private float aimLeadMultiplier = 0f;
    [SerializeField, Min(0f), Tooltip("発射位置に加えるランダムなずれ")]
    private float randomMuzzleOffset = 0f;
    [SerializeField, Min(0f)] private float attackRange = 12f;
    [SerializeField] private bool showDebug;

    private float nextMovementDecisionTime;
    private float nextFireTime;
    private float nextBurstShotTime;
    private int remainingBurstShots;
    private Vector2 moveDirection;

    public override bool OverridesState(EnemyAIState state) => state == EnemyAIState.Chase;

    public override void OnStandardStateUpdate(EnemyAIContext context, EnemyAIState state)
    {
        if (state != EnemyAIState.Chase || context.Player == null)
        {
            moveDirection = Vector2.zero;
            context.SetMovement(moveDirection, moveSpeed);
            return;
        }

        if (Time.time >= nextMovementDecisionTime)
        {
            nextMovementDecisionTime = Time.time + Mathf.Max(0.05f, movementDecisionInterval);
            Vector2 toPlayer = context.DirectionToPlayer;
            float distance = context.DistanceToPlayer;
            float minDistance = Mathf.Max(0f, preferredDistance - distanceTolerance);
            float maxDistance = preferredDistance + distanceTolerance;

            if (distance < minDistance)
                moveDirection = ChooseDirection(context, -toPlayer, toPlayer, true);
            else if (distance > maxDistance)
                moveDirection = ChooseDirection(context, toPlayer, toPlayer, false);
            else
                moveDirection = Vector2.zero;
        }
        context.SetMovement(moveDirection, moveSpeed);

        if (context.DistanceToPlayer <= attackRange)
            UpdateShooting(context);
        else
            remainingBurstShots = 0;
    }

    private Vector2 ChooseDirection(EnemyAIContext context, Vector2 desired, Vector2 toPlayer, bool retreating)
    {
        if (desired.sqrMagnitude <= 0.0001f) return Vector2.zero;
        desired.Normalize();
        Vector2 origin = context.CenterPosition;
        RaycastHit2D directHit = Physics2D.CircleCast(origin, checkRadius, desired,
            avoidanceRange, obstacleMask);
        if (directHit.collider == null || directHit.distance >= avoidanceRange * 0.85f)
            return desired;

        Vector2 bestDirection = desired;
        float bestScore = float.NegativeInfinity;
        int scanCount = Mathf.Clamp(rayCount, 4, 8);
        for (int i = 0; i < scanCount; i++)
        {
            float angle = i * (360f / scanCount) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 rayOrigin = origin + direction * rayLaunchRadius;
            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, direction, avoidanceRange, obstacleMask);
            float clearDistance = hit.collider != null ? hit.distance : avoidanceRange;
            float score = clearDistance + Vector2.Dot(direction, desired) * avoidanceRange;
            if (retreating)
                score -= Mathf.Max(0f, Vector2.Dot(direction, toPlayer))
                    * playerDirectionPenalty * avoidanceRange;

            if (showDebug)
                Debug.DrawRay(rayOrigin, direction * Mathf.Max(0.2f, clearDistance), Color.yellow,
                    movementDecisionInterval);
            if (score > bestScore)
            {
                bestScore = score;
                bestDirection = direction;
            }
        }
        return bestDirection;
    }

    private void UpdateShooting(EnemyAIContext context)
    {
        if (projectilePrefab == null || context.Player == null) return;
        if (remainingBurstShots <= 0)
        {
            if (Time.time < nextFireTime) return;
            remainingBurstShots = Mathf.Max(1, shotsPerBurst);
            nextBurstShotTime = Time.time;
            float interval = randomizeFireInterval
                ? Random.Range(Mathf.Min(minimumFireInterval, maximumFireInterval),
                    Mathf.Max(minimumFireInterval, maximumFireInterval))
                : fireInterval;
            nextFireTime = Time.time + Mathf.Max(0.05f, interval);
        }
        if (Time.time < nextBurstShotTime) return;
        FireProjectile(context.Player, context.LastKnownPlayerPosition);
        remainingBurstShots--;
        nextBurstShotTime = Time.time + burstShotInterval;
    }

    private void FireProjectile(Transform player, Vector2 targetPosition)
    {
        Vector2 origin = firePoint != null ? firePoint.position : transform.position;
        origin += Random.insideUnitCircle * randomMuzzleOffset;
        Vector2 aimPosition = targetPosition;
        if (aimLeadMultiplier > 0f)
        {
            Rigidbody2D playerBody = player.GetComponent<Rigidbody2D>();
            if (playerBody == null) playerBody = player.GetComponentInParent<Rigidbody2D>();
            if (playerBody != null) aimPosition += playerBody.linearVelocity * aimLeadMultiplier;
        }
        Vector2 direction = (aimPosition - origin).normalized;
        if (direction.sqrMagnitude <= 0.001f) return;

        int shotIndex = Mathf.Max(0, shotsPerBurst - remainingBurstShots);
        float centeredSpread = shotsPerBurst > 1
            ? (shotIndex - (shotsPerBurst - 1) * 0.5f) * spreadAngle
            : 0f;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg
            + centeredSpread + Random.Range(-aimInaccuracy, aimInaccuracy);
        direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));

        GameObject projectileObject = Instantiate(projectilePrefab, origin,
            Quaternion.Euler(0f, 0f, angle));
        ArrowProjectile projectile = projectileObject.GetComponent<ArrowProjectile>();
        if (projectile != null)
        {
            projectile.SetSourceFaction(GetComponent<EnemyFaction>());
            projectile.Launch(direction);
        }
    }
}
