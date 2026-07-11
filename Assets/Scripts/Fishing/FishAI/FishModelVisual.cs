using UnityEngine;
using UnityEngine.Rendering;

// The underwater body of a zone fish: instantiates the species' fishPrefab under the
// FishRipple host so the model swims along with the surface ripple. Swimming fish are
// always rendered as a flat, unlit black silhouette — the player only ever sees a fish in
// full color once it's out of the water (inventory, encyclopedia, tank, photo studio).
//
// When the assigned silhouette material uses the "FishOWisp/Fish Silhouette Sway" shader,
// each fish gets its own material instance carrying the species' swim parameters, the spine
// bounds measured from the mesh, and a script-driven swim clock — and the body is skinned
// onto a TRAILING CHAIN: a row of spine points where the head is pinned to the steered head
// position and each point behind trails the one in front at fixed spacing. Follow-through,
// turn lag and whip all emerge from the chain instead of being estimated from yaw rates.
// The chain lives in world space, so when the host turns, the body genuinely stays on the
// path the head carved. Points are uploaded per fish via MaterialPropertyBlock.
//
// Owned by FishRipple, which calls Spawn() from Initialize() and Tick() from Update().
public class FishModelVisual
{
    private const int ChainCount = 8;
    private const float IntensityResponse = 4f;

    // World-space distance from the host origin to the head along the host's forward, so
    // FishRipple can steer the head as the moving agent. Assumes the project's
    // head-at-min-Z convention under the default 180° model rotation offset.
    public float HeadForwardOffset { get; private set; }

    // World position of the rendered snout — the front-most point of the model, found by
    // scanning the body mesh. The chain pins the head station at chainWorld[0], so the xz
    // comes from the chain and the height from the snout's transformed mesh point. Used to
    // park the fishing line's attach point on a hooked fish's mouth.
    public Vector3 MouthWorldPosition
    {
        get
        {
            if (bodyTransform == null) return host.position;
            Vector3 snout = bodyTransform.TransformPoint(snoutLocalPosition);
            if (swayInstance != null || cpuSwayActive)
                return new Vector3(chainWorld[0].x, snout.y, chainWorld[0].z);
            return snout;
        }
    }

    // World-space centre of the rendered fish body (combined renderer bounds). The host pivot is
    // NOT the body centre — the mesh sits forward of it — so anything that wants to float "straight
    // above the fish" (the interest '!'/'...' indicators) must anchor here, or it swings off to the
    // side whenever the fish yaws. Falls back to the host origin before the model exists.
    public Vector3 VisualCenterWorldPosition
    {
        get
        {
            if (modelRenderers == null) return host.position;
            bool has = false;
            Bounds b = default;
            for (int i = 0; i < modelRenderers.Length; i++)
            {
                if (modelRenderers[i] == null) continue;
                if (!has) { b = modelRenderers[i].bounds; has = true; }
                else b.Encapsulate(modelRenderers[i].bounds);
            }
            return has ? b.center : host.position;
        }
    }

    private Vector3 snoutLocalPosition;

    private readonly Transform host;
    private GameObject modelInstance;
    // Every renderer on the spawned model, cached at spawn so VisualCenterWorldPosition doesn't
    // allocate a GetComponentsInChildren array each frame.
    private Renderer[] modelRenderers;

    private Material silhouetteOverride;
    private static Material sharedFallbackSilhouette;

    // Per-renderer state captured just before the silhouette override is applied, so a caught
    // fish can be revealed in its true colors (catch inspection) by restoring exactly what the
    // prefab shipped with — materials, shadows and probe settings alike.
    private struct OriginalRendererState
    {
        public Renderer renderer;
        public Material[] materials;
        public ShadowCastingMode shadowMode;
        public bool receiveShadows;
        public LightProbeUsage probeUsage;
        public ReflectionProbeUsage reflectionUsage;
    }
    private OriginalRendererState[] originalStates;

    // While true the trailing chain follows the host's TRUE forward instead of the flattened
    // horizontal one — used when a caught fish hangs nose-up from the line, so the chain (and
    // the body skinned onto it) dangles straight down instead of degenerating at the vertical.
    private bool verticalHang;
    public void SetVerticalHang(bool value) => verticalHang = value;

    // Per-fish instance of the sway material. Null when the override isn't the sway shader,
    // in which case the override (or the flat-black fallback) is used shared, as before.
    private Material swayInstance;
    private float swimPhase;
    private float speedMultiplier = 1f;
    private float worldBodyLength = 1f;

