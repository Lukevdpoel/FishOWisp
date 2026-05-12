using UnityEngine;

// Drops a fixed amount of currency into PlayerInventory and shows a floating pickup popup
// with the configured icon. Mirrors BaitDropper but for coins. Attach to anything that
// should reward currency on destroy (urns, chests, etc.) and call Drop(worldPos).
public class CurrencyDropper : MonoBehaviour
{
    [Header("Reward")]
    [Tooltip("Probability the urn drops anything at all. 1 = always, 0 = never.")]
    [Range(0f, 1f)] public float dropChance = 1f;
    [Tooltip("Minimum coins awarded when a drop succeeds.")]
    [Min(0)] public int minAmount = 1;
    [Tooltip("Maximum coins awarded when a drop succeeds (inclusive).")]
    [Min(0)] public int maxAmount = 10;

    [Header("Popup")]
    [Tooltip("Sprite shown as the floating popup when the currency drops.")]
    public Sprite icon;
    [Tooltip("World-space height above the drop point where the popup starts.")]
    [SerializeField] private float popupSpawnHeight = 0.6f;
    [Tooltip("How far the popup rises before disappearing.")]
    [SerializeField] private float popupRiseDistance = 1.2f;
    [Tooltip("Total lifetime of the popup in seconds.")]
    [SerializeField] private float popupDuration = 1.4f;
    [Tooltip("Scale of the popup sprite in world units.")]
    [SerializeField] private float popupScale = 0.5f;

    public void Drop(Vector3 worldPos)
    {
        if (PlayerInventory.Instance == null)
        {
            Debug.LogWarning("[CurrencyDropper] No PlayerInventory in scene; drop skipped.");
            return;
        }

        if (Random.value > dropChance) return;

        int lo = Mathf.Min(minAmount, maxAmount);
        int hi = Mathf.Max(minAmount, maxAmount);
        int amount = Random.Range(lo, hi + 1);
        if (amount <= 0) return;

        PlayerInventory.Instance.TransactionAddCurrency(amount);

        if (icon != null)
        {
            Vector3 spawnPos = worldPos + Vector3.up * popupSpawnHeight;
            BaitPickupPopup.Spawn(icon, spawnPos, popupScale, popupDuration, popupRiseDistance, 0f);
        }
    }
}
