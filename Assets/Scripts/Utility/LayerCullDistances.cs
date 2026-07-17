using UnityEngine;

// Distance-culls whole layers on this camera, in plain world-space meters.
// This is the effects-culling lever: unlike LODGroup thresholds, it is not
// affected by renderer bounds, transform scale, or the global LOD Bias.
// Layers not listed here keep the camera's far plane as their cull distance.
[RequireComponent(typeof(Camera))]
public class LayerCullDistances : MonoBehaviour
{
    [System.Serializable]
    public struct LayerDistance
    {
        [Tooltip("Layer name exactly as it appears in the Tags & Layers list.")]
        public string layerName;

        [Tooltip("Objects on this layer stop rendering beyond this distance (meters) from the camera.")]
        public float cullDistance;
    }

    [Tooltip("Per-layer cull distances. Everything on a listed layer disappears past its distance.")]
    public LayerDistance[] layers;

    [Tooltip("Measure distance as a sphere around the camera instead of along the view direction. Keeps the cull point consistent when the camera turns.")]
    public bool sphericalDistance = true;

    void Start()
    {
        Apply();
    }

    // Re-apply after editing distances at runtime (call from the inspector context menu).
    [ContextMenu("Apply Now")]
    public void Apply()
    {
        var cam = GetComponent<Camera>();
        float[] distances = new float[32]; // 0 = use the camera far plane for that layer

        foreach (var entry in layers)
        {
            int layer = LayerMask.NameToLayer(entry.layerName);
            if (layer < 0)
            {
                Debug.LogWarning($"LayerCullDistances: layer '{entry.layerName}' does not exist.", this);
                continue;
            }
            distances[layer] = entry.cullDistance;
        }

        cam.layerCullDistances = distances;
        cam.layerCullSpherical = sphericalDistance;
    }
}
