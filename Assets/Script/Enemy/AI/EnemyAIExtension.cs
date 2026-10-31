using UnityEngine;

/// <summary>EnemyAIの拡張が参照する標準の行動状態。</summary>
public enum EnemyAIState
{
    Wander = 0,
    Chase = 1,
    Search = 2,
    Investigate = 3,
    Flee = 4
}

/// <summary>
/// EnemyAIの状態行動を置き換えたり、独自状態を追加したりするための基底クラス。
/// 拡張側はRigidbody2Dを直接動かさず、EnemyAIContext経由で移動・遷移を要求する。
/// </summary>
public abstract class EnemyAIExtension : MonoBehaviour
{
    [SerializeField, Min(0)] private int priority;
    public int Priority => priority;
    public EnemyAIContext Context { get; private set; }

    internal void Initialize(EnemyAI ai)
    {
        Context = new EnemyAIContext(ai, this);
        OnInitialized(Context);
    }

    protected virtual void OnInitialized(EnemyAIContext context) { }

    /// <summary>この標準状態の行動を、この拡張が置き換えるか。</summary>
    public virtual bool OverridesState(EnemyAIState state) => false;

    /// <summary>独自状態への移行条件。優先度順に呼ばれ、最初にtrueを返した拡張が制御する。</summary>
    public virtual bool TryEnterState(EnemyAIContext context, out string stateId)
    {
        stateId = null;
        return false;
    }

    public virtual void OnStateEntered(EnemyAIContext context, string stateId) { }
    public virtual void OnStateExited(EnemyAIContext context, string stateId) { }
    public virtual void OnCustomStateUpdate(EnemyAIContext context, string stateId) { }
    public virtual void OnStandardStateUpdate(EnemyAIContext context, EnemyAIState state) { }
}

/// <summary>EnemyAIの情報参照と、競合しない移動・状態遷移要求を提供する。</summary>
public sealed class EnemyAIContext
{
    private readonly EnemyAI ai;
    private readonly EnemyAIExtension owner;

    internal EnemyAIContext(EnemyAI ai, EnemyAIExtension owner)
    {
        this.ai = ai;
        this.owner = owner;
    }

    public EnemyAIState State => ai.CurrentState;
    public string CustomStateId => ai.ActiveCustomStateId;
    public Transform Player => ai.PlayerTransform;
    public bool CanSeePlayer => ai.CanSeePlayer;
    public Vector2 CenterPosition => ai.CenterPosition;
    public Vector2 LastKnownPlayerPosition => ai.LastKnownPlayerPosition;
    public Vector2 DirectionToPlayer => ai.DirectionToPlayer;
    public float DistanceToPlayer => ai.DistanceToPlayer;
    public bool IsInCustomState => ai.IsInCustomState;

    public void SetMovement(Vector2 direction) => ai.SetExtensionMovement(owner, direction, null);
    public void SetMovement(Vector2 direction, float baseSpeed) =>
        ai.SetExtensionMovement(owner, direction, Mathf.Max(0f, baseSpeed));

    public void TransitionTo(EnemyAIState state) => ai.RequestStandardState(owner, state);
    public void TransitionToCustomState(string stateId) => ai.RequestCustomState(owner, stateId);
    public Vector2 GetFarthestDirection() => ai.GetFarthestDirectionForExtension();
    public Vector2 ChooseDirection(Vector2 desiredDirection) =>
        ai.ChooseDirectionForExtension(desiredDirection);
}
