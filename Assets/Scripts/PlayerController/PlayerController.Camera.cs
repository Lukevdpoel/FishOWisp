using System.Collections;
using UnityEngine;

// Part of PlayerController (partial class). Serialized fields live in PlayerController.cs.
public partial class PlayerController
{
    // Re-seeds the orbit camera from the gameplay camera's CURRENT transform. Used by the main-menu
    // handoff: after a Cinemachine blend leaves the camera at an over-the-shoulder pose, this lets
    // PlayerCameraController pick up smoothly from exactly that pose instead of snapping. Mirrors the
    // Initialize() call in Start(), so it's safe to call before this component's own Start has run.
    public void ReinitializeCamera()
    {
        // measureFromPivot:true so the orbit pose reconstructs exactly onto the current (blended)
        // camera pose instead of pulling back by the origin->pivot offset. See Initialize().
        if (cameraController != null) cameraController.Initialize(playerModel, true);
        cameraSeeded = true;
    }

    // Seeds the resting orbit pose for the title single-move handoff from the PlayerFollow vcam's
    // world pose (angles from its rotation, so the camera lands behind the player regardless of the
    // player's still-settling position). Marks the camera seeded so Start won't re-seed over it.
    public void SeedCameraFromMenuVcam(Vector3 vcamPos, Quaternion vcamRot)
    {
        if (cameraController != null) cameraController.SeedRestingPoseFromMenuVcam(playerModel, vcamPos, vcamRot);
        cameraSeeded = true;
    }

    // Eases the camera from its current pose into the live orbit pose over `duration` seconds.
    // The main menu calls this right after ReinitializeCamera so the takeover from the Cinemachine
    // blend doesn't snap. Call while the camera still sits at the blend's end pose.
    public void BeginCameraHandoffBlend(float duration)
    {
        if (cameraController != null) cameraController.BeginHandoffBlend(duration);
    }

    public void LockOnFish(Transform target)
    {
        isLockedOnFish = true;
        fishLockTarget = target;
        ZeroMovement();
    }

    public void UnlockFromFish()
    {
        isLockedOnFish = false;
        fishLockTarget = null;
    }

    public void SetCatchCamera(bool active)
    {
        if (cameraController != null) cameraController.SetCatchCamera(active);
    }

    // Forces the static-camera follow code to recompute its framing offset on the next LateUpdate.
    // Call before re-entering a shop so framing doesn't carry over from a previous visit.
    public void ResetStaticCameraOffset()
    {
        staticCameraOffsetCaptured = false;
    }

    // followPlayer=true keeps the legacy behavior (camera glides at a captured offset from the player).
    // followPlayer=false leaves the camera frozen wherever it was placed in the editor — used for shop
    // interiors where we want a truly fixed shot. Input is still remapped to this camera's forward/right
    // regardless, which is what gives WASD the 2D-ish plane.
    public void SetStaticCamera(bool active, Transform target, bool followPlayer = true)
    {
        useStaticCamera = active;
        staticCameraTarget = target;
        staticCameraFollowsPlayer = followPlayer;
        staticCameraOffsetCaptured = false;
    }

    // The transform of the camera the player controller drives — the one PlayerCameraController
    // snaps back to after a UI panel releases the view. UI that temporarily hijacks Camera.main
    // (e.g. the loadout framing camera) compares against this so it never grabs a fixed camera
    // (like a shop interior view) that nothing would restore, leaving it stranded mid-zoom.
    public Transform ActivePlayerCameraTransform =>
        cameraController != null ? cameraController.CameraTransform : null;

    private bool staticCameraFollowsPlayer = true;

    private void HandleStaticCameraFollow()
    {
        if (!useStaticCamera || staticCameraTarget == null) return;
        if (!staticCameraFollowsPlayer) return;

        if (!staticCameraOffsetCaptured)
        {
            // Compute an offset that places the player at the camera's forward-ray hit on the
            // player's Y plane. This keeps the player centered on screen regardless of where
            // they spawn, instead of preserving whatever framing existed at scene start.
            Vector3 camPos = staticCameraTarget.position;
            Vector3 camForward = staticCameraTarget.forward;
            float fy = camForward.y;
            if (Mathf.Abs(fy) > 1e-4f)
            {
                float t = (transform.position.y - camPos.y) / fy;
                staticCameraOffset = -camForward * t;
            }
            else
            {
                staticCameraOffset = camPos - transform.position;
            }
            staticCameraOffsetCaptured = true;
        }
        staticCameraTarget.position = transform.position + staticCameraOffset;
    }

}
