using UnityEngine;
using System.Collections.Generic; // リストを使うために必要

public class SwordAttack : MonoBehaviour
{
    [Header("剣のダメージ")]
    public float attackDamage = 10f;

    // ★ 攻撃ごとにリセットされる「攻撃済みリスト」
    private HashSet<GameObject> hitEnemies = new HashSet<GameObject>();
    private NewSwordAction swordAction;
    private Transform playerHitOrigin;
    private int wallLayerMask;
    private bool wasAttackingLastFrame = false;
    private bool durabilityAppliedThisAttack;

    void Start()
    {
        swordAction = GetComponentInParent<NewSwordAction>();
        wallLayerMask = LayerMask.GetMask("Wall");
    }

    void Update()
    {
        if (swordAction == null) return;

        // ★ 攻撃が始まった瞬間にリストをクリアする
        if (swordAction.IsAttacking && !wasAttackingLastFrame)
        {
            hitEnemies.Clear();
            durabilityAppliedThisAttack = false;
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

                // Only the player's weapon has a player root. Cast from the player's
                // HB point, lowered by 0.15 units, toward the sword contact point.
                // A Wall hit anywhere along that segment blocks this attack.
                // NewSwordAction.Start may run after this component's Start, so resolve
                // the player's HB on each hit rather than caching a possibly-null value.
                if (swordAction.PlayerTransform != null)
                {
                    Transform playerTransform = swordAction.PlayerTransform;
                    // NewSwordAction can resolve RandomTeleporter on the HB itself;
                    // in that case searching its children for another HB always fails.
                    playerHitOrigin = playerTransform.name == "HB"
                        ? playerTransform
                        : playerTransform.Find("HB");
                    // A player's attack without its required HB origin cannot be
                    // confirmed as unblocked, so reject it safely.
                    if (playerHitOrigin == null || IsBlockedByWall(other)) return;
                }

                enemy.TakeDamage(attackDamage, true);

                if (!durabilityAppliedThisAttack)
                {
                    PlayerCombat playerCombat = GetComponentInParent<PlayerCombat>();
                    if (playerCombat != null)
                        playerCombat.DamageWeaponDurability(transform);
                    durabilityAppliedThisAttack = true;
                }

                // ★ 本体を登録
                hitEnemies.Add(enemy.gameObject);
            }
        }
    }

    private bool IsBlockedByWall(Collider2D target)
    {
        if (wallLayerMask == 0) return true;

        Vector2 origin = (Vector2)playerHitOrigin.position + Vector2.down * 0.15f;
        Vector2 destination = target.ClosestPoint(origin);
        Vector2 ray = destination - origin;
        if (ray.sqrMagnitude <= 0.0001f) return false;

        RaycastHit2D hit = Physics2D.Raycast(origin, ray.normalized, ray.magnitude, wallLayerMask);
        return hit.collider != null;
    }
}
