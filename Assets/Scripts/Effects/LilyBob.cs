using System.Collections.Generic;
using UnityEngine;

// Cheap procedural bob for a lily pad reacting to a passing body — a decaying spring on the
// TRANSFORM (lift + tilt), NOT a per-vertex Verlet solve. A lily pad is effectively rigid, so a
// whole-object bob reads the same as the old VerletFoliage wiggle at a tiny fraction of the cost:
// a handful of floats per frame instead of hundreds of vertex integrations + SetVertices +
// RecalculateBounds. Across the pond's hundreds of pads this is the difference between ~2 ms and
// near-zero.
//
// Trigger: reuses the existing PlantPusher registry, exactly like VerletFoliage did — the player
// and every fish register a capsule there. When one first comes within reach of this pad it fires a
// single bob, then is "spent" until it leaves and returns (one-shot with hysteresis, so a fish
// parked on a pad costs nothing after its one kick and hovering at the edge can't retrigger). Both
// fish (impulse pushers) and the player (continuous pushers) bob the pad; there's no per-vertex
// parting anymore, just the bob.
//
// Idle cost: one broad-phase distance check per active pusher, and only while the pad is on-screen
// (an off-screen bob is invisible, so the whole update early-outs). While settled with no body near,
// it writes nothing to the transform — only that cheap scan runs.
//
// Replaces VerletFoliage on the lily/waterplant prefabs. Keep Verlet only where per-vertex jelly
// deformation actually shows (e.g. a hanging curtain); a flat pad doesn't need it.
[DefaultExecutionOrder(500)]
public class LilyBob : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("Extra world-space reach added to a body's push radius before it counts as touching the pad. Larger = the pad reacts to bodies passing a little further away.")]
    public float reach = 0.35f;
    [Tooltip("Approximate horizontal radius of the pad (world units), used for the touch test. Auto-estimated from the renderer bounds at startup if left <= 0.")]
    public float padRadius = 0f;

    [Header("Bob Feel")]
    [Tooltip("Upward speed of the bob kick (world units/sec) when a body first touches. ~0.15 gives a gentle few-cm rise.")]
    public float bobStrength = 0.15f;
    [Tooltip("Tilt kick (degrees/sec of angular velocity) so the pad rocks away from the contact, like water displaced under it. 0 = pure vertical bob, no rock.")]
    public float tiltStrength = 25f;
    [Tooltip("Spring stiffness pulling the pad back to rest. Higher = snappier, faster settle.")]
    public float stiffness = 45f;
    [Tooltip("Spring damping. Higher = less overshoot / fewer bounces before it settles.")]
    public float damping = 6f;
    [Tooltip("Largest vertical rise allowed (world units) — clamps the bob so a fast body can't fling the pad.")]
    public float maxRise = 0.2f;
    [Tooltip("Largest tilt allowed (degrees).")]
    public float maxTilt = 20f;

    [Header("Culling")]
    [Tooltip("Skip everything (scan + bob) while no camera can see the pad. An off-screen bob is invisible, so this is free performance across a big pond. Turn off only if something needs pads to react off-screen.")]
    public bool gateByVisibility = true;

    // Rest pose the bob is layered on top of, captured once so the spring always returns exactly.
    private Vector3 baseLocalPos;
    private Quaternion baseLocalRot;
    private Vector3 upLocal = Vector3.up; // world up expressed in the parent's space, so the lift stays vertical

    // Spring state: vertical rise (h) and two tilt angles (tx around local X, tz around local Z).
    private float h, hVel;
    private float tx, txVel;
    private float tz, tzVel;
    private bool active; // any spring non-zero — lets a settled pad skip the transform write

    private Renderer rend;
    private HashSet<PlantPusher> spent;

    private const float SettleEps = 1e-4f;

    void Start()
    {
        baseLocalPos = transform.localPosition;
        baseLocalRot = transform.localRotation;
        upLocal = transform.parent != null
            ? transform.parent.InverseTransformDirection(Vector3.up)
            : Vector3.up;

        rend = GetComponentInChildren<Renderer>();

        if (padRadius <= 0f)
        {
            // Estimate the pad's horizontal half-extent from its bounds so the touch test is sized
            // to the actual mesh without hand-tuning per pad.
            padRadius = rend != null
                ? Mathf.Max(0.1f, Mathf.Max(rend.bounds.extents.x, rend.bounds.extents.z))
                : 0.4f;
        }
    }

    void LateUpdate()
    {
        // Freeze with the game (pause / notebook), matching the foliage convention.
        if (Time.timeScale == 0f || Time.deltaTime <= 0.0001f) return;

        // Off-screen: nobody sees the bob, so skip the scan AND the spring entirely.
        if (gateByVisibility && rend != null && !rend.isVisible) return;

        DetectTouches();

        if (!active) return;

        float dt = Time.deltaTime;
        IntegrateSpring(ref h, ref hVel, dt);
        IntegrateSpring(ref tx, ref txVel, dt);
        IntegrateSpring(ref tz, ref tzVel, dt);

        h = Mathf.Clamp(h, -maxRise, maxRise);
        tx = Mathf.Clamp(tx, -maxTilt, maxTilt);
        tz = Mathf.Clamp(tz, -maxTilt, maxTilt);

        // Fully settled? Snap to rest exactly and stop writing the transform until the next touch.
        if (Mathf.Abs(h) < SettleEps && Mathf.Abs(hVel) < SettleEps
            && Mathf.Abs(tx) < SettleEps && Mathf.Abs(txVel) < SettleEps
            && Mathf.Abs(tz) < SettleEps && Mathf.Abs(tzVel) < SettleEps)
        {
            h = hVel = tx = txVel = tz = tzVel = 0f;
            active = false;
            transform.localPosition = baseLocalPos;
            transform.localRotation = baseLocalRot;
            return;
        }

        transform.localPosition = baseLocalPos + upLocal * h;
        transform.localRotation = baseLocalRot * Quaternion.Euler(tx, 0f, tz);
    }

    // Semi-implicit (symplectic) spring toward 0: stable at any framerate, and the exp() damping
    // never overshoots into instability the way a raw (1 - d*dt) factor can at low FPS.
    private void IntegrateSpring(ref float x, ref float v, float dt)
    {
        v += -stiffness * x * dt;
        v *= Mathf.Exp(-damping * dt);
        x += v * dt;
    }

    // Broad-phase touch detection against every registered pusher, one-shot per pusher with
    // hysteresis. Fires a bob the first frame a body comes within reach; that pusher stays "spent"
    // until it leaves the reach radius, so a body sitting on the pad doesn't retrigger.
    private void DetectTouches()
    {
        var all = PlantPusher.All;
        if (all.Count == 0) return;

        Vector3 center = transform.position;
        for (int i = 0; i < all.Count; i++)
        {
            PlantPusher p = all[i];
            if (p == null || !p.isActiveAndEnabled) continue;

            p.GetCapsule(out Vector3 a, out Vector3 b, out float r);
            float touchDist = r + padRadius + reach;
            Vector3 closest = ClosestPointOnSegment(a, b, center);
            bool inRange = (closest - center).sqrMagnitude < touchDist * touchDist;

            bool isSpent = spent != null && spent.Contains(p);
            if (isSpent)
            {
                if (!inRange) spent.Remove(p); // left — re-arm for the next pass
                continue;
            }
            if (!inRange) continue;

            spent ??= new HashSet<PlantPusher>();
            spent.RemoveWhere(x => x == null); // drop destroyed fish (rare — only on a new touch)
            spent.Add(p);
            Kick(closest, center);
        }
    }

    // Turn a touch into spring velocity: a vertical rise plus a rock away from the contact side.
    private void Kick(Vector3 contactPoint, Vector3 center)
    {
        hVel += bobStrength;

        if (tiltStrength > 0f)
        {
            // Horizontal direction from the contact toward the pad centre, in the pad's local space,
            // mapped to the two tilt axes so the pad rocks away from where it was touched.
            Vector3 dirWorld = center - contactPoint;
            dirWorld.y = 0f;
            if (dirWorld.sqrMagnitude > 1e-6f)
            {
                Vector3 dl = transform.InverseTransformDirection(dirWorld.normalized);
                txVel += dl.z * tiltStrength;
                tzVel += -dl.x * tiltStrength;
            }
        }
        active = true;
    }

    // Nearest point on segment a..b to p (same helper the pushers/foliage use).
    private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-8f) return a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return a + ab * t;
    }
}
