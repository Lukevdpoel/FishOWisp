using UnityEngine;

// Adds the procedural swim sine to a DISPLAYED fish — encyclopedia model, photo studio,
// fish tank, catch inspection — where the fish isn't actually swimming anywhere. The wave
// needs no movement: it's purely time-driven in the vertex shader (only the turn-follow
// chain depends on motion, and display fish don't turn). This component:
//   1. measures the spine bounds the shader needs (combined mesh bounds, like the zone fish),
//   2. applies gentle display-idle swim parameters,
//   3. drives the shader's script clock with UNSCALED time, so fish keep swaying inside
//      paused menus where Time.timeScale — and with it the shader's own _Time — freezes.
//
// Two modes, picked automatically:
//  - GPU: if the material's shader carries the FishSway displacement (the silhouette sway
//    shader, or a Shader Graph with the FishSway Custom Function node — recipe in
//    Assets/Shaders/FishSway/FishSway.hlsl), the component just drives its clock.
//  - CPU: otherwise, the same wave is applied directly to the mesh vertices each frame.
//    Works under ANY material with zero shader work — fine for the one or two fish a
//    display shows (the pond fish stay on the GPU path for a reason). Needs Read/Write
//    enabled on the model import, which the fish models have.
//
// Usage: add to the root of a displayed fish instance (or the prefab used for display).
// Materials/meshes are instanced per fish, so two displayed fish never sway in sync.
public class FishDisplaySway : MonoBehaviour
{
    [Tooltip("Tail-beat rate in beats/second. Keep low for a relaxed display idle.")]
    public float frequency = 1f;
    [Tooltip("Tail swing as a fraction of body length. Keep small (0.02–0.05) for display poses.")]
    [Range(0f, 0.3f)] public float waveAmplitude = 0.035f;
    [Tooltip("Wave cycles along the body — stiff swimmers ~0.6, eels ~1.8 (see the species' FishPreset for reference).")]
    public float bodyWaves = 0.7f;
    [Tooltip("Portion of the body (from the head) that stays rigid.")]
    [Range(0f, 1f)] public float maskStart = 0.35f;
    [Tooltip("Gentle whole-body drift, fraction of body length.")]
    [Range(0f, 0.1f)] public float sideAmplitude = 0.008f;
    [Tooltip("Drive the sway with unscaled time so it keeps moving while the game is paused (menus, encyclopedia).")]
    public bool useUnscaledTime = true;

    private static readonly int SpineMinZId     = Shader.PropertyToID("_SpineMinZ");
    private static readonly int SpineMaxZId     = Shader.PropertyToID("_SpineMaxZ");
    private static readonly int FrequencyId     = Shader.PropertyToID("_Frequency");
    private static readonly int BodyWavesId     = Shader.PropertyToID("_BodyWaves");
    private static readonly int WaveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
    private static readonly int SideAmplitudeId = Shader.PropertyToID("_SideAmplitude");
    private static readonly int MaskStartId     = Shader.PropertyToID("_MaskStart");
    private static readonly int UseScriptTimeId = Shader.PropertyToID("_UseScriptTime");
    private static readonly int ScriptTimeId    = Shader.PropertyToID("_ScriptTime");
    private static readonly int UseChainId      = Shader.PropertyToID("_UseChain");

    private Material[] swayMaterials;
    private float clock;
    private float spineMinZ;
    private float spineMaxZ;

    // CPU-deformation fallback state: per deformed mesh, the instanced mesh, its rest-pose
    // vertices, a scratch buffer, and the per-vertex wave constants baked once at setup (they
    // depend only on the rest pose and the inspector parameters, not on time).
    private struct CpuMesh
    {
        public Mesh mesh;
        public Vector3[] restVertices;
        public Vector3[] workVertices;
        public float[] wavePhase;  // phase offset along the spine: s * bodyWaves * 2π
        public float[] waveAmp;    // waveAmplitude * bodyLength * tail mask, per vertex
        public float[] sideAmp;    // sideAmplitude * bodyLength * head guard, per vertex
    }
    private System.Collections.Generic.List<CpuMesh> cpuMeshes;
    private Renderer[] cpuRenderers; // visibility gate for the CPU path — no viewer, no per-vertex work

    private void Start()
    {
        // One spine range for the whole fish — body and fin parts share the model's object
        // space, so all materials must normalize against the same head-to-tail range.
        spineMinZ = float.MaxValue;
        spineMaxZ = float.MinValue;
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds b = filter.sharedMesh.bounds;
            spineMinZ = Mathf.Min(spineMinZ, b.min.z);
            spineMaxZ = Mathf.Max(spineMaxZ, b.max.z);
        }
        if (spineMinZ >= spineMaxZ) return;

        clock = Random.Range(0f, 100f);

