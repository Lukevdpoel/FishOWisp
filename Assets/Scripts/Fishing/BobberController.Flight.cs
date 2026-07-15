using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Part of BobberController (partial class). Serialized fields live in BobberController.cs.
public partial class BobberController
{
    public void ResetForCast()
    {
        // FishingRodController disables this component during its reel-in arc. Re-enable so FixedUpdate
        // (extra gravity, buoyancy) runs again on the next cast.
        enabled = true;

        CancelNibbleSequence();
        StopAllCoroutines();

        // Silence the cast whir and any fight loop — a fresh cast/park starts from quiet.
        StopCastFly();
        if (fightLoopSource != null && fightLoopSource.isPlaying) fightLoopSource.Stop();

        // If the fight ended mid-leap, tell the rope the fish is down so it doesn't stay slack.
        EndFightJump();

        isInWater = false;
        waterVolumes.Clear();
        isInFlight = false;
        airSteerArmed = false;
        spiralInitialized = false;
        pendingLaunchVelocity = Vector3.zero;
        pendingCastCharge = 1f;
        isSubmerged = false;
        hasSplashed = false;
        hasPlayedSplashSound = false;
        hasPlayedImpactSound = false;
        isStruggling = false;
        fightBurst = false;
        hasStrikeYaw = false;
        hasStruggleAnchor = false;
        hookedFish = null;
        isGrabTowed = false;
        lureVisualPitchDeg = 0f;
        lureBobOffset = 0f;
        isPopperLure = false;
        popperBounceDeg = 0f;
        popperLeanPitch = 0f;
        nibbleWobbleAmp = 0f;
        nibbleWobblePhase = 0f;

        if (activeFishModel != null)
        {
            Destroy(activeFishModel);
            activeFishModel = null;
        }
        if (bobberVisuals != null) bobberVisuals.SetActive(true);

        if (activeWakeInstance != null)
        {
            Destroy(activeWakeInstance);
            activeWakeInstance = null;
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.linearDamping = initialLinearDamping;
            rb.angularDamping = initialAngularDamping;
        }
    }

