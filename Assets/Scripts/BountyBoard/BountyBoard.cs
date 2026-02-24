using UnityEngine;
using TMPro;
using System;
using System.Collections.Generic;

[System.Serializable]
public class BountyData
{
    public string targetFishName;
    public int amountRequired;
    public int amountDelivered;
    public int totalReward;
    public bool isCompleted;
}

public class BountyBoard : MonoBehaviour
{
    // Event to notify the PlayerController to lock controls and frame the camera
    public static event Action<bool, Transform> OnBountyBoardStateChange;

    [Header("Bounty Settings")]
    public int bountiesPerDay = 5;
    public float rewardMultiplier = 1.5f; // Bounties pay 50% more than base price
    public int minRequired = 1;
    public int maxRequired = 4;

    [Header("UI & Interaction")]
    public GameObject promptUI; // The 'E' to interact prompt
    public GameObject bountyPanelUI; // The actual board UI canvas
    public TextMeshProUGUI bountyText; // Text displaying the bounty list

    // Internal Bounty State
    private List<BountyData> dailyBounties = new List<BountyData>();
    private bool playerInZone;
    private bool isViewingBoard = false;

    void Start()
    {
        if (promptUI != null) promptUI.SetActive(false);
        if (bountyPanelUI != null) bountyPanelUI.SetActive(false);

        CheckDailyBounty();
    }

