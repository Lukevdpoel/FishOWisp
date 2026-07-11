using System.Collections.Generic;
using UnityEngine;

// Per-vertex soft-body jiggle for an UNSKINNED MeshFilter (Option E: Verlet on the mesh).
//
// Runs a Verlet simulation in WORLD space: each (welded) vertex carries inertia and is pulled back
// toward its rest position — which itself rides this object's transform. So parent movement, the
// charge-squash on playerModel, and the ball-morph scale on normalMeshRoot all feed in automatically;
// no acceleration math is needed. Structural edge constraints keep the surface coherent so it reads as
// a squishy jelly rather than independent points.
//
// Layering with the Animator: this writes ONLY to the mesh (vertex data). The Animator on this object's
// ancestors only animates transforms, so there is no conflict — the jiggle composes underneath the
// animated scale, exactly like a vertex-shader wobble would. Mirrors the project's existing pattern of
// layering procedural deformation on top of the evaluated Animator (see PlayerSquashStretch).
//
// Execution order 500: after PlayerController (squash/stretch + tumble write the transform at order 0)
// and before VerletRope (order 1000) samples rodTip/bobber from the world.
[DefaultExecutionOrder(500)]
[RequireComponent(typeof(MeshFilter))]
public class SoftBodyJiggle : MonoBehaviour
{
    [Header("Feel")]
    [Tooltip("How strongly each vertex springs back to its rest shape each frame (0 = floppy, 1 = rigid/snappy).")]
    [Range(0f, 1f)] public float stiffness = 0.2f;
    [Tooltip("Velocity bled off each frame (0 = wobbles forever, 1 = dead stop). Higher settles faster.")]
    [Range(0f, 1f)] public float damping = 0.08f;
    [Tooltip("Structural edge-constraint passes per frame. 1-2 reads as soft jelly; more stiffens the surface.")]
    [Range(0, 8)] public int constraintIterations = 2;
    [Tooltip("How tightly edges hold their rest length each pass (0 = stretchy, 1 = firm).")]
    [Range(0f, 1f)] public float edgeStiffness = 0.5f;

    [Header("Simulation")]
    [Tooltip("Fixed simulation rate (Hz). The sim always advances in steps of 1/this regardless of framerate, so the wobble feels identical at 30, 60 or 144 FPS and a lag spike can't destabilise it. 60 is a good match for most targets.")]
    public float simulationRate = 60f;
    [Tooltip("Max fixed steps run in a single frame. This is a last-resort spiral-of-death guard, NOT a normal-case cap: it sets the lowest framerate the sim still tracks in real time at = simulationRate / maxSubSteps (e.g. 60Hz / 8 = 7.5 FPS). Below that the sim drops time and runs in slow-motion, which desyncs the wobble from the body and looks broken on fast transients like the bounce — so keep this high enough that it never engages at framerates you actually ship at. Steps are cheap on this mesh; don't starve it.")]
    [Range(1, 16)] public int maxSubSteps = 8;

    [Header("Sprint Damping")]
    [Tooltip("Scale the jiggle down while the player sprints (auto-reads PlayerController.IsSprinting from a parent). Running drives bigger accelerations, so this tames the extra wobble without touching the walk feel.")]
    public bool dampenWhileSprinting = true;
    [Tooltip("Wobble amount while sprinting (1 = no reduction, 0 = fully still). Only the visible amplitude is scaled — the sim itself is unchanged.")]
    [Range(0f, 1f)] public float sprintIntensity = 0.6f;
    [Tooltip("Seconds to blend between full and reduced wobble as sprint starts/stops, so it doesn't pop. Also used as the blend time for the airborne damping below.")]
    public float sprintBlendTime = 0.15f;

    [Header("Air Damping")]
    [Tooltip("Scale the jiggle down while the player is airborne (jump flight / falling; NOT while charging on the ground). The launch already elongates the body along its velocity — taming the secondary surface wobble midair keeps the eye on that forward pull instead of the rippling.")]
    public bool dampenWhileAirborne = true;
    [Tooltip("Wobble amount while airborne (1 = no reduction, 0 = fully still). Only the visible amplitude is scaled — the sim itself is unchanged, so the landing wobble is unaffected.")]
    [Range(0f, 1f)] public float airIntensity = 0.35f;

