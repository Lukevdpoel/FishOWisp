using UnityEngine;
using System.Collections.Generic;

public class FishEscalator : MonoBehaviour
{
    [Header("Path")]
    [Tooltip("The parent object containing the waypoint transforms (e.g., P1, P2, P3...).")]
    public Transform waypointParent;

    [Header("Movement")]
    public float moveSpeed = 1f;
    [Tooltip("Smaller = Slower/Smoother Turn, Larger = Faster/Sharper Turn")]
    public float rotateSmoothTime = 3f;
    [Tooltip("How much space to leave between fish on the same path segment (0 = no space, 0.5 = half a segment)")]
    public float spacingProgress = 0.2f;

    private Dictionary<Transform, FishPathState> fishStates = new Dictionary<Transform, FishPathState>();
    private Transform[] waypoints;
    private bool isInitialized = false;

    private class FishPathState
    {
        public int currentWaypointIndex;
        public float progressToNextWaypoint;
    }

    void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (isInitialized) return;

        if (waypointParent != null)
        {
            waypoints = new Transform[waypointParent.childCount];
            for (int i = 0; i < waypointParent.childCount; i++)
            {
                waypoints[i] = waypointParent.GetChild(i);
            }
            isInitialized = true;
        }
        else
        {
            Debug.LogError("FishEscalator: Waypoint Parent is not assigned in the Inspector!", this.gameObject);
        }
    }

    void Update()
    {
        if (!isInitialized || waypoints == null || waypoints.Length < 2) return;

        List<Transform> fishToRemove = new List<Transform>();
        List<Transform> currentFish = new List<Transform>(fishStates.Keys);

        foreach (Transform fish in currentFish)
        {
            if (fish == null)
            {
                fishToRemove.Add(fish);
                continue;
            }

            FishPathState state = fishStates[fish];

            Transform currentTarget = waypoints[state.currentWaypointIndex];
            int nextIndex = (state.currentWaypointIndex + 1) % waypoints.Length;
            Transform nextTarget = waypoints[nextIndex];

            float distanceToNext = Vector3.Distance(currentTarget.position, nextTarget.position);
            if (distanceToNext <= 0.01f) distanceToNext = 0.01f;

            float progressStep = (moveSpeed / distanceToNext) * Time.unscaledDeltaTime;
            state.progressToNextWaypoint += progressStep;

            if (state.progressToNextWaypoint >= 1.0f)
            {
                state.currentWaypointIndex = nextIndex;
                state.progressToNextWaypoint -= 1.0f;

                // Re-get targets
                currentTarget = waypoints[state.currentWaypointIndex];
                nextIndex = (state.currentWaypointIndex + 1) % waypoints.Length;
                nextTarget = waypoints[nextIndex];
            }

            Vector3 targetPosition = Vector3.LerpUnclamped(currentTarget.position, nextTarget.position, state.progressToNextWaypoint);

            // --- THIS IS THE FIX for smooth turns ---
            // We revert to the old logic: have the fish look AT the next waypoint
            // from its current position. This creates a natural curve.
            Vector3 direction = (nextTarget.position - fish.position).normalized;
            // --- END OF FIX ---

            Quaternion targetRotation = Quaternion.identity;
            if (direction != Vector3.zero)
            {
                targetRotation = Quaternion.LookRotation(direction);
            }

            fish.position = targetPosition;
            fish.rotation = Quaternion.Slerp(fish.rotation, targetRotation, Time.unscaledDeltaTime * rotateSmoothTime);
        }

        foreach (Transform fish in fishToRemove)
        {
            fishStates.Remove(fish);
        }
    }

    public void AddFish(Transform fishTransform)
    {
        if (!isInitialized) Initialize();

        if (fishTransform != null && !fishStates.ContainsKey(fishTransform) && waypoints != null && waypoints.Length > 0)
        {
            PhysicalFish pf = fishTransform.GetComponent<PhysicalFish>();
            if (pf != null) pf.EnableSwarmMode();

            FishPathState newState = new FishPathState();

            // --- (Spacing logic, no changes) ---
            int bestStartIndex = 0;
            if (fishStates.Count > 0)
            {
                int[] fishPerSegment = new int[waypoints.Length];
                foreach (var state in fishStates.Values)
                    fishPerSegment[state.currentWaypointIndex]++;

                int minFish = int.MaxValue;
                for (int i = 0; i < fishPerSegment.Length; i++)
                {
                    if (fishPerSegment[i] < minFish)
                    {
                        minFish = fishPerSegment[i];
                        bestStartIndex = i;
                    }
                }
            }

            float minProgress = 0f;
            foreach (var state in fishStates.Values)
            {
                if (state.currentWaypointIndex == bestStartIndex)
                {
                    if (state.progressToNextWaypoint < minProgress)
                        minProgress = state.progressToNextWaypoint;
                }
            }

            newState.currentWaypointIndex = bestStartIndex;
            newState.progressToNextWaypoint = minProgress - spacingProgress;
            // ---

            fishStates[fishTransform] = newState;

            Transform startTarget = waypoints[bestStartIndex];
            Transform nextTarget = waypoints[(bestStartIndex + 1) % waypoints.Length];
            fishTransform.position = Vector3.LerpUnclamped(startTarget.position, nextTarget.position, newState.progressToNextWaypoint);
        }
    }

    public void RemoveFish(Transform fishTransform)
    {
        if (fishTransform != null && fishStates.ContainsKey(fishTransform))
        {
            fishStates.Remove(fishTransform);
        }
    }
}