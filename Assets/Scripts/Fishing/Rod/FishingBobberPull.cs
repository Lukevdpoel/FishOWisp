using UnityEngine;

// The catch-range check used to gate fish capture, and the helper that locates the waterline
// point the catch measures to (the lure retract uses it too).
//
// The reel target is the WATERLINE between the player and the bobber — where the straight line
// from the player out to the bobber first crosses onto the water — NOT the player. So a player
// standing back from the bank only needs the fish brought to the water's edge in front of
// them, instead of all the way to their feet (which it can never reach: the fish beaches at
// the shore). When the player is at/over the water, that target collapses to the player. Pure
// helper — caller (FishingRodController) computes the target once per frame and hands it in.
public static class FishingBobberPull
{
    // The water surface is a non-convex MeshCollider (WaterColliderFish layer), so ClosestPoint
    // is unusable. We probe it with vertical raycasts instead. Start well above any plausible
    // bank height and shoot down far enough to pass through the surface.
    private const float WaterProbeHeight = 50f;
    private const float WaterProbeDistance = 100f;

    public static bool IsWithinCatchRange(BobberController activeBobber, Vector3 target, float catchDistance)
    {
        if (activeBobber == null) return false;
        Vector3 delta = activeBobber.transform.position - target;
        delta.y = 0f;
        return delta.sqrMagnitude <= catchDistance * catchDistance;
    }

    // Finds the point on the near shoreline between the player and the bobber — i.e. where the
    // straight line from the player out to the bobber first crosses onto the water. That's the
    // spot the catch should reel the fish to. Probes the water-surface MeshCollider (waterMask)
    // with downward raycasts and binary-searches the land→water boundary along the segment.
    //
    // Falls back to the player position when: the mask is unset, the player is already over
    // water (dock / shallows), or the bobber end can't be confirmed over water (mesh gap, fish
    // mid-jump) — all of which reduce to the old measure-straight-to-the-player behavior.
    public static Vector3 NearestWaterlinePoint(Vector3 playerPos, Vector3 bobberPos, LayerMask waterMask)
    {
        if (waterMask.value == 0) return playerPos;

        // Player already standing over water → the waterline is at (or behind) them; reel to them.
        if (IsOverWater(playerPos, waterMask)) return playerPos;

        Vector3 dry = playerPos;   // known land
        Vector3 wet = bobberPos;   // expected over water
        if (!IsOverWater(wet, waterMask)) return playerPos;

        // 8 iterations ≈ segment/256 precision — plenty for a catch radius measured in meters.
        for (int i = 0; i < 8; i++)
        {
            Vector3 mid = (dry + wet) * 0.5f;
            if (IsOverWater(mid, waterMask)) wet = mid; else dry = mid;
        }
        return wet; // first point on the water side of the boundary
    }

    // True when a downward probe from above worldPos hits the water-surface collider — i.e. the
    // point lies over water, not over the bank. Public: the fish-fight drive uses it to refuse
    // to swim the hooked fish out of the water.
    public static bool IsOverWater(Vector3 worldPos, LayerMask waterMask)
    {
        Vector3 from = new Vector3(worldPos.x, worldPos.y + WaterProbeHeight, worldPos.z);
        return Physics.Raycast(from, Vector3.down, WaterProbeDistance, waterMask, QueryTriggerInteraction.Collide);
    }
}
