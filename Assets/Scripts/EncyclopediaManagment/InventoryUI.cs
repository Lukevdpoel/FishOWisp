using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public partial class InventoryUI : MonoBehaviour
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
