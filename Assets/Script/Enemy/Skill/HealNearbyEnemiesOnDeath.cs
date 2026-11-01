using UnityEngine;

/// <summary>
/// プレイヤーに倒されたとき、周囲のエネミーを回復するスキル。
/// EnemyHealthを持たないHBなどの子オブジェクトに追加して使用する。
/// </summary>
public sealed class HealNearbyEnemiesOnDeath : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("このキャラクターが倒されたとき、周囲のエネミーを回復するHP量です。")]
    private float healAmount = 10f;

    [SerializeField, Min(0f), Tooltip("このHBを中心とする回復範囲です。")]
    private float range = 3f;

    private EnemyHealth ownerHealth;

    private void Awake()
    {
        ownerHealth = GetComponentInParent<EnemyHealth>();
    }

    private void OnEnable()
    {
        EnemyHealth.OnAnyEnemyDestroyed += HandleEnemyDestroyed;
    }

    private void OnDisable()
    {
        EnemyHealth.OnAnyEnemyDestroyed -= HandleEnemyDestroyed;
    }

    private void HandleEnemyDestroyed(EnemyHealth defeatedEnemy, bool killedByPlayer)
    {
        if (!killedByPlayer || ownerHealth == null || defeatedEnemy != ownerHealth || healAmount <= 0f)
            return;

        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        Vector2 origin = transform.position;

        foreach (EnemyHealth enemy in enemies)
        {
            if (enemy == null || enemy == ownerHealth)
                continue;

            if (Vector2.Distance(origin, enemy.transform.position) <= range)
                enemy.Heal(healAmount);
        }
    }
}
