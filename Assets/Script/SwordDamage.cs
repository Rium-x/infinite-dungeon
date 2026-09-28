using UnityEngine;
using System.Collections.Generic; // リストを使うために必要

public class SwordAttack : MonoBehaviour
{
    [Header("剣のダメージ")]
    public float attackDamage = 10f;

    // ★ 攻撃ごとにリセットされる「攻撃済みリスト」
    private HashSet<GameObject> hitEnemies = new HashSet<GameObject>();
    private NewSwordAction swordAction;
    private bool wasAttackingLastFrame = false;

    void Start()
    {
        swordAction = GetComponentInParent<NewSwordAction>();
    }

    void Update()
    {
        if (swordAction == null) return;

        // ★ 攻撃が始まった瞬間にリストをクリアする
        if (swordAction.IsAttacking && !wasAttackingLastFrame)
        {
            hitEnemies.Clear();
        }
        wasAttackingLastFrame = swordAction.IsAttacking;
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryDamage(other);
    }

    private void TryDamage(Collider2D other)
    {
        if (swordAction == null || !swordAction.IsAttacking) return;

        if (other.CompareTag("Enemy"))
        {
            // ★ 当たった部位の親（EnemyHealthが付いているオブジェクト）を取得
            EnemyHealth enemy = other.GetComponentInParent<EnemyHealth>();

            if (enemy != null)
            {
                // ★ 個別のコライダーではなく、「EnemyHealth（本体）」をリストでチェック
                // これで、頭・胴体・足のどこに当たっても、その敵本体へのダメージは1回のみになる
                if (hitEnemies.Contains(enemy.gameObject)) return;

                enemy.TakeDamage(attackDamage, true);
            
                // ★ 本体を登録
                hitEnemies.Add(enemy.gameObject);
            }
        }
    }
}