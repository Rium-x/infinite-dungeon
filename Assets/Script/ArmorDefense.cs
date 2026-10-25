using UnityEngine;

public class ArmorDefense : MonoBehaviour
{
    [Header("防御設定")]
    [Range(0f, 100f)]
    [SerializeField] private float defenseChance = 100f;

    [Range(0f, 100f)]
    [SerializeField] private float damageReductionPercent = 25f;

    [Range(0f, 100f)]
    [SerializeField] private float perfectBlockChance = 0f;

    [Min(0f)]
    [SerializeField] private float minimumDamage = 0f;

    [Min(0f)]
    [SerializeField] private float blockCooldown = 0f;

    [SerializeField] private bool affectNonEnemyDamage = false;

    private float nextBlockTime;
    private PlayerHealth playerHealth;
    public float DefenseChance => defenseChance;
    public float DamageReductionPercent => damageReductionPercent;

    private void Awake()
    {
        FindPlayerHealth();
    }

    private void OnTransformParentChanged()
    {
        FindPlayerHealth();
    }

    private void FindPlayerHealth()
    {
        playerHealth = GetComponentInParent<PlayerHealth>();
    }

    public float ApplyDamageReduction(float damage, bool isEnemyDamage = true)
    {
        if (damage <= 0f) return 0f;
        if (!isEnemyDamage && !affectNonEnemyDamage) return damage;
        if (Time.time < nextBlockTime) return damage;
        if (Random.value > defenseChance / 100f) return damage;

        nextBlockTime = Time.time + blockCooldown;
        if (Random.value <= perfectBlockChance / 100f) return 0f;

        float reducedDamage = damage * (1f - damageReductionPercent / 100f);
        return Mathf.Max(minimumDamage, reducedDamage);
    }

    public PlayerHealth PlayerHealth => playerHealth;
}