    [Header("Limits & Safety")]
    [Tooltip("Max distance (world units) a vertex may sit from its rest position. Bounds the wobble and stops a frame hitch from blowing the mesh apart.")]
    public float maxOffset = 0.5f;
    [Tooltip("If this object moves more than this in one frame (glide / drive / scene load), the sim snaps to rest instead of flinging.")]
    public float teleportThreshold = 3f;
    [Tooltip("Recompute smooth normals each frame so lighting follows the deformation. Off is cheaper but lighting stays at the rest shape.")]
    public bool recalculateNormals = true;
    [Tooltip("Vertices closer than this are welded into one sim point, so UV/normal seams don't tear apart while jiggling.")]
    public float weldEpsilon = 1e-4f;

    [Header("Seam Anchoring")]
    // For a character built from separate meshes that join at the body, vertices near a joint must stay
    // glued to their animated rest pose so the seam can't gap. Pin them here: full pin (no jiggle) inside
    // anchorRadius, blending back to full jiggle across anchorFalloff. Pin the SAME joint on both meshes
    // that meet there. Distances are in the mesh's local units. Leave empty for a free-floating part.
    [Tooltip("Pin the local origin — the joint a limb rotates about. Zero-setup anchor for a limb whose pivot sits at its attachment.")]
    public bool anchorPivot = false;
    [Tooltip("Extra anchor centers (e.g. the body's shoulder/hip/neck sockets). Sampled into local space once at startup.")]
    public Transform[] anchors;
    [Tooltip("Local-space distance within which vertices are fully pinned to the animated rest pose (no jiggle).")]
    public float anchorRadius = 0.15f;
    [Tooltip("Local-space distance over which a pinned vertex blends back up to full jiggle.")]
    public float anchorFalloff = 0.25f;

    [Header("Ground Conform")]
    // While the player charges a jump the body squashes flat and — because the squash preserves volume —
    // WIDENS. A rigid wide disc can't follow a curved surface: over a rounded rock its rim juts out past
    // where the ground falls away and hangs in the air. This drapes the render mesh over the ground
    // beneath it each frame: a small grid of downward rays samples the ground height under the footprint,
    // then every vertex is shifted in world-Y toward that height (referenced to the height under the
    // footprint centre, so flat ground = no change). The rim tucks down to hug convex bumps and settles
    // into dips instead of floating. Cost is the ray grid + a per-vertex bilinear lookup, and the whole
    // pass only runs while charging (auto-read from the parent PlayerController), so normal play pays nothing.
    [Tooltip("Drape the mesh over the ground surface while the player charges a jump, so the flattened/widened body follows curves instead of hanging flat over the edge of a rounded surface.")]
    public bool conformToGround = true;
    [Tooltip("Which collider layers the drape responds to — pick your ground/terrain layer(s) here. The player's own colliders are always skipped automatically. Left empty (Nothing), the conform is inactive; set at least one layer to enable it.")]
    public LayerMask conformGroundMask = 0;
    [Tooltip("Resolution of the square ray grid cast under the footprint each frame (N×N rays). 3-5 is plenty for smooth curves; higher tracks bumpier ground at more rays.")]
    [Range(2, 8)] public int conformResolution = 4;
    [Tooltip("Overall drape amount. 1 = fully follow the ground contour, 0 = off (stays flat). Dial down for a subtler conform.")]
    [Range(0f, 1f)] public float conformStrength = 1f;
    [Tooltip("Furthest a vertex may be pulled DOWN to follow ground that falls away under it (world units). Bounds the drape so a vertex overhanging a ledge can't be yanked far down and tear the mesh.")]
    public float conformMaxDrop = 0.35f;
    [Tooltip("Furthest a vertex may be pushed UP to follow ground that rises under it (world units).")]
    public float conformMaxRise = 0.25f;
    [Tooltip("Extra world-space padding around the mesh footprint when placing the ray grid, so the rim still samples ground just beyond the silhouette.")]
    public float conformPadding = 0.05f;
    [Tooltip("How quickly the drape follows changes in the ground beneath the body (per second). Turning during the charge swings the footprint over new ground; this eases every vertex's height toward its new target instead of snapping it there in one frame. Lower = softer and laggier, higher = snappier; very high approximates the old instant behaviour.")]
    public float conformEaseSpeed = 10f;
    [Tooltip("How much of the drape reaches the TOP of the body (1 = the top folds with the ground exactly like the base does, 0 = the top stays rigid and only the base falls into place). Vertices blend smoothly from full drape at the base up to this at the crown, so the underside hugs the surface while the upper body only softly follows.")]
    [Range(0f, 1f)] public float conformTopAmount = 0.35f;
    // NOTE: this pass only BENDS the body mesh to the ground's curvature. Dropping the whole body onto the
    // surface (the hover fix) is done one level up in PlayerSquashStretch (keep-base-planted on squashRoot),
    // so the separate limb meshes drop together with the body instead of being left behind by a body-only warp.

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh workingMesh;

