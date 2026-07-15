using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Part of FishRipple (partial class). Serialized fields live in FishRipple.cs.
public partial class FishRipple
{
    // Fish swim head-first: the HEAD is the steered agent — it turns toward the target at a
    // capped rate and only ever advances along its own heading — and the body centre is
    // placed a fixed distance behind it, trailer-style. Steering the head rather than the
    // centre matters: pivoting around the head swings the centre sideways, so steering by
    // the centre's bearing feeds that swing back into the next turn, which reads as jitter
    // and endless orbiting. The head can't move sideways, so the loop can't form.
    //
    // The final centre position is validated against bounds/obstacles BEFORE anything is
    // applied — a turn can never push the body into a rock. Rotation stays yaw-only and the
    // result is pinned to the water surface: strictly horizontal swimming.
    private void SteerToward(Vector3 target, float speed, float turnDegPerSec)
    {
        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        float headOffset = modelVisual != null ? modelVisual.HeadForwardOffset : 0f;
        Vector3 head = transform.position + flatForward * headOffset;

        // Pre-emptive avoidance: look ahead of the head, and if that water is blocked,
        // steer for the open side BEFORE contact — the fish arcs away from shores and rocks
        // instead of stalling nose-first against them.
        float lookahead = Mathf.Max(speed * 0.75f, 0.3f);
        if (FishMovementHelpers.IsObstacleAt(head + flatForward * lookahead, obstacleLayers, waterSurfaceY, obstacleRayHeight))
        {
            Vector3 leftDir = Quaternion.Euler(0f, -50f, 0f) * flatForward;
            Vector3 rightDir = Quaternion.Euler(0f, 50f, 0f) * flatForward;
            bool leftFree = !FishMovementHelpers.IsObstacleAt(head + leftDir * lookahead, obstacleLayers, waterSurfaceY, obstacleRayHeight);
            bool rightFree = !FishMovementHelpers.IsObstacleAt(head + rightDir * lookahead, obstacleLayers, waterSurfaceY, obstacleRayHeight);

            Vector3 toGoal = target - head;
            toGoal.y = 0f;
            Vector3 avoidDir;
            if (leftFree && rightFree)
                avoidDir = Vector3.SignedAngle(flatForward, toGoal, Vector3.up) < 0f ? leftDir : rightDir;
            else if (leftFree) avoidDir = leftDir;
            else if (rightFree) avoidDir = rightDir;
            else avoidDir = -flatForward; // pocket dead ahead: turn around

            target = head + avoidDir * 2f;
            turnDegPerSec *= 2f;
        }

        float deltaYaw = 0f;
        Vector3 toTarget = target - head;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.0001f)
        {
            // A target closer than the turning diameter can never be reached at this turn
            // rate — the fish orbits it forever at a steady ~90° bearing. Real fish simply
            // pivot harder when the goal is right beside them: raise the effective turn
            // rate until the turning circle fits the distance (capped so it can't snap).
            float turnRadius = speed / Mathf.Max(turnDegPerSec * Mathf.Deg2Rad, 0.01f);
            float targetDist = toTarget.magnitude;
            float signedAngle = Vector3.SignedAngle(flatForward, toTarget, Vector3.up);
            if (targetDist < 2f * turnRadius)
            {
                turnDegPerSec *= Mathf.Min(2f * turnRadius / Mathf.Max(targetDist, 0.05f), 8f);

                // Even pivoting 8x harder can leave a VERY close, off-axis target inside the
                // turning circle — the fish then carves an endless tail-chasing orbit around it
                // (steering offsets like separation/schooling can hold such a point right beside
                // the fish indefinitely). Real fish brake into tight turns: the turning radius is
                // proportional to speed, so slow down until the circle fits and every target
                // becomes reachable. Only fires for genuinely off-axis targets — a close point
                // dead ahead needs no turn, so the approach isn't slowed.
                float boostedRadius = speed / Mathf.Max(turnDegPerSec * Mathf.Deg2Rad, 0.01f);
                if (targetDist < 2f * boostedRadius && Mathf.Abs(signedAngle) > 30f)
                    speed *= Mathf.Max(targetDist / (2f * boostedRadius), 0.15f);
            }
            float maxStep = turnDegPerSec * Time.deltaTime;
            deltaYaw = Mathf.Clamp(signedAngle, -maxStep, maxStep);
        }

