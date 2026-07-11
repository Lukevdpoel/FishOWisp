using UnityEngine;

// Throttles a capture camera (e.g. TerrainCapture rendering terrain color into
// TerrainColor.renderTexture for the grass shader) so it renders one frame every `interval`
// seconds instead of every frame. The captured content only changes as slowly as the scene
// lighting does, but an enabled camera pays full culling + render-graph record + draw cost
// per frame — this keeps the RenderTexture fresh at a tiny fraction of that.
//
// The first frame after enable always renders, so the texture is never blank on scene load.
[RequireComponent(typeof(Camera))]
public class CaptureCameraThrottle : MonoBehaviour
{
    [Tooltip("Seconds between captures. The terrain tint only drifts with the day/night cycle, so ~1s is visually indistinguishable from every-frame. 0 disables throttling (camera stays on).")]
    public float interval = 1f;

    private Camera cam;
    private float nextCaptureTime;
    private bool renderPending;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void OnEnable()
    {
        // Always capture immediately on enable/scene load.
        cam.enabled = true;
        renderPending = true;
    }

    void OnDisable()
    {
        // Leave the camera in its authored (always-on) state when the throttle is switched off.
        if (cam != null) cam.enabled = true;
    }

    // Rendering happens after LateUpdate, so: enable here -> the camera renders at the end of
    // THIS frame -> next frame's LateUpdate sees the render done and disables it again.
    void LateUpdate()
    {
        if (interval <= 0f)
        {
            if (!cam.enabled) cam.enabled = true;
            return;
        }

        if (renderPending)
        {
            // The camera was just enabled (by us or scene load) and renders at the end of this
            // frame — leave it on for that render, then shut it off next LateUpdate.
            renderPending = false;
            return;
        }

        if (cam.enabled)
        {
            cam.enabled = false;
            nextCaptureTime = Time.unscaledTime + interval;
        }
        else if (Time.unscaledTime >= nextCaptureTime)
        {
            cam.enabled = true;
            renderPending = true;
        }
    }
}
