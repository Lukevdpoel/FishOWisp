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

    private BobberController currentBobber;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Bobber") || other.GetComponent<BobberController>() != null)
        {
            currentBobber = other.GetComponent<BobberController>();

            if (currentBobber == null)
            {
                Debug.LogError("Object tagged 'Bobber' is missing the 'BobberController' script!", other.gameObject);
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
        var exitingBobber = other.GetComponent<BobberController>();

        if (exitingBobber != null && exitingBobber == currentBobber)
        {
            Debug.Log($"Bobber exited pool: {fishPool.poolName}");
            currentBobber = null; // Clear the reference

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

        yield return new WaitForSeconds(waitTime);

        if (currentBobber != null && fishCatchable)
        {
            TryCatchFish();
        }

        catchRoutine = null; 
    }

    public void TryCatchFish()
    {
        if (fishPool == null || fishPool.availableFish.Count == 0)
        {
            Debug.LogWarning("Fish pool is empty or not assigned.");
            return;
        }

        if (currentBobber.HookedFish != null)
        {
            Debug.Log("Bobber already has a fish. Won't catch another.");
            return;
        }

        var preset = fishPool.availableFish[Random.Range(0, fishPool.availableFish.Count)];

        if (preset == null) return;

        Debug.Log($"Fish detected! Starting nibble sequence for: {preset.fishName}");

        currentBobber.StartNibbleSequence(preset);
    }
}