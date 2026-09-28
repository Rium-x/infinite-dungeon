using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class RandomTeleporter : MonoBehaviour
{
    [Header("移動範囲の設定")]
    [SerializeField] private Vector2 minRange = new Vector2(56f, 56f);
    [SerializeField] private Vector2 maxRange = new Vector2(-56f, -56f);

    [Header("判定の設定")]
    [SerializeField] private LayerMask wallLayer = 8;
    [SerializeField] private Vector2 checkSize = new Vector2(0.5f, 0.5f);

    [Header("追加の安全設定")]
    [SerializeField] private bool useEnemyCheck = true;
    [SerializeField] private float enemySafetyRadius = 15f;
    [SerializeField] private LayerMask enemyLayer = 128;

    [Header("演出設定")]
    [SerializeField] private float fadeDuration = 0.7f;

    private List<SpriteRenderer> targetRenderers = new List<SpriteRenderer>();
    private List<EnemyAttack> targetAttacks = new List<EnemyAttack>(); 
    private List<BoxCollider2D> targetColliders = new List<BoxCollider2D>();
    private List<CanvasGroup> targetCanvasGroups = new List<CanvasGroup>();
    private List<MonoBehaviour> targetHealths = new List<MonoBehaviour>();
    
    private Rigidbody2D rb;
    private bool isTeleporting = false;
    private float lastEnabledTime;

    public bool IsTeleporting => isTeleporting;

    /// <summary>Sets the random destination bounds. The vectors are normalized so callers can pass either order.</summary>
    public void SetSpawnRange(Vector2 firstCorner, Vector2 secondCorner)
    {
        minRange = Vector2.Min(firstCorner, secondCorner);
        maxRange = Vector2.Max(firstCorner, secondCorner);
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        // unscaledTime を使うことでタイムスケール停止中の生成にも対応
        lastEnabledTime = Time.unscaledTime;
    }

    /// <summary>
    /// フェード演出を伴うテレポート
    /// useVisualFade を false にすると演出をスキップして即時移動します
    /// </summary>
    public void TeleportWithFade(Action onComplete = null, bool useVisualFade = true)
    {
        if (isTeleporting) return;

        // 生成直後（0.1秒以内）またはフェード不要フラグが立っている場合は即時移動
        if (Time.unscaledTime - lastEnabledTime < 0.1f || !useVisualFade)
        {
            ExecuteTeleportLogic();
            Physics2D.SyncTransforms();
            onComplete?.Invoke();
        }
        else
        {
            StartCoroutine(TeleportSequence(onComplete));
        }
    }

    public void TeleportToRandomPosition()
    {
        ExecuteTeleportLogic();
        Physics2D.SyncTransforms();
    }

    private IEnumerator TeleportSequence(Action onComplete)
    {
        isTeleporting = true;
        
        UpdateTargets();

        StopMovement(true);
        ToggleAttacks(false);
        ToggleHealths(false); 
        ToggleColliders(false);

        // 1. 消えるフェード（タイムスケール無視）
        yield return StartCoroutine(Fade(1f, 0f));

        // 2. 移動実行
        ExecuteTeleportLogic();
        Physics2D.SyncTransforms();

        // 移動完了後のコールバック
        onComplete?.Invoke();

        // 3. 現れるフェード（タイムスケール無視）
        yield return StartCoroutine(Fade(0f, 1f));

        // --- 復活処理 ---
        ToggleColliders(true);
        Physics2D.SyncTransforms();
        
        ToggleHealths(true); 
        ToggleAttacks(true);
        StopMovement(false);
        
        isTeleporting = false;
    }

    private void UpdateTargets()
    {
        targetRenderers.Clear();
        targetAttacks.Clear();
        targetColliders.Clear();
        targetCanvasGroups.Clear();
        targetHealths.Clear();

        Transform characterRoot = transform;
        var h = GetComponentInParent<EnemyHealth>();
        if (h != null) characterRoot = h.transform;
        else
        {
            var ph = GetComponentInParent<PlayerHealth>();
            if (ph != null) characterRoot = ph.transform;
        }

        targetRenderers.AddRange(characterRoot.GetComponentsInChildren<SpriteRenderer>());
        targetAttacks.AddRange(characterRoot.GetComponentsInChildren<EnemyAttack>());
        
        // Health系コンポーネントの取得
        var enemyHealths = characterRoot.GetComponentsInChildren<EnemyHealth>();
        foreach(var eh in enemyHealths) targetHealths.Add(eh);
        var playerHealths = characterRoot.GetComponentsInChildren<PlayerHealth>();
        foreach(var phh in playerHealths) targetHealths.Add(phh);

        targetColliders.AddRange(characterRoot.GetComponentsInChildren<BoxCollider2D>());

        Slider[] sliders = characterRoot.GetComponentsInChildren<Slider>();
        foreach (var s in sliders)
        {
            CanvasGroup cg = s.GetComponent<CanvasGroup>();
            if (cg == null) cg = s.gameObject.AddComponent<CanvasGroup>();
            if (!targetCanvasGroups.Contains(cg)) targetCanvasGroups.Add(cg);
        }
    }

    private void ToggleColliders(bool state)
    {
        foreach (var col in targetColliders)
        {
            if (col != null) col.enabled = state;
        }
    }

    private void ToggleHealths(bool state)
    {
        foreach (var h in targetHealths)
        {
            if (h != null) h.enabled = state;
        }
    }

    private void StopMovement(bool stop)
    {
        if (rb == null) return;
        if (stop)
        {
            rb.linearVelocity = Vector2.zero;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
        }
        else
        {
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
    }

    private void ToggleAttacks(bool state)
    {
        foreach (var attack in targetAttacks)
        {
            if (attack != null) attack.enabled = state; 
        }
    }

    private IEnumerator Fade(float startAlpha, float endAlpha)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            // Time.timeScale = 0 でも動くように unscaledDeltaTime を使用
            elapsed += Time.unscaledDeltaTime;
            float currentAlpha = Mathf.Lerp(startAlpha, endAlpha, elapsed / fadeDuration);
            
            foreach (var sr in targetRenderers)
            {
                if (sr != null) { Color c = sr.color; c.a = currentAlpha; sr.color = c; }
            }
            foreach (var cg in targetCanvasGroups)
            {
                if (cg != null) cg.alpha = currentAlpha;
            }
            yield return null;
        }
        SetFinalAlpha(endAlpha);
    }

    private void SetFinalAlpha(float alpha)
    {
        foreach (var sr in targetRenderers)
        {
            if (sr != null) { Color c = sr.color; c.a = alpha; sr.color = c; }
        }
        foreach (var cg in targetCanvasGroups)
        {
            if (cg != null) cg.alpha = alpha;
        }
    }

    private void ExecuteTeleportLogic()
    {
        Vector2 randomPos;
        bool isInvalid;
        int safetyNet = 0;

        do
        {
            float randomX = UnityEngine.Random.Range(minRange.x, maxRange.x);
            float randomY = UnityEngine.Random.Range(minRange.y, maxRange.y);
            randomPos = new Vector2(randomX, randomY);

            bool isInsideWall = Physics2D.OverlapBox(randomPos, checkSize, 0f, wallLayer);
            bool isEnemyNearby = false;
            if (useEnemyCheck && !isInsideWall) isEnemyNearby = CheckEnemyDanger(randomPos);

            isInvalid = isInsideWall || isEnemyNearby;
            safetyNet++;
            if (safetyNet > 100) break;

        } while (isInvalid);

        if (!isInvalid)
        {
            transform.position = new Vector3(randomPos.x, randomPos.y, transform.position.z);
        }
    }

    private bool CheckEnemyDanger(Vector2 targetPos)
    {
        Collider2D[] enemies = Physics2D.OverlapCircleAll(targetPos, enemySafetyRadius, enemyLayer);
        foreach (var enemy in enemies)
        {
            if (enemy.transform == this.transform || enemy.transform.IsChildOf(this.transform)) continue;
            
            RaycastHit2D hit = Physics2D.Linecast(targetPos, enemy.transform.position, wallLayer);
            if (hit.collider == null) return true; 
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!enabled || isTeleporting) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, enemySafetyRadius);
    }
}
