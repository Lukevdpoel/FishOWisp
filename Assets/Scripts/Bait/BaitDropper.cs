using System.Collections.Generic;
using UnityEngine;

public class BaitDropper : MonoBehaviour
{
    [System.Serializable]
    public class DropEntry
    {
        public BaitItem bait;
        [Range(0f, 1f)] public float chance = 0.5f;
        [Min(1)] public int minCount = 1;
        [Min(1)] public int maxCount = 1;
    }

    [Tooltip("Each entry is rolled independently when the pot drops loot.")]
    [SerializeField] private List<DropEntry> dropTable = new List<DropEntry>();

    [Tooltip("If true, only the first successful entry awards bait (mutually exclusive rolls).")]
    [SerializeField] private bool firstHitOnly = false;

    [Header("Popup")]
    [Tooltip("World-space height above the drop point where the popup starts.")]
    [SerializeField] private float popupSpawnHeight = 0.6f;
    [Tooltip("How far the popup rises before disappearing.")]
    [SerializeField] private float popupRiseDistance = 1.2f;
    [Tooltip("Total lifetime of the popup in seconds.")]
    [SerializeField] private float popupDuration = 1.4f;
    [Tooltip("Scale of the popup sprite in world units.")]
    [SerializeField] private float popupScale = 0.5f;
    [Tooltip("Stagger when multiple items drop at once so they don't overlap.")]
    [SerializeField] private float popupStaggerSeconds = 0.15f;

    public void Drop(Vector3 worldPos)
    {
        if (BaitInventory.Instance == null)
        {
            Debug.LogWarning("[BaitDropper] No BaitInventory in scene; drop skipped.");
            return;
        }

        int dropIndex = 0;
        foreach (DropEntry entry in dropTable)
        {
            if (entry == null || entry.bait == null) continue;
            if (entry.bait.isAlwaysAvailable) continue; // Always-available bait is never collected.
            if (Random.value > entry.chance) continue;

            int amount = Random.Range(entry.minCount, entry.maxCount + 1);
            BaitInventory.Instance.AddBait(entry.bait, amount);

            ShowPopup(entry.bait, worldPos, dropIndex);
            dropIndex++;

            if (firstHitOnly) break;
        }
    }

    private void ShowPopup(BaitItem bait, Vector3 worldPos, int index)
    {
        if (bait == null || bait.icon == null) return;

        Vector3 spawnPos = worldPos + Vector3.up * popupSpawnHeight;
        float delay = index * popupStaggerSeconds;
        BaitPickupPopup.Spawn(bait.icon, spawnPos, popupScale, popupDuration, popupRiseDistance, delay);
    }
}
