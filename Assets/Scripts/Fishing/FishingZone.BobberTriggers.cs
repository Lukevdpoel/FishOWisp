using UnityEngine;
using System.Collections.Generic;

// Part of FishingZone (partial class). Serialized fields live in FishingZone.cs.
public partial class FishingZone
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent<BobberController>(out var bobber))
        {
            Rigidbody bobberRb = bobber.GetComponent<Rigidbody>();
            if (bobberRb != null && bobberRb.isKinematic)
                return;

            // Adopt only — no splashdown here. A bobber that is already floating drifted in
            // across the seam from a neighboring zone, and the handoff must be seamless (no
            // scare, no fake lure-splash). A genuine landing is handled by HandleBobberLanded.
            AdoptBobber(bobber);
        }
    }

    private void AdoptBobber(BobberController bobber)
    {
        if (currentBobber == bobber) return;

        Debug.Log($"Bobber entered pool: {fishPool?.poolName}");
        currentBobber = bobber;
        ResetAutoNibbleTimer();

        // Wire the bobber onto every fish — the brain's direct-hit strike needs it.
        for (int i = 0; i < activeFish.Count; i++)
        {
            if (activeFish[i] != null)
                activeFish[i].SetBobberTransform(bobber.transform);
        }
    }

    private void HandleBobberLanded(BobberController bobber)
    {
        if (bobber == null) return;

        // Trigger order on landing isn't guaranteed — the water-enter event can fire before
        // this zone's own OnTriggerEnter. If we haven't adopted the bobber yet, do so now,
        // but only when it actually landed inside this zone.
        if (currentBobber != bobber)
        {
            Rigidbody bobberRb = bobber.GetComponent<Rigidbody>();
            if (bobberRb != null && bobberRb.isKinematic) return;
            if (!ContainsPoint(bobber.transform.position)) return;
            AdoptBobber(bobber);
        }

        Vector3 splashPos = bobber.transform.position;

        // Splashdown: counts as lure movement, and a lure landing directly on a fish
        // (within directHitRadius) makes that fish strike instantly.
        // (Landing near fish no longer scares them — a splash close by is a dinner bell, not a
        // threat. The only scares left are reset-with-interest and attract-abuse, per design.)
        if (BobberInventory.IsLureEquipped)
            lureBrain.OnSplashdown(in lureBrainSettings, activeFish, splashPos);
    }

    private bool ContainsPoint(Vector3 point)
    {
        return (zoneCollider.ClosestPoint(point) - point).sqrMagnitude < 0.01f;
    }

    private void OnTriggerStay(Collider other)
    {
        if (waterSurfaceMarker == null && other.CompareTag(waterTag))
        {
            waterSurfaceY = other.bounds.max.y;
            for (int i = 0; i < activeFish.Count; i++)
            {
                if (activeFish[i] != null)
                    activeFish[i].SetWaterSurfaceY(waterSurfaceY);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent<BobberController>(out var bobber) && bobber == currentBobber)
        {
            // A committed bite is being fought/reeled — the fight legitimately drags the bobber
            // out of the zone (FishingBobberPull). Clearing here would null bobberTransform and
            // lift the pond's lifetime/predator freeze mid-fight, so a wandering fish despawns or
            // gets hunted while the player is still fighting. Leave the freeze in place; the catch
            // resolves through HandleReelIn (OnStartReeling / OnCancelFishing), which clears cleanly.
            if (isCatchingFish) return;

            Debug.Log($"Bobber exited pool: {fishPool.poolName}");
            ClearBobber();
        }
    }

}
