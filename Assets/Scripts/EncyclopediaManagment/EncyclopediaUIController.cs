using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class EncyclopediaUIController : MonoBehaviour
{
    [Header("Main Container")]
    [Tooltip("The parent GameObject holding all encyclopedia UI elements.")]
    public GameObject uiContainer;

    [Header("The Raster (Grid)")]
    [Tooltip("The 'Content' object inside your Scroll View.")]
    public Transform gridContentParent;
    [Tooltip("The prefab with the EncyclopediaGridSlot script attached.")]
    public GameObject gridSlotPrefab;

    [Header("The Details (Left Side)")]
    [Tooltip("Reference to the script that shows text stats.")]
    public FishEntryUI detailsUI;
    [Tooltip("The RawImage that displays the Render Texture (3D Fish).")]
    public RawImage fish3DPreview;

    private List<EncyclopediaGridSlot> spawnedSlots = new List<EncyclopediaGridSlot>();

    /// <summary>
    /// Called by NoteMenu.cs to turn the UI on/off
    /// </summary>
    public void SetUIState(bool isOpen)
    {
        if (uiContainer != null) uiContainer.SetActive(isOpen);

        if (isOpen)
        {
            GenerateRaster();

            // Start with details hidden until player clicks a specific fish
            if (detailsUI != null) detailsUI.gameObject.SetActive(false);
            if (fish3DPreview != null) fish3DPreview.gameObject.SetActive(false);
        }
    }

    void GenerateRaster()
    {
        // 1. Clear old buttons to prevent duplicates
        foreach (var slot in spawnedSlots)
        {
            if (slot != null) Destroy(slot.gameObject);
        }
        spawnedSlots.Clear();

        // 2. Get all known fish entries from the manager
        // Ensure FishEncyclopediaManager is in the scene!
        if (FishEncyclopediaManager.Instance == null) return;

        var entries = FishEncyclopediaManager.Instance.encyclopediaEntries;

        // 3. Create a button for each fish
        foreach (var entry in entries)
        {
            GameObject newSlotObj = Instantiate(gridSlotPrefab, gridContentParent);
            EncyclopediaGridSlot slot = newSlotObj.GetComponent<EncyclopediaGridSlot>();

            // Pass 'this' controller so the slot can call OnSlotClicked back here
            slot.Setup(entry, this);
            spawnedSlots.Add(slot);
        }
    }

    /// <summary>
    /// Called when a button in the grid is clicked
    /// </summary>
    public void OnSlotClicked(FishEncyclopediaEntry entry, EncyclopediaGridSlot selectedSlot)
    {
        // 1. Visual Selection Update (Highlight the clicked one, unhighlight others)
        foreach (var slot in spawnedSlots) slot.Deselect();
        selectedSlot.Select();

        // 2. Logic: If caught, show details. If not, show nothing or ???
        if (entry.hasCaught > 0)
        {
            // Show Details and 3D View
            if (detailsUI != null)
            {
                detailsUI.gameObject.SetActive(true);
                detailsUI.Populate(entry); // Update text fields
            }

            if (fish3DPreview != null) fish3DPreview.gameObject.SetActive(true);

            // Update the off-screen 3D model
            if (ModelViewer.Instance != null)
            {
                ModelViewer.Instance.ShowModel(entry.preset);
            }
        }
        else
        {
            // If we haven't caught it yet, keep details hidden
            if (detailsUI != null) detailsUI.gameObject.SetActive(false);
            if (fish3DPreview != null) fish3DPreview.gameObject.SetActive(false);
        }
    }
}