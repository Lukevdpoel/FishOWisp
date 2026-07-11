using UnityEngine;

// Owns the player's cartoon scale VFX:
//   - Charge-squash: progressive Y-squash while ChargeJumpController is in the Charging phase.
//   - Bounce-impact: one-shot curve play (squash → stretch → neutral) after BounceOnLand fires.
//     Driven directly (no lerp) so the curve plays exactly as authored.
// Bounce-impact wins over charge-squash priority when both overlap. ChargeJumpController triggers
// the bounce via TriggerBounceImpact() — this class owns the timer that drives the curve.
//
// WORLD-SPACE SQUASH: the deformation is applied on a dedicated, world-axis-aligned parent inserted
// above playerModel (the "squash root"), NOT on playerModel itself. A Transform's localScale is
// applied before its rotation in the TRS chain, so scaling playerModel directly squashes along the
// model's *local* up — which tilts with the model (e.g. the bounce, where the model is still
// righting itself out of the ball tumble), giving a crooked squash that rides the model's rotation.
// By scaling a parent whose axes stay aligned to the world, the scale is outermost in the chain
// (S · R · p): a pure world-space vertical pancake of the already-rotated model, independent of how
// the model is oriented or how it keeps rotating while the bounce plays out.
public class PlayerSquashStretch
{
    private Transform playerModel;
    private Transform normalMeshRoot;

    // The world-axis-aligned parent we actually scale. Created in Init by inserting it above
    // playerModel. Falls back to playerModel itself if it has no parent to insert under.
    private Transform squashRoot;

    // Keep-base-planted: the charge/bounce squash scales about squashRoot's pivot (the model origin), which
    // lifts the model's BASE off the ground as it flattens — the body appears to hover. We counter-translate
    // squashRoot down by exactly that lift so the base stays put. Because squashRoot parents the WHOLE model
    // (the body mesh AND the separate limb meshes), they all drop together as one — which is what stops the
    // body sinking to the ground while the limbs hang behind it. Only the squash (scale < 1) is compensated;
    // the bounce's stretch phase (scale > 1) is left alone so its "stretch up off the ground" look is intact.
    private bool squashRootInserted;      // false in the no-parent fallback, where moving playerModel would fight the animator
    private Vector3 restSquashLocalPos;   // squashRoot's neutral local position, captured at Init
    private float pivotToBase;            // world distance from the squash pivot down to the model's base, measured at Init

    private Vector3 originalNormalMeshScale = Vector3.one;

    private float bounceImpactTimer = -1f; // -1 = inactive

    // Velocity-aligned jump stretch (Dragon-Quest-slime elongation along the flight path). Two nested
    // nodes inserted between playerModel and normalMeshRoot form a "rotate · scale · un-rotate" sandwich:
    // the outer rotates so its local up points along the world velocity and scales along it (squashing the
    // perpendicular axes to preserve volume), the inner cancels that rotation. Net result is a clean
    // directional stretch of the whole body in WORLD space that leaves every child's orientation untouched —
    // so the ball-tumble (which lives above, on playerModel) and the morph clips are unaffected. Placing it
    // BELOW playerModel keeps the non-uniform scale out of the tumble's world-rotation math and out of
    // squashRoot's world-vertical bounce pancake.
    private Transform stretchOuter;
    private Transform stretchInner;
    private float currentStretch01;             // smoothed stretch amount, 0 = round, 1 = max elongation
    private Vector3 lastStretchDir = Vector3.up; // held during ease-out so the axis doesn't snap

    public void Init(Transform playerModel, GameObject normalMeshRoot)
    {
        this.playerModel = playerModel;

        if (normalMeshRoot != null)
        {
            this.normalMeshRoot = normalMeshRoot.transform;
            originalNormalMeshScale = normalMeshRoot.transform.localScale;
        }

        if (playerModel == null) return;

        // Insert a fresh world-aligned node between playerModel and its parent, then reparent
        // playerModel under it (keeping world pose). We scale this node, never playerModel — so the
        // squash axes stay world-aligned regardless of the model's own rotation/tumble.
        Transform parent = playerModel.parent;
        if (parent != null)
        {
            var go = new GameObject("SquashRoot_WorldAligned");
            squashRoot = go.transform;
            squashRoot.SetParent(parent, false);
            squashRoot.position = playerModel.position;
            squashRoot.rotation = Quaternion.identity; // world-axis aligned
            squashRoot.localScale = Vector3.one;
            playerModel.SetParent(squashRoot, true); // keep world position/rotation
        }
        else
        {
            // No parent to insert under — degrade to the old (local-space) behaviour rather than fail.
            squashRoot = playerModel;
        }

        // Only keep-base-planted when we actually inserted our own node; moving playerModel itself would
        // fight the animator that drives it. Capture the neutral pose and how far the base sits below the pivot.
        squashRootInserted = squashRoot != playerModel;
        if (squashRootInserted)
        {
            restSquashLocalPos = squashRoot.localPosition;
            pivotToBase = MeasurePivotToBase();
        }

        BuildVelocityStretchNodes();
    }

