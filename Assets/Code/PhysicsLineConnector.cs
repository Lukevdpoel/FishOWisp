using UnityEngine;

public class LineToThrownObject : MonoBehaviour
{
    private LineRenderer lineRenderer;
    private Transform target;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        // Hide the line at the beginning
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.enabled = false;
        }
    }

    void Update()
    {
        // Only show and update the line if we have a target
        if (target != null)
        {
            if (!lineRenderer.enabled)
                lineRenderer.enabled = true;

            lineRenderer.SetPosition(0, transform.position);
            lineRenderer.SetPosition(1, target.position);
        }
        else
        {
            if (lineRenderer.enabled)
                lineRenderer.enabled = false;
        }
    }

    // Call this after the bobber is instantiated
    public void AttachTo(Transform newTarget)
    {
        target = newTarget;
    }
    public void Detach()
    {
        // You can clear any references or visual line elements here
        transform.parent = null;

        // Example: If you have a LineRenderer component
        // LineRenderer line = GetComponent<LineRenderer>();
        // if (line != null) line.enabled = false;
    }



}
