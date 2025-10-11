using UnityEngine;

public class FlockManager : MonoBehaviour
{
    [Header("Spawning")]
    public GameObject fishPrefab;
    public int numFish = 20;
    public Vector3 spawnBounds = new Vector3(5, 5, 5);

    [Header("Fish Settings")]
    [Range(0f, 5f)]
    public float minSpeed = 2f;
    [Range(0f, 5f)]
    public float maxSpeed = 5f;
    [Range(1f, 10f)]
    public float perceptionRadius = 2.5f;
    [Range(0f, 5f)]
    public float avoidanceRadius = 1f;

    // ----- NEW WANDER SETTINGS -----
    [Header("Manager Movement")]
    public float managerSpeed = 1.5f;
    public float wanderInterval = 5f; // Time in seconds between picking a new target
    public Vector3 wanderArea = new Vector3(10, 5, 10); // The size of the box the manager can wander in

    private Vector3 startPosition;
    private Vector3 targetPosition;
    private float wanderTimer;
    // -------------------------------

    public static FlockManager FM;
    public GameObject[] allFish;

    void Awake()
    {
        FM = this;
    }

    void Start()
    {
        // --- SPAWNING LOGIC ---
        allFish = new GameObject[numFish];
        for (int i = 0; i < numFish; i++)
        {
            Vector3 randomPos = this.transform.position + new Vector3(
                Random.Range(-spawnBounds.x, spawnBounds.x),
                Random.Range(-spawnBounds.y, spawnBounds.y),
                Random.Range(-spawnBounds.z, spawnBounds.z)
            );
            allFish[i] = Instantiate(fishPrefab, randomPos, Quaternion.identity);
        }

        // --- NEW WANDER LOGIC INITIALIZATION ---
        startPosition = this.transform.position;
        PickNewTargetPosition();
    }

    void Update()
    {
        // --- NEW WANDER MOVEMENT LOGIC ---
        wanderTimer -= Time.deltaTime;
        if (wanderTimer <= 0f)
        {
            PickNewTargetPosition();
        }

        // Smoothly move the manager towards the target position
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * managerSpeed);
    }

    // --- NEW METHOD TO PICK A WANDER TARGET ---
    void PickNewTargetPosition()
    {
        wanderTimer = wanderInterval;

        // Calculate a new random position within the wanderArea, centered around the start position
        targetPosition = startPosition + new Vector3(
            Random.Range(-wanderArea.x / 2, wanderArea.x / 2),
            Random.Range(-wanderArea.y / 2, wanderArea.y / 2),
            Random.Range(-wanderArea.z / 2, wanderArea.z / 2)
        );
    }
}