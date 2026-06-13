using UnityEngine;

// Owns the in-water bobber-follow rig: tracks which bobber is being followed, computes a
// per-frame bobber-side camera pose, and exposes blend state so the spine
// (PlayerCameraController) can interpolate between player orbit and bobber pose.
//
// Note: the "reel-press return" path (snapshot in-water pose, lerp back to player over a
// fixed duration) is preserved here but currently unreachable — no caller sets
// IsReturningToPlayer to true. Behavior parity with the previous implementation, kept in
// case it gets re-enabled.
public class BobberCameraTracker
{
    public struct Pose
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    private BobberController bobberFollowController;
    private Transform bobberFollowTarget;

    private Pose bobberCameraPose;
    private bool hasBobberCameraPose;
    private float bobberCameraBlend;
    private float bobberCameraBlendTarget;

    private bool isReturningToPlayer;
    private Pose returnFromPose;
    private float returnT;

    public bool HasBobberCameraPose => hasBobberCameraPose;
    public float BlendValue => bobberCameraBlend;
    public float BlendTarget => bobberCameraBlendTarget;
    public Pose CurrentBobberPose => bobberCameraPose;
    public bool IsReturningToPlayer => isReturningToPlayer;
    public Pose ReturnFromPose => returnFromPose;
    public float ReturnT => returnT;

    // True when the bobber rig should suppress player orbit input (i.e. the camera is owned
    // by the bobber side or is mid-return). Read by the spine before applying mouse input.
    public bool BobberCameraDominant => bobberCameraBlend > 0.01f || isReturningToPlayer;

    public void OnBobberLanded(BobberController b)
    {
        if (b == null) return;
        isReturningToPlayer = false;
        bobberFollowController = b;
        bobberFollowTarget = b.transform;
        bobberCameraBlendTarget = 1f;
    }

    // Empty-line reel: stop tracking the bobber and fade the camera back to the player.
    // Drops the follow target immediately so we don't sample the bobber transform as it
    // arcs back to the rod, but keeps the last computed bobber pose around so the blend
    // value can ease from its current value down to 0 over bobberFollowLerpSpeed.
    // ReleaseIfFadedOut (called from the spine when blend < 0.001) handles final cleanup.
    public void HardClear()
    {
        bobberCameraBlendTarget = 0f;
        bobberFollowTarget = null;
        bobberFollowController = null;
        isReturningToPlayer = false;
    }

    // True iff there is an active bobber follow target — used by the spine to decide whether
    // to call HardClear in HandleStartReeling.
    public bool HasFollowTarget => bobberFollowTarget != null;
    public bool HasHookedFish => bobberFollowController != null && bobberFollowController.HookedFish != null;

    // Hooked-fish path: fade out smoothly, but stop sampling the bobber transform immediately.
    public void OnFollowEnd()
    {
        bobberCameraBlendTarget = 0f;
        bobberFollowTarget = null;
        bobberFollowController = null;
    }

    // Advance the blend toward the target. Called once per frame from the spine before
    // applying the blend to camera position.
    public void TickBlend(float bobberFollowLerpSpeed)
    {
        float t = 1f - Mathf.Exp(-bobberFollowLerpSpeed * Time.deltaTime);
        bobberCameraBlend = Mathf.Lerp(bobberCameraBlend, bobberCameraBlendTarget, t);
    }

    // Called by the spine once the blend has fully faded out, to release any lingering pose.
    public void ReleaseIfFadedOut()
    {
        if (bobberCameraBlendTarget <= 0.001f && hasBobberCameraPose)
        {
            bobberCameraBlend = 0f;
            hasBobberCameraPose = false;
            bobberFollowTarget = null;
            bobberFollowController = null;
        }
    }

    public void AdvanceReturnT(float returnDuration)
    {
        float dur = Mathf.Max(returnDuration, 0.0001f);
        returnT += Time.deltaTime / dur;
        if (returnT >= 1f) isReturningToPlayer = false;
    }

    // Compute and store the bobber-side pose if there is an active follow target. Returns
    // true if a pose was produced this frame. Inputs from the spine:
    //   smoothXAngle/smoothYAngle: shared orbit angles, so the bobber pose mirrors the
    //     player orbit when bobberOrbitWithMouse is true.
    //   cameraTransform: fallback rotation source when no anchor is available.
    public bool ComputePose(Transform playerModel, Transform cameraTransform,
                            float smoothXAngle, float smoothYAngle,
                            float bobberFollowHeight, float bobberFollowDistance,
                            bool bobberOrbitWithMouse, float bobberCameraWaterClearance)
    {
        if (bobberFollowTarget == null) return false;

        Vector3 bobberPos = bobberFollowTarget.position;
        Vector3 bobberPivot = bobberPos + Vector3.up * bobberFollowHeight;

        if (bobberOrbitWithMouse)
        {
            Quaternion rotation = Quaternion.Euler(smoothYAngle, smoothXAngle, 0f);
            Vector3 cameraDirection = -(rotation * Vector3.forward);
            Vector3 orbitPos = bobberPivot + cameraDirection * bobberFollowDistance;

            // Don't dip below the water plane. Lift and re-aim at the pivot if we would.
            if (bobberFollowController != null && bobberFollowController.IsInWater)
            {
                float minY = bobberFollowController.WaterSurfaceY + bobberCameraWaterClearance;
                if (orbitPos.y < minY)
                {
                    orbitPos.y = minY;
                    Vector3 liftedLookDir = bobberPivot - orbitPos;
                    if (liftedLookDir.sqrMagnitude > 0.0001f)
                        rotation = Quaternion.LookRotation(liftedLookDir.normalized);
                }
            }

            bobberCameraPose.position = orbitPos;
            bobberCameraPose.rotation = rotation;
            hasBobberCameraPose = true;
            return true;
        }

        // Legacy path: read pose from a child anchor on the bobber prefab if one exists.
        Transform anchor = bobberFollowController != null ? bobberFollowController.CameraAnchor : null;
        if (anchor != null)
        {
            bobberCameraPose.position = anchor.position;
            bobberCameraPose.rotation = anchor.rotation;
            hasBobberCameraPose = true;
            return true;
        }

        // Final fallback: fixed pose looking back toward the bobber from the player's side.
        Vector3 playerPos = playerModel != null ? playerModel.position : bobberPos;
        Vector3 toBobber = bobberPos - playerPos;
        toBobber.y = 0f;
        Vector3 awayFromBobber;
        if (toBobber.sqrMagnitude > 0.0001f)
        {
            awayFromBobber = -toBobber.normalized;
        }
        else
        {
            Vector3 fwd = playerModel != null ? playerModel.forward : Vector3.forward;
            fwd.y = 0f;
            awayFromBobber = fwd.sqrMagnitude > 0.0001f ? -fwd.normalized : -Vector3.forward;
        }

        Vector3 fallbackPos = bobberPivot + awayFromBobber * bobberFollowDistance;
        Vector3 lookDir = bobberPos - fallbackPos;
        Quaternion fallbackRot = lookDir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(lookDir.normalized)
            : (cameraTransform != null ? cameraTransform.rotation : Quaternion.identity);

        bobberCameraPose.position = fallbackPos;
        bobberCameraPose.rotation = fallbackRot;
        hasBobberCameraPose = true;
        return true;
    }
}
