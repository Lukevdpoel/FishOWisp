using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    // --- Singleton and Mode ---
    public static InventoryUI Instance { get; private set; }
    public enum InventoryMode { Viewing, Selling }
    private InventoryMode currentMode;

    [Header("UI References")]
    public GameObject inventoryPanel;
    public Transform contentContainer;
    public GameObject inventorySlotPrefab;
    public TextMeshProUGUI currencyText;
    public TextMeshProUGUI titleText; // To show "Inventory" or "Sell"
    public Button sellButton;
    public Button sellAllButton;

    [Header("Data Source")]
    public PlayerInventory playerInventory;

    [Header("Navigation Settings")]
    public float initialMoveDelay = 0.5f;
    public float fastMoveRate = 0.1f;

    private List<InventorySlotUI> inventorySlots = new List<InventorySlotUI>();
    private int currentSelectedIndex = -1;
    private int columnCount = 0;
    private GridLayoutGroup gridLayout;
    private float moveTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    void Start()
    {
        InitializeInventory();

        // Hook up button listeners
        sellButton.onClick.AddListener(SellSelectedFish);
        sellAllButton.onClick.AddListener(playerInventory.SellAllFish);

        inventoryPanel.SetActive(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.I))
        {
            if (inventoryPanel.activeSelf) Close();
            else Open(InventoryMode.Viewing); // Player 'I' key opens View mode
        }

        if (inventoryPanel.activeSelf)
        {
            // Keyboard navigation only works in Viewing mode
            if (currentMode == InventoryMode.Viewing)
            {
                HandleNavigationInput();
            }

            // A dedicated "Sell" key for keyboard users
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            {
                SellSelectedFish();
            }
        }
    }

    private void InitializeInventory()
    {
        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }
        gridLayout = contentContainer.GetComponent<GridLayoutGroup>();
        if (gridLayout != null)
        {
            columnCount = gridLayout.constraintCount;
        }
        for (int i = 0; i < playerInventory.inventorySize; i++)
        {
            GameObject slotGO = Instantiate(inventorySlotPrefab, contentContainer);
            InventorySlotUI slotUI = slotGO.GetComponent<InventorySlotUI>();
            slotUI.Clear();
            inventorySlots.Add(slotUI);
        }
    }

    public void Open(InventoryMode mode)
    {
        currentMode = mode;
        inventoryPanel.SetActive(true);

        if (mode == InventoryMode.Viewing)
        {
            titleText.text = "Inventory";
            sellButton.gameObject.SetActive(false);
            sellAllButton.gameObject.SetActive(false);
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            // Select first item for keyboard navigation
            if (inventorySlots.Count > 0)
            {
                MoveSelection(0, true);
            }
        }
        else // Selling Mode
        {
            titleText.text = "Sell Fish";
            sellButton.gameObject.SetActive(true);
            sellAllButton.gameObject.SetActive(true);
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            DeselectCurrentSlot();
        }

        UpdateDisplay();
    }

    public void Close()
    {
        inventoryPanel.SetActive(false);
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        DeselectCurrentSlot();
    }

    public void UpdateDisplay()
    {
        UpdateCurrencyDisplay();
        UpdateFishDisplay();
        // If the selected fish was just sold, deselect it
        if (currentSelectedIndex != -1 && inventorySlots[currentSelectedIndex].CurrentFish == null)
        {
            DeselectCurrentSlot();
        }
    }

    public void SelectSlot(InventorySlotUI slotToSelect)
    {
        if (currentMode != InventoryMode.Selling) return; // Mouse clicks only for selling

        int slotIndex = inventorySlots.IndexOf(slotToSelect);
        if (slotIndex != -1)
        {
            MoveSelection(slotIndex, true); // Use MoveSelection to handle highlights
        }
    }

    private void SellSelectedFish()
    {
        if (currentSelectedIndex != -1 && inventorySlots[currentSelectedIndex].CurrentFish != null)
        {
            playerInventory.SellFish(inventorySlots[currentSelectedIndex].CurrentFish);
        }
    }

    private void DeselectCurrentSlot()
    {
        if (currentSelectedIndex != -1 && inventorySlots.Count > currentSelectedIndex)
        {
            inventorySlots[currentSelectedIndex].Deselect();
        }
        currentSelectedIndex = -1;
        sellButton.interactable = false;
    }

    private void HandleNavigationInput()
    {
        // This is a placeholder for your existing rapid-scroll logic
        if (Input.GetKeyDown(KeyCode.W)) MoveSelection(-columnCount);
        if (Input.GetKeyDown(KeyCode.S)) MoveSelection(columnCount);
        if (Input.GetKeyDown(KeyCode.A)) MoveSelection(-1);
        if (Input.GetKeyDown(KeyCode.D)) MoveSelection(1);
    }

    public void UpdateFishDisplay()
    {
        for (int i = 0; i < inventorySlots.Count; i++)
        {
            if (i < playerInventory.caughtFishes.Count)
            {
                inventorySlots[i].Populate(playerInventory.caughtFishes[i]);
            }
            else
            {
                inventorySlots[i].Clear();
            }
        }
    }

    public void UpdateCurrencyDisplay()
    {
        if (currencyText != null)
        {
            currencyText.text = $"Coins: {playerInventory.currentCurrency}";
        }
    }

    private void MoveSelection(int offset, bool absolute = false)
    {
        int potentialNewIndex;
        if (absolute)
        {
            potentialNewIndex = offset;
        }
        else
        {
            potentialNewIndex = currentSelectedIndex + offset;
        }

        if (offset != 0 && !absolute)
        {
            if (offset == -1 && currentSelectedIndex % columnCount == 0) return;
            if (offset == 1 && (currentSelectedIndex + 1) % columnCount == 0 && columnCount > 0) return;
            if (potentialNewIndex < 0 || potentialNewIndex >= inventorySlots.Count) return;
        }

        if (currentSelectedIndex != -1)
        {
            inventorySlots[currentSelectedIndex].Deselect();
        }

        currentSelectedIndex = potentialNewIndex;
        inventorySlots[currentSelectedIndex].Select();

        // Enable sell button only if the selected slot has a fish
        sellButton.interactable = inventorySlots[currentSelectedIndex].CurrentFish != null;
    }
}

