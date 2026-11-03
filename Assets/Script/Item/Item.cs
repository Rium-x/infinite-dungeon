using UnityEngine;
using System.Collections;

public class Item : MonoBehaviour
{
    public ItemData data;
    public int durability = -1;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private bool isPlayerNearby = false;

    // ★新Input Systemの参照用
    private PlayerControls controls;

    [Header("寿命の設定")]
    public bool isPermanent = false; 
    public float lifeTime = 180f;      
    public float blinkStartTime = 10f; 
    public float blinkInterval = 0.2f; 

    // ★Awakeでインスタンス化
    private void Awake()
    {
        controls = new PlayerControls();
    }

    // ★アクティブ状態に合わせて入力を有効・無効化
    private void OnEnable()  => controls.Enable();
    private void OnDisable() => controls.Disable();

    private void Start()
    {
        ApplyDataSettings();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();

        if (data != null)
        {
            if (data.icon != null) spriteRenderer.sprite = data.icon;
            rb.mass = data.mass;
        }

        StartCoroutine(DisableColliderRoutine());
        StartCoroutine(LifeCycleRoutine());
    }

    private void Update()
    {
        if (BSPDungeonMaster.IsPlayerInputLocked || ShopWindowUI.IsShopOpen) return;

        bool pickUpPressed = controls.Player.PickUp.IsPressed();
        if (isPlayerNearby && (pickUpPressed || (!pickUpPressed && InputKeyboardFallback.WasPressedThisFrame(KeyCode.X))))
        {
            PickUp();
        }
    }

    private IEnumerator DisableColliderRoutine()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) yield break;
        col.enabled = false;
        yield return new WaitForSeconds(0.1f);
        col.enabled = true;
    }

    private IEnumerator LifeCycleRoutine()
    {
        float rarityMultiplier = 1.0f;
        if (data != null)
        {
            // Preserve legacy rarity lifetime tiers while rarity itself is now numeric.
            if (data.rarity >= 3f) rarityMultiplier = 5.0f;
            else if (data.rarity >= 2f) rarityMultiplier = 2.0f;
            else if (data.rarity >= 1f) rarityMultiplier = 1.5f;
        }

        float baseTime = (data != null ? data.baseLifeTime : 180f) * rarityMultiplier;
        lifeTime = baseTime + Random.Range(-10f, 10f);

        if (isPermanent) lifeTime *= 1.7f;
        if (lifeTime < blinkStartTime + 5f) lifeTime = blinkStartTime + 5f;

        float waitBeforeBlink = lifeTime - blinkStartTime;
        if (waitBeforeBlink > 0) yield return new WaitForSeconds(waitBeforeBlink);

        float elapsed = 0;
        while (elapsed < blinkStartTime)
        {
            if (spriteRenderer != null) spriteRenderer.enabled = !spriteRenderer.enabled;
            yield return new WaitForSeconds(blinkInterval);
            elapsed += blinkInterval;
        }

        Destroy(gameObject);
    }

    private void ApplyDataSettings()
    {
        if (data == null) return;
        transform.localScale = Vector3.one * data.visualScale;
        CircleCollider2D col = GetComponent<CircleCollider2D>();
        if (col != null) col.radius = data.collisionRadius;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player")) isPlayerNearby = true;
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player")) isPlayerNearby = false;
    }

    private void PickUp()
    {
        if (data == null || Inventory.Instance == null) return;

        bool isNewItem = !Inventory.Instance.HasItem(data);
        bool isAdded = durability >= 0
            ? Inventory.Instance.AddItemWithDurability(data, durability)
            : Inventory.Instance.AddItem(data);

        if (!isAdded) return;

        if (data.pickupSound != null)
        {
            Vector3 spawnPos = Camera.main != null
                ? Camera.main.transform.position
                : transform.position;
            spawnPos.z += 1f;
            AudioSource.PlayClipAtPoint(data.pickupSound, spawnPos);
        }

        Destroy(gameObject);

        if (NotificationManager.Instance == null)
        {
            Debug.LogError("NotificationManager が見つからないため、拾得通知を表示できません。", this);
            return;
        }

        if (isNewItem)
        {
            NotificationManager.Instance.Show("新しく" + data.itemName + "を拾った！", Color.yellow, NotificationManager.NotificationSound.Pickup);
        }
        else
        {
            NotificationManager.Instance.Show(data.itemName + "を拾った！");
        }
    }

    public void SetItemData(ItemData newData)
    {
        this.data = newData;
        ApplyDataSettings();
    }

    public void SetDurability(int value) { durability = value; }
}
