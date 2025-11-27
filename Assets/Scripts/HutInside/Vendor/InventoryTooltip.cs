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
    public float baseDistance = 60f; // How far to push away from the slot center
    public bool clampToScreen = true;

    private RectTransform rectTransform;
    private Canvas parentCanvas;
    private RectTransform currentTargetSlot;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();

        // Hide by default
        gameObject.SetActive(false);
    }

    void Update()
    {
        FollowCursorWithAvoidance();
    }

    private void FollowCursorWithAvoidance()
    {
        if (parentCanvas == null || currentTargetSlot == null) return;

        // 1. Get positions in Screen Space
        Vector2 mousePos = Input.mousePosition;

        // Convert the Slot's world position to Screen Space
        Camera cam = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;
        Vector2 slotScreenPos = RectTransformUtility.WorldToScreenPoint(cam, currentTargetSlot.position);

        // 2. Calculate Direction (Slot Center -> Mouse)
        Vector2 dirFromCenter = (mousePos - slotScreenPos).normalized;

        // Fallback: If mouse is dead center, push up-right
        if (dirFromCenter == Vector2.zero) dirFromCenter = new Vector2(1, 1).normalized;

        // 3. Calculate Target Position (Mouse + Push Away)
        // We push the tooltip away from the mouse in the same direction 
        // that the mouse is from the center.
        Vector2 finalScreenPos = mousePos + (dirFromCenter * baseDistance);

        // 4. Convert Screen Point back to Local Point for the Tooltip's RectTransform
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentCanvas.transform as RectTransform,
            finalScreenPos,
            cam,
            out localPoint);

        transform.localPosition = localPoint;
    }

    public void ShowTooltip(CaughtFish fish, RectTransform slotRect)
    {
        currentTargetSlot = slotRect;
        gameObject.SetActive(true);
        transform.SetAsLastSibling(); // Ensure it renders on top

        if (headerText != null)
            headerText.text = fish.preset.fishName;

        if (bodyText != null)
            bodyText.text = $"{fish.lengthCm:F1} cm\n<size=80%><color=#CCCCCC>{fish.preset.description}</color></size>";

        if (valueText != null)
            valueText.text = $"{fish.GetValue()} coins";
    }

    public void HideTooltip()
    {
        currentTargetSlot = null;
        gameObject.SetActive(false);
    }
}