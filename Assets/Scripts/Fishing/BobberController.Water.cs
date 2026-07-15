using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Part of BobberController (partial class). Serialized fields live in BobberController.cs.
public partial class BobberController
{
    // ------------------------------------------------------------------------
    // WATER ENTRY LOGIC
    // ------------------------------------------------------------------------
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(waterTag))
        {
            waterVolumes.Add(other);
            waterSurfaceY = HighestWaterSurfaceY();
            if (!isInWater)
            {
                EnterWater();
            }
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag(waterTag) && isInWater)
        {
            // Adding here too heals any Enter event missed while crossing a zone seam.
            waterVolumes.Add(other);
            waterSurfaceY = HighestWaterSurfaceY();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(waterTag)) return;

        waterVolumes.Remove(other);
        if (!isInWater) return;

        // Enter/Exit ordering at the seam between two overlapping water volumes is not
        // guaranteed, so when the bookkeeping says we left the last volume, physically probe
        // before sinking — the bobber may still be inside a neighboring volume.
        waterVolumes.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
        if (waterVolumes.Count == 0) ProbeForWaterVolumes();
        if (waterVolumes.Count > 0)
        {
            waterSurfaceY = HighestWaterSurfaceY();
            return;
        }

        isInWater = false;
        isSubmerged = false;
        SetStruggleActive(false);
        // Keep the tether anchor while a fish is on the line: a fight jump exits the water for
        // a moment, and losing the anchor here would permanently disable the tether/pull-away
        // tug-of-war for the rest of the fight. (The fight loop re-enables struggling each
        // frame; the anchor is the only state that wouldn't come back on its own.)
        if (hookedFish == null) hasStruggleAnchor = false;

        // UNITY 6 FIX: Reset Damping
        if (rb != null)
        {
            rb.linearDamping = initialLinearDamping;
        }

        // Destroy Wake when leaving water
        if (activeWakeInstance != null)
        {
            Destroy(activeWakeInstance);
            activeWakeInstance = null;
        }
    }

    private float HighestWaterSurfaceY()
    {
        // Overlapping volumes can sit at slightly different heights; the bobber floats on the
        // highest surface it is currently touching.
        float top = float.MinValue;
        foreach (Collider c in waterVolumes)
        {
            if (c == null) continue;
            top = Mathf.Max(top, c.bounds.max.y);
        }
        return top > float.MinValue ? top : waterSurfaceY;
    }

    private void ProbeForWaterVolumes()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, 0.1f, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].CompareTag(waterTag))
                waterVolumes.Add(hits[i]);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!isInWater && !hasPlayedImpactSound)
        {
            if (!collision.gameObject.CompareTag(waterTag))
            {
                if (audioSource != null && impactSound != null)
                {
                    audioSource.PlayOneShot(impactSound);
                }
                hasPlayedImpactSound = true;
            }
        }
    }

    private void EnterWater()
    {
        // The fly-cast whir ends the instant the bobber touches water, regardless of what happens next.
        StopCastFly();

        // solve in cleaner issue should not be allowed to enter water while reeling after canceling fishing.
        if (FindFirstObjectByType<FishingRodController>().currentState == FishingRodController.FishingState.Reeling)
            return;
        isInWater = true;
        isInFlight = false;

        // UNITY 6 FIX: Apply Water Drag
        if (rb != null)
        {
            rb.linearDamping = waterDrag;
            rb.angularDamping = waterAngularDamping;

            // Absorb the cast's plunge so the lightly-damped buoyancy spring doesn't catapult
            // the bobber back up. Only the downward component is killed; preserve any upward
            // motion (unlikely on entry but harmless) and horizontal glide.
            Vector3 v = rb.linearVelocity;
            if (v.y < 0f) v.y *= Mathf.Clamp01(waterImpactVelocityRetention);
            rb.linearVelocity = v;
            // Also dump residual spin from the air tumble so the bobber doesn't roll on landing.
            rb.angularVelocity *= 0.25f;

            // Lure-only: override the auto-computed inertia tensor so off-center pulls at the
            // LineAttachPoint produce a controlled rotation rather than spinning the lure wildly.
            if (waterInertiaTensorOverride > 0f)
            {
                rb.inertiaTensor = Vector3.one * waterInertiaTensorOverride;
            }
        }

        // 1. TRIGGER IMPACT SPLASH
        if (!hasSplashed)
        {
            SpawnEffect(impactPrefab, impactLifetime);
            hasSplashed = true;
        }

        // 2. SPAWN WAKE / TRAIL
        if (wakePrefab != null && activeWakeInstance == null)
        {
            Vector3 wakePos = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);
            activeWakeInstance = Instantiate(wakePrefab, wakePos, wakePrefab.transform.rotation);
        }

        if (hookedFish != null)
        {
            // Splash-down from a fight jump: dive back to the fight depth with its own sound
            // (no particle — the fight is effect-free), and do NOT re-announce the landing —
            // FishingZone's bite brain would start a second bite cycle on a bobber that
            // already has a fish on the line.
            isSubmerged = true;
            if (audioSource != null && waterEntrySound != null)
            {
                audioSource.PlayOneShot(waterEntrySound, waterEntryVolumeScale);
            }
            EndFightJump();
            return;
        }

        Debug.LogWarning("dobber landed in water calling event");

        FishingEvents.OnBobberLandedInWater?.Invoke(this);

        if (audioSource != null && waterEntrySound != null && !hasPlayedSplashSound)
        {
            audioSource.PlayOneShot(waterEntrySound, waterEntryVolumeScale);
            hasPlayedSplashSound = true;
        }
    }

    // ------------------------------------------------------------------------
    // PHYSICS & MOVEMENT
    // ------------------------------------------------------------------------
    private void ApplyBuoyancy()
    {
        // Bob offset added to the rest float height drives the visual bounce while a lure is
        // being nudged — buoyancy follows the moving target naturally.
        float restFloatHeight = floatHeight + (isSubmerged ? 0f : lureBobOffset);
        float effectiveFloatHeight = isSubmerged ? biteSubmergeDepth : restFloatHeight;
        float targetY = waterSurfaceY - effectiveFloatHeight;
        float depth = targetY - transform.position.y;

        if (depth > 0)
        {
            // UNITY 6 FIX: Use 'linearVelocity' instead of 'velocity'
            Vector3 force = Vector3.up * (depth * buoyancyForce - rb.linearVelocity.y * bounceDamp);
            rb.AddForce(force, ForceMode.Acceleration);
        }

        // Decaying nibble wobble: a quick oscillating tilt layered on top of the buoyancy rotation
        // so a fish brushing the bobber/lure visibly jiggles it. Advanced/decayed here (FixedUpdate)
        // where the rotation is actually written.
        float wobble = 0f;
        if (nibbleWobbleAmp > 0.05f)
        {
            nibbleWobblePhase += nibbleWobbleFrequency * Time.fixedDeltaTime * Mathf.PI * 2f;
            wobble = nibbleWobbleAmp * Mathf.Sin(nibbleWobblePhase);
            nibbleWobbleAmp *= Mathf.Exp(-nibbleWobbleDecay * Time.fixedDeltaTime);
        }

        // Pitch X is driven by lureVisualPitchDeg (0 = level, positive = LineAttachPoint side
        // rises). Heading Y is preserved; roll Z is held at 0.
        if (isPopperLure && !isSubmerged)
        {
            // Popper: smooth the slow lean ourselves (kept clean of the bounce), then add the fast
            // nose chatter on top and apply it directly — a slerp at waterRotationSpeed would damp
            // the high-frequency pop into nothing. Heading/roll handled exactly as the slerp path.
            popperLeanPitch = Mathf.Lerp(popperLeanPitch, lureVisualPitchDeg,
                                         1f - Mathf.Exp(-waterRotationSpeed * Time.fixedDeltaTime));
            Quaternion popRotation =
                Quaternion.Euler(popperLeanPitch + popperBounceDeg + wobble, rb.rotation.eulerAngles.y, wobble * 0.5f);
            rb.MoveRotation(popRotation);
        }
        else
        {
            Quaternion targetRotation = Quaternion.Euler(lureVisualPitchDeg, rb.rotation.eulerAngles.y, 0);
            Quaternion newRotation = Quaternion.Slerp(rb.rotation, targetRotation, waterRotationSpeed * Time.fixedDeltaTime);
            // Layer the wobble on AFTER the slerp (in the bobber's local space) so the crisp jiggle
            // isn't smoothed away by the buoyancy slerp toward the upright target.
            if (Mathf.Abs(wobble) > 0.01f)
                newRotation *= Quaternion.Euler(wobble, 0f, wobble * 0.5f);
            rb.MoveRotation(newRotation);
        }
    }

}
