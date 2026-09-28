using UnityEngine;

public class EnemyTouchHandler : MonoBehaviour
{
    private EnemyHealth enemyHealth;
    private bool isTouchingPlayer = false;
    private PlayerControls controls; // ★追加

    private void Awake()
    {
        controls = new PlayerControls(); // ★追加
    }

    private void OnEnable()  => controls.Enable();  // ★追加
    private void OnDisable() => controls.Disable(); // ★追加

    void Start()
    {
        enemyHealth = GetComponentInParent<EnemyHealth>();
        if (enemyHealth == null)
        {
            Debug.LogError("親オブジェクトに EnemyHealth が見つかりません！");
        }
    }

    void Update()
    {
        if (BSPDungeonMaster.IsPlayerInputLocked) return;

        // ★Input.GetKeyDown(KeyCode.X) から変更
        if (isTouchingPlayer && controls.Player.PickUp.triggered)
        {
            if (enemyHealth != null)
            {
                enemyHealth.TakeDamage(10, true);
                Debug.Log($"{gameObject.name} を通じて親の EnemyHealth に10ダメージ！");
            }
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player")) isTouchingPlayer = true;
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player")) isTouchingPlayer = false;
    }
}