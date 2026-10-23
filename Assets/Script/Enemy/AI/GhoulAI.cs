using System.Collections;
using UnityEngine;

/// <summary>
/// Companion behavior for EnemyAI: after spending long enough in Wander, the ghoul burrows
/// underground, then reappears when it has a wall-free line to a nearby player.
/// </summary>
[RequireComponent(typeof(EnemyAI))]
public sealed class GhoulAI : MonoBehaviour
{
    private enum Phase { WaitingForSearch, PreparingBurrow, Burrowing, Buried, Emerging }

    [Header("探索・接近")]
    [SerializeField, Min(0f), Tooltip("スポーン後、最初にWanderへ入ってから初めて潜伏するまでの時間。")]
    private float firstBurrowDelay = 10f;
    [SerializeField, Min(0f), Tooltip("一度潜伏して出現した後、次に潜伏するまでのWander時間。")]
    private float repeatBurrowDelay = 25f;
    [SerializeField, Range(0f, 100f), Tooltip("潜伏時間のランダムな増減幅。20なら基準時間の80〜120%から抽選します。")]
    private float burrowDelayRandomVariationPercent = 20f;
    [SerializeField, Min(0.1f)] private float emergenceRange = 5f;
    [SerializeField] private LayerMask wallMask;
    [SerializeField, Min(0.02f)] private float playerCheckInterval = 0.15f;

    [Header("デバッグログ")]
    [SerializeField] private bool showDebugLogs = true;

    [Header("見た目とヒットボックス")]
    [Tooltip("未指定ならこのオブジェクト以下の全SpriteRendererを使用します。")]
    [SerializeField] private SpriteRenderer[] visuals;
    [Tooltip("Animator以下のSpriteRendererはアニメーション表示用として自動取得します。")]
    private SpriteRenderer[] animationVisuals;
    [Tooltip("ルート外にあるものも含め、敵の攻撃判定・被弾判定Colliderをすべて登録してください。")]
    [SerializeField] private Collider2D[] hitboxes;

    [Header("アニメーション")]
    [Tooltip("アニメーション専用の子オブジェクトに付けたAnimatorを指定します。Animator以下のSpriteRendererだけをアニメーション中に表示します。")]
    [SerializeField] private Animator animator;
    [Tooltip("地面を掘り始めるアニメーションのAnimator Controllerです。最後まで1回再生します。")]
    [SerializeField] private RuntimeAnimatorController digStartAnimation;
    [Tooltip("地面を掘っている間のループアニメーションです。1サイクルを最後まで再生し、指定回数繰り返します。")]
    [SerializeField] private RuntimeAnimatorController diggingLoopAnimation;
    [Tooltip("地面に潜るアニメーションのAnimator Controllerです。最後まで1回再生します。")]
    [SerializeField] private RuntimeAnimatorController burrowAnimation;
    [Tooltip("地面から出現するアニメーションのAnimator Controllerです。最後まで1回再生します。")]
    [SerializeField] private RuntimeAnimatorController emergeAnimation;
    [Tooltip("掘るループアニメーションを何サイクル再生してから潜るかを指定します。")]
    [SerializeField, Min(1)] private int diggingLoopCycles = 2;

    private EnemyAI enemyAI;
    private Transform player;
    private Phase phase;
    private float searchTimer;
    private float activeBurrowDelay;
    private bool searchCycleStarted;
    private bool hasBurrowedOnce;
    private bool playerWasCloseAtBurrowStart;
    private float nextPlayerCheckTime;
    private Coroutine burrowRoutine;
    private string lastLoggedEnemyState;
    private int lastLoggedSearchProgress;

