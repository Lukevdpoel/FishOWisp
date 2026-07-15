using System.Collections.Generic;
using UnityEngine;

// A moving body that shoves VerletFoliage (vine curtains, lily pads) out of its way. Register the
// player and each fish here so foliage reacts without any per-object wiring — VerletFoliage walks
// PlantPusher.All every frame, exactly the decoupled, global-broadcast style the project already
// uses for WaterRippleSimRenderer and FoliageInteractionSetter.
//
// Two response modes:
//   - CONTINUOUS (default, the player): foliage verts are projected out of the capsule every sim
//     step while the body is inside, so a curtain stays parted around you.
//   - IMPULSE (impulseOnly, fish): foliage gets ONE wiggle kick the first time the body touches it,
//     then ignores the body until it leaves and comes back. Impulse bodies never hold plants awake
//     and never enter the per-vertex solver loop, so a pond full of fish stays near-free.
//
// Modeled as a CAPSULE (a vertical segment + radius), not a point, so a tall vine curtain is parted
// along the whole height of the body rather than only where its centre sits. Set height ~= 2*radius
// for a plain sphere.
//
// No physics colliders involved: VerletFoliage projects its sim points out of this volume directly,
// and Verlet turns that positional shove into momentum on its own, so the curtain swings and settles
// after the body passes through.
public class PlantPusher : MonoBehaviour
{
    public static readonly List<PlantPusher> All = new List<PlantPusher>();

    [Tooltip("Radius of the push volume, world units.")]
    public float radius = 0.45f;
    [Tooltip("Total height of the capsule push volume (world units). Set ~= 2*radius for a sphere.")]
    public float height = 1.8f;
    [Tooltip("Local-space offset of the capsule centre from this transform (e.g. raise it to the body's middle).")]
    public Vector3 centerOffset = new Vector3(0f, 0.9f, 0f);

    [Header("Response Mode")]
    [Tooltip("OFF (player): continuously parts foliage while inside it. ON (fish): foliage gets a single wiggle when this body first touches it, then ignores it until it leaves and returns — it never holds plants awake, so schools of fish cost almost nothing.")]
    public bool impulseOnly = false;
    [Tooltip("Impulse mode only: outward vertex speed (world units/sec) of the wiggle at the contact point. Keep SMALL — the plant's impulseStiffness/impulseDamping are floaty, so this velocity travels far before it's reined in (~0.15 gives a gentle few-cm bob; 0.6 launches the plant).")]
    public float impulseStrength = 0.15f;

    // The capsule as a segment p0..p1 plus radius, in world space. half collapses to 0 (a sphere)
    // when height <= 2*radius.
    public void GetCapsule(out Vector3 p0, out Vector3 p1, out float r)
    {
        r = Mathf.Max(0f, radius);
        Vector3 center = transform.TransformPoint(centerOffset);
        Vector3 axis = transform.up;
        float half = Mathf.Max(0f, height * 0.5f - r);
        p0 = center - axis * half;
        p1 = center + axis * half;
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    void OnDrawGizmosSelected()
    {
        GetCapsule(out Vector3 p0, out Vector3 p1, out float r);
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.6f);
        Gizmos.DrawWireSphere(p0, r);
        Gizmos.DrawWireSphere(p1, r);
        Gizmos.DrawLine(p0, p1);
    }
}
