using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class EncyclopediaUIController : MonoBehaviour
{
    [Header("Lifecycle")]
    [Tooltip("The GameObject whose active-in-hierarchy state drives the encyclopedia. " +
             "Typically the Canvas_UI parent under the notebook's encyclopedia page. " +
             "When this is active, the encyclopedia generates its list and accepts clicks. " +
             "When it deactivates (e.g. when the notebook closes or you flip to a different spread), " +
             "the 3D camera shuts off. " +
             "No internal noteMenu / pageFlipper polling — we trust whatever drives this GameObject's active state.")]
    public GameObject lifecycleSource;

    [Header("The Fish List")]
    [Tooltip("Parent the slots stack under — give it a vertical layout (GridLayoutGroup with one " +
             "column, or a VerticalLayoutGroup). One slot per fish spawns here once; only the " +
             "current list page's slots are active, so each page shows its 4-5 rows from the top. " +
             "No scrolling — the fishListNext/fishListPrev flip pages instead.")]
    public Transform gridContentParent;
    [Tooltip("The list-row button prefab with an EncyclopediaGridSlot on the root. Author the " +
             "slot image and name text yourself and wire them to iconImage / nameText on the slot. " +
             "Uncaught fish still get a slot — they render as ??? until caught.")]
    public GameObject gridSlotPrefab;

    [Header("List Paging")]
    [Tooltip("Slots shown per list page (the small in-page flip). 4-5 fits the page height.")]
    public int slotsPerPage = 5;
    [Tooltip("Whether flipping past the last list page wraps back to the first (and vice versa). " +
             "Mirrors NotebookPageFlipper.wrapAround, which stays false for the physical pages.")]
    public bool wrapListPages = false;

    [Header("The Details (Left Page)")]
    public FishEntryUI detailsUI;
    public RawImage fish3DPreview;

    private readonly List<EncyclopediaGridSlot> spawnedSlots = new List<EncyclopediaGridSlot>();
    private PointerEventData pointerData;
    private List<RaycastResult> raycastResults;
    private bool isLive;
    private FishEncyclopediaEntry currentEntry;
    private int listPage = 0;

    public int ListPage => listPage;
    public int ListPageCount => Mathf.Max(1, Mathf.CeilToInt(spawnedSlots.Count / (float)Mathf.Max(1, slotsPerPage)));

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
        // Generate the list immediately, even though Canvas_UI is likely inactive
        // (notebook closed). Slots get parented under the inactive hierarchy and stay
        // dormant until Canvas_UI activates — but critically, they EXIST. Without this,
        // the flipper's bake of the encyclopedia page captures an empty canvas when
        // flipping backward toward it.
        GenerateList();
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

            // The list is vertical: d-pad/stick up-down steps the selection one slot, clamped
            // to the current list page. Left/right and the dedicated fishListNext/fishListPrev
            // bindings flip a whole list page — the small in-page flip, distinct from the big
            // physical page turn (pageNext/pagePrev on NotebookPageFlipper) — and are the ONLY
            // way the fish page changes. Only while the notebook is actually open: isLive should
            // already imply that (Canvas_UI deactivates on close), but the explicit gate
            // guarantees navigation can never reach the encyclopedia from gameplay or from
            // the inventory's slot navigation.
            if (NoteMenu.IsNotebookOpen)
            {
                // Vertical: the character's forward/back movement keys (W/S, fixed — gameplay
                // movement is gated while the notebook is open, so they're free) plus the
                // up/down arrows step the selection, mirroring the d-pad and stick below.
                if (GamepadInput.DpadDownPressed || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
                    MoveSelection(1);
                else if (GamepadInput.DpadUpPressed || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
                    MoveSelection(-1);

                HandleStickNavigation();

                // Rebindable list-page flip (InputBindings: fishListNext/fishListPrev).
                // D-pad left/right double as fixed page-flip alternates, matching how the
                // horizontal axis lost its slot-stepping job when the grid became a list.
                bool listNext = KeyInput.FishListNextPressed || GamepadInput.FishListNextPressed
                                || GamepadInput.DpadRightPressed;
                bool listPrev = KeyInput.FishListPrevPressed || GamepadInput.FishListPrevPressed
                                || GamepadInput.DpadLeftPressed;
                if (listNext) FlipListPage(1);
                else if (listPrev) FlipListPage(-1);
            }
        }
    }

    /// <summary>
    /// Steps the selection up/down WITHIN the current list page, clamping at its first and last
    /// slot — it never changes the page. Page changes only happen through FlipListPage
    /// (fishListNext/fishListPrev and friends), so scrolling to the bottom of a page can't
    /// yank the player onto the next one. Reuses the click path so the details/3D preview
    /// update exactly like a click.
    /// </summary>
    private void MoveSelection(int delta)
    {
        int count = spawnedSlots.Count;
        if (count == 0) return;

        int per = Mathf.Max(1, slotsPerPage);
        int firstOnPage = listPage * per;
        int lastOnPage = Mathf.Min(firstOnPage + per, count) - 1;

        int currentIndex = firstOnPage;
        EncyclopediaGridSlot currentSlot = FindSlotFor(currentEntry);
        if (currentSlot != null) currentIndex = spawnedSlots.IndexOf(currentSlot);
        // A selection left over from another page (shouldn't happen, but cheap to guard)
        // re-enters at the page edge nearest to it.
        currentIndex = Mathf.Clamp(currentIndex, firstOnPage, lastOnPage);

        int nextIndex = Mathf.Clamp(currentIndex + delta, firstOnPage, lastOnPage);
        if (nextIndex == currentIndex) return;

        EncyclopediaGridSlot nextSlot = spawnedSlots[nextIndex];
        if (nextSlot != null) OnSlotClicked(nextSlot.myEntry, nextSlot);
    }

    /// <summary>
    /// The small in-page flip: shows the next/previous list page inside the encyclopedia spread.
    /// The selection stays on the EQUIVALENT row of the new page (row 3 stays row 3), clamped to
    /// the page's last slot when the final page is short. Clamps at the ends unless wrapListPages
    /// is on.
    /// </summary>
    public void FlipListPage(int direction)
    {
        int pageCount = ListPageCount;
        int target = listPage + direction;
        if (wrapListPages) target = ((target % pageCount) + pageCount) % pageCount;
        else target = Mathf.Clamp(target, 0, pageCount - 1);
        if (target == listPage) return;

        // Remember which row of the page is selected before the flip so it carries over.
        int per = Mathf.Max(1, slotsPerPage);
        int row = 0;
        EncyclopediaGridSlot currentSlot = FindSlotFor(currentEntry);
        if (currentSlot != null)
        {
            int currentIndex = spawnedSlots.IndexOf(currentSlot);
            if (currentIndex >= 0) row = currentIndex % per;
        }

        ShowListPage(target);

        int nextIndex = Mathf.Min(target * per + row, spawnedSlots.Count - 1);
        if (nextIndex >= 0 && spawnedSlots[nextIndex] != null)
        {
            EncyclopediaGridSlot slot = spawnedSlots[nextIndex];
            OnSlotClicked(slot.myEntry, slot);
        }
    }

    /// <summary>
    /// Activates exactly the slots belonging to the given list page. Inactive slots are skipped
    /// by the vertical layout, so each page stacks its 4-5 rows from the top. Slots on hidden
    /// pages stay alive (just inactive) — they must EXIST so the flipper's bake of the
    /// encyclopedia page captures the current list page.
    /// </summary>
    private void ShowListPage(int page)
    {
        listPage = Mathf.Clamp(page, 0, ListPageCount - 1);
        int per = Mathf.Max(1, slotsPerPage);
        int first = listPage * per;
        for (int i = 0; i < spawnedSlots.Count; i++)
        {
            EncyclopediaGridSlot slot = spawnedSlots[i];
            if (slot == null) continue;
            bool active = i >= first && i < first + per;
            if (slot.gameObject.activeSelf != active) slot.gameObject.SetActive(active);
        }
    }

    /// <summary>
    /// Left-stick list navigation. The stick holds its deflection, so each axis steps once on
    /// crossing the nav threshold and won't step again until it relaxes below the release
    /// threshold. Vertical steps the selection (stick up = previous slot, matching the d-pad);
    /// horizontal flips a whole list page. The dominant axis wins so a diagonal push doesn't
    /// fire both.
    /// </summary>
    private void HandleStickNavigation()
    {
        Vector2 s = GamepadInput.Move;

        if (Mathf.Abs(s.x) >= Mathf.Abs(s.y))
        {
            if (stickReadyX && Mathf.Abs(s.x) > StickNavThreshold)
            {
                FlipListPage(s.x > 0 ? 1 : -1);
                stickReadyX = false;
            }
        }
        else
        {
            if (stickReadyY && Mathf.Abs(s.y) > StickNavThreshold)
            {
                MoveSelection(s.y > 0 ? -1 : 1);
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
            if (spawnedSlots.Count == 0) GenerateList();

            if (spawnedSlots.Count > 0)
            {
                FishEncyclopediaEntry target = currentEntry != null ? currentEntry : spawnedSlots[0].myEntry;
                EncyclopediaGridSlot slot = FindSlotFor(target) ?? spawnedSlots[0];
                // Re-show the list page the restored selection lives on — Canvas_UI reactivating
                // brings back every slot's own activeSelf state, so this also re-hides the pages
                // that shouldn't be visible.
                ShowListPage(spawnedSlots.IndexOf(slot) / Mathf.Max(1, slotsPerPage));
                // Re-run Setup so a slot's ??? lifts the moment its fish gets caught between opens.
                foreach (var s in spawnedSlots) if (s != null) s.Setup(s.myEntry, this);
                OnSlotClicked(slot.myEntry, slot);
            }
        }
        else
        {
            // Don't clear the list here — slots persist so the flipper's bake during a backward
            // flip toward the encyclopedia captures the populated page. They deactivate
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

    void GenerateList()
    {
        ClearList();

        if (FishEncyclopediaManager.Instance == null) return;
        var entries = FishEncyclopediaManager.Instance.encyclopediaEntries;

        foreach (var entry in entries)
        {
            GameObject newSlotObj = Instantiate(gridSlotPrefab, gridContentParent);
            EncyclopediaGridSlot slot = newSlotObj.GetComponent<EncyclopediaGridSlot>();
            slot.Setup(entry, this);
            spawnedSlots.Add(slot);
        }

        // Start on the first list page; the rest of the slots go dormant until flipped to.
        ShowListPage(0);
    }

    void ClearList()
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
