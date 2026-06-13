using UnityEngine;
using UnityEngine.EventSystems;

// Pointer-hover relay for runtime-built item slots (bait bar, bobber bar, shop cells):
// shows the shared InventoryTooltip with the item's name and description while the mouse
// is over the slot. The bars build their UI from code, so this is attached via Attach()
// rather than wired in a prefab.
public class ItemHoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private string header;
    private string body;

    private static InventoryTooltip sharedTooltip;
    private static ItemHoverTooltip activeSource;

    public static void Attach(GameObject target, string header, string body)
    {
        ItemHoverTooltip hover = target.GetComponent<ItemHoverTooltip>();
        if (hover == null) hover = target.AddComponent<ItemHoverTooltip>();
        hover.header = header;
        hover.body = body;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        InventoryTooltip tooltip = ResolveTooltip();
        if (tooltip == null) return;
        activeSource = this;
        tooltip.ShowItemTooltip(header, body, transform as RectTransform);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HideIfActive();
    }

    // The bars rebuild (destroying their slots) while the pointer can still be over one —
    // without this the tooltip would linger with no exit event left to dismiss it.
    private void OnDisable() => HideIfActive();

    private void HideIfActive()
    {
        if (activeSource != this) return;
        activeSource = null;
        if (sharedTooltip != null) sharedTooltip.HideTooltip();
    }

    private static InventoryTooltip ResolveTooltip()
    {
        // Inactive included: the tooltip object deactivates itself whenever it's hidden.
        if (sharedTooltip == null)
            sharedTooltip = FindFirstObjectByType<InventoryTooltip>(FindObjectsInactive.Include);
        return sharedTooltip;
    }
}
