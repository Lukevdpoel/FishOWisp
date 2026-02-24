using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    public static bool IsInventoryOpen { get; private set; }

    [Header("Keybindings")]
    public KeyCode toggleKey = KeyCode.I;

    [Header("Main References")]
    public GameObject uiPanel;
    public Transform physicsContainer;
    public GameObject inventorySlotPrefab;

    [Header("Tooltip System")]
    public InventoryTooltip tooltip;

    [Header("Physics Settings")]
    public float blowForce = 8000f;
    public Vector2 normalizedSpawnPoint = new Vector2(0.5f, 0.5f);
    public float spawnSpacing = 120f;
    public float blowSpread = 0.8f;

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

    private List<InventorySlotUI> spawnedSlots = new List<InventorySlotUI>();
    private EdgeCollider2D containerBoundary;

    // State for dragging
    private GameObject currentDraggedModel;
    private CaughtFish currentDraggedFishData;

    private void Start()
    {
        IsInventoryOpen = false;
        ToggleInputScripts(true);

        if (PlayerInventory.Instance != null)
        {
            PlayerInventory.Instance.OnInventoryChanged += RefreshInventory;
        }

        if (uiPanel != null) uiPanel.SetActive(false);
        if (tooltip != null) tooltip.HideTooltip();

        if (closeButton != null)
            closeButton.onClick.AddListener(CloseInventory);

        SetupBoundaries();
    }

    private void OnDestroy()
    {
        IsInventoryOpen = false;

        if (PlayerInventory.Instance != null)
        {
            PlayerInventory.Instance.OnInventoryChanged -= RefreshInventory;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleInventory();
        }

        HandleFishDrag();
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
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
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
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

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

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            currentDraggedModel.transform.position = ray.GetPoint(modelDistance);

            currentDraggedModel.transform.rotation = Quaternion.identity;
            currentDraggedModel.transform.localScale = Vector3.one * modelScale;

            // Remove physics components so the drag model doesn't block rays or fall
            foreach (var rb in currentDraggedModel.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (var col in currentDraggedModel.GetComponentsInChildren<Collider>()) Destroy(col);
        }
    }

    private void SetupBoundaries()
    {
        if (physicsContainer == null) return;

        containerBoundary = physicsContainer.GetComponent<EdgeCollider2D>();
        if (containerBoundary == null)
        {
            containerBoundary = physicsContainer.gameObject.AddComponent<EdgeCollider2D>();
        }

        UpdateBoundaryShape();
    }

    private void UpdateBoundaryShape()
    {
        if (containerBoundary == null) return;

        RectTransform rect = physicsContainer.GetComponent<RectTransform>();
        if (rect == null) return;

        Vector3[] corners = new Vector3[4];
        rect.GetLocalCorners(corners);

        Vector2[] points = new Vector2[5];
        points[0] = corners[0]; // Bottom Left
        points[1] = corners[1]; // Top Left
        points[2] = corners[2]; // Top Right
        points[3] = corners[3]; // Bottom Right
        points[4] = corners[0]; // Close Loop

        containerBoundary.points = points;
    }

    public void ToggleInventory()
    {
        if (uiPanel.activeSelf) CloseInventory();
        else OpenInventory();
    }

    public void OpenInventory()
    {
        IsInventoryOpen = true;
        uiPanel.SetActive(true);
        UpdateBoundaryShape();
        RefreshInventory();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ToggleInputScripts(false);
    }

    public void CloseInventory()
    {
        IsInventoryOpen = false;
        uiPanel.SetActive(false);
        if (tooltip != null) tooltip.HideTooltip();

        if (currentDraggedModel != null) Destroy(currentDraggedModel);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        ToggleInputScripts(true);
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
            Debug.LogError("InventoryUI: Physics Container is missing! Assign it in Inspector.");
            return;
        }

        foreach (var slot in spawnedSlots)
        {
            if (slot != null) Destroy(slot.gameObject);
        }
        spawnedSlots.Clear();

        if (PlayerInventory.Instance == null) return;

        List<CaughtFish> inventoryData = PlayerInventory.Instance.caughtFishes;
        RectTransform containerRect = physicsContainer.GetComponent<RectTransform>();

        Vector3[] corners = new Vector3[4];
        containerRect.GetLocalCorners(corners);

        Vector3 bottomLeft = corners[0];
        Vector3 topRight = corners[2];
        float width = topRight.x - bottomLeft.x;
        float height = topRight.y - bottomLeft.y;

        float spawnX = bottomLeft.x + (width * normalizedSpawnPoint.x);
        float spawnY = bottomLeft.y + (height * normalizedSpawnPoint.y);
        Vector3 baseSpawnOrigin = new Vector3(spawnX, spawnY, 0);

        int cols = Mathf.CeilToInt(Mathf.Sqrt(inventoryData.Count));
        int rows = Mathf.CeilToInt((float)inventoryData.Count / cols);

        int i = 0;
        foreach (CaughtFish fish in inventoryData)
        {
            GameObject newSlotObj = Instantiate(inventorySlotPrefab, physicsContainer, false);
            newSlotObj.SetActive(true);
            newSlotObj.transform.localScale = Vector3.one;

            int row = i / cols;
            int col = i % cols;

            float xOffset = (col - (cols - 1) / 2f) * spawnSpacing;
            float yOffset = (row - (rows - 1) / 2f) * spawnSpacing;

            Vector3 positionJitter = Random.insideUnitCircle * (spawnSpacing * 0.4f);

            newSlotObj.transform.localPosition = baseSpawnOrigin + new Vector3(xOffset, yOffset, 0) + positionJitter;

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

            BoxCollider2D boxCol = newSlotObj.GetComponent<BoxCollider2D>();
            RectTransform slotRect = newSlotObj.GetComponent<RectTransform>();
            if (boxCol == null) boxCol = newSlotObj.AddComponent<BoxCollider2D>();
            if (slotRect != null) boxCol.size = slotRect.rect.size;

            InventoryFloatingItem physicsItem = newSlotObj.GetComponent<InventoryFloatingItem>();
            if (physicsItem == null) physicsItem = newSlotObj.AddComponent<InventoryFloatingItem>();

            Vector3 centerToItem = newSlotObj.transform.localPosition - baseSpawnOrigin;
            Vector2 outwardDirection = centerToItem.normalized;
            if (outwardDirection == Vector2.zero) outwardDirection = Random.insideUnitCircle.normalized;

            Vector2 randomChaos = Random.insideUnitCircle * blowSpread;
            Vector2 finalDirection = (outwardDirection + randomChaos).normalized;

            float randomizedForce = blowForce * Random.Range(0.7f, 1.4f);

            physicsItem.Init(finalDirection * randomizedForce);

            i++;
        }
    }
}