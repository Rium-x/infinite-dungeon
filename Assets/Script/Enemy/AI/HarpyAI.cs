using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class HarpyAI : MonoBehaviour
{
    enum State { Orbit, PrepareAttack, Attack, Recover }
    // 状態はランタイムで管理し、Inspectorには表示しない
    private State state = State.Orbit;

    [Header("プレイヤー検出")]
    // プレイヤー参照（自動検出される。Inspectorには表示しない）
    private Transform player;
    // プレイヤー検出レンジ
    [SerializeField] private float detectRange = 8f;
    [SerializeField, Min(0.02f)] private float wallCheckInterval = 0.1f;
    // 壁判定用のレイヤーマスク（Inspectorで設定）
    [SerializeField] private LayerMask wallLayerMask;

    [Header("追従と予測")]
    // 軌道の中心（ランタイムで更新されるためInspectorには表示しない）
    private Vector2 center = Vector2.zero;
    // 中心追従速度（プレイヤーへの追従の速さ）
    [SerializeField] private float centerFollowSpeed = 2.0f;
    // プレイヤー予測に使う時間（秒）
    [SerializeField] private float predictionTime = 0.5f;
    // ランタイム - 前フレームのプレイヤー位置（予測用）
    private Vector2 lastPlayerPos;
    // ランタイム - プレイヤー速度（予測用）
    private Vector2 playerVelocity;

    [Header("旋回")]
    // 基本角速度（度/秒）
    [SerializeField] private float baseAngularSpeed = 60f; // deg/s
    // 角度オフセット追従速度（揺らぎの追従速さ）
    [SerializeField] private float angularOffsetFollowSpeed = 3f;
    // 角度オフセットがこの閾値以下であれば新しいターゲットを決める（度）
    [SerializeField] private float angularOffsetThreshold = 1f; // deg
    // 角度オフセットのランダム幅（±deg）
    [SerializeField] private float angularOffsetRange = 8f; // deg
    // ランタイム - 現在の角度オフセット
    private float angularOffset = 0f;
    // ランタイム - 目標の角度オフセット
    private float targetAngularOffset = 0f;
    // 回転方向（+1: 時計回り, -1: 反時計回り）
    [SerializeField] private int rotationDirection = 1; // +1 cw, -1 ccw

    [Header("旋回半径")]
    // 基本半径（軌道の基準距離）
    [SerializeField] private float baseRadius = 3f;
    // 半径追従速度
    [SerializeField] private float radiusFollowSpeed = 3f;
    // ランタイム - 現在の半径（表示不要）
    private float radius;

    [Header("旋回中の移動")]
    // 軌道追従の応答速度（位置に対して）
    [SerializeField] private float orbitFollowSpeed = 6f;
    // 回復時にOrbitWeightを増やす速度
    [SerializeField] private float orbitRecoverSpeed = 2f; // for OrbitWeight
    // ランタイム - 回復時のブレンド重み（0=攻撃位置,1=軌道）
    private float orbitWeight = 1f; // 0..1 used during recover
    // 軌道追従時の最大速度
    [SerializeField] private float orbitMaxSpeed = 4f;
    // 速度変更の加速度（速度を目標に近づける速さ）
    [SerializeField] private float movementAcceleration = 20f; // how quickly velocity approaches target


    [Header("ホバリング")]
    // ホバリングの周波数（Hz）
    [SerializeField] private float hoverFrequency = 2f;
    // ホバリングの振幅（世界単位）
    [SerializeField] private float hoverAmplitude = 0.15f;

    [Header("突進攻撃")]
    // 突進までの待機時間
    [SerializeField] private float attackWaitTime = 0.6f;
    // 攻撃開始確率（プレイヤーが検出範囲にいるとき、毎秒の確率）
    [SerializeField, Range(0f, 10f)] private float attackTriggerChance = 1.5f;
    // ブレーキ（減速）応答速度
    [SerializeField] private float brakeSpeed = 6f;
    // 突進中の最大速度
    [SerializeField] private float attackSpeed = 9f;
    // 突進中の加速度（速度を目標に近づける速さ）
    [SerializeField] private float attackAcceleration = 8f;
    // 突進到達判定の距離
    [SerializeField] private float attackArrivalDistance = 0.2f;
    // 攻撃クールダウン時間
    [SerializeField] private float attackCooldownTime = 2.5f;
    // 攻撃終了時に回転方向を反転する確率
    [SerializeField] [Range(0f,1f)] private float reverseProbability = 0.25f;

    [Header("攻撃後の復帰")]
    // 半径を基準値に戻す速度
    [SerializeField] private float radiusRecoverSpeed = 2f;

    [Header("予測・デバッグ")]
    // プレイヤー予測を使うか
    [SerializeField] private bool usePrediction = true;
    // デバッグ表示を行うか
    [SerializeField] private bool showDebug = true;
    // デバッグのオンオフを切り替えるキー（Inspectorには表示しない）
    private KeyCode debugToggleKey = KeyCode.D;

    // ランタイム - 現在の角度（度）
    private float angle = 0f; // degrees
    // ランタイム - 現在の速度（攻撃時に使用）
    private float currentSpeed = 0f;
    // ランタイム - 攻撃待機タイマー
    private float attackWaitTimer = 0f;
    // ランタイム - 攻撃のクールダウン残り
    private float attackCooldown = 0f;

    // ランタイム - 攻撃時に保存する軌道情報（目視用、表示不要）
    private float attackAngle;
    private float attackRadius;
    private float targetAttackAngle;
    private Vector2 attackTarget;

    // ランタイム - Rigidbody参照（表示不要）
    private Rigidbody2D rb;
    private MoveStats moveStats;
    private WaterTerrainEffect waterEffect;
    private WaterTerrainEffect playerWaterEffect;
    // ランタイム - 目標位置（軌道追従の内部状態）
    private Vector2 desiredPosition;
    
    // デバッグ用：計算された予測位置をGizmosに渡すための変数
    private Vector2 debugPredictedPos;
    // ランタイム - プレイヤーが近くにいて壁越しでないか
    private bool playerNearbyVisible;
    private float nextWallCheckTime;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        moveStats = GetComponentInChildren<MoveStats>(true);
        waterEffect = GetComponentInParent<WaterTerrainEffect>();
        radius = baseRadius;
        targetAngularOffset = Random.Range(-angularOffsetRange, angularOffsetRange);
        if (player == null) FindPlayer();
        lastPlayerPos = player != null ? (Vector2)player.position : Vector2.zero;
        desiredPosition = transform.position;
    }

    void FindPlayer()
    {
        var players = GameObject.FindGameObjectsWithTag("Player");
        if (players.Length > 0)
        {
            player = players[0].transform;
            playerWaterEffect = player.GetComponentInParent<WaterTerrainEffect>();
        }
    }

    void Update()
    {
        // キー入力でデバッグのオンオフ切り替え
        if (Input.GetKeyDown(debugToggleKey))
        {
            showDebug = !showDebug;
        }

        float dt = Time.deltaTime;

        if (player != null)
        {
            if (playerWaterEffect == null)
                playerWaterEffect = player.GetComponentInParent<WaterTerrainEffect>();
            if (waterEffect != null && waterEffect.AvoidsWater && playerWaterEffect != null
                && playerWaterEffect.IsInWaterNow)
                playerNearbyVisible = false;

            playerVelocity = ((Vector2)player.position - lastPlayerPos) / Mathf.Max(dt, 1e-6f);
            lastPlayerPos = player.position;
        }

        // Center follow (with prediction)
        if (player != null)
        {
            Vector2 currentPos2D = transform.position;
            Vector2 playerPos2D = player.position;
            float distanceToPlayer = Vector2.Distance(currentPos2D, playerPos2D);
            Vector2 playerDir = playerPos2D - currentPos2D;
            bool playerInAvoidedWater = waterEffect != null && waterEffect.AvoidsWater
                && playerWaterEffect != null && playerWaterEffect.IsInWaterNow;

            if (distanceToPlayer > detectRange || playerInAvoidedWater)
            {
                playerNearbyVisible = false;
            }
            else if (Time.time >= nextWallCheckTime)
            {
                nextWallCheckTime = Time.time + Mathf.Max(0.02f, wallCheckInterval);
                bool rayHitWall = playerDir.sqrMagnitude > 0f &&
                    Physics2D.Raycast(currentPos2D, playerDir.normalized, playerDir.magnitude, wallLayerMask).collider != null;
                playerNearbyVisible = !rayHitWall;
            }
            debugPredictedPos = (Vector2)player.position + (usePrediction ? playerVelocity * predictionTime : Vector2.zero);

            if (playerNearbyVisible)
            {
                center += (debugPredictedPos - center) * centerFollowSpeed * dt;
            }
            else
            {
                center += ((Vector2)transform.position - center) * centerFollowSpeed * dt;
            }
        }

        // Angular offset follow
        angularOffset += (targetAngularOffset - angularOffset) * angularOffsetFollowSpeed * dt;
        if (Mathf.Abs(targetAngularOffset - angularOffset) < angularOffsetThreshold)
            targetAngularOffset = Random.Range(-angularOffsetRange, angularOffsetRange);

        // Radius follow
        radius += (baseRadius - radius) * radiusFollowSpeed * dt;

        // Angular speed
        float angularSpeed = baseAngularSpeed + angularOffset; // deg/s
        angle += angularSpeed * rotationDirection * dt;

        // compute orbit target position
        float rad = angle * Mathf.Deg2Rad;
        Vector2 orbitTarget = center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;

        // 攻撃中もターゲット位置を現在の円（center）に追従させる
        if (state == State.PrepareAttack || state == State.Attack || state == State.Recover)
        {
            float tarRad = targetAttackAngle * Mathf.Deg2Rad;
            attackTarget = center + new Vector2(Mathf.Cos(tarRad), Mathf.Sin(tarRad)) * attackRadius;
        }

        // State machine
        attackCooldown = Mathf.Max(0f, attackCooldown - dt);

        switch (state)
        {
            case State.Orbit:
                if (playerNearbyVisible)
                {
                    // hover
                    float hover = Mathf.Sin(Time.time * hoverFrequency) * hoverAmplitude;
                    Vector2 targetWithHover = orbitTarget + Vector2.up * hover;
                    // smooth follow
                    desiredPosition = Vector2.Lerp(desiredPosition, targetWithHover, 1f - Mathf.Exp(-orbitFollowSpeed * dt));

                    // look for attack opportunity
                    if (attackCooldown <= 0f)
                    {
                        // ランダムな感覚で攻撃を開始
                        if (Random.value < attackTriggerChance * dt)
                        {
                            StartPrepareAttack();
                        }
                    }
                }
                else
                {
                    // 近くにプレイヤーがいないときは待機
                    desiredPosition = rb.position;
                }
                break;

            case State.PrepareAttack:
                // braking
                currentSpeed += (0f - currentSpeed) * brakeSpeed * dt;
                // hold position (slowly stick to current)
                desiredPosition = Vector2.Lerp(desiredPosition, transform.position, 1f - Mathf.Exp(-brakeSpeed * dt));
                attackWaitTimer += dt;
                if (attackWaitTimer >= attackWaitTime)
                {
                    StartAttack();
                }
                break;

            case State.Attack:
                // look at player
                if (player != null)
                {
                    Vector2 lookDir = ((Vector2)player.position - (Vector2)transform.position).normalized;
                }
                // accelerate
                currentSpeed += (attackSpeed - currentSpeed) * attackAcceleration * dt;
                break;

            case State.Recover:
                // radius recover
                radius += (baseRadius - radius) * radiusRecoverSpeed * dt;

                // orbitWeight increases from 0->1
                orbitWeight += orbitRecoverSpeed * dt;
                orbitWeight = Mathf.Min(1f, orbitWeight);

                // compute blended position
                desiredPosition = Vector2.Lerp(desiredPosition, orbitTarget, orbitWeight);

                if (orbitWeight >= 1f)
                {
                    state = State.Orbit;
                    attackCooldown = attackCooldownTime;
                }
                break;
        }
    }

    void StartPrepareAttack()
    {
        attackAngle = angle;
        attackRadius = radius;

        targetAttackAngle = attackAngle + 180f + Random.Range(-angularOffsetRange, angularOffsetRange);
        
        // 初回の attackTarget 計算 (以降はUpdateで毎フレーム追従する)
        float tarRad = targetAttackAngle * Mathf.Deg2Rad;
        attackTarget = center + new Vector2(Mathf.Cos(tarRad), Mathf.Sin(tarRad)) * attackRadius;

        attackWaitTimer = 0f;
        state = State.PrepareAttack;
    }

    void StartAttack()
    {
        Vector2 toTarget = (attackTarget - (Vector2)transform.position);
        if (toTarget.sqrMagnitude < 0.0001f) toTarget = (attackTarget - center);
        toTarget.Normalize();

        currentSpeed = 0f;
        state = State.Attack;
    }

    void EndAttack()
    {
        angle = targetAttackAngle;
        orbitWeight = 0f;
        state = State.Recover;

        if (Random.value < reverseProbability) rotationDirection *= -1;
    }

    void FixedUpdate()
    {
        float fdt = Time.fixedDeltaTime;
        if (state == State.Attack)
        {
            Vector2 pos = rb.position;
            Vector2 dir = (attackTarget - pos);
            float dist = dir.magnitude;
            if (dist < 0.0001f) dir = (attackTarget - center).normalized;
            else dir /= dist;

            currentSpeed += (attackSpeed - currentSpeed) * attackAcceleration * fdt;
            Vector2 targetVel = dir * currentSpeed;
            ApplyWaterMovement(ref targetVel);
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, targetVel, attackAcceleration * fdt * attackSpeed);

            if (Vector2.Distance(pos, attackTarget) <= attackArrivalDistance)
            {
                EndAttack();
                // 急停止を防ぐため、速度を少し残して減速させる
                rb.linearVelocity *= 0.2f; 
            }
        }
        else
        {
            Vector2 pos = rb.position;
            Vector2 targetVel;
            if (state == State.PrepareAttack)
            {
                targetVel = Vector2.zero;
            }
            else
            {
                Vector2 toDesired = desiredPosition - pos;
                targetVel = toDesired * orbitFollowSpeed;
                targetVel = Vector2.ClampMagnitude(targetVel, orbitMaxSpeed);
            }

            ApplyWaterMovement(ref targetVel);
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, targetVel, movementAcceleration * fdt);
        }
    }

    private void ApplyWaterMovement(ref Vector2 targetVelocity)
    {
        if (moveStats != null)
            targetVelocity *= moveStats.ApplySpeed(1f);
        if (waterEffect != null && !waterEffect.AllowsWaterDirection(targetVelocity))
            targetVelocity = Vector2.zero;
    }

    private void OnDrawGizmos()
    {
        if (!showDebug) return;

        // 1. 軌道中心（水色の球）と現在の軌道円（黄色の円）
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(center, 0.1f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, radius);

        // 2. プレイヤー予測位置（緑色の線と球体）
        if (player != null && usePrediction)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(player.position, debugPredictedPos);
            Gizmos.DrawWireSphere(debugPredictedPos, 0.15f);
        }

        // 3. AIが現在目指している目標地点（青い小さな球）
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(desiredPosition, 0.08f);
        Gizmos.DrawLine(transform.position, desiredPosition);

        // 4. プレイヤー探知範囲（薄いグレーの円）
        Gizmos.color = new Color(1f, 1f, 1f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, detectRange);

        // 5. 攻撃状態のデバッグ（赤い球とターゲットへの線）
        if (state == State.PrepareAttack || state == State.Attack || state == State.Recover)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(attackTarget, 0.12f);
            Gizmos.DrawLine(transform.position, attackTarget);
        }
    }

    private void OnGUI()
    {
        if (!showDebug) return;

        GUIStyle style = new GUIStyle();
        style.fontSize = 14;
        style.normal.textColor = Color.white;

        // 左上に大きめのデバッグウィンドウを表示
        GUILayout.BeginArea(new Rect(10, 10, 350, 300), GUI.skin.box);
        GUILayout.Label($"<b>[ HarpyAI Debug - {gameObject.name} ]</b>", style);
        GUILayout.Space(5);
        GUILayout.Label($"State: <color=yellow>{state}</color>", style);
        GUILayout.Label($"Speed: {rb.linearVelocity.magnitude:F2} / Max: {orbitMaxSpeed} (AttackMax: {attackSpeed})", style);
        GUILayout.Label($"Direction: {(rotationDirection == 1 ? "CW (時計回り)" : "CCW (反時計回り)")}", style);
        
        GUILayout.Space(5);
        GUILayout.Label($"--- Motion Status ---", style);
        GUILayout.Label($"Current Angle: {angle:F1}° (Offset: {angularOffset:F1}° / Target: {targetAngularOffset:F1}°)", style);
        GUILayout.Label($"Radius: {radius:F2} / Base: {baseRadius}", style);
        
        GUILayout.Space(5);
        GUILayout.Label($"--- Timers & AI ---", style);
        GUILayout.Label($"Attack Cooldown: {attackCooldown:F2}s", style);
        if (state == State.PrepareAttack)
        {
            GUILayout.Label($"Prepare Timer: {attackWaitTimer:F2}s / {attackWaitTime}s", style);
        }
        if (state == State.Recover)
        {
            GUILayout.Label($"Recover Orbit Weight: {orbitWeight * 100:F0}%", style);
        }

        GUILayout.Space(5);
        if (player != null)
        {
            float dist = Vector2.Distance(transform.position, player.position);
            GUILayout.Label($"Distance to Player: {dist:F2}m (Detect Range: {detectRange}m)", style);
            GUILayout.Label($"Player Velocity: {playerVelocity.magnitude:F2} m/s", style);
        }
        else
        {
            GUILayout.Label("<color=red>Player: NOT FOUND</color>", style);
        }

        GUILayout.Space(5);
        GUILayout.Label($"<color=orange>[D] キーでデバッグ表示のON/OFF</color>", style);
        GUILayout.EndArea();
    }
}
