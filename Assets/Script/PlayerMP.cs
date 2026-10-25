using UnityEngine;
using UnityEngine.UI;

/// <summary>Player MP state and activity-based consumption/recovery.</summary>
public class PlayerMP : MonoBehaviour
{
    public const float BaseMaximumMP = 25f;
    public float MaximumMP { get; private set; } = BaseMaximumMP;
    public static PlayerMP Instance { get; private set; }

    [SerializeField, Range(0f, BaseMaximumMP)] private float currentMP = BaseMaximumMP;
    [SerializeField] private Slider mpSlider;
    [SerializeField, Min(0f)] private float idleRecoveryPerSecond = 0.1f;
    [SerializeField, Min(0f)] private float idleRecoveryDelay = 5f;
    [SerializeField, Min(0f)] private float walkingRampSeconds = 120f;
    [SerializeField, Min(0f)] private float walkingMaximumCostPerSecond = 0.01667f;
    [SerializeField, Range(0f, 1f)] private float exhaustedSpeedMultiplier = 0.5f;
    private float buffConsumptionMultiplier = 1f;

    private float lastActivityTime;
    private float continuousWalkTime;
    public float CurrentMP => currentMP;
    public bool IsExhausted => currentMP <= 0f;
    public float SpeedMultiplier => IsExhausted ? exhaustedSpeedMultiplier : 1f;

    private void Awake()
    {
        Instance = this;
        currentMP = Mathf.Clamp(currentMP, 0f, MaximumMP);
        lastActivityTime = Time.time;
        UpdateSlider();
    }

    private void LateUpdate() => UpdateSlider();

    private void UpdateSlider()
    {
        if (mpSlider == null) return;
        mpSlider.minValue = 0f;
        mpSlider.maxValue = MaximumMP;
        mpSlider.wholeNumbers = false;
        mpSlider.SetValueWithoutNotify(currentMP);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if (Time.time - lastActivityTime >= idleRecoveryDelay)
        {
            currentMP = Mathf.Min(MaximumMP, currentMP + idleRecoveryPerSecond * Time.deltaTime);
        }
    }

    public void TickWalking(bool moving)
    {
        if (!moving) { continuousWalkTime = 0f; return; }
        lastActivityTime = Time.time;
        float before = continuousWalkTime;
        continuousWalkTime += Time.deltaTime;
        float ramp = walkingRampSeconds <= 0f ? 1f : Mathf.Clamp01(continuousWalkTime / walkingRampSeconds);
        float previousRamp = walkingRampSeconds <= 0f ? 1f : Mathf.Clamp01(before / walkingRampSeconds);
        float cost = walkingMaximumCostPerSecond * (ramp + previousRamp) * 0.5f * Time.deltaTime * buffConsumptionMultiplier;
        Spend(cost);
    }

    public bool TrySpendAttack(float amount)
    {
        if (IsExhausted) return false;
        lastActivityTime = Time.time;
        Spend(amount * buffConsumptionMultiplier);
        return true;
    }

    public void Restore(float amount) => currentMP = Mathf.Min(MaximumMP, currentMP + Mathf.Max(0f, amount));
    public void ChangeMP(float amount) => currentMP = Mathf.Clamp(currentMP + amount, 0f, MaximumMP);
    public void IncreaseMaximum(float amount)
    {
        if (amount <= 0f) return;
        MaximumMP += amount;
        currentMP += amount;
    }
    public void SetBuffConsumptionMultiplier(float multiplier) => buffConsumptionMultiplier = Mathf.Max(1f, multiplier);

    private void Spend(float amount)
    {
        if (amount <= 0f) return;
        currentMP = Mathf.Max(0f, currentMP - amount);
        lastActivityTime = Time.time;
    }
}
