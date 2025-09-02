using UnityEngine;
using System.Collections;

[RequireComponent(typeof(VerletRope))]
public class FishingLine : MonoBehaviour
{
    [Header("References")]
    public Transform rodTip;

    [Header("Bobber")]
    public GameObject bobberPrefab;

    [Header("Reeling Animation")]
    public float reelInArcHeight = 2f;
    public float reelInAnimationTime = 0.5f;
    [Tooltip("The delay in seconds before the bobber starts moving, to sync with the player animation.")]
    public float reelInStartDelay = 0.25f;

    [Tooltip("How long the reel-in animation takes when a fish is attached.")]
    public float reelInAnimationTimeWithFish = 1.5f;

    [Tooltip("How fast the fish model tumbles while being reeled in.")]
    public float fishTumbleSpeed = 360f;


    private VerletRope verletRope;
    private BobberController activeBobber;

    void Awake()
    {
        verletRope = GetComponent<VerletRope>();
        verletRope.DeactivateRope();
    }

    private void OnEnable()
    {
        FishingEvents.OnThrowBobber += SpawnAndAttachBobber;
        FishingEvents.OnCancelFishing += CancelAndDestroyBobber;
        FishingEvents.OnStartReeling += StartReelInAnimation;
    }

    private void OnDisable()
    {
        FishingEvents.OnThrowBobber -= SpawnAndAttachBobber;
        FishingEvents.OnCancelFishing -= CancelAndDestroyBobber;
        FishingEvents.OnStartReeling -= StartReelInAnimation;
    }

    private void SpawnAndAttachBobber(Vector3 direction, float force)
    {
        GameObject bobberInstance = Instantiate(bobberPrefab, rodTip.position, Quaternion.identity);
        activeBobber = bobberInstance.GetComponent<BobberController>();
        if (activeBobber == null)
        {
            Debug.LogError("The bobberPrefab is missing the BobberController script!");
            Destroy(bobberInstance);
            return;
        }
        activeBobber.GetComponent<Rigidbody>().AddForce(direction * force, ForceMode.VelocityChange);
        verletRope.SetupRope(rodTip, activeBobber.transform);
    }

    private void CancelAndDestroyBobber()
    {
        StopAllCoroutines();
        DestroyBobber();
    }

    private void DestroyBobber()
    {
        verletRope.DeactivateRope();
        if (activeBobber != null)
        {
            Destroy(activeBobber.gameObject);
            activeBobber = null;
        }
    }

    private void StartReelInAnimation()
    {
        if (activeBobber != null)
        {
            StartCoroutine(DelayedReelInRoutine());
        }
    }

    private IEnumerator DelayedReelInRoutine()
    {
        yield return new WaitForSeconds(reelInStartDelay);

        if (activeBobber != null)
        {
            StartCoroutine(ReelInBobberRoutine());
        }
    }

    // --- MODIFIED ROUTINE ---
    private IEnumerator ReelInBobberRoutine()
    {
        if (activeBobber.TryGetComponent<Rigidbody>(out var bobberRb))
        {
            bobberRb.isKinematic = true;
        }

        Vector3 startPoint = activeBobber.transform.position;
        float elapsedTime = 0f;

        bool hasFish = activeBobber.HookedFish != null;

        // NEW: If there's a fish, tell the bobber to swap its model NOW.
        if (hasFish)
        {
            activeBobber.SwapBobberForFishModel();
        }

        // Get the reference to the fish model *after* it has been instantiated.
        GameObject fishModel = activeBobber.ActiveFishModel;
        float duration = hasFish ? reelInAnimationTimeWithFish : reelInAnimationTime;

        while (elapsedTime < duration)
        {
            if (activeBobber == null) yield break;

            float t = elapsedTime / duration;
            Vector3 endPoint = rodTip.position;

            Vector3 controlPoint = (startPoint + endPoint) / 2f + Vector3.up * reelInArcHeight;
            Vector3 m1 = Vector3.Lerp(startPoint, controlPoint, t);
            Vector3 m2 = Vector3.Lerp(controlPoint, endPoint, t);
            activeBobber.transform.position = Vector3.Lerp(m1, m2, t);

            if (fishModel != null)
            {
                fishModel.transform.Rotate(new Vector3(1f, 0.5f, 0.2f), fishTumbleSpeed * Time.deltaTime);
            }

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        DestroyBobber();
        FishingEvents.OnReelingCompleted?.Invoke();
    }
}