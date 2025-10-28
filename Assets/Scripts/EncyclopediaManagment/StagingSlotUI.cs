using UnityEngine;
using TMPro;

// Add this to your "StagingSlotPrefab"
public class StagingSlotUI : MonoBehaviour
{
    public TextMeshProUGUI fishInfoText;
    // You could add a button here to "Return fish to bucket"

    public void Populate(CaughtFish fish)
    {
        if (fish != null)
        {
            fishInfoText.text = $"{fish.GetDisplayName()} ({fish.GetValue()} coins)";
        }
    }
}