    private Vector3[] restLocalFull;   // exact rest positions of every render vertex (local space)
    private Vector3[] renderVerts;     // scratch written to the mesh each frame

    // Welded ("unique") simulation set — duplicate verts at a shared position collapse to one point.
    private Vector3[] uniqueRest;      // rest position per unique vert, local space
    private Vector3[] cur;             // current sim position, world space
    private Vector3[] prev;            // previous sim position, world space
    private Vector3[] restWorldCache;  // per-frame rest world positions (computed once, reused)
    private int[] fullToUnique;        // render-vertex index -> unique index
    private float[] weight;            // per unique vert jiggle weight: 0 = pinned to rest, 1 = full jiggle

    // Structural edges between unique verts. restDelta (local a->b) is re-transformed each frame so
    // edge target lengths track the animated scale/rotation of the body.
    private int[] edgeA;
    private int[] edgeB;
    private Vector3[] edgeRestDelta;
    private float[] edgeTargetLen;     // rest length under the current frame's transform, recomputed once per frame

    private Vector3 lastPosition;
    private float accumulator;          // unspent real time, drained in fixed simulationRate steps
    private PlayerController sprintSource;
    private float currentIntensity = 1f; // smoothed wobble amplitude scale (1 = full, sprintIntensity while running)
    private bool initialized;

    // --- Ground conform ---
    private float[] conformGridH;        // ground height per grid cell (row-major z*N+x), NaN-filled misses replaced
    private int conformGridN;            // grid resolution used this frame
    private float conformMinX, conformMinZ, conformInvCellX, conformInvCellZ; // world->grid mapping
    private float conformRefY;           // ground height under the footprint centre — the contour drape zero point
    private bool conformActiveThisFrame; // grid sampled and ground found this frame
    private float[] conformOffset;       // smoothed drape height per UNIQUE vertex — the memory that makes ground changes ease in instead of snapping
    private bool conformOffsetLive;      // any offsets nonzero (lets the ease-out loop stop once fully settled)
    private float[] conformHeight01;     // per-unique-vertex normalized height in the rest mesh (0 = base, 1 = crown), drives the top falloff
    private Transform playerRoot;        // conform rays skip any collider under this (the player itself)
    private static readonly RaycastHit[] conformHitBuffer = new RaycastHit[8];

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        sprintSource = GetComponentInParent<PlayerController>();
        // Conform rays start above the flattened body and travel down THROUGH the player's own colliders
        // to reach the ground; remember the player root so those self-hits can be skipped.
        playerRoot = sprintSource != null ? sprintSource.transform : transform.root;

        Mesh source = meshFilter.sharedMesh;
        if (source == null)
        {
            Debug.LogWarning($"{nameof(SoftBodyJiggle)} on '{name}' has no mesh — disabling.", this);
            enabled = false;
            return;
        }

        // CPU vertex deformation needs a readable mesh; imported models have this off by default.
        if (!source.isReadable)
        {
            Debug.LogError(
                $"{nameof(SoftBodyJiggle)} on '{name}': mesh '{source.name}' is not readable — disabling. " +
                "Enable it in the model's import settings: Inspector → Model → Meshes → 'Read/Write Enabled' → Apply.",
                this);
            enabled = false;
            return;
        }

