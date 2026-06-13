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
            if (swayInstance != null)
                return new Vector3(chainWorld[0].x, snout.y, chainWorld[0].z);
            return snout;
        }
    }

    private Vector3 snoutLocalPosition;

    private readonly Transform host;
    private GameObject modelInstance;

    private Material silhouetteOverride;
    private static Material sharedFallbackSilhouette;

    // Per-fish instance of the sway material. Null when the override isn't the sway shader,
    // in which case the override (or the flat-black fallback) is used shared, as before.
    private Material swayInstance;
    private float swimPhase;
    private float speedMultiplier = 1f;
    private float worldBodyLength = 1f;

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
    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly int SrcBlendId      = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId      = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId        = Shader.PropertyToID("_ZWrite");

    private bool fadeConfigured;

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
        bodyTransform = null;
        swayRenderers = null;
        chainBlock = null;
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
    public void Tick(float deltaTime, float targetIntensity, float straightenRate)
    {
        if (swayInstance == null || deltaTime <= 0f) return;

        speedMultiplier = Mathf.Lerp(speedMultiplier, targetIntensity,
                                     1f - Mathf.Exp(-IntensityResponse * deltaTime));
        swimPhase += deltaTime * speedMultiplier;
        swayInstance.SetFloat(ScriptTimeId, swimPhase);

        if (bodyTransform == null || swayRenderers == null) return;

        Vector3 forward = FlatForward();
        Vector3 head = host.position + forward * HeadForwardOffset;

        // A teleport (anti-stuck rescue, respawn) snaps the chain instead of letting the
        // body streak across the pond for a frame.
        if ((head - chainWorld[0]).sqrMagnitude > worldBodyLength * worldBodyLength * 4f)
            ResetChain();

        chainWorld[0] = head;

        // Always feed the chain's full height into the shader — a flat chain stays flat, an
        // arcing head produces a real arch.
        swayInstance.SetFloat(ChainVerticalAmountId, 1f);

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

        UploadChain();
    }

    // Fades the silhouette out (1 = opaque, 0 = gone) — used by the escape swim-off when a
    // fish gets away. First call switches this fish's material instance to alpha blending
    // and the transparent queue; the per-fish instance means no other fish is affected.
    public void SetFadeAlpha(float alpha)
    {
        if (swayInstance == null) return;

        if (!fadeConfigured)
        {
            fadeConfigured = true;
            swayInstance.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            swayInstance.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            swayInstance.SetFloat(ZWriteId, 0f);
            swayInstance.renderQueue = (int)RenderQueue.Transparent;
        }

        Color color = swayInstance.GetColor(BaseColorId);
        color.a = Mathf.Clamp01(alpha);
        swayInstance.SetColor(BaseColorId, color);
    }

    // Lays the chain out straight behind the head along the current heading.
    private void ResetChain()
    {
        Vector3 forward = FlatForward();
        Vector3 head = host.position + forward * HeadForwardOffset;
        float spacing = worldBodyLength / (ChainCount - 1);
        for (int i = 0; i < ChainCount; i++)
            chainWorld[i] = head - forward * (spacing * i);
    }

    private void UploadChain()
    {
        Matrix4x4 worldToLocal = bodyTransform.worldToLocalMatrix;
        for (int i = 0; i < ChainCount; i++)
            chainObjectSpace[i] = worldToLocal.MultiplyPoint3x4(chainWorld[i]);

        chainBlock.SetVectorArray(ChainPointsId, chainObjectSpace);
        for (int i = 0; i < swayRenderers.Length; i++)
            swayRenderers[i].SetPropertyBlock(chainBlock);
    }

    private Vector3 FlatForward()
    {
        Vector3 forward = host.forward;
        forward.y = 0f;
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
        }

        if (preset != null)
        {
            swayInstance.SetFloat(FrequencyId, preset.swimFrequency);
            swayInstance.SetFloat(BodyWavesId, preset.swimBodyWaves);
            swayInstance.SetFloat(WaveAmplitudeId, preset.swimWaveAmplitude);
            swayInstance.SetFloat(SideAmplitudeId, preset.swimSideAmplitude);
            swayInstance.SetFloat(PivotAmountId, preset.swimPivotAmount);
            swayInstance.SetFloat(MaskStartId, preset.swimMaskStart);
        }

        // Random start de-syncs neighbouring fish; the clock is script-driven from here on,
        // and the body is skinned onto the trailing chain rather than the analytic bend.
        swimPhase = Random.Range(0f, 100f);
        speedMultiplier = 1f;
        fadeConfigured = false;
        swayInstance.SetFloat(UseScriptTimeId, 1f);
        swayInstance.SetFloat(ScriptTimeId, swimPhase);
        swayInstance.SetFloat(UseChainId, bodyTransform != null ? 1f : 0f);
        swayInstance.SetFloat(ChainVerticalAmountId, 0f); // flat until a leap ramps it up

    }

    private void ApplySilhouette(Renderer[] renderers)
    {
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
