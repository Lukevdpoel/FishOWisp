using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class FishingUIController : MonoBehaviour
{
    [Header("Fight UI")]
    public GameObject fightUIPanel;
    public Slider progressBar;
    public TextMeshProUGUI fishNameText;

    [Header("Charge UI (deprecated)")]
    [Tooltip("Legacy on-screen charge panel. Force-hidden — charge feedback now lives on the camera (zoom/pitch).")]
    public GameObject chargeUIPanel;

    private void OnEnable()
    {
        // Fight UI Events
        FishingEvents.OnFishFightBegin += ShowFightUI;
        FishingEvents.OnFishFightEnd += HideFightUI;
        FishingEvents.OnFishFightProgressUpdate += UpdateProgress;

        // FIX: Also hide UI when fishing is cancelled (e.g. line snap / fail)
        FishingEvents.OnCancelFishing += HideFightUIOnCancel;

        // Force-hide the legacy charge bar in case anything else tries to enable it.
        FishingEvents.OnToggleChargeUI += ForceHideChargePanel;

        SceneManager.sceneLoaded += OnSceneLoaded;

        // Re-hide on every enable so a scene reload can't leave the legacy bar visible.
        HideChargePanel();
        HideFightPanel();
    }

    private void OnDisable()
    {
        FishingEvents.OnFishFightBegin -= ShowFightUI;
        FishingEvents.OnFishFightEnd -= HideFightUI;
        FishingEvents.OnFishFightProgressUpdate -= UpdateProgress;

        FishingEvents.OnCancelFishing -= HideFightUIOnCancel;
        FishingEvents.OnToggleChargeUI -= ForceHideChargePanel;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // After a scene swap, panels can come back active even if the script instance is fresh.
        // Force them off so the player doesn't see a phantom charge bar on the next scene.
        HideChargePanel();
        HideFightPanel();
    }

    private void Awake()
    {
        // Hide as early as possible so the legacy bar never flashes on screen.
        HideChargePanel();
    }

    void Start()
    {
        HideFightPanel();
        HideChargePanel();
    }

    private void HideChargePanel()
    {
        if (chargeUIPanel != null)
        {
            chargeUIPanel.SetActive(false);
        }
    }

    private void HideFightPanel()
    {
        if (fightUIPanel != null)
        {
            fightUIPanel.SetActive(false);
        }
    }

    private void ForceHideChargePanel(bool show) => HideChargePanel();

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
}