using UnityEngine;

public class FishVendor : MonoBehaviour
{
    [Header("Vendor Settings")]
    [Tooltip("Optional: Multiplier for fish prices (e.g. 1.2 for 20% bonus)")]
    public float priceMultiplier = 1.0f;

    [Header("Feedback")]
    public ParticleSystem sellParticles;
    // You could add AudioSource here for a "Cha-ching" sound

    public void SellFishToVendor(CaughtFish fish)
    {
        if (fish == null) return;

        // Calculate Value
        int baseValue = fish.GetValue();
        int finalValue = Mathf.RoundToInt(baseValue * priceMultiplier);

        // Add to Player Currency via Inventory Singleton
        // We use a custom method in PlayerInventory to handle the transaction logic
        PlayerInventory.Instance.TransactionAddCurrency(finalValue);

        // Feedback
        Debug.Log($"Sold {fish.preset.fishName} for {finalValue} coins!");

        if (sellParticles != null)
            sellParticles.Play();
    }
}