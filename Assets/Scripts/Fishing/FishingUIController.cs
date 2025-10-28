using UnityEngine;
using UnityEngine.UI; // Required for UI elements like Slider
using TMPro; // Required if you use TextMeshPro for text

public class FishingUIController : MonoBehaviour
{
    [Header("Fight UI")]
    public GameObject fightUIPanel; // The parent object of all fight UI elements
    public Slider progressBar;      // The slider to show catch progress
    public TextMeshProUGUI fishNameText; // Optional: Text to show the fish's name

    // NEW: Header and variables for the charge UI
    [Header("Charge UI")]
    public GameObject chargeUIPanel; // The parent object for the charge slider
    public Slider chargeSlider;      // The slider for casting power

    private void OnEnable()
    {
        // Fight UI Events
        FishingEvents.OnFishFightBegin += ShowFightUI;
        FishingEvents.OnFishFightEnd += HideFightUI;
        FishingEvents.OnFishFightProgressUpdate += UpdateProgress;

        // NEW: Charge UI Events
        FishingEvents.OnToggleChargeUI += ToggleChargeUI;
        FishingEvents.OnUpdateChargeUI += UpdateChargeSlider;
    }

    private void OnDisable()
    {
        // Fight UI Events
        FishingEvents.OnFishFightBegin -= ShowFightUI;
        FishingEvents.OnFishFightEnd -= HideFightUI;
        FishingEvents.OnFishFightProgressUpdate -= UpdateProgress;

        // NEW: Unsubscribe from Charge UI Events
        FishingEvents.OnToggleChargeUI -= ToggleChargeUI;
        FishingEvents.OnUpdateChargeUI -= UpdateChargeSlider;
    }

    void Start()
    {
        // Start with both UI panels hidden
        if (fightUIPanel != null)
        {
            fightUIPanel.SetActive(false);
        }
        // NEW: Hide the charge UI at the start
        if (chargeUIPanel != null)
        {
            chargeUIPanel.SetActive(false);
        }
    }

    // Renamed from ShowUI to be more specific
    private void ShowFightUI(FishPreset fish)
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

    // Renamed from HideUI to be more specific
    private void HideFightUI(bool success)
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

    // NEW: Method to show or hide the entire charge UI panel
    private void ToggleChargeUI(bool show)
    {
        if (chargeUIPanel != null)
        {
            chargeUIPanel.SetActive(show);
        }
    }

    // NEW: Method to update the charge slider's value
    private void UpdateChargeSlider(float current, float max)
    {
        if (chargeSlider != null)
        {
            chargeSlider.maxValue = max;
            chargeSlider.value = current;
            Debug.Log("chargeSliderCalled");
        }
    }
}