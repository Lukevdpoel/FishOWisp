using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class EncyclopediaUIController : MonoBehaviour
{
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

    // Left-stick navigation re-arm gates. The stick is analog and holds a deflection for many
    // frames, so we step the selection once when it crosses the nav threshold, then require it
    // to fall back below the release threshold before it can step again (one move per push).
    private const float StickNavThreshold = 0.5f;
    private const float StickReleaseThreshold = 0.3f;
    private bool stickReadyX = true;
    private bool stickReadyY = true;

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

        if (isLive)
        {
            if (Input.GetMouseButtonDown(0)) CheckForSlotClick();

            // D-pad + left stick move the selection AROUND the raster grid — left/right steps
            // one slot, up/down jumps a full row — but only while the notebook is actually open.
            // isLive should already imply that (Canvas_UI deactivates on close), but the explicit
            // gate guarantees navigation can never reach the encyclopedia from gameplay or from
            // the inventory's slot navigation.
            if (NoteMenu.IsNotebookOpen)
            {
                if (GamepadInput.DpadRightPressed) MoveSelection(1, 0);
                else if (GamepadInput.DpadLeftPressed) MoveSelection(-1, 0);
                else if (GamepadInput.DpadDownPressed) MoveSelection(0, 1);
                else if (GamepadInput.DpadUpPressed) MoveSelection(0, -1);

                HandleStickNavigation();
            }
        }
    }

    /// <summary>
    /// Moves the selection around the raster. dx steps one slot left/right in reading order
    /// (wrapping so every fish stays reachable). dy moves a whole row up/down, keeping the same
    /// column, so "down" actually drops to the slot below on the page. Reuses the click path so
    /// the details/3D preview update exactly like a click.
    /// </summary>
    private void MoveSelection(int dx, int dy)
    {
        int count = spawnedSlots.Count;
        if (count == 0) return;

        int currentIndex = 0;
        EncyclopediaGridSlot currentSlot = FindSlotFor(currentEntry);
        if (currentSlot != null) currentIndex = spawnedSlots.IndexOf(currentSlot);

        int cols = GetColumnCount();
        int nextIndex = currentIndex;

        if (dx != 0)
        {
            // Left/right walk the raster in reading order, wrapping at the ends.
            nextIndex = ((currentIndex + dx) % count + count) % count;
        }
        else if (dy != 0)
        {
            // The grid fills rows of `cols` slots left-to-right, so the slot directly below is
            // `cols` indices further on. Move by a full row, keeping the column, and wrap rows.
            int rows = Mathf.CeilToInt(count / (float)cols);
            int col = currentIndex % cols;
            int row = currentIndex / cols;
            int newRow = ((row + dy) % rows + rows) % rows;
            nextIndex = newRow * cols + col;
            // The last row can be partial: if this column doesn't exist there, land on its last slot.
            if (nextIndex >= count) nextIndex = count - 1;
        }

        EncyclopediaGridSlot nextSlot = spawnedSlots[nextIndex];
        if (nextSlot != null) OnSlotClicked(nextSlot.myEntry, nextSlot);
    }

    /// <summary>
    /// Slots per visual row. Read straight off the GridLayoutGroup so it stays correct regardless
    /// of the page's tiny canvas-unit scale (cells are ~0.08 units here, not pixels — position
    /// math with pixel-sized tolerances silently breaks). Handles the fixed-column, fixed-row and
    /// flexible constraint modes; defaults to 4 (the grid's authored column count) if absent.
    /// </summary>
    private int GetColumnCount()
    {
        int count = spawnedSlots.Count;
        GridLayoutGroup grid = gridContentParent != null ? gridContentParent.GetComponent<GridLayoutGroup>() : null;

        if (grid != null)
        {
            if (grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
                return Mathf.Max(1, grid.constraintCount);

            if (grid.constraint == GridLayoutGroup.Constraint.FixedRowCount && grid.constraintCount > 0)
                return Mathf.Max(1, Mathf.CeilToInt(count / (float)grid.constraintCount));

            // Flexible: columns follow the laid-out width. Derive from the content rect and cell pitch.
            RectTransform rt = grid.transform as RectTransform;
            if (rt != null)
            {
                float pitch = grid.cellSize.x + grid.spacing.x;
                float usable = rt.rect.width - grid.padding.left - grid.padding.right + grid.spacing.x;
                if (pitch > 0f) return Mathf.Clamp(Mathf.FloorToInt(usable / pitch), 1, Mathf.Max(1, count));
            }
        }

        return 4; // authored column count fallback
    }

    /// <summary>
    /// Left-stick grid navigation. The stick holds its deflection, so each axis steps the
    /// selection once on crossing the nav threshold and won't step again until it relaxes below
    /// the release threshold. The dominant axis wins so a diagonal push doesn't fire both.
    /// Stick up is +y but moves UP a row (dy = -1), matching the d-pad.
    /// </summary>
    private void HandleStickNavigation()
    {
        Vector2 s = GamepadInput.Move;

        if (Mathf.Abs(s.x) >= Mathf.Abs(s.y))
        {
            if (stickReadyX && Mathf.Abs(s.x) > StickNavThreshold)
            {
                MoveSelection(s.x > 0 ? 1 : -1, 0);
                stickReadyX = false;
            }
        }
        else
        {
            if (stickReadyY && Mathf.Abs(s.y) > StickNavThreshold)
            {
                MoveSelection(0, s.y > 0 ? -1 : 1);
                stickReadyY = false;
            }
        }

        if (Mathf.Abs(s.x) < StickReleaseThreshold) stickReadyX = true;
        if (Mathf.Abs(s.y) < StickReleaseThreshold) stickReadyY = true;
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

        // Uncaught fish render as mystery entries — "???" name/preferences and a black
        // silhouette model — so every slot populates; FishEntryUI decides what to reveal.
        if (detailsUI != null) detailsUI.Populate(entry);
        // fish3DPreview stays as-is; ModelViewer.ShowModel inside Populate populates the RT.
    }
}
