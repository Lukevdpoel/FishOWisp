using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FishEntryUI : MonoBehaviour
{
    public Image fishImage;
    public TextMeshProUGUI fishNameText;
    public TextMeshProUGUI sizeClassText;
    public TextMeshProUGUI caughtText;
    public TextMeshProUGUI largestCaughtText;
    public TextMeshProUGUI smallestCaughtText;
    public TextMeshProUGUI basePriceText;
    public TextMeshProUGUI pricePerCmText; // New UI element
    public TextMeshProUGUI baitText;
    public TextMeshProUGUI weatherText;

    [Tooltip("Optional research checklist for this page (catch/zoom dots + name-row icon). Wire it in the notebook prefab.")]
    public ResearchChecklistUI researchChecklist;

    [Button]
    public void Populate(FishEncyclopediaEntry entry)
    {
        if (entry == null)
        {
            return;
        }
        var preset = entry.preset;

        // Uncaught fish stay a mystery: black silhouette model, "???" for the name and the
        // bait/weather preferences. Everything reveals on the first catch.
        bool revealed = entry.hasCaught > 0;

        // Null-guard: FishEncyclopediaManager calls Populate from its Awake/OnEnable during
        // scene load, and Awake order isn't guaranteed — ModelViewer's Awake may not have
        // registered the singleton yet. Skipping the 3D preview here is harmless: the
        // encyclopedia re-populates via OnSlotClicked when the notebook actually opens,
        // by which point the viewer exists.
        if (ModelViewer.Instance != null)
        {
            ModelViewer.Instance.ShowModel(preset, revealed);
        }

        //fishImage.sprite = preset.fishImage;
        fishNameText.text = revealed ? preset.fishName : "???";
        if(sizeClassText != null )
            sizeClassText.text = preset.sizeClass.ToString();
        if( caughtText != null )
            caughtText.text = entry.hasCaught.ToString();

        if (entry.hasCaught > 0)
        {
            if(largestCaughtText != null)
                largestCaughtText.text = $"{entry.largestCaught:F1} cm";
            if (smallestCaughtText != null)
                smallestCaughtText.text = $"{entry.smallestCaught:F1} cm";
        }
        else
        {
            if (largestCaughtText != null)
                largestCaughtText.text = "???";
            if (smallestCaughtText != null)
                smallestCaughtText.text = "???";
        }

        if (basePriceText != null)
            basePriceText.text = $"{preset.basePrice} coins";
        // Populate the new text field to show the value-per-cm

        if (pricePerCmText != null)
            pricePerCmText.text = $"+{preset.pricePerCm:F1} / cm";

        if (baitText != null)
        {
            // Bait prefs reveal once the fish has been zoom-researched OR caught (not catch-only),
            // so the Arceus-style zoom loop unlocks them before the first catch.
            if (!FishEncyclopediaManager.BaitRevealed(entry))
            {
                baitText.text = "???";
            }
            else
            {
                // What actually draws this species to the hook: its bait list (bobber path),
                // the lure, or both. attractedTo gates the bait list — lure-only species
                // ignore bait entirely, so their leftover preferredBaits data must not leak
                // into the notebook. An empty bait list on a bobber-responsive fish means it
                // takes any bait (see BaitInventory.PresetAcceptsSelectedBait), not none.
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                if (preset.RespondsToBobber)
                {
                    if (preset.preferredBaits != null)
                    {
                        for (int i = 0; i < preset.preferredBaits.Count; i++)
                        {
                            BaitItem b = preset.preferredBaits[i];
                            if (b == null) continue;
                            if (sb.Length > 0) sb.Append(", ");
                            sb.Append(string.IsNullOrEmpty(b.displayName) ? b.name : b.displayName);
                        }
                    }
                    if (sb.Length == 0) sb.Append("Any bait");
                }
                if (preset.RespondsToLure)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(preset.prefersPopper ? "Lure (Popper)" : "Lure");
                }
                baitText.text = sb.ToString();
            }
        }

        if (weatherText != null)
            weatherText.text = revealed ? preset.TimeOfDayLabel : "???";

        if (researchChecklist != null)
            researchChecklist.Populate(entry);
    }
}