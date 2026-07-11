using UnityEngine;

// The world-space cast reticle. A real object (not a decal/projector — those can't hit the unlit
// water shader anyway) placed a hair above whatever surface is under the aim point, land or
// water alike. Its default visual is a flat ring built from a LineRenderer whose material is
// queued AFTER the opaque-queue-3000 water, so it always draws on top of the surface it sits on.
//
// To give it real art: make a prefab with this component and any visuals as children — if any
// Renderer already exists under the marker at Awake, the procedural ring is skipped (the water/
// land tinting then only applies to renderers you leave in the 'tintedRenderers' list).
public class CastAimMarker : MonoBehaviour
{
    [Header("Default Ring Visual")]
    [Tooltip("Radius (m) of the procedurally built fallback ring.")]
    public float ringRadius = 0.55f;
    [Tooltip("Line width (m) of the fallback ring.")]
    public float ringWidth = 0.06f;
    [Tooltip("Segments of the fallback ring circle.")]
    public int ringSegments = 40;
    [Tooltip("Ring tint while the marker sits on land. Fish only live in water, so land reads muted.")]
    public Color landColor = new Color(1f, 0.95f, 0.8f, 0.85f);
    [Tooltip("Ring tint while the marker sits on water.")]
    public Color waterColor = new Color(0.45f, 0.95f, 1f, 0.95f);
    [Tooltip("Gentle breathing of the ring scale so the marker reads as live. 0 disables.")]
    [Range(0f, 0.3f)] public float pulseAmplitude = 0.07f;
    public float pulseHz = 1.2f;

    [Header("Surface Snap")]
    [Tooltip("How far above the last position the downward surface probe starts.")]
    public float probeHeight = 30f;
    [Tooltip("Length of the downward surface probe.")]
    public float probeDistance = 80f;
    [Tooltip("Lift above the hit surface so the ring never z-fights the geometry it sits on.")]
    public float surfaceOffset = 0.06f;

    /// <summary>True when the marker currently sits on a water surface.</summary>
    public bool IsOverWater { get; private set; }

    /// <summary>The snapped world point on the surface (before the anti-z-fight lift).</summary>
    public Vector3 SurfacePoint { get; private set; }

    private LineRenderer ring;
    private Transform ringTransform;
    private Material ringMaterial;

    private void Awake()
    {
        if (GetComponentInChildren<Renderer>() == null) BuildDefaultRing();
        else ring = GetComponentInChildren<LineRenderer>();
    }

    private void OnDestroy()
    {
        if (ringMaterial != null) Destroy(ringMaterial);
    }

    private void Update()
    {
        if (ringTransform == null || pulseAmplitude <= 0f) return;
        float s = 1f + pulseAmplitude * Mathf.Sin(Time.time * pulseHz * 2f * Mathf.PI);
        ringTransform.localScale = new Vector3(s, s, s);
    }

    /// <summary>
    /// Snap the marker onto the surface under the given horizontal position. Water is probed the
    /// same way the reel-in waterline is (downward ray, triggers included) so docks/banks resolve
    /// consistently; solid geometry is probed with triggers ignored so fishing-zone volumes and
    /// fish ripples can't catch the ray. The higher of the two hits wins (water lies over the bed).
    /// </summary>
    public void Place(Vector3 horizontalPosition, LayerMask surfaceMask, LayerMask waterMask)
    {
        Vector3 from = new Vector3(horizontalPosition.x, horizontalPosition.y + probeHeight, horizontalPosition.z);

        bool hasSolid = Physics.Raycast(from, Vector3.down, out RaycastHit solidHit, probeDistance,
                                        surfaceMask & ~waterMask.value, QueryTriggerInteraction.Ignore);
        RaycastHit waterHit = default;
        bool hasWater = waterMask.value != 0
                        && Physics.Raycast(from, Vector3.down, out waterHit, probeDistance,
                                           waterMask, QueryTriggerInteraction.Collide);

        Vector3 point;
        Vector3 normal;
        if (hasWater && (!hasSolid || waterHit.point.y >= solidHit.point.y))
        {
            point = waterHit.point;
            normal = Vector3.up; // water is flat; its collider normal can be noisy on mesh seams
            IsOverWater = true;
        }
        else if (hasSolid)
        {
            point = solidHit.point;
            normal = solidHit.normal;
            IsOverWater = false;
        }
        else
        {
            // Nothing under the probe (off the edge of the world) — hover at the requested spot.
            point = horizontalPosition;
            normal = Vector3.up;
            IsOverWater = false;
        }

        SurfacePoint = point;
        transform.position = point + normal * surfaceOffset;
        transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);

        if (ring != null)
        {
            Color c = IsOverWater ? waterColor : landColor;
            ring.startColor = c;
            ring.endColor = c;
        }
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);

    private void BuildDefaultRing()
    {
        GameObject ringGO = new GameObject("Ring");
        ringTransform = ringGO.transform;
        ringTransform.SetParent(transform, false);

        ring = ringGO.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.widthMultiplier = ringWidth;
        ring.positionCount = ringSegments;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;

        Vector3[] points = new Vector3[ringSegments];
        for (int i = 0; i < ringSegments; i++)
        {
            float a = i / (float)ringSegments * Mathf.PI * 2f;
            points[i] = new Vector3(Mathf.Cos(a) * ringRadius, 0f, Mathf.Sin(a) * ringRadius);
        }
        ring.SetPositions(points);

        // Queue 3100 puts the ring after the opaque-queue-3000 water, so it draws on top of the
        // water plane instead of being swallowed by its fake-transparency scene-color pass.
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            ringMaterial = new Material(shader) { renderQueue = 3100 };
            ring.material = ringMaterial;
        }
    }
}