    // Mid-flight steering acceleration from the move axes, expressed relative to the current
    // horizontal travel direction (stick left = curve left across the arc, stick up = carry
    // further). Reads input state directly — GetAxisRaw and GamepadInput.Move are level-based,
    // so sampling them in FixedUpdate is safe (same pattern as FishingRodController.TickLure).
    private Vector3 ReadAirSteerAccel(Vector3 travelVelocity)
    {
        if (!airSteerEnabled) return Vector3.zero;
        if (NoteMenu.IsNotebookOpen || Time.timeScale == 0f) return Vector3.zero;

        float side = Mathf.Clamp(Input.GetAxisRaw("Horizontal") + GamepadInput.Move.x, -1f, 1f);
        float fore = Mathf.Clamp(Input.GetAxisRaw("Vertical") + GamepadInput.Move.y, -1f, 1f);
        if (Mathf.Abs(side) < 0.01f && Mathf.Abs(fore) < 0.01f)
        {
            airSteerArmed = true;
            return Vector3.zero;
        }
        if (!airSteerArmed) return Vector3.zero;

        Vector3 fwd = travelVelocity;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) return Vector3.zero;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        return right * (side * airSteerSideAccel) + fwd * (fore * airSteerForwardAccel);
    }

    public void ApplyAirTumbleTorque()
    {
        if (rb != null && !rb.isKinematic)
        {
            rb.AddTorque(Random.insideUnitSphere * airTumbleTorque, ForceMode.Impulse);
        }
    }

    public void BeginFlight()
    {
        isInFlight = true;
        PlayCastFly();
    }

    // Corkscrew the bobber through the air. We integrate a ballistic "guide" position with the same
    // total gravity the free bobber would feel (so the centreline arc — and thus the landing spot —
    // is unchanged), then steer the rigidbody to guide + a rotating offset perpendicular to the
    // flight line. Steering by velocity toward the target from the ACTUAL position each step keeps
    // it self-correcting (Unity's own useGravity add-on gets absorbed) and still swept, so the water
    // trigger fires normally. The Verlet rope tracks the bobber's attach point, so it trails the
    // helix for free and straightens on landing via its existing landed-tighten path.
    private void ApplyCastSpiral()
    {
        float dt = Time.fixedDeltaTime;

        if (!spiralInitialized)
        {
            spiralGuidePos = rb.position;
            // Use the velocity FishingLine handed us — NOT rb.linearVelocity, which the launch
            // impulse hasn't populated yet on this first FixedUpdate (see field comment).
            spiralGuideVel = pendingLaunchVelocity.sqrMagnitude > 0.0001f ? pendingLaunchVelocity : rb.linearVelocity;
            spiralFwd = spiralGuideVel.sqrMagnitude > 0.0001f ? spiralGuideVel.normalized : transform.forward;
            spiralAxisA = Vector3.Cross(spiralFwd, Vector3.up);
            if (spiralAxisA.sqrMagnitude < 0.0001f) spiralAxisA = Vector3.Cross(spiralFwd, Vector3.right);
            spiralAxisA.Normalize();
            spiralAxisB = Vector3.Cross(spiralFwd, spiralAxisA).normalized;
            spiralTime = 0f;
            spiralInitialized = true;
        }

        Vector3 gTotal = Physics.gravity + Vector3.down * extraGravity;
        spiralGuideVel += gTotal * dt;
        // Mid-air steering bends the ballistic centreline itself, so the corkscrew and the Verlet
        // rope ride along with the curved arc for free.
        spiralGuideVel += ReadAirSteerAccel(spiralGuideVel) * dt;
        spiralGuidePos += spiralGuideVel * dt;
        spiralTime += dt;

        float ramp = spiralRampInTime > 0f ? Mathf.Clamp01(spiralTime / spiralRampInTime) : 1f;
        float chargeScale = Mathf.Lerp(1f, pendingCastCharge, spiralChargeInfluence);
        float amp = spiralRadius * chargeScale * ramp * Mathf.Exp(-spiralDecayRate * spiralTime);
        float theta = spiralTime * spiralFrequency * Mathf.PI * 2f;
        Vector3 offset = amp * (Mathf.Cos(theta) * spiralAxisA + Mathf.Sin(theta) * spiralAxisB);

        Vector3 target = spiralGuidePos + offset;
        rb.linearVelocity = (target - rb.position) / dt;

        // Hold a trailing flight pose: line-attach point back toward the player, feet leading, so the
        // bobber drags the line after it instead of tumbling randomly.
        OrientTrailingAlong(-spiralFwd, dt);
    }

    // Rotate the bobber so its line-attach axis points along attachWorldDir (during a cast this is the
    // direction back toward the player), with a roll lock so a consistent side stays up rather than
    // spinning free. Same fixed-geometry trick as DangleRestRotation, aimed along flight instead of up.
    private void OrientTrailingAlong(Vector3 attachWorldDir, float dt)
    {
        if (rb == null || spiralOrientSpeed <= 0f) return;

        Transform attach = LineAttachPoint;
        if (attach == null || attach == transform) return;
        if (attachWorldDir.sqrMagnitude < 0.0001f) return;
        attachWorldDir.Normalize();

        // Local (rotation-invariant) direction from the bobber origin to its line-attach point.
        Vector3 localAttachDir = transform.InverseTransformPoint(attach.position);
        if (localAttachDir.sqrMagnitude < 0.0001f) return;
        localAttachDir.Normalize();

        // 1) Aim the attach axis backward along the flight line.
        Quaternion rot = Quaternion.FromToRotation(localAttachDir, attachWorldDir);

        // 2) Lock the free spin about that axis so a fixed side faces up.
        Vector3 localSide = Vector3.ProjectOnPlane(Vector3.up, localAttachDir);
        if (localSide.sqrMagnitude < 0.0001f) localSide = Vector3.ProjectOnPlane(Vector3.forward, localAttachDir);
        if (localSide.sqrMagnitude > 0.0001f)
        {
            Vector3 sideNow = Vector3.ProjectOnPlane(rot * localSide.normalized, attachWorldDir);
            Vector3 upFlat = Vector3.ProjectOnPlane(Vector3.up, attachWorldDir);
            if (sideNow.sqrMagnitude > 0.0001f && upFlat.sqrMagnitude > 0.0001f)
                rot = Quaternion.FromToRotation(sideNow.normalized, upFlat.normalized) * rot;
        }

        Quaternion newRotation = Quaternion.Slerp(rb.rotation, rot, spiralOrientSpeed * dt);
        rb.MoveRotation(newRotation);
    }

}
