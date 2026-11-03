using UnityEngine;

/// 敵専用の自動剣攻撃スクリプト。
///
/// 概要:
/// ・プレイヤーが射程内にいて、間に障害物（LayerMaskで判定）が無い場合のみ自動的に攻撃する
/// ・攻撃間隔はランダム（min〜max）
/// ・攻撃時は手のTransformをプレイヤー方向へ動かし、終了後は追従動作を再開する
/// ・このスクリプトは剣自身にアタッチする想定。Start時に自動で「手」の子オブジェクトになる
///
/// 前提（既存プロジェクトに依存する箇所）:
/// ・IWeapon インターフェースは Attack() / SpecialAbility() を持つ想定
///   （NewSwordAction と同じシグネチャを踏襲）
/// ・プレイヤーには "Player" タグが付いている想定（未設定なら player フィールドへ手動アサイン可）
/// ・敵の階層内に "Hand_R" があれば手として使用し、なければ "HB" を探す
public class EnemySwordAction : MonoBehaviour, IWeapon
{
    [Header("参照設定")]
    [Tooltip("プレイヤー。未設定の場合は Player タグから自動取得")]
    [SerializeField] private Transform player;

    [Tooltip("剣と一緒に動かす「手」。未設定の場合は敵階層から Hand_R、次にHBを検索")]
    [SerializeField] private Transform targetHand;

    [Tooltip("敵本体（親オブジェクト）。未設定の場合は親Transformを自動取得")]
    [SerializeField] private Transform enemyRoot;

    [SerializeField] private string handObjectName = "HB";

