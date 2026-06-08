using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class EncyclopediaUIController : MonoBehaviour
{
#if UNITY_EDITOR
    [Header("Debug")]
    public bool debugIgnoreCaughtRequirement = true;
#endif

    [Header("Lifecycle")]
    [Tooltip("The GameObject whose active-in-hierarchy state drives the encyclopedia. " +
             "Typically the Canvas_UI parent under the notebook's encyclopedia page. " +
             "When this is active, the encyclopedia generates its raster and accepts clicks. " +
             "When it deactivates (e.g. when the notebook closes or you flip to a different spread), " +
             "the raster clears and the 3D camera shuts off. " +
             "No internal noteMenu / pageFlipper polling — we trust whatever drives this GameObject's active state.")]
    public GameObject lifecycleSource;

    [Header("The Raster (Grid)")]
    public Transform gridContentParent;
    public GameObject gridSlotPrefab;

    [Header("The Details (Left Page)")]
    public FishEntryUI detailsUI;
    public RawImage fish3DPreview;

    private readonly List<EncyclopediaGridSlot> spawnedSlots = new List<EncyclopediaGridSlot>();
    private PointerEventData pointerData;
    private List<RaycastResult> raycastResults;
    private bool isLive;
    private FishEncyclopediaEntry currentEntry;

    private void Start()
    {
        pointerData = new PointerEventData(EventSystem.current);
        raycastResults = new List<RaycastResult>();
        // Generate the raster immediately, even though Canvas_UI is likely inactive
        // (notebook closed). Slots get parented under the inactive hierarchy and stay
        // dormant until Canvas_UI activates — but critically, they EXIST. Without this,
        // the flipper's bake of the encyclopedia page captures an empty canvas when
        // flipping backward toward it (since the raster was previously cleared).
        GenerateRaster();
        // Sync initial state with the lifecycle source so we don't briefly run with the wrong polarity.
        ApplyLiveState(ShouldBeLive());
    }

    private void Update()
    {
        bool shouldBeLive = ShouldBeLive();
        if (shouldBeLive != isLive)
        {
            ApplyLiveState(shouldBeLive);
        }

        if (isLive && Input.GetMouseButtonDown(0))
        {
            CheckForSlotClick();
        }
    }

    private bool ShouldBeLive()
    {
        return lifecycleSource != null && lifecycleSource.activeInHierarchy;
    }

    private void ApplyLiveState(bool live)
    {
        isLive = live;
        if (live)
        {
            // Fallback: if Start() ran before FishEncyclopediaManager was ready and no
            // slots got created, try again now.
            if (spawnedSlots.Count == 0) GenerateRaster();

            if (spawnedSlots.Count > 0)
            {
                FishEncyclopediaEntry target = currentEntry != null ? currentEntry : spawnedSlots[0].myEntry;
                EncyclopediaGridSlot slot = FindSlotFor(target) ?? spawnedSlots[0];
                OnSlotClicked(slot.myEntry, slot);
            }
        }
        else
        {
            // Don't ClearRaster here — slots persist so the flipper's bake during a backward
            // flip toward the encyclopedia captures the populated raster. They deactivate
            // naturally with Canvas_UI's hierarchy and reactivate when Canvas_UI does.
            // Details panel + 3D RawImage similarly follow Canvas_UI via inheritance.
            // Only the ModelViewer camera (outside Canvas_UI's hierarchy) needs explicit shutdown.
            if (ModelViewer.Instance != null) ModelViewer.Instance.HideViewer();
        }
    }

    private EncyclopediaGridSlot FindSlotFor(FishEncyclopediaEntry entry)
    {
        if (entry == null) return null;
        foreach (var slot in spawnedSlots)
        {
            if (slot != null && slot.myEntry == entry) return slot;
        }
        return null;
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

    void GenerateRaster()
    {
        ClearRaster();

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

    void ClearRaster()
    {
        foreach (var slot in spawnedSlots) if (slot != null) Destroy(slot.gameObject);
        spawnedSlots.Clear();
    }

    public void OnSlotClicked(FishEncyclopediaEntry entry, EncyclopediaGridSlot selectedSlot)
    {
        foreach (var slot in spawnedSlots) slot.Deselect();
        if (selectedSlot != null) selectedSlot.Select();
        currentEntry = entry;

        bool showDetails = entry.hasCaught > 0;
#if UNITY_EDITOR
        showDetails = debugIgnoreCaughtRequirement || showDetails;
#endif
        if (showDetails)
        {
            if (detailsUI != null) detailsUI.Populate(entry);
            // fish3DPreview stays as-is; ModelViewer.ShowModel inside Populate populates the RT.
        }
        else
        {
            // No SetActive(false) on the panels — they're children of Canvas_UI and follow its
            // active state via hierarchy. For an uncaught fish we just hide the model camera
            // and leave the panel showing whatever it last showed. If you want a true "mystery
            // mode" placeholder, extend FishEntryUI.Populate to accept a null/uncaught entry
            // and render "???" placeholders — that's a UI concern, not a lifecycle concern.
            if (ModelViewer.Instance != null) ModelViewer.Instance.HideViewer();
        }
    }
}
