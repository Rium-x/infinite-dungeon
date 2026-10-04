using UnityEngine;
using System.Collections.Generic; // Listを使うために追加

public class EnemyAttack : MonoBehaviour
{
    [Header("敵の攻撃力")]
    public float attackDamage = 5.0f;

    [Header("攻撃の間隔（秒）")]
    public float attackCooldown = 2.0f;

    [Header("攻撃時に付与するバフ/デバフのリスト")]
    public List<BuffData> buffsToApply = new List<BuffData>();

    // 次に攻撃が可能になる時刻
    private float nextAttackTime;
    private EnemyFaction myFaction;

    private void Start()
    {
        myFaction = GetComponentInParent<EnemyFaction>();
    }

    // 触れた瞬間
    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDamage(other);
    }

    // 触れている間
    private void OnTriggerStay2D(Collider2D other)
    {
        TryDamage(other);
    }

    private void TryDamage(Collider2D other)
    {
        // クールタイム中なら何もしない
        if (Time.time < nextAttackTime) return;

        bool hasAttacked = false;

        // ===== 1. 相手がプレイヤーの場合 =====
        if (other.gameObject.CompareTag("P_HB"))
        {
            PlayerHealth playerHealth = other.gameObject.GetComponentInParent<PlayerHealth>();

            if (playerHealth != null)
            {
                playerHealth.TakeDamage(attackDamage, true);

                // プレイヤーのBuffControllerを取得してバフを付与
                BuffController targetBuffs = other.gameObject.GetComponentInParent<BuffController>();
                ApplyBuffsToTarget(targetBuffs);

                hasAttacked = true;
            }
        }

        // ===== 2. 相手が他エネミー（敵対勢力）の場合 =====
        EnemyFaction targetFaction = other.GetComponentInParent<EnemyFaction>();

        if (targetFaction != null && myFaction != null && FactionUtility.IsEnemy(myFaction.faction, targetFaction.faction))
        {
            EnemyHealth health = other.GetComponentInParent<EnemyHealth>();

            if (health != null)
            {
                health.TakeDamage(attackDamage);

                // 対象エネミーのBuffControllerを取得してバフを付与
                BuffController targetBuffs = other.GetComponentInParent<BuffController>();
                ApplyBuffsToTarget(targetBuffs);

                hasAttacked = true;
            }
        }

        // 攻撃が成立していたら、次回攻撃時刻を更新
        if (hasAttacked)
        {
            nextAttackTime = Time.time + attackCooldown;
        }
    }

    /// <summary>
    /// 対象にリスト内のすべてのバフ・デバフを付与する
    /// </summary>
    private void ApplyBuffsToTarget(BuffController targetBuffController)
    {
        if (targetBuffController == null || buffsToApply == null) return;

        foreach (BuffData buff in buffsToApply)
        {
            if (buff != null)
            {
                targetBuffController.AddBuff(buff);
            }
        }
    }
}