// FishPoolArea.cs (Updated)
using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Collider))]
public class FishPoolArea : MonoBehaviour
{
    public FishPool fishPool;
    public bool fishCatchable = true;

    [Header("Catch Timing")]
    public float minCatchTime = 2f; // minimum seconds before a catch
    public float maxCatchTime = 5f; // maximum seconds before a catch

    private Coroutine catchRoutine;
    private Bobber currentBobber; // Store a reference to the bobber's script

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Bobber"))
        {
            // Try to get the Bobber script from the object that entered.
            currentBobber = other.GetComponent<Bobber>();

            if (currentBobber == null)
            {
                Debug.LogError("The object with 'Bobber' tag is missing the Bobber script!", other.gameObject);
                return;
            }

            Debug.Log($"Bobber entered pool: {fishPool.poolName}");

            if (fishCatchable && catchRoutine == null)
            {
                catchRoutine = StartCoroutine(CatchFishAfterDelay());
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // Check if the exiting object is the same one we're tracking.
        if (other.CompareTag("Bobber") && other.GetComponent<Bobber>() == currentBobber)
        {
            Debug.Log($"Bobber exited pool: {fishPool.poolName}");
            currentBobber = null; // Clear the reference.

            // Stop the catch timer if the bobber leaves.
            if (catchRoutine != null)
            {
                StopCoroutine(catchRoutine);
                catchRoutine = null;
            }
        }
    }

    private IEnumerator CatchFishAfterDelay()
    {
        float waitTime = Random.Range(minCatchTime, maxCatchTime);
        Debug.Log($"Waiting {waitTime:F1} seconds to try and catch a fish...");

        yield return new WaitForSeconds(waitTime);

        // Check if the bobber is still inside the area before attempting the catch.
        if (currentBobber != null && fishCatchable)
        {
            TryCatchFish();
        }

        catchRoutine = null; // Reset so it can start again next time.
    }

    public void TryCatchFish()
    {
        if (fishPool == null || fishPool.availableFish.Count == 0)
        {
            Debug.LogWarning("Fish pool is empty or not assigned.");
            return;
        }

        if (currentBobber.hookedFish != null)
        {
            Debug.Log("Bobber already has a fish. Won't catch another.");
            return;
        }

        // 1. Select a fish preset from the pool.
        var preset = fishPool.availableFish[Random.Range(0, fishPool.availableFish.Count)];

        if (preset.fishPrefab == null)
        {
            Debug.LogError($"The fish preset '{preset.fishName}' is missing its prefab!");
            return;
        }

        // 2. Instantiate the fish model at the bobber's current position.
        GameObject fishInstance = Instantiate(preset.fishPrefab, currentBobber.transform.position, Quaternion.identity);
        fishInstance.name = preset.fishName; // Give it a clean name in the hierarchy.

        // 3. Call the HookFish method on the bobber's script, passing in the new fish.
        currentBobber.HookFish(fishInstance);

        // This part can stay the same for your encyclopedia.
        float length = Random.Range(preset.minLengthCm, preset.maxLengthCm);
        // FishEncyclopediaManager.Instance.RegisterCaughtFish(preset, length);

        Debug.Log($"Hooked {preset.fishName} ({length:F1} cm) from {fishPool.poolName}. Handing off to Bobber.");

        // Optional: You could make the pool un-fishable for a while.
        // fishCatchable = false;
    }
}
