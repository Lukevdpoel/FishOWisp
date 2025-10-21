using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class VendorUI : MonoBehaviour
{
    [Header("Staging Area")]
    public Transform stagingAreaContainer; // A layout group to hold fish
    public GameObject stagingSlotPrefab; // A new UI prefab for the list
    public Button sellStagedButton;
    public TextMeshProUGUI currencyText;

    private List<PhysicalFish> stagedFish = new List<PhysicalFish>();

    void Start()
    {
        sellStagedButton.onClick.AddListener(SellStagedFish);
        UpdateCurrencyDisplay();
    }

    public void AddFishToStaging(PhysicalFish fish)
    {
        if (stagedFish.Contains(fish)) return; // Already staged

        // Add to our list
        stagedFish.Add(fish);

        // Disable the 3D model in the bucket
        fish.gameObject.SetActive(false);

        // Create a UI slot in the staging area
        GameObject slotGO = Instantiate(stagingSlotPrefab, stagingAreaContainer);
        // --- This new "StagingSlotUI" script is very simple ---
        // It just needs to display the fish name/value
        // And maybe have a button to "return" the fish to the bucket
        StagingSlotUI slotUI = slotGO.GetComponent<StagingSlotUI>();
        slotUI.Populate(fish.FishData);
        // You'll need to create this simple StagingSlotUI script.
    }

    void SellStagedFish()
    {
        if (stagedFish.Count == 0) return;

        foreach (PhysicalFish fish in stagedFish)
        {
            // Tell the PlayerInventory to sell the data and destroy the GO
            PlayerInventory.Instance.SellFish(fish.FishData);
        }

        // Clear the staging list
        stagedFish.Clear();

        // Clear the UI slots
        foreach (Transform child in stagingAreaContainer)
        {
            Destroy(child.gameObject);
        }

        UpdateCurrencyDisplay();
    }

    public void UpdateCurrencyDisplay()
    {
        if (currencyText != null)
        {
            currencyText.text = $"Coins: {PlayerInventory.Instance.currentCurrency}";
        }
    }
}