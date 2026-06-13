using UnityEngine;

// Spins the player's normal-mesh root while airborne after a charge-jump launch (or its
// bounce). Seeded by ChargeJumpController on launch with the launch speed; spin rate decays
// on bounce. Rotation is reset whenever the jump returns to None/Charging so we never carry
// a stale tilt back into idle.
public class PlayerAirborneTumble
{
    private Transform meshRoot;
    private Quaternion originalLocalRotation = Quaternion.identity;

    private Vector3 ballSpinAxis = Vector3.right;
    private float ballSpinSpeedDeg;

    public void Init(GameObject normalMeshRoot)
    {
        meshRoot = normalMeshRoot != null ? normalMeshRoot.transform : null;
        if (meshRoot != null) originalLocalRotation = meshRoot.localRotation;
    }

    public void Seed(float launchHorizontalSpeed,
                     float degPerUnitSpeed, float axisJitter, float speedJitter)
    {
        // Forward tumble around local X with a small random tilt so it doesn't look mechanical.
        Vector3 jitter = new Vector3(
            0f,
            Random.Range(-axisJitter, axisJitter),
            Random.Range(-axisJitter, axisJitter));
        ballSpinAxis = (Vector3.right + jitter).normalized;

        float rate = launchHorizontalSpeed * degPerUnitSpeed;
        ballSpinSpeedDeg = rate * (1f + Random.Range(-speedJitter, speedJitter));

        ResetRotation();
    }

    public void OnBounce(float bounceForwardMultiplier)
    {
        ballSpinSpeedDeg *= bounceForwardMultiplier;
    }

    public void ResetRotation()
    {
        if (meshRoot != null) meshRoot.localRotation = originalLocalRotation;
    }

    // shouldTumble: true while the jump is in Launched or Bounced phase (PlayerController decides).
    public void Tick(bool shouldTumble, bool isGrounded)
    {
        if (meshRoot == null) return;
        if (!shouldTumble) return;
        if (isGrounded) return;
        meshRoot.Rotate(ballSpinAxis, ballSpinSpeedDeg * Time.deltaTime, Space.Self);
    }
}
