using UnityEngine;
using UnityEngine.AI; // Important: Add this line!

public class RandomWanderAI : MonoBehaviour
{
    [Tooltip("The NavMeshAgent component attached to this object.")]
    private NavMeshAgent agent;

    [Tooltip("How far the AI will wander from its current position.")]
    public float wanderRadius = 10f;

    [Tooltip("How often (in seconds) the AI will choose a new destination.")]
    public float wanderTimer = 5f;

    private float timer;

    void Start()
    {
        // Get the NavMeshAgent component
        agent = GetComponent<NavMeshAgent>();
        timer = wanderTimer;
    }

    void Update()
    {
        // Countdown the timer
        timer += Time.deltaTime;

        // If the timer is up, find a new destination
        if (timer >= wanderTimer)
        {
            // Find a random point within the wanderRadius on the NavMesh
            Vector3 newPos = RandomNavSphere(transform.position, wanderRadius, -1);

            // Set the agent's destination to this new position
            agent.SetDestination(newPos);

            // Reset the timer
            timer = 0;
        }
    }

    /// <summary>
    /// Finds a random point on the NavMesh within a certain radius of an origin point.
    /// </summary>
    /// <param name="origin">The center of the search sphere.</param>
    /// <param name="dist">The radius of the search sphere.</param>
    /// <param name="layermask">The NavMesh layer mask.</param>
    /// <returns>A random position on the NavMesh.</returns>
    public static Vector3 RandomNavSphere(Vector3 origin, float dist, int layermask)
    {
        // Get a random direction and multiply it by a random distance
        Vector3 randDirection = Random.insideUnitSphere * dist;

        randDirection += origin;

        NavMeshHit navHit;

        // Use NavMesh.SamplePosition to find the closest point on the NavMesh to our random point
        NavMesh.SamplePosition(randDirection, out navHit, dist, layermask);

        return navHit.position;
    }
}