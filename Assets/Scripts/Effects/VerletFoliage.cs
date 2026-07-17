using System.Collections.Generic;
using UnityEngine;

// Per-vertex Verlet for an UNSKINNED foliage MeshFilter (vine curtains now; the same solver suits
// lily pads later). It is the sibling of SoftBodyJiggle: same proven core — world-space Verlet with
// inertia, structural edge constraints, a fixed timestep and a hard offset clamp — but specialised
// for plants the player/fish move THROUGH rather than a character body that wobbles from its own
// motion. The two differences from SoftBodyJiggle:
//
//   1. EXTERNAL PUSHERS. Every continuous PlantPusher (the player) shoves the sim points out of its
//      capsule each step. Verlet converts that positional shove into momentum for free, so the
//      curtain parts as you walk in and swings + settles once you pass through. SoftBodyJiggle had
//      no concept of an outside agent touching it. Pushers marked impulseOnly (fish) instead
//      deliver ONE wiggle kick on first touch and are then ignored until they leave and return —
//      they never appear in the per-step solver loop and never block sleeping.
//
//   2. EDGE ANCHORING. A vine hangs from its top edge, so verts near the top (along anchorAxis) are
//      pinned to their rest pose while the rest swing freely — instead of SoftBodyJiggle's pin-by-
//      distance-to-a-socket scheme. Set anchorAxis to pin a different edge (lily pads will pin a
//      centre region instead; that's a future mode).
//
//   3. SLEEPING. Once the plant has settled back at its rest pose (and no wind is configured), the
//      sim parks the rest mesh and shuts off completely; a sleeping instance costs one distance
//      check per PlantPusher per frame. This is what makes it OK to scatter dozens of lily pads
//      with this component — only the plants actively being pushed (or still swinging afterwards)
//      pay for the solver. Three rules keep that true even with fish pushers roaming BETWEEN the
//      plants all day:
//        - Waking needs actual contact: a capsule near a real (rest) vertex, not merely inside the
//          mesh's bounds sphere. A fish idling beside or under a pad doesn't hold it awake.
//        - Off-screen plants never wake, and an awake plant that leaves the screen parks instantly.
//        - MaxAwake caps how many instances may simulate at once, bounding the worst frame.
//
// Like SoftBodyJiggle, this writes ONLY vertex data on a private mesh copy, so it composes under any
// transform animation and never mutates the shared asset. The source mesh MUST be Read/Write enabled
// (Inspector → Model → Meshes → 'Read/Write Enabled').
//
// Execution order 500: after PlayerController writes the player transform (order 0) so pushers are
// sampled at their current-frame position, matching SoftBodyJiggle.
[DefaultExecutionOrder(500)]
[RequireComponent(typeof(MeshFilter))]
public class VerletFoliage : MonoBehaviour
{
    [Header("Feel")]
    [Tooltip("How strongly each vertex springs back to its rest shape each step (0 = floppy, 1 = rigid/snappy). Vines want this low so they part and swing.")]
    [Range(0f, 1f)] public float stiffness = 0.08f;
    [Tooltip("Velocity bled off each step (0 = swings forever, 1 = dead stop). Higher settles faster after you pass through.")]
    [Range(0f, 1f)] public float damping = 0.06f;
    [Tooltip("Structural edge-constraint passes per step. Keeps strands from stretching; more stiffens the curtain.")]
    [Range(0, 8)] public int constraintIterations = 3;
    [Tooltip("How tightly edges hold their rest length each pass (0 = stretchy, 1 = firm).")]
    [Range(0f, 1f)] public float edgeStiffness = 0.6f;

    [Header("Push Response")]
    [Tooltip("Extra reach added to every PlantPusher's radius when parting this foliage, world units. Bump up if the curtain only reacts once the body is right on top of it.")]
    public float pusherPadding = 0.1f;
    [Tooltip("Max distance (world units) a vertex may sit from its rest position. Bounds how far the curtain parts and stops a frame hitch from blowing it apart. Make this larger than how far the player should be able to shove a strand.")]
    public float maxOffset = 1.2f;

    public enum ImpulseStyle { Radial, WaterBob }
    [Tooltip("Shape of the one-shot fish wiggle. Radial: shoved away from the body — bushes, curtains, reeds. WaterBob: lifted upward (strongest at the contact side, so the plant tilts and rocks) like water displaced under it — lily pads and anything floating.")]
    public ImpulseStyle impulseStyle = ImpulseStyle.Radial;
    [Tooltip("Spring-back used ONLY while ringing out from a fish wiggle — the normal Stiffness above still governs player interaction. Lower = slower, lazier bob (water). Set equal to Stiffness for no difference.")]
    [Range(0f, 1f)] public float impulseStiffness = 0.03f;
    [Tooltip("Velocity bleed used ONLY while ringing out from a fish wiggle — the normal Damping above still governs player interaction. Lower = the wiggle rings longer before settling.")]
    [Range(0f, 1f)] public float impulseDamping = 0.02f;

