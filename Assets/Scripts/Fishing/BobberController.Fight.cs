using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Part of BobberController (partial class). Serialized fields live in BobberController.cs.
public partial class BobberController
{
    private void UpdateStruggleMovement()
    {
        // Compute perpendicular axis to the player→bobber line
        Vector3 toBobber = transform.position - (playerTransform != null ? playerTransform.position : transform.position - Vector3.forward);
        toBobber.y = 0f;
        if (toBobber.sqrMagnitude < 0.01f) toBobber = Vector3.forward;
        toBobber.Normalize();

        Vector3 perpendicular = Vector3.Cross(toBobber, Vector3.up).normalized;

        struggleTimer -= Time.fixedDeltaTime;
        if (struggleTimer <= 0f)
        {
            int inwardSide = ChooseInwardStruggleSide(perpendicular);
            if (inwardSide != 0)
            {
                currentStruggleSide = inwardSide;
            }
            else if (Random.value > repeatSideChance)
            {
                currentStruggleSide = -currentStruggleSide;
            }
            StartNewStruggleBurst(perpendicular);
            TryFightJump(struggleDirection);
        }

        // Check for obstacles ahead — flip side and pick a fresh burst if blocked
        if (obstacleCheckLayers != 0)
        {
            Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(rayOrigin, struggleDirection, obstacleCheckDistance, obstacleCheckLayers))
            {
                currentStruggleSide = -currentStruggleSide;
                StartNewStruggleBurst(perpendicular);
            }
        }

        if (rb != null && !rb.isKinematic)
        {
            rb.AddForce(struggleDirection * struggleForce * currentStruggleForceMultiplier, ForceMode.Acceleration);

            // The fish takes line: a steady pull straight away from the player, with the
            // tether anchor dragged along so the gained distance sticks instead of
            // springing back to the bite point. The pull is capped by how far the anchor
            // has progressed away from the original bite spot (measured along the current
            // away direction, so line the player reels back can be taken again — a real
            // tug-of-war), and stops against obstacles and the fishing line's own tether.
            float pulledAway = Vector3.Dot(struggleAnchor - initialStruggleAnchor, toBobber);
            if (strugglePullAwayForce > 0f && pulledAway < maxPullAwayDistance)
            {
                rb.AddForce(toBobber * strugglePullAwayForce, ForceMode.Acceleration);

                if (hasStruggleAnchor && pullAwayAnchorSpeed > 0f)
                {
                    bool blocked = obstacleCheckLayers != 0
                        && Physics.Raycast(transform.position + Vector3.up * 0.5f, toBobber,
                                           obstacleCheckDistance, obstacleCheckLayers);
                    if (!blocked)
                        struggleAnchor += toBobber * (pullAwayAnchorSpeed * Time.fixedDeltaTime);
                }
            }

            ApplyTetherPullBack();
        }
    }

    private int ChooseInwardStruggleSide(Vector3 perpendicular)
    {
        if (!hasStruggleAnchor || tetherRadius <= 0f) return 0;

        Vector3 offset = transform.position - struggleAnchor;
        offset.y = 0f;
        float dist = offset.magnitude;
        if (dist < tetherRadius * tetherInwardBiasThreshold) return 0;

        Vector3 toAnchor = -offset / dist;
        return Vector3.Dot(perpendicular, toAnchor) >= 0f ? 1 : -1;
    }

    private void ApplyTetherPullBack()
    {
        if (!hasStruggleAnchor || tetherRadius <= 0f || tetherReturnForce <= 0f) return;

        Vector3 offset = transform.position - struggleAnchor;
        offset.y = 0f;
        float dist = offset.magnitude;
        if (dist <= tetherRadius) return;

        Vector3 returnDir = -offset / dist;
        float overshoot = dist - tetherRadius;
        rb.AddForce(returnDir * tetherReturnForce * overshoot, ForceMode.Acceleration);
    }

    // Rolled once per fight burst: occasionally the hooked fish leaps clean out of the water,
    // arcing along horizontalDir (the direction it is currently swimming). Only the heavy
    // in-water damping comes off so the launch velocity carries up cleanly — buoyancy is
    // deliberately left targeting the bite depth (NOT the surface), so it adds no upward boost
    // and the breach height is governed solely by jumpUpSpeed. (Aiming buoyancy at the surface
    // here was what sent the fish way too high.) The trigger exit/enter pair handles the splash,
    // and OnHookedFishJumpChanged lets the rope go slack mid-air. Public so the fish-control
    // fight (FishFightHandler) can trigger it at the top of a burst; the roll/cooldown and all
    // the leap tuning stay on this component.
    public void TryFightJump(Vector3 horizontalDir)
    {
        if (jumpActive || fightJumpChance <= 0f || hookedFish == null) return;
        if (rb == null || rb.isKinematic) return;
        if (Time.time < nextJumpAllowedTime) return;
        if (Random.value > fightJumpChance) return;

        jumpActive = true;
        jumpStartTime = Time.time;
        nextJumpAllowedTime = Time.time + jumpCooldown;

        rb.linearDamping = initialLinearDamping;

        horizontalDir.y = 0f;
        if (horizontalDir.sqrMagnitude > 0.0001f) horizontalDir.Normalize();
        else horizontalDir = Vector3.zero;
        Vector3 horizontal = horizontalDir * jumpForwardSpeed;
        // Whole launch scaled together so the arc shape is preserved; the matching extra gravity in
        // FixedUpdate then replays that same arc faster (see jumpSpeedMultiplier).
        rb.linearVelocity = new Vector3(horizontal.x, jumpUpSpeed, horizontal.z) * jumpSpeedMultiplier;

        FishingEvents.OnHookedFishJumpChanged?.Invoke(true);
    }

    private void EndFightJump()
    {
        if (!jumpActive) return;
        jumpActive = false;
        FishingEvents.OnHookedFishJumpChanged?.Invoke(false);
    }

    private void StartNewStruggleBurst(Vector3 perpendicular)
    {
        float angleOffset = Random.Range(-maxAngleOffsetDegrees, maxAngleOffsetDegrees);
        Vector3 baseDir = perpendicular * currentStruggleSide;
        struggleDirection = Quaternion.AngleAxis(angleOffset, Vector3.up) * baseDir;

        currentStruggleForceMultiplier = Random.Range(forceMultiplierRange.x, forceMultiplierRange.y);
        struggleTimer = Random.Range(struggleHoldRange.x, struggleHoldRange.y);
    }

}
