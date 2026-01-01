using UnityEngine;
using System.Collections.Generic;

public class FishTankManager : MonoBehaviour
{
    [Header("Tank Boundaries")]
    public Vector3 tankSize = new Vector3(5, 3, 5);

    [Header("Movement Settings")]
    public float fishSpeed = 2f;
    public float turnSpeed = 2.0f;
    public float neighborDistance = 3f;

    [Header("Boundary Control")]
    [Tooltip("How much faster they turn when hitting the glass.")]
    public float boundaryTurnMultiplier = 5f;
    [Tooltip("Push fish back if they try to leave.")]
    public bool enforceBoundaries = true;

    [Header("Model Fix")]
    public bool fixBackwardModels = true;

    private List<GameObject> tankFish = new List<GameObject>();
    private Vector3 goalPos;

    void Start()
    {
        goalPos = GetRandomPointInTank();
    }

    void Update()
    {
        // 1. Move the target occasionally
        if (Random.Range(0, 100) < 1)
        {
            goalPos = GetRandomPointInTank();
        }

        // 2. Update fish
        for (int i = 0; i < tankFish.Count; i++)
        {
            GameObject fish = tankFish[i];
            if (fish == null) { tankFish.RemoveAt(i); i--; continue; }

            ApplyTankMovement(fish);
        }
    }

    public void AddFish(GameObject newFish)
    {
        if (!tankFish.Contains(newFish))
        {
            tankFish.Add(newFish);
        }
    }

    private void ApplyTankMovement(GameObject fish)
    {
        Vector3 direction = Vector3.zero;
        float currentTurnSpeed = turnSpeed;
        bool isOutOfBounds = false;

        // --- 1. CHECK BOUNDARIES ---
        if (enforceBoundaries)
        {
            // Calculate vector from tank center to fish
            Vector3 offsetFromCenter = fish.transform.position - transform.position;

            // Check if outside box (with a small buffer)
            if (Mathf.Abs(offsetFromCenter.x) > tankSize.x / 2 ||
                Mathf.Abs(offsetFromCenter.y) > tankSize.y / 2 ||
                Mathf.Abs(offsetFromCenter.z) > tankSize.z / 2)
            {
                isOutOfBounds = true;
                // Force direction towards exact center of tank
                direction = transform.position - fish.transform.position;
                // Turn drastically faster to avoid escaping further
                currentTurnSpeed *= boundaryTurnMultiplier;
            }
        }

        // --- 2. FLOCKING (Only if inside bounds) ---
        // If we are out of bounds, ignore neighbors and just get back in!
        if (!isOutOfBounds)
        {
            Vector3 center = Vector3.zero;
            Vector3 avoid = Vector3.zero;
            int groupSize = 0;

            foreach (GameObject neighbor in tankFish)
            {
                if (neighbor != fish)
                {
                    float dist = Vector3.Distance(fish.transform.position, neighbor.transform.position);
                    if (dist <= neighborDistance)
                    {
                        center += neighbor.transform.position;
                        groupSize++;

                        if (dist < 1.0f)
                        {
                            avoid = avoid + (fish.transform.position - neighbor.transform.position);
                        }
                    }
                }
            }

            if (groupSize > 0)
            {
                center = center / groupSize + (goalPos - transform.position);
                direction = (center + avoid) - fish.transform.position;
            }
            else
            {
                direction = goalPos - fish.transform.position;
            }
        }

        // --- 3. ROTATION & MOVEMENT ---
        if (direction != Vector3.zero)
        {
            Vector3 lookDir = fixBackwardModels ? -direction : direction;

            fish.transform.rotation = Quaternion.Slerp(
                fish.transform.rotation,
                Quaternion.LookRotation(lookDir),
                currentTurnSpeed * Time.deltaTime
            );
        }

        // Move Forward (or Backward if fixed)
        float moveSpeed = fixBackwardModels ? -fishSpeed : fishSpeed;
        fish.transform.Translate(0, 0, Time.deltaTime * moveSpeed);
    }

    private Vector3 GetRandomPointInTank()
    {
        return transform.position + new Vector3(
            Random.Range(-tankSize.x / 2, tankSize.x / 2),
            Random.Range(-tankSize.y / 2, tankSize.y / 2),
            Random.Range(-tankSize.z / 2, tankSize.z / 2)
        );
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0, 1, 1, 0.3f);
        Gizmos.DrawCube(transform.position, tankSize);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(transform.position, tankSize);
    }
}