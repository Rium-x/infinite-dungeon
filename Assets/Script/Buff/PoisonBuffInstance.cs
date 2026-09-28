using UnityEngine;

public class PoisonBuffInstance : BuffInstance
{
    private PoisonBuffData poisonData;

    private float tickTimer;

    public PoisonBuffInstance(
        GameObject target,
        PoisonBuffData data
    ) : base(target, data)
    {
        poisonData = data;
    }

    public override void OnUpdate(float deltaTime)
    {
        base.OnUpdate(deltaTime);

        tickTimer += deltaTime;

        if (tickTimer >= poisonData.tickInterval)
        {
            tickTimer = 0f;

            EnemyHealth enemy =
                target.GetComponent<EnemyHealth>();

            if (enemy != null)
            {
                enemy.TakeDamage(
                    poisonData.damagePerTick
                );
            }

            PlayerHealth player =
                target.GetComponent<PlayerHealth>();

            if (player != null)
            {
                player.TakeDamage(
                    poisonData.damagePerTick
                );
            }
        }
    }
}