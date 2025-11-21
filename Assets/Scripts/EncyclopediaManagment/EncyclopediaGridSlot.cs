using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class EncyclopediaGridSlot : MonoBehaviour
{
    [Header("UI References")]
    public Image iconImage;
    [Tooltip("An image (like a yellow border) enabled when selected.")]
    public GameObject selectionHighlight;
    [Tooltip("An overlay (like a black panel or question mark) enabled when fish is not caught.")]
    public GameObject unknownOverlay;

    public FishEncyclopediaEntry myEntry;
    private EncyclopediaUIController controller;

    public void Setup(FishEncyclopediaEntry entry, EncyclopediaUIController uiController)
    {
        myEntry = entry;
        controller = uiController;

        // 1. Set the icon based on the Preset data
        if (entry.preset != null)
        {
            iconImage.sprite = entry.preset.fishImage;
        }

        // 2. Handle "Unknown" state visuals
        bool isCaught = entry.hasCaught > 0;

        if (unknownOverlay != null)
        {
            unknownOverlay.SetActive(!isCaught);
        }

        // Optional: Darken the icon if not caught (silhouette effect)
        iconImage.color = isCaught ? Color.white : Color.black;

        // Start deselected
        Deselect();
    }

    // Detects the click and notifies the Controller
    public void OnClicked()
    {
        if (controller != null)
        {
            controller.OnSlotClicked(myEntry, this);
        }
    }

    public void Select()
    {
        if (selectionHighlight != null) selectionHighlight.SetActive(true);
    }

    public void Deselect()
    {
        if (selectionHighlight != null) selectionHighlight.SetActive(false);
    }
}