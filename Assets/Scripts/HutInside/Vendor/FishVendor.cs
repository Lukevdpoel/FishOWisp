using System;
using System.Collections.Generic;
using UnityEngine;

public class FishVendor : MonoBehaviour
{
    [Header("Vendor Settings")]
    [Tooltip("Optional: Multiplier for fish prices (e.g. 1.2 for 20% bonus)")]
    public float priceMultiplier = 1.0f;

    [Header("Bait Shop")]
    [Tooltip("How many bait units the player receives per purchase.")]
    [Min(1)] public int baitStackSize = 5;
    [Tooltip("Bait this vendor will sell. Each entry has a BaitItem and the price for one stack.")]
    public List<BaitOffer> baitOffers = new List<BaitOffer>();

    [Header("Time-of-Day Shop")]
    [Tooltip("Buyable permanent time-of-day overrides. Each entry sets the world to Day or Night until another is bought.")]
    public List<TimeOfDayOffer> timeOfDayOffers = new List<TimeOfDayOffer>();

    [Header("Interaction")]
    [Tooltip("Player must be within this distance to open the shop with E.")]
    [Min(0f)] public float interactionRadius = 3f;
    [Tooltip("Key the player presses to open/close this vendor's shop.")]
    public KeyCode interactKey = KeyCode.E;

    [Header("Feedback")]
    public ParticleSystem sellParticles;
    public ParticleSystem buyParticles;

    public static event Action OnVendorInventoryChanged;
    public static event Action OnCurrentShoppingVendorChanged;
    public static FishVendor CurrentShoppingVendor { get; private set; }

    private Transform playerTransform;

    [Serializable]
    public class BaitOffer
    {
        public BaitItem bait;
        [Min(0)] public int pricePerStack = 25;
    }

    [Serializable]
    public class TimeOfDayOffer
    {
        [Tooltip("Display name shown on the buy button row, e.g. 'Night Lantern' or 'Sunshine Stone'.")]
        public string displayName = "Night Lantern";
        [Tooltip("Optional icon shown to the left of the row.")]
        public Sprite icon;
        [Min(0)] public int price = 200;
        [Tooltip("Which permanent override this entry sets when bought.")]
        public WorldStateManager.TimeMode mode = WorldStateManager.TimeMode.ForcedNight;
    }

    private void Start()
    {
        ResolvePlayerTransform();
    }

    private void Update()
    {
        if (Input.GetKeyDown(interactKey))
        {
            if (CurrentShoppingVendor == this)
            {
                CloseShop();
            }
            else if (CurrentShoppingVendor == null && IsPlayerInRange())
            {
                OpenShop();
            }
        }
    }

    private void ResolvePlayerTransform()
    {
        // PlayerController owns the CharacterController and is what actually moves around. Its
        // transform.root often points at a static rig parent ("Dev") that stays at world origin,
        // so we deliberately use the PlayerController's own transform here.
        PlayerController pc = FindFirstObjectByType<PlayerController>();
        if (pc != null)
        {
            playerTransform = pc.transform;
            return;
        }

        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null) playerTransform = tagged.transform;
    }

    private bool IsPlayerInRange()
    {
        if (playerTransform == null) ResolvePlayerTransform();
        if (playerTransform == null) return false;
        return (playerTransform.position - transform.position).sqrMagnitude
               <= interactionRadius * interactionRadius;
    }

    public void OpenShop()
    {
        CurrentShoppingVendor = this;
        OnCurrentShoppingVendorChanged?.Invoke();

        InventoryUI inv = FindFirstObjectByType<InventoryUI>();
        if (inv != null && !InventoryUI.IsInventoryOpen) inv.OpenInventory();
    }

    public void CloseShop()
    {
        if (CurrentShoppingVendor != this) return;
        CurrentShoppingVendor = null;
        OnCurrentShoppingVendorChanged?.Invoke();

        InventoryUI inv = FindFirstObjectByType<InventoryUI>();
        if (inv != null && InventoryUI.IsInventoryOpen) inv.CloseInventory();
    }

    public void SellFishToVendor(CaughtFish fish)
    {
        if (fish == null) return;

        int baseValue = fish.GetValue();
        int finalValue = Mathf.RoundToInt(baseValue * priceMultiplier);

        PlayerInventory.Instance.TransactionAddCurrency(finalValue);

        Debug.Log($"Sold {fish.preset.fishName} for {finalValue} coins!");

        if (sellParticles != null)
            sellParticles.Play();
    }

    public bool TryBuyBait(BaitOffer offer)
    {
        if (offer == null || offer.bait == null) return false;
        if (PlayerInventory.Instance == null || BaitInventory.Instance == null) return false;

        if (!PlayerInventory.Instance.TrySpendCurrency(offer.pricePerStack))
        {
            Debug.Log($"Not enough coins to buy {offer.bait.displayName} x{baitStackSize} ({offer.pricePerStack} required).");
            return false;
        }

        BaitInventory.Instance.AddBait(offer.bait, baitStackSize);

        if (buyParticles != null) buyParticles.Play();
        OnVendorInventoryChanged?.Invoke();
        Debug.Log($"Bought {offer.bait.displayName} x{baitStackSize} for {offer.pricePerStack} coins.");
        return true;
    }

    public bool TryBuyTimeOfDay(TimeOfDayOffer offer)
    {
        if (offer == null) return false;
        if (PlayerInventory.Instance == null) return false;

        WorldStateManager world = WorldStateManager.Instance;
        if (world == null)
        {
            Debug.LogWarning("[FishVendor] No WorldStateManager available; cannot apply time override.");
            return false;
        }

        if (world.CurrentTimeMode == offer.mode)
        {
            Debug.Log($"{offer.displayName} already active.");
            return false;
        }

        if (!PlayerInventory.Instance.TrySpendCurrency(offer.price))
        {
            Debug.Log($"Not enough coins to buy {offer.displayName} ({offer.price} required).");
            return false;
        }

        world.SetTimeMode(offer.mode);

        if (buyParticles != null) buyParticles.Play();
        OnVendorInventoryChanged?.Invoke();
        Debug.Log($"Bought {offer.displayName} for {offer.price} coins. Time mode: {offer.mode}");
        return true;
    }
}