using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Collider))]
public class FishingZone : MonoBehaviour
{
    public FishPool fishPool;

   /* [Header("Catch Timing")]
    public float minCatchTime = 3f;
    public float maxCatchTime = 8f;*/

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
        float waitTime = Random.Range(fishPool.minCatchTime, fishPool.maxCatchTime);
        yield return new WaitForSeconds(waitTime);

        if (currentBobber != null)
        {
            TryCatchFish();
        }
        catchRoutine = null; // Allow catching another fish
    }

    public void TryCatchFish()
    {
        if (fishPool == null || fishPool.availableFish.Count == 0) return;

        // Select a fish and hook it to the bobber
        var preset = fishPool.availableFish[Random.Range(0, fishPool.availableFish.Count)];
        if (Random.value <= preset.catchProbability)
        {
            currentBobber.HookFish(preset);
        }
        else
        {
            Debug.Log($"{preset.fishName} got away!");
            // Restart the timer to try for another fish
            catchRoutine = StartCoroutine(CatchFishAfterDelay());
        }
    }
}