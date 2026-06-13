using UnityEngine;

// Owns the player's cartoon scale VFX:
//   - Charge-squash: progressive Y-squash while ChargeJumpController is in the Charging phase.
//   - Bounce-impact: one-shot curve play (squash → stretch → neutral) after BounceOnLand fires.
//     Driven directly (no lerp) so the curve plays exactly as authored.
// Bounce-impact wins over charge-squash priority when both overlap. ChargeJumpController triggers
// the bounce via TriggerBounceImpact() — this class owns the timer that drives the curve.
public class PlayerSquashStretch
{
    private Transform playerModel;
    private Transform normalMeshRoot;

    private Vector3 originalModelScale = Vector3.one;
    private Vector3 originalModelLocalPosition;
    private Vector3 originalNormalMeshScale = Vector3.one;

    private float bounceImpactTimer = -1f; // -1 = inactive

    public void Init(Transform playerModel, GameObject normalMeshRoot)
    {
        this.playerModel = playerModel;
        if (playerModel != null)
        {
            originalModelScale = playerModel.localScale;
            originalModelLocalPosition = playerModel.localPosition;
        }
        if (normalMeshRoot != null)
        {
            this.normalMeshRoot = normalMeshRoot.transform;
            originalNormalMeshScale = normalMeshRoot.transform.localScale;
        }
    }

    public void TriggerBounceImpact() => bounceImpactTimer = 0f;

    public void Tick(
        bool isCharging,
        float chargeNorm, // chargeTimer / maxChargeTime, 0..1
        float chargeMaxSquash,
        float scaleLerpSpeed,
        AnimationCurve bounceImpactCurve,
        float bounceImpactDuration)
    {
        if (playerModel == null) return;

        Vector3 scaleMul = Vector3.one;
        Vector3 targetPos = originalModelLocalPosition;
        bool driveDirectly = false;

        if (bounceImpactTimer >= 0f)
        {
            bounceImpactTimer += Time.deltaTime;
            if (bounceImpactTimer >= bounceImpactDuration)
            {
                bounceImpactTimer = -1f;
            }
            else
            {
                float bt = bounceImpactTimer / Mathf.Max(0.01f, bounceImpactDuration);
                float yScale = bounceImpactCurve.Evaluate(bt);
                float xz = VolumePreserveXZ(yScale);
                scaleMul = new Vector3(xz, yScale, xz);
                driveDirectly = true;
            }
        }

        // Charge squash takes priority if both overlap (e.g. user charges again immediately).
        if (isCharging)
        {
            float y = Mathf.Lerp(1f, chargeMaxSquash, Mathf.Clamp01(chargeNorm));
            float xz = VolumePreserveXZ(y);
            scaleMul = new Vector3(xz, y, xz);
            driveDirectly = false;
        }

        Vector3 targetScale = Vector3.Scale(originalModelScale, scaleMul);
        if (driveDirectly)
            playerModel.localScale = targetScale;
        else
            playerModel.localScale = Vector3.Lerp(playerModel.localScale, targetScale, scaleLerpSpeed * Time.deltaTime);
        playerModel.localPosition = Vector3.Lerp(playerModel.localPosition, targetPos, scaleLerpSpeed * Time.deltaTime);

        // Belt-and-suspenders: keep the mesh-root scale at its captured original so stale scaling
        // from a previous code path can never compound with playerModel's scale.
        if (normalMeshRoot != null && normalMeshRoot.localScale != originalNormalMeshScale)
            normalMeshRoot.localScale = originalNormalMeshScale;
    }

    private static float VolumePreserveXZ(float yScale)
    {
        return 1f / Mathf.Sqrt(Mathf.Max(0.01f, yScale));
    }
}
