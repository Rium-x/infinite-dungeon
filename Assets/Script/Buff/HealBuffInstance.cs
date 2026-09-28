using UnityEngine;

public class HealBuffInstance : BuffInstance
{
    private HealBuffData healData;

    private float timer;

    public HealBuffInstance(
        GameObject target,
        HealBuffData data
    ) : base(target, data)
    {
        healData = data;
    }

    public override void OnApply()
    {
        timer = 0f;

        Debug.Log(
            healData.buffName +
            " を付与"
        );
    }

    public override void OnUpdate(float deltaTime)
{
    base.OnUpdate(deltaTime);

    timer += deltaTime;

    if (timer >= healData.tickInterval)
    {
        timer -= healData.tickInterval;

        Heal();
    }
}
    private void Heal()
    {
        Debug.Log("Heal Tick");

        PlayerHealth player =
            target.GetComponent<PlayerHealth>();

        if (player != null)
        {
            Debug.Log("Player Heal");

            player.Heal(
                healData.healPerTick
            );

            return;
        }

        EnemyHealth enemy =
            target.GetComponent<EnemyHealth>();

        if (enemy != null)
        {
            Debug.Log("Enemy Heal");

            enemy.Heal(
                healData.healPerTick
            );
        }
    }

    public override void OnRemove()
    {
        Debug.Log(
            healData.buffName +
            " が解除"
        );
    }
}