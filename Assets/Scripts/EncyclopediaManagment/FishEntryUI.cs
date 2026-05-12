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

    [Button]
    public void Populate(FishEncyclopediaEntry entry)
    {
        if (entry == null)
        {
            return;
        }
        var preset = entry.preset;
        ModelViewer.Instance.ShowModel(preset);

        //fishImage.sprite = preset.fishImage;
        fishNameText.text = preset.fishName;
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
            if (preset.preferredBaits == null || preset.preferredBaits.Count == 0)
            {
                baitText.text = "None";
            }
            else
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < preset.preferredBaits.Count; i++)
                {
                    BaitItem b = preset.preferredBaits[i];
                    if (b == null) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(string.IsNullOrEmpty(b.displayName) ? b.name : b.displayName);
                }
                baitText.text = sb.Length > 0 ? sb.ToString() : "None";
            }
        }

        if (weatherText != null)
            weatherText.text = preset.preferredWeather.ToString();
    }
}