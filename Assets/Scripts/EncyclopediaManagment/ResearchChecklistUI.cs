using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Notebook research checklist for one fish page. Wire the references in the notebook prefab; the
// layout (2x2 of dots: rows = Catch / Zoom, columns = 10x / 20x) and the name-row icon are yours to
// place. Populate() fills the dots from FishResearch and swaps the name icon by completion tier.
//
//   research checklist:
//                          10x        20x
//   Catch fish              o          o      <- dots[0]=Catch10, dots[1]=Catch20
//   Zoom in on fish         o          o      <- dots[2]=Zoom10,  dots[3]=Zoom20
//
// The dots array MUST be in this order; it maps 1:1 to FishResearch.AllItems.
public class ResearchChecklistUI : MonoBehaviour
{
    [Header("Checklist dots (order: Catch10, Catch20, Zoom10, Zoom20)")]
    [Tooltip("Exactly four dot Images, in checklist order. Each is shown 'filled' when its item is complete.")]
    public Image[] dots = new Image[FishResearch.ItemCount];

    [Tooltip("Optional sprite used for a COMPLETED dot. If unset, the dot is tinted filledColor instead.")]
    public Sprite filledSprite;
    [Tooltip("Optional sprite used for an INCOMPLETE dot. If unset, the dot is tinted emptyColor instead.")]
    public Sprite emptySprite;
    public Color filledColor = Color.white;
    public Color emptyColor = new Color(1f, 1f, 1f, 0.25f);

    [Header("Name-row research icon")]
    [Tooltip("Icon shown next to the fish name. Hidden below 50% (tier 0), tier1Icon at >=50%, tier2Icon at 100%.")]
    public Image researchIcon;
    [Tooltip("Icon at >=50% research (you'll fill this in).")]
    public Sprite tier1Icon;
    [Tooltip("Icon at 100% research (you'll fill this in).")]
    public Sprite tier2Icon;

    [Header("Optional labels")]
    public TextMeshProUGUI percentText;
    [Tooltip("'Catch fish' row label; if set, Populate appends the current catch count.")]
    public TextMeshProUGUI catchTaskText;
    [Tooltip("'Zoom in on fish' row label; if set, Populate appends the current zoom-research count.")]
    public TextMeshProUGUI zoomTaskText;

    public void Populate(FishEncyclopediaEntry entry)
    {
        if (entry == null) return;

        for (int i = 0; i < dots.Length && i < FishResearch.AllItems.Length; i++)
        {
            if (dots[i] == null) continue;
            bool done = FishResearch.IsComplete(entry, FishResearch.AllItems[i]);
            SetDot(dots[i], done);
        }

        if (researchIcon != null)
        {
            int tier = FishResearch.Tier(entry);
            Sprite icon = tier == 2 ? tier2Icon : (tier == 1 ? tier1Icon : null);
            researchIcon.sprite = icon;
            researchIcon.enabled = icon != null;
        }

        if (percentText != null)
            percentText.text = $"{Mathf.RoundToInt(FishResearch.Percent(entry) * 100f)}%";

        if (catchTaskText != null)
            catchTaskText.text = $"Catch fish ({entry.hasCaught})";
        if (zoomTaskText != null)
            zoomTaskText.text = $"Zoom in on fish ({entry.zoomResearchCount})";
    }

    private void SetDot(Image dot, bool done)
    {
        Sprite s = done ? filledSprite : emptySprite;
        if (s != null) dot.sprite = s;
        dot.color = done ? filledColor : emptyColor;
    }
}
