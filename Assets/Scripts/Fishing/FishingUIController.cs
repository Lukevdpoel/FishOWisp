using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FishingUIController : MonoBehaviour
{
    [Header("Fight UI")]
    public GameObject fightUIPanel;
    public Slider progressBar;
    public TextMeshProUGUI fishNameText;

    [Header("Charge UI")]
    public GameObject chargeUIPanel;
    public Slider chargeSlider;

    private void OnEnable()
    {
        // Fight UI Events
        FishingEvents.OnFishFightBegin += ShowFightUI;
        FishingEvents.OnFishFightEnd += HideFightUI;
        FishingEvents.OnFishFightProgressUpdate += UpdateProgress;

        // FIX: Also hide UI when fishing is cancelled (e.g. line snap / fail)
        FishingEvents.OnCancelFishing += HideFightUIOnCancel;

        // Charge UI Events
        FishingEvents.OnToggleChargeUI += ToggleChargeUI;
        FishingEvents.OnUpdateChargeUI += UpdateChargeSlider;
    }

    private void OnDisable()
    {
        FishingEvents.OnFishFightBegin -= ShowFightUI;
        FishingEvents.OnFishFightEnd -= HideFightUI;
        FishingEvents.OnFishFightProgressUpdate -= UpdateProgress;

        FishingEvents.OnCancelFishing -= HideFightUIOnCancel;

        FishingEvents.OnToggleChargeUI -= ToggleChargeUI;
        FishingEvents.OnUpdateChargeUI -= UpdateChargeSlider;
    }

    void Start()
    {
        if (fightUIPanel != null)
        {
            fightUIPanel.SetActive(false);
        }
        if (chargeUIPanel != null)
        {
            chargeUIPanel.SetActive(false);
        }
    }

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

    private void HideFightUI(bool success)
    {
        if (fightUIPanel != null)
        {
            fightUIPanel.SetActive(false);
        }
    }

    // Helper method for the OnCancelFishing event which has no arguments
    private void HideFightUIOnCancel()
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

    private void ToggleChargeUI(bool show)
    {
        if (chargeUIPanel != null)
        {
            chargeUIPanel.SetActive(show);
        }
    }

    private void UpdateChargeSlider(float current, float max)
    {
        if (chargeSlider != null)
        {
            chargeSlider.maxValue = max;
            chargeSlider.value = current;
        }
    }
}