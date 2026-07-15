using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class EncyclopediaGridSlot : MonoBehaviour, IPointerClickHandler
{
    [Header("UI References")]
    public Image iconImage;
    [Tooltip("Fish name label on the horizontal list slot. Shows the species name once caught, " +
             "\"???\" before. Optional — leave empty for icon-only slots.")]
    public TMP_Text nameText;
    public GameObject selectionHighlight;
    public GameObject unknownOverlay;

    public FishEncyclopediaEntry myEntry;
    private EncyclopediaUIController controller;

    public void Setup(FishEncyclopediaEntry entry, EncyclopediaUIController uiController)
    {
        myEntry = entry;
        controller = uiController;

        bool isCaught = entry != null && entry.hasCaught > 0;

        // Do NOT assign entry.preset.fishImage to iconImage here. The fish-icon sprite art is
        // currently missing from the project, so that field is a dangling reference: it points at
        // a deleted asset, which makes it "real non-null" (a != null check passes) while the
        // backing native object is dead. UnityEngine.UI.Image's sprite setter reads the
        // assigned sprite's .rect, and reading the rect of that dead object HARD-CRASHES the
        // player build (a native access violation, not a catchable managed exception — so no
        // managed guard can stop it). The icon keeps the prefab's placeholder sprite.
        // Re-enable once fishImage points at a valid sprite again:
        //     iconImage.sprite = (entry != null && entry.preset != null) ? entry.preset.fishImage : null;
        //
        // Also: no black tint for uncaught fish. With the placeholder sprite in every slot, the
        // old silhouette tint just blacked out the authored row art (read as "random slots are
        // black"). The mystery state is carried by nameText's ??? and the unknownOverlay instead.
        // A silhouette treatment can come back together with the real per-species sprites.

        if (nameText != null)
        {
            // Same mystery convention as FishEntryUI: uncaught fish stay "???".
            nameText.text = (isCaught && entry.preset != null) ? entry.preset.fishName : "???";
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