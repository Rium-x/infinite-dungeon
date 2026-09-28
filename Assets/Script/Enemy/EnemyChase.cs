using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))] // Rigidbody2Dを必須にする
public class EnemyChase : MonoBehaviour
{
    [Header("ターゲット設定")]
    [SerializeField] private Transform player;
    [SerializeField] private float detectRange = 10.0f;
    [SerializeField] private LayerMask detectionMask;

    [Header("移動設定")]
    [SerializeField] private float maxSpeed = 3.0f;      // 最高速度
    [SerializeField] private float acceleration = 5.0f; // 加速度（大きいほどすぐ最高速になる）
    
    private Rigidbody2D rb;
    private Vector2 moveInput;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        if (player == null) return;

        Vector2 direction = (player.position - transform.position).normalized;
        float distanceToPlayer = Vector2.Distance(transform.position, player.position);

        moveInput = Vector2.zero; // 基本は止まる

        if (distanceToPlayer <= detectRange)
        {
            RaycastHit2D hit = Physics2D.Raycast(transform.position, direction, detectRange, detectionMask);

            if (hit.collider != null)
            {
                // 視覚化（デバッグ用）
                Debug.DrawLine(transform.position, hit.point, hit.collider.CompareTag("Player") ? Color.blue : Color.red);

                if (hit.collider.CompareTag("Player"))
                {
                    // プレイヤーが見えている時だけ移動方向をセット
                    moveInput = direction;
                }
            }
        }
    }

    void FixedUpdate()
    {
        // 物理演算に基づいた滑らかな加速・減速
        // 現在の速度から目標の速度（moveInput * maxSpeed）へ、徐々に近づける
        Vector2 targetVelocity = moveInput * maxSpeed;
        rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, targetVelocity, acceleration * Time.fixedDeltaTime);
    }
}