    private void Awake()
    {
        enemyAI = GetComponent<EnemyAI>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (visuals == null || visuals.Length == 0)
        {
            SpriteRenderer[] allVisuals = GetComponentsInChildren<SpriteRenderer>(true);
            var bodyVisualList = new System.Collections.Generic.List<SpriteRenderer>(allVisuals.Length);
            foreach (SpriteRenderer sprite in allVisuals)
            {
                if (animator != null &&
                    (sprite.transform == animator.transform || sprite.transform.IsChildOf(animator.transform)))
                    continue;
                bodyVisualList.Add(sprite);
            }
            visuals = bodyVisualList.ToArray();
        }
        if (animator != null)
            animationVisuals = animator.GetComponentsInChildren<SpriteRenderer>(true);
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycastLayer >= 0 && animationVisuals != null)
        {
            foreach (SpriteRenderer sprite in animationVisuals)
            {
                if (sprite == null) continue;
                sprite.gameObject.layer = ignoreRaycastLayer;
                sprite.SetPropertyBlock(null);
            }
        }
        if (animationVisuals != null && animationVisuals.Length > 0 && visuals != null && visuals.Length > 0)
        {
            int sortingOrder = visuals[0] != null ? visuals[0].sortingOrder : 0;
            int sortingLayerId = visuals[0] != null ? visuals[0].sortingLayerID : 0;
            foreach (SpriteRenderer sprite in visuals)
            {
                if (sprite == null) continue;
                if (sprite.sortingLayerID == sortingLayerId)
                    sortingOrder = Mathf.Max(sortingOrder, sprite.sortingOrder);
            }
            foreach (SpriteRenderer sprite in animationVisuals)
            {
                if (sprite == null) continue;
                sprite.sortingLayerID = sortingLayerId;
                sprite.sortingOrder = sortingOrder + 1;
            }
        }
        if (hitboxes == null || hitboxes.Length == 0)
            hitboxes = GetComponentsInChildren<Collider2D>(true);
        SetVisible(true);
        SetAnimationVisible(false);
        SetHitboxesEnabled(true);
    }

    private void Update()
    {
        if (Time.time >= nextPlayerCheckTime)
        {
            nextPlayerCheckTime = Time.time + playerCheckInterval;
            FindPlayerIfNeeded();
        }

        LogEnemyStateIfChanged();

        if (phase == Phase.WaitingForSearch)
        {
            if (!enemyAI.IsWandering)
            {
                if (showDebugLogs && (searchCycleStarted || searchTimer > 0f))
                    Debug.Log($"[GhoulAI] {name}: Wander以外の状態 ({GetEnemyStateForLog()}) に移ったため潜伏タイマーをリセット (経過 {searchTimer:F1}/{activeBurrowDelay:F1} 秒)", this);
                searchTimer = 0f;
                searchCycleStarted = false;
                return;
            }

            if (!searchCycleStarted)
            {
                float baseDelay = hasBurrowedOnce ? repeatBurrowDelay : firstBurrowDelay;
                float variation = Mathf.Clamp01(burrowDelayRandomVariationPercent / 100f);
                activeBurrowDelay = baseDelay * Random.Range(1f - variation, 1f + variation);
                searchCycleStarted = true;
                lastLoggedSearchProgress = 0;
                if (showDebugLogs)
                    Debug.Log($"[GhoulAI] {name}: Wander開始を検知。{(hasBurrowedOnce ? "再潜伏" : "初回潜伏")}まで {activeBurrowDelay:F1} 秒の計測開始 (基準 {baseDelay:F1} 秒、変動幅 ±{burrowDelayRandomVariationPercent:F0}%)", this);
            }

            if (searchCycleStarted)
            {
                searchTimer += Time.deltaTime;
                int progress = Mathf.FloorToInt(searchTimer / 5f) * 5;
                if (showDebugLogs && progress > lastLoggedSearchProgress)
                {
                    lastLoggedSearchProgress = progress;
                    Debug.Log($"[GhoulAI] {name}: Wander継続時間 {searchTimer:F1}/{activeBurrowDelay:F1} 秒", this);
                }
            }

            if (searchTimer >= activeBurrowDelay)
                BeginBurrow();
            return;
        }

        bool playerClose = IsPlayerCloseAndUnblocked();
        if ((phase == Phase.PreparingBurrow || phase == Phase.Burrowing)
            && playerClose && !playerWasCloseAtBurrowStart)
        {
            InterruptAndChase();
        }
        else if (phase == Phase.PreparingBurrow || phase == Phase.Burrowing)
        {
            playerWasCloseAtBurrowStart = playerClose;
        }
        else if (phase == Phase.Buried && playerClose)
        {
            BeginEmerge();
        }
    }

    private void BeginBurrow()
    {
        searchTimer = 0f;
        searchCycleStarted = false;
        playerWasCloseAtBurrowStart = IsPlayerCloseAndUnblocked();
        phase = Phase.PreparingBurrow;
        enemyAI.SetMovementPausedByCompanion(true);
        SetAnimationMode(true);
        if (showDebugLogs)
            Debug.Log($"[GhoulAI] {name}: 潜伏アニメーション開始。通常パーツを隠して専用アニメーションを表示。開始時プレイヤー接近={playerWasCloseAtBurrowStart}", this);
        burrowRoutine = StartCoroutine(BurrowSequence());
    }

    private IEnumerator BurrowSequence()
    {
        yield return PlayAnimationToEnd(digStartAnimation, Phase.PreparingBurrow, "地面を掘り始める");
        if (phase != Phase.PreparingBurrow) yield break;

        phase = Phase.Burrowing;
        yield return PlayAnimationToEnd(diggingLoopAnimation, Phase.Burrowing, $"地面を掘るループ {diggingLoopCycles}回", diggingLoopCycles, true);
        if (phase != Phase.Burrowing) yield break;

        yield return PlayAnimationToEnd(burrowAnimation, Phase.Burrowing, "地面に潜る");
        if (phase != Phase.Burrowing) yield break;

        SetAnimationVisible(false);
        SetVisible(false);
        SetHitboxesEnabled(false);
        phase = Phase.Buried;
        hasBurrowedOnce = true;
        burrowRoutine = null;
        LogObjectState("潜伏完了");
    }

    private void BeginEmerge()
    {
        phase = Phase.Emerging;
        SetAnimationMode(true);
        SetHitboxesEnabled(true);
        LogObjectState("出現開始");
        burrowRoutine = StartCoroutine(EmergeSequence());
    }

    private IEnumerator EmergeSequence()
    {
        yield return PlayAnimationToEnd(emergeAnimation, Phase.Emerging, "地面から出現する");
        if (phase != Phase.Emerging) yield break;
        SetAnimationVisible(false);
        SetVisible(true);
        phase = Phase.WaitingForSearch;
        enemyAI.SetMovementPausedByCompanion(false);
        burrowRoutine = null;
        if (showDebugLogs) Debug.Log($"[GhoulAI] {name}: 出現完了。探索待機に戻ります", this);
    }

    private void InterruptAndChase()
    {
        if (burrowRoutine != null) StopCoroutine(burrowRoutine);
        burrowRoutine = null;
        SetAnimationVisible(false);
        SetVisible(true);
        SetHitboxesEnabled(true);
        phase = Phase.WaitingForSearch;
        searchTimer = 0f;
        enemyAI.SetMovementPausedByCompanion(false);
        if (showDebugLogs) Debug.Log($"[GhoulAI] {name}: プレイヤー接近で潜伏を中断。移動を再開", this);
    }

    private string GetEnemyStateForLog()
    {
        if (enemyAI.IsWandering) return "Wander";
        if (enemyAI.IsChasing) return "Chase";
        if (enemyAI.IsSearching) return "Search";
        if (enemyAI.IsInvestigating) return "Investigate";
        if (enemyAI.IsFleeing) return "Flee";
        return "Unknown";
    }

    private void LogEnemyStateIfChanged()
    {
        if (!showDebugLogs) return;
        string currentState = GetEnemyStateForLog();
        if (currentState == lastLoggedEnemyState) return;
        lastLoggedEnemyState = currentState;
        Debug.Log($"[GhoulAI] {name}: EnemyAI状態={currentState}", this);
    }

    private void LogObjectState(string message)
    {
        if (!showDebugLogs) return;
        int enabledVisuals = 0;
        int totalVisuals = 0;
        if (visuals != null)
        {
            foreach (SpriteRenderer sprite in visuals)
            {
                if (sprite == null) continue;
                totalVisuals++;
                if (sprite.enabled) enabledVisuals++;
            }
        }

        int enabledHitboxes = 0;
        int totalHitboxes = 0;
        if (hitboxes != null)
        {
            foreach (Collider2D hitbox in hitboxes)
            {
                if (hitbox == null) continue;
                totalHitboxes++;
                if (hitbox.enabled) enabledHitboxes++;
            }
        }

        int enabledAnimationVisuals = 0;
        int totalAnimationVisuals = 0;
        if (animationVisuals != null)
        {
            foreach (SpriteRenderer sprite in animationVisuals)
            {
                if (sprite == null) continue;
                totalAnimationVisuals++;
                if (sprite.enabled) enabledAnimationVisuals++;
            }
        }

        Debug.Log($"[GhoulAI] {name}: {message}。通常SpriteRenderer={enabledVisuals}/{totalVisuals}有効、アニメSpriteRenderer={enabledAnimationVisuals}/{totalAnimationVisuals}有効、Collider2D={enabledHitboxes}/{totalHitboxes}有効", this);
    }

    private void FindPlayerIfNeeded()
    {
        if (player != null && player.gameObject.activeInHierarchy) return;
        GameObject[] candidates = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject candidate in candidates)
        {
            if (candidate.GetComponent<RandomTeleporter>() == null) continue;
            player = candidate.transform;
            return;
        }
        player = null;
    }

    private bool IsPlayerCloseAndUnblocked()
    {
        if (player == null || !player.gameObject.activeInHierarchy) return false;
        Vector2 origin = transform.position;
        Vector2 target = player.position;
        Vector2 delta = target - origin;
        float distance = delta.magnitude;
        if (distance > emergenceRange) return false;
        if (wallMask.value == 0) return true;
        return !Physics2D.Linecast(origin, target, wallMask).collider;
    }

    private void SetVisible(bool visible)
    {
        if (visuals == null) return;
        foreach (SpriteRenderer sprite in visuals)
            if (sprite != null) sprite.enabled = visible;
    }

    private void SetHitboxesEnabled(bool enabled)
    {
        if (hitboxes == null) return;
        foreach (Collider2D hitbox in hitboxes)
            if (hitbox != null) hitbox.enabled = enabled;
    }

    private IEnumerator PlayAnimationToEnd(
        RuntimeAnimatorController controller,
        Phase expectedPhase,
        string action,
        int cycles = 1,
        bool loop = false)
    {
        if (animator == null)
        {
            Debug.LogError($"[GhoulAI] {name}: Animatorが見つかりません。アニメーション専用オブジェクトにAnimatorを設定してください。", this);
            yield break;
        }
        if (controller == null)
        {
            Debug.LogError($"[GhoulAI] {name}: {action}用Animator Controllerが未設定です。GhoulAIのInspectorを確認してください。", this);
            yield break;
        }
        if (animationVisuals == null || animationVisuals.Length == 0)
        {
            Debug.LogError($"[GhoulAI] {name}: Animator配下にSpriteRendererがありません。アニメーション用SpriteRendererをAnimatorと同じオブジェクトか子に配置してください。", this);
            yield break;
        }

        SetAnimationVisible(false);
        animator.enabled = true;
        animator.speed = 1f;
        animator.runtimeAnimatorController = controller;
        animator.Rebind();
        animator.Play("Base Layer.New Animation", 0, 0f);
        animator.Update(0f);
        SetAnimationVisible(true);
        AnimatorStateInfo initialState = animator.GetCurrentAnimatorStateInfo(0);
        if (!initialState.IsName("Base Layer.New Animation"))
            Debug.LogError($"[GhoulAI] {name}: Controller '{controller.name}' の 'Base Layer.New Animation' Stateを再生できませんでした。Animator ControllerのState名を確認してください。", this);

        Sprite firstFrame = null;
        foreach (SpriteRenderer spriteRenderer in animationVisuals)
        {
            if (spriteRenderer == null) continue;
            firstFrame = spriteRenderer.sprite;
            if (firstFrame != null) break;
        }
        if (firstFrame == null)
            Debug.LogError($"[GhoulAI] {name}: アニメーション開始後もSpriteRendererにSpriteが設定されていません。ClipのSpriteキーとAnimator上のオブジェクト構成を確認してください。", this);

        if (showDebugLogs)
        {
            Debug.Log($"[GhoulAI] {name}: {action}アニメーション再生開始。Controller={controller.name}, StateHash={initialState.fullPathHash}, 長さ={initialState.length:F2}秒, Renderer数={animationVisuals.Length}, Sprite={firstFrame?.name ?? "(なし)"}", this);
        }

        while (phase == expectedPhase && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < cycles)
            yield return null;

        if (phase != expectedPhase) yield break;
        if (loop)
        {
            animator.speed = 0f;
            animator.Play("Base Layer.New Animation", 0, 0.999f);
            animator.Update(0f);
        }
        if (showDebugLogs)
            Debug.Log($"[GhoulAI] {name}: {action}アニメーションを{cycles}サイクル最後まで再生しました", this);
    }

    private void SetAnimationMode(bool animationMode)
    {
        SetVisible(!animationMode);
        SetAnimationVisible(animationMode);
    }

    private void SetAnimationVisible(bool visible)
    {
        if (animationVisuals == null) return;
        foreach (SpriteRenderer sprite in animationVisuals)
            if (sprite != null) sprite.enabled = visible;
    }

    private void OnDisable()
    {
        if (burrowRoutine != null) StopCoroutine(burrowRoutine);
        burrowRoutine = null;
        SetAnimationVisible(false);
        SetVisible(true);
        if (enemyAI != null) enemyAI.SetMovementPausedByCompanion(false);
    }
}