    [Header("Ambient Wind")]
    [Tooltip("Gentle idle sway so the foliage isn't dead-static. 0 = OFF, which is the right choice when the foliage SHADER already does wind (e.g. the willow leaves) — let the GPU shader handle ambient flutter and leave Verlet for interaction only, otherwise the two stack. Turn this on only for plants with no wind shader.")]
    public float windStrength = 0f;
    [Tooltip("Wind sway cycles per second.")]
    public float windFrequency = 0.5f;
    [Tooltip("World-space direction the wind pushes (auto-normalised).")]
    public Vector3 windDirection = new Vector3(1f, 0f, 0.3f);
    [Tooltip("Per-axis spatial frequency of the travelling wind wave across the mesh. Non-zero makes different parts of the curtain sway out of phase (a wave travels across it) instead of the whole thing shoving as one block. (0,0,0) = uniform. Higher values = shorter waves / more per-leaf variation.")]
    public Vector3 windWaveScale = new Vector3(3f, 0f, 3f);

    [Header("Edge Anchoring")]
    [Tooltip("Local-space direction of the attached edge — verts at the far end of this axis are pinned. (0,1,0) pins the TOP (hanging vine). Auto-normalised.")]
    public Vector3 anchorAxis = Vector3.up;
    [Tooltip("Fraction of the mesh's extent (along anchorAxis), measured from the anchored edge, that stays fully pinned to the rest pose.")]
    [Range(0f, 1f)] public float pinFraction = 0.04f;
    [Tooltip("Fraction of the extent over which a pinned vert blends back up to full freedom below the pinned band.")]
    [Range(0f, 1f)] public float falloffFraction = 0.18f;

    [Header("Simulation")]
    [Tooltip("Fixed simulation rate (Hz). The sim advances in 1/this steps regardless of framerate, so the motion feels identical at any FPS and a lag spike can't destabilise it.")]
    public float simulationRate = 60f;
    [Tooltip("Max fixed steps run in one frame — a spiral-of-death guard. Below simulationRate/maxSubSteps FPS the sim slows down rather than exploding.")]
    [Range(1, 16)] public int maxSubSteps = 8;

    [Header("Sleeping")]
    [Tooltip("Shut the sim off completely once the plant has settled at rest — a sleeping plant costs one distance check per pusher per frame. It wakes when a PlantPusher comes in reach. Has no effect while Wind Strength > 0 (wind never lets the plant settle).")]
    public bool allowSleep = true;
    [Tooltip("The plant counts as settled when every vertex sits within this world-space distance of its rest position.")]
    public float sleepThreshold = 0.01f;
    [Tooltip("Seconds the plant must stay settled, with no pusher in reach, before it sleeps.")]
    public float sleepDelay = 0.4f;
    [Tooltip("Extra reach (world units) on the wake check, so the plant is already simulating by the time a pusher actually touches it.")]
    public float wakeMargin = 0.3f;

    // Hard cap on how many instances may run the solver at once, across the whole scene. Wakes past
    // the cap are skipped (the plant stays parked at rest) — a swarm of fish can then never blow the
    // frame budget, it can only animate the MaxAwake nearest-to-contact plants. Tune from code.
    public static int MaxAwake = 5;
    private static int awakeCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { awakeCount = 0; } // survives disabled domain reload

    [Header("Limits & Safety")]
    [Tooltip("If this object moves more than this in one frame (scene load / teleport), the sim snaps to rest instead of flinging.")]
    public float teleportThreshold = 5f;
    [Tooltip("Recompute normals each frame so lighting follows the deformation. OFF by default — most stylized foliage shaders rely on authored normals that recalculation would clobber.")]
    public bool recalculateNormals = false;
    [Tooltip("Vertices closer than this are welded into one sim point, so UV/normal seams don't tear apart while swinging.")]
    public float weldEpsilon = 1e-4f;

    private MeshFilter meshFilter;
    private Mesh workingMesh;

    private Vector3[] restLocalFull;   // exact rest positions of every render vertex (local space)
    private Vector3[] renderVerts;     // scratch written to the mesh each frame

    // Welded ("unique") simulation set — duplicate verts at a shared position collapse to one point.
    private Vector3[] uniqueRest;      // rest position per unique vert, local space
    private Vector3[] cur;             // current sim position, world space
    private Vector3[] prev;            // previous sim position, world space
    private Vector3[] restWorldCache;  // per-frame rest world positions (computed once, reused)
    private int[] fullToUnique;        // render-vertex index -> unique index
    private float[] weight;            // per unique vert: 0 = pinned to rest (top edge), 1 = full swing
    private float[] windPhase;         // per unique vert spatial phase, so wind travels as a wave instead of one block

    // Structural edges between unique verts. restDelta (local a->b) is re-transformed each frame so
    // target lengths track the object's scale/rotation.
    private int[] edgeA;
    private int[] edgeB;
    private Vector3[] edgeRestDelta;
    private float[] edgeTargetLen;

    // Pusher capsules resolved once per frame (tiny lists in practice — one player).
    private readonly List<Vector3> pushP0 = new List<Vector3>();
    private readonly List<Vector3> pushP1 = new List<Vector3>();
    private readonly List<float> pushR = new List<float>();

    private Vector3 lastPosition;
    private float accumulator;          // unspent real time, drained in fixed simulationRate steps
    private bool initialized;

