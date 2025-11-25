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

        if (entry.preset != null)
        {
            iconImage.sprite = entry.preset.fishImage;
        }

        bool isCaught = entry.hasCaught > 0;

        if (unknownOverlay != null)
        {
            unknownOverlay.SetActive(!isCaught);
        }

        if (iconImage != null)
        {
            iconImage.color = isCaught ? Color.white : Color.black;
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