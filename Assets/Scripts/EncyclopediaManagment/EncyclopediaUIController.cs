using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class EncyclopediaUIController : MonoBehaviour
{
    [Header("Debug")]
    public bool debugIgnoreCaughtRequirement = true;

    [Header("Main Container")]
    public GameObject uiContainer;

    [Header("The Raster (Grid)")]
    public Transform gridContentParent;
    public GameObject gridSlotPrefab;

    [Header("The Details (Left Side)")]
    public FishEntryUI detailsUI;
    public RawImage fish3DPreview;

    private List<EncyclopediaGridSlot> spawnedSlots = new List<EncyclopediaGridSlot>();
    private PointerEventData pointerData;
    private List<RaycastResult> raycastResults;

    private void Start()
    {
        pointerData = new PointerEventData(EventSystem.current);
        raycastResults = new List<RaycastResult>();
    }

    private void Update()
    {
        if (uiContainer != null && uiContainer.activeSelf)
        {
            if (Input.GetMouseButtonDown(0))
            {
                CheckForSlotClick();
            }
        }
    }

    private void CheckForSlotClick()
    {
        pointerData.position = Input.mousePosition;
        raycastResults.Clear();
        EventSystem.current.RaycastAll(pointerData, raycastResults);

        foreach (var result in raycastResults)
        {
            EncyclopediaGridSlot slot = result.gameObject.GetComponent<EncyclopediaGridSlot>();
            if (slot == null) slot = result.gameObject.GetComponentInParent<EncyclopediaGridSlot>();

            if (slot != null)
            {
                OnSlotClicked(slot.myEntry, slot);
                return;
            }
        }
    }

    public void SetUIState(bool isOpen)
    {
        if (uiContainer != null) uiContainer.SetActive(isOpen);

        if (isOpen)
        {
            GenerateRaster();
            if (spawnedSlots.Count > 0)
            {
                OnSlotClicked(spawnedSlots[0].myEntry, spawnedSlots[0]);
            }
        }
    }

    void GenerateRaster()
    {
        foreach (var slot in spawnedSlots) if (slot != null) Destroy(slot.gameObject);
        spawnedSlots.Clear();

        if (FishEncyclopediaManager.Instance == null) return;
        var entries = FishEncyclopediaManager.Instance.encyclopediaEntries;

        foreach (var entry in entries)
        {
            GameObject newSlotObj = Instantiate(gridSlotPrefab, gridContentParent);
            EncyclopediaGridSlot slot = newSlotObj.GetComponent<EncyclopediaGridSlot>();
            slot.Setup(entry, this);
            spawnedSlots.Add(slot);
        }
    }

    public void OnSlotClicked(FishEncyclopediaEntry entry, EncyclopediaGridSlot selectedSlot)
    {
        foreach (var slot in spawnedSlots) slot.Deselect();
        if (selectedSlot != null) selectedSlot.Select();

        if (debugIgnoreCaughtRequirement || entry.hasCaught > 0)
        {
            if (detailsUI != null)
            {
                detailsUI.gameObject.SetActive(true);
                detailsUI.Populate(entry);
            }

            if (fish3DPreview != null) fish3DPreview.gameObject.SetActive(true);
        }
        else
        {
            if (detailsUI != null) detailsUI.gameObject.SetActive(false);
            if (fish3DPreview != null) fish3DPreview.gameObject.SetActive(false);
        }
    }
}