    // Plants are static in practice, so restWorldCache / edgeTargetLen only change when the
    // transform does. Cache them against the localToWorldMatrix: wake checks and PrepareFrame then
    // read precomputed world positions instead of paying a matrix multiply per vertex per frame —
    // which was most of this component's cost with dozens of instances near the player.
    private Matrix4x4 cachedL2W;
    private bool worldCacheValid;

    // Sleeping plants only run their per-vertex wake checks every few frames, staggered by instance
    // so they don't all land on the same frame. wakeMargin already makes the check trigger early,
    // which more than covers the added latency.
    private const int WakeCheckInterval = 3;
    private int wakeCheckPhase;

    // Sleep state + the rest-mesh bounds (local space) used as the broad phase for wake/cull checks.
    private bool asleep;
    private bool countedAwake;         // holds one MaxAwake slot right now
    private float settleTimer;
    private Bounds restLocalBounds;
    private Vector3 localBoundsCenter;
    private float localBoundsRadius;
    private Renderer meshRenderer;     // visibility gate; null-safe if somehow absent

    // Impulse-mode pushers (fish): who already delivered their one-shot wiggle (spent until they
    // leave the broad sphere), and wiggles queued for the next sim step. Lazy — most plants never
    // meet a fish.
    private HashSet<PlantPusher> impulseSpent;
    private struct QueuedImpulse { public Vector3 a, b; public float r, strength; }
    private readonly List<QueuedImpulse> queuedImpulses = new List<QueuedImpulse>();
    // True while the current motion is a fish-wiggle ring-out: Step swaps to impulseStiffness /
    // impulseDamping for the floaty water feel. Cleared when a continuous pusher (the player)
    // gets involved, or on sleep — player interaction always uses the normal spring values.
    private bool impulseRinging;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<Renderer>();

        Mesh source = meshFilter.sharedMesh;
        if (source == null)
        {
            Debug.LogWarning($"{nameof(VerletFoliage)} on '{name}' has no mesh — disabling.", this);
            enabled = false;
            return;
        }

        if (!source.isReadable)
        {
            Debug.LogError(
                $"{nameof(VerletFoliage)} on '{name}': mesh '{source.name}' is not readable — disabling. " +
                "Enable it in the model's import settings: Inspector → Model → Meshes → 'Read/Write Enabled' → Apply.",
                this);
            enabled = false;
            return;
        }

        // Private writable copy so we never mutate the shared asset; dynamic for frequent updates.
        workingMesh = Instantiate(source);
        workingMesh.name = source.name + " (Foliage)";
        workingMesh.MarkDynamic();
        meshFilter.mesh = workingMesh;

        // CPU-deformed mesh: opt out of the GPU Resident Drawer, or every per-frame SetVertices
        // re-dispatches this renderer to the batcher (InstanceCullingBatcher.BuildBatch churn).
        GpuDrivenRenderingOptOut.Apply(meshRenderer);

        restLocalBounds = source.bounds;
        localBoundsCenter = restLocalBounds.center;
        localBoundsRadius = restLocalBounds.extents.magnitude;
        ApplyFixedMeshBounds();

        BuildSimData(source);
        ComputeWeights();
        ComputeWindPhases();
        ResetToRest();
        lastPosition = transform.position;
        wakeCheckPhase = ((GetInstanceID() % WakeCheckInterval) + WakeCheckInterval) % WakeCheckInterval;
        initialized = true;

