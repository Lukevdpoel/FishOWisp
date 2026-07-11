using UnityEngine;

// Hangs a lure/bobber on the shop board from this (empty) anchor, reusing the BobberItem scriptable
// asset — the same asset the rod uses to show tackle while you're not fishing.
//
// Why this exists: the rod's passive dangle is physics-driven (Rigidbody + ConfigurableJoint +
// gravity, with FishingLine orienting it each frame), and the prefab's BobberController runs
// buoyancy/struggle in FixedUpdate. So a raw prefab parented to the board would just fall and keep
// ticking. This spawns the model and neutralizes that — kinematic Rigidbody + disabled
// BobberController — so it hangs as a clean static prop, still driven by the BobberItem asset.
//
// Hover reaction: instead of ShopItem's lift, the tackle SWINGS — a damped spring-pendulum about
// its hang point (its top), kicked each time you start hovering, settling on its own. Put this on
// the same GameObject as a ShopItem (the board slot, which carries the Collider + TackleShopOffer).
[RequireComponent(typeof(ShopItem))]
public class ShopTackleHanger : MonoBehaviour
{
    [Header("Tackle")]
    [Tooltip("The lure/bobber to hang. Use the same BobberItem asset the slot's TackleShopOffer sells.")]
    [SerializeField] private BobberItem tackle;
    [Tooltip("Optional extra tilt (degrees) applied ON TOP of the natural vertical hang.")]
    [SerializeField] private Vector3 localEuler;
    [Tooltip("Multiplies the prefab's own scale (1 = unchanged).")]
    [SerializeField] private float scaleMultiplier = 1f;
    [Tooltip("Hang the model from its line-attach point (its top) so it dangles BELOW this slot, " +
             "like on the rod. Off = the model's origin sits on the slot.")]
    [SerializeField] private bool hangFromAttachPoint = true;
    [Tooltip("Extra local nudge applied after aligning.")]
    [SerializeField] private Vector3 hangOffset;

    [Header("Hover Wiggle")]
    [Tooltip("Master switch for the swing. Untick to make the tackle hang dead-still (for isolating " +
             "whether the per-frame swing is a perf cost — hover still highlights/labels).")]
    [SerializeField] private bool enableWiggle = true;
    [Tooltip("Swing frequency in Hz (how fast it sways back and forth).")]
    [SerializeField] private float wiggleFrequency = 2.2f;
    [Tooltip("Damping as a fraction of critical: 0 = never settles, 1 = no overshoot. ~0.1 reads as " +
             "a lively swing that settles in a second or so.")]
    [Range(0f, 1f)][SerializeField] private float wiggleDamping = 0.12f;
    [Tooltip("Initial swing amplitude (degrees) imparted each time you start hovering.")]
    [SerializeField] private float wiggleStrength = 12f;
    [Tooltip("Hard cap on swing angle (degrees) so rapid re-hovers can't fling it.")]
    [SerializeField] private float wiggleMaxAngle = 35f;

    private Transform pivot;
    private ShopItem shopItem;
    private bool wasHighlighted;
    private bool settledAtRest;
    private float angX, velX;   // swing about local X (degrees, deg/s)
    private float angZ, velZ;   // swing about local Z

    private void Start()
    {
        shopItem = GetComponent<ShopItem>();
        if (tackle == null || tackle.bobberPrefab == null) return;

        // Pivot at this slot's origin — the point the model hangs from. Swinging the pivot swings
        // the model about that point (its top), like a pendulum.
        pivot = new GameObject("TackleHangPivot").transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = Vector3.zero;
        pivot.localRotation = Quaternion.identity;

        GameObject inst = Instantiate(tackle.bobberPrefab, pivot);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localScale *= scaleMultiplier;

        // Capture the attach point BEFORE neutralizing components (the property still works after, but
        // be safe), then orient + position while it's a live transform.
        BobberController bc = inst.GetComponent<BobberController>();
        Transform attach = bc != null ? bc.LineAttachPoint : null;

        // Orient like the rod's passive dangle: rotate so the model's line-attach axis points straight
        // up, so it hangs vertically (these are authored lying flat for the water). Falls back to just
        // the facing yaw if there's no distinct attach point.
        if (attach != null && attach != inst.transform)
            inst.transform.rotation = ComputeDangleRotation(inst.transform, attach);
        else
            inst.transform.localRotation = Quaternion.Euler(0f, tackle.dangleFacingYawOffset, 0f);

        if (localEuler != Vector3.zero) inst.transform.Rotate(localEuler, Space.Self);

        // Now that it's oriented, land its attach point on the pivot origin so it hangs below it.
        if (hangFromAttachPoint && attach != null)
            inst.transform.position += pivot.position - attach.position;
        inst.transform.localPosition += hangOffset;

        // Make it a fully inert visual: the slot owns the hover collider and we drive the swing, so the
        // spawned prefab needs no physics, particles, audio, or scripts. Leaving those live made every
        // wiggle re-sync colliders/rigidbodies and keep particle systems emitting — the perf drop.
        MakeStaticVisual(inst);

        // Tackle reacts to hover by swinging, not lifting.
        if (shopItem != null) shopItem.LiftEnabled = false;
    }

