using UnityEngine;
using TMPro;

public class InventoryTooltip : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The text component for the Fish Name")]
    public TextMeshProUGUI headerText;
    [Tooltip("The text component for the description/lore")]
    public TextMeshProUGUI bodyText;
    [Tooltip("The text component for the price/value")]
    public TextMeshProUGUI valueText;

    [Header("Settings")]
    public float baseDistance = 60f; // How far to push away from the cursor
    public bool clampToScreen = true;

    private RectTransform rectTransform;
    private Canvas parentCanvas;

    // Nullable: If null, we are in "World Mode" (hovering 3D objects)
    private RectTransform currentTargetSlot;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();
        gameObject.SetActive(false);
    }

    void Update()
    {
        FollowCursorWithAvoidance();
    }

    private void FollowCursorWithAvoidance()
    {
        if (parentCanvas == null) return;

        Vector2 mousePos = Input.mousePosition;
        Vector2 finalScreenPos;

        // --- CHANGED: Handle cases where there is no UI slot (World Mode) ---
        if (currentTargetSlot != null)
        {
            // MODE A: Inventory Slot (Push away from slot center)
            Camera cam = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;
            Vector2 slotScreenPos = RectTransformUtility.WorldToScreenPoint(cam, currentTargetSlot.position);

            Vector2 dirFromCenter = (mousePos - slotScreenPos).normalized;
            if (dirFromCenter == Vector2.zero) dirFromCenter = new Vector2(1, 1).normalized;

            finalScreenPos = mousePos + (dirFromCenter * baseDistance);
        }
        else
        {
            // MODE B: World Object (Just push up-right from mouse so it doesn't block the view)
            Vector2 defaultDir = new Vector2(1, 1).normalized;
            finalScreenPos = mousePos + (defaultDir * baseDistance);
        }

        // Convert to Local Point
        Camera canvasCam = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentCanvas.transform as RectTransform,
            finalScreenPos,
            canvasCam,
            out Vector2 localPoint);

        transform.localPosition = localPoint;
    }

    // Called by Inventory Slots (2D)
    public void ShowTooltip(CaughtFish fish, RectTransform slotRect)
    {
        currentTargetSlot = slotRect;
        UpdateText(fish);
    }

    // --- NEW: Called by WorldFishHover (3D) ---
    public void ShowWorldTooltip(CaughtFish fish)
    {
        currentTargetSlot = null; // Indicates we are in World Mode
        UpdateText(fish);
    }

    private void UpdateText(CaughtFish fish)
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling(); // Render on top

        if (headerText != null) headerText.text = fish.preset.fishName;
        if (bodyText != null) bodyText.text = $"{fish.lengthCm:F1} cm\n<size=80%><color=#CCCCCC>{fish.preset.description}</color></size>";
        if (valueText != null) valueText.text = $"{fish.GetValue()} coins";
    }

    public void HideTooltip()
    {
        currentTargetSlot = null;
        gameObject.SetActive(false);
    }
}