using UnityEngine;

// Owns FOV state: base FOV (captured at init), bite-zoom-in, and sprint FOV boost.
// PlayerCameraController forwards the bite events here and calls Tick() each frame.
public class CameraFovTracker
{
    private Camera cam;
    private float baseFov;
    private bool hasBaseFov;
    private bool isBiteActive;

    public void Initialize(Camera camera)
    {
        cam = camera;
        if (cam != null)
        {
            baseFov = cam.fieldOfView;
            hasBaseFov = true;
        }
    }

    public void OnBiteStart() => isBiteActive = true;
    public void OnBiteRelease() => isBiteActive = false;

    // Called from UpdateCamera each frame. Lerps cam.fieldOfView toward the right target
    // based on bite vs sprint state.
    public void Tick(
        Transform cameraTransform,
        bool isSprinting,
        float biteFovZoom,
        float biteFovLerpSpeed,
        float sprintFovBoost,
        float sprintFovLerpSpeed)
    {
        if (cam == null && cameraTransform != null) cam = cameraTransform.GetComponent<Camera>();
        if (cam == null) return;

        if (!hasBaseFov)
        {
            baseFov = cam.fieldOfView;
            hasBaseFov = true;
        }

        float targetFov;
        float lerpSpeed;
        if (isBiteActive)
        {
            targetFov = baseFov - biteFovZoom;
            lerpSpeed = biteFovLerpSpeed;
        }
        else
        {
            targetFov = baseFov + (isSprinting ? sprintFovBoost : 0f);
            lerpSpeed = sprintFovLerpSpeed;
        }
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, Time.deltaTime * lerpSpeed);
    }
}
