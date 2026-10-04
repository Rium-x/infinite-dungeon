
using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public class EnemyHealth : MonoBehaviour
{
    [SerializeField] private float maxHealth = 30f;
    [SerializeField] private float currentHealth;

    [Header("自動回復")]
    [SerializeField] private bool enableAutoHeal = true;
    [SerializeField] private float healStartDelay = 5f;
    [SerializeField] private float healAmount = 3f;
    [SerializeField] private float healInterval = 2f;
    private Coroutine healCoroutine;

    [Header("攻略率")]
    [SerializeField] private int progressPoint = 1;

    [Header("サウンド")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private List<AudioClip> damageSounds = new List<AudioClip>();
    [HideInInspector, FormerlySerializedAs("damageSound")]
    [SerializeField] private AudioClip legacyDamageSound;
    [HideInInspector, FormerlySerializedAs("deathSound")]
    [SerializeField] private AudioClip legacyDeathSound;

    [Header("ダメージ演出")]
    [SerializeField] private Color damageColor = Color.red;
    [SerializeField] private float flashDuration = 0.2f;

    [Header("フェード設定")]
    [SerializeField] private float fadeInDuration = 1.0f;
    [SerializeField] private float fadeOutDuration = 1.0f;

    [Header("ドロップ設定")]
    public GameObject itemPrefab;
    [Tooltip("通常アイテムの種類ごとの倍率補正。0=標準、-100=出ない、+100=2倍です。未登録の種類は標準です。")]
    public List<MonsterItemTypeAdjustment> normalItemTypeAdjustments = new List<MonsterItemTypeAdjustment>();
    [Range(0f, 100f), Tooltip("最終的なドロップ個数のうち通常ドロップにする割合(%)です。残りがモンスター固有ドロップになります。片方の候補がない場合は、もう片方から補います。")]
    public float normalDropRatioPercent = 50f;

    [Header("全体のドロップ個数")]
    [Min(0), Tooltip("通常ドロップとモンスター固有ドロップを合算した、1体あたりのドロップ個数の上限です。")]
    public int maxTotalDropCount = 20;
    [Tooltip("横軸は通常・固有を合算したドロップ個数(0～上限)、縦軸は確率の重みです。整数個数で評価し、合計が100%になるよう正規化して抽選します。候補アイテムが1つも抽選されなかった場合はドロップしません。")]
    public AnimationCurve totalDropCountProbability = new AnimationCurve(
        new Keyframe(0f, 5f), new Keyframe(1f, 25f), new Keyframe(2f, 30f),
        new Keyframe(3f, 20f), new Keyframe(4f, 10f), new Keyframe(5f, 5f), new Keyframe(6f, 0f),
        new Keyframe(20f, 0f));

    [Header("モンスター固有ドロップ")]
    [Tooltip("モンスター固有アイテム。レアリティ(%) + 各行のモンスター補正(-100～100)を確率として個別に抽選します。")]
    public List<DropItemSetting> dropList = new List<DropItemSetting>();

    private SpriteRenderer[] allRenderers;
    private Collider2D[] allColliders;
    private bool isFlashing = false;
    private bool isDying = false;
    private bool killedByPlayer = false;
    private CharacterSorting characterSorting;

    public event Action OnDestroyed;

    void Start()
    {
        currentHealth = maxHealth;
        allRenderers = GetComponentsInChildren<SpriteRenderer>();
        allColliders = GetComponentsInChildren<Collider2D>();

        characterSorting = GetComponent<CharacterSorting>();
        if (characterSorting == null)
        {
            characterSorting = gameObject.AddComponent<CharacterSorting>();
        }

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        StartCoroutine(FadeRoutine(0, 1, fadeInDuration));
    }

    public void TakeDamage(float damage, bool fromPlayer)
    {
        if (!this.enabled || isDying) return;

        killedByPlayer = fromPlayer;
        currentHealth -= damage;

        // ダメージを受けたら回復タイマーをリセット
        if (enableAutoHeal)
        {
            if (healCoroutine != null)
                StopCoroutine(healCoroutine);

            healCoroutine = StartCoroutine(AutoHealRoutine());
        }

        PlayDamageSound();

        if (!isFlashing)
            StartCoroutine(FlashDamageEffect());

        if (currentHealth <= 0)
            Die();
    }

    public void TakeDamage(float damage)
    {
        if (!this.enabled || isDying) return;

        currentHealth -= damage;

        // ダメージを受けたら回復タイマーをリセット
        if (enableAutoHeal)
        {
            if (healCoroutine != null)
                StopCoroutine(healCoroutine);

            healCoroutine = StartCoroutine(AutoHealRoutine());
        }

        PlayDamageSound();

        if (!isFlashing)
            StartCoroutine(FlashDamageEffect());

        if (currentHealth <= 0)
            Die();
    }

    private IEnumerator AutoHealRoutine()
    {
        // 最後にダメージを受けてから回復開始まで待つ
        yield return new WaitForSeconds(healStartDelay);

        // その間に死亡していなければ回復開始
        while (!isDying && currentHealth < maxHealth)
        {
            Heal(healAmount);
            yield return new WaitForSeconds(healInterval);
        }

        healCoroutine = null;
    }

    private IEnumerator FlashDamageEffect()
    {
        isFlashing = true;

        foreach (SpriteRenderer r in allRenderers)
            if (r != null) r.color = damageColor;

        yield return new WaitForSeconds(flashDuration);

        foreach (SpriteRenderer r in allRenderers)
            if (r != null) r.color = Color.white;

        isFlashing = false;
    }

    void Die()
    {
        if (isDying) return;
        isDying = true;

        if (healCoroutine != null)
        {
            StopCoroutine(healCoroutine);
            healCoroutine = null;
        }

        foreach (Collider2D col in allColliders)
        {
            if (col != null)
                col.enabled = false;
        }

        DropConfiguredLoot();

        AudioClip sound = GetRandomDamageSound();
        if (sound != null)
            AudioSource.PlayClipAtPoint(sound, transform.position);

        if (killedByPlayer)
        {
            string enemyName = gameObject.name.Replace("(Clone)", "").Trim();
            enemyName = Regex.Replace(enemyName, @"\(\d+\)", "").Trim();
            string message;

            if (enemyName.ToLower().Contains("chest"))
                message = enemyName + "を開けた！";
            else
                message = enemyName + "を倒した！";

            NotificationManager.Instance.Show(message);
            ExplorationManager.Instance?.AddProgress(progressPoint);
        }

        OnDestroyed?.Invoke();
        OnDestroyed = null;

        StartCoroutine(FadeAndDestroy());
    }

    private IEnumerator FadeRoutine(float startAlpha, float endAlpha, float duration)
    {
        float elapsed = 0;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float currentAlpha = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);

            foreach (SpriteRenderer r in allRenderers)
            {
                if (r != null)
                {
                    Color c = r.color;
                    c.a = currentAlpha;
                    r.color = c;
                }
            }

            yield return null;
        }
    }

    private IEnumerator FadeAndDestroy()
    {
        yield return StartCoroutine(FadeRoutine(1, 0, fadeOutDuration));
        Destroy(gameObject);
    }

    private void PlayDamageSound()
    {
        if (audioSource == null) return;

        AudioClip sound = GetRandomDamageSound();
        if (sound != null)
            audioSource.PlayOneShot(sound);
    }

    private AudioClip GetRandomDamageSound()
    {
        if (damageSounds != null)
        {
            int validSoundCount = 0;
            foreach (AudioClip sound in damageSounds)
            {
                if (sound != null)
                    validSoundCount++;
            }

            if (validSoundCount > 0)
            {
                int selectedSound = Random.Range(0, validSoundCount);
                foreach (AudioClip sound in damageSounds)
                {
                    if (sound == null) continue;
                    if (selectedSound-- == 0)
                        return sound;
                }
            }
        }

        return legacyDamageSound;
    }

    private void DropItem(ItemData data)
    {
        if (itemPrefab == null || data == null) return;

        Transform spawnPoint = transform.Find("HB");
        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;

        GameObject droppedObj = Instantiate(itemPrefab, pos, Quaternion.identity);
        Item itemScript = droppedObj.GetComponent<Item>();

        if (itemScript != null)
            itemScript.data = data;

        Rigidbody2D rb = droppedObj.GetComponent<Rigidbody2D>();

        if (rb != null)
        {
            Vector2 randomDir = Random.insideUnitCircle.normalized;
            rb.AddForce(randomDir * 0.0005f, ForceMode2D.Impulse);
        }
    }

    private void DropConfiguredLoot()
    {
        List<ItemData> normalEligibleItems = new List<ItemData>();
        List<ItemData> uniqueEligibleItems = new List<ItemData>();
        int playerLevel = PlayerHealth.Instance != null ? PlayerHealth.Instance.PlayerLevel : 1;
        StageData stage = BSPDungeonMaster.Instance != null
            ? BSPDungeonMaster.Instance.currentStageData
            : null;
        BSPDungeonMaster dungeon = BSPDungeonMaster.Instance;

        // Shared normal drops: rarity + probability keyframes in the BSP stage order.
        if (stage != null)
        {
            List<StageData> stageOrder = dungeon != null
                ? dungeon.GetOrderedStages()
                : new List<StageData> { stage };
            float falloff = stage.normalItemOrderFalloff;
            float rampDistance = stage.normalItemEntryRampStages;
            List<ItemData> candidates = NormalItemDropCalculator.GetCandidateItems(stageOrder);
            foreach (ItemData item in candidates)
            {
                float chance = NormalItemDropCalculator.GetStageProbability(
                    stageOrder, stage, playerLevel, item, falloff, rampDistance);
                chance *= GetNormalItemTypeMultiplier(item.itemType);
                if (RollDropChance(chance))
                    AddConfiguredItemAmounts(normalEligibleItems, item);
            }
        }

        // Monster-specific drops are an independent second roll.
        if (dropList != null)
        {
            foreach (DropItemSetting setting in dropList)
            {
                if (setting == null || setting.itemData == null) continue;
                float chance = Mathf.Clamp(setting.itemData.rarity, 0f, 100f) +
                    Mathf.Clamp(setting.monsterModifier, -100f, 100f);
                if (RollDropChance(chance))
                    AddConfiguredItemAmounts(uniqueEligibleItems, setting.itemData);
            }
        }

        int totalCount = RollTotalDropCount();
        if (normalEligibleItems.Count == 0 && uniqueEligibleItems.Count == 0) return;
        float normalRatio = Mathf.Clamp(normalDropRatioPercent, 0f, 100f) / 100f;
        for (int i = 0; i < totalCount; i++)
        {
            List<ItemData> selectedPool;
            if (normalEligibleItems.Count == 0)
                selectedPool = uniqueEligibleItems;
            else if (uniqueEligibleItems.Count == 0)
                selectedPool = normalEligibleItems;
            else
                selectedPool = Random.value < normalRatio ? normalEligibleItems : uniqueEligibleItems;

            DropItem(selectedPool[Random.Range(0, selectedPool.Count)]);
        }
    }

    private static void AddConfiguredItemAmounts(List<ItemData> eligibleItems, ItemData item)
    {
        if (item == null) return;
        int count = RollDropCount(item);
        for (int i = 0; i < count; i++)
            eligibleItems.Add(item);
    }

    private int RollTotalDropCount()
    {
        AnimationCurve curve = totalDropCountProbability;
        if (curve == null || curve.length == 0) return 0;

        int maxCount = Mathf.Clamp(maxTotalDropCount, 0, 100);
        float totalWeight = 0f;
        for (int count = 0; count <= maxCount; count++)
            totalWeight += Mathf.Max(0f, curve.Evaluate(count));

        if (totalWeight <= Mathf.Epsilon) return 0;

        float roll = Random.Range(0f, totalWeight);
        for (int count = 0; count <= maxCount; count++)
        {
            roll -= Mathf.Max(0f, curve.Evaluate(count));
            if (roll < 0f) return count;
        }

        return maxCount;
    }

    private static int RollDropCount(ItemData item)
    {
        AnimationCurve curve = item.dropCountProbability;
        if (curve == null || curve.length == 0) return 1;

        int maxCount = Mathf.Clamp(item.maxDropCount, 1, 100);
        float totalWeight = 0f;
        for (int count = 1; count <= maxCount; count++)
            totalWeight += Mathf.Clamp(curve.Evaluate(count), 0f, 100f);

        if (totalWeight <= Mathf.Epsilon) return 1;

        float roll = Random.Range(0f, totalWeight);
        for (int count = 1; count <= maxCount; count++)
        {
            roll -= Mathf.Clamp(curve.Evaluate(count), 0f, 100f);
            if (roll < 0f) return count;
        }

        return maxCount;
    }

    private float GetNormalItemTypeMultiplier(ItemType itemType)
    {
        if (normalItemTypeAdjustments == null) return 1f;
        for (int i = 0; i < normalItemTypeAdjustments.Count; i++)
        {
            MonsterItemTypeAdjustment adjustment = normalItemTypeAdjustments[i];
            if (adjustment != null && adjustment.itemType == itemType)
                return Mathf.Max(0f, 1f + Mathf.Clamp(adjustment.modifier, -100f, 100f) / 100f);
        }
        return 1f;
    }

    private static bool RollDropChance(float chancePercent)
    {
        float chance = Mathf.Clamp(chancePercent, 0f, 100f);
        return chance >= 100f || (chance > 0f && Random.Range(0f, 100f) < chance);
    }

    public void Heal(float amount)
    {
        currentHealth += amount;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
    }

    public void IncreaseMaxHealth(float amount)
    {
        maxHealth += amount;
        currentHealth += amount;

        if (currentHealth > maxHealth)
            currentHealth = maxHealth;
    }

    // OnDisable() は使用しません。
}
