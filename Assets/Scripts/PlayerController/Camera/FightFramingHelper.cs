using UnityEngine;

// Over-shoulder framing for fish fights. Owns its own smoothing state so each fight transition
// ramps in from the prior orbit pose (no snap) and so the smoothing doesn't bleed between fights.
//
// Usage: PlayerCameraController computes the orbit pose first, stores it in basePose, then calls
// Apply() with the fight inputs. If a fight is active, basePose is overwritten with the smoothed
// over-shoulder pose; otherwise smoothing is reset and basePose is left unchanged.
public class FightFramingHelper
{
    private Vector3 smoothedPos;
    private Quaternion smoothedRot = Quaternion.identity;
    private bool initialized;

    public void Apply(
        ref Vector3 basePosition,
        ref Quaternion baseRotation,
        bool isFightingFish,
        Transform playerModel,
        Transform activeBobberTransform,
        float shoulderSide,
        float shoulderBack,
        float shoulderHeight,
        float framingAimT,
        float framingLerpSpeed)
    {
        if (!isFightingFish || activeBobberTransform == null || playerModel == null)
        {
            // Reset so the next fight onset starts from the current orbit pose, not stale state.
            smoothedPos = basePosition;
            smoothedRot = baseRotation;
            initialized = false;
            return;
        }

        Vector3 playerPos = playerModel.position;
        Vector3 bobberPos = activeBobberTransform.position;

        Vector3 toBobber = bobberPos - playerPos;
        toBobber.y = 0f;
        Vector3 forward = toBobber.sqrMagnitude > 0.0001f ? toBobber.normalized : playerModel.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        Vector3 targetPos = playerPos
                            + right * shoulderSide
                            - forward * shoulderBack
                            + Vector3.up * shoulderHeight;

        // Aim along the player→bobber line so both are framed.
        Vector3 playerLookAnchor = playerPos + Vector3.up * (shoulderHeight * 0.7f);
        Vector3 lookAt = Vector3.Lerp(playerLookAnchor, bobberPos, framingAimT);
        Vector3 lookDir = lookAt - targetPos;
        Quaternion targetRot = lookDir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(lookDir.normalized)
            : baseRotation;

        if (!initialized)
        {
            smoothedPos = basePosition;
            smoothedRot = baseRotation;
            initialized = true;
        }

        float t = 1f - Mathf.Exp(-framingLerpSpeed * Time.deltaTime);
        smoothedPos = Vector3.Lerp(smoothedPos, targetPos, t);
        smoothedRot = Quaternion.Slerp(smoothedRot, targetRot, t);

        basePosition = smoothedPos;
        baseRotation = smoothedRot;
    }
}