    // Base body-wave amplitudes captured from the preset at spawn, so a struggling hooked fish can
    // scale them up for a more powerful flap and relax back without losing the species' tuned values.
    private float baseWaveAmplitude;
    private float baseSideAmplitude;
    private float basePivotAmount;
    private float currentFlapAmp = 1f;
    private float lastUploadedFlapAmp = 1f;

    // Waves-along-body, smoothed like the flap amplitude. The caught hang eases this toward ~0:
    // with no travelling wave left, the flexible body swings side to side as ONE — a fish
    // flopping on the line — instead of undulating like it's still swimming. Swimming fish never
    // change it, so for them this is target == base and nothing is ever re-uploaded.
    private float targetBodyWaves;
    private float currentBodyWaves;
    private float lastUploadedBodyWaves;

    public void SetBodyWavesTarget(float waves) => targetBodyWaves = waves;

    // The rest of the sway inputs, mirrored from the preset/mesh at spawn so the CPU skinning
    // path (below) can run the exact FishSway.hlsl math after the sway material is gone.
    private float swimFrequency;
    private float swimMaskStart;
    private float spineMinZ;
    private float spineMaxZ;

    // CPU continuation of the sway + chain skinning after the caught-fish material reveal: the
    // real fish materials (the cel-shade graph) have no sway vertex pass, so from the reveal on
    // the same deformation is applied to per-instance mesh copies on the CPU instead — one
    // low-poly showcase fish, so rewriting its vertices each frame is cheap. Base vertices are
    // pre-transformed into the body's object space (the space the chain points are converted
    // into), so per frame it's just the skin math plus one matrix multiply back into part space.
    private struct CpuSwayPart
    {
        public Mesh mesh;               // per-instance copy owned by us; destroyed in Despawn
        public Vector3[] baseBodyVerts; // rest pose, already in the body's object space
        public Vector3[] workVerts;     // scratch output, in the part's own mesh space
        public Matrix4x4 bodyToPart;
    }
    private CpuSwayPart[] cpuSwayParts;
    private bool cpuSwayActive;

    // Trailing chain state. chainWorld is simulated in world space; chainObjectSpace is the
    // same points converted into the body mesh's object space for the vertex shader.
    private readonly Vector3[] chainWorld = new Vector3[ChainCount];
    private readonly Vector4[] chainObjectSpace = new Vector4[ChainCount];
    private Transform bodyTransform;
    private Renderer[] swayRenderers;
    private MaterialPropertyBlock chainBlock;

    private static readonly int SpineMinZId     = Shader.PropertyToID("_SpineMinZ");
    private static readonly int SpineMaxZId     = Shader.PropertyToID("_SpineMaxZ");
    private static readonly int FrequencyId     = Shader.PropertyToID("_Frequency");
    private static readonly int BodyWavesId     = Shader.PropertyToID("_BodyWaves");
    private static readonly int WaveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
    private static readonly int SideAmplitudeId = Shader.PropertyToID("_SideAmplitude");
    private static readonly int PivotAmountId   = Shader.PropertyToID("_PivotAmount");
    private static readonly int MaskStartId     = Shader.PropertyToID("_MaskStart");
    private static readonly int UseScriptTimeId = Shader.PropertyToID("_UseScriptTime");
    private static readonly int ScriptTimeId    = Shader.PropertyToID("_ScriptTime");
    private static readonly int UseChainId      = Shader.PropertyToID("_UseChain");
    private static readonly int ChainPointsId   = Shader.PropertyToID("_ChainPoints");
    private static readonly int ChainVerticalAmountId = Shader.PropertyToID("_ChainVerticalAmount");
    private static readonly int FadeAmountId    = Shader.PropertyToID("_FadeAmount");

    // Spawn-in dissolve: the model materialises from fully clipped to fully opaque over
    // spawnFadeDuration, the reverse of the swim-off dive's fade-out. The ordered dither lives in
    // the shader; here we just ramp _FadeAmount each frame.
    private bool spawningIn;
    private float spawnFadeTimer;
    private float spawnFadeDuration = 0.6f;

    public FishModelVisual(Transform host)
    {
        this.host = host;
    }

