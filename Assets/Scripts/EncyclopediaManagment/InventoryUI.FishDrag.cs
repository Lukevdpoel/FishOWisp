using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Part of InventoryUI (partial class). Serialized fields live in InventoryUI.cs.
public partial class InventoryUI
{
    private void HandleFishDrag()
    {
        if (currentDraggedModel != null)
        {
            // 1. Release Logic
            if (Input.GetMouseButtonUp(0))
            {
                TryDropFish();
                return;
            }

            // 2. Position Logic
            Ray ray = ActiveCamera.ScreenPointToRay(Input.mousePosition);
            currentDraggedModel.transform.position = ray.GetPoint(modelDistance);

            // 3. Rotation — unscaled so the showcase spin keeps turning if the game
            // is paused (e.g. dragging while the notebook holds Time.timeScale at 0).
            currentDraggedModel.transform.Rotate(Vector3.up, 30f * Time.unscaledDeltaTime);
        }
    }

    private void TryDropFish()
    {
        if (currentDraggedModel == null) return;

        bool actionTaken = false;

        // 1. Check UI Block (Is mouse over a UI element?)
        if (EventSystem.current.IsPointerOverGameObject())
        {
            Debug.Log("Released over UI. Cancelled.");
            actionTaken = false;
        }
        else
        {
            // 2. Check World Block
            Ray ray = ActiveCamera.ScreenPointToRay(Input.mousePosition);

            // Calculate mask: All layers EXCEPT the ignore list.
            int finalMask = ~raycastIgnoreLayers.value;

            // We cast against this mask.
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, finalMask, QueryTriggerInteraction.Collide))
            {
                int hitLayer = hit.collider.gameObject.layer;

                // --- CASE A: HIT WATER (RELEASE) ---
                if (((1 << hitLayer) & waterLayerMask) != 0)
                {
                    Debug.Log($"Hit Water: {hit.collider.name}. Releasing fish.");

                    // --- SPAWN RELEASE PREFAB ---
                    if (releasePrefab != null)
                    {
                        // Use the prefab's OWN rotation, not Quaternion.identity (which forces 0,0,0)
                        GameObject effect = Instantiate(releasePrefab, hit.point, releasePrefab.transform.rotation);

                        // Destroy it after the specified time
                        Destroy(effect, releasePrefabLifetime);
                    }
                    // ----------------------------

                    if (PlayerInventory.Instance != null && currentDraggedFishData != null)
                    {
                        PlayerInventory.Instance.RemoveFish(currentDraggedFishData);
                        actionTaken = true;
                    }
                }
                // --- CASE B: HIT VENDOR (SELL) ---
                else if (((1 << hitLayer) & vendorLayerMask) != 0)
                {
                    Debug.Log($"Hit Vendor: {hit.collider.name}. Selling fish.");

                    // Try to find the Vendor script on the object or its parent
                    FishVendor vendor = hit.collider.GetComponent<FishVendor>();
                    if (vendor == null) vendor = hit.collider.GetComponentInParent<FishVendor>();

                    if (vendor != null && PlayerInventory.Instance != null)
                    {
                        // 1. Give money
                        vendor.SellFishToVendor(currentDraggedFishData);
                        // 2. Remove fish
                        PlayerInventory.Instance.RemoveFish(currentDraggedFishData);
                        actionTaken = true;
                    }
                }
                // --- CASE C: HIT TANK (DISPLAY) ---
                else if (((1 << hitLayer) & tankLayerMask) != 0)
                {
                    Debug.Log($"Hit Tank: {hit.collider.name}. Adding fish to tank.");

                    // Look for the NEW DropZone script
                    FishTankDropZone dropZone = hit.collider.GetComponent<FishTankDropZone>();
                    if (dropZone == null) dropZone = hit.collider.GetComponentInParent<FishTankDropZone>();

                    if (dropZone != null && PlayerInventory.Instance != null)
                    {
                        // Pass the data to the drop zone
                        dropZone.ReceiveFish(currentDraggedFishData);

                        // Remove from player inventory
                        PlayerInventory.Instance.RemoveFish(currentDraggedFishData);
                        actionTaken = true;
                    }
                }
                // --- CASE D: HIT BOUNTY BOARD (DELIVER) ---
                else if (((1 << hitLayer) & bountyLayerMask) != 0)
                {
                    Debug.Log($"Hit Bounty Board: {hit.collider.name}. Checking delivery.");

                    BountyBoard board = hit.collider.GetComponent<BountyBoard>();
                    if (board == null) board = hit.collider.GetComponentInParent<BountyBoard>();

                    if (board != null && PlayerInventory.Instance != null)
                    {
                        // The board checks if it needs this specific fish
                        if (board.TryDeliverFish(currentDraggedFishData))
                        {
                            PlayerInventory.Instance.RemoveFish(currentDraggedFishData);
                            actionTaken = true;
                        }
                        else
                        {
                            Debug.Log("The Bounty Board doesn't need this fish right now.");
                        }
                    }
                }
                else
                {
                    Debug.Log($"Blocked by Obstacle: {hit.collider.name} (Layer: {LayerMask.LayerToName(hitLayer)})");
                }
            }
            else
            {
                Debug.Log("Raycast hit nothing (skybox?).");
            }
        }

        if (!actionTaken)
        {
            Debug.Log("Cancelled. Fish returned to inventory.");
        }

        // Always cleanup the drag model
        Destroy(currentDraggedModel);
        currentDraggedModel = null;
        currentDraggedFishData = null;
    }

    private void StartDraggingFish(CaughtFish fish)
    {
        if (currentDraggedModel != null) return;

        currentDraggedFishData = fish;

        if (fish.preset.fishPrefab != null)
        {
            currentDraggedModel = Instantiate(fish.preset.fishPrefab);

            Ray ray = ActiveCamera.ScreenPointToRay(Input.mousePosition);
            currentDraggedModel.transform.position = ray.GetPoint(modelDistance);

            currentDraggedModel.transform.rotation = Quaternion.identity;
            currentDraggedModel.transform.localScale = Vector3.one * modelScale;

            // Remove physics components so the drag model doesn't block rays or fall
            foreach (var rb in currentDraggedModel.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (var col in currentDraggedModel.GetComponentsInChildren<Collider>()) Destroy(col);
        }
    }

    // --- Loadout navigation ---
    // Two screens: Bobber (cycle the bobber/lure on the rod, live) and Bait (cycle the
    // equipped bait, live). Left/right cycles within a screen; down goes Bobber→Bait (only
    // from a regular bobber — lures take no bait), up goes Bait→Bobber. The selection is
    // applied immediately, so the bobber/lure model swaps and the bars re-highlight without
    // any confirm button.
    private readonly List<BaitItem> cyclableBaits = new List<BaitItem>();
    // "No bait" (null) + cyclable baits — the stops CycleBait steps through (see BaitBarUI parity).
    private readonly List<BaitItem> cycleBaitStops = new List<BaitItem>();

}
