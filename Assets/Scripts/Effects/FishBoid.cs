using UnityEngine;

public class FishBoid : MonoBehaviour
{
    // ----- NEW: A direct reference to this fish's specific manager -----
    public FlockManager myManager;
    // -----------------------------------------------------------------

    private float speed;
    private float lockedYPosition;

    void Start()
    {
        lockedYPosition = transform.position.y;
        // All settings are now pulled from the personal manager reference
        speed = Random.Range(myManager.minSpeed, myManager.maxSpeed);
    }

    void Update()
    {
        // Replace every instance of FlockManager.FM with myManager
        if (Vector3.Distance(transform.position, myManager.transform.position) >= myManager.spawnBounds.x)
        {
            Vector3 directionToCenter = (myManager.transform.position - transform.position).normalized;
            if (myManager.lockYAxis)
            {
                directionToCenter.y = 0;
            }
            transform.rotation = Quaternion.Slerp(transform.rotation,
                                                 Quaternion.LookRotation(directionToCenter),
                                                 Time.deltaTime * 2f);
        }
        else
        {
            if (Random.Range(0, 100) < 10)
            {
                speed = Random.Range(myManager.minSpeed, myManager.maxSpeed);
            }
            ApplyRules();
        }
        transform.Translate(0, 0, Time.deltaTime * speed);
    }

    void LateUpdate()
    {
        if (myManager.lockYAxis)
        {
            transform.position = new Vector3(transform.position.x, lockedYPosition, transform.position.z);
        }
    }

    void ApplyRules()
    {
        Vector3 cohesionVector = Vector3.zero;
        Vector3 separationVector = Vector3.zero;
        Vector3 alignmentVector = Vector3.zero;
        int neighborsFound = 0;

        // The fish now only checks against the list of fish from its own manager
        foreach (GameObject fish in myManager.allFish)
        {
            if (fish != this.gameObject)
            {
                float distance = Vector3.Distance(fish.transform.position, this.transform.position);
                if (distance <= myManager.perceptionRadius)
                {
                    cohesionVector += fish.transform.position;
                    if (distance < myManager.avoidanceRadius)
                    {
                        separationVector -= (fish.transform.position - this.transform.position);
                    }
                    alignmentVector += fish.GetComponent<FishBoid>().transform.forward;
                    neighborsFound++;
                }
            }
        }

        if (neighborsFound > 0)
        {
            cohesionVector /= neighborsFound;
            alignmentVector /= neighborsFound;
            cohesionVector = (cohesionVector - this.transform.position).normalized;

            Vector3 steering = (cohesionVector + separationVector + alignmentVector).normalized;

            if (myManager.lockYAxis)
            {
                steering.y = 0;
            }

            if (steering != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(steering);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 2f);
            }
        }
    }
}