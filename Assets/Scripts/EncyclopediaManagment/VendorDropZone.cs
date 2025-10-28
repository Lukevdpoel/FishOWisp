using UnityEngine;
using UnityEngine.EventSystems;

public class VendorDropZone : MonoBehaviour, IDropHandler
{
    // Assign your main VendorUI script here
    public VendorUI vendorUI;

    public void OnDrop(PointerEventData eventData)
    {
        // Check if we have a fish being dragged
        PhysicalFish draggedFish = FishDragger.CurrentDraggedFish;

        if (draggedFish != null)
        {
            // Success! Add this fish to the vendor's staging area
            vendorUI.AddFishToStaging(draggedFish);

            // Clear the static reference
            FishDragger.CurrentDraggedFish = null;
        }
    }
}