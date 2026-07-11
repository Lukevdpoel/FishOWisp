using UnityEngine;

// Owns the purely-visual midair "ball tumble": while launched in ball form the model spins around a
// random axis at a random speed, re-seeded each launch. The spin is applied to playerModel (the parent
// of normalMeshRoot, where the body Animator lives) so it rides over the morph clips instead of fighting
// them, and is pivoted about the body mesh's bounds-center — its visual middle — rather than playerModel's
// own origin.
//
// Split across two LateUpdate calls so the camera reads a stable body point:
//   - TickReset() runs BEFORE the camera. It seeds the tumble on the first launched frame, advances the
//     spin angle, and snaps playerModel back to its rest pose, so the camera (which reads
//     playerModel.position with no smoothing) follows the stable rest position.
//   - ApplySpin() runs AFTER the camera, orbiting playerModel about the bounds-center pivot. Because it's
//     applied post-camera it's purely visual and never drags the camera pivot.
//
// PlayerController keeps the inspector tunables (enable flag, min/max speed, exclude roots) and passes
// them in, matching how ChargeJumpController and PlayerSquashStretch are driven.
public class PlayerBallTumble
{
    private Transform playerModel;
    private Transform playerRoot;

    // Body renderers used for the bounds-center pivot, cached at Init. Excludes any ballPivotExcludeRoots
    // subtrees (e.g. the held fishing rod) so they can't pull the pivot off the body's center.
    private Renderer[] bodyRenderers;

    // Random midair tumble state, re-seeded each launch (see TickReset).
    private Vector3 ballTumbleAxis = Vector3.right;
    private float ballTumbleSpeed;
    private float ballTumbleAngle;
    private bool ballTumbleSeeded;
    private Vector3 ballRestLocalPos;
    private Quaternion ballRestLocalRot = Quaternion.identity;
    private Vector3 ballPivotRootLocal;   // ball-center spin pivot, stored in player-root local space

    public void Init(Transform playerModel, Transform playerRoot, Transform normalMeshRoot, Transform[] excludeRoots)
    {
        this.playerModel = playerModel;
        this.playerRoot = playerRoot;

        if (normalMeshRoot != null)
        {
            var all = normalMeshRoot.GetComponentsInChildren<Renderer>(true);
            bodyRenderers = System.Array.FindAll(all, r => !IsUnderExcludedRoot(r.transform, excludeRoots));
        }
    }

    // Re-arm so the next launch rolls a fresh axis/speed/pivot. Call once fully grounded (jump phase None),
    // not at the bounce — TickReset keeps easing the model back to rest while seeded stays true.
    public void Disarm() => ballTumbleSeeded = false;

    // First half, run BEFORE the camera in LateUpdate. Seeds a random axis/speed/pivot on the first
    // launched frame, advances the spin angle, and snaps playerModel back to its rest pose. Returns true
    // while a tumble is active this frame (i.e. the caller should call ApplySpin after the camera).
    public bool TickReset(bool enabled, bool isLaunched, float minSpeed, float maxSpeed, float rotationSpeed)
    {
        if (playerModel == null) return false;

        if (!enabled || !isLaunched)
        {
            // Not spinning this frame. If we tumbled during this jump, the last spun frame left
            // playerModel orbited off its rest pivot — ApplySpin's RotateAround moves it because the
            // pivot is the body center, not the model origin — and nothing else clears that offset.
            // Ease it back to rest so the model settles centered after the jump; rotation is handled
            // separately by the caller's upright slerp.
            if (ballTumbleSeeded)
                playerModel.localPosition = Vector3.Lerp(
                    playerModel.localPosition, ballRestLocalPos, rotationSpeed * Time.deltaTime);
            return false;
        }

        if (!ballTumbleSeeded)
        {
            ballTumbleAxis = Random.onUnitSphere;
            ballTumbleSpeed = Random.Range(minSpeed, maxSpeed);
            ballTumbleAngle = 0f;
            ballRestLocalPos = playerModel.localPosition;
            ballRestLocalRot = playerModel.localRotation;
            ballPivotRootLocal = playerRoot.InverseTransformPoint(ComputeBallCenterWorld());
            ballTumbleSeeded = true;
        }

        ballTumbleAngle += ballTumbleSpeed * Time.deltaTime;
        playerModel.localPosition = ballRestLocalPos;
        playerModel.localRotation = ballRestLocalRot;
        return true;
    }

    // Second half, run AFTER the camera. Spins playerModel about the mesh's bounds-center pivot (which
    // sits at the visual middle, unlike playerModel's own origin). Applying it post-camera keeps the
    // orbit purely visual — the camera already framed the stable rest position.
    public void ApplySpin()
    {
        playerModel.RotateAround(playerRoot.TransformPoint(ballPivotRootLocal), ballTumbleAxis, ballTumbleAngle);
    }

    // World-space point the ball spins about: the combined bounds center of the body mesh (its visual
    // middle). Falls back to the model origin if no renderers were cached.
    private Vector3 ComputeBallCenterWorld()
    {
        if (bodyRenderers == null || bodyRenderers.Length == 0) return playerModel.position;

        bool has = false;
        Bounds b = new Bounds();
        foreach (var r in bodyRenderers)
        {
            // Skip hidden renderers (e.g. the bobber/line disabled this frame) so they don't drag the
            // pivot off the visible ball.
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        return has ? b.center : playerModel.position;
    }

    // True if t is the same transform as, or nested under, any configured excludeRoots entry — used to
    // keep the held rod (and similar offset props) out of the spin pivot.
    private static bool IsUnderExcludedRoot(Transform t, Transform[] excludeRoots)
    {
        if (excludeRoots == null) return false;
        foreach (var root in excludeRoots)
            if (root != null && t.IsChildOf(root)) return true;
        return false;
    }
}
