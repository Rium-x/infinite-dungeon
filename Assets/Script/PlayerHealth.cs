using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using System.Collections.Generic;

public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    [SerializeField] private float maxHealth = 100f;
    [SerializeField, Min(1), Tooltip("ドロップ抽選に使うレベル。レベル進行を追加したら SetPlayerLevel で更新してください。")]
    private int playerLevel = 1;
    public int PlayerLevel => Mathf.Max(1, playerLevel);

    [Header("レベル・経験値")]
    [SerializeField, Min(0)] private int experiencePoints;
    [SerializeField, Min(1)] private int experienceToNextLevel = 100;
    [SerializeField, Min(0)] private int experienceRequiredIncreasePerLevel = 50;
    [SerializeField, Min(0f)] private float maxHealthPerLevel = 10f;
    [SerializeField, Min(0f)] private float maxMPPerLevel = 5f;
    [SerializeField, Min(0f)] private float attackPowerPerLevel = 1f;
    [SerializeField, Min(0f)] private float defensePerLevel = 1f;
    private int basePlayerLevel = 1;
    private float baseMaxHealth;
    public int ExperiencePoints => experiencePoints;
    public int ExperienceToNextLevel => experienceToNextLevel;
    public bool CanLevelUp => experiencePoints >= experienceToNextLevel;
    public float CurrentHealth => currentHealth;
    public float MaximumHealth => maxHealth;
    public float MaximumHealthBonus => (PlayerLevel - basePlayerLevel) * maxHealthPerLevel;
    public float MaximumMPBonus => (PlayerLevel - basePlayerLevel) * maxMPPerLevel;
    public float AttackPowerBonus => (PlayerLevel - basePlayerLevel) * attackPowerPerLevel;
    public float DefenseBonus => (PlayerLevel - basePlayerLevel) * defensePerLevel;

    public bool IsAtFullHealth => currentHealth >= maxHealth;
    
    [Header("HPバーの設定")]
    [SerializeField] private Slider hpSlider; 
    [SerializeField] private float currentHealth;

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip damageSound;

    [Header("ダメージ演出")]
    [SerializeField] private Color damageColor = Color.red;
    [SerializeField] private float flashDuration = 0.2f;

    private SpriteRenderer[] allRenderers;
    private bool isFlashing = false;
    private CharacterSorting characterSorting;

    [Header("ドロップ設定")]
    public GameObject itemPrefab;

    void Awake()
    {
        basePlayerLevel = PlayerLevel;
        baseMaxHealth = maxHealth;
    }

    void Start()
    {
        currentHealth = maxHealth;
        if (hpSlider != null)
        {
            hpSlider.maxValue = maxHealth;
            hpSlider.value = maxHealth;
        }
        allRenderers = GetComponentsInChildren<SpriteRenderer>();

        characterSorting = GetComponent<CharacterSorting>();
        if (characterSorting == null)
        {
            characterSorting = gameObject.AddComponent<CharacterSorting>();
        }
    }

    void OnEnable()
    {
        Instance = this;
        BSPDungeonMaster.OnGenerationComplete -= HandleDungeonGenerated;
        BSPDungeonMaster.OnGenerationComplete += HandleDungeonGenerated;
    }

    void OnDisable()
    {
        BSPDungeonMaster.OnGenerationComplete -= HandleDungeonGenerated;
        if (Instance == this)
            Instance = null;
    }

    public void SetPlayerLevel(int level)
    {
        playerLevel = Mathf.Max(1, level);
    }

    public void AddExperience(int amount)
    {
        if (amount <= 0) return;
        experiencePoints += amount;
        NotificationManager.Instance?.Show($"経験値 +{amount}");
    }

    public bool TryLevelUp()
    {
        if (!CanLevelUp) return false;

        experiencePoints -= experienceToNextLevel;
        playerLevel++;
        experienceToNextLevel += experienceRequiredIncreasePerLevel;

        maxHealth = baseMaxHealth + (playerLevel - basePlayerLevel) * maxHealthPerLevel;
        currentHealth = Mathf.Min(maxHealth, currentHealth + maxHealthPerLevel);
        if (hpSlider != null)
        {
            hpSlider.maxValue = maxHealth;
            hpSlider.value = currentHealth;
        }

        PlayerMP.Instance?.IncreaseMaximum(maxMPPerLevel);
        NotificationManager.Instance?.Show($"レベルアップ！ Lv.{playerLevel}");
        return true;
    }

    void HandleDungeonGenerated()
    {
        RandomTeleporter tp = GetComponentInChildren<RandomTeleporter>();
        if (tp != null)
        {
            StageData stageData = BSPDungeonMaster.Instance != null
                ? BSPDungeonMaster.Instance.currentStageData
                : null;

            if (stageData != null && !stageData.randomPlayerSpawn)
            {
                transform.position = new Vector3(
                    stageData.playerSpawnPosition.x,
                    stageData.playerSpawnPosition.y,
                    transform.position.z);
                Physics2D.SyncTransforms();
                GetComponent<WaterTerrainEffect>()?.RefreshTerrainAtSpawn();
            }
            else
            {
                tp.TeleportWithFade();
            }
        }
    }

    public void TakeDamage(float damage, bool isEnemyDamage = false)
    {
        if (!this.enabled) return; 

        float finalDamage = damage;
        bool armorDefended = false;

        ArmorDefense[] armorDefenses = GetComponentsInChildren<ArmorDefense>(true);
        foreach (ArmorDefense armorDefense in armorDefenses)
        {
            float damageBeforeArmor = finalDamage;
            finalDamage = armorDefense.ApplyDamageReduction(finalDamage, isEnemyDamage);
            if (finalDamage < damageBeforeArmor)
                armorDefended = true;
        }

        if (isEnemyDamage)
            finalDamage = Mathf.Max(0f, finalDamage - DefenseBonus);

        if (EquipmentManager.Instance != null)
        {
            ShieldAction shield = null;

            if (EquipmentManager.Instance.leftHandSlot != null)
                shield = EquipmentManager.Instance.leftHandSlot.GetComponentInChildren<ShieldAction>();

            if (shield == null && EquipmentManager.Instance.rightHandSlot != null)
                shield = EquipmentManager.Instance.rightHandSlot.GetComponentInChildren<ShieldAction>();

            if (shield != null)
            {
                float damageBeforeShield = finalDamage;
                finalDamage = shield.ApplyDamageReduction(finalDamage);
                if (finalDamage < damageBeforeShield && Inventory.Instance != null)
                {
                    int shieldSlot = EquipmentManager.Instance.leftHandSlot != null &&
                        EquipmentManager.Instance.leftHandSlot.GetComponentInChildren<ShieldAction>() == shield
                        ? EquipmentManager.Instance.currentLeftSlot
                        : EquipmentManager.Instance.currentRightSlot;
                    Inventory.Instance.DamageDurability(shieldSlot);
                }
            }

            if (isEnemyDamage && damage > 0f && armorDefended && Inventory.Instance != null)
            {
                EquipmentManager equipment = EquipmentManager.Instance;
                Inventory.Instance.DamageDurability(equipment.currentHandsArmorSlot);
                Inventory.Instance.DamageDurability(equipment.currentBodyArmorSlot);
                Inventory.Instance.DamageDurability(equipment.currentFaceArmorSlot);
                Inventory.Instance.DamageDurability(equipment.currentFeetArmorSlot);
            }
        }

        currentHealth -= finalDamage;
        
        if (hpSlider != null) hpSlider.value = currentHealth;

        if (isFlashing) return; 

        if (damageSound != null && audioSource != null) audioSource.PlayOneShot(damageSound);
        StartCoroutine(FlashDamageEffect());

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private IEnumerator FlashDamageEffect()
    {
        isFlashing = true;
        foreach (SpriteRenderer renderer in allRenderers) renderer.color = damageColor;
        yield return new WaitForSeconds(flashDuration);
        foreach (SpriteRenderer renderer in allRenderers) renderer.color = Color.white;
        isFlashing = false;
    }

    void Die()
    {
        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null)
        {
            combat.ResetActionState();
        }

        DropHalfItems();

        NotificationManager.Instance.Show("プレイヤーが力つきた。アイテムを再はいふしました。");


        Debug.Log("プレイヤーが力尽きた。アイテムを再配布しました。");

        currentHealth = maxHealth;

        

        RandomTeleporter tp = GetComponentInChildren<RandomTeleporter>();
        if (tp != null)
        {
            tp.TeleportWithFade(() => {
                if (hpSlider != null) hpSlider.value = maxHealth;
            });
        }
    }

    private void DropHalfItems()
{
    if (Inventory.Instance == null || itemPrefab == null)
        return;

    Transform spawnPoint = transform.Find("HB");

    Vector3 pos =
        spawnPoint != null
        ? spawnPoint.position
        : transform.position;

    pos.z = 0;

    List<ItemStack> inventory =
        Inventory.Instance.items;

    for (int slot = 0; slot < inventory.Count; slot++)
    {
        ItemStack stack = inventory[slot];

        if (stack == null ||
    stack.itemData == null ||
    stack.amount <= 0)
{
    continue;
}

if (stack.amount == 1)
{
    continue;
}

// 所持数の20～60％をランダムで落とす
int minDrop =
    Mathf.FloorToInt(stack.amount * 0.2f);

int maxDrop =
    Mathf.CeilToInt(stack.amount * 0.6f);

int dropAmount =
    Random.Range(minDrop, maxDrop + 1);

        dropAmount =
            Mathf.Clamp(
                dropAmount,
                1,
                stack.amount
            );

        // インベントリから減らす
        stack.amount -= dropAmount;

        // 地面へ落とす
        for (int i = 0; i < dropAmount; i++)
        {
            GameObject droppedObj =
                Instantiate(
                    itemPrefab,
                    pos,
                    Quaternion.identity
                );

            Item itemScript =
                droppedObj.GetComponent<Item>();

            if (itemScript != null)
            {
                itemScript.SetItemData(
                    stack.itemData
                );

                itemScript.isPermanent = true;
            }

            Rigidbody2D rb =
                droppedObj.GetComponent<Rigidbody2D>();

            if (rb != null)
            {
                Vector2 scatterDir =
                    Random.insideUnitCircle.normalized;

                rb.AddForce(
                    scatterDir * 0.0005f,
                    ForceMode2D.Impulse
                );
            }
        }

        // 0個になったらスロットを空にする
        if (stack.amount <= 0)
        {
            inventory[slot] = null;
        }
    }

    NotificationManager.Instance.Show(
        "死亡した"
    );
}

public void Heal(float amount)
{
    Debug.Log(
        $"回復前HP: {currentHealth}"
    );

    currentHealth += amount;

    currentHealth =
        Mathf.Min(
            currentHealth,
            maxHealth
        );

    Debug.Log(
        $"回復後HP: {currentHealth}"
    );

    if (hpSlider != null)
    {
        hpSlider.value =
            currentHealth;
    }
}
}
