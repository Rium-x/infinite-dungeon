using UnityEngine;
using UnityEngine.InputSystem; // 1. Input Systemの導入

// IWeaponインターフェースを実装しつつ、元のロジックを完全維持＋マルチデバイス対応
public class  NewSwordAction: MonoBehaviour, IWeapon
{
    [Header("対象参照")]
    [SerializeField] private Transform targetHand; // ここに「右手」をアサイン
    [SerializeField] private MonoBehaviour followScript; 
    [SerializeField] private Transform player;

    [Header("構え位置")]
    [SerializeField] private Vector3 followOffset = new Vector3(0.6f, 0.4f, 0);

    [Header("攻撃距離・入力")]
    [SerializeField] private float maxAttackDistance = 5.0f;
    [SerializeField] private float sensitivity = 1.2f;
    [SerializeField, Range(0f, 1f)] private float stickDirectionSaveThreshold = 4.0f;

    [Header("攻撃動作")]
    [SerializeField] private float attackSmoothTime = 0.05f;
    [SerializeField] private float attackDuration = 0.2f;
    [SerializeField] private float initialReturnSmoothTime = 0.2f;
    [SerializeField] private float stopThreshold = 0.5f;

    [Header("追従・回転")]
    [SerializeField] private float rotationSmoothTime = 0.05f;
    [SerializeField] private float targetLerpSpeed = 15f; 

