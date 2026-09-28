using UnityEngine;

public class ShieldAction : MonoBehaviour, IWeapon
{
    [Header("参照設定")]
    [SerializeField] private Transform targetHand;
    [SerializeField] private MonoBehaviour followScript;
    [SerializeField] private Transform player;

    [Header("防御設定")]
    [SerializeField] private float damageReduction = 0.5f;
    [SerializeField] private float guardAngle = 35f;
    [SerializeField] private float guardLerpSpeed = 12f;

    [Header("アクション位置設定")]
    [SerializeField] private Vector3 actionTargetOffset = new Vector3(0.5f, 0.5f, 0f);

    [Header("移動設定")]
    [SerializeField] private float attackSmoothTime = 0.05f;
    [SerializeField] private float initialReturnSmoothTime = 0.18f;
    [SerializeField] private float stopThreshold = 0.05f;
    [SerializeField] private float rotationSmoothTime = 0.05f;

    [Header("音設定")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip guardHitSound;

    private enum State
    {
        Idle,
        Acting,
        Returning
    }

    private State currentState = State.Idle;

    private Vector3 currentVelocity;
    private float currentRotationVelocity;

    // プレイヤーから見た通常時の盾の位置
    private Vector3 restOffset;

    private float fixedZ;

    private bool followWasDisabled;
    private bool isGuarding;
    private Quaternion defaultLocalRotation;

    // 前フレームのプレイヤー位置
    private Vector3 previousPlayerPosition;
    private bool hasPreviousPlayerPosition;

    public bool IsGuarding => isGuarding;
    public float DamageReductionRate => damageReduction;
    // 盾の「アクション中」判定は、ガード中または開始直後のみを対象にする。
    // 解除した瞬間をアクション終了とみなし、元の位置に戻る演出中は再開判定を許可する。
    public bool IsBusy => isGuarding || currentState == State.Acting;

    private void Awake()
    {
        if (targetHand == null)
            targetHand = transform.parent != null ? transform.parent : transform;

        if (player == null)
        {
            Transform currentParent = transform.parent;

            while (currentParent != null && player == null)
            {
                RandomTeleporter targetScript =
                    currentParent.GetComponentInChildren<RandomTeleporter>();

                if (targetScript != null)
                {
                    player = targetScript.transform;

                    Debug.Log(
                        $"【特定成功】RandomTeleporter持ちの {player.name} をPlayerとして認識しました！"
                    );
                }
                else
                {
                    currentParent = currentParent.parent;
                }
            }
        }

        if (followScript == null && targetHand != null)
        {
            followScript =
                targetHand.GetComponent("Follow_WithAnimation") as MonoBehaviour;
        }
    }

    private void Start()
    {
        if (targetHand == null)
            targetHand = transform.parent;

        if (player == null || targetHand == null)
        {
            Debug.LogError(
                "【ShieldAction】PlayerまたはTargetHandが見つかりません。"
            );
            return;
        }

        if (followScript == null)
        {
            followScript =
                targetHand.GetComponent("Follow_WithAnimation") as MonoBehaviour;
        }

        fixedZ = targetHand.position.z;
        defaultLocalRotation = targetHand.localRotation;

        // 通常時の盾とプレイヤーの距離を保存
        restOffset = targetHand.position - player.position;
        restOffset.z = 0f;

        previousPlayerPosition = player.position;
        hasPreviousPlayerPosition = true;
    }

    private void LateUpdate()
    {
        if (player == null || targetHand == null)
            return;

        // プレイヤーが移動した量
        Vector3 playerDelta = Vector3.zero;

        if (hasPreviousPlayerPosition)
        {
            playerDelta = player.position - previousPlayerPosition;
        }

        previousPlayerPosition = player.position;

        switch (currentState)
        {
            case State.Idle:
                // 通常時はFollow_WithAnimationに任せる
                break;

            case State.Acting:
                UpdateActing(playerDelta);
                break;

            case State.Returning:
                UpdateReturning(playerDelta);
                break;
        }
    }

    private void UpdateActing(Vector3 playerDelta)
    {
        // プレイヤーの移動分をそのまま盾へ反映
        targetHand.position += playerDelta;

        // プレイヤーの現在位置を基準にアクション位置を計算
        Vector3 targetPosition =
            player.position + actionTargetOffset;

        targetPosition.z = fixedZ;

        // アクション位置への移動だけをSmoothDamp
        targetHand.position = Vector3.SmoothDamp(
            targetHand.position,
            targetPosition,
            ref currentVelocity,
            attackSmoothTime
        );

        Quaternion guardRotation = Quaternion.Euler(0f, 0f, guardAngle);
        targetHand.localRotation = Quaternion.Slerp(
            targetHand.localRotation,
            guardRotation,
            Time.deltaTime * guardLerpSpeed
        );
    }

    private void UpdateReturning(Vector3 playerDelta)
    {
        // ★重要
        // プレイヤーの移動分はそのまま盾に反映する。
        //
        // これを先に行うことで、
        // 「プレイヤーが動いたせいで復帰先が遠ざかる」
        // という問題を防ぐ。
        targetHand.position += playerDelta;

        // プレイヤーの現在位置を基準に通常位置を計算
        Vector3 returnPosition =
            player.position + restOffset;

        returnPosition.z = fixedZ;

        // 通常位置への残りの距離だけを滑らかに詰める
        targetHand.position = Vector3.SmoothDamp(
            targetHand.position,
            returnPosition,
            ref currentVelocity,
            initialReturnSmoothTime
        );

        targetHand.localRotation = Quaternion.Slerp(
            targetHand.localRotation,
            defaultLocalRotation,
            Time.deltaTime * guardLerpSpeed
        );

        float distance =
            Vector3.Distance(targetHand.position, returnPosition);

        if (distance <= stopThreshold)
        {
            FinishReturn(returnPosition);
        }
    }

    // =========================
    // IWeapon
    // =========================

    public void Attack()
    {
        if (currentState == State.Idle)
        {
            StartAction();
        }
    }

    public void SpecialAbility()
    {
    }

    // =========================
    // アクション開始
    // =========================

    private void StartAction()
    {
        if (followScript == null && targetHand != null)
        {
            followScript =
                targetHand.GetComponent("Follow_WithAnimation") as MonoBehaviour;
        }

        // Follow_WithAnimationを停止
        if (followScript != null && followScript.enabled)
        {
            followScript.enabled = false;
            followWasDisabled = true;
        }

        if (player != null && targetHand != null)
        {
            // 現在の通常位置を保存
            restOffset =
                targetHand.position - player.position;

            restOffset.z = 0f;

            fixedZ = targetHand.position.z;

            previousPlayerPosition = player.position;
            hasPreviousPlayerPosition = true;
        }

        currentVelocity = Vector3.zero;
        currentRotationVelocity = 0f;

        currentState = State.Acting;
    }

    // =========================
    // 回転
    // =========================

    private void RotateTowards(Vector3 target)
    {
        Vector2 direction =
            target - targetHand.position;

        if (direction.sqrMagnitude <= 0.001f)
            return;

        float targetAngle =
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg
            - 90f;

        ApplySmoothRotation(targetAngle);
    }

    private void RotateTowardsZero()
    {
        ApplySmoothRotation(0f);
    }

    private void ApplySmoothRotation(float targetAngle)
    {
        float currentAngle =
            targetHand.eulerAngles.z;

        float newAngle =
            Mathf.SmoothDampAngle(
                currentAngle,
                targetAngle,
                ref currentRotationVelocity,
                rotationSmoothTime
            );

        targetHand.rotation =
            Quaternion.Euler(0f, 0f, newAngle);
    }

    // =========================
    // 復帰完了
    // =========================

    private void FinishReturn(Vector3 position)
    {
        position.z = fixedZ;

        targetHand.position = position;
        targetHand.localRotation = defaultLocalRotation;

        currentState = State.Idle;

        if (followWasDisabled && followScript != null)
        {
            followScript.enabled = true;
            followWasDisabled = false;
        }

        previousPlayerPosition = player.position;
        hasPreviousPlayerPosition = true;

        currentVelocity = Vector3.zero;
        currentRotationVelocity = 0f;
    }

    // =========================
    // 防御状態
    // =========================

    public void SetGuard(bool guard)
    {
        isGuarding = guard;

        if (guard)
        {
            // 防御開始
            if (currentState == State.Idle)
            {
                StartAction();
            }
            // 戻っている途中で再び防御
            else if (currentState == State.Returning)
            {
                currentState = State.Acting;

                currentVelocity = Vector3.zero;
                currentRotationVelocity = 0f;

                previousPlayerPosition = player.position;
            }

            return;
        }

        // 防御解除
        if (currentState != State.Idle)
        {
            currentState = State.Returning;

            currentVelocity = Vector3.zero;
            currentRotationVelocity = 0f;

            previousPlayerPosition = player.position;
            hasPreviousPlayerPosition = true;
        }
    }

    // =========================
    // ダメージ軽減
    // =========================

    public float ApplyDamageReduction(float damage)
    {
        if (!IsGuarding)
            return damage;

        if (audioSource != null && guardHitSound != null)
        {
            audioSource.PlayOneShot(guardHitSound);
        }

        return damage * (1f - damageReduction);
    }

    // =========================
    // 破棄
    // =========================

    private void OnDestroy()
    {
        if (followScript != null)
        {
            followScript.enabled = true;
        }
    }
}