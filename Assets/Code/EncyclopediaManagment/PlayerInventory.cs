using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PlayerInventory : MonoBehaviour
{
    public static PlayerInventory Instance { get; private set; }

    [Header("Data")]
    public int inventorySize = 24;
    public int currentCurrency = 100;
    public List<CaughtFish> caughtFishes = new List<CaughtFish>();

    [Header("Physical References")]
    public Transform bucketFishContainer; // Assign the empty GameObject inside your bucket
    public GameObject defaultFishPrefab;  // A default 3D fish model to spawn

    // This new dictionary links the data to the 3D model
    private Dictionary<CaughtFish, GameObject> physicalFishMap = new Dictionary<CaughtFish, GameObject>();

    // --- You will need a reference to your new Vendor UI ---
    public VendorUI vendorUI;

    // We can keep the old UI for 'Viewing' mode if you want
    public InventoryUI oldInventoryUI;


    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public void AddFish(FishPreset preset)
    {
        if (preset == null) return;
        CaughtFish newFish = new CaughtFish(preset);
        AddFish(newFish, preset.physicalModelPrefab); // Pass the prefab
    }

    public void AddFish(CaughtFish fishToAdd, GameObject physicalPrefab = null)
    {
        if (caughtFishes.Count >= inventorySize)
        {
            Debug.Log("Inventory is full! Cannot add fish.");
            return;
        }
        if (fishToAdd == null) return;

        // 1. Add to data list
        caughtFishes.Add(fishToAdd);

        // 2. Spawn the physical 3D model
        GameObject fishGO = Instantiate(physicalPrefab != null ? physicalPrefab : defaultFishPrefab, bucketFishContainer);

        // --- This part is key ---
        // Add the PhysicalFish component and link the data
        PhysicalFish fishComponent = fishGO.AddComponent<PhysicalFish>();
        fishComponent.Initialize(fishToAdd);

        // Add RigidBody for physics (you can tweak settings)
        Rigidbody rb = fishGO.AddComponent<Rigidbody>();
        rb.useGravity = true; // Or false if you manage position
        rb.isKinematic = false; // Let it fall into the bucket

        // Add a collider
        fishGO.AddComponent<BoxCollider>(); // Or a MeshCollider

        // 3. Add to our tracking dictionary
        physicalFishMap[fishToAdd] = fishGO;
    }

    // SellFish is now called by the VendorUI
    public void SellFish(CaughtFish fishToSell)
    {
        if (fishToSell != null && caughtFishes.Contains(fishToSell))
        {
            currentCurrency += fishToSell.GetValue();
            caughtFishes.Remove(fishToSell);

            // --- Destroy the physical model ---
            if (physicalFishMap.TryGetValue(fishToSell, out GameObject fishGO))
            {
                Destroy(fishGO);
                physicalFishMap.Remove(fishToSell);
            }

            // We update the vendor UI, not the old inventory UI
            if (vendorUI != null)
            {
                vendorUI.UpdateCurrencyDisplay();
            }
        }
    }

    // This method is no longer used by the old InventoryUI
    // It will be replaced by logic in VendorUI
    /*
    public void SellAllFish()
    {
        // This logic will move to VendorUI.cs
    }
    */
}