        // Born asleep (the mesh IS the rest pose); the first pusher in reach wakes it. Keeps a scene
        // full of lily pads at zero sim cost until something actually swims up to one.
        asleep = allowSleep && windStrength <= 0f;
    }

    void OnDisable()
    {
        // A disabled instance must not keep holding a MaxAwake slot.
        if (countedAwake) { awakeCount--; countedAwake = false; }
        // Leave the mesh at its clean rest pose when switched off, and re-enter through the sleep
        // path on re-enable (the parked mesh already matches the sleeping state).
        if (initialized && workingMesh != null)
        {
            workingMesh.SetVertices(restLocalFull);
            if (recalculateNormals) workingMesh.RecalculateNormals();
            asleep = allowSleep && windStrength <= 0f;
            settleTimer = 0f;
            accumulator = 0f;
        }
    }

    void OnDestroy()
    {
        if (workingMesh != null) Destroy(workingMesh);
    }

    void LateUpdate()
    {
        if (!initialized) return;
        // Freeze with the game (pause / notebook), matching SoftBodyJiggle and PlayerController.
        if (Time.timeScale == 0f || Time.deltaTime <= 0.0001f) return;

        if (asleep)
        {
            // Wind switched on at runtime always wakes; otherwise waking needs the plant on screen,
            // real contact (continuous pusher near a vertex, or a fish first-touch wiggle), and a
            // free MaxAwake slot.
            if (windStrength <= 0f)
            {
                if (meshRenderer != null && !meshRenderer.isVisible) return;
                if ((Time.frameCount + wakeCheckPhase) % WakeCheckInterval != 0) return;
                bool contact = AnyPusherTouching();
                bool wiggle = CheckImpulseTriggers();
                if (!contact && !wiggle) return;
                if (awakeCount >= MaxAwake) { queuedImpulses.Clear(); return; }
            }
            WakeUp();
        }
        else if (allowSleep && windStrength <= 0f && meshRenderer != null && !meshRenderer.isVisible)
        {
            // Went off-screen mid-swing: park immediately — nobody can see the snap to rest.
            Sleep();
            return;
        }
        else
        {
            // Awake and visible: a fish can also first-touch while the player is already pushing.
            CheckImpulseTriggers();
        }

        // Teleport guard: a scene move shouldn't fling the mesh across the world.
        if ((transform.position - lastPosition).sqrMagnitude > teleportThreshold * teleportThreshold)
        {
            ResetToRest();
            lastPosition = transform.position;
            return;
        }
        lastPosition = transform.position;

        float fixedDt = 1f / Mathf.Max(1f, simulationRate);
        accumulator = Mathf.Min(accumulator + Time.deltaTime, maxSubSteps * fixedDt);
        if (accumulator >= fixedDt)
        {
            PrepareFrame();
            ApplyQueuedImpulses();
            while (accumulator >= fixedDt)
            {
                Step(fixedDt);
                accumulator -= fixedDt;
            }
        }

        WriteBack(accumulator / fixedDt);
        TrySleep();
    }

    // Per-frame setup that depends only on the (frame-constant) transform and pusher set: rest world
    // positions, edge target lengths, and each pusher's resolved capsule. Reused by every sub-step.
    private void PrepareFrame()
    {
        EnsureWorldCache();

        pushP0.Clear();
        pushP1.Clear();
        pushR.Clear();
        GetWorldBounds(out Vector3 boundsC, out float boundsR);
        var all = PlantPusher.All;
        for (int i = 0; i < all.Count; i++)
        {
            PlantPusher p = all[i];
            if (p == null || !p.isActiveAndEnabled || p.impulseOnly) continue;
            p.GetCapsule(out Vector3 a, out Vector3 b, out float r);
            // Broad phase: verts live within maxOffset of rest, and rest within boundsR of the bounds
            // centre — a capsule farther than that can't touch anything, so it never reaches the
            // per-vertex loop. Keeps cost flat when many pushers (a school of fish) share the pond.
            float reach = r + pusherPadding + boundsR + maxOffset;
            if ((ClosestPointOnSegment(a, b, boundsC) - boundsC).sqrMagnitude >= reach * reach) continue;
            pushP0.Add(a);
            pushP1.Add(b);
            pushR.Add(r + pusherPadding);
        }

        // A continuous body (the player) in play takes over the feel: back to the normal spring.
        if (pushP0.Count > 0) impulseRinging = false;
    }

    private void Step(float dt)
    {
        int n = cur.Length;
        // A fish-wiggle ring-out gets its own floatier spring; everything else (player, wind) uses
        // the normal values.
        float useStiffness = impulseRinging ? impulseStiffness : stiffness;
        float keep = 1f - (impulseRinging ? impulseDamping : damping);

        // Per-vertex ambient wind: a travelling wave (phase varies by rest position) so the curtain
        // sways out of phase rather than as one rigid block. Off by default — the willow shader owns
        // wind there; this is only for plants without a wind shader.
        bool doWind = windStrength > 0f && windPhase != null;
        Vector3 windDirN = Vector3.zero;
        float baseT = 0f;
        if (doWind)
        {
            windDirN = windDirection.sqrMagnitude > 1e-6f ? windDirection.normalized : Vector3.right;
            baseT = Time.time * windFrequency * Mathf.PI * 2f;
        }

        // Verlet integrate with inertia, then a soft spring pull back toward the rest pose. Pinned
        // verts (weight -> 0) pull fully to rest, anchoring the top edge the rest hangs off.
        for (int i = 0; i < n; i++)
        {
            Vector3 vel = (cur[i] - prev[i]) * keep;
            prev[i] = cur[i];
            Vector3 p = cur[i] + vel;
            if (doWind)
            {
                float ph = windPhase[i];
                float s = (Mathf.Sin(baseT + ph) + 0.5f * Mathf.Sin(baseT * 1.7f + ph * 1.7f + 1.3f)) * 0.6667f;
                p += windDirN * (s * windStrength * dt * weight[i]);
            }
            float stiff = Mathf.Lerp(1f, useStiffness, weight[i]);
            p = Vector3.Lerp(p, restWorldCache[i], stiff);
            cur[i] = p;
        }

        // Structural edge constraints keep strands coherent (a curtain, not confetti).
        for (int it = 0; it < constraintIterations; it++)
        {
            for (int e = 0; e < edgeA.Length; e++)
            {
                int a = edgeA[e];
                int b = edgeB[e];
                float targetLen = edgeTargetLen[e];

                Vector3 delta = cur[b] - cur[a];
                float len = delta.magnitude;
                if (len < 1e-6f) continue;

                float diff = (len - targetLen) / len;
                Vector3 corr = delta * (0.5f * edgeStiffness * diff);
                cur[a] += corr;
                cur[b] -= corr;
            }
        }

        // External pushers: project each vert out of any capsule it has entered. The positional shove
        // alone gives the strand momentum (prev isn't moved), so it swings after the body leaves.
        ApplyPushers();

        // Apply the anchor weight to the final displacement (0 = pinned exactly to rest, so the top
        // edge can't drift) and hard-clamp within maxOffset.
        float maxSqr = maxOffset * maxOffset;
        for (int i = 0; i < n; i++)
        {
            Vector3 off = (cur[i] - restWorldCache[i]) * weight[i];
            if (off.sqrMagnitude > maxSqr)
                off = off.normalized * maxOffset;
            cur[i] = restWorldCache[i] + off;
        }
    }

    private void ApplyPushers()
    {
        int pc = pushP0.Count;
        if (pc == 0) return;

        for (int i = 0; i < cur.Length; i++)
        {
            if (weight[i] <= 0f) continue; // pinned verts never move
            Vector3 v = cur[i];
            for (int j = 0; j < pc; j++)
            {
                float r = pushR[j];
                Vector3 closest = ClosestPointOnSegment(pushP0[j], pushP1[j], v);
                Vector3 d = v - closest;
                float distSqr = d.sqrMagnitude;
                if (distSqr >= r * r) continue;

                float dist = Mathf.Sqrt(distSqr);
                // Inside the capsule: shove out to its surface. If the vert sits exactly on the axis,
                // pick a stable sideways direction so it still parts instead of jittering.
                Vector3 dir = dist > 1e-5f ? d / dist : StableSideways(pushP0[j], pushP1[j]);
                v = closest + dir * r;
            }
            cur[i] = v;
        }
    }

    // Spatial phase per unique vert, so the wind wave travels across the mesh instead of shoving it
    // as one block. windWaveScale is a per-axis spatial frequency; (0,0,0) collapses to uniform wind.
    public void ComputeWindPhases()
    {
        if (uniqueRest == null) return;
        if (windPhase == null || windPhase.Length != uniqueRest.Length)
            windPhase = new float[uniqueRest.Length];
        for (int i = 0; i < uniqueRest.Length; i++)
            windPhase[i] = Vector3.Dot(uniqueRest[i], windWaveScale);
    }

    // alpha (0..1) is the leftover-time fraction between the last two completed sim steps.
    private void WriteBack(float alpha)
    {
        Matrix4x4 w2l = transform.worldToLocalMatrix;
        for (int i = 0; i < renderVerts.Length; i++)
        {
            int u = fullToUnique[i];
            renderVerts[i] = w2l.MultiplyPoint3x4(Vector3.Lerp(prev[u], cur[u], alpha));
        }

        workingMesh.SetVertices(renderVerts);
        if (recalculateNormals) workingMesh.RecalculateNormals();
        // No RecalculateBounds: the mesh keeps the fixed conservative bounds set at startup.
    }

    private void ResetToRest()
    {
        EnsureWorldCache();
        for (int i = 0; i < uniqueRest.Length; i++)
        {
            cur[i] = restWorldCache[i];
            prev[i] = restWorldCache[i];
        }
        accumulator = 0f;
        if (workingMesh != null) workingMesh.SetVertices(restLocalFull);
    }

    // Recompute restWorldCache / edgeTargetLen only when the transform actually changed. Plants sit
    // still their whole life, so in practice this runs once and every later call is a matrix compare.
    private void EnsureWorldCache()
    {
        Matrix4x4 l2w = transform.localToWorldMatrix;
        if (worldCacheValid && l2w == cachedL2W) return;
        for (int i = 0; i < cur.Length; i++)
            restWorldCache[i] = l2w.MultiplyPoint3x4(uniqueRest[i]);
        for (int e = 0; e < edgeA.Length; e++)
            edgeTargetLen[e] = l2w.MultiplyVector(edgeRestDelta[e]).magnitude;
        cachedL2W = l2w;
        worldCacheValid = true;
    }

    // Verts never leave rest ± maxOffset, so the private mesh gets one conservative fixed bound at
    // startup instead of paying RecalculateBounds every frame. maxOffset is world units; divide by
    // the smallest scale axis to expand safely in local space.
    private void ApplyFixedMeshBounds()
    {
        if (workingMesh == null) return;
        Vector3 s = transform.lossyScale;
        float minScale = Mathf.Min(Mathf.Abs(s.x), Mathf.Min(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        Bounds b = restLocalBounds;
        b.Expand(2f * maxOffset / Mathf.Max(1e-4f, minScale));
        workingMesh.bounds = b;
    }

    // Rest-mesh bounds as a world-space sphere — the shared broad phase for waking and pusher culling.
    private void GetWorldBounds(out Vector3 center, out float radius)
    {
        center = transform.TransformPoint(localBoundsCenter);
        Vector3 s = transform.lossyScale;
        radius = localBoundsRadius * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
    }

    // True when any pusher capsule is within (radius + padding + wakeMargin) of an actual movable
    // REST vertex. Two phases: a bounds-sphere test rejects far pushers for pennies; only a pusher
    // inside the bounds pays the per-vertex loop. Vertex-precise on purpose — fish live BETWEEN the
    // lily pads, so a bounds-only test would hold every pad on a fish's route awake all day.
    // Uses the rest pose (recomputed from the live transform), which is exact while asleep and the
    // right question ("would it touch the settled plant?") when deciding whether sleeping is safe.
    private bool AnyPusherTouching()
    {
        var all = PlantPusher.All;
        if (all.Count == 0) return false;
        GetWorldBounds(out Vector3 c, out float br);
        bool cacheReady = false;
        for (int i = 0; i < all.Count; i++)
        {
            PlantPusher p = all[i];
            if (p == null || !p.isActiveAndEnabled || p.impulseOnly) continue; // fish: CheckImpulseTriggers
            p.GetCapsule(out Vector3 a, out Vector3 b, out float r);
            float reach = r + pusherPadding + wakeMargin;

            float broad = reach + br;
            if ((ClosestPointOnSegment(a, b, c) - c).sqrMagnitude >= broad * broad) continue;

            if (!cacheReady) { EnsureWorldCache(); cacheReady = true; }
            float reachSqr = reach * reach;
            for (int v = 0; v < uniqueRest.Length; v++)
            {
                if (weight[v] <= 0f) continue; // pinned verts can't move anyway
                Vector3 w = restWorldCache[v];
                if ((ClosestPointOnSegment(a, b, w) - w).sqrMagnitude < reachSqr) return true;
            }
        }
        return false;
    }

    // Fish-mode contact edge detection. Fires ONE wiggle the first time an impulse pusher touches a
    // movable rest vertex; that pusher is then 'spent' for this plant until it exits the broad
    // sphere (hysteresis — hovering at the touch boundary can't retrigger, and a fish parked on a
    // lily pad costs nothing after its one kick). Returns true when a new wiggle got queued, which
    // is the caller's cue to wake the plant.
    private bool CheckImpulseTriggers()
    {
        var all = PlantPusher.All;
        if (all.Count == 0) return false;

        bool fired = false;
        GetWorldBounds(out Vector3 c, out float br);
        bool cacheReady = false;
        for (int i = 0; i < all.Count; i++)
        {
            PlantPusher p = all[i];
            if (p == null || !p.isActiveAndEnabled || !p.impulseOnly) continue;
            p.GetCapsule(out Vector3 a, out Vector3 b, out float r);
            float reach = r + pusherPadding + wakeMargin;
            float broad = reach + br;
            bool inBroad = (ClosestPointOnSegment(a, b, c) - c).sqrMagnitude < broad * broad;

            if (impulseSpent != null && impulseSpent.Contains(p))
            {
                if (!inBroad) impulseSpent.Remove(p); // left the area — re-armed
                continue;
            }
            if (!inBroad) continue;

            // Narrow phase: only a capsule near an actual movable rest vertex counts as a touch.
            if (!cacheReady) { EnsureWorldCache(); cacheReady = true; }
            float reachSqr = reach * reach;
            bool touch = false;
            for (int v = 0; v < uniqueRest.Length; v++)
            {
                if (weight[v] <= 0f) continue;
                Vector3 w = restWorldCache[v];
                if ((ClosestPointOnSegment(a, b, w) - w).sqrMagnitude < reachSqr) { touch = true; break; }
            }
            if (!touch) continue;

            impulseSpent ??= new HashSet<PlantPusher>();
            impulseSpent.RemoveWhere(x => x == null); // adds are rare; drop destroyed fish here
            impulseSpent.Add(p);
            // Kick radius spans the WHOLE plant (touch reach + bounds diameter): 'first touch' fires
            // at the reach boundary by definition, and the wiggle should read as the plant reacting,
            // not one rim vertex twitching. The falloff (with its floor in ApplyQueuedImpulses)
            // still makes the contact side move the most.
            queuedImpulses.Add(new QueuedImpulse { a = a, b = b, r = reach * 2f + br * 2f, strength = p.impulseStrength });
            impulseRinging = true;
            fired = true;
        }
        return fired;
    }

    // Play-mode sanity check: right-click the component header → 'Test Wiggle' fires a kick from the
    // plant's own centre, no fish needed. Separates "the response is broken/too subtle" from "the
    // trigger never fired" (usually a fish capsule that doesn't actually reach the verts).
    [ContextMenu("Test Wiggle")]
    private void TestWiggle()
    {
        if (!initialized || !Application.isPlaying) return;
        GetWorldBounds(out Vector3 c, out float br);
        queuedImpulses.Add(new QueuedImpulse { a = c, b = c, r = br + maxOffset, strength = 0.3f });
        impulseRinging = true;
        if (asleep)
        {
            if (awakeCount >= MaxAwake) { queuedImpulses.Clear(); return; }
            WakeUp();
        }
    }

    // Convert queued wiggles into outward vertex velocity. Verlet velocity is (cur - prev), so a
    // kick of S world-units/sec means moving prev against the push by S / simulationRate.
    private void ApplyQueuedImpulses()
    {
        if (queuedImpulses.Count == 0) return;
        float invRate = 1f / Mathf.Max(1f, simulationRate);
        for (int q = 0; q < queuedImpulses.Count; q++)
        {
            QueuedImpulse imp = queuedImpulses[q];
            float rSqr = imp.r * imp.r;
            for (int i = 0; i < cur.Length; i++)
            {
                if (weight[i] <= 0f) continue;
                Vector3 closest = ClosestPointOnSegment(imp.a, imp.b, cur[i]);
                Vector3 d = cur[i] - closest;
                float distSqr = d.sqrMagnitude;
                if (distSqr >= rSqr) continue;

                float dist = Mathf.Sqrt(distSqr);
                Vector3 dir = dist > 1e-5f ? d / dist : StableSideways(imp.a, imp.b);
                if (impulseStyle == ImpulseStyle.WaterBob)
                {
                    // Water displaced under a floating plant lifts it: mostly straight up, with a
                    // whisper of horizontal drift away from the body. The falloff gradient makes the
                    // contact side rise first, so the plant TILTS and rocks as the spring rings out
                    // instead of jello-ing sideways.
                    Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                    dir = (Vector3.up + flat * 0.35f).normalized;
                }
                // Floor of 0.35 so the far side of the plant visibly joins the wiggle instead of
                // only the contact zone twitching; the gradient on top keeps the near side dominant.
                float falloff = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, 1f - dist / imp.r));
                prev[i] -= dir * (imp.strength * falloff * weight[i] * invRate);
            }
        }
        queuedImpulses.Clear();
    }

    private void WakeUp()
    {
        asleep = false;
        settleTimer = 0f;
        if (windStrength <= 0f) { awakeCount++; countedAwake = true; } // wind wakes bypass the budget
        // The transform may have moved while asleep; re-seed at the CURRENT rest pose so the first
        // step doesn't read that move as a fling.
        ResetToRest();
        lastPosition = transform.position;
    }

    private void Sleep()
    {
        asleep = true;
        settleTimer = 0f;
        accumulator = 0f;
        queuedImpulses.Clear(); // never carry a stale world-space wiggle into a later wake
        impulseRinging = false;
        if (countedAwake) { awakeCount--; countedAwake = false; }
        workingMesh.SetVertices(restLocalFull);
        if (recalculateNormals) workingMesh.RecalculateNormals();
    }

    // Runs each awake frame after WriteBack: once every vert has hugged its rest position for
    // sleepDelay seconds and nothing is touching, park the exact rest mesh and stop simulating.
    private void TrySleep()
    {
        if (!allowSleep || windStrength > 0f) { settleTimer = 0f; return; }

        float thrSqr = sleepThreshold * sleepThreshold;
        for (int i = 0; i < cur.Length; i++)
        {
            if ((cur[i] - restWorldCache[i]).sqrMagnitude > thrSqr)
            {
                settleTimer = 0f;
                return;
            }
        }
        // Same test (and margin) as waking, so sleep implies the wake check is false next frame —
        // no wake/sleep churn while a pusher hovers at the boundary.
        if (AnyPusherTouching()) { settleTimer = 0f; return; }

        settleTimer += Time.deltaTime;
        if (settleTimer < sleepDelay) return;

        Sleep();
    }

    // Welds duplicate vertices by quantized position, then builds the unique-vertex set and a deduped
    // edge list (mapped to unique indices) from the triangles. Mirrors SoftBodyJiggle.
    private void BuildSimData(Mesh source)
    {
        restLocalFull = source.vertices;
        renderVerts = new Vector3[restLocalFull.Length];

        int n = restLocalFull.Length;
        fullToUnique = new int[n];
        var map = new Dictionary<Vector3Int, int>(n);
        var uniqueList = new List<Vector3>(n);
        float inv = 1f / Mathf.Max(1e-6f, weldEpsilon);

        for (int i = 0; i < n; i++)
        {
            Vector3 p = restLocalFull[i];
            var key = new Vector3Int(
                Mathf.RoundToInt(p.x * inv),
                Mathf.RoundToInt(p.y * inv),
                Mathf.RoundToInt(p.z * inv));

            if (!map.TryGetValue(key, out int u))
            {
                u = uniqueList.Count;
                map.Add(key, u);
                uniqueList.Add(p);
            }
            fullToUnique[i] = u;
        }
        uniqueRest = uniqueList.ToArray();

        int[] tris = source.triangles;
        var edgeSet = new HashSet<long>();
        var ea = new List<int>();
        var eb = new List<int>();
        for (int t = 0; t < tris.Length; t += 3)
        {
            AddEdge(fullToUnique[tris[t]], fullToUnique[tris[t + 1]], edgeSet, ea, eb);
            AddEdge(fullToUnique[tris[t + 1]], fullToUnique[tris[t + 2]], edgeSet, ea, eb);
            AddEdge(fullToUnique[tris[t + 2]], fullToUnique[tris[t]], edgeSet, ea, eb);
        }
        edgeA = ea.ToArray();
        edgeB = eb.ToArray();
        edgeRestDelta = new Vector3[edgeA.Length];
        edgeTargetLen = new float[edgeA.Length];
        for (int e = 0; e < edgeA.Length; e++)
            edgeRestDelta[e] = uniqueRest[edgeB[e]] - uniqueRest[edgeA[e]];

        cur = new Vector3[uniqueRest.Length];
        prev = new Vector3[uniqueRest.Length];
        restWorldCache = new Vector3[uniqueRest.Length];
    }

    // Per-vertex swing weight from the anchored edge: 0 (pinned) within pinFraction of the edge along
    // anchorAxis, blending to 1 (full swing) across falloffFraction. t is 0 at the anchored edge, 1 at
    // the opposite end. With a degenerate axis or extent, everything is free.
    public void ComputeWeights()
    {
        if (uniqueRest == null) return;
        if (weight == null || weight.Length != uniqueRest.Length)
            weight = new float[uniqueRest.Length];

        Vector3 axis = anchorAxis.sqrMagnitude > 1e-6f ? anchorAxis.normalized : Vector3.up;

        float minP = float.MaxValue, maxP = float.MinValue;
        for (int i = 0; i < uniqueRest.Length; i++)
        {
            float proj = Vector3.Dot(uniqueRest[i], axis);
            if (proj < minP) minP = proj;
            if (proj > maxP) maxP = proj;
        }

        float extent = maxP - minP;
        if (extent <= 1e-6f)
        {
            for (int i = 0; i < weight.Length; i++) weight[i] = 1f;
            return;
        }

        float pin = Mathf.Clamp01(pinFraction);
        float fall = Mathf.Max(1e-4f, falloffFraction);
        for (int i = 0; i < weight.Length; i++)
        {
            float proj = Vector3.Dot(uniqueRest[i], axis);
            float t = (maxP - proj) / extent;          // 0 at anchored edge, 1 at far end
            float w = Mathf.Clamp01((t - pin) / fall);
            weight[i] = Mathf.SmoothStep(0f, 1f, w);
        }
    }

    void OnValidate()
    {
        if (initialized)
        {
            ComputeWeights();
            ComputeWindPhases();
            ApplyFixedMeshBounds();
        }
    }

    // Setup aid: paints each vertex by its swing weight so you can dial anchorAxis / pinFraction /
    // falloffFraction visually. RED = pinned (the anchored edge), GREEN = free to swing. Works in edit
    // mode (sampled from the shared mesh at the rest pose) and in play mode (live sim positions).
    void OnDrawGizmosSelected()
    {
        Vector3[] points;
        float[] w;

        if (initialized && uniqueRest != null)
        {
            points = restWorldCache != null && restWorldCache.Length == uniqueRest.Length
                ? restWorldCache : null;
            // restWorldCache is only filled during play; fall back to transforming rest each draw.
            w = weight;
            if (points == null)
            {
                points = new Vector3[uniqueRest.Length];
                for (int i = 0; i < uniqueRest.Length; i++)
                    points[i] = transform.TransformPoint(uniqueRest[i]);
            }
        }
        else
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            Vector3[] verts = mf.sharedMesh.vertices;
            points = new Vector3[verts.Length];
            w = new float[verts.Length];

            Vector3 axis = anchorAxis.sqrMagnitude > 1e-6f ? anchorAxis.normalized : Vector3.up;
            float minP = float.MaxValue, maxP = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                float proj = Vector3.Dot(verts[i], axis);
                if (proj < minP) minP = proj;
                if (proj > maxP) maxP = proj;
            }
            float extent = Mathf.Max(1e-6f, maxP - minP);
            float pin = Mathf.Clamp01(pinFraction);
            float fall = Mathf.Max(1e-4f, falloffFraction);
            for (int i = 0; i < verts.Length; i++)
            {
                points[i] = transform.TransformPoint(verts[i]);
                float t = (maxP - Vector3.Dot(verts[i], axis)) / extent;
                w[i] = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - pin) / fall));
            }
        }

        if (points == null || w == null) return;
        float size = HandleSize();
        for (int i = 0; i < points.Length && i < w.Length; i++)
        {
            Gizmos.color = Color.Lerp(Color.red, Color.green, w[i]);
            Gizmos.DrawSphere(points[i], size);
        }
    }

    private float HandleSize()
    {
        var mf = GetComponent<MeshFilter>();
        Mesh m = initialized ? workingMesh : (mf != null ? mf.sharedMesh : null);
        float s = m != null ? m.bounds.extents.magnitude : 1f;
        return Mathf.Max(0.01f, s * 0.02f * transform.lossyScale.magnitude * 0.5f);
    }

    private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float lenSqr = ab.sqrMagnitude;
        if (lenSqr < 1e-10f) return a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSqr);
        return a + ab * t;
    }

    private static Vector3 StableSideways(Vector3 a, Vector3 b)
    {
        Vector3 axis = b - a;
        if (axis.sqrMagnitude < 1e-10f) return Vector3.right;
        Vector3 side = Vector3.Cross(axis.normalized, Vector3.forward);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(axis.normalized, Vector3.up);
        return side.normalized;
    }

    private static void AddEdge(int a, int b, HashSet<long> set, List<int> ea, List<int> eb)
    {
        if (a == b) return;
        int lo = Mathf.Min(a, b);
        int hi = Mathf.Max(a, b);
        long key = ((long)lo << 32) | (uint)hi;
        if (set.Add(key))
        {
            ea.Add(lo);
            eb.Add(hi);
        }
    }
}
