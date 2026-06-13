using UnityEngine;

// Owns the small per-frame pitch perturbations applied to the orbit camera:
//   1. Nibble pitch dip — a brief downward look when a fish nibbles the bobber.
//   2. Charge progress — smoothed 0..1 value from FishingEvents.OnChargeProgressNormalized,
//      consumed by the orbit math for charge-driven pitch-up and zoom-in.
// PlayerCameraController forwards the relevant fishing events here and reads the smoothed
// values back each frame.
public class CameraReactionTracker
{
    private float nibbleHoldTimer;
    private float currentPitchOffset;

    private float chargeProgressTarget;
    private float chargeProgress;
    private float chargeProgressVel;

    public float CurrentNibblePitchOffset => currentPitchOffset;
    public float ChargeProgress => chargeProgress;

    public void OnNibble(float holdTime)
    {
        nibbleHoldTimer = holdTime;
    }

    // Bite cancels any pending nibble pitch — the camera is about to pull into the bite zoom
    // and we don't want the nibble dip fighting that.
    public void OnBite() => nibbleHoldTimer = 0f;

    public void SetChargeProgress(float t) => chargeProgressTarget = Mathf.Clamp01(t);
    public void ClearChargeProgress() => chargeProgressTarget = 0f;

    // Called each frame from PlayerCameraController.ComputePlayerCameraPose, before the orbit
    // math runs. Advances the nibble timer and the smoothed charge value.
    public void Tick(float nibblePitchOffsetMagnitude, float nibblePitchLerpSpeed,
                     float chargeSmoothTime, float chargeMaxSpeed)
    {
        if (nibbleHoldTimer > 0f) nibbleHoldTimer -= Time.deltaTime;
        float pitchTarget = nibbleHoldTimer > 0f ? nibblePitchOffsetMagnitude : 0f;
        currentPitchOffset = Mathf.Lerp(currentPitchOffset, pitchTarget, Time.deltaTime * nibblePitchLerpSpeed);

        chargeProgress = Mathf.SmoothDamp(chargeProgress, chargeProgressTarget, ref chargeProgressVel,
                                          chargeSmoothTime, chargeMaxSpeed, Time.deltaTime);
    }
}
