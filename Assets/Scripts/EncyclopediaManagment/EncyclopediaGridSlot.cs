using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class EncyclopediaGridSlot : MonoBehaviour, IPointerClickHandler
{
    [Header("UI References")]
    public Image iconImage;
    public GameObject selectionHighlight;
    public GameObject unknownOverlay;

    public FishEncyclopediaEntry myEntry;
    private EncyclopediaUIController controller;

    public void Setup(FishEncyclopediaEntry entry, EncyclopediaUIController uiController)
    {
        myEntry = entry;
        controller = uiController;

        bool isCaught = entry != null && entry.hasCaught > 0;

        if (iconImage != null)
        {
            // Guard the icon: only assign a sprite that is actually present. A fishImage that is
            // unassigned OR points at a since-deleted asset reads as Unity "fake null", and pushing
            // that into Image.sprite hard-crashes a player build — the engine reads sprite.rect on
            // assignment and faults on the dangling native object (NOT a catchable managed exception).
            // Assigning real null is safe, so collapse the fake-null down to null.
            Sprite icon = (entry != null && entry.preset != null) ? entry.preset.fishImage : null;
            iconImage.sprite = icon != null ? icon : null;
            iconImage.color = isCaught ? Color.white : Color.black;
        }

        if (unknownOverlay != null)
        {
            unknownOverlay.SetActive(!isCaught);
        }

        Deselect();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnClicked();
    }

    public void OnClicked()
    {
        if (controller != null)
        {
            controller.OnSlotClicked(myEntry, this);
        }
    }

    public void Select()
    {
        if (selectionHighlight != null) selectionHighlight.SetActive(true);
    }

    public void Deselect()
    {
        if (selectionHighlight != null) selectionHighlight.SetActive(false);
    }
}