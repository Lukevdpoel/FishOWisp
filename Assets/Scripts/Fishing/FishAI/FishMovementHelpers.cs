using UnityEngine;

// Pure-math movement utilities shared by FishRipple's state machine. No state of its own —
// every method takes the needed transform/bounds/layer as parameters. Kept static so callers
// don't need to allocate a per-fish instance.
public static class FishMovementHelpers
{
    public static float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    public static Vector3 ClampToBounds(Vector3 pos, Collider zoneBounds)
    {
        if (zoneBounds == null) return pos;
        Vector3 closest = zoneBounds.ClosestPoint(pos);
        closest.y = pos.y;
        return closest;
    }

    public static bool IsObstacleAt(Vector3 pos, LayerMask obstacleLayers, float waterSurfaceY, float obstacleRayHeight)
    {
        if (obstacleLayers == 0) return false;
        Vector3 rayOrigin = new Vector3(pos.x, waterSurfaceY + obstacleRayHeight, pos.z);
        return Physics.Raycast(rayOrigin, Vector3.down, obstacleRayHeight + 1f, obstacleLayers);
    }

    // True when the point AND a small ring around it are obstacle-free. A fish isn't a
    // point — its head and tail extend roughly half a body length from the centre, so spawn
    // and wander spots need room for the whole body or heads end up inside rocks.
    public static bool HasClearanceAt(Vector3 pos, float clearanceRadius,
                                      LayerMask obstacleLayers, float waterSurfaceY, float obstacleRayHeight)
    {
        if (IsObstacleAt(pos, obstacleLayers, waterSurfaceY, obstacleRayHeight)) return false;
        if (clearanceRadius <= 0f) return true;

        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 0.5f;
            Vector3 probe = pos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * clearanceRadius;
            if (IsObstacleAt(probe, obstacleLayers, waterSurfaceY, obstacleRayHeight)) return false;
        }
        return true;
    }

    public static Vector3 GetRandomPointInBounds(Collider zoneBounds, Vector3 fallbackPos,
                                                  float waterSurfaceY,
                                                  LayerMask obstacleLayers, float obstacleRayHeight,
                                                  float clearanceRadius = 0.6f)
    {
        if (zoneBounds == null) return fallbackPos;

        Bounds b = zoneBounds.bounds;
        for (int i = 0; i < 30; i++)
        {
            Vector3 point = new Vector3(
                Random.Range(b.min.x, b.max.x),
                waterSurfaceY,
                Random.Range(b.min.z, b.max.z)
            );

            // Verify the point is actually inside the collider (handles non-box shapes).
            Vector3 closest = zoneBounds.ClosestPoint(point);
            if (GetHorizontalDistance(point, closest) < 0.1f
                && HasClearanceAt(point, clearanceRadius, obstacleLayers, waterSurfaceY, obstacleRayHeight))
                return point;
        }

        // Fallback to center.
        return new Vector3(b.center.x, waterSurfaceY, b.center.z);
    }

    // Faces the host transform along the XZ direction toward target, slerped by rotateLerpSpeed
    // (default 5 — matches the original FishRipple value).
    public static void FaceMovementDirection(Transform host, Vector3 target, float rotateLerpSpeed = 5f)
    {
        Vector3 dir = target - host.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
        {
            host.rotation = Quaternion.Slerp(
                host.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * rotateLerpSpeed
            );
        }
    }
}
