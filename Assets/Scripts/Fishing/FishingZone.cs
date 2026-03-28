using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public class FishingZone : MonoBehaviour
{
    public FishPool fishPool;

    [Header("Interest Settings")]
    [Tooltip("How fast interest passively builds per second (0 to 1 scale).")]
    public float passiveInterestRate = 0.05f;
    [Tooltip("How much interest each attract input adds.")]
    public float attractInterestBoost = 0.15f;
    [Tooltip("Interest must reach this to trigger a bite.")]
    public float interestThreshold = 1f;

    [Header("Scare Settings")]
    [Tooltip("Max attract presses allowed within the scare window.")]
    public int maxAttractsBeforeScare = 3;
    [Tooltip("Time window in seconds for tracking attract presses.")]
    public float scareWindow = 2f;
    [Tooltip("Cooldown after fish are scared before interest can build again.")]
    public float scareCooldown = 4f;

    private BobberController currentBobber;
    private float currentInterest;
    private bool isActive;
    private bool isScared;
    private float scareTimer;

    private List<float> attractTimestamps = new List<float>();

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnEnable()
    {
        FishingEvents.OnAttractFish += HandleAttract;
    }

    private void OnDisable()
    {
        FishingEvents.OnAttractFish -= HandleAttract;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent<BobberController>(out var bobber))
        {
            Rigidbody bobberRb = bobber.GetComponent<Rigidbody>();
            if (bobberRb != null && bobberRb.isKinematic)
                return;

            Debug.Log($"Bobber entered pool: {fishPool.poolName}");
            currentBobber = bobber;
            currentInterest = 0f;
            isActive = true;
            isScared = false;
            scareTimer = 0f;
            attractTimestamps.Clear();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent<BobberController>(out var bobber) && bobber == currentBobber)
        {
            Debug.Log($"Bobber exited pool: {fishPool.poolName}");
            ResetZone();
        }
    }

    void Update()
    {
        if (!isActive || currentBobber == null) return;
        if (fishPool == null || fishPool.availableFish.Count == 0) return;

        if (isScared)
        {
            scareTimer -= Time.deltaTime;
            if (scareTimer <= 0f)
            {
                isScared = false;
                currentInterest = 0f;
                Debug.Log("Fish are no longer scared.");
            }
            return;
        }

        // Passive interest buildup
        currentInterest += passiveInterestRate * Time.deltaTime;

        if (currentInterest >= interestThreshold)
        {
            TriggerBite();
        }
    }

    private void HandleAttract()
    {
        if (!isActive || currentBobber == null || isScared) return;

        float now = Time.time;

        // Prune old timestamps outside the window
        attractTimestamps.RemoveAll(t => now - t > scareWindow);
        attractTimestamps.Add(now);

        if (attractTimestamps.Count > maxAttractsBeforeScare)
        {
            // Scared!
            isScared = true;
            scareTimer = scareCooldown;
            currentInterest = 0f;
            attractTimestamps.Clear();
            Debug.Log("Fish have been scared off!");
            FishingEvents.OnFishScared?.Invoke();
            return;
        }

        currentInterest += attractInterestBoost;
        Debug.Log($"Fish attracted! Interest: {currentInterest:F2}/{interestThreshold}");

        // Bobber jiggle feedback
        if (currentBobber != null)
        {
            currentBobber.PlayAttractJiggle();
        }
    }

    private void TriggerBite()
    {
        isActive = false;
        var fishToCatch = fishPool.availableFish[Random.Range(0, fishPool.availableFish.Count)];
        Debug.Log($"A {fishToCatch.fishName} is biting!");

        if (currentBobber != null)
        {
            Rigidbody bobberRb = currentBobber.GetComponent<Rigidbody>();
            if (bobberRb != null && !bobberRb.isKinematic)
            {
                currentBobber.StartNibbleSequence(fishToCatch);
            }
        }
    }

    private void ResetZone()
    {
        currentBobber = null;
        currentInterest = 0f;
        isActive = false;
        isScared = false;
        scareTimer = 0f;
        attractTimestamps.Clear();
    }

    // Legacy method kept for external use
    public void HookFish(FishPreset preset)
    {
        if (currentBobber != null)
        {
            currentBobber.HookFish(preset);
        }
    }
}
