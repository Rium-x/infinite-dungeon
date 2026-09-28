using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(EnemyAI))]
public class LineFollowerEnemy : MonoBehaviour
{
    [Header("探索")]
    [SerializeField] private float searchRadius = 6f;
    [SerializeField] private float searchInterval = 1f;

    [Header("追従")]
    [SerializeField] private float moveSpeed = 3f;

    [Tooltip("減速開始距離")]
    [SerializeField] private float stopDistance = 1.5f;

    [Tooltip("通常速度に戻る距離")]
    [SerializeField] private float resumeDistance = 2.0f;

    [Tooltip("近付きすぎたので後退する距離")]
    [SerializeField] private float retreatDistance = 1.0f;

    [Header("見失い")]
    [SerializeField] private float lostTime = 3f;

    [Header("壁判定")]
    [SerializeField] private LayerMask obstacleMask;

    private Rigidbody2D rb;
    private EnemyAI enemyAI;

    private float searchTimer;
    private float lostTimer;

    // 自分が追う相手
    private LineFollowerEnemy target;

    // 自分を追っている相手
    private LineFollowerEnemy follower;

    private bool isFollowing;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        enemyAI = GetComponent<EnemyAI>();
    }

    private void Update()
    {
        if (!isFollowing)
        {
            SearchTarget();
            return;
        }

        UpdateFollowState();
    }

    private void FixedUpdate()
    {
        if (!isFollowing)
            return;

        if (target == null)
            return;

        float distance =
            Vector2.Distance(
                transform.position,
                target.transform.position);

        Vector2 dir =
            (
                target.transform.position -
                transform.position
            ).normalized;

        // 近付きすぎたら後退
        if (distance <= retreatDistance)
        {
            rb.linearVelocity =
                -dir * moveSpeed;

            return;
        }

        float speedMultiplier = 1f;

        // 減速ゾーン
        if (distance < stopDistance)
        {
            speedMultiplier =
                Mathf.InverseLerp(
                    retreatDistance,
                    stopDistance,
                    distance);

            speedMultiplier =
                Mathf.Clamp(
                    speedMultiplier,
                    0.2f,
                    1f);
        }
        // 離れたら全速
        else if (distance > resumeDistance)
        {
            speedMultiplier = 1f;
        }

        rb.linearVelocity =
            dir *
            moveSpeed *
            speedMultiplier;
    }

    private void SearchTarget()
    {
        searchTimer -= Time.deltaTime;

        if (searchTimer > 0f)
            return;

        searchTimer = searchInterval;

        Collider2D[] hits =
            Physics2D.OverlapCircleAll(
                transform.position,
                searchRadius);

        foreach (Collider2D hit in hits)
        {
            if (hit.gameObject == gameObject)
                continue;

            LineFollowerEnemy candidate =
                hit.GetComponent<LineFollowerEnemy>();

            if (candidate == null)
                continue;

            // A→B→A 防止
            if (candidate == follower)
                continue;

            // A→B
            // C→B
            // D→B
            // 防止
            if (candidate.follower != null)
                continue;

            if (IsBlocked(candidate))
                continue;

            StartFollowing(candidate);
            return;
        }
    }

    private void StartFollowing(LineFollowerEnemy newTarget)
    {
        target = newTarget;

        target.follower = this;

        lostTimer = 0f;

        isFollowing = true;

        if (enemyAI != null)
        {
            enemyAI.enabled = false;
        }
    }

    private void StopFollowing()
    {
        if (target != null)
        {
            if (target.follower == this)
            {
                target.follower = null;
            }
        }

        target = null;

        lostTimer = 0f;

        isFollowing = false;

        rb.linearVelocity = Vector2.zero;

        if (enemyAI != null)
        {
            enemyAI.enabled = true;
        }
    }

    private void UpdateFollowState()
    {
        if (target == null)
        {
            StopFollowing();
            return;
        }

        if (!target.gameObject.activeInHierarchy)
        {
            StopFollowing();
            return;
        }

        if (IsBlocked(target))
        {
            lostTimer += Time.deltaTime;

            if (lostTimer >= lostTime)
            {
                StopFollowing();
            }

            return;
        }

        lostTimer = 0f;
    }

    private bool IsBlocked(LineFollowerEnemy other)
    {
        Vector2 dir =
            other.transform.position -
            transform.position;

        RaycastHit2D hit =
            Physics2D.Raycast(
                transform.position,
                dir.normalized,
                dir.magnitude,
                obstacleMask);

        return hit.collider != null;
    }

    private void OnDisable()
    {
        if (target != null)
        {
            if (target.follower == this)
            {
                target.follower = null;
            }
        }

        if (follower != null)
        {
            follower.StopFollowing();
        }

        rb.linearVelocity = Vector2.zero;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(
            transform.position,
            searchRadius);

        if (target != null)
        {
            Gizmos.color = Color.green;

            Gizmos.DrawLine(
                transform.position,
                target.transform.position);
        }
    }
}