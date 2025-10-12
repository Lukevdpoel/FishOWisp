using UnityEngine;
using TMPro;

public class InventorySlotUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The text element that displays the fish's name and size.")]
    public TextMeshProUGUI fishNameText;

    [Header("Selection Visuals")]
    [Tooltip("A visual element (like a border) that appears when this slot is selected.")]
    public GameObject selectionHighlight;

    // It's good practice to disable the highlight when the slot is first created.
    private void Awake()
    {
        if (selectionHighlight != null)
        {
            selectionHighlight.SetActive(false);
        }
    }

    public void Populate(CaughtFish fish)
    {
        fishNameText.gameObject.SetActive(true);
        fishNameText.text = fish.GetDisplayName();
    }

    public void Clear()
    {
        fishNameText.gameObject.SetActive(false);
    }

    // --- NEW METHODS FOR SELECTION ---
    /// <summary>
    /// Activates the visual highlight for this slot.
    /// </summary>
    public void Select()
    {
        if (selectionHighlight != null)
        {
            selectionHighlight.SetActive(true);
        }
    }

    /// <summary>
    /// Deactivates the visual highlight for this slot.
    /// </summary>
    public void Deselect()
    {
        if (selectionHighlight != null)
        {
            selectionHighlight.SetActive(false);
        }
    }
}

