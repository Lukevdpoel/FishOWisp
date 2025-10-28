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
        // Start hidden until it's needed.
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

    public void UpdateTrajectory(Vector3 startPoint, Vector3 direction, float force)
    {
        Vector3 velocity = direction * force;
        float gravity = Physics.gravity.y;
        List<Vector3> points = new List<Vector3>();
        Vector3 currentPoint = startPoint;

        float timeOfFlight = (2 * velocity.y) / -gravity;
        if (timeOfFlight <= 0) timeOfFlight = 5f;

        if (targetIndicator != null) targetIndicator.SetActive(false);

        for (int i = 0; i < resolution; i++)
        {
            points.Add(currentPoint);
            float t = (i + 1) * (timeOfFlight / resolution);

            Vector3 nextPoint = startPoint + velocity * t + 0.5f * Vector3.up * gravity * t * t;

            if (Physics.Linecast(currentPoint, nextPoint, out RaycastHit hit, waterLayer))
            {
                points.Add(hit.point);
                if (targetIndicator != null)
                {
                    targetIndicator.transform.position = hit.point + hit.normal * 0.02f;
                    // CORRECTED: Rotate to face the OPPOSITE of the surface normal, making it visible to a camera looking down.
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