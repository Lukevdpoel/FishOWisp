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
    private float segmentLength;
    private bool isInitialized = false;

    // --- NEW ---
    // A flag to track if the line should be drawn straight and tight.
    private bool isLineTight = false;
    // ---------

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }

    // --- NEW ---
    // Subscribe to fishing events when the component is enabled.
    private void OnEnable()
    {
        FishingEvents.OnFishFightBegin += HandleFishFightBegin;
        FishingEvents.OnFishFightEnd += HandleFishFightEnd;
    }

    // Unsubscribe from events when the component is disabled.
    private void OnDisable()
    {
        FishingEvents.OnFishFightBegin -= HandleFishFightBegin;
        FishingEvents.OnFishFightEnd -= HandleFishFightEnd;
    }

    // Event handler to set the line to "tight" mode.
    private void HandleFishFightBegin(FishPreset fish)
    {
        isLineTight = true;
    }

    // Event handler to return the line to normal simulation.
    private void HandleFishFightEnd(bool success)
    {
        isLineTight = false;
    }
    // ---------

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
                currentPosition = rodTip.position,
                previousPosition = rodTip.position,
                isLocked = (i == 0)
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
        isLineTight = false; // Also reset the tight flag
        if (lineRenderer != null)
            lineRenderer.positionCount = 0;
    }

    // MODIFIED: Update now chooses which drawing method to use.
    void Update()
    {
        if (isInitialized)
        {
            if (isLineTight)
            {
                DrawTightLine();
            }
            else
            {
                DrawSimulatedRope();
            }
        }
    }

    // MODIFIED: FixedUpdate now checks if the line is tight before simulating.
    void FixedUpdate()
    {
        // Only run the simulation if the rope is active and not in "tight" mode.
        if (isInitialized && !isLineTight)
        {
            Simulate();
        }
    }

    private void Simulate()
    {
        float currentRopeLength = Vector3.Distance(rodTip.position, bobber.position);
        segmentLength = currentRopeLength / segmentCount;

        float deltaTime = Time.fixedDeltaTime;

        // VERLET INTEGRATION
        for (int i = 0; i < ropePoints.Count; i++)
        {
            RopePoint point = ropePoints[i];
            if (point.isLocked) continue;

            Vector3 velocity = point.currentPosition - point.previousPosition;
            point.previousPosition = point.currentPosition;

            point.currentPosition += velocity + gravity * (deltaTime * deltaTime);
            ropePoints[i] = point;
        }

        // CONSTRAINTS
        for (int i = 0; i < constraintIterations; i++)
        {
            ApplyConstraints();
        }

        bobber.position = ropePoints[ropePoints.Count - 1].currentPosition;
    }

    private void ApplyConstraints()
    {
        RopePoint firstPoint = ropePoints[0];
        firstPoint.currentPosition = rodTip.position;
        ropePoints[0] = firstPoint;

        RopePoint lastPoint = ropePoints[ropePoints.Count - 1];
        lastPoint.currentPosition = bobber.position;
        ropePoints[ropePoints.Count - 1] = lastPoint;

        for (int i = 0; i < ropePoints.Count - 1; i++)
        {
            RopePoint point1 = ropePoints[i];
            RopePoint point2 = ropePoints[i + 1];

            Vector3 delta = point2.currentPosition - point1.currentPosition;
            float distance = delta.magnitude;

            if (distance == 0) continue;

            float error = distance - segmentLength;
            Vector3 correction = delta.normalized * error;

            if (!point1.isLocked)
                point1.currentPosition += correction * 0.5f;
            if (!point2.isLocked)
                point2.currentPosition -= correction * 0.5f;

            ropePoints[i] = point1;
            ropePoints[i + 1] = point2;
        }
    }

    // Renamed from DrawRope to be more specific
    private void DrawSimulatedRope()
    {
        lineRenderer.positionCount = ropePoints.Count;
        Vector3[] positions = new Vector3[ropePoints.Count];
        for (int i = 0; i < ropePoints.Count; i++)
        {
            positions[i] = ropePoints[i].currentPosition;
        }
        lineRenderer.SetPositions(positions);
    }

    // --- NEW ---
    // A simple drawing method that creates a straight line.
    private void DrawTightLine()
    {
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, rodTip.position);
        lineRenderer.SetPosition(1, bobber.position);
    }
    // ---------
}