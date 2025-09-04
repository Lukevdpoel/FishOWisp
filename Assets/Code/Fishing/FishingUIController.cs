using UnityEngine;
using UnityEngine.UI; // Required for UI elements like Slider
using TMPro; // Required if you use TextMeshPro for text

public class FishingUIController : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject fightUIPanel; // The parent object of all fight UI elements
    public Slider progressBar;      // The slider to show catch progress
    public TextMeshProUGUI fishNameText; // Optional: Text to show the fish's name

    private void OnEnable()
    {
        FishingEvents.OnFishFightBegin += ShowUI;
        FishingEvents.OnFishFightEnd += HideUI;
        FishingEvents.OnFishFightProgressUpdate += UpdateProgress;
    }

    private void OnDisable()
    {
        FishingEvents.OnFishFightBegin -= ShowUI;
        FishingEvents.OnFishFightEnd -= HideUI;
        FishingEvents.OnFishFightProgressUpdate -= UpdateProgress;
    }

    void Start()
    {
        // Start with the UI hidden
        if (fightUIPanel != null)
        {
            fightUIPanel.SetActive(false);
        }
    }

    private void ShowUI(FishPreset fish)
    {
        if (fightUIPanel != null)
        {
            fightUIPanel.SetActive(true);
        }

        if (fishNameText != null && fish != null)
        {
            fishNameText.text = $"A {fish.fishName} is on the line!";
        }
    }

    private void HideUI(bool success)
    {
        if (fightUIPanel != null)
        {
            fightUIPanel.SetActive(false);
        }
    }

    private void UpdateProgress(float current, float max)
    {
        if (progressBar != null)
        {
            progressBar.maxValue = max;
            progressBar.value = current;
        }
    }
}