    private static void MakeStaticVisual(GameObject go)
    {
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
            // Stop interpolation: it lerps the rendered pose toward the rigidbody each frame, which
            // both costs perf and fights our transform-driven swing (the "rests wrongly" drift).
            rb.interpolation = RigidbodyInterpolation.None;
        }
        foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.EmissionModule em = ps.emission;
            em.enabled = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        foreach (TrailRenderer tr in go.GetComponentsInChildren<TrailRenderer>(true)) tr.enabled = false;
        foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
        // Disable every behaviour (BobberController, VerletRope, lure handlers, …) so nothing ticks.
        foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) mb.enabled = false;
    }

    // Port of FishingLine.DangleRestRotation: point the model's line-attach axis up so it hangs
    // vertically, then lock the spin so its facing is consistent (heading = this slot's forward),
    // plus the per-item dangleFacingYawOffset. This is the same pose the rod parks it in when passive.
    private Quaternion ComputeDangleRotation(Transform model, Transform attach)
    {
        Vector3 localAttachDir = model.InverseTransformPoint(attach.position);
        if (localAttachDir.sqrMagnitude < 0.0001f) return model.rotation;
        localAttachDir.Normalize();

        // 1) Point the attach axis straight up (leaves the spin about up free).
        Quaternion rot = Quaternion.FromToRotation(localAttachDir, Vector3.up);

        // 2) Lock that spin so an elongated lure shows a consistent face — aim a side axis along
        //    this slot's forward (the rod uses the rod tip's forward).
        Vector3 localSide = Vector3.ProjectOnPlane(Vector3.up, localAttachDir);
        if (localSide.sqrMagnitude < 0.0001f) localSide = Vector3.ProjectOnPlane(Vector3.forward, localAttachDir);
        if (localSide.sqrMagnitude > 0.0001f)
        {
            Vector3 sideNow = Vector3.ProjectOnPlane(rot * localSide.normalized, Vector3.up);
            Vector3 heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (sideNow.sqrMagnitude > 0.0001f && heading.sqrMagnitude > 0.0001f)
                rot = Quaternion.FromToRotation(sideNow.normalized, heading.normalized) * rot;
        }

        // 3) Per-item facing nudge so each model can present its good side.
        float yaw = tackle.dangleFacingYawOffset;
        if (Mathf.Abs(yaw) > 0.001f) rot = Quaternion.AngleAxis(yaw, Vector3.up) * rot;

        return rot;
    }

    private const float RestEpsilon = 0.02f; // deg / deg-per-sec below which we consider it settled

    private void Update()
    {
        if (pivot == null || !enableWiggle) return;

        bool hi = shopItem != null && shopItem.IsHighlighted;
        if (hi && !wasHighlighted) Kick();   // nudge it each time hovering starts
        wasHighlighted = hi;

        // Idle (not hovered and not still swinging) slots do NOTHING — no per-frame transform writes,
        // so a wall of tackle costs nothing until you actually hover one. Snap to rest exactly once.
        bool active = hi || Mathf.Abs(angX) > RestEpsilon || Mathf.Abs(angZ) > RestEpsilon
                      || Mathf.Abs(velX) > RestEpsilon || Mathf.Abs(velZ) > RestEpsilon;
        if (!active)
        {
            if (!settledAtRest)
            {
                angX = angZ = velX = velZ = 0f;
                pivot.localRotation = Quaternion.identity;
                settledAtRest = true;
            }
            return;
        }
        settledAtRest = false;

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        // Damped harmonic oscillator per swing axis: rest is hanging straight (angle 0).
        float omega = 2f * Mathf.PI * Mathf.Max(0.01f, wiggleFrequency);
        float k = omega * omega;              // stiffness
        float c = 2f * wiggleDamping * omega; // damping
        Integrate(ref angX, ref velX, k, c, dt);
        Integrate(ref angZ, ref velZ, k, c, dt);

        pivot.localRotation = Quaternion.Euler(angX, 0f, angZ);
    }

    // Semi-implicit (symplectic) Euler — stable for stiff springs; clamped so it can't blow up.
    private void Integrate(ref float a, ref float v, float k, float c, float dt)
    {
        v += (-k * a - c * v) * dt;
        a += v * dt;
        if (a > wiggleMaxAngle) { a = wiggleMaxAngle; if (v > 0f) v = 0f; }
        else if (a < -wiggleMaxAngle) { a = -wiggleMaxAngle; if (v < 0f) v = 0f; }
    }

    private void Kick()
    {
        float omega = 2f * Mathf.PI * Mathf.Max(0.01f, wiggleFrequency);
        float speed = wiggleStrength * omega; // deg/s to reach ~wiggleStrength deg of swing
        float dir = Random.Range(0f, Mathf.PI * 2f); // random swing direction for variety
        velX += Mathf.Cos(dir) * speed;
        velZ += Mathf.Sin(dir) * speed;
    }
}
