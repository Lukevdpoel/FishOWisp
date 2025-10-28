using UnityEngine;
using System.Collections.Generic;

public class BucketSwarm : MonoBehaviour
{
    private List<Transform> fishInBucket = new List<Transform>();

    [Header("Swarm Settings")]
    public float radius = 0.5f;
    public float rotationSpeed = 20f;
    public float bobSpeed = 1f;
    public float bobHeight = 0.1f;
    public float verticalSpacing = 0.1f; // How much to stack them
    public float moveSmoothTime = 5f; // How fast they swim to their new spot
    public float rotateSmoothTime = 5f;

    void Update()
    {
        if (fishInBucket.Count == 0) return;

        // Base angle that rotates over time
        float currentBaseAngle = Time.time * rotationSpeed;

        // This makes sure the fish are evenly spaced in a circle
        float anglePerFish = 360f / fishInBucket.Count;

        for (int i = 0; i < fishInBucket.Count; i++)
        {
            Transform fish = fishInBucket[i];

            // Failsafe in case a fish was destroyed somehow
            if (fish == null)
            {
                fishInBucket.RemoveAt(i);
                i--;
                continue;
            }

            // --- Calculate the fish's target position ---
            float fishAngle = (i * anglePerFish) + currentBaseAngle;
            float rad = fishAngle * Mathf.Deg2Rad;

            // 1. Position in a circle (X and Z)
            float x = Mathf.Cos(rad) * radius;
            float z = Mathf.Sin(rad) * radius;

            // 2. Vertical position (Y)
            // Stacks them and adds a sine wave "bob"
            float y = (i * verticalSpacing) + (Mathf.Sin((Time.time * bobSpeed) + i) * bobHeight);

            Vector3 targetPosition = new Vector3(x, y, z);

            // --- Calculate the fish's target rotation ---
            // Makes them "face" the direction they are swimming
            Vector3 tangent = new Vector3(-z, 0, x).normalized;
            Quaternion targetRotation = Quaternion.identity;

            if (tangent != Vector3.zero)
            {
                targetRotation = Quaternion.LookRotation(tangent);
            }

            // --- Smoothly move fish to its target ---
            fish.localPosition = Vector3.Lerp(fish.localPosition, targetPosition, Time.deltaTime * moveSmoothTime);
            fish.localRotation = Quaternion.Slerp(fish.localRotation, targetRotation, Time.deltaTime * rotateSmoothTime);
        }
    }

    // Called by PlayerInventory when a fish is added
    public void AddFish(Transform fishTransform)
    {
        if (fishTransform != null && !fishInBucket.Contains(fishTransform))
        {
            fishInBucket.Add(fishTransform);

            // Tell the fish to disable its physics
            PhysicalFish pf = fishTransform.GetComponent<PhysicalFish>();
            if (pf != null)
            {
                pf.EnableSwarmMode();
            }
        }
    }

    // Called by PlayerInventory when a fish is sold
    public void RemoveFish(Transform fishTransform)
    {
        if (fishTransform != null && fishInBucket.Contains(fishTransform))
        {
            fishInBucket.Remove(fishTransform);
        }
    }
}