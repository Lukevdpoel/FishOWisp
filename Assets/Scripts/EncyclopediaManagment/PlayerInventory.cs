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
    public Transform bucketFishContainer;
    public GameObject defaultFishPrefab;
    public FishEscalator fishEscalator;

    // This new dictionary links the data to the 3D model
    private Dictionary<CaughtFish, GameObject> physicalFishMap = new Dictionary<CaughtFish, GameObject>();

    [Header("UI References")]
    public VendorUI vendorUI;
    public InventoryUI inventoryUI;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public void AddFish(FishPreset preset)
    {
        if (preset == null) return;

        CaughtFish newFish = new CaughtFish(preset);
        GameObject prefabToSpawn = preset.physicalModelPrefab != null ? preset.physicalModelPrefab : defaultFishPrefab;

        AddFish(newFish, prefabToSpawn);
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

        // --- DEBUG CHECKS ---
        if (physicalPrefab == null)
        {
            Debug.LogError("DEBUG: 'physicalPrefab' IS NULL. Check your FishPreset and DefaultFishPrefab slots.");
        }

        if (bucketFishContainer == null)
        {
            Debug.LogError("DEBUG: 'bucketFishContainer' IS NULL. Check the PlayerInventory Inspector slot.");
        }
        // --- END DEBUG CHECKS ---

        GameObject fishGO = Instantiate(physicalPrefab, bucketFishContainer);

        // --- THIS IS THE CORRECTED ORDER ---

        // 1. Add RigidBody for physics
        Rigidbody rb = fishGO.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;

        // 2. Add a collider (This MUST come before PhysicalFish)
        fishGO.AddComponent<BoxCollider>(); // Or a MeshCollider

        // 3. NOW we can add PhysicalFish, since its Collider requirement is met
        PhysicalFish fishComponent = fishGO.AddComponent<PhysicalFish>();
        fishComponent.Initialize(fishToAdd);

        // --- END OF CORRECTION ---

        // --- THIS IS THE FIX FOR VISIBILITY ---
        int bucketLayer = LayerMask.NameToLayer("BucketUI");

        if (bucketLayer == -1)
        {
            Debug.LogError("FATAL ERROR: The layer 'BucketUI' does not exist. Please create it in Project Settings > Tags and Layers. Fish will not be visible.");
            physicalFishMap[fishToAdd] = fishGO;
            return;
        }

        SetLayerRecursively(fishGO, bucketLayer);
        // ---

        // --- NEW LINE ---
        // Tell the swarm controller to add this new fish
        if (fishEscalator != null) fishEscalator.AddFish(fishGO.transform);
        // ---

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

            if (physicalFishMap.TryGetValue(fishToSell, out GameObject fishGO))
            {
                // --- NEW LINE ---
                // Tell the swarm controller to remove this fish
                if (fishEscalator != null) fishEscalator.RemoveFish(fishGO.transform);
                // ---

                Destroy(fishGO);
                physicalFishMap.Remove(fishToSell);
            }

            if (vendorUI != null)
            {
                vendorUI.UpdateCurrencyDisplay();
            }
        }
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;

        obj.layer = newLayer;

        foreach (Transform child in obj.transform)
        {
            if (child == null) continue;
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }
}