    [Header("攻撃条件")]
    [Tooltip("攻撃を開始できる最大距離。プレイヤーがこの距離内なら攻撃を起動する")]
    [SerializeField] private float maxAttackDistance = 5.0f;
    [Tooltip("剣が実際に振り出す最大距離。ここを短くすると、攻撃はするが届かない演出が作れる")]
    [SerializeField] private float attackReachDistance = 5.0f;
    [SerializeField] private float attackRayDistance = 6.0f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("攻撃間隔（自動発動・ランダム）")]
    [SerializeField] private float minAttackInterval = 1.5f;
    [SerializeField] private float maxAttackInterval = 3.5f;

    [Header("動作のなめらかさ")]
    [SerializeField] private float attackSmoothTime = 0.05f;
    [SerializeField] private float attackDuration = 0.2f;
    [SerializeField] private float returnSmoothTime = 0.2f;
    [SerializeField] private float stopThreshold = 0.1f;
    [SerializeField] private float rotationSmoothTime = 0.05f;

    [Header("オーディオ（任意）")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip attackSound;

    private enum State { Idle, Attacking, Returning }
    private State currentState = State.Idle;

    private Vector3 currentVelocity;
    private float currentRotationVelocity;
    private Vector3 attackTargetPos;
    private float attackTimer;
    private float nextAttackTime;
    private Vector3 handReturnOffset;
    private Quaternion handReturnRotation;
    private Follow_WithAnimation handFollow;

    public bool IsAttacking => currentState != State.Idle;

    private void Awake()
    {
        // 手（targetHand）の自動取得。見た目の手を先に探し、なければHBを使う
        if (targetHand == null)
        {
            Transform searchRoot = enemyRoot != null ? enemyRoot : transform.root;
            Transform hand = FindChildByName(searchRoot, "Hand_R");
            if (hand == null) hand = FindChildByName(searchRoot, handObjectName);
            targetHand = hand != null ? hand : searchRoot;
        }

        // 剣の親(手)ではなく、手を含む敵本体を基準にする。
        if (enemyRoot == null)
            enemyRoot = targetHand != null && targetHand.parent != null ? targetHand.parent : transform.root;

        if (targetHand != null)
            handFollow = targetHand.GetComponent<Follow_WithAnimation>();

        // プレイヤーの自動取得
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) player = playerObj.transform;
        }
    }

    private void Start()
    {
        // 剣を手の子オブジェクトにする
        if (targetHand != null && transform.parent != targetHand)
            transform.SetParent(targetHand, true);

        Transform returnAnchor = GetHandReturnAnchor();
        handReturnOffset = returnAnchor != null
            ? returnAnchor.InverseTransformPoint(targetHand.position)
            : targetHand.position - transform.root.position;
        handReturnRotation = returnAnchor != null
            ? Quaternion.Inverse(returnAnchor.rotation) * targetHand.rotation
            : targetHand.rotation;

        ScheduleNextAttack();
    }

    // --- IWeapon 実装 ---
    public void Attack()
    {
        if (currentState == State.Idle)
            StartAttack();
    }

    public void SpecialAbility() { }

    private void LateUpdate()
    {
        if (player == null || targetHand == null) return;

        switch (currentState)
        {
            case State.Idle:
                // 自動発動：ランダム間隔ごとに攻撃可否を判定
                if (Time.time >= nextAttackTime)
                {
                    if (CanAttackAtPlayer())
                        Attack();
                    else
                        ScheduleNextAttack(); // 条件を満たさなければ次のタイミングを再抽選
                }
                break;

            case State.Attacking:
                attackTimer += Time.deltaTime;
                RotateHandSmoothly(attackTargetPos);
                targetHand.position = Vector3.SmoothDamp(
                    targetHand.position, attackTargetPos, ref currentVelocity, attackSmoothTime);

                if (attackTimer >= attackDuration)
                    currentState = State.Returning;
                break;

            case State.Returning:
                Transform returnAnchor = GetHandReturnAnchor();
                Vector3 returnDest = returnAnchor != null
                    ? returnAnchor.TransformPoint(handReturnOffset)
                    : transform.root.position + handReturnOffset;
                Quaternion returnRotation = returnAnchor != null
                    ? returnAnchor.rotation * handReturnRotation
                    : handReturnRotation;
                RotateHandSmoothlyTo(returnRotation.eulerAngles.z);
                targetHand.position = Vector3.SmoothDamp(
                    targetHand.position, returnDest, ref currentVelocity, returnSmoothTime);

                if (Vector3.Distance(targetHand.position, returnDest) < stopThreshold)
                    FinishReturn(returnDest);
                break;
        }
    }

    private void StartAttack()
    {
        if (handFollow != null)
            handFollow.enabled = false;

        if (audioSource != null && attackSound != null)
            audioSource.PlayOneShot(attackSound);

        UpdateRawTarget();

        currentState = State.Attacking;
        attackTimer = 0f;
        currentVelocity = Vector3.zero;
        currentRotationVelocity = 0f;
    }

    /// <summary>
    /// プレイヤー方向・射程に基づいて攻撃の到達点を計算する
    /// </summary>
    private void UpdateRawTarget()
    {
        Vector3 direction = player.position - targetHand.position;
        direction.z = 0f;
        direction.Normalize();

        float reachDistance = Mathf.Clamp(attackReachDistance, 0f, maxAttackDistance);
        float dist = Mathf.Min(Vector3.Distance(targetHand.position, player.position), reachDistance);

        attackTargetPos = targetHand.position + direction * dist;
        attackTargetPos.z = targetHand.position.z;
    }

    /// <summary>
    /// プレイヤーが射程内にいて、かつ間に障害物（obstacleMask）が無いかを判定する
    /// </summary>
    private bool CanAttackAtPlayer()
    {
        if (player == null) return false;

        Vector3 origin = targetHand.position;
        Vector3 toPlayer = player.position - origin;
        float distance = toPlayer.magnitude;

        if (distance > maxAttackDistance) return false;

        Vector2 dir = toPlayer.normalized;
        RaycastHit2D hit = Physics2D.Raycast(origin, dir, attackRayDistance, obstacleMask);

        // レイが障害物に当たり、その距離がプレイヤーより手前なら攻撃不可（遮蔽されている）
        if (hit.collider != null && hit.distance < distance)
            return false;

        return true;
    }

    private void ScheduleNextAttack()
    {
        nextAttackTime = Time.time + Random.Range(minAttackInterval, maxAttackInterval);
    }

    private void RotateHandSmoothly(Vector3 target)
    {
        Vector2 dir = target - targetHand.position;
        if (dir.sqrMagnitude > 0.001f)
        {
            float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            ApplySmoothHandRotation(targetAngle);
        }
    }

    private void RotateHandSmoothlyTo(float targetAngle) => ApplySmoothHandRotation(targetAngle);

    private void ApplySmoothHandRotation(float targetAngle)
    {
        float currentAngle = targetHand.eulerAngles.z;
        float newAngle = Mathf.SmoothDampAngle(currentAngle, targetAngle, ref currentRotationVelocity, rotationSmoothTime);
        targetHand.rotation = Quaternion.Euler(0, 0, newAngle);
    }

    private void FinishReturn(Vector3 snapPos)
    {
        targetHand.position = snapPos;
        Transform returnAnchor = GetHandReturnAnchor();
        targetHand.rotation = returnAnchor != null
            ? returnAnchor.rotation * handReturnRotation
            : handReturnRotation;

        currentState = State.Idle;
        if (handFollow != null)
            handFollow.enabled = true;
        ScheduleNextAttack();
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            if (t.name == name) return t;
        }
        return null;
    }

    private Transform GetHandReturnAnchor()
    {
        if (handFollow != null && handFollow.FollowTarget != null)
            return handFollow.FollowTarget;

        return enemyRoot;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = targetHand != null ? targetHand.position : transform.position;

        Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
        Gizmos.DrawWireSphere(origin, maxAttackDistance);

        Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
        Gizmos.DrawWireSphere(origin, Mathf.Clamp(attackReachDistance, 0f, maxAttackDistance));
    }

    private void OnDestroy()
    {
        if (handFollow != null)
            handFollow.enabled = true;
    }
}
