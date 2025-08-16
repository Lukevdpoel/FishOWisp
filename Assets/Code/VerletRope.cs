using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class VerletRope : MonoBehaviour
{
    public struct RopePoint
    {
        public Vector3 currentPosition;
        public Vector3 previousPosition;
        public bool isLocked; // To lock points, like the one on the rod
    }

    [Header("Rope Ends")]
    public Transform rodTip;   // The start of the rope (will be locked)
    public Transform bobber;   // The end of the rope (will be influenced by physics)

    [Header("Rope Settings")]
    public int segmentCount = 35;
    [Tooltip("Number of iterations to enforce constraints. Higher is more accurate but costs performance.")]
    public int constraintIterations = 50;
    [Tooltip("The total length of the rope.")]
    public float ropeLength = 10f;

    [Header("Physics")]
    public Vector3 gravity = new Vector3(0f, -9.81f, 0f);

    private LineRenderer lineRenderer;
    private List<RopePoint> ropePoints = new List<RopePoint>();
    private float segmentLength;

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        InitializeRope();
    }

    void Update()
    {
        DrawRope();
    }

    void FixedUpdate()
    {
        Simulate();
    }

    // Creates the initial points for the rope in a straight line
    private void InitializeRope()
    {
        segmentLength = ropeLength / segmentCount;
        Vector3 ropeStartPoint = rodTip.position;

        for (int i = 0; i <= segmentCount; i++)
        {
            ropePoints.Add(new RopePoint
            {
                // Position points in a straight line downwards from the rod tip
                currentPosition = ropeStartPoint - new Vector3(0, segmentLength * i, 0),
                previousPosition = ropeStartPoint - new Vector3(0, segmentLength * i, 0),
                isLocked = (i == 0) // Lock the very first point to the rod tip
            });
        }
    }

    // Main simulation loop
    private void Simulate()
    {
        float deltaTime = Time.fixedDeltaTime;

        // STEP 1: Simulate the inertia for each point
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

        // The first point is always locked to the rod tip
        RopePoint firstPoint = ropePoints[0];
        firstPoint.currentPosition = rodTip.position;
        ropePoints[0] = firstPoint;

        // The last point is attached to the bobber
        RopePoint lastPoint = ropePoints[ropePoints.Count - 1];
        lastPoint.currentPosition = bobber.position;
        ropePoints[ropePoints.Count - 1] = lastPoint;


        // STEP 2: Apply constraints to maintain segment length
        for (int i = 0; i < constraintIterations; i++)
        {
            ApplyConstraints();
        }
    }

    // Adjusts points to keep the rope segments from stretching
    private void ApplyConstraints()
    {
        for (int i = 0; i < ropePoints.Count - 1; i++)
        {
            RopePoint point1 = ropePoints[i];
            RopePoint point2 = ropePoints[i + 1];

            Vector3 delta = point2.currentPosition - point1.currentPosition;
            float distance = delta.magnitude;
            float error = distance - segmentLength;
            Vector3 correction = delta.normalized * error;

            // Move the points to correct the distance
            if (!point1.isLocked)
                point1.currentPosition += correction * 0.5f;
            if (!point2.isLocked) // The bobber isn't "locked" in the same way
                point2.currentPosition -= correction * 0.5f;

            ropePoints[i] = point1;
            ropePoints[i + 1] = point2;
        }
    }


    // Draws the rope using the Line Renderer
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