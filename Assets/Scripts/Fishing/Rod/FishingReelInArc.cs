using System.Collections;
using UnityEngine;

// Quadratic-bezier reel-in animation: arcs the bobber from its current position to a target,
// with a peak height above the higher of the two endpoints. The end target is sampled each
// frame so it can move (e.g. attached to the player's hold point), while the arc's start
// and peak are captured once when the coroutine starts — preserves the original behavior
// (a stable arc shape that follows a moving target).
public static class FishingReelInArc
{
    public static IEnumerator Animate(Transform bobber, Transform target, float duration, float arcHeight)
    {
        if (bobber == null || target == null) yield break;

        Vector3 startPos = bobber.position;
        Vector3 targetSnapshotForArc = target.position;
        Vector3 controlPoint = (startPos + targetSnapshotForArc) * 0.5f;
        float highestY = Mathf.Max(startPos.y, targetSnapshotForArc.y);
        controlPoint.y = highestY + arcHeight;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (bobber == null) yield break;

            float t = elapsed / duration;
            float easedT = t * t * (3f - 2f * t);
            float oneMinusEasedT = 1f - easedT;

            // Resample target each frame so the bobber tracks a moving hold point.
            Vector3 endPos = target != null ? target.position : targetSnapshotForArc;

            Vector3 position = (oneMinusEasedT * oneMinusEasedT * startPos)
                             + (2f * oneMinusEasedT * easedT * controlPoint)
                             + (easedT * easedT * endPos);
            bobber.position = position;

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // Pose-aware variant: arcs the bobber to a target POSITION while also slerping its rotation
    // toward a target ROTATION over the arc, so it arrives at an exact resting pose (used for the
    // empty-line reel-in — the bobber lands precisely where it dangles, in its upright rest
    // rotation, so the hand-off to the dangle physics is seamless with no teleport/snap). Both
    // providers are resampled each frame so the pose tracks a moving rod tip. A null rotation
    // provider leaves rotation untouched. Lands exactly on the final pose on completion.
    //
    // Cubic bezier, not the quadratic above: both callers target a point hanging BELOW the rod
    // tip, and the reel pose can put that point at or behind the player — a quadratic's
    // horizontal path is the straight start→end line, which dragged the caught fish clean
    // through the character. The cubic's second control point sits directly above the (moving)
    // end point, so the final approach is always a vertical drop down the line's own clear
    // column under the rod tip, never a horizontal swing through the body.
    public static IEnumerator Animate(Transform bobber, System.Func<Vector3> targetProvider,
                                      System.Func<Quaternion> rotationProvider, float duration, float arcHeight)
    {
        if (bobber == null || targetProvider == null) yield break;

        Vector3 startPos = bobber.position;
        Quaternion startRot = bobber.rotation;
        Vector3 targetSnapshotForArc = targetProvider();
        float apexY = Mathf.Max(startPos.y, targetSnapshotForArc.y) + arcHeight;
        // First control point: over the path's first half, so the launch out of the water keeps
        // the same swooping arc shape as before.
        Vector3 control1 = (startPos + targetSnapshotForArc) * 0.5f;
        control1.y = apexY;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (bobber == null) yield break;

            float t = elapsed / duration;
            float easedT = t * t * (3f - 2f * t);
            float oneMinusEasedT = 1f - easedT;

            Vector3 endPos = targetProvider();
            // Second control point: straight above the live end point → vertical arrival.
            Vector3 control2 = new Vector3(endPos.x, apexY, endPos.z);
            Vector3 position = (oneMinusEasedT * oneMinusEasedT * oneMinusEasedT * startPos)
                             + (3f * oneMinusEasedT * oneMinusEasedT * easedT * control1)
                             + (3f * oneMinusEasedT * easedT * easedT * control2)
                             + (easedT * easedT * easedT * endPos);
            bobber.position = position;
            if (rotationProvider != null)
                bobber.rotation = Quaternion.Slerp(startRot, rotationProvider(), easedT);

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Land exactly on the final pose so the dangle hand-off is a no-op (no visible snap).
        if (bobber != null)
        {
            bobber.position = targetProvider();
            if (rotationProvider != null) bobber.rotation = rotationProvider();
        }
    }
}
