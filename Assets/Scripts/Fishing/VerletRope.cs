using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class VerletRope : MonoBehaviour
{
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
    public int constraintIterations = 50;

    [Header("Physics")]
    public Vector3 gravity = new Vector3(0f, -9.81f, 0f);

    private LineRenderer lineRenderer;
    private List<RopePoint> ropePoints = new List<RopePoint>();
    private float segmentLength;
    private bool isInitialized = false;
    private bool isLineTight = false;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }

    private void OnEnable()
    {
        FishingEvents.OnFishFightBegin += HandleFishFightBegin;
        FishingEvents.OnFishFightEnd += HandleFishFightEnd;
    }

    private void OnDisable()
    {
        FishingEvents.OnFishFightBegin -= HandleFishFightBegin;
        FishingEvents.OnFishFightEnd -= HandleFishFightEnd;
    }

    private void HandleFishFightBegin(FishPreset fish)
    {
        isLineTight = true;
    }

    private void HandleFishFightEnd(bool success)
    {
        isLineTight = false;
    }

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

    public void DeactivateRope()
    {
        isInitialized = false;
        isLineTight = false;
        if (lineRenderer != null)
            lineRenderer.positionCount = 0;
    }

    void LateUpdate()
    {
        if (!isInitialized) return;

        // --- FIXED: Added Safety Check ---
        // If the bobber or rod has been destroyed, stop immediately to prevent MissingReferenceException.
        if (bobber == null || rodTip == null)
        {
            DeactivateRope();
            return;
        }
        // ---------------------------------

        if (isLineTight)
        {
            DrawTightLine();
        }
        else
        {
            Simulate();
            DrawSimulatedRope();
        }
    }

    private void Simulate()
    {
        // Safety check is now handled in LateUpdate, so we can access .position safely here
        float currentRopeLength = Vector3.Distance(rodTip.position, bobber.position);
        segmentLength = currentRopeLength / segmentCount;

        float deltaTime = Time.deltaTime;

        for (int i = 0; i < ropePoints.Count; i++)
        {
            RopePoint point = ropePoints[i];
            if (point.isLocked) continue;

            Vector3 velocity = point.currentPosition - point.previousPosition;
            point.previousPosition = point.currentPosition;

            point.currentPosition += velocity + gravity * (deltaTime * deltaTime);
            ropePoints[i] = point;
        }

        for (int i = 0; i < constraintIterations; i++)
        {
            ApplyConstraints();
        }
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

    private void DrawTightLine()
    {
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, rodTip.position);
        lineRenderer.SetPosition(1, bobber.position);
    }
}