    // World distance from the squash pivot (squashRoot) down to the lowest point of the model, measured from
    // the mesh renderers in their neutral pose at startup. This is the amount the base would lift per unit of
    // squash, so the counter-translation can cancel it exactly.
    private float MeasurePivotToBase()
    {
        if (playerModel == null || squashRoot == null) return 0f;

        bool has = false;
        Bounds b = default;
        foreach (var r in playerModel.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            if (has) b.Encapsulate(r.bounds);
            else { b = r.bounds; has = true; }
        }
        if (!has) return 0f;
        return Mathf.Max(0f, squashRoot.position.y - b.min.y);
    }

    // Inserts the two-node stretch sandwich between playerModel and normalMeshRoot (see fields above).
    private void BuildVelocityStretchNodes()
    {
        if (normalMeshRoot == null || playerModel == null) return;

        Transform meshParent = normalMeshRoot.parent; // playerModel — normalMeshRoot rides under it
        if (meshParent == null) return;

        var outerGO = new GameObject("VelocityStretch_Outer");
        stretchOuter = outerGO.transform;
        stretchOuter.SetParent(meshParent, false);
        stretchOuter.localPosition = normalMeshRoot.localPosition; // pivot the stretch about the mesh root
        stretchOuter.localRotation = Quaternion.identity;
        stretchOuter.localScale = Vector3.one;

        var innerGO = new GameObject("VelocityStretch_Inner");
        stretchInner = innerGO.transform;
        stretchInner.SetParent(stretchOuter, false);
        stretchInner.localPosition = Vector3.zero;
        stretchInner.localRotation = Quaternion.identity;
        stretchInner.localScale = Vector3.one;

        normalMeshRoot.SetParent(stretchInner, true); // keep world pose; its local offset collapses to ~0
    }

    public void TriggerBounceImpact() => bounceImpactTimer = 0f;

    public void Tick(
        bool isCharging,
        float chargeNorm, // chargeTimer / maxChargeTime, 0..1
        float chargeMaxSquash,
        float scaleLerpSpeed,
        AnimationCurve bounceImpactCurve,
        float bounceImpactDuration,
        float bounceImpactIntensity,
        float chargeGroundSink) // extra world-units the base sinks toward the ground at full charge
    {
        if (squashRoot == null) return;

        Vector3 scaleMul = Vector3.one;
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
                // Scale the bounce's deviation from neutral by intensity (1 = full authored curve,
                // 0 = no bounce), so the whole squash/stretch can be dialled down without re-drawing it.
                float yScale = Mathf.LerpUnclamped(1f, bounceImpactCurve.Evaluate(bt), bounceImpactIntensity);
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

        if (driveDirectly)
            squashRoot.localScale = scaleMul;
        else
            squashRoot.localScale = Vector3.Lerp(squashRoot.localScale, scaleMul, scaleLerpSpeed * Time.deltaTime);

        // Counter-translate the squash node down by the lift the squash introduces, so the model's base stays
        // planted on the ground as it flattens (see field comment). Only squash (scale.y < 1) is compensated,
        // so the bounce's stretch phase reads unchanged. Moves body + limb meshes together, being on squashRoot.
        if (squashRootInserted)
        {
            float squashLift = pivotToBase * Mathf.Max(0f, 1f - squashRoot.localScale.y);
            // Extra sink toward (and slightly into) the ground, eased in by charge so it never pops. Only while
            // charging — the bounce isn't sunk. This is the knob for "the drape still floats a little too high".
            float sink = isCharging ? chargeGroundSink * Mathf.Clamp01(chargeNorm) : 0f;
            squashRoot.localPosition = new Vector3(
                restSquashLocalPos.x, restSquashLocalPos.y - squashLift - sink, restSquashLocalPos.z);
        }

        // Belt-and-suspenders: keep the mesh-root scale at its captured original so stale scaling
        // from a previous code path can never compound with the squash node's scale.
        if (normalMeshRoot != null && normalMeshRoot.localScale != originalNormalMeshScale)
            normalMeshRoot.localScale = originalNormalMeshScale;
    }

