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
            // Do NOT assign entry.preset.fishImage here. The fish-icon sprite art is currently
            // missing from the project, so that field is a dangling reference: it points at a
            // deleted asset, which makes it "real non-null" (a != null check passes) while the
            // backing native object is dead. UnityEngine.UI.Image's sprite setter reads the
            // assigned sprite's .rect, and reading the rect of that dead object HARD-CRASHES the
            // player build (a native access violation, not a catchable managed exception — so no
            // managed guard can stop it). Removing this assignment entirely deletes the only
            // set_sprite call in Setup, so the crash site cannot occur. The icon keeps the
            // prefab's placeholder sprite. Re-enable once fishImage points at a valid sprite again:
            //     iconImage.sprite = (entry != null && entry.preset != null) ? entry.preset.fishImage : null;
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