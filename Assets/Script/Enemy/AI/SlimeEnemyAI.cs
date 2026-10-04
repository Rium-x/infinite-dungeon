using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SlimeItemEater))]
public class SlimeEnemyAI : MonoBehaviour
{
[Header("探索対象")]
[SerializeField] private float searchRadius = 8f;
[SerializeField] private LayerMask itemLayer;

[Tooltip("植物系エネミーを探す半径")]
[SerializeField] private float enemySearchRadius = 8f;

[Header("探索間隔")]
[SerializeField] private float searchInterval = 0.5f;

[Header("移動")]
[SerializeField] private float moveSpeed = 2f;
[SerializeField] private float acceleration = 8f;

[Header("捕食・移動")]
[SerializeField] private float eatDistance = 0.5f;

private Rigidbody2D rb;
private SlimeItemEater eater;
private MoveStats moveStats;

private Item targetItem;
private EnemyHealth targetEnemy;

private float searchTimer;

private void Awake()
{
    rb = GetComponent<Rigidbody2D>();
    eater = GetComponent<SlimeItemEater>();
    moveStats = GetComponent<MoveStats>();
}

private void Update()
{
    if (eater.IsEating)
        return;

    searchTimer -= Time.deltaTime;

    if (searchTimer <= 0f)
    {
        searchTimer = searchInterval;

        FindNearestItem();
        FindPlant();
    }

    if (targetItem != null)
    {
        float itemDistance =
            Vector2.Distance(
                transform.position,
                targetItem.transform.position
            );

        if (itemDistance <= eatDistance)
        {
            eater.StartEat(targetItem);
            targetItem = null;
        }
    }
}

private void FixedUpdate()
{
    if (eater.IsEating)
    {
        rb.linearVelocity = Vector2.zero;
        return;
    }

    Transform target = GetCurrentTarget();

    if (target == null)
    {
        rb.linearVelocity =
            Vector2.Lerp(
                rb.linearVelocity,
                Vector2.zero,
                acceleration * Time.fixedDeltaTime
            );

        return;
    }

    Vector2 direction =
        (
            (Vector2)target.position -
            (Vector2)transform.position
        ).normalized;

    float speed = moveSpeed;

    if (moveStats != null)
    {
        speed =
            moveStats.ApplySpeed(
                moveSpeed
            );
    }

    Vector2 targetVelocity =
        direction * speed;

    rb.linearVelocity =
        Vector2.Lerp(
            rb.linearVelocity,
            targetVelocity,
            acceleration * Time.fixedDeltaTime
        );
}

private Transform GetCurrentTarget()
{
    float itemDistance = float.MaxValue;
    float plantDistance = float.MaxValue;

    if (targetItem != null)
    {
        itemDistance =
            Vector2.Distance(
                transform.position,
                targetItem.transform.position
            );
    }

    if (targetEnemy != null)
    {
        plantDistance =
            Vector2.Distance(
                transform.position,
                targetEnemy.transform.position
            );
    }

    if (
        targetItem == null &&
        targetEnemy == null
    )
    {
        return null;
    }

    if (
        targetItem != null &&
        itemDistance <= plantDistance
    )
    {
        return targetItem.transform;
    }

    if (targetEnemy != null)
    {
        return targetEnemy.transform;
    }

    return null;
}

private void FindNearestItem()
{
    Collider2D[] hits =
        Physics2D.OverlapCircleAll(
            transform.position,
            searchRadius,
            itemLayer
        );

    float closestDistance =
        float.MaxValue;

    Item closestItem = null;

    foreach (Collider2D hit in hits)
    {
        Item item =
            hit.GetComponent<Item>();

        if (item == null)
            continue;

        float distance =
            Vector2.Distance(
                transform.position,
                item.transform.position
            );

        if (distance < closestDistance)
        {
            closestDistance = distance;
            closestItem = item;
        }
    }

    targetItem = closestItem;
}

private void FindPlant()
{

    
    EnemyFaction[] factions =
        FindObjectsByType<EnemyFaction>();

    float closestDistance =
        float.MaxValue;

    EnemyHealth closestEnemy = null;

    foreach (EnemyFaction faction in factions)
    {
        if (faction.faction != Faction.Plant)
            continue;

        EnemyHealth health =
            faction.GetComponentInParent<EnemyHealth>();

        if (health == null)
            continue;

        float distance =
            Vector2.Distance(
                transform.position,
                faction.transform.position
            );

        if (
            distance < closestDistance &&
            distance <= enemySearchRadius
        )
        {
            closestDistance = distance;
            closestEnemy = health;
        }
    }

    targetEnemy = closestEnemy;

    if (targetEnemy != null)
{
    Debug.Log(
        gameObject.name +
        " 植物発見: " +
        targetEnemy.name
    );
}
}

private void OnDrawGizmosSelected()
{
    Gizmos.color = Color.green;
    Gizmos.DrawWireSphere(
        transform.position,
        searchRadius
    );

    Gizmos.color = Color.red;
    Gizmos.DrawWireSphere(
        transform.position,
        enemySearchRadius
    );
}


}
