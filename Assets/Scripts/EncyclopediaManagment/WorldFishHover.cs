using UnityEngine;
using UnityEngine.EventSystems;

public class WorldFishHover : MonoBehaviour
{
    [Header("References")]
    public InventoryTooltip tooltip;

    [Header("Settings")]
    public LayerMask fishLayerMask;
    public float rayDistance = 50f;
    [Tooltip("How 'thick' the mouse ray is. Higher numbers make it easier to hit fish.")]
    public float castRadius = 0.5f;

    private WorldFishData currentHoveredFish;

    void Update()
    {
        // 1. Force clear tooltip if inventory is opened
        if (InventoryUI.IsInventoryOpen)
        {
            if (currentHoveredFish != null) ClearTooltip();
            return;
        }

        // 2. Safety Check: If cursor is hidden/locked, don't try to hover
        if (!Cursor.visible || Cursor.lockState == CursorLockMode.Locked)
        {
            if (currentHoveredFish != null) ClearTooltip();
            return;
        }

        // 3. UI Blocking
        if (EventSystem.current.IsPointerOverGameObject())
        {
            if (currentHoveredFish != null) ClearTooltip();
            return;
        }

        HandleHover();
    }

    private void HandleHover()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        // --- FIX: Use SphereCast ("Thick Ray") instead of Raycast ---
        // This shoots a sphere forward, acting like a thick beam.
        if (Physics.SphereCast(ray, castRadius, out hit, rayDistance, fishLayerMask))
        {
            // Check if we hit a fish (or a child of a fish)
            WorldFishData fishData = hit.collider.GetComponent<WorldFishData>();
            if (fishData == null) fishData = hit.collider.GetComponentInParent<WorldFishData>();

            if (fishData != null)
            {
                if (currentHoveredFish != fishData)
                {
                    currentHoveredFish = fishData;
                    tooltip.ShowWorldTooltip(fishData.fishInfo);
                }
                return; // Found a fish, stop here
            }
        }

        // If we missed, clear the tooltip
        if (currentHoveredFish != null) ClearTooltip();
    }

    private void ClearTooltip()
    {
        if (tooltip != null) tooltip.HideTooltip();
        currentHoveredFish = null;
    }
}