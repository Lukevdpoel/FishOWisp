using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    public static bool IsInventoryOpen { get; private set; }

    [Header("Keybindings")]
    public KeyCode toggleKey = KeyCode.B;

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

    // Click-away-to-close support.
    private PointerEventData clickPointerData;
    private List<RaycastResult> clickRaycastResults;
    private BaitBarUI baitBar;
    private int openedFrame = -1;

    private void Start()
    {
        cachedCamera = Camera.main;
        IsInventoryOpen = false;
        ToggleInputScripts(true);

        clickRaycastResults = new List<RaycastResult>();
        baitBar = FindFirstObjectByType<BaitBarUI>();

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
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleInventory();
        }

        // Click-away-to-close: a left click that lands outside the inventory panel
        // (and outside the bait bar) closes the menu. Skipped on the frame it opened
        // so the very click that opened it can't immediately close it again, and
        // skipped mid-drag so dropping a fish doesn't count as a click-away.
        if (IsInventoryOpen
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
        // dismisses the inventory automatically.
        if (bait != null && IsInventoryOpen)
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

            // 3. Rotation
            currentDraggedModel.transform.Rotate(Vector3.up, 30f * Time.deltaTime);
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

    public void ToggleInventory()
    {
        if (IsInventoryOpen) CloseInventory();
        else OpenInventory();
    }

    public void OpenInventory()
    {
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
        if (uiPanel != null) uiPanel.SetActive(false);
        if (tooltip != null) tooltip.HideTooltip();

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