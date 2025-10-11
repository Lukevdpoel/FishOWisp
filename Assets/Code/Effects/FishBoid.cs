using UnityEngine;

public class FishBoid : MonoBehaviour
{
    private float speed;
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        speed = Random.Range(FlockManager.FM.minSpeed, FlockManager.FM.maxSpeed);
    }

    void Update()
    {
        // Keep fish within the manager's bounds
        if (Vector3.Distance(transform.position, FlockManager.FM.transform.position) >= FlockManager.FM.spawnBounds.x)
        {
            // Steer back towards the center
            Vector3 directionToCenter = (FlockManager.FM.transform.position - transform.position).normalized;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                                                 Quaternion.LookRotation(directionToCenter),
                                                 Time.deltaTime * 2f);
        }
        else
        {
            // Randomly change speed sometimes
            if (Random.Range(0, 100) < 10)
            {
                speed = Random.Range(FlockManager.FM.minSpeed, FlockManager.FM.maxSpeed);
            }

            // Apply the flocking rules
            ApplyRules();
        }

        // Move the fish forward
        transform.Translate(0, 0, Time.deltaTime * speed);
    }

    void ApplyRules()
    {
        Vector3 cohesionVector = Vector3.zero;
        Vector3 separationVector = Vector3.zero;
        Vector3 alignmentVector = Vector3.zero;
        int neighborsFound = 0;

        foreach (GameObject fish in FlockManager.FM.allFish)
        {
            if (fish != this.gameObject)
            {
                float distance = Vector3.Distance(fish.transform.position, this.transform.position);

                // Is the fish a neighbor?
                if (distance <= FlockManager.FM.perceptionRadius)
                {
                    // --- Cohesion ---
                    cohesionVector += fish.transform.position;

                    // --- Separation ---
                    if (distance < FlockManager.FM.avoidanceRadius)
                    {
                        separationVector -= (fish.transform.position - this.transform.position);
                    }

                    // --- Alignment ---
                    alignmentVector += fish.GetComponent<FishBoid>().transform.forward;

                    neighborsFound++;
                }
            }
        }

        if (neighborsFound > 0)
        {
            // Calculate average vectors
            cohesionVector /= neighborsFound;
            alignmentVector /= neighborsFound;

            // Create a direction vector towards the center of the flock
            cohesionVector = (cohesionVector - this.transform.position).normalized;

            // Combine the rules into one steering vector
            Vector3 steering = (cohesionVector + separationVector + alignmentVector).normalized;

            // Smoothly rotate the fish towards the new direction
            if (steering != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(steering);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 2f);
            }
        }
    }
}