        // Private writable copy so we never mutate the shared asset; dynamic for frequent updates.
        workingMesh = Instantiate(source);
        workingMesh.name = source.name + " (Jiggle)";
        workingMesh.MarkDynamic();
        meshFilter.mesh = workingMesh;

        BuildSimData(source);
        ResetToRest();
        lastPosition = transform.position;
        initialized = true;
    }

    void OnDisable()
    {
        // Leave the mesh at its clean rest pose when the component is switched off in play/editor.
        if (initialized && workingMesh != null)
        {
            workingMesh.SetVertices(restLocalFull);
            workingMesh.RecalculateNormals();
            workingMesh.RecalculateBounds();
        }
    }

    void OnDestroy()
    {
        if (workingMesh != null) Destroy(workingMesh);
    }

    void LateUpdate()
    {
        if (!initialized) return;
        conformActiveThisFrame = false; // recomputed below; stays off unless charging over ground
        // Freeze with the game (pause / notebook), matching PlayerController.LateUpdate's guard.
        if (Time.timeScale == 0f || Time.deltaTime <= 0.0001f) return;

        // While the body is hidden (ball-morph in/flight) keep the sim parked at rest, so it reappears
        // clean and the landing bounce starts its jiggle from neutral instead of mid-wobble.
        if (meshRenderer != null && !meshRenderer.enabled)
        {
            ResetToRest();
            lastPosition = transform.position;
            return;
        }

        // Teleport guard: a glide / drive / scene move shouldn't fling the mesh across the screen.
        if ((transform.position - lastPosition).sqrMagnitude > teleportThreshold * teleportThreshold)
        {
            ResetToRest();
            lastPosition = transform.position;
            return;
        }
        lastPosition = transform.position;

        // Sample the ground under the flattened footprint ONCE per frame (independent of how many fixed
        // sim sub-steps run below), so WriteBack can drape the mesh onto it. Only while charging — the
        // one time the body is squashed wide enough to overhang a curved surface — so it's free otherwise.
        if (conformToGround && conformGroundMask.value != 0 && sprintSource != null && sprintSource.IsChargingJump)
            SampleGroundGrid();

        // Ease the wobble amplitude toward its target so the change blends in smoothly. Airborne damping
        // outranks sprint damping (you can't sprint midair); charging is deliberately NOT airborne — the
        // grounded flatten keeps its full wobble, only the flight is calmed so the velocity stretch leads.
        float targetIntensity = 1f;
        if (dampenWhileAirborne && sprintSource != null
            && !sprintSource.IsGrounded && !sprintSource.IsChargingJump)
            targetIntensity = airIntensity;
        else if (dampenWhileSprinting && sprintSource != null && sprintSource.IsSprinting)
            targetIntensity = sprintIntensity;
        float blendRate = sprintBlendTime > 0f ? Time.deltaTime / sprintBlendTime : 1f;
        currentIntensity = Mathf.MoveTowards(currentIntensity, targetIntensity, blendRate);

        // Fixed-timestep accumulator: advance the sim in constant 1/simulationRate steps so it never
        // depends on framerate. Capping the accumulator at maxSubSteps means a lag spike just advances
        // a little less this frame instead of taking one huge, unstable step — no post-spike jitter.
        float fixedDt = 1f / Mathf.Max(1f, simulationRate);
        accumulator = Mathf.Min(accumulator + Time.deltaTime, maxSubSteps * fixedDt);
        if (accumulator >= fixedDt)
        {
            PrepareFrame();
            while (accumulator >= fixedDt)
            {
                Step();
                accumulator -= fixedDt;
            }
        }

        // Render the state interpolated between the last two sim steps by the leftover time, so the
        // mesh stays smooth even when steps don't line up with frames (e.g. high framerate).
        WriteBack(accumulator / fixedDt);
    }

    // Per-frame setup that depends only on the (frame-constant) transform: rest world positions and
    // edge target lengths. Resolved once here, then reused by every fixed sub-step this frame.
    private void PrepareFrame()
    {
        // One cached matrix instead of a native TransformPoint call per vertex — same math,
        // and these per-vertex loops are most of the component's frame cost.
        Matrix4x4 l2w = transform.localToWorldMatrix;
        for (int i = 0; i < cur.Length; i++)
            restWorldCache[i] = l2w.MultiplyPoint3x4(uniqueRest[i]);
        for (int e = 0; e < edgeA.Length; e++)
            edgeTargetLen[e] = l2w.MultiplyVector(edgeRestDelta[e]).magnitude;
    }

    // One fixed-timestep Verlet step. stiffness/damping are per-step, so a fixed step rate keeps the
    // feel framerate-independent.
    private void Step()
    {
        int n = cur.Length;
        float keep = 1f - damping;

        // Verlet integrate with inertia, then a soft spring pull back toward the rest pose.
        for (int i = 0; i < n; i++)
        {
            Vector3 vel = (cur[i] - prev[i]) * keep;
            prev[i] = cur[i];
            Vector3 p = cur[i] + vel;
            // Anchored verts (weight -> 0) pull fully to rest, so they sit at the animated pose before
            // the constraint pass and act as fixed points the rest of the surface hangs off.
            float s = Mathf.Lerp(1f, stiffness, weight[i]);
            p = Vector3.Lerp(p, restWorldCache[i], s);
            cur[i] = p;
        }

        // Structural edge constraints keep the surface coherent (jelly, not confetti). Edge target
        // lengths were resolved for this frame in PrepareFrame.
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

        // Apply the anchor weight to the final displacement (0 = pinned exactly to the animated rest
        // pose, so seams can't gap) and hard-clamp within maxOffset — bounds the wobble and makes a
        // frame hitch or bad tuning impossible to blow the mesh up.
        float maxSqr = maxOffset * maxOffset;
        for (int i = 0; i < n; i++)
        {
            Vector3 off = (cur[i] - restWorldCache[i]) * weight[i];
            if (off.sqrMagnitude > maxSqr)
                off = off.normalized * maxOffset;
            cur[i] = restWorldCache[i] + off;
        }
    }

    // alpha (0..1) is the leftover-time fraction between the last two completed sim steps; prev holds
    // the position one step back, so lerping prev->cur renders the in-between state for smooth motion.
    private void WriteBack(float alpha)
    {
        bool full = currentIntensity >= 0.999f;

        // Ground conform, with per-vertex memory: each unique vertex's drape height EASES toward the height
        // the ground currently asks for, instead of snapping there in one frame. Turning during the charge
        // swings the footprint over new ground and re-targets every vertex at once — without the ease that
        // read as an instant, unpolished pop. The whole-body drop onto the surface is handled at the
        // transform level in PlayerSquashStretch; this only bends the body to the ground's curvature.
        if (conformActiveThisFrame)
        {
            float k = Mathf.Clamp01(conformEaseSpeed * Time.deltaTime);
            for (int u = 0; u < cur.Length; u++)
            {
                Vector3 w = Vector3.Lerp(prev[u], cur[u], alpha);
                float target = Mathf.Clamp(
                    (SampleGroundHeight(w.x, w.z) - conformRefY) * conformStrength,
                    -conformMaxDrop, conformMaxRise);
                // Vertical falloff: the base takes the full drape so it falls into place on the surface,
                // higher vertices take progressively less (down to conformTopAmount at the crown) so the
                // top half squashes along softly instead of folding with every bump the underside hugs.
                target *= Mathf.Lerp(1f, conformTopAmount, Mathf.SmoothStep(0f, 1f, conformHeight01[u]));
                conformOffset[u] = Mathf.Lerp(conformOffset[u], target, k);
            }
            conformOffsetLive = true;
        }
        else if (conformOffsetLive)
        {
            // Conform just ended (charge released/cancelled with the body still visible) — ease the drape
            // back out to flat with the same response, so the exit doesn't pop either.
            float k = Mathf.Clamp01(conformEaseSpeed * Time.deltaTime);
            bool any = false;
            for (int u = 0; u < cur.Length; u++)
            {
                float v = Mathf.Lerp(conformOffset[u], 0f, k);
                if (Mathf.Abs(v) < 1e-4f) v = 0f; else any = true;
                conformOffset[u] = v;
            }
            conformOffsetLive = any;
        }

        Matrix4x4 w2l = transform.worldToLocalMatrix;
        for (int i = 0; i < renderVerts.Length; i++)
        {
            int u = fullToUnique[i];
            Vector3 world = Vector3.Lerp(prev[u], cur[u], alpha);
            // A pure render-space warp on top of the jiggle, so the sim stays stable and unaware of it.
            if (conformOffsetLive)
                world.y += conformOffset[u];
            Vector3 deformedLocal = w2l.MultiplyPoint3x4(world);
            // Sprint damping: blend the deformation back toward the rest shape to shrink the visible
            // wobble amplitude, leaving the simulation itself untouched.
            renderVerts[i] = full ? deformedLocal : Vector3.Lerp(restLocalFull[i], deformedLocal, currentIntensity);
        }

        workingMesh.SetVertices(renderVerts);
        if (recalculateNormals) workingMesh.RecalculateNormals();
        workingMesh.RecalculateBounds();
    }

    // Casts the N×N downward ray grid over the mesh's current world footprint and stores the ground
    // heights for bilinear lookup in WriteBack. Sets conformActiveThisFrame only if at least one ray
    // found ground (else there's nothing to drape onto — e.g. charging out over a ledge). Runs once per
    // frame while charging; every array here is reused so the pass allocates nothing after warm-up.
    private void SampleGroundGrid()
    {
        if (meshRenderer == null) return;

        int n = Mathf.Max(2, conformResolution);
        if (conformGridH == null || conformGridH.Length != n * n)
            conformGridH = new float[n * n];
        conformGridN = n;

        Bounds b = meshRenderer.bounds;
        float minX = b.min.x - conformPadding;
        float minZ = b.min.z - conformPadding;
        float spanX = Mathf.Max(1e-4f, (b.max.x + conformPadding) - minX);
        float spanZ = Mathf.Max(1e-4f, (b.max.z + conformPadding) - minZ);
        conformMinX = minX;
        conformMinZ = minZ;
        conformInvCellX = (n - 1) / spanX;
        conformInvCellZ = (n - 1) / spanZ;

        // Start each ray above the body and reach below its base far enough to still catch ground the
        // drape is allowed to pull down onto.
        float rayTop = b.max.y + 0.5f;
        float rayLen = (rayTop - b.min.y) + conformMaxDrop + 0.5f;

        float sum = 0f;
        int hits = 0;
        for (int iz = 0; iz < n; iz++)
        {
            float z = minZ + spanZ * (iz / (float)(n - 1));
            for (int ix = 0; ix < n; ix++)
            {
                float x = minX + spanX * (ix / (float)(n - 1));
                if (RaycastGround(x, rayTop, z, rayLen, out float h))
                {
                    conformGridH[iz * n + ix] = h;
                    sum += h;
                    hits++;
                }
                else
                {
                    conformGridH[iz * n + ix] = float.NaN;
                }
            }
        }

        if (hits == 0) return; // no ground under the footprint — leave conform off this frame

        // Fill missed cells with the average hit so the bilinear lookup never reads a NaN.
        float avg = sum / hits;
        for (int i = 0; i < conformGridH.Length; i++)
            if (float.IsNaN(conformGridH[i])) conformGridH[i] = avg;

        conformRefY = SampleGroundHeight(b.center.x, b.center.z);
        conformActiveThisFrame = true;
    }

    // Nearest ground hit under (x,z), skipping the player's own colliders. The ray travels down from
    // above the body, so among the non-player hits the highest (max y) is the surface it rests on.
    private bool RaycastGround(float x, float topY, float z, float length, out float height)
    {
        height = 0f;
        int count = Physics.RaycastNonAlloc(
            new Vector3(x, topY, z), Vector3.down, conformHitBuffer, length,
            conformGroundMask, QueryTriggerInteraction.Ignore);

        float best = float.NegativeInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider col = conformHitBuffer[i].collider;
            if (col == null) continue;
            if (playerRoot != null && col.transform.IsChildOf(playerRoot)) continue; // skip self
            float y = conformHitBuffer[i].point.y;
            if (y > best) { best = y; found = true; }
        }
        if (found) height = best;
        return found;
    }

    // Bilinear ground height at world (x,z) from the sampled grid.
    private float SampleGroundHeight(float x, float z)
    {
        int n = conformGridN;
        if (conformGridH == null || n < 2) return conformRefY;

        float fx = Mathf.Clamp((x - conformMinX) * conformInvCellX, 0f, n - 1.0001f);
        float fz = Mathf.Clamp((z - conformMinZ) * conformInvCellZ, 0f, n - 1.0001f);
        int x0 = (int)fx, z0 = (int)fz;
        int x1 = Mathf.Min(x0 + 1, n - 1), z1 = Mathf.Min(z0 + 1, n - 1);
        float tx = fx - x0, tz = fz - z0;

        float top = Mathf.Lerp(conformGridH[z0 * n + x0], conformGridH[z0 * n + x1], tx);
        float bot = Mathf.Lerp(conformGridH[z1 * n + x0], conformGridH[z1 * n + x1], tx);
        return Mathf.Lerp(top, bot, tz);
    }

    private void ResetToRest()
    {
        Matrix4x4 l2w = transform.localToWorldMatrix;
        for (int i = 0; i < uniqueRest.Length; i++)
        {
            Vector3 w = l2w.MultiplyPoint3x4(uniqueRest[i]);
            cur[i] = w;
            prev[i] = w;
        }
        accumulator = 0f;   // drop unspent time so re-enabling doesn't burst a batch of steps
        // Drop any lingering drape too — the body is hidden/teleporting, so easing it out is meaningless
        // and stale offsets must not reapply when it reappears somewhere else.
        if (conformOffset != null)
            for (int i = 0; i < conformOffset.Length; i++) conformOffset[i] = 0f;
        conformOffsetLive = false;
        if (workingMesh != null) workingMesh.SetVertices(restLocalFull);
    }

    // Welds duplicate vertices by quantized position, then builds the unique-vertex set and a deduped
    // edge list (mapped to unique indices) from the triangles.
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

        // Edges from triangles, deduplicated and mapped onto unique indices.
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
        conformOffset = new float[uniqueRest.Length];

        // Normalized rest height per vertex (0 = base, 1 = crown), for the drape's vertical falloff.
        // Relative position within the body is scale-independent, so the squash doesn't invalidate it.
        conformHeight01 = new float[uniqueRest.Length];
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        for (int i = 0; i < uniqueRest.Length; i++)
        {
            float y = uniqueRest[i].y;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        float invSpan = 1f / Mathf.Max(1e-4f, maxY - minY);
        for (int i = 0; i < uniqueRest.Length; i++)
            conformHeight01[i] = (uniqueRest[i].y - minY) * invSpan;

        ComputeWeights();
    }

    // Per-vertex jiggle weight from the anchor centers: fully pinned (0) within anchorRadius, smoothly
    // back to full jiggle (1) past the falloff band. With no anchors every vertex is free (1).
    // Anchor transforms are sampled into this mesh's local space here (once at startup, recomputable).
    public void ComputeWeights()
    {
        if (uniqueRest == null) return;
        if (weight == null || weight.Length != uniqueRest.Length)
            weight = new float[uniqueRest.Length];

        var centers = new List<Vector3>();
        if (anchorPivot) centers.Add(Vector3.zero);
        if (anchors != null)
            foreach (var a in anchors)
                if (a != null) centers.Add(transform.InverseTransformPoint(a.position));

        if (centers.Count == 0)
        {
            for (int i = 0; i < weight.Length; i++) weight[i] = 1f;
            return;
        }

        float r = Mathf.Max(0f, anchorRadius);
        float f = Mathf.Max(1e-4f, anchorFalloff);
        for (int i = 0; i < weight.Length; i++)
        {
            float nearest = float.MaxValue;
            for (int c = 0; c < centers.Count; c++)
            {
                float d = (uniqueRest[i] - centers[c]).sqrMagnitude;
                if (d < nearest) nearest = d;
            }
            float t = Mathf.Clamp01((Mathf.Sqrt(nearest) - r) / f);
            weight[i] = Mathf.SmoothStep(0f, 1f, t);
        }
    }

    void OnValidate()
    {
        // Let anchor radius/falloff be tuned live in play mode.
        if (initialized) ComputeWeights();
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