        // Rebuild a level, yaw-only rotation every frame (never rotate incrementally): the
        // host can then never accumulate pitch or roll, no matter what orientation the
        // prefab spawned with.
        float yawDeg = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
        Quaternion newRotation = Quaternion.Euler(0f, yawDeg + deltaYaw, 0f);
        Vector3 newForward = newRotation * Vector3.forward;

        Vector3 newHead = head + newForward * (speed * Time.deltaTime);
        Vector3 newPos = newHead - newForward * headOffset;
        newPos = FishMovementHelpers.ClampToBounds(newPos, zoneBounds);
        newPos.y = waterSurfaceY;

        // Check the centre AND the head — the head pokes half a body ahead, and it's the
        // part that ends up visibly buried in rocks if only the centre is validated.
        Vector3 newHeadPos = newPos + newForward * headOffset;
        if (!FishMovementHelpers.IsObstacleAt(newPos, obstacleLayers, waterSurfaceY, obstacleRayHeight)
            && !FishMovementHelpers.IsObstacleAt(newHeadPos, obstacleLayers, waterSurfaceY, obstacleRayHeight))
        {
            transform.rotation = newRotation;
            transform.position = newPos;
            return;
        }

        // Blocked this frame: keep turning, and keep MOVING — slide along the nearest open
        // direction at half speed rather than freezing. Only a fish boxed in on every side
        // stays put, and the stuck rescue eventually relocates those.
        transform.rotation = newRotation;
        if (currentState == FishState.Wandering) hasWanderTarget = false;

