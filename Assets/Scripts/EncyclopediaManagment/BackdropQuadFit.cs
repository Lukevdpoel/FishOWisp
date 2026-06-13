using UnityEngine;

[ExecuteAlways]
public class BackdropQuadFit : MonoBehaviour
{
    [Tooltip("Orthographic camera this quad should fill. Quad is sized from the camera's ortho size and aspect.")]
    public Camera targetCamera;

    [Tooltip("Distance in front of the camera to place the quad (along the camera's local +Z).")]
    public float distance = 1f;

    void LateUpdate() => Fit();

    // Public so NoteMenu can force-fit on the same frame it activates the backdrop camera,
    // before that camera renders. Relying on LateUpdate alone is brittle when the GameObject
    // was inactive moments earlier — first-frame render can beat the LateUpdate tick.
    public void Fit()
    {
        if (targetCamera == null) return;
        if (!targetCamera.orthographic) return;

        // Orthographic camera shows a vertical extent of (2 * size) and horizontal of (2 * size * aspect).
        // A Unity default Quad mesh is 1x1 unit, so its local scale needs to equal those extents.
        float height = 2f * targetCamera.orthographicSize;
        float width  = height * targetCamera.aspect;

        // Set scale in world space so any parent's lossyScale doesn't squash/stretch us.
        Vector3 parentLossy = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        Vector3 desiredLocal = new Vector3(
            width  / Mathf.Max(0.0001f, parentLossy.x),
            height / Mathf.Max(0.0001f, parentLossy.y),
            1f     / Mathf.Max(0.0001f, parentLossy.z)
        );
        transform.localScale = desiredLocal;

        // Place the quad in front of the camera. Default Unity Quad faces -Z in its local space,
        // so a (0,0,0) rotation with the quad positioned at the camera's +Z makes the front face
        // visible to the camera. Use the camera's transform as the basis.
        transform.position = targetCamera.transform.position + targetCamera.transform.forward * distance;
        transform.rotation = targetCamera.transform.rotation;
    }
}
