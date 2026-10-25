using UnityEngine;

public abstract class MPBuffData : BuffData
{
    [Min(0.01f)] public float tickInterval = 1f;
    [Min(0f)] public float mpPerTick = 1f;

    public override BuffInstance CreateInstance(GameObject target) => new MPBuffInstance(target, this);
}

public class MPBuffInstance : BuffInstance
{
    private readonly MPBuffData mpData;
    private float tickTimer;
    private PlayerMP playerMP;

    public MPBuffInstance(GameObject target, MPBuffData data) : base(target, data)
    {
        mpData = data;
    }

    public override void OnApply()
    {
        playerMP = target != null ? target.GetComponent<PlayerMP>() : null;
        if (playerMP != null && mpData is HungerBuffData hunger)
            playerMP.SetBuffConsumptionMultiplier(hunger.consumptionMultiplier);
    }

    public override void OnRemove()
    {
        if (playerMP != null && mpData is HungerBuffData)
            playerMP.SetBuffConsumptionMultiplier(1f);
    }

    public override void OnUpdate(float deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (playerMP == null || IsFinished) return;
        tickTimer += deltaTime;
        float interval = Mathf.Max(0.01f, mpData.tickInterval);
        while (tickTimer >= interval)
        {
            tickTimer -= interval;
            if (mpData is CorrosionBuffData)
                playerMP.ChangeMP(-mpData.mpPerTick);
            else if (mpData is MoistureBuffData)
                playerMP.Restore(mpData.mpPerTick);
        }
    }
}
