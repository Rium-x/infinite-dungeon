using UnityEngine;
using UnityEngine.InputSystem;

public class PL_movement : MonoBehaviour
{
    public float acceleration = 10f;
    public float deceleration = 15f;
    public float maxSpeed = 15f;

    [Tooltip("マウス操作時、最大速度に達する画面中心からの距離（ピクセル単位）")]
    public float speedRange = 300f;

    private Rigidbody2D rb;
    private Vector2 currentVelocity;
    private MoveStats moveStats;
    private CharacterSorting characterSorting;
    private PlayerMP playerMP;

    // Input System用
    private PlayerControls controls;
    private Vector2 gamepadMoveInput;
    private Vector2 mousePositionInput;
    private bool isMouseClicking;

    private void Awake()
    {
        controls = new PlayerControls();

        // Xboxスティックの入力を監視
        controls.Player.Move.performed += ctx =>
        {
            gamepadMoveInput = BSPDungeonMaster.IsPlayerInputLocked
                ? Vector2.zero
                : ctx.ReadValue<Vector2>();
        };
        controls.Player.Move.canceled += ctx => gamepadMoveInput = Vector2.zero;

        // マウス位置の入力を監視
        controls.Player.MousePos.performed += ctx => mousePositionInput = ctx.ReadValue<Vector2>();

        // マウスクリックの入力を監視
        controls.Player.MouseClick.performed += ctx => isMouseClicking = true;
        controls.Player.MouseClick.canceled += ctx => isMouseClicking = false;
    }

    private void OnEnable() => controls.Player.Enable();
    private void OnDisable() => controls.Player.Disable();

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        moveStats = GetComponentInParent<MoveStats>();
        playerMP = GetComponentInParent<PlayerMP>();

        characterSorting = GetComponent<CharacterSorting>();
        if (characterSorting == null)
        {
            characterSorting = gameObject.AddComponent<CharacterSorting>();
        }
    }

    void Update()
    {
        if (BSPDungeonMaster.IsPlayerInputLocked)
        {
            currentVelocity = Vector2.zero;
            return;
        }

        // マウスを画面内に閉じ込める（PC操作用）
        Cursor.lockState = CursorLockMode.Confined;

        // タッチ中の指位置をマウス位置として扱い、既存の移動操作を使う。
        bool isTouching = false;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
        {
            mousePositionInput = Touchscreen.current.primaryTouch.position.ReadValue();
            isTouching = true;
        }

        Vector2 targetDirection = Vector2.zero;
        float targetSpeedPercentage = 0f;
        bool isMoving = false;

        // -------------------------------------------------------------
        // 1. Xboxコントローラーの処理（スティックが倒されているか）
        // -------------------------------------------------------------
        if (gamepadMoveInput.sqrMagnitude > 0.01f)
        {
            targetDirection = gamepadMoveInput.normalized;
            targetSpeedPercentage = Mathf.Clamp01(gamepadMoveInput.magnitude);
            isMoving = true;
        }
        // -------------------------------------------------------------
        // 2. マウスの処理（左クリックされているか）※コントローラー未操作時
        // -------------------------------------------------------------
        else if (isMouseClicking || isTouching)
        {
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 offset = mousePositionInput - screenCenter;
            float distance = offset.magnitude;

            if (distance > 0.1f)
            {
                targetDirection = offset.normalized;
                targetSpeedPercentage = Mathf.Clamp01(distance / speedRange);
                isMoving = true;
            }
        }

        // -------------------------------------------------------------
        // 3. 移動速度の計算と反映
        // -------------------------------------------------------------
        if (playerMP != null) playerMP.TickWalking(isMoving);
        if (isMoving)
        {
            float finalSpeed = maxSpeed;

            // Buffによる速度補正
            if (moveStats != null)
            {
                finalSpeed = moveStats.ApplySpeed(finalSpeed);
            }
            if (playerMP != null) finalSpeed *= playerMP.SpeedMultiplier;

            // 目標速度を計算（入力の強さ × 最終速度）
            float targetSpeed = targetSpeedPercentage * finalSpeed;

            // 加速（滑らかに目標速度へ近づける）
            currentVelocity = Vector2.Lerp(
                currentVelocity,
                targetDirection * targetSpeed,
                acceleration * Time.deltaTime
            );
        }
        else
        {
            // 入力がない時は減速
            currentVelocity = Vector2.Lerp(
                currentVelocity,
                Vector2.zero,
                deceleration * Time.deltaTime
            );
        }
    }

    void FixedUpdate()
    {
        rb.linearVelocity = BSPDungeonMaster.IsPlayerInputLocked
            ? Vector2.zero
            : currentVelocity;
    }
}
