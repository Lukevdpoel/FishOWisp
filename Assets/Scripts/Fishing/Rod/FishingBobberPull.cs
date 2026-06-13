using UnityEngine;

// Physically tugs an in-water bobber horizontally toward the player while reeling, with a
// stop margin so the bobber lands well inside the catch radius. Also exposes the catch-range
// check used to gate fish capture. Pure helper — caller (FishingRodController) handles state,
// this just operates on the bobber + player references it's handed.
public static class FishingBobberPull
{
    // Returns true if the bobber moved this frame.
    public static bool PullToward(BobberController activeBobber,
                                  Transform playerModel,
                                  float reelPullSpeed,
                                  float catchDistance,
                                  float pullStopInset)
    {
        if (activeBobber == null || playerModel == null) return false;
        if (!activeBobber.IsInWater) return false;

        Rigidbody bobberRb = activeBobber.GetComponent<Rigidbody>();
        if (bobberRb == null || bobberRb.isKinematic) return false;

        Vector3 bobberPos = bobberRb.position;
        Vector3 toPlayer = playerModel.position - bobberPos;
        toPlayer.y = 0f;

        float horizontalDist = toPlayer.magnitude;
        // Pull until the bobber is well INSIDE the catch radius, so IsWithinCatchRange()
        // reliably passes once the reel meter fills. Stopping outside the boundary would
        // strand the bobber in a dead zone and the catch would never trigger.
        float pullStopDistance = Mathf.Max(0.05f, catchDistance - pullStopInset);
        if (horizontalDist <= pullStopDistance) return false;
        if (horizontalDist < 0.001f) return false;

        Vector3 step = (toPlayer / horizontalDist) * Mathf.Min(reelPullSpeed * Time.deltaTime, horizontalDist - pullStopDistance);
        Vector3 next = new Vector3(bobberPos.x + step.x, bobberPos.y, bobberPos.z + step.z);
        bobberRb.MovePosition(next);

        // Drag the struggle tether anchor along with the bobber so it doesn't keep yanking the
        // bobber back toward the original hook spot as the player reels it in.
        activeBobber.ShiftStruggleAnchor(new Vector3(step.x, 0f, step.z));
        return true;
    }

    public static bool IsWithinCatchRange(BobberController activeBobber, Transform playerModel, float catchDistance)
    {
        if (activeBobber == null || playerModel == null) return false;
        Vector3 delta = activeBobber.transform.position - playerModel.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= catchDistance * catchDistance;
    }
}
