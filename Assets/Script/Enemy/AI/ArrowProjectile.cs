using UnityEngine;
using System.Collections.Generic;

/// <summary>遠隔敵が発射する矢。プレイヤーに命中するとダメージを与え、壁で消える。</summary>
[RequireComponent(typeof(Rigidbody2D))]
public class ArrowProjectile : MonoBehaviour
{
    [Header("攻撃設定")]
    [SerializeField, Min(0f)] private float damage = 10f;
    [SerializeField] private List<BuffData> buffsToApply = new List<BuffData>();
    [SerializeField, Min(0.1f)] private float speed = 8f;
    [SerializeField, Min(0.1f)] private float lifetime = 5f;
    [SerializeField] private LayerMask wallMask = 8;

    private Rigidbody2D rb;
    private bool hasHit;
    private EnemyFaction sourceFaction;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }

    public void Launch(Vector2 direction)
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.linearVelocity = direction.normalized * speed;
    }

    public void SetSourceFaction(EnemyFaction faction) => sourceFaction = faction;

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleHit(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        HandleHit(collision.collider);
    }

    private void HandleHit(Collider2D other)
    {
        if (hasHit || other == null) return;
        bool damagedTarget = false;
        if (other.CompareTag("P_HB"))
        {
            PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage, true);
                ApplyBuffs(other.GetComponentInParent<BuffController>());
                damagedTarget = true;
            }
        }

        EnemyFaction targetFaction = other.GetComponentInParent<EnemyFaction>();
        if (targetFaction != null && sourceFaction != null
            && FactionUtility.IsEnemy(sourceFaction.faction, targetFaction.faction))
        {
            EnemyHealth enemyHealth = other.GetComponentInParent<EnemyHealth>();
            if (enemyHealth != null)
            {
                enemyHealth.TakeDamage(damage);
                ApplyBuffs(other.GetComponentInParent<BuffController>());
                damagedTarget = true;
            }
        }

        if (damagedTarget)
        {
            hasHit = true;
            Destroy(gameObject);
            return;
        }

        bool isWall = (wallMask.value & (1 << other.gameObject.layer)) != 0
            || other.CompareTag("wall");
        if (isWall)
        {
            hasHit = true;
            Destroy(gameObject);
        }
    }

    private void ApplyBuffs(BuffController target)
    {
        if (target == null || buffsToApply == null) return;
        foreach (BuffData buff in buffsToApply)
            if (buff != null) target.AddBuff(buff);
    }
}