        float slideDistance = speed * 0.5f * Time.deltaTime;
        float preferredSide = deltaYaw >= 0f ? 1f : -1f;
        for (int step = 1; step <= 4; step++)
        {
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? preferredSide : -preferredSide;
                Vector3 slideDir = Quaternion.Euler(0f, 45f * step * sign, 0f) * newForward;
                Vector3 slidePos = transform.position + slideDir * slideDistance;
                slidePos = FishMovementHelpers.ClampToBounds(slidePos, zoneBounds);
                slidePos.y = waterSurfaceY;
                Vector3 slideHead = slidePos + newForward * headOffset;
                if (!FishMovementHelpers.IsObstacleAt(slidePos, obstacleLayers, waterSurfaceY, obstacleRayHeight)
                    && !FishMovementHelpers.IsObstacleAt(slideHead, obstacleLayers, waterSurfaceY, obstacleRayHeight))
                {
                    transform.position = slidePos;
                    return;
                }
            }
        }
    }

    private void PickNewWanderTarget()
    {
        wanderTarget = FishMovementHelpers.GetRandomPointInBounds(zoneBounds, transform.position, waterSurfaceY, obstacleLayers, obstacleRayHeight);
        wanderTarget.y = waterSurfaceY;
        BeginWanderLeg();
    }

    // Every fresh wander leg (random roll or a hand-authored target) re-arms the no-progress
    // watchdog alongside setting the target.
    private void BeginWanderLeg()
    {
        hasWanderTarget = true;
        wanderBestDist = float.MaxValue;
        wanderNoProgressTimer = 0f;
    }

    private Vector3 GetFlatForward()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
    }

    // Personal-space steering: a push away from schoolmates within separationRadius,
    // stronger the closer they are. Applied in every free-swimming state (wander, attracted
    // hover, flee, hunt) — only committed bite dashes (Nibbling/Striking/Grabbing) skip it so
    // contact ranges still land. `ignore` exempts one fish from the scan: a hunting predator
    // must never be pushed off its own prey. O(n²) over a zone's handful of fish is negligible.
    private Vector3 ComputeSeparation(FishRipple ignore = null)
    {
        if (school == null || separationStrength <= 0f) return Vector3.zero;

        Vector3 push = Vector3.zero;
        for (int i = 0; i < school.Count; i++)
        {
            FishRipple other = school[i];
            if (other == null || other == this || other == ignore) continue;
            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;
            float dist = away.magnitude;
            if (dist >= separationRadius || dist < 0.0001f) continue;
            push += away / dist * (1f - dist / separationRadius);
        }
        return push;
    }

    // Same-species shoaling: boids cohesion + alignment, layered on top of the all-fish
    // separation above. Only fires for species that opt in (preset.Schools) and only toward
    // calm, wandering fish of the SAME species within schoolPerceptionRadius — so a lone fish of
    // a schooling species, or one surrounded only by other species, just wanders. Returns a
    // world-space steering offset folded into the wander target, same convention as separation.
    private Vector3 ComputeSchooling()
    {
        if (school == null || preset == null || !preset.Schools) return Vector3.zero;
        if (schoolCohesionStrength <= 0f && schoolAlignmentStrength <= 0f) return Vector3.zero;

        float perceptionSqr = schoolPerceptionRadius * schoolPerceptionRadius;
        Vector3 centerSum = Vector3.zero;
        Vector3 headingSum = Vector3.zero;
        int neighbors = 0;

        for (int i = 0; i < school.Count; i++)
        {
            FishRipple other = school[i];
            if (other == null || other == this) continue;
            if (other.preset != preset) continue;                    // same species only
            if (other.currentState != FishState.Wandering) continue; // school with calm fish only

            Vector3 diff = other.transform.position - transform.position;
            diff.y = 0f;
            if (diff.sqrMagnitude > perceptionSqr) continue;

            centerSum += other.transform.position;
            headingSum += other.GetFlatForward();
            neighbors++;
        }

        if (neighbors == 0) return Vector3.zero;

        Vector3 steer = Vector3.zero;

        // Cohesion: steer toward the shoal's average position, PROPORTIONAL to how far this fish
        // has strayed (capped at perception). A fixed nudge is too weak to overcome the random
        // wander target, so a strayed fish would never rejoin — the distance scaling makes a
        // far fish commit hard to returning while one already in the shoal just mingles.
        if (schoolCohesionStrength > 0f)
        {
            Vector3 toCenter = (centerSum / neighbors) - transform.position;
            toCenter.y = 0f;
            float dist = toCenter.magnitude;
            if (dist > 0.0001f)
                steer += toCenter / dist * Mathf.Min(dist, schoolPerceptionRadius) * schoolCohesionStrength;
        }

        // Alignment: nudge toward the shoal's shared heading.
        if (schoolAlignmentStrength > 0f)
        {
            Vector3 avgHeading = headingSum / neighbors;
            avgHeading.y = 0f;
            if (avgHeading.sqrMagnitude > 0.0001f)
                steer += avgHeading.normalized * schoolAlignmentStrength;
        }

        return steer;
    }

    // The steered head point — where the fish is actually going. Movement targets should be
    // judged from here, not the transform centre, which trails half a body length behind.
    private Vector3 GetHeadPosition()
    {
        float headOffset = modelVisual != null ? modelVisual.HeadForwardOffset : 0f;
        return transform.position + GetFlatForward() * headOffset;
    }

    // Local wrappers so the state-machine bodies (UpdateWandering, UpdateScared) read the same
    // as before. They forward to FishMovementHelpers but capture this fish's bounds and surface.
    private Vector3 ClampToBounds(Vector3 pos) => FishMovementHelpers.ClampToBounds(pos, zoneBounds);
    private bool IsObstacleAt(Vector3 pos) => FishMovementHelpers.IsObstacleAt(pos, obstacleLayers, waterSurfaceY, obstacleRayHeight);
    private float GetHorizontalDistance(Vector3 a, Vector3 b) => FishMovementHelpers.GetHorizontalDistance(a, b);

}