    public void Spawn(FishPreset preset, float depthBelowSurface, float scale,
                      Vector3 rotationOffsetEuler, Material silhouetteMaterial)
    {
        Despawn();

        silhouetteOverride = silhouetteMaterial;

        if (preset == null || preset.fishPrefab == null) return;

        modelInstance = Object.Instantiate(preset.fishPrefab, host);
        modelInstance.transform.localPosition = Vector3.zero;
        modelInstance.transform.localRotation = Quaternion.Euler(rotationOffsetEuler);
        modelInstance.transform.localScale = Vector3.one * scale;

        // The art prefabs double as physics props elsewhere (PhysicalFish, fish tank) — the
        // swimming copy must never collide with the bobber or trip zone triggers.
        foreach (Collider col in modelInstance.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
        foreach (Rigidbody rb in modelInstance.GetComponentsInChildren<Rigidbody>(true))
            rb.isKinematic = true;

        SetupSwayMaterial(preset);

        Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
        modelRenderers = renderers;
        ApplySilhouette(renderers);
        SinkBelowWaterLine(renderers, depthBelowSurface);

        // Chain setup happens after the sink so the world-to-object conversion sees the
        // model's final transform.
        if (swayInstance != null && bodyTransform != null)
        {
            swayRenderers = renderers;
            chainBlock = new MaterialPropertyBlock();
            ResetChain();
            UploadChain();
        }
    }

    public void Despawn()
    {
        if (modelInstance != null)
        {
            Object.Destroy(modelInstance);
            modelInstance = null;
        }
        if (swayInstance != null)
        {
            Object.Destroy(swayInstance);
            swayInstance = null;
        }
        if (cpuSwayParts != null)
        {
            for (int i = 0; i < cpuSwayParts.Length; i++)
                if (cpuSwayParts[i].mesh != null) Object.Destroy(cpuSwayParts[i].mesh);
            cpuSwayParts = null;
        }
        cpuSwayActive = false;
        bodyTransform = null;
        swayRenderers = null;
        modelRenderers = null;
        chainBlock = null;
        originalStates = null;
        verticalHang = false;
    }

    // Swaps the silhouette back for the prefab's real materials — the caught-fish reveal during
    // the inspection camera transition. The sway/chain vertex animation lives in the silhouette
    // shader, so this hands the deformation over to the CPU skinning path (per-instance mesh
    // copies, same math) so the fish keeps flapping in its true colors. Returns true when that
    // handover succeeded; false means a mesh wasn't CPU-readable and the body renders rigid from
    // here on — the caller should AlignSnoutToForwardAxis and drive motion via the transform.
    // MouthWorldPosition keeps using the chain while the CPU sway runs, so the line stays parked
    // on the mouth either way.
    public bool RevealTrueMaterials()
    {
        if (originalStates == null) return cpuSwayActive;

        // Take over the deformation BEFORE the sway state is torn down: the CPU path needs the
        // body transform, spine bounds and chain, all captured while the sway instance still exists.
        bool swayContinues = TryBeginCpuSway();

        for (int i = 0; i < originalStates.Length; i++)
        {
            Renderer r = originalStates[i].renderer;
            if (r == null) continue;
            r.sharedMaterials = originalStates[i].materials;
            r.shadowCastingMode = originalStates[i].shadowMode;
            r.receiveShadows = originalStates[i].receiveShadows;
            r.lightProbeUsage = originalStates[i].probeUsage;
            r.reflectionProbeUsage = originalStates[i].reflectionUsage;
            r.SetPropertyBlock(null); // drop the chain-point block the sway shader was fed
        }
        originalStates = null;

        if (swayInstance != null)
        {
            Object.Destroy(swayInstance);
            swayInstance = null;
        }
        swayRenderers = null;
        chainBlock = null;
        return swayContinues;
    }

    // Sets up the post-reveal CPU skinning: every mesh part gets a per-instance copy whose
    // vertices are rewritten each frame with the same wave+chain math the shader ran. All parts
    // must be CPU-readable (Read/Write enabled on the import) — deforming only some of them
    // would tear the body apart at the seams, so one unreadable mesh means no CPU sway at all.
    private bool TryBeginCpuSway()
    {
        if (swayInstance == null || bodyTransform == null || modelInstance == null) return false;

        MeshFilter[] filters = modelInstance.GetComponentsInChildren<MeshFilter>(true);
        int usable = 0;
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i].sharedMesh == null) continue;
            if (!filters[i].sharedMesh.isReadable) return false;
            usable++;
        }
        if (usable == 0) return false;

        // Seat the model on the line axis BEFORE capturing the rest pose: the baked swimming
        // offsets (waterline sink along local -Y, mesh pivot/snout offset) are perpendicular to
        // the spine, and the chain skin only replaces coordinates ALONG its plane — the
        // perpendicular offset survives, spins with the host, and orbits the revealed fish
        // around the line in a cone instead of hanging it from the hook. Align first, then
        // capture matrices/vertices in the corrected body space.
        AlignSnoutToForwardAxis();

        Matrix4x4 worldToBody = bodyTransform.worldToLocalMatrix;
        float objectBodyLength = Mathf.Max(spineMaxZ - spineMinZ, 1e-4f);
        cpuSwayParts = new CpuSwayPart[usable];
        int p = 0;
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i].sharedMesh == null) continue;

            Mesh mesh = filters[i].mesh; // instantiates a copy this class now owns
            mesh.MarkDynamic();
            Vector3[] baseVerts = mesh.vertices;

            Matrix4x4 partToBody = worldToBody * filters[i].transform.localToWorldMatrix;
            Vector3[] baseBody = new Vector3[baseVerts.Length];
            for (int v = 0; v < baseVerts.Length; v++)
                baseBody[v] = partToBody.MultiplyPoint3x4(baseVerts[v]);

            // One-time conservative bounds so the swaying vertices never out-run culling — beats
            // a RecalculateBounds every frame.
            Bounds b = mesh.bounds;
            b.Expand(objectBodyLength);
            mesh.bounds = b;

            cpuSwayParts[p++] = new CpuSwayPart
            {
                mesh = mesh,
                baseBodyVerts = baseBody,
                workVerts = baseVerts, // reused as scratch; rest pose lives in baseBodyVerts
                bodyToPart = partToBody.inverse,
            };
        }

        cpuSwayActive = true;
        return true;
    }

    // Recenters the model so the snout sits EXACTLY on the host's forward axis — the axis the
    // hang pose pins to the line's end and spins around. The swimming setup bakes two local
    // offsets into the model that don't matter while the sway shader skins the body onto the
    // chain, but become visible sideways displacement the moment the shader is stripped with
    // the host pitched vertical: the below-waterline sink (local -Y) and the mesh's own
    // pivot/snout offset. Without this the revealed fish orbits the line in a cone as it spins,
    // with the line tilted to reach the off-axis mouth.
    public void AlignSnoutToForwardAxis()
    {
        if (modelInstance == null || bodyTransform == null) return;

        // Undo the waterline sink, then measure where the snout actually sits relative to the
        // host and shift the model so only its along-forward distance remains.
        modelInstance.transform.localPosition = Vector3.zero;
        Vector3 fromHost = bodyTransform.TransformPoint(snoutLocalPosition) - host.position;
        float along = Vector3.Dot(fromHost, host.forward);
        Vector3 lateral = fromHost - host.forward * along;
        modelInstance.transform.position -= lateral;

        // The mouth-on-hook placement (hook - forward * HeadForwardOffset) must use the snout's
        // true distance, not the bounds-derived estimate, so the mouth lands on the line's end.
        HeadForwardOffset = Mathf.Max(0f, along);
    }

    // Advances the swim clock and the trailing chain; called every frame by FishRipple.
    // targetIntensity multiplies the species' base tail-beat rate (1 = calm cruise, >1 =
    // agitated). straightenRate is how quickly the body relaxes straight behind the head
    // (per second) — lower values hold curves longer for a lazier, floppier follow.
    //
    // The chain is a TRUE 3D ROPE: each point trails the one in front by a fixed distance along
    // the real 3D direction between them, so the body genuinely follows wherever the head goes —
    // including up and down. A level-swimming fish stays perfectly flat with no special-casing
    // (its head's height never changes, so every segment stays at that height), while a head that
    // rises or dives (a strike arc, a hooked leap, an escape dive) drags a real trailing arch
    // behind it. The shader honours the chain's height in full; a flat chain renders identically
    // to the old strictly-horizontal skin.
    public void Tick(float deltaTime, float targetIntensity, float straightenRate,
                     float flapFrequencyMultiplier = 1f, float flapAmplitudeMultiplier = 1f)
    {
        // Two deformation backends, same math and state: the sway material while silhouetted,
        // the CPU mesh rewrite after the caught-fish reveal. The clock, amplitude ramp and chain
        // all persist across the handover, so the flap is continuous through the material swap.
        bool gpuSway = swayInstance != null;
        if ((!gpuSway && !cpuSwayActive) || deltaTime <= 0f) return;

        // Spawn-in dissolve ramps independently of swim state — a fish fades in whether it's
        // cruising, attracted or being hunted the instant it appears.
        if (gpuSway && spawningIn)
        {
            spawnFadeTimer += deltaTime;
            float fade = Mathf.Clamp01(spawnFadeTimer / spawnFadeDuration);
            swayInstance.SetFloat(FadeAmountId, fade);
            if (fade >= 1f) spawningIn = false;
        }

        speedMultiplier = Mathf.Lerp(speedMultiplier, targetIntensity,
                                     1f - Mathf.Exp(-IntensityResponse * deltaTime));
        // The flap-frequency multiplier rides ON TOP of the intensity-driven clock, so a struggling
        // fish can beat its tail drastically faster than even an agitated cruise.
        swimPhase += deltaTime * speedMultiplier * Mathf.Max(0f, flapFrequencyMultiplier);
        if (gpuSway) swayInstance.SetFloat(ScriptTimeId, swimPhase);

        // Body-wave "power": ramp the tail-beat amplitude toward the requested multiplier so a
        // struggling fish flaps WIDER, not only faster. Only re-upload on a real change, so calmly
        // swimming fish (multiplier 1) never touch the material after spawn.
        currentFlapAmp = Mathf.Lerp(currentFlapAmp, flapAmplitudeMultiplier,
                                    1f - Mathf.Exp(-IntensityResponse * deltaTime));
        if (gpuSway && Mathf.Abs(currentFlapAmp - lastUploadedFlapAmp) > 0.001f)
        {
            swayInstance.SetFloat(WaveAmplitudeId, baseWaveAmplitude * currentFlapAmp);
            swayInstance.SetFloat(SideAmplitudeId, baseSideAmplitude * currentFlapAmp);
            swayInstance.SetFloat(PivotAmountId, basePivotAmount * currentFlapAmp);
            lastUploadedFlapAmp = currentFlapAmp;
        }

        currentBodyWaves = Mathf.Lerp(currentBodyWaves, targetBodyWaves,
                                      1f - Mathf.Exp(-IntensityResponse * deltaTime));
        if (gpuSway && Mathf.Abs(currentBodyWaves - lastUploadedBodyWaves) > 0.001f)
        {
            swayInstance.SetFloat(BodyWavesId, currentBodyWaves);
            lastUploadedBodyWaves = currentBodyWaves;
        }

        if (bodyTransform == null || (gpuSway && swayRenderers == null)) return;

        Vector3 forward = FlatForward();
        Vector3 head = host.position + forward * HeadForwardOffset;

        // A teleport (anti-stuck rescue, respawn) snaps the chain instead of letting the
        // body streak across the pond for a frame.
        if ((head - chainWorld[0]).sqrMagnitude > worldBodyLength * worldBodyLength * 4f)
            ResetChain();

        chainWorld[0] = head;

        // Always feed the chain's full height into the shader — a flat chain stays flat, an
        // arcing head produces a real arch. (The CPU path bakes the equivalent at full strength.)
        if (gpuSway) swayInstance.SetFloat(ChainVerticalAmountId, 1f);

        float spacing = worldBodyLength / (ChainCount - 1);
        float straighten = 1f - Mathf.Exp(-Mathf.Max(straightenRate, 0f) * deltaTime);

        Vector3 previousDir = -forward; // first segment relaxes toward straight back from the head
        for (int i = 1; i < ChainCount; i++)
        {
            Vector3 toPoint = chainWorld[i] - chainWorld[i - 1];
            Vector3 dir = toPoint.sqrMagnitude < 1e-8f ? previousDir : toPoint.normalized;
            // Relax toward the segment ahead so a cruising body straightens out behind the head;
            // the spacing constraint below keeps the rope taut so it trails the head's path.
            dir = Vector3.Slerp(dir, previousDir, straighten);
            if (dir.sqrMagnitude < 1e-8f) dir = previousDir;
            dir.Normalize();
            chainWorld[i] = chainWorld[i - 1] + dir * spacing;
            previousDir = dir;
        }

        if (gpuSway) UploadChain();
        else DeformCpuSwayParts();
    }

    // Dissolves the silhouette (1 = fully visible, 0 = gone) — used by the escape/lifetime
    // swim-off dive. The fade is an ordered DITHER CLIP in the shader, not alpha blending: the
    // pond water renders at the transparent queue but fakes its see-through look by sampling the
    // camera opaque texture, so a swimming fish is only visible because it sits in that texture
    // (opaque, queue Geometry). Alpha-blending a fading fish dropped it out of the opaque texture
    // and popped a transparent ghost onto the water surface; clipping keeps it opaque so it
    // dissolves cleanly underneath the water. The per-fish material instance isolates the fade.
    public void SetFadeAlpha(float alpha)
    {
        if (swayInstance == null) return;
        spawningIn = false; // an explicit swim-off fade owns the dissolve from here on
        swayInstance.SetFloat(FadeAmountId, Mathf.Clamp01(alpha));
    }

    // Starts the spawn-in dissolve: the fish materialises from invisible to fully opaque over
    // `seconds`, the exact reverse of the swim-off dive's fade-out. No-op without the sway shader
    // (the flat-black fallback can't dither), so such fish simply appear — an acceptable fallback.
    public void BeginSpawnFadeIn(float seconds)
    {
        if (swayInstance == null) return;
        spawnFadeDuration = Mathf.Max(0.01f, seconds);
        spawnFadeTimer = 0f;
        spawningIn = true;
        swayInstance.SetFloat(FadeAmountId, 0f);
    }

    // Lays the trailing chain out dead straight behind the head along the CURRENT heading,
    // immediately. Called by the hooked fish at the hook-set so the fight starts from a clean,
    // straight body regardless of what shape the bite window left the chain in. Call it after
    // the host's pose has been written for the frame; the next Tick uploads it.
    public void SnapChainStraight() => ResetChain();

    // Lays the chain out straight behind the head along the current heading.
    private void ResetChain()
    {
        Vector3 forward = FlatForward();
        Vector3 head = host.position + forward * HeadForwardOffset;
        float spacing = worldBodyLength / (ChainCount - 1);
        for (int i = 0; i < ChainCount; i++)
            chainWorld[i] = head - forward * (spacing * i);
    }

    private void ConvertChainToObjectSpace()
    {
        Matrix4x4 worldToLocal = bodyTransform.worldToLocalMatrix;
        for (int i = 0; i < ChainCount; i++)
            chainObjectSpace[i] = worldToLocal.MultiplyPoint3x4(chainWorld[i]);
    }

    private void UploadChain()
    {
        ConvertChainToObjectSpace();
        chainBlock.SetVectorArray(ChainPointsId, chainObjectSpace);
        for (int i = 0; i < swayRenderers.Length; i++)
            swayRenderers[i].SetPropertyBlock(chainBlock);
    }

    // The post-reveal deformation: a straight C# port of FishSway.hlsl's FishSwayLateralWave +
    // FishChainSkin (head-at-min-Z convention, chain vertical at full strength — exactly what the
    // sway material was fed), applied to the per-instance mesh copies. MUST stay in lockstep with
    // the HLSL, or the caught fish's flap pops the moment the reveal hands the skinning over.
    private void DeformCpuSwayParts()
    {
        if (cpuSwayParts == null) return;
        ConvertChainToObjectSpace();

        float bodyLength = Mathf.Max(spineMaxZ - spineMinZ, 1e-4f);
        float phase = swimPhase * swimFrequency * Mathf.PI * 2f;
        float sinPhase = Mathf.Sin(phase);
        float cosPhase = Mathf.Cos(phase);
        float waveAmp = baseWaveAmplitude * currentFlapAmp;
        float sideAmp = baseSideAmplitude * currentFlapAmp;
        float pivotAmp = basePivotAmount * currentFlapAmp;
        float headY = chainObjectSpace[0].y;

        for (int p = 0; p < cpuSwayParts.Length; p++)
        {
            Vector3[] src = cpuSwayParts[p].baseBodyVerts;
            Vector3[] dst = cpuSwayParts[p].workVerts;
            Matrix4x4 bodyToPart = cpuSwayParts[p].bodyToPart;

            for (int v = 0; v < src.Length; v++)
            {
                Vector3 pos = src[v];
                float s = Mathf.Clamp01((pos.z - spineMinZ) / bodyLength);

                // FishSwayLateralWave: travelling wave + side drift + yaw wobble, head pinned.
                float mask = SmoothStep(swimMaskStart, 1f, s);
                float headGuard = SmoothStep(0f, 0.35f, s);
                float waveLateral =
                    Mathf.Sin(phase - s * currentBodyWaves * Mathf.PI * 2f) * waveAmp * bodyLength * mask
                    + (cosPhase * sideAmp + sinPhase * pivotAmp * (s - 0.5f)) * bodyLength * headGuard;

                // FishChainSkin: xz from the trailing chain, cross-section pitched to its slope.
                float u = s * (ChainCount - 1);
                int seg = Mathf.Min((int)u, ChainCount - 2);
                float t = u - seg;
                Vector4 c0 = chainObjectSpace[seg];
                Vector4 c1 = chainObjectSpace[seg + 1];

                float tanX = c1.x - c0.x;
                float tanZ = c1.z - c0.z;
                float len = Mathf.Sqrt(tanX * tanX + tanZ * tanZ);
                float tdirX, tdirZ;
                if (len > 1e-5f) { tdirX = tanX / len; tdirZ = tanZ / len; }
                else { tdirX = 0f; tdirZ = 1f; }

                float lateral = pos.x + waveLateral;
                float outX = Mathf.Lerp(c0.x, c1.x, t) + tdirZ * lateral;
                float outY = pos.y;
                float outZ = Mathf.Lerp(c0.z, c1.z, t) - tdirX * lateral;

                float slope = (c1.y - c0.y) / Mathf.Max(len, 1e-4f);
                float pitch = Mathf.Atan(slope);
                float sinP = Mathf.Sin(pitch);
                float cosP = Mathf.Cos(pitch);
                outY += Mathf.Lerp(c0.y, c1.y, t) - headY; // spine follows the chain's height
                outX += tdirX * (-sinP * pos.y);           // pitch the cross-section along the slope
                outZ += tdirZ * (-sinP * pos.y);
                outY += (cosP - 1f) * pos.y;               // foreshorten the upright as it tilts

                dst[v] = bodyToPart.MultiplyPoint3x4(new Vector3(outX, outY, outZ));
            }

            cpuSwayParts[p].mesh.SetVertices(dst);
        }
    }

    // HLSL smoothstep(a, b, x) — Mathf.SmoothStep interpolates differently, so port it exactly.
    private static float SmoothStep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / Mathf.Max(b - a, 1e-5f));
        return t * t * (3f - 2f * t);
    }

    private Vector3 FlatForward()
    {
        Vector3 forward = host.forward;
        // Swimming fish steer on the horizontal plane, so the chain heading is flattened. A fish
        // hanging nose-up from the line points straight up — flattening would degenerate to the
        // fallback axis — so the hang keeps the true 3D forward and the chain dangles below it.
        if (!verticalHang) forward.y = 0f;
        return forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
    }

    // The snout = the centroid of the body mesh's front-most vertices (within 2% of body
    // length of the head-end extreme). Mesh-accurate, so it lands on the actual mouth tip
    // regardless of how each species' pivot or proportions are set up.
    private void ComputeSnoutPoint()
    {
        snoutLocalPosition = Vector3.zero;
        MeshFilter bodyFilter = bodyTransform != null ? bodyTransform.GetComponent<MeshFilter>() : null;
        if (bodyFilter == null || bodyFilter.sharedMesh == null) return;

        Mesh mesh = bodyFilter.sharedMesh;
        Bounds b = mesh.bounds;
        float headZ = b.min.z; // head-at-min-Z convention

        if (!mesh.isReadable)
        {
            // Read/Write disabled on the import: vertices aren't CPU-accessible, so settle
            // for the bounds-based snout (front centre) instead of the mesh-exact tip.
            snoutLocalPosition = new Vector3(b.center.x, b.center.y, headZ);
            return;
        }

        float epsilon = Mathf.Max((b.max.z - b.min.z) * 0.02f, 1e-4f);

        Vector3[] vertices = mesh.vertices;
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i].z <= headZ + epsilon)
            {
                sum += vertices[i];
                count++;
            }
        }
        snoutLocalPosition = count > 0 ? sum / count : new Vector3(0f, 0f, headZ);
    }

    // The host rides exactly on the water line, so the model is pushed down far enough that
    // its highest rendered point sits depthBelowSurface under the surface. Measured from
    // renderer bounds — prefab pivots and big species can't poke through the water.
    //
    // The sink is measured in WORLD units but applied as a LOCAL offset, so it must be
    // divided by the host's lossy Y scale — the ripple prefab roots are scaled (2x) and
    // zones can scale their children further; skipping this leaves fish riding high or
    // sunk too deep depending on which prefab/zone spawned them.
    private void SinkBelowWaterLine(Renderer[] renderers, float depthBelowSurface)
    {
        float topY = float.MinValue;
        for (int i = 0; i < renderers.Length; i++)
            topY = Mathf.Max(topY, renderers[i].bounds.max.y);

        float sink = renderers.Length > 0
            ? (topY - host.position.y) + depthBelowSurface
            : depthBelowSurface;

        float parentScaleY = Mathf.Max(Mathf.Abs(host.lossyScale.y), 0.0001f);
        modelInstance.transform.localPosition = new Vector3(0f, -sink / parentScaleY, 0f);
    }

    private void SetupSwayMaterial(FishPreset preset)
    {
        if (silhouetteOverride == null || !silhouetteOverride.HasProperty(ScriptTimeId)) return;

        swayInstance = new Material(silhouetteOverride)
        {
            name = silhouetteOverride.name + " (fish instance)"
        };

        // One spine range for the whole fish: the art prefabs are split into parts (body,
        // fins) that share the model's object space, so every part must normalize against
        // the same head-to-tail range or the parts shear apart at the seams.
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        float bodyScaleZ = 1f;
        float longestPart = 0f;
        foreach (MeshFilter filter in modelInstance.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds b = filter.sharedMesh.bounds;
            minZ = Mathf.Min(minZ, b.min.z);
            maxZ = Mathf.Max(maxZ, b.max.z);

            // The mesh-to-world scale can sit anywhere in the art prefab hierarchy (nested
            // model prefab, import scale), so read it off the mesh that actually forms the
            // body — the longest part — rather than the spawned root. That mesh's transform
            // also defines the object space the chain points get converted into.
            float partLength = b.max.z - b.min.z;
            if (partLength > longestPart)
            {
                longestPart = partLength;
                bodyScaleZ = Mathf.Abs(filter.transform.lossyScale.z);
                bodyTransform = filter.transform;
            }
        }
        if (minZ < maxZ)
        {
            swayInstance.SetFloat(SpineMinZId, minZ);
            swayInstance.SetFloat(SpineMaxZId, maxZ);
            spineMinZ = minZ;
            spineMaxZ = maxZ;
            worldBodyLength = Mathf.Max((maxZ - minZ) * bodyScaleZ, 0.01f);

            // Head sits at mesh min Z; the 180° rotation offset maps mesh -Z onto the
            // host's forward, so the head's forward offset from the host origin is -minZ.
            HeadForwardOffset = Mathf.Clamp(-minZ * bodyScaleZ, 0f, worldBodyLength);

            ComputeSnoutPoint();
        }
        else
        {
            worldBodyLength = 1f;
            HeadForwardOffset = 0f;
            bodyTransform = null;
            snoutLocalPosition = Vector3.zero;
            spineMinZ = -0.5f;
            spineMaxZ = 0.5f;
        }

        if (preset != null)
        {
            swayInstance.SetFloat(FrequencyId, preset.swimFrequency);
            swayInstance.SetFloat(BodyWavesId, preset.swimBodyWaves);
            swayInstance.SetFloat(WaveAmplitudeId, preset.swimWaveAmplitude);
            swayInstance.SetFloat(SideAmplitudeId, preset.swimSideAmplitude);
            swayInstance.SetFloat(PivotAmountId, preset.swimPivotAmount);
            swayInstance.SetFloat(MaskStartId, preset.swimMaskStart);

            // Remember the species' base amplitudes so the struggle "power" boost can scale them
            // and relax back to exactly these. Reset the live multiplier in case this instance is reused.
            baseWaveAmplitude = preset.swimWaveAmplitude;
            baseSideAmplitude = preset.swimSideAmplitude;
            basePivotAmount = preset.swimPivotAmount;
            currentFlapAmp = 1f;
            lastUploadedFlapAmp = 1f;

            // Mirror the rest of the sway inputs for the post-reveal CPU skinning path.
            swimFrequency = preset.swimFrequency;
            swimMaskStart = preset.swimMaskStart;
            targetBodyWaves = currentBodyWaves = lastUploadedBodyWaves = preset.swimBodyWaves;
        }

        // Random start de-syncs neighbouring fish; the clock is script-driven from here on,
        // and the body is skinned onto the trailing chain rather than the analytic bend.
        swimPhase = Random.Range(0f, 100f);
        speedMultiplier = 1f;
        swayInstance.SetFloat(UseScriptTimeId, 1f);
        swayInstance.SetFloat(ScriptTimeId, swimPhase);
        swayInstance.SetFloat(UseChainId, bodyTransform != null ? 1f : 0f);
        swayInstance.SetFloat(ChainVerticalAmountId, 0f); // flat until a leap ramps it up

    }

    private void ApplySilhouette(Renderer[] renderers)
    {
        // Remember what the prefab shipped with so RevealTrueMaterials can restore it exactly.
        originalStates = new OriginalRendererState[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            originalStates[i] = new OriginalRendererState
            {
                renderer = renderers[i],
                materials = renderers[i].sharedMaterials,
                shadowMode = renderers[i].shadowCastingMode,
                receiveShadows = renderers[i].receiveShadows,
                probeUsage = renderers[i].lightProbeUsage,
                reflectionUsage = renderers[i].reflectionProbeUsage,
            };
        }

        Material mat = swayInstance != null ? swayInstance : GetSilhouetteMaterial();
        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] mats = new Material[renderers[i].sharedMaterials.Length];
            for (int m = 0; m < mats.Length; m++) mats[m] = mat;
            renderers[i].sharedMaterials = mats;

            // Flat black means flat: no shadows cast or received, no probe/reflection
            // contribution — nothing the lighting can do to shade the silhouette.
            renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            renderers[i].receiveShadows = false;
            renderers[i].lightProbeUsage = LightProbeUsage.Off;
            renderers[i].reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    private Material GetSilhouetteMaterial()
    {
        if (silhouetteOverride != null) return silhouetteOverride;

        if (sharedFallbackSilhouette == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            sharedFallbackSilhouette = new Material(shader) { name = "FishSilhouette (runtime)" };
            if (sharedFallbackSilhouette.HasProperty("_BaseColor"))
                sharedFallbackSilhouette.SetColor("_BaseColor", Color.black);
            if (sharedFallbackSilhouette.HasProperty("_Color"))
                sharedFallbackSilhouette.SetColor("_Color", Color.black);
        }
        return sharedFallbackSilhouette;
    }
}
