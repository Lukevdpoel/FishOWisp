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
    private bool bobberInside = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Bobber"))
        {
            bobberInside = true;
            Debug.Log($"Bobber entered pool: {fishPool.poolName}");

            if (fishCatchable && catchRoutine == null)
            {
                catchRoutine = StartCoroutine(CatchFishAfterDelay());
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Bobber"))
        {
            bobberInside = false;
            Debug.Log($"Bobber exited pool: {fishPool.poolName}");

            // Stop the catch timer if the bobber leaves
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

        if (bobberInside && fishCatchable)
        {
            TryCatchFish();
        }

        catchRoutine = null; // reset so it can start again next time
    }

    public void TryCatchFish()
    {
        if (fishPool == null || fishPool.availableFish.Count == 0)
        {
            Debug.LogWarning("Fish pool is empty or not assigned.");
            return;
        }

        var preset = fishPool.availableFish[Random.Range(0, fishPool.availableFish.Count)];
        float length = Random.Range(preset.minLengthCm, preset.maxLengthCm);

        FishEncyclopediaManager.Instance.RegisterCaughtFish(preset, length);

        Debug.Log($"Caught {preset.fishName} ({length:F1} cm) from {fishPool.poolName}");
    }
}
