using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI; // Required for GridLayoutGroup

public class InventoryUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject inventoryPanel;
    public Transform contentContainer;
    public GameObject inventorySlotPrefab;
    public TextMeshProUGUI currencyText;

    [Header("Data Source")]
    public PlayerInventory playerInventory;

    [Header("Navigation Settings")]
    [Tooltip("The initial delay before repeated movement starts when a key is held down.")]
    public float initialMoveDelay = 0.5f;
    [Tooltip("The speed of repeated movement after the initial delay.")]
    public float fastMoveRate = 0.1f;

    private List<InventorySlotUI> inventorySlots = new List<InventorySlotUI>();

    private int currentSelectedIndex = -1;
    private int columnCount = 0;
    private GridLayoutGroup gridLayout;
    private float moveTimer;

    void Start()
    {
        InitializeInventory();
        UpdateCurrencyDisplay();

        inventoryPanel.SetActive(false);
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.I))
        {
            if (inventoryPanel.activeSelf) Close();
            else Open();
        }

        if (inventoryPanel.activeSelf)
        {
            HandleNavigationInput();
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

    private void HandleNavigationInput()
    {
        if (columnCount == 0 || inventorySlots.Count == 0) return;

        // --- Handle initial key presses ---
        if (Input.GetKeyDown(KeyCode.W))
        {
            MoveSelection(-columnCount);
            moveTimer = initialMoveDelay; // Start the initial delay timer
            return; // Exit to prevent the held-key logic from running on the same frame
        }
        if (Input.GetKeyDown(KeyCode.S))
        {
            MoveSelection(columnCount);
            moveTimer = initialMoveDelay;
            return;
        }
        if (Input.GetKeyDown(KeyCode.A))
        {
            MoveSelection(-1);
            moveTimer = initialMoveDelay;
            return;
        }
        if (Input.GetKeyDown(KeyCode.D))
        {
            MoveSelection(1);
            moveTimer = initialMoveDelay;
            return;
        }

        // --- Handle held keys for rapid scrolling ---
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D))
        {
            if (moveTimer > 0)
            {
                // Use unscaledDeltaTime because the game is paused (timeScale = 0)
                moveTimer -= Time.unscaledDeltaTime;
            }
            else
            {
                // Timer has expired, so move again and reset the timer to the fast rate
                if (Input.GetKey(KeyCode.W)) MoveSelection(-columnCount);
                else if (Input.GetKey(KeyCode.S)) MoveSelection(columnCount);
                else if (Input.GetKey(KeyCode.A)) MoveSelection(-1);
                else if (Input.GetKey(KeyCode.D)) MoveSelection(1);

                moveTimer = fastMoveRate;
            }
        }
    }

    private void MoveSelection(int indexOffset)
    {
        // Store the potential new index before making changes
        int potentialNewIndex = currentSelectedIndex + indexOffset;

        // --- HORIZONTAL BOUNDARY CHECKS ---
        // Prevent moving left from the first column
        if (indexOffset == -1 && currentSelectedIndex % columnCount == 0)
        {
            return; // At left edge, do nothing
        }
        // Prevent moving right from the last column
        if (indexOffset == 1 && (currentSelectedIndex + 1) % columnCount == 0 && columnCount > 0)
        {
            return; // At right edge, do nothing
        }

        // --- VERTICAL & LIST BOUNDARY CHECKS ---
        // Prevent moving past the top or bottom of the entire list of slots
        if (potentialNewIndex < 0 || potentialNewIndex >= inventorySlots.Count)
        {
            return; // At top or bottom edge, do nothing
        }

        // --- If all checks pass, the move is valid ---
        // Deselect the previously selected slot
        if (currentSelectedIndex != -1)
        {
            inventorySlots[currentSelectedIndex].Deselect();
        }

        // Update the index and select the new slot
        currentSelectedIndex = potentialNewIndex;
        inventorySlots[currentSelectedIndex].Select();
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

    public void Open()
    {
        inventoryPanel.SetActive(true);
        UpdateFishDisplay();
        UpdateCurrencyDisplay();

        Time.timeScale = 0f;

        if (inventorySlots.Count > 0)
        {
            currentSelectedIndex = 0;
            inventorySlots[currentSelectedIndex].Select();
        }
    }

    public void Close()
    {
        inventoryPanel.SetActive(false);

        Time.timeScale = 1f;

        if (currentSelectedIndex != -1)
        {
            inventorySlots[currentSelectedIndex].Deselect();
            currentSelectedIndex = -1;
        }
    }
}

