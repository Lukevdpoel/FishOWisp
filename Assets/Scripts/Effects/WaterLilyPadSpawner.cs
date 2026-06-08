using System.Collections.Generic;
using UnityEngine;

// Spawns a cluster of WaterLilyPad instances around the player whenever the player
// is over water. Pools instances so we don't churn allocations as the player walks.
// Attach to the player root (or a child of the player) — the spawner uses its own
// position as the cluster center each spawn tick.
public class WaterLilyPadSpawner : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private WaterLilyPad lilyPrefab;
    [SerializeField] private Transform poolParent;

    public enum WaterDetectionMode { RaycastLayer, FixedSurfaceY, ReferenceTransform }

    [Header("Water Detection")]
    [SerializeField] private WaterDetectionMode detectionMode = WaterDetectionMode.RaycastLayer;
    [Tooltip("Used when detectionMode = RaycastLayer. The raycast passes QueryTriggerInteraction.Collide so the existing trigger water collider works without changes.")]
    [SerializeField] private LayerMask waterLayer;
    [SerializeField] private float raycastFromHeight = 5f;
    [SerializeField] private float raycastDistance = 12f;
    [Tooltip("Used when detectionMode = FixedSurfaceY. Skips the raycast entirely — just pretends water is at this world Y.")]
    [SerializeField] private float fixedSurfaceY = 0f;
    [Tooltip("Used when detectionMode = ReferenceTransform. Pulls the surface Y from this transform's world position each tick.")]
    [SerializeField] private Transform waterSurfaceReference;
    [Tooltip("If true, spawning is suppressed when the player is more than this far above the water surface (so jumping off doesn't seed pads in mid-air).")]
    [SerializeField] private bool requireNearSurface = true;
    [SerializeField] private float maxHeightAboveSurface = 1.5f;

    [Header("Cluster")]
    [Tooltip("Radius around player where lilies may pop up.")]
    [SerializeField] private float clusterRadius = 1.8f;
    [Tooltip("Hard cap so we don't spam pads.")]
    [SerializeField] private int maxActive = 24;

    [Header("Timing")]
    [SerializeField] private float spawnIntervalMin = 0.12f;
    [SerializeField] private float spawnIntervalMax = 0.28f;
    [Tooltip("Max placement attempts per spawn tick before giving up that tick.")]
    [SerializeField] private int placementAttempts = 5;

    [Header("Behavior")]
    [Tooltip("Only spawn when the player is moving — set false to also spawn while idle on water.")]
    [SerializeField] private bool requireMovement = false;
    [SerializeField] private float movementThreshold = 0.3f;

    private float nextSpawnTime;
    private Vector3 lastPosition;
    private readonly Stack<WaterLilyPad> pool = new Stack<WaterLilyPad>();
    private readonly List<WaterLilyPad> active = new List<WaterLilyPad>();
    private Transform playerTransform;

    void Awake()
    {
        playerTransform = transform;
        lastPosition = transform.position;
    }

    void Update()
    {
        if (lilyPrefab == null) return;

        if (Time.time < nextSpawnTime) return;
        nextSpawnTime = Time.time + Random.Range(spawnIntervalMin, spawnIntervalMax);

        if (requireMovement)
        {
            float moved = (transform.position - lastPosition).magnitude / Mathf.Max(0.0001f, Time.deltaTime);
            lastPosition = transform.position;
            if (moved < movementThreshold) return;
        }

        if (active.Count >= maxActive) return;

        if (!TryGetWaterSurfaceY(out float waterY)) return;

        if (requireNearSurface && Mathf.Abs(transform.position.y - waterY) > maxHeightAboveSurface) return;

        for (int attempt = 0; attempt < placementAttempts; attempt++)
        {
            Vector2 r = Random.insideUnitCircle * clusterRadius;
            Vector3 candidate = new Vector3(transform.position.x + r.x, waterY, transform.position.z + r.y);
            if (TooCloseToExisting(candidate)) continue;
            Spawn(candidate, waterY);
            break;
        }
    }

    private bool TryGetWaterSurfaceY(out float y)
    {
        switch (detectionMode)
        {
            case WaterDetectionMode.FixedSurfaceY:
                y = fixedSurfaceY;
                return true;
            case WaterDetectionMode.ReferenceTransform:
                if (waterSurfaceReference == null) { y = 0f; return false; }
                y = waterSurfaceReference.position.y;
                return true;
            case WaterDetectionMode.RaycastLayer:
            default:
                Vector3 origin = transform.position + Vector3.up * raycastFromHeight;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastDistance, waterLayer, QueryTriggerInteraction.Collide))
                {
                    y = hit.point.y;
                    return true;
                }
                y = 0f;
                return false;
        }
    }

    private bool TooCloseToExisting(Vector3 pos)
    {
        // Each pad reserves its own footprint — no new pad spawns inside it.
        // Symmetric check (candidate vs each active pad's radius) is enough since
        // all pads are spawned from the same prefab.
        for (int i = 0; i < active.Count; i++)
        {
            WaterLilyPad pad = active[i];
            float r = pad.FootprintRadius;
            Vector3 d = pad.transform.position - pos;
            d.y = 0f;
            if (d.sqrMagnitude < r * r) return true;
        }
        return false;
    }

    private void Spawn(Vector3 pos, float waterY)
    {
        WaterLilyPad pad = pool.Count > 0 ? pool.Pop() : Instantiate(lilyPrefab, poolParent != null ? poolParent : transform.parent);
        active.Add(pad);
        pad.Activate(pos, waterY, playerTransform, ReturnToPool);
    }

    private void ReturnToPool(WaterLilyPad pad)
    {
        active.Remove(pad);
        pool.Push(pad);
    }
}
