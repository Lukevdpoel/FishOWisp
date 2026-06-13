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
}