    // Injects a stretch impulse — call once on the launch/bounce edge. amount01 should scale with launch
    // speed (speed / maxSpeed). The body snaps to this elongation; TickJumpStretch then springs it back.
    //   launchDir : the launch velocity direction. The stretch axis is SNAPPED to it here (not eased), so the
    //               ease in TickJumpStretch only damps ongoing mid-air jitter — otherwise the axis would visibly
    //               swing from its stale held value (e.g. straight up) into the launch direction over the first
    //               few frames, reading as a stretch stuck on a wrong upward axis before it corrects.
    public void InjectJumpStretch(float amount01, Vector3 launchDir)
    {
        currentStretch01 = Mathf.Max(currentStretch01, Mathf.Clamp01(amount01));
        if (launchDir.sqrMagnitude > 0.04f) lastStretchDir = launchDir.normalized;
    }

    // Relaxes the jump stretch each frame. The elongation spikes on InjectJumpStretch then decays
    // (exponential spring-back), so the body reads as stretched BY the fast launch and returns to round
    // well before it lands — rather than tracking flight speed for the whole arc.
    //   worldVelocity : current velocity; only its DIRECTION is used, to point the stretch along the arc.
    //   maxStretch    : elongation along the flight axis at full energy (0.6 = +60% length, volume-preserved).
    //   decayPerSec   : spring-back rate; higher returns to round earlier in the jump.
    //   dirResponse   : how fast the stretch AXIS eases toward the velocity direction (per second). Lower is
    //                   smoother/laggier and kills the mid-air axis jitter; higher tracks the arc more tightly.
    public void TickJumpStretch(Vector3 worldVelocity, float maxStretch, float decayPerSec, float dirResponse)
    {
        if (stretchOuter == null || stretchInner == null) return;

        // Point the stretch along the current motion while there's energy, so it follows the arc as it
        // relaxes; hold the last direction otherwise so it can't snap. Ease the axis toward the velocity
        // rather than snapping to it each frame: the elongation AMOUNT already decays smoothly, but
        // hard-tracking the instantaneous direction makes the axis jitter (air-steer nudges, the vertical
        // crossing zero near the apex), which reads as a fast "ping-pong" of the stretched body. This slerp
        // is framerate-independent (exp), so the damping feel is the same at any frame rate.
        if (worldVelocity.sqrMagnitude > 0.04f)
        {
            Vector3 target = worldVelocity.normalized;
            float t = 1f - Mathf.Exp(-Mathf.Max(0f, dirResponse) * Time.deltaTime);
            lastStretchDir = Vector3.Slerp(lastStretchDir, target, t).normalized;
        }

        currentStretch01 *= Mathf.Exp(-Mathf.Max(0f, decayPerSec) * Time.deltaTime);

        if (currentStretch01 <= 1e-3f)
        {
            currentStretch01 = 0f;
            stretchOuter.localRotation = Quaternion.identity;
            stretchOuter.localScale = Vector3.one;
            stretchInner.localRotation = Quaternion.identity;
            return;
        }

        float along = 1f + Mathf.Max(0f, maxStretch) * currentStretch01;
        float perp = 1f / Mathf.Sqrt(along); // volume-preserving squash on the two perpendicular axes

        // Express the world stretch rotation in the sandwich's parent (playerModel) space, so the stretch
        // axis tracks WORLD velocity regardless of how playerModel is oriented. parentRot is the live rot.
        Quaternion worldRot = Quaternion.FromToRotation(Vector3.up, lastStretchDir);
        Quaternion parentRot = stretchOuter.parent != null ? stretchOuter.parent.rotation : Quaternion.identity;
        Quaternion localRot = Quaternion.Inverse(parentRot) * worldRot;

        stretchOuter.localRotation = localRot;
        stretchOuter.localScale = new Vector3(perp, along, perp);
        stretchInner.localRotation = Quaternion.Inverse(localRot);
        stretchInner.localScale = Vector3.one;
    }

    private static float VolumePreserveXZ(float yScale)
    {
        return 1f / Mathf.Sqrt(Mathf.Max(0.01f, yScale));
    }
}
