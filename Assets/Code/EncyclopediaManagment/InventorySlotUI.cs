using UnityEngine;
using TMPro;
using UnityEngine.EventSystems; // Required for click events

public class InventorySlotUI : MonoBehaviour, IPointerClickHandler
{
    [Header("UI References")]
    public TextMeshProUGUI fishNameText;
    public TextMeshProUGUI fishValueText; // Add a text field for the value
    public GameObject selectionHighlight;

    public CaughtFish CurrentFish { get; private set; }

    private void Awake()
    {
        if (selectionHighlight != null)
        {
            selectionHighlight.SetActive(false);
        }
    }

    public void Populate(CaughtFish fish)
    {
        CurrentFish = fish;
        fishNameText.gameObject.SetActive(true);
        fishNameText.text = fish.GetDisplayName();

        // Show the value text
        if (fishValueText != null)
        {
            fishValueText.gameObject.SetActive(true);
            fishValueText.text = $"{fish.GetValue()} coins";
        }
    }

    public void Clear()
    {
        CurrentFish = null;
        fishNameText.gameObject.SetActive(false);
        if (fishValueText != null)
        {
            fishValueText.gameObject.SetActive(false);
        }
    }

    public void Select()
    {
        if (selectionHighlight != null)
        {
            selectionHighlight.SetActive(true);
        }
    }

    public void Deselect()
    {
        if (selectionHighlight != null)
        {
            selectionHighlight.SetActive(false);
        }
    }

    // --- NEW: Handle Clicks ---
    public void OnPointerClick(PointerEventData eventData)
    {
        // Tell the main UI controller that this slot was clicked
        InventoryUI.Instance.SelectSlot(this);
    }
}

