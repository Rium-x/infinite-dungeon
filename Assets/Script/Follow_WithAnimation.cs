using UnityEngine;

public class Follow_WithAnimation : MonoBehaviour
{
    [Header("追跡設定")]
    [SerializeField] private Transform target; 
    [SerializeField] private float smoothTime = 0.1f;
    [SerializeField] private Vector3 offset = Vector3.zero;

    public Transform FollowTarget => target;

    /// <summary>水などの外部効果が追従先アンカーを切り替えます。</summary>
    public void SetFollowTarget(Transform newTarget)
    {
        if (newTarget != null)
            target = newTarget;
    }

    [Header("歩行アニメーション設定")]
    [SerializeField] private Rigidbody2D playerRb;
    [SerializeField] private float bobAmount = 0.2f; 
    [SerializeField] private float phaseOffset = 0f;

    [Header("揺れる方向の設定")]
    [SerializeField] private Vector3 wobbleDirection = Vector3.up; 

    [Header("速度連動の設定")]
    [SerializeField] private float speedMultiplier = 2.0f; 
    [SerializeField] private float maxBobSpeed = 20.0f;    

    [Header("停止時の滑らかさ")]
    [Range(1f, 10f)]
    [SerializeField] private float stopSmoothness = 3f; 

    private Vector3 velocity = Vector3.zero;
    private float timer = 0f;
    private float currentBobAmount = 0f;
    private float currentBobSpeed = 0f;

    // --- ここで1つにまとめました ---
    void Start()
    {
        // もしインスペクターで Rigidbody2D が空だったら自動取得
        if (playerRb == null)
        {
            playerRb = GetComponentInParent<Rigidbody2D>();
        }

        // ターゲットがいる場合は初期位置を合わせる
        if (target != null)
        {
            Vector3 startPos = target.position + offset;
            startPos.z = transform.position.z;
            transform.position = startPos;
        }
    }

    void LateUpdate()
    {
        // ターゲットかRigidbodyのどちらかが欠けていたら何もしない
        if (target == null || playerRb == null) return;

        // Rigidbody2Dの最新のプロパティ名(velocity)を使用
        float moveSpeed = playerRb.linearVelocity.magnitude;

        // 速度計算
        float targetSpeed = moveSpeed * speedMultiplier;
        targetSpeed = Mathf.Min(targetSpeed, maxBobSpeed);
        float targetAmount = (moveSpeed > 0.1f) ? bobAmount : 0f;

        // 滑らかに変化
        currentBobSpeed = Mathf.Lerp(currentBobSpeed, targetSpeed, Time.deltaTime * stopSmoothness);
        currentBobAmount = Mathf.Lerp(currentBobAmount, targetAmount, Time.deltaTime * stopSmoothness);

        // タイマー更新
        timer += Time.deltaTime * currentBobSpeed;

        // --- 揺れの計算 ---
        float wave = Mathf.Sin(timer + phaseOffset);
        Vector3 wobbleOffset = wobbleDirection.normalized * (wave * currentBobAmount);

        // 最終的な目標位置
        Vector3 targetPosition = target.position + offset + wobbleOffset;
        targetPosition.z = transform.position.z;

        // SmoothDampで移動
        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref velocity,
            smoothTime
        );
    }
}
