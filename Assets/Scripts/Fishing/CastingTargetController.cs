using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class CastingTargetController : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Length of the directional indicator line.")]
    [SerializeField] private float lineLength = 3f;

    private LineRenderer lineRenderer;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        Hide();
    }

    public void Show()
    {
        lineRenderer.enabled = true;
    }

    public void Hide()
    {
        lineRenderer.enabled = false;
    }

    public void UpdateTrajectory(Vector3 startPoint, Vector3 direction, float force, float extraDownForce)
    {
        Vector3 endPoint = startPoint + direction * lineLength;

        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, startPoint);
        lineRenderer.SetPosition(1, endPoint);
    }
}