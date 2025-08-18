using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class VerletRope : MonoBehaviour
{
    // This struct holds the data for each point on the rope.
    public struct RopePoint
    {
        public Vector3 currentPosition;
        public Vector3 previousPosition;
        public bool isLocked;
    }

    private Transform rodTip;
    private Transform bobber;

    [Header("Rope Settings")]
    public int segmentCount = 35;
    [Tooltip("Higher iterations are more accurate but cost more performance.")]
    public int constraintIterations = 50;

    [Header("Physics")]
    public Vector3 gravity = new Vector3(0f, -9.81f, 0f);

    private LineRenderer lineRenderer;
    private List<RopePoint> ropePoints = new List<RopePoint>();
    private float segmentLength; // This will now be calculated dynamically
    private bool isInitialized = false;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }

    /// <summary>
    /// Initializes the rope, creating all its points at the rod tip for a smooth start.
    /// </summary>
    public void SetupRope(Transform rodTip, Transform bobber)
    {
        this.rodTip = rodTip;
        this.bobber = bobber;

        ropePoints.Clear();

        for (int i = 0; i <= segmentCount; i++)
        {
            ropePoints.Add(new RopePoint
            {
                // Start all points at the rod tip for a stable spawn
                currentPosition = rodTip.position,
                previousPosition = rodTip.position,
                isLocked = (i == 0) // Lock the first point to the rod
            });
        }

        isInitialized = true;
    }

    /// <summary>
    /// Hides the rope and stops its simulation.
    /// </summary>
    public void DeactivateRope()
    {
        isInitialized = false;
        if(lineRenderer != null)
        lineRenderer.positionCount = 0;
    }

    void Update()
    {
        if (isInitialized)
        {
            DrawRope();
        }
    }

    void FixedUpdate()
    {
        if (isInitialized)
        {
            Simulate();
        }
    }

    private void Simulate()
    {
        // Dynamically calculate the desired length of each segment based on the total distance
        float currentRopeLength = Vector3.Distance(rodTip.position, bobber.position);
        segmentLength = currentRopeLength / segmentCount;

        float deltaTime = Time.fixedDeltaTime;

        // --- VERLET INTEGRATION ---
        for (int i = 0; i < ropePoints.Count; i++)
        {
            RopePoint point = ropePoints[i];
            if (point.isLocked) continue;

            Vector3 velocity = point.currentPosition - point.previousPosition;
            point.previousPosition = point.currentPosition;

            // Apply gravity
            point.currentPosition += velocity + gravity * (deltaTime * deltaTime);
            ropePoints[i] = point;
        }

        // --- CONSTRAINTS ---
        for (int i = 0; i < constraintIterations; i++)
        {
            ApplyConstraints();
        }

        // After constraints, apply the rope's pull to the bobber's actual position
        bobber.position = ropePoints[ropePoints.Count - 1].currentPosition;
    }

    private void ApplyConstraints()
    {
        // The first point is always locked to the rod tip
        RopePoint firstPoint = ropePoints[0];
        firstPoint.currentPosition = rodTip.position;
        ropePoints[0] = firstPoint;

        // The last point is always locked to the bobber's position
        RopePoint lastPoint = ropePoints[ropePoints.Count - 1];
        lastPoint.currentPosition = bobber.position;
        ropePoints[ropePoints.Count - 1] = lastPoint;

        for (int i = 0; i < ropePoints.Count - 1; i++)
        {
            RopePoint point1 = ropePoints[i];
            RopePoint point2 = ropePoints[i + 1];

            Vector3 delta = point2.currentPosition - point1.currentPosition;
            float distance = delta.magnitude;

            // Avoid division by zero if points are on top of each other
            if (distance == 0) continue;

            float error = distance - segmentLength;
            Vector3 correction = delta.normalized * error;

            // Move the points to satisfy the constraint
            if (!point1.isLocked)
                point1.currentPosition += correction * 0.5f;
            if (!point2.isLocked)
                point2.currentPosition -= correction * 0.5f;

            ropePoints[i] = point1;
            ropePoints[i + 1] = point2;
        }
    }

    private void DrawRope()
    {
        lineRenderer.positionCount = ropePoints.Count;
        Vector3[] positions = new Vector3[ropePoints.Count];
        for (int i = 0; i < ropePoints.Count; i++)
        {
            positions[i] = ropePoints[i].currentPosition;
        }
        lineRenderer.SetPositions(positions);
    }
}
