using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    public static InventoryUI Instance { get; private set; }

    [Header("UI References")]
    public GameObject bucketCameraObject; // Assign your BucketCamera GameObject
    public GameObject vendorPanel;        // Assign your VendorPanel
    public PlayerInventory playerInventory; // Assign your PlayerInventory

    private bool isOpen = false;

    // We can keep this to update the new VendorUI's currency
    public VendorUI vendorUI;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    void Start()
    {
        // Make sure everything is closed on start
        bucketCameraObject.SetActive(false);
        vendorPanel.SetActive(false);
    }

    void Update()
    {
        // Use 'I' key to toggle the bucket UI
        if (Input.GetKeyDown(KeyCode.I))
        {
            if (isOpen) Close();
            else Open();
        }
    }

    public void Open()
    {
        isOpen = true;
        vendorPanel.SetActive(true);
        bucketCameraObject.SetActive(true);

        Time.timeScale = 0f; // Pauses the game

        // --- THIS IS THE FIX ---
        // Unlocks the cursor and makes it visible so you can click on UI
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        // ---

        if (vendorUI != null)
        {
            vendorUI.UpdateCurrencyDisplay();
        }
    }

    public void Close()
    {
        isOpen = false;
        vendorPanel.SetActive(false);
        bucketCameraObject.SetActive(false);

        Time.timeScale = 1f; // Resumes the game

        // --- THIS RE-LOCKS THE CURSOR FOR GAMEPLAY ---
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        // ---
    }

    // You can call this from other scripts if needed
    public void UpdateCurrencyDisplay()
    {
        if (vendorUI != null)
        {
            vendorUI.UpdateCurrencyDisplay();
        }
    }

    // This is needed by PlayerInventory.cs to refresh the UI after a sale
    public void UpdateDisplay()
    {
        if (vendorUI != null)
        {
            vendorUI.UpdateCurrencyDisplay();
        }
    }
}