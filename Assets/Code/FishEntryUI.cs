using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FishEntryUI : MonoBehaviour
{
    public Image fishImage;
    public TextMeshProUGUI fishNameText;
    public TextMeshProUGUI rarityText;
    public TextMeshProUGUI caughtText;
    public TextMeshProUGUI largestCaughtText;
    public TextMeshProUGUI smallestCaughtText;
    public TextMeshProUGUI basePriceText;
    public TextMeshProUGUI baitText;
    public TextMeshProUGUI weatherText;

    [Button]
    public void Populate(FishEncyclopediaEntry entry)
    {
        var preset = entry.preset;
        ModelViewer.Instance.ShowModel(preset);

        //fishImage.sprite = preset.fishImage;
        fishNameText.text = preset.fishName;
        rarityText.text = preset.rarity.ToString();
        caughtText.text = entry.hasCaught.ToString();

        if (entry.hasCaught >= 0)
        {
            largestCaughtText.text = $"{entry.largestCaught:F1} cm";
            smallestCaughtText.text = $"{entry.smallestCaught:F1} cm";
        }
        else
        {
            largestCaughtText.text = "—";
            smallestCaughtText.text = "—";
        }

        basePriceText.text = $"{preset.basePrice} coins";
        baitText.text = preset.preferredBait.ToString();
        weatherText.text = preset.preferredWeather.ToString();
    }
}
