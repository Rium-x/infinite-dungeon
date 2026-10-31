using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ShopOffer
{
    public ItemData item;
    [Min(0)] public int buyPrice = 1;
    [Min(0)] public int sellPrice = 1;
}

/// <summary>Player-facing shop entity. PickUp input opens the shop while the player is nearby.</summary>
[RequireComponent(typeof(CircleCollider2D))]
public sealed class ShopEntity : MonoBehaviour
{
    [Header("店主")]
    [SerializeField] private string shopName = "ショップ";
    [SerializeField] private Sprite shopkeeperPortrait;
    [SerializeField] private Sprite shopkeeperBackground;
    [Header("品ぞろえ")]
    [SerializeField] private List<ShopOffer> offers = new List<ShopOffer>();
    [Header("接触判定")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField, Min(0.1f)] private float interactionRadius = 1f;

    private PlayerControls controls;
    private bool playerNearby;

    public string ShopName => shopName;
    public Sprite ShopkeeperPortrait => shopkeeperPortrait;
    public Sprite ShopkeeperBackground => shopkeeperBackground;
    public IReadOnlyList<ShopOffer> Offers => offers;

    private void Awake()
    {
        CircleCollider2D interactionCollider = GetComponent<CircleCollider2D>();
        interactionCollider.isTrigger = true;
        interactionCollider.radius = interactionRadius;
        controls = new PlayerControls();
        controls.Player.PickUp.performed += OnPickUpPerformed;
    }

    private void OnValidate()
    {
        CircleCollider2D interactionCollider = GetComponent<CircleCollider2D>();
        if (interactionCollider != null)
        {
            interactionCollider.isTrigger = true;
            interactionCollider.radius = Mathf.Max(0.1f, interactionRadius);
        }
    }
    private void OnEnable() => controls?.Enable();
    private void OnDisable() => controls?.Disable();

    private void OnPickUpPerformed(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        if (BSPDungeonMaster.IsPlayerInputLocked || !playerNearby || ShopWindowUI.IsShopOpen) return;
        ShopWindowUI.OpenShop(this);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(playerTag)) playerNearby = true;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag(playerTag)) playerNearby = false;
    }
}
