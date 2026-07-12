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
    [Tooltip("Buyable time-of-day skips. Each entry fast forwards the clock to the start of Day or Night, then time keeps flowing (not a permanent lock).")]
    public List<TimeOfDayOffer> timeOfDayOffers = new List<TimeOfDayOffer>();

    [Header("Interaction")]
    [Tooltip("Player must be within this distance to open the shop with the interact key.")]
    [Min(0f)] public float interactionRadius = 3f;
    // The interact key lives on the InputBindings asset (keyboardMouse.interact) — read via KeyInput.

    [Header("Feedback")]
    public ParticleSystem sellParticles;
    public ParticleSystem buyParticles;

    public static event Action OnVendorInventoryChanged;
    public static event Action OnCurrentShoppingVendorChanged;
    public static FishVendor CurrentShoppingVendor { get; private set; }

    private Transform playerTransform;
    private bool playerResolveAttempted;

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
        [Tooltip("Which time of day this entry skips the clock to when bought. ForcedNight = skip to the start of night, ForcedDay = skip to the start of day. (No longer a permanent lock.)")]
        public WorldStateManager.TimeMode mode = WorldStateManager.TimeMode.ForcedNight;
    }

    private void Start()
    {
        ResolvePlayerTransform();
    }

    private void Update()
    {
        // Opening the shop waits until any charge jump has fully landed (hint hidden too).
        if (PlayerController.IsPlayerJumping) return;

        // Tell the HUD prompt bar an interact is available (same gate as the press below).
        if (CurrentShoppingVendor == null && IsPlayerInRange()) InteractHint.Ping();

        if (KeyInput.InteractPressed || GamepadInput.InteractPressed)
        {
            // Opening only. While the shop is open, ShopController owns all input (closing is its
            // B/Esc back-out), so the vendor stays out of the way and the same A/Interact button
            // can be used to buy items without also toggling the shop shut.
            if (CurrentShoppingVendor == null && IsPlayerInRange())
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
        playerResolveAttempted = true;

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
        // Resolve at most once per scene from a hot path. The original code retried
        // FindFirstObjectByType every frame the player ref was null, which is a hidden
        // per-frame scene scan.
        if (playerTransform == null && !playerResolveAttempted) ResolvePlayerTransform();
        if (playerTransform == null) return false;
        return (playerTransform.position - transform.position).sqrMagnitude
               <= interactionRadius * interactionRadius;
    }

    public void OpenShop()
    {
        if (CurrentShoppingVendor != null) return;
        CurrentShoppingVendor = this;
        OnCurrentShoppingVendorChanged?.Invoke();

        // The 3D shop drives everything now — the inventory is NOT borrowed (that's what used to
        // leak the bait/tackle selector arrows into the shop).
        if (ShopController.Instance != null) ShopController.Instance.BeginConversation(this);
        else Debug.LogWarning("[FishVendor] No ShopController in scene; cannot open the 3D shop.");
    }

    public void CloseShop()
    {
        if (CurrentShoppingVendor != this) return;
        if (ShopController.Instance != null && ShopController.Instance.IsOpen)
            ShopController.Instance.CloseShop(); // tears down + clears the static via ClearShoppingVendor
        else
            ClearShoppingVendor();
    }

    // Clears the active-shopping-vendor static without routing back through ShopController (which is
    // what calls this during its own teardown). Use CloseShop() for the normal "close the shop" entry.
    public static void ClearShoppingVendor()
    {
        if (CurrentShoppingVendor == null) return;
        CurrentShoppingVendor = null;
        OnCurrentShoppingVendorChanged?.Invoke();
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
        if (offer.bait.isAlwaysAvailable) return false; // Always-available bait can't be purchased.
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
        if (world == null || GameTimeManager.Instance == null)
        {
            Debug.LogWarning("[FishVendor] No WorldStateManager/GameTimeManager available; cannot skip time of day.");
            return false;
        }

        // The offer's mode doubles as a day/night selector: ForcedNight skips to the start of
        // night, anything else skips to the start of day. The skip is a one-time clock jump,
        // so re-buying the same time of day simply resets the clock to that period's start.
        bool toNight = offer.mode == WorldStateManager.TimeMode.ForcedNight;

        if (!PlayerInventory.Instance.TrySpendCurrency(offer.price))
        {
            Debug.Log($"Not enough coins to buy {offer.displayName} ({offer.price} required).");
            return false;
        }

        world.SkipToTimeOfDay(toNight);

        if (buyParticles != null) buyParticles.Play();
        OnVendorInventoryChanged?.Invoke();
        Debug.Log($"Bought {offer.displayName} for {offer.price} coins. Skipped to {(toNight ? "night" : "day")}.");
        return true;
    }
}