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

    [Header("Manager Movement")]
    public float managerSpeed = 1.5f;
    public float wanderInterval = 5f;
    public Vector3 wanderArea = new Vector3(10, 5, 10);

    [Header("Constraints")]
    public bool lockYAxis = false;

    private Vector3 startPosition;
    private Vector3 targetPosition;
    private float wanderTimer;

    // ----- SINGLETON REMOVED -----
    // public static FlockManager FM; // This line is now gone.
    // -----------------------------

    public GameObject[] allFish;
    public FishBoid[] allFishBoids;

    // Awake() is no longer needed
    // void Awake() { ... }

    void Start()
    {
        allFish = new GameObject[numFish];
        allFishBoids = new FishBoid[numFish];
        for (int i = 0; i < numFish; i++)
        {
            Vector3 randomPos = this.transform.position + new Vector3(
                Random.Range(-spawnBounds.x, spawnBounds.x),
                Random.Range(-spawnBounds.y, spawnBounds.y),
                Random.Range(-spawnBounds.z, spawnBounds.z)
            );

            if (lockYAxis)
            {
                randomPos.y = this.transform.position.y;
            }

            GameObject newFish = Instantiate(fishPrefab, randomPos, Quaternion.identity);
            FishBoid boid = newFish.GetComponent<FishBoid>();
            boid.myManager = this;
            allFish[i] = newFish;
            allFishBoids[i] = boid;
        }

        startPosition = this.transform.position;
        PickNewTargetPosition();
    }

    void Update()
    {
        wanderTimer -= Time.deltaTime;
        if (wanderTimer <= 0f)
        {
            PickNewTargetPosition();
        }
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * managerSpeed);
    }

    void PickNewTargetPosition()
    {
        wanderTimer = wanderInterval;

        targetPosition = startPosition + new Vector3(
            Random.Range(-wanderArea.x / 2, wanderArea.x / 2),
            Random.Range(-wanderArea.y / 2, wanderArea.y / 2),
            Random.Range(-wanderArea.z / 2, wanderArea.z / 2)
        );

        if (lockYAxis)
        {
            targetPosition.y = startPosition.y;
        }
    }
}