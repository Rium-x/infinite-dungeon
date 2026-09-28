using UnityEngine;

public class LimbAnimator : MonoBehaviour
{
    [Header("四肢の設定")]
    [SerializeField] private Transform[] limbs; // インスペクターで4つの手足を設定
    [SerializeField] private float bobSpeed = 10f; // 動く速さ
    [SerializeField] private float bobAmount = 0.2f; // 動く幅（高さ）

    private Rigidbody2D rb;
    private Vector3[] startPositions;
    private float timer = 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        // 各パーツの初期位置を保存
        if (limbs != null && limbs.Length > 0)
        {
            startPositions = new Vector3[limbs.Length];
            for (int i = 0; i < limbs.Length; i++)
            {
                startPositions[i] = limbs[i].localPosition;
            }
        }
    }

    void Update()
    {
        // 現在の移動速度を取得（ある程度の速さがある時だけ動かす）
        float currentSpeed = rb.linearVelocity.magnitude;

        if (currentSpeed > 0.1f)
        {
            // 移動速度に応じてアニメーション速度を調整（オプション）
            timer += Time.deltaTime * bobSpeed * (currentSpeed / 5f);
            
            for (int i = 0; i < limbs.Length; i++)
            {
                if (limbs[i] == null) continue;

                // 交互に動かすために、インデックスによってサイン波を反転させる
                // i % 2 == 0 (0, 2番目) はそのまま、それ以外 (1, 3番目) はマイナス
                float offset = (i % 2 == 0) ? 1.0f : -1.0f;
                float newY = startPositions[i].y + Mathf.Sin(timer) * bobAmount * offset;

                limbs[i].localPosition = new Vector3(startPositions[i].x, newY, startPositions[i].z);
            }
        }
        else
        {
            // 止まっている時はゆっくり初期位置に戻す
            timer = 0; // タイマーリセット
            for (int i = 0; i < limbs.Length; i++)
            {
                if (limbs[i] == null) continue;
                limbs[i].localPosition = Vector3.Lerp(limbs[i].localPosition, startPositions[i], Time.deltaTime * 5f);
            }
        }
    }
}
