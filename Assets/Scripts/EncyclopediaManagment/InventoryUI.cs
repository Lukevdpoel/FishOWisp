using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    public static bool IsInventoryOpen { get; private set; }

    // True only while the player-driven gear menu (loadout cycling) is open — false for
    // programmatic opens (vendor shop, bait-missing prompt). The bobber/bait selectors read this
    // to decide their layout: in loadout mode only the dimension you're steering is shown, while
    // a programmatic open shows whatever is relevant (e.g. bait for the bait-missing prompt).
    public static bool IsLoadoutActive { get; private set; }

    // Self-registering static instance. Set in Awake, cleared in OnDestroy. Lets other
    // systems (FishingRodController, PauseMenu, FishVendor, BaitShopUI) reach the
    // inventory without ever calling FindFirstObjectByType — which was returning null
    // intermittently for the bait-missing prompt at cast time, even with include-inactive.
    public static InventoryUI Instance { get; private set; }

    // Reset statics at the start of every play session, before any Awake fires.
    // Required when "Reload Domain" is disabled in Enter Play Mode Settings — without
    // this, Instance keeps pointing at a destroyed object from the previous play and
    // IsInventoryOpen lies. Symptom: "everything works once per play session, then breaks."
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        IsInventoryOpen = false;
        IsLoadoutActive = false;
    }

    // The toggle key lives on the InputBindings asset (keyboardMouse.inventoryToggle) — read via KeyInput.

    [Header("Main References")]
    public GameObject uiPanel;
    public Transform physicsContainer;
    public GameObject inventorySlotPrefab;

    [Header("Tooltip System")]
    public InventoryTooltip tooltip;

    [Header("Layout Settings")]
    [Tooltip("Horizontal spacing between fish slots in pixels.")]
    public float slotSpacing = 120f;
    [Tooltip("Pixels of left padding inside the container before the first slot.")]
    public float leftPadding = 40f;

    [Header("3D Inspection Settings")]
    public float modelDistance = 3.5f;
    public float modelScale = 1.0f;

    [Header("Drop Zones")]
    [Tooltip("Layer for water (Release fish).")]
    public LayerMask waterLayerMask;

    [Tooltip("Layer for vendors (Sell fish).")]
    public LayerMask vendorLayerMask;

    [Tooltip("Layer for tanks/aquariums (Display fish).")]
    public LayerMask tankLayerMask;

    [Tooltip("Layer for bounty boards (Deliver fish).")]
    public LayerMask bountyLayerMask;

    [Tooltip("Layers that the raycast should ignore completely (e.g. Player, UI, TriggerZones).")]
    public LayerMask raycastIgnoreLayers;

    [Header("Release Visuals")]
    [Tooltip("Prefab to spawn when releasing a fish (e.g. Splash VFX or a Fish animation).")]
    public GameObject releasePrefab;

    [Tooltip("How long (in seconds) before the release prefab is automatically destroyed.")]
    public float releasePrefabLifetime = 3.0f;

    [Header("Interaction Control")]
    public List<MonoBehaviour> scriptsToDisableInput = new List<MonoBehaviour>();
    public Button closeButton;

    [Tooltip("Clicking anywhere outside the inventory panel closes it. The bait bar is " +
             "always treated as 'inside'. Add any other UI roots here that should also keep " +
             "the inventory open when clicked.")]
    public List<RectTransform> additionalClickThroughRoots = new List<RectTransform>();

    [Header("Loadout Selector")]
    [Tooltip("When off, the caught-fish grid is hidden and this menu shows only the " +
             "bobber/lure and bait rows. The fish drag/sell code stays intact but dormant.")]
    [SerializeField] private bool showCaughtFish = false;

    [Tooltip("Hold the inventory button to open the gear menu; release to close. Left/right " +
             "cycles bobber/lure (live), down opens the bait sub-screen from a bobber, up returns.")]
    [SerializeField] private bool holdToOpenLoadout = true;

    [Tooltip("The player's visual model — used to place the framing camera in front of the " +
             "angler. Falls back to the PlayerController transform if left empty.")]
    [SerializeField] private Transform playerModel;

    [Header("Loadout Camera (while held open)")]
    [Tooltip("How far below the rod tip the tackle dangles. The camera aims here.")]
    [SerializeField] private float dangleDrop = 0.4f;
    [Tooltip("How far in FRONT of the dangling tackle (away from the player) the camera sits, " +
             "so it looks back at the angler.")]
    [SerializeField] private float cameraFrontDistance = 2.5f;
    [Tooltip("Height of the camera above the tackle.")]
    [SerializeField] private float cameraHeightOffset = 0.5f;
    [Tooltip("Vertical offset of the point the camera looks at, relative to the tackle.")]
    [SerializeField] private float cameraLookHeightOffset = 0.15f;
    [Tooltip("How fast the camera eases into the framing pose. Higher = snappier.")]
    [SerializeField] private float cameraLerpSpeed = 10f;

    private List<InventorySlotUI> spawnedSlots = new List<InventorySlotUI>();
    private Camera cachedCamera;

    // Returns the active main camera. The cached ref can go stale when something else swaps the
    // MainCamera tag at runtime (e.g. ShopDoorController switching to an interior camera), so we
    // refresh whenever the cached camera becomes null or inactive.
    private Camera ActiveCamera
    {
        get
        {
            if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
                cachedCamera = Camera.main;
            return cachedCamera;
        }
    }

    private GameObject currentDraggedModel;
    private CaughtFish currentDraggedFishData;

    // For closing the notebook when the inventory opens (mutual exclusion). Lives on the
    // same GameObject in the PlayerMaster prefab; the scene-wide find is just a fallback.
    private NoteMenu noteMenu;

    // Click-away-to-close support.
    private PointerEventData clickPointerData;
    private List<RaycastResult> clickRaycastResults;
    private BaitBarUI baitBar;
    private BobberBarUI bobberBar;
    private int openedFrame = -1;

    // --- Loadout selector state ---
    // loadoutMode: this open is the player's gear menu — live cycling + the bobber-framing
    // camera are active. False for programmatic opens (vendor shop, bait-missing prompt),
    // which keep the legacy click/drag behavior.
    // closeOnRelease: releasing the inventory button closes the menu (hold-to-open flow).
    private bool loadoutMode;
    private bool closeOnRelease;
    private bool isFishingActive;
    // True only during the cast charge-up (OnStartCharging → throw/abandon). isFishingActive
    // doesn't cover this window because it only flips true once the bobber is thrown, so the
    // gear/bait menu could otherwise be opened mid-charge. Cleared on throw (isFishingActive
    // takes over from there) or when the charge is abandoned.
    private bool isChargingCast;
    // True while the caught fish is being shown off (FishingState.InspectingCatch). Tracked
    // separately because OnReelingCompleted clears isFishingActive right before the showcase begins.
    private bool isInspectingCatch;
    private FishingLine fishingLine;
    private Transform resolvedPlayer;
    private PlayerController playerController;

    // Pose of a fixed camera (e.g. a shop interior view) borrowed by the loadout framing, so it
    // can be eased back exactly where it was once the menu closes. Null camera = nothing to
    // restore (the player's own orbit camera is reset by PlayerCameraController instead).
    private Camera loadoutBorrowedCamera;
    private Vector3 loadoutBorrowedCamPos;
    private Quaternion loadoutBorrowedCamRot;
    private bool loadoutCamRestoring;

    private enum LoadoutScreen { Bobber, Bait }
    private LoadoutScreen loadoutScreen = LoadoutScreen.Bobber;

    // Debounce for treating the analog left stick as discrete left/right/up/down steps.
    private bool stickXNeutral = true;
    private bool stickYNeutral = true;
    private const float StickStepOn = 0.5f;
    private const float StickStepOff = 0.3f;

    private void Awake()
    {
        // First-instance-wins. Duplicate inventories in additively-loaded scenes are kept
        // alive but won't claim the static slot from the original.
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        cachedCamera = Camera.main;
        IsInventoryOpen = false;
        ToggleInputScripts(true);

        clickRaycastResults = new List<RaycastResult>();
        baitBar = FindFirstObjectByType<BaitBarUI>();
        bobberBar = FindFirstObjectByType<BobberBarUI>();

        // Refs for the loadout-framing camera. The FishingLine owns the dangling tackle (rod
        // tip); the player transform places the camera in front of the angler.
        fishingLine = FindFirstObjectByType<FishingLine>();
        playerController = FindFirstObjectByType<PlayerController>();
        if (playerModel != null) resolvedPlayer = playerModel;
        else if (playerController != null) resolvedPlayer = playerController.transform;

        // Mirror the bars' fishing-active tracking so the loadout can't be held open (and the
        // camera can't be yanked) while a bobber is cast / a fish is on the line.
        FishingEvents.OnThrowBobber += HandleFishingStarted;
        FishingEvents.OnCancelFishing += HandleFishingEnded;
        FishingEvents.OnReelingCompleted += HandleFishingEnded;

        // Track the charge-up window so the gear/bait menu can't open while winding up a cast.
        FishingEvents.OnStartCharging += HandleCastChargeStarted;
        FishingEvents.OnChargeCanceled += HandleCastChargeEnded;

        // …and the catch-showcase window, which isn't covered by isFishingActive (see field).
        FishingEvents.OnCatchInspectionStarted += HandleCatchInspectionStarted;
        FishingEvents.OnCatchInspectionEnded += HandleCatchInspectionEnded;

        noteMenu = GetComponent<NoteMenu>();
        if (noteMenu == null) noteMenu = FindFirstObjectByType<NoteMenu>(FindObjectsInactive.Include);

        if (PlayerInventory.Instance != null)
        {
            PlayerInventory.Instance.OnInventoryChanged += RefreshInventory;
        }

        if (BaitInventory.Instance != null)
        {
            BaitInventory.Instance.OnSelectedBaitChanged += HandleSelectedBaitChanged;
        }

        if (uiPanel != null) uiPanel.SetActive(false);
        if (tooltip != null) tooltip.HideTooltip();

        if (closeButton != null)
            closeButton.onClick.AddListener(CloseInventory);
    }

    private void OnDestroy()
    {
        IsInventoryOpen = false;

        FishingEvents.OnThrowBobber -= HandleFishingStarted;
        FishingEvents.OnCancelFishing -= HandleFishingEnded;
        FishingEvents.OnReelingCompleted -= HandleFishingEnded;

        FishingEvents.OnStartCharging -= HandleCastChargeStarted;
        FishingEvents.OnChargeCanceled -= HandleCastChargeEnded;

        FishingEvents.OnCatchInspectionStarted -= HandleCatchInspectionStarted;
        FishingEvents.OnCatchInspectionEnded -= HandleCatchInspectionEnded;

        if (Instance == this) Instance = null;

        if (PlayerInventory.Instance != null)
        {
            PlayerInventory.Instance.OnInventoryChanged -= RefreshInventory;
        }

        if (BaitInventory.Instance != null)
        {
            BaitInventory.Instance.OnSelectedBaitChanged -= HandleSelectedBaitChanged;
        }
    }

    private void Update()
    {
        HandleInventoryButton();

        // Gamepad B / Circle backs out of the inventory — and with it the vendor shop and
        // bait bar, which only render while the inventory is open.
        if (IsInventoryOpen && GamepadInput.CancelPressed)
        {
            CloseInventory();
            return;
        }

        // Loadout cycling while the gear menu is open (and the notebook isn't): left/right
        // cycles the bobber/lure or bait live; down opens the bait sub-screen from a bobber,
        // up returns. Programmatic opens (vendor/bait prompt) skip this.
        if (loadoutMode && IsInventoryOpen && !NoteMenu.IsNotebookOpen)
        {
            HandleLoadoutNavigation();
        }

        // Click-away-to-close: a left click that lands outside the inventory panel
        // (and outside the bait bar) closes the menu. Only for non-loadout opens — the
        // loadout menu is closed by releasing the held button. Skipped on the frame it
        // opened and mid-drag.
        if (IsInventoryOpen
            && !loadoutMode
            && Time.frameCount != openedFrame
            && currentDraggedModel == null
            && Input.GetMouseButtonDown(0)
            && !IsPointerOverInventoryUI())
        {
            CloseInventory();
            return;
        }

        HandleFishDrag();
    }

    // Hold-to-open / release-to-close for the gear menu, plus tap-to-close for menus opened
    // programmatically (vendor shop, bait-missing prompt) or when hold-to-open is disabled.
    private void HandleInventoryButton()
    {
        bool pressed  = KeyInput.InventoryTogglePressed || GamepadInput.InventoryTogglePressed;
        bool released = KeyInput.InventoryToggleReleased || GamepadInput.InventoryToggleReleased;

        if (!IsInventoryOpen)
        {
            if (pressed && CanOpenLoadout()) OpenLoadout(closeWhenReleased: holdToOpenLoadout);
            return;
        }

        if (closeOnRelease)
        {
            if (released) CloseInventory();
        }
        else if (pressed)
        {
            // Tap toggles closed: legacy toggle-mode loadout and vendor/bait-prompt opens.
            CloseInventory();
        }
    }

    private bool CanOpenLoadout()
    {
        if (MainMenuController.IsTitleSequenceActive) return false; // not until the title hands off the camera
        // A control-locked player is mid-cinematic or in a menu that owns the view (the 3D shop, the
        // door walk-in). The loadout must not pop on top of those — it would override the shop menu.
        if (playerController != null && playerController.areControlsLocked) return false;
        if (isFishingActive) return false;
        if (isChargingCast) return false; // No popping the menu mid-cast-charge.
        if (isInspectingCatch) return false; // …or while showing off the catch.
        if (playerController != null && playerController.IsJumping) return false; // …or mid-jump.
        if (NoteMenu.IsNotebookOpen) return false;
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive()) return false;
        return true;
    }

    private void OpenLoadout(bool closeWhenReleased)
    {
        OpenInventory();                 // resets loadoutMode/closeOnRelease, then we upgrade
        loadoutMode = true;
        IsLoadoutActive = true;
        closeOnRelease = closeWhenReleased;
        loadoutScreen = LoadoutScreen.Bobber;
        BeginLoadoutCameraBorrow();      // remember a fixed (shop) camera so we can reset it on close
        // Start "armed but not neutral": if the stick happens to be deflected at open, require
        // it to return to centre before the first cycle/screen step registers.
        stickXNeutral = false;
        stickYNeutral = false;
        RefreshLoadoutFocus();
    }

    private void HandleFishingStarted(Vector3 dir, float force) { isFishingActive = true; isChargingCast = false; }
    private void HandleFishingEnded() { isFishingActive = false; isChargingCast = false; }
    private void HandleCastChargeStarted() => isChargingCast = true;
    private void HandleCastChargeEnded() => isChargingCast = false;
    private void HandleCatchInspectionStarted() => isInspectingCatch = true;
    private void HandleCatchInspectionEnded() => isInspectingCatch = false;

    private bool IsPointerOverInventoryUI()
    {
        if (EventSystem.current == null) return true; // Can't tell — err on keeping it open.

        if (clickPointerData == null)
            clickPointerData = new PointerEventData(EventSystem.current);
        clickPointerData.position = Input.mousePosition;

        clickRaycastResults.Clear();
        EventSystem.current.RaycastAll(clickPointerData, clickRaycastResults);

        foreach (var result in clickRaycastResults)
        {
            Transform hit = result.gameObject.transform;

            if (uiPanel != null && hit.IsChildOf(uiPanel.transform))
                return true;

            // The bait bar only exists while the inventory is open, so clicks on it
            // must count as "inside" — otherwise closing on mouse-down would kill the
            // slot button before its mouse-up click can register a bait selection.
            if (baitBar != null && hit.IsChildOf(baitBar.transform))
                return true;

            if (bobberBar != null && hit.IsChildOf(bobberBar.transform))
                return true;

            foreach (var root in additionalClickThroughRoots)
            {
                if (root != null && hit.IsChildOf(root))
                    return true;
            }
        }

        return false;
    }

    private void HandleSelectedBaitChanged(BaitItem bait)
    {
        // Picking a bait (e.g. from the bait bar that pops up when casting without bait)
        // dismisses the inventory automatically — but NOT while cycling bait in the loadout
        // sub-screen, where each step changes the equipped bait and must keep the menu open.
        if (bait != null && IsInventoryOpen && !loadoutMode)
        {
            CloseInventory();
        }
    }

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

    private void HandleLoadoutNavigation()
    {
        int xStep = ReadHorizontalStep();
        if (xStep != 0) { Cycle(xStep); return; }

        int yStep = ReadVerticalStep();
        if (yStep < 0) EnterBaitScreen();
        else if (yStep > 0) EnterBobberScreen();
    }

    // D-pad / arrow keys / WASD are discrete; the left stick is debounced into single steps so a
    // held deflection doesn't spin through every option in a few frames. Player movement is
    // disabled while the gear menu is open (ToggleInputScripts), so WASD is free to reuse here.
    private int ReadHorizontalStep()
    {
        if (GamepadInput.DpadRightPressed || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) return 1;
        if (GamepadInput.DpadLeftPressed  || Input.GetKeyDown(KeyCode.LeftArrow)  || Input.GetKeyDown(KeyCode.A)) return -1;

        float x = GamepadInput.Move.x;
        if (stickXNeutral && Mathf.Abs(x) > StickStepOn) { stickXNeutral = false; return x > 0 ? 1 : -1; }
        if (Mathf.Abs(x) < StickStepOff) stickXNeutral = true;
        return 0;
    }

    private int ReadVerticalStep()
    {
        if (GamepadInput.DpadUpPressed   || Input.GetKeyDown(KeyCode.UpArrow)   || Input.GetKeyDown(KeyCode.W)) return 1;
        if (GamepadInput.DpadDownPressed || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) return -1;

        float y = GamepadInput.Move.y;
        if (stickYNeutral && Mathf.Abs(y) > StickStepOn) { stickYNeutral = false; return y > 0 ? 1 : -1; }
        if (Mathf.Abs(y) < StickStepOff) stickYNeutral = true;
        return 0;
    }

    private void Cycle(int delta)
    {
        if (loadoutScreen == LoadoutScreen.Bait) CycleBait(delta);
        else CycleBobber(delta);
        RefreshLoadoutFocus();
    }

    private void CycleBobber(int delta)
    {
        if (BobberInventory.Instance == null) return;
        IReadOnlyList<BobberItem> owned = BobberInventory.Instance.OwnedBobbers;
        if (owned == null || owned.Count == 0) return;

        int current = IndexOfSelectedBobber(owned);
        int next = current < 0 ? (delta > 0 ? 0 : owned.Count - 1)
                               : ((current + delta) % owned.Count + owned.Count) % owned.Count;
        if (owned[next] != null) BobberInventory.Instance.SetSelectedBobber(owned[next]);
    }

    private void CycleBait(int delta)
    {
        if (BaitInventory.Instance == null) return;

        // Cycle stops = "no bait" (null — an empty hook is always a valid choice) + each owned/
        // infinite bait, so the player can deliberately fish baitless from the gear menu. Mirrors
        // BaitBarUI.CycleSelection so the keyboard/gamepad path reaches the same stops as the arrows.
        cycleBaitStops.Clear();
        cycleBaitStops.Add(null);
        cycleBaitStops.AddRange(GetCyclableBaits());
        if (cycleBaitStops.Count <= 1) return; // only "no bait" available — nothing to cycle to

        int current = cycleBaitStops.IndexOf(BaitInventory.Instance.SelectedBait); // null resolves to 0
        if (current < 0) current = 0;
        int next = ((current + delta) % cycleBaitStops.Count + cycleBaitStops.Count) % cycleBaitStops.Count;
        BaitInventory.Instance.SetSelectedBait(cycleBaitStops[next]);
    }

    private int IndexOfSelectedBobber(IReadOnlyList<BobberItem> owned)
    {
        BobberItem sel = BobberInventory.Instance.SelectedBobber;
        if (sel == null) return -1;
        for (int i = 0; i < owned.Count; i++) if (owned[i] == sel) return i;
        return -1;
    }

    // Registered bait that is on the shelf and actually obtainable (in stock or infinite).
    private List<BaitItem> GetCyclableBaits()
    {
        cyclableBaits.Clear();
        if (BaitInventory.Instance == null) return cyclableBaits;
        IReadOnlyList<BaitItem> registered = BaitInventory.Instance.RegisteredBaits;
        if (registered == null) return cyclableBaits;
        for (int i = 0; i < registered.Count; i++)
        {
            BaitItem b = registered[i];
            if (b == null || !b.isAvailable) continue;
            if (b.isAlwaysAvailable || BaitInventory.Instance.GetCount(b) > 0) cyclableBaits.Add(b);
        }
        return cyclableBaits;
    }

    private void EnterBaitScreen()
    {
        if (loadoutScreen == LoadoutScreen.Bait) return;
        // Reachable only from a regular bobber — lures take no bait.
        if (BobberInventory.IsLureEquipped) return;
        if (GetCyclableBaits().Count == 0) return;
        loadoutScreen = LoadoutScreen.Bait;
        RefreshLoadoutFocus();
    }

    private void EnterBobberScreen()
    {
        if (loadoutScreen == LoadoutScreen.Bobber) return;
        loadoutScreen = LoadoutScreen.Bobber;
        RefreshLoadoutFocus();
    }

    // Tint the equipped slot on the active screen's bar (reusing the bars' gamepad-focus
    // highlight) so it's obvious which row left/right is steering. Always re-centres on the
    // equipped item so the focus tracks the live selection.
    private void RefreshLoadoutFocus()
    {
        if (loadoutScreen == LoadoutScreen.Bait)
        {
            if (bobberBar != null) bobberBar.ClearGamepadFocus();
            if (baitBar != null) { baitBar.ClearGamepadFocus(); baitBar.FocusGamepad(); }
        }
        else
        {
            if (baitBar != null) baitBar.ClearGamepadFocus();
            if (bobberBar != null) { bobberBar.ClearGamepadFocus(); bobberBar.FocusGamepad(); }
        }
    }

    private void ResetLoadoutNavigation()
    {
        loadoutScreen = LoadoutScreen.Bobber;
        stickXNeutral = true;
        stickYNeutral = true;
        if (bobberBar != null) bobberBar.ClearGamepadFocus();
        if (baitBar != null) baitBar.ClearGamepadFocus();
    }

    // --- Loadout framing camera ---
    // While the gear menu is held open, swing the main camera to a pose in FRONT of the
    // angler looking back at the dangling tackle, so the player sees the bobber/lure they are
    // cycling with themselves behind it. PlayerCameraController yields the camera while the
    // inventory is open (see its UpdateCamera early-out), so we own it here uncontested.
    //
    // On close the player's orbit camera is resumed and snapped back by PlayerCameraController.
    // But a fixed camera (e.g. a shop interior view) is driven by nobody, so for that case we
    // captured its pose on open (BeginLoadoutCameraBorrow) and ease it back ourselves here.
    private void LateUpdate()
    {
        if (loadoutMode && IsInventoryOpen) UpdateLoadoutCamera();
        else if (loadoutCamRestoring) RestoreLoadoutCameraStep();
    }

    // Captures the active camera's pose when the gear menu opens, but only if it's a fixed camera
    // that PlayerCameraController won't restore (the player's own orbit camera is left alone — it
    // snaps back on its own). The captured pose is the target RestoreLoadoutCameraStep eases to.
    private void BeginLoadoutCameraBorrow()
    {
        Camera cam = ActiveCamera;
        if (cam == null) { loadoutBorrowedCamera = null; loadoutCamRestoring = false; return; }

        if (playerController == null) playerController = FindFirstObjectByType<PlayerController>();
        Transform restorable = playerController != null ? playerController.ActivePlayerCameraTransform : null;
        if (restorable != null && cam.transform == restorable)
        {
            // Player orbit camera — PlayerCameraController owns the reset, nothing to remember.
            loadoutBorrowedCamera = null;
            loadoutCamRestoring = false;
            return;
        }

        // Reopening mid-restore of the same camera: keep the original home pose as the target so a
        // half-finished restore doesn't become the new "home". Otherwise snapshot the current pose.
        if (loadoutBorrowedCamera != cam)
        {
            loadoutBorrowedCamera = cam;
            loadoutBorrowedCamPos = cam.transform.position;
            loadoutBorrowedCamRot = cam.transform.rotation;
        }
        loadoutCamRestoring = false;
    }

    // Closing the menu hands the player camera back to PlayerCameraController; a borrowed fixed
    // camera instead eases back to its captured pose over the next frames.
    private void EndLoadoutCameraBorrow()
    {
        if (loadoutBorrowedCamera != null) loadoutCamRestoring = true;
    }

    private void RestoreLoadoutCameraStep()
    {
        if (loadoutBorrowedCamera == null) { loadoutCamRestoring = false; return; }

        Transform ct = loadoutBorrowedCamera.transform;
        float t = 1f - Mathf.Exp(-cameraLerpSpeed * Time.unscaledDeltaTime);
        ct.position = Vector3.Lerp(ct.position, loadoutBorrowedCamPos, t);
        ct.rotation = Quaternion.Slerp(ct.rotation, loadoutBorrowedCamRot, t);

        if ((ct.position - loadoutBorrowedCamPos).sqrMagnitude < 0.0001f
            && Quaternion.Angle(ct.rotation, loadoutBorrowedCamRot) < 0.1f)
        {
            ct.SetPositionAndRotation(loadoutBorrowedCamPos, loadoutBorrowedCamRot);
            loadoutBorrowedCamera = null;
            loadoutCamRestoring = false;
        }
    }

    private void UpdateLoadoutCamera()
    {
        Camera cam = ActiveCamera;
        if (cam == null || fishingLine == null || fishingLine.rodTip == null) return;

        if (resolvedPlayer == null)
        {
            if (playerModel != null) resolvedPlayer = playerModel;
            else { var pc = FindFirstObjectByType<PlayerController>(); if (pc != null) resolvedPlayer = pc.transform; }
            if (resolvedPlayer == null) return;
        }

        Vector3 tackle = fishingLine.rodTip.position + Vector3.down * dangleDrop;

        // Horizontal direction from the player out to the tackle (≈ the way the rod points).
        Vector3 horizFwd = tackle - resolvedPlayer.position;
        horizFwd.y = 0f;
        if (horizFwd.sqrMagnitude < 0.0001f) { horizFwd = resolvedPlayer.forward; horizFwd.y = 0f; }
        if (horizFwd.sqrMagnitude < 0.0001f) horizFwd = Vector3.forward;
        horizFwd.Normalize();

        Vector3 targetPos = tackle + horizFwd * cameraFrontDistance + Vector3.up * cameraHeightOffset;
        Vector3 lookTarget = tackle + Vector3.up * cameraLookHeightOffset;
        Vector3 lookDir = lookTarget - targetPos;
        if (lookDir.sqrMagnitude < 0.0001f) return;
        Quaternion targetRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

        // Ease in from wherever the camera currently is (the player view) for a smooth swing.
        float t = 1f - Mathf.Exp(-cameraLerpSpeed * Time.unscaledDeltaTime);
        cam.transform.position = Vector3.Lerp(cam.transform.position, targetPos, t);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, targetRot, t);
    }

    public void ToggleInventory()
    {
        if (IsInventoryOpen) CloseInventory();
        else OpenInventory();
    }

    public void OpenInventory()
    {
        // Mutually exclusive with the notebook — opening the inventory closes it through
        // its normal close path, which restores time scale, the main camera and the cursor.
        if (NoteMenu.IsNotebookOpen && noteMenu != null)
        {
            noteMenu.CloseNotebook();
        }

        // Default every open to non-loadout (programmatic: vendor / bait prompt). OpenLoadout
        // upgrades it afterwards for the player-driven gear menu.
        loadoutMode = false;
        IsLoadoutActive = false;
        closeOnRelease = false;
        // If a programmatic open interrupts an active loadout, let any borrowed fixed (shop)
        // camera ease back. Harmless on a normal open (no camera borrowed); OpenLoadout re-borrows
        // immediately after this when it upgrades to loadout mode.
        EndLoadoutCameraBorrow();

        IsInventoryOpen = true;
        openedFrame = Time.frameCount;
        if (uiPanel != null) uiPanel.SetActive(true);
        RefreshInventory();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ToggleInputScripts(false);
    }

    public void CloseInventory()
    {
        IsInventoryOpen = false;
        loadoutMode = false;
        IsLoadoutActive = false;
        closeOnRelease = false;
        EndLoadoutCameraBorrow();   // ease a borrowed fixed (shop) camera back to its captured pose
        if (uiPanel != null) uiPanel.SetActive(false);
        if (tooltip != null) tooltip.HideTooltip();
        ResetLoadoutNavigation();

        if (currentDraggedModel != null) Destroy(currentDraggedModel);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        ToggleInputScripts(true);

        // Closing the inventory by any route (I key, close button) also ends any active shopping
        // session so the shop panel doesn't reappear next time the inventory opens.
        if (FishVendor.CurrentShoppingVendor != null)
        {
            FishVendor.CurrentShoppingVendor.CloseShop();
        }
    }

    private void ToggleInputScripts(bool enable)
    {
        foreach (var script in scriptsToDisableInput)
        {
            if (script != null) script.enabled = enable;
        }
    }

    private void RefreshInventory()
    {
        if (physicsContainer == null)
        {
            Debug.LogError("InventoryUI: Container is missing! Assign it in Inspector.");
            return;
        }

        foreach (var slot in spawnedSlots)
        {
            if (slot != null) Destroy(slot.gameObject);
        }
        spawnedSlots.Clear();

        // Loadout-only menu: the caught-fish grid is hidden. The fish drag/sell code below
        // (and in TryDropFish) stays intact but dormant — no slots are spawned to drag.
        if (!showCaughtFish) return;

        if (PlayerInventory.Instance == null) return;

        List<CaughtFish> inventoryData = PlayerInventory.Instance.caughtFishes;

        for (int i = 0; i < inventoryData.Count; i++)
        {
            CaughtFish fish = inventoryData[i];

            GameObject newSlotObj = Instantiate(inventorySlotPrefab, physicsContainer, false);
            newSlotObj.SetActive(true);
            newSlotObj.transform.localScale = Vector3.one;

            RectTransform slotRect = newSlotObj.GetComponent<RectTransform>();
            if (slotRect != null)
            {
                slotRect.anchorMin = new Vector2(0f, 0.5f);
                slotRect.anchorMax = new Vector2(0f, 0.5f);
                slotRect.pivot = new Vector2(0.5f, 0.5f);
                slotRect.anchoredPosition = new Vector2(leftPadding + i * slotSpacing, 0f);
            }

            // Strip any leftover physics components from the previous floating-bubble layout.
            var floating = newSlotObj.GetComponent<InventoryFloatingItem>();
            if (floating != null) Destroy(floating);
            var rb2d = newSlotObj.GetComponent<Rigidbody2D>();
            if (rb2d != null) Destroy(rb2d);
            var col2d = newSlotObj.GetComponent<BoxCollider2D>();
            if (col2d != null) Destroy(col2d);

            InventorySlotUI slotScript = newSlotObj.GetComponent<InventorySlotUI>();
            if (slotScript != null)
            {
                slotScript.Populate(
                    fish,
                    (f, rect) => { if (tooltip) tooltip.ShowTooltip(f, rect); },
                    () => { if (tooltip) tooltip.HideTooltip(); },
                    (f) => { StartDraggingFish(f); }
                );
                spawnedSlots.Add(slotScript);
            }
        }
    }
}