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
            // --- FIX STARTS HERE ---
            // If the bobber is kinematic, it means it's being reeled in by the FishingLine script.
            // We should ignore it so we don't start a new fishing sequence mid-air.
            Rigidbody bobberRb = bobber.GetComponent<Rigidbody>();
            if (bobberRb != null && bobberRb.isKinematic)
            {
                return;
            }
            // --- FIX ENDS HERE ---

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
        if (fishPool == null || fishPool.availableFish.Count == 0)
        {
            Debug.LogWarning("Fish pool is not set up or has no fish.");
            yield break;
        }

        var fishToCatch = fishPool.availableFish[Random.Range(0, fishPool.availableFish.Count)];
        float waitTime = Random.Range(fishPool.minCatchTime, fishPool.maxCatchTime);

        Debug.Log($"Something is interested... A {fishToCatch.fishName} will start nibbling in {waitTime:F1} seconds...");
        yield return new WaitForSeconds(waitTime);

        // Additional safety check: Ensure bobber is still valid and not being reeled in
        if (currentBobber != null)
        {
            Rigidbody bobberRb = currentBobber.GetComponent<Rigidbody>();
            if (bobberRb != null && !bobberRb.isKinematic)
            {
                // Instead of hooking the fish, start the nibble sequence on the bobber.
                currentBobber.StartNibbleSequence(fishToCatch);
            }
        }

        catchRoutine = null;
    }

    // This method is no longer called from within this class, but can be left for other potential uses.
    public void HookFish(FishPreset preset)
    {
        if (currentBobber != null)
        {
            currentBobber.HookFish(preset);
        }
    }
}