    void Update()
    {
        // --- DEBUG SHORTCUT: Force Refresh Bounties ---
        if (Input.GetKeyDown(KeyCode.F5))
        {
            PlayerPrefs.DeleteKey("BountyDate");
            PlayerPrefs.DeleteKey("BountyCount");
            CheckDailyBounty();
            Debug.Log("Forced Bounty Board Refresh!");
        }
        // ----------------------------------------------

        if (playerInZone && !isViewingBoard && Input.GetKeyDown(KeyCode.E))
        {
            OpenBoard();
        }
        else if (isViewingBoard)
        {
            // Exit on E, Escape, or Right Click
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(1))
            {
                CloseBoard();
            }
        }
    }

    private void CheckDailyBounty()
    {
        string savedDate = PlayerPrefs.GetString("BountyDate", "");
        string currentDate = DateTime.Now.Date.ToString();
        int savedCount = PlayerPrefs.GetInt("BountyCount", 0);

        // If the date doesn't match, OR if the save file says we have 0 bounties, generate new ones!
        if (savedDate != currentDate || savedCount == 0)
        {
            GenerateNewBounties(currentDate);
        }
        else
        {
            LoadExistingBounties();
        }

        UpdateUI();
    }

    private void GenerateNewBounties(string date)
    {
        if (FishEncyclopediaManager.Instance == null || FishEncyclopediaManager.Instance.allFishPresets.Count == 0)
        {
            if (bountyText != null) bountyText.text = "Error: Encyclopedia missing or has no fish presets!";
            return;
        }

        dailyBounties.Clear();
        PlayerPrefs.SetString("BountyDate", date);

        List<FishPreset> availablePresets = new List<FishPreset>();
        foreach (var preset in FishEncyclopediaManager.Instance.allFishPresets)
        {
            if (preset != null) availablePresets.Add(preset);
        }

        for (int i = 0; i < bountiesPerDay; i++)
        {
            if (availablePresets.Count == 0) break;

            int randIndex = UnityEngine.Random.Range(0, availablePresets.Count);
            FishPreset chosenFish = availablePresets[randIndex];
            availablePresets.RemoveAt(randIndex);

            BountyData newBounty = new BountyData
            {
                targetFishName = chosenFish.fishName,
                amountRequired = UnityEngine.Random.Range(minRequired, maxRequired + 1),
                amountDelivered = 0,
                isCompleted = false
            };

            // Calculate total reward with multiplier
            newBounty.totalReward = Mathf.RoundToInt(chosenFish.basePrice * newBounty.amountRequired * rewardMultiplier);

            dailyBounties.Add(newBounty);
        }

        SaveBounties();
    }

    private void SaveBounties()
    {
        PlayerPrefs.SetInt("BountyCount", dailyBounties.Count);
        for (int i = 0; i < dailyBounties.Count; i++)
        {
            PlayerPrefs.SetString($"BountyFish_{i}", dailyBounties[i].targetFishName);
            PlayerPrefs.SetInt($"BountyReq_{i}", dailyBounties[i].amountRequired);
            PlayerPrefs.SetInt($"BountyDel_{i}", dailyBounties[i].amountDelivered);
            PlayerPrefs.SetInt($"BountyReward_{i}", dailyBounties[i].totalReward);
            PlayerPrefs.SetInt($"BountyDone_{i}", dailyBounties[i].isCompleted ? 1 : 0);
        }
        PlayerPrefs.Save();
    }

    private void LoadExistingBounties()
    {
        dailyBounties.Clear();
        int count = PlayerPrefs.GetInt("BountyCount", 0);

        for (int i = 0; i < count; i++)
        {
            BountyData loadedBounty = new BountyData
            {
                targetFishName = PlayerPrefs.GetString($"BountyFish_{i}", "None"),
                amountRequired = PlayerPrefs.GetInt($"BountyReq_{i}", 1),
                amountDelivered = PlayerPrefs.GetInt($"BountyDel_{i}", 0),
                totalReward = PlayerPrefs.GetInt($"BountyReward_{i}", 100),
                isCompleted = PlayerPrefs.GetInt($"BountyDone_{i}", 0) == 1
            };
            dailyBounties.Add(loadedBounty);
        }
    }

    private void UpdateUI()
    {
        if (bountyText == null) return;

        string displayText = "<b><size=120%>DAILY BOUNTIES</size></b>\n\n";
        bool allCompleted = true;

        foreach (var bounty in dailyBounties)
        {
            if (bounty.isCompleted)
            {
                displayText += $"<s>{bounty.amountRequired} {bounty.targetFishName}</s> - <color=#55FF55>Done!</color>\n";
            }
            else
            {
                allCompleted = false;
                displayText += $"{bounty.amountDelivered}/{bounty.amountRequired} {bounty.targetFishName} - <color=#FFD700>{bounty.totalReward} Coins</color>\n";
            }
        }

        if (allCompleted && dailyBounties.Count > 0)
        {
            displayText += "\n<i>All bounties completed! Come back tomorrow.</i>";
        }

        bountyText.text = displayText;
    }

    public bool TryDeliverFish(CaughtFish fish)
    {
        bool acceptedFish = false;

        foreach (var bounty in dailyBounties)
        {
            if (!bounty.isCompleted && bounty.targetFishName == fish.preset.fishName)
            {
                bounty.amountDelivered++;
                acceptedFish = true;

                if (bounty.amountDelivered >= bounty.amountRequired)
                {
                    bounty.isCompleted = true;
                    PlayerInventory.Instance.TransactionAddCurrency(bounty.totalReward);
                    Debug.Log($"Bounty for {bounty.targetFishName} Completed! Reward: {bounty.totalReward}");
                }

                break;
            }
        }

        if (acceptedFish)
        {
            SaveBounties();
            UpdateUI();
        }

        return acceptedFish;
    }

    private void OpenBoard()
    {
        isViewingBoard = true;
        if (promptUI != null) promptUI.SetActive(false);
        if (bountyPanelUI != null) bountyPanelUI.SetActive(true);

        // Tell PlayerController to lock controls and focus camera on this board
        OnBountyBoardStateChange?.Invoke(true, this.transform);
    }

    private void CloseBoard()
    {
        isViewingBoard = false;
        if (bountyPanelUI != null) bountyPanelUI.SetActive(false);
        if (playerInZone && promptUI != null) promptUI.SetActive(true);

        // Tell PlayerController to release controls and resume standard camera behavior
        OnBountyBoardStateChange?.Invoke(false, null);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = true;
            if (promptUI != null && !isViewingBoard) promptUI.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = false;
            if (promptUI != null) promptUI.SetActive(false);
            if (isViewingBoard) CloseBoard();
        }
    }
}