using UnityEngine;
using TMPro;

public class CurrencyHUD : MonoBehaviour
{
    public TextMeshProUGUI currencyText;
    public string prefix = "Coins: ";

    private void Start()
    {
        UpdateDisplay();

        // Subscribe to inventory changes to update the coin count automatically
        if (PlayerInventory.Instance != null)
        {
            PlayerInventory.Instance.OnInventoryChanged += UpdateDisplay;
        }
    }

    private void OnDestroy()
    {
        if (PlayerInventory.Instance != null)
        {
            PlayerInventory.Instance.OnInventoryChanged -= UpdateDisplay;
        }
    }

    private void UpdateDisplay()
    {
        if (PlayerInventory.Instance != null && currencyText != null)
        {
            currencyText.text = $"{prefix}{PlayerInventory.Instance.currentCurrency}";
        }
    }
}