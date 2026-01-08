using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer))]
public class CastingTargetController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The visual sprite that marks the landing spot.")]
    [SerializeField] private GameObject targetIndicator;

    [Header("Settings")]
    [Tooltip("Set this to the layer your water is on.")]
    [SerializeField] private LayerMask waterLayer;
    [Tooltip("The number of points to calculate for the trajectory arc.")]
    [SerializeField] private int resolution = 30;

    private LineRenderer lineRenderer;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        Hide();
    }

    public void Show()
    {
        lineRenderer.enabled = true;
        if (targetIndicator != null) targetIndicator.SetActive(true);
    }

    public void Hide()
    {
        lineRenderer.enabled = false;
        if (targetIndicator != null) targetIndicator.SetActive(false);
    }

    // --- UPDATED SIGNATURE: Now accepts extraDownForce ---
    public void UpdateTrajectory(Vector3 startPoint, Vector3 direction, float force, float extraDownForce)
    {
        Vector3 velocity = direction * force;

        // CALCULATE EFFECTIVE GRAVITY
        // Physics.gravity.y is usually -9.81. 
        // We subtract the extra positive force (e.g. 30) to get a stronger downward pull (e.g. -39.81).
        float effectiveGravityY = Physics.gravity.y - extraDownForce;

        List<Vector3> points = new List<Vector3>();
        Vector3 currentPoint = startPoint;

        // Recalculate time of flight using the heavier gravity
        // Formula: t = (2 * Vy) / -g
        float timeOfFlight = (2 * velocity.y) / -effectiveGravityY;

        // Safety check to prevent infinite loops or errors if aiming down
        if (timeOfFlight <= 0 || float.IsNaN(timeOfFlight)) timeOfFlight = 2f;

        if (targetIndicator != null) targetIndicator.SetActive(false);

        for (int i = 0; i < resolution; i++)
        {
            points.Add(currentPoint);
            float t = (i + 1) * (timeOfFlight / resolution);

            // Standard trajectory formula: P = P0 + vt + 0.5at^2
            Vector3 gravityStep = 0.5f * Vector3.up * effectiveGravityY * t * t;
            Vector3 nextPoint = startPoint + velocity * t + gravityStep;

            if (Physics.Linecast(currentPoint, nextPoint, out RaycastHit hit, waterLayer))
            {
                points.Add(hit.point);
                if (targetIndicator != null)
                {
                    targetIndicator.transform.position = hit.point + hit.normal * 0.02f;
                    targetIndicator.transform.rotation = Quaternion.FromToRotation(Vector3.forward, -hit.normal);
                    targetIndicator.SetActive(true);
                }
                break;
            }
            currentPoint = nextPoint;
        }

        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(points.ToArray());
    }
}