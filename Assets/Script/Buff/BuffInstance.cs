using UnityEngine;

public abstract class BuffInstance
{
    protected GameObject target;

    protected float remainingTime;

    public BuffData Data { get; private set; }

    public bool IsFinished => remainingTime <= 0;

    public BuffInstance(
        GameObject target,
        BuffData data
    )
    {
        this.target = target;

        Data = data;

        remainingTime = data.duration;
    }

    public virtual void OnApply() { }

    public virtual void OnRemove() { }

    public virtual void OnUpdate(float deltaTime)
    {
        remainingTime -= deltaTime;
    }

    public void RefreshDuration()
    {
        remainingTime = Data.duration;
    }

    public float GetRemainingTime()
{
    return remainingTime;
}
}