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
    [Tooltip("Max fixed steps run in a single frame. Caps catch-up after a lag spike so a stutter can't fire a burst of steps (or a feedback spiral) — the sim just advances a little less that frame and stays stable.")]
    [Range(1, 16)] public int maxSubSteps = 4;

    [Header("Sprint Damping")]
    [Tooltip("Scale the jiggle down while the player sprints (auto-reads PlayerController.IsSprinting from a parent). Running drives bigger accelerations, so this tames the extra wobble without touching the walk feel.")]
    public bool dampenWhileSprinting = true;
    [Tooltip("Wobble amount while sprinting (1 = no reduction, 0 = fully still). Only the visible amplitude is scaled — the sim itself is unchanged.")]
    [Range(0f, 1f)] public float sprintIntensity = 0.6f;
    [Tooltip("Seconds to blend between full and reduced wobble as sprint starts/stops, so it doesn't pop.")]
    public float sprintBlendTime = 0.15f;

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

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        sprintSource = GetComponentInParent<PlayerController>();

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

        // Ease the wobble amplitude toward its sprint/normal target so the change blends in smoothly.
        float targetIntensity = (dampenWhileSprinting && sprintSource != null && sprintSource.IsSprinting)
            ? sprintIntensity : 1f;
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
        for (int i = 0; i < cur.Length; i++)
            restWorldCache[i] = transform.TransformPoint(uniqueRest[i]);
        for (int e = 0; e < edgeA.Length; e++)
            edgeTargetLen[e] = transform.TransformVector(edgeRestDelta[e]).magnitude;
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
        for (int i = 0; i < renderVerts.Length; i++)
        {
            int u = fullToUnique[i];
            Vector3 deformedLocal = transform.InverseTransformPoint(Vector3.Lerp(prev[u], cur[u], alpha));
            // Sprint damping: blend the deformation back toward the rest shape to shrink the visible
            // wobble amplitude, leaving the simulation itself untouched.
            renderVerts[i] = full ? deformedLocal : Vector3.Lerp(restLocalFull[i], deformedLocal, currentIntensity);
        }

        workingMesh.SetVertices(renderVerts);
        if (recalculateNormals) workingMesh.RecalculateNormals();
        workingMesh.RecalculateBounds();
    }

    private void ResetToRest()
    {
        for (int i = 0; i < uniqueRest.Length; i++)
        {
            Vector3 w = transform.TransformPoint(uniqueRest[i]);
            cur[i] = w;
            prev[i] = w;
        }
        accumulator = 0f;   // drop unspent time so re-enabling doesn't burst a batch of steps
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
