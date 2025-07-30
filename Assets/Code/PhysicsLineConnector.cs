using UnityEngine;

public class LineToThrownObject : MonoBehaviour
{
    [Tooltip("Start point for the line (e.g. castPoint on player)")]
    public Transform startPoint; // << NEW: Set this from ObjectThrower

    private LineRenderer lineRenderer;
    private Transform target;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.enabled = false;
        }
    }

    void Update()
    {
        if (target != null && startPoint != null)
        {
            if (!lineRenderer.enabled)
                lineRenderer.enabled = true;

            lineRenderer.SetPosition(0, startPoint.position); // Fixed: use castPoint
            lineRenderer.SetPosition(1, target.position);
        }
        else
        {
            if (lineRenderer.enabled)
                lineRenderer.enabled = false;
        }
    }

    // Attach end of line to bobber
    public void AttachTo(Transform newTarget)
    {
        target = newTarget;
    }

    public void Detach()
    {
        target = null;
        if (lineRenderer != null)
            lineRenderer.enabled = false;
    }
}
