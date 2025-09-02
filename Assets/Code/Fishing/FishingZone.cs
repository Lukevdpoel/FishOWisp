using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Collider))]
public class FishingZone : MonoBehaviour
{
    public FishPool fishPool;

    private Coroutine catchRoutine;
    private BobberController currentBobber;

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent<BobberController>(out var bobber))
        {
            Debug.Log($"Bobber entered pool: {fishPool.poolName}");
            currentBobber = bobber;
            if (catchRoutine == null)
            {
                catchRoutine = StartCoroutine(CatchFishAfterDelay());
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent<BobberController>(out var bobber) && bobber == currentBobber)
        {
            Debug.Log($"Bobber exited pool: {fishPool.poolName}");
            currentBobber = null;
            if (catchRoutine != null)
            {
                StopCoroutine(catchRoutine);
                catchRoutine = null;
            }
        }
    }

    private IEnumerator CatchFishAfterDelay()
    {
        // First, check if there are any fish to catch
        if (fishPool == null || fishPool.availableFish.Count == 0)
        {
            Debug.LogWarning("Fish pool is not set up or has no fish.");
            yield break; // Exit the coroutine
        }

        // Select a fish preset from the pool *before* the wait begins.
        var fishToCatch = fishPool.availableFish[Random.Range(0, fishPool.availableFish.Count)];

        // Calculate the random wait time.
        float waitTime = Random.Range(fishPool.minCatchTime, fishPool.maxCatchTime);

        // --- NEW DEBUG LOG ---
        // Log the chosen fish and the countdown.
        Debug.Log($"A {fishToCatch.fishName} will bite in {waitTime:F1} seconds...");

        yield return new WaitForSeconds(waitTime);

        // If the bobber is still in the water, hook the fish.
        if (currentBobber != null)
        {
            // The "Try" is removed, as it will now always succeed.
            HookFish(fishToCatch);
        }

        catchRoutine = null; // Allow catching another fish if the bobber stays in the zone.
    }

    // This method is simplified to just hook the fish without a probability check.
    public void HookFish(FishPreset preset)
    {
        // The check for the currentBobber is already done in the coroutine,
        // but it's good practice to keep it here as well.
        if (currentBobber != null)
        {
            currentBobber.HookFish(preset);
        }
    }
}