        var collected = new System.Collections.Generic.List<Material>();
        foreach (Renderer rend in GetComponentsInChildren<Renderer>(true))
        {
            // renderer.materials instantiates per-renderer copies — display fish are few,
            // and instancing keeps every displayed fish on its own clock and parameters.
            Material[] mats = rend.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material mat = mats[i];
                if (mat == null || !mat.HasProperty(ScriptTimeId)) continue;

                mat.SetFloat(SpineMinZId, spineMinZ);
                mat.SetFloat(SpineMaxZId, spineMaxZ);
                mat.SetFloat(FrequencyId, frequency);
                mat.SetFloat(BodyWavesId, bodyWaves);
                mat.SetFloat(WaveAmplitudeId, waveAmplitude);
                mat.SetFloat(SideAmplitudeId, sideAmplitude);
                mat.SetFloat(MaskStartId, maskStart);
                mat.SetFloat(UseScriptTimeId, 1f);
                mat.SetFloat(ScriptTimeId, clock);
                if (mat.HasProperty(UseChainId)) mat.SetFloat(UseChainId, 0f); // analytic path: no chain in displays
                collected.Add(mat);
            }
        }
        swayMaterials = collected.ToArray();

        // No sway-capable material? Deform the meshes on the CPU instead — same wave, any
        // shader, no graph work. Affordable at display scale.
        if (swayMaterials.Length == 0) SetupCpuFallback();
    }

    private void SetupCpuFallback()
    {
        cpuRenderers = GetComponentsInChildren<Renderer>(true);
        cpuMeshes = new System.Collections.Generic.List<CpuMesh>();
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;

            // .mesh instances the mesh for this filter only — the shared asset stays pristine.
            Mesh mesh = filter.mesh;
            Vector3[] rest = mesh.vertices;

            // CPU-swayed mesh: opt out of the GPU Resident Drawer, or every per-frame SetVertices
            // re-dispatches this renderer to the batcher (InstanceCullingBatcher.BuildBatch churn).
            if (filter.TryGetComponent(out MeshRenderer meshRenderer))
                GpuDrivenRenderingOptOut.Apply(meshRenderer);

            // Lock the bounds expanded by the maximum displacement once, so the renderer
            // never gets culled mid-wave and we skip per-frame bounds recalculation.
            Bounds bounds = mesh.bounds;
            bounds.Expand((waveAmplitude + sideAmplitude) * (spineMaxZ - spineMinZ) * 2f);
            mesh.bounds = bounds;

            // Bake the wave constants per vertex (mirrors FishSwayLateralWave in FishSway.hlsl):
            // only sin/cos of the running phase remains for the per-frame loop.
            float bodyLength = spineMaxZ - spineMinZ;
            var wavePhase = new float[rest.Length];
            var waveAmp = new float[rest.Length];
            var sideAmp = new float[rest.Length];
            for (int i = 0; i < rest.Length; i++)
            {
                float s = Mathf.Clamp01((rest[i].z - spineMinZ) / bodyLength);
                float mask = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - maskStart) / Mathf.Max(1f - maskStart, 0.0001f)));
                float headGuard = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / 0.35f));
                wavePhase[i] = s * bodyWaves * Mathf.PI * 2f;
                waveAmp[i] = waveAmplitude * bodyLength * mask;
                sideAmp[i] = sideAmplitude * bodyLength * headGuard;
            }

            cpuMeshes.Add(new CpuMesh
            {
                mesh = mesh,
                restVertices = rest,
                workVertices = (Vector3[])rest.Clone(),
                wavePhase = wavePhase,
                waveAmp = waveAmp,
                sideAmp = sideAmp,
            });
        }
    }

    private void Update()
    {
        clock += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        if (swayMaterials != null && swayMaterials.Length > 0)
        {
            for (int i = 0; i < swayMaterials.Length; i++)
            {
                if (swayMaterials[i] != null)
                    swayMaterials[i].SetFloat(ScriptTimeId, clock);
            }
            return;
        }

        if (cpuMeshes == null || cpuMeshes.Count == 0) return;

        // No camera can see this fish (display fish spend most of their life inside closed
        // menus or across the map) — skip the per-vertex deform. The clock above still
        // advances, so the wave is in the right pose the moment it's visible again.
        bool anyVisible = false;
        for (int i = 0; i < cpuRenderers.Length; i++)
        {
            if (cpuRenderers[i] != null && cpuRenderers[i].isVisible) { anyVisible = true; break; }
        }
        if (!anyVisible) return;

        // CPU mirror of FishSwayLateralWave in FishSway.hlsl. The masks and amplitudes were baked
        // per vertex at setup; only the running phase is evaluated here.
        float phase = clock * frequency * Mathf.PI * 2f;
        float cosPhase = Mathf.Cos(phase);

        for (int m = 0; m < cpuMeshes.Count; m++)
        {
            CpuMesh entry = cpuMeshes[m];
            Vector3[] rest = entry.restVertices;
            Vector3[] work = entry.workVertices;
            float[] wavePhase = entry.wavePhase;
            float[] waveAmp = entry.waveAmp;
            float[] sideAmp = entry.sideAmp;
            for (int i = 0; i < rest.Length; i++)
            {
                // work[i] stays equal to rest[i] in y/z (set once at clone time); only x moves.
                work[i].x = rest[i].x
                          + Mathf.Sin(phase - wavePhase[i]) * waveAmp[i]
                          + cosPhase * sideAmp[i];
            }
            entry.mesh.SetVertices(work);
        }
    }

    private void OnDestroy()
    {
        if (swayMaterials != null)
        {
            for (int i = 0; i < swayMaterials.Length; i++)
            {
                if (swayMaterials[i] != null) Destroy(swayMaterials[i]);
            }
        }
        if (cpuMeshes != null)
        {
            for (int i = 0; i < cpuMeshes.Count; i++)
            {
                if (cpuMeshes[i].mesh != null) Destroy(cpuMeshes[i].mesh);
            }
        }
    }
}
