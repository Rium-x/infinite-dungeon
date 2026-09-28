using UnityEngine;

/// 敵専用の自動剣攻撃スクリプト。
///
/// 概要:
/// ・プレイヤーが射程内にいて、間に障害物（LayerMaskで判定）が無い場合のみ自動的に攻撃する
/// ・攻撃間隔はランダム（min〜max）
/// ・攻撃時は「手（HB優先）」を基準にプレイヤー方向へ剣が振られ、終了後は手元へ滑らかに戻る
/// ・このスクリプトは剣自身にアタッチする想定。Start時に自動で「手」の子オブジェクトになる
///
/// 前提（既存プロジェクトに依存する箇所）:
/// ・IWeapon インターフェースは Attack() / SpecialAbility() を持つ想定
///   （NewSwordAction と同じシグネチャを踏襲）
/// ・プレイヤーには "Player" タグが付いている想定（未設定なら player フィールドへ手動アサイン可）
/// ・敵の階層内に "HB" という名前のオブジェクトがあれば、それを手の基準として最優先で使用する
///   （見つからない場合は enemyRoot＝敵の親オブジェクトを手の基準として使う）
public class EnemySwordAction : MonoBehaviour, IWeapon
{
    [Header("参照設定")]
    [Tooltip("プレイヤー。未設定の場合は Player タグから自動取得")]
    [SerializeField] private Transform player;

    [Tooltip("剣を振る基準となる「手」。未設定の場合は enemyRoot 配下の \"HB\" を自動検索")]
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
    private Vector3 handLocalOffset; // 「手元」＝剣の基準位置（ローカル座標）
    private float fixedZ;

    public bool IsAttacking => currentState != State.Idle;

    private void Awake()
    {
        // 敵本体（親）の自動取得
        if (enemyRoot == null)
            enemyRoot = transform.parent != null ? transform.parent : transform;

        // 手（targetHand）の自動取得。"HB" を優先的に探す
        if (targetHand == null)
        {
            Transform hb = FindChildByName(enemyRoot, handObjectName);
            targetHand = hb != null ? hb : enemyRoot;
        }

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

        fixedZ = transform.position.z;
        handLocalOffset = transform.localPosition; // これが「手元＝基準位置」

        ScheduleNextAttack();
    }

    // --- IWeapon 実装 ---
    public void Attack()
    {
        if (currentState == State.Idle)
            StartAttack();
    }

    public void SpecialAbility() { }

    private void Update()
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
                RotateSmoothly(attackTargetPos);
                transform.position = Vector3.SmoothDamp(
                    transform.position, attackTargetPos, ref currentVelocity, attackSmoothTime);

                if (attackTimer >= attackDuration)
                    currentState = State.Returning;
                break;

            case State.Returning:
                Vector3 returnDest = targetHand.TransformPoint(handLocalOffset);
                RotateSmoothlyToZero();
                transform.position = Vector3.SmoothDamp(
                    transform.position, returnDest, ref currentVelocity, returnSmoothTime);

                if (Vector3.Distance(transform.position, returnDest) < stopThreshold)
                    FinishReturn(returnDest);
                break;
        }
    }

    private void StartAttack()
    {
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
        attackTargetPos.z = fixedZ;
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

    private void RotateSmoothly(Vector3 target)
    {
        Vector2 dir = target - transform.position;
        if (dir.sqrMagnitude > 0.001f)
        {
            float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            ApplySmoothRotation(targetAngle);
        }
    }

    private void RotateSmoothlyToZero() => ApplySmoothRotation(0f);

    private void ApplySmoothRotation(float targetAngle)
    {
        float currentAngle = transform.eulerAngles.z;
        float newAngle = Mathf.SmoothDampAngle(currentAngle, targetAngle, ref currentRotationVelocity, rotationSmoothTime);
        transform.rotation = Quaternion.Euler(0, 0, newAngle);
    }

    private void FinishReturn(Vector3 snapPos)
    {
        transform.position = snapPos;
        transform.rotation = Quaternion.Euler(0, 0, 0);

        currentState = State.Idle;
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

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = targetHand != null ? targetHand.position : transform.position;

        Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
        Gizmos.DrawWireSphere(origin, maxAttackDistance);

        Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
        Gizmos.DrawWireSphere(origin, Mathf.Clamp(attackReachDistance, 0f, maxAttackDistance));
    }
}