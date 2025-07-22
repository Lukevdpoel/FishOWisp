using UnityEngine;

public class FishingManager : MonoBehaviour
{
    public GameObject bobberPrefab;
    public Transform castOrigin;

    private GameObject currentBobber;

    void Update()
    {
        // Right Mouse Button (Cancel/Reset)
        if (Input.GetMouseButtonDown(1))
        {
            ResetBobber();
        }

        // Example Throw (Left Mouse Button)
        if (Input.GetMouseButtonDown(0) && currentBobber == null)
        {
            CastBobber();
        }
    }

    void CastBobber()
    {
        Quaternion randomRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        currentBobber = Instantiate(bobberPrefab, castOrigin.position, randomRotation);
    }

    void ResetBobber()
    {
        if (currentBobber != null)
        {
            Destroy(currentBobber);
            currentBobber = null;

            // Add any other cleanup logic if needed
        }
    }
}
