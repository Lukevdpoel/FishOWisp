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
            StartCoroutine(ReelInBobberRoutine());
        }
    }

    private IEnumerator ReelInBobberRoutine()
    {
        if (activeBobber.TryGetComponent<Rigidbody>(out var bobberRb))
        {
            bobberRb.isKinematic = true;
        }

        Vector3 startPoint = activeBobber.transform.position;
        float elapsedTime = 0f;

        while (elapsedTime < reelInAnimationTime)
        {
            if (activeBobber == null) yield break;

            float t = elapsedTime / reelInAnimationTime;
            Vector3 endPoint = rodTip.position;

            Vector3 controlPoint = (startPoint + endPoint) / 2f + Vector3.up * reelInArcHeight;
            Vector3 m1 = Vector3.Lerp(startPoint, controlPoint, t);
            Vector3 m2 = Vector3.Lerp(controlPoint, endPoint, t);
            activeBobber.transform.position = Vector3.Lerp(m1, m2, t);

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        DestroyBobber();
        FishingEvents.OnReelingCompleted?.Invoke(); 
    }
}