    [Header("サウンド")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip attackSound;

    private Vector3 currentVelocity = Vector3.zero;
    private float currentRotationVelocity = 0f;
    private Vector3 rawTargetPos;
    private Vector3 smoothedTarget;
    private float attackTimer = 0f;
    private float fixedZ;

    private enum State { Idle, Attacking, Returning }
    private State currentState = State.Idle;

    public bool IsAttacking => currentState == State.Attacking || currentState == State.Returning;
    public Transform PlayerTransform => player;
    // 互換性のための共通プロパティ
    public bool IsBusy => IsAttacking;

    // 2. Input System用の変数
    private PlayerControls controls;
    private Vector2 moveInput;
    private bool lastInputWasStick;
    private Vector2 previousMousePosition;
    private bool hasMousePositionInitialized;
    private Vector2 lastStickDirection = Vector2.right;
    private float lastStickDistance;

    private void Awake()
    {
        controls = new PlayerControls();
        // ★ここを Aim から Move に変更
        controls.Player.Move.performed += ctx =>
        {
            if (BSPDungeonMaster.IsPlayerInputLocked)
            {
                moveInput = Vector2.zero;
                return;
            }

            moveInput = ctx.ReadValue<Vector2>();
            if (moveInput.sqrMagnitude > stickDirectionSaveThreshold * stickDirectionSaveThreshold)
            {
                lastStickDirection = moveInput.normalized;
                lastStickDistance = Mathf.Clamp01(moveInput.magnitude) * maxAttackDistance;
                lastInputWasStick = true;
            }
        };
        controls.Player.Move.canceled += ctx => moveInput = Vector2.zero;
    }

    private void OnEnable() => controls.Player.Enable();
    private void OnDisable() => controls.Player.Disable();

    void Start()
    {
        if (player == null)
        {
            Transform currentParent = transform.parent;
            while (currentParent != null && player == null)
            {
                RandomTeleporter targetScript = currentParent.GetComponentInChildren<RandomTeleporter>();
                if (targetScript != null)
                {
                    player = targetScript.transform;
                    Debug.Log($"【特定成功】RandomTeleporter持ちの {player.name} をPlayerとして認識しました！");
                }
                else
                {
                    currentParent = currentParent.parent;
                }
            }
        }

        if (targetHand == null) targetHand = transform.parent;
        if (followScript == null && targetHand != null) 
            followScript = targetHand.GetComponent<MonoBehaviour>();

        if (player != null)
        {
            fixedZ = targetHand.position.z;
            smoothedTarget = player.position + followOffset;
        }
        else
        {
            Debug.LogError("【緊急事態】RandomTeleporterを持った親戚が見つかりません！");
        }
    }

    // --- IWeapon 実装 ---
    public void Attack()
    {
        if (BSPDungeonMaster.IsPlayerInputLocked) return;

        if (currentState == State.Idle)
        {
            StartAttack();
        }
    }

    public void SpecialAbility() { }

    public void CancelAction()
    {
        if (targetHand == null)
        {
            currentState = State.Idle;
            return;
        }

        currentState = State.Idle;
        attackTimer = 0f;
        currentVelocity = Vector3.zero;
        currentRotationVelocity = 0f;

        if (player != null)
        {
            Vector3 returnPos = player.position + followOffset;
            returnPos.z = fixedZ;
            targetHand.position = returnPos;
        }

        targetHand.rotation = Quaternion.Euler(0f, 0f, 0f);

        if (followScript != null)
        {
            followScript.enabled = true;
        }
    }

    void Update()
    {
        if (currentState == State.Idle) return;

        switch (currentState)
        {
            case State.Attacking:
                attackTimer += Time.deltaTime;

                UpdateRawTarget();
                smoothedTarget = Vector3.Lerp(smoothedTarget, rawTargetPos, Time.deltaTime * targetLerpSpeed);
                RotateSmoothly(smoothedTarget);

                targetHand.position = Vector3.SmoothDamp(targetHand.position, smoothedTarget, ref currentVelocity, attackSmoothTime);

                if (attackTimer >= attackDuration)
                    currentState = State.Returning;
                break;

            case State.Returning:
                Vector3 returnDest = player.position + followOffset;
                smoothedTarget = Vector3.Lerp(smoothedTarget, returnDest, Time.deltaTime * targetLerpSpeed);
                RotateSmoothlyToZero();

                float dist = Vector3.Distance(targetHand.position, returnDest);
                float dynamicSmooth = Mathf.Lerp(0.001f, initialReturnSmoothTime, Mathf.Clamp01(dist / 2.0f));

                targetHand.position = Vector3.SmoothDamp(targetHand.position, returnDest, ref currentVelocity, dynamicSmooth);

                if (dist < stopThreshold)
                    FinishReturn(returnDest);
                break;
        }
    }

    void StartAttack()
    {
        if (followScript != null) followScript.enabled = false;
        if (audioSource != null && attackSound != null) audioSource.PlayOneShot(attackSound);

        currentState = State.Attacking;
        attackTimer = 0f;
        currentVelocity = Vector3.zero;
        currentRotationVelocity = 0f;
        smoothedTarget = targetHand.position; 
    }

    void UpdateRawTarget()
    {
        Vector3 direction;
        float dist = maxAttackDistance;

        Vector2 currentMousePosition = Vector2.zero;
        bool mouseMoved = false;

        if (Mouse.current != null)
        {
            currentMousePosition = Mouse.current.position.ReadValue();

            if (hasMousePositionInitialized)
            {
                mouseMoved = (currentMousePosition - previousMousePosition).sqrMagnitude > 0.01f;
            }
            else
            {
                hasMousePositionInitialized = true;
            }

            previousMousePosition = currentMousePosition;
        }

        if (moveInput.sqrMagnitude > stickDirectionSaveThreshold * stickDirectionSaveThreshold)
        {
            lastInputWasStick = true;
            lastStickDirection = moveInput.normalized;
            lastStickDistance = Mathf.Clamp01(moveInput.magnitude) * maxAttackDistance;
            direction = new Vector3(lastStickDirection.x, lastStickDirection.y, 0f);
            dist = lastStickDistance;
        }
        else if (mouseMoved)
        {
            lastInputWasStick = false;
            Vector3 mousePos = new Vector3(currentMousePosition.x, currentMousePosition.y, 10f);
            Vector3 worldMousePos = Camera.main != null
                ? Camera.main.ScreenToWorldPoint(mousePos)
                : player.position + Vector3.right;

            direction = (worldMousePos - player.position).normalized;
            dist = Mathf.Min(Vector3.Distance(player.position, worldMousePos) * sensitivity, maxAttackDistance);
        }
        else if (lastInputWasStick)
        {
            if (lastStickDirection.sqrMagnitude < 0.001f)
            {
                lastStickDirection = Vector2.right;
            }

            direction = new Vector3(lastStickDirection.x, lastStickDirection.y, 0f);
            dist = maxAttackDistance;
        }
        else
        {
            Vector3 mousePos = new Vector3(currentMousePosition.x, currentMousePosition.y, 10f);
            Vector3 worldMousePos = Camera.main != null
                ? Camera.main.ScreenToWorldPoint(mousePos)
                : player.position + Vector3.right;

            direction = (worldMousePos - player.position).normalized;
            dist = Mathf.Min(Vector3.Distance(player.position, worldMousePos) * sensitivity, maxAttackDistance);
        }

        rawTargetPos = player.position + (direction * dist);
        rawTargetPos.z = fixedZ;
    }
    
        void RotateSmoothly(Vector3 target)
    {
        Vector2 dir = target - targetHand.position;
        if (dir.sqrMagnitude > 0.001f)
        {
            float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            ApplySmoothRotation(targetAngle);
        }
    }

    void RotateSmoothlyToZero() => ApplySmoothRotation(0f);

    void ApplySmoothRotation(float targetAngle)
    {
        float currentAngle = targetHand.eulerAngles.z;
        float newAngle = Mathf.SmoothDampAngle(currentAngle, targetAngle, ref currentRotationVelocity, rotationSmoothTime);
        targetHand.rotation = Quaternion.Euler(0, 0, newAngle);
    }

    void FinishReturn(Vector3 snapPos)
    {
        snapPos.z = fixedZ;
        targetHand.position = snapPos;
        targetHand.rotation = Quaternion.Euler(0, 0, 0);

        currentState = State.Idle;
        if (followScript != null) followScript.enabled = true;
    }

    private void OnDestroy()
    {
        if (followScript != null)
        {
            followScript.enabled = true;
            if (targetHand != null)
            {
                targetHand.rotation = Quaternion.Euler(0, 0, 0);
            }
            Debug.Log($"【装備解除】{gameObject.name} が削除されたため、{followScript.name} を再有効化しました。");
        }
    }
}
