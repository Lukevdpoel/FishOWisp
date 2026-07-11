using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interactive water ripples: a damped 2D wave simulation in a small ping-pong
/// RenderTexture that follows an anchor (the bobber, while it's in the water).
/// Modeled on FoliageTrailRenderer — one Graphics.Blit per fixed sim step through
/// Hidden/WaterRippleSim, with the map shifted in UV space so ripples stay anchored
/// to world XZ as the anchor moves. Gameplay stamps impulses in (WaterRippleBinder)
/// and they propagate outward as expanding, merging rings.
///
/// Globals consumed by the water shader (SSR_Transparent):
///   _WaterRippleMap    (Texture) - RGHalf, R = signed surface height
///   _WaterRippleCenter (Vector4) - xy = world XZ the map content is centered on
///   _WaterRippleParams (Vector4) - x = 1/worldSize, y = texel size in UV,
///                                  z = 1 while the sim runs, 0 when idle (multiply the
///                                  whole ripple contribution by z in the graph so menus,
///                                  other scenes and edit mode stay ripple-free)
///
/// A bootstrap spawns one of these (plus WaterRippleBinder) at startup — no scene
/// wiring needed. Place one in a scene yourself only if you want to override the tuning;
/// the bootstrap then skips.
/// </summary>
public class WaterRippleSimRenderer : MonoBehaviour
{
    [Header("Coverage")]
    [Tooltip("World-space size of the simulated area, in meters, centered on the anchor (the bobber). Every ripple source sits at or near the bobber, and damping kills rings within a couple of meters anyway, so this mostly buys texel density: smaller = crisper thin rings.")]
    public float worldSize = 12f;

    [Tooltip("Sim resolution. 768 over 12m is ~1.6cm per texel, enough to keep very thin rings crisp. Raise together with simRate: finer texels tighten the stability limit, so a higher sim rate is what keeps waveSpeed from being silently clamped.")]
    [Range(128, 1024)]
    public int resolution = 768;

    [Header("Waves")]
    [Tooltip("How fast wavefronts travel outward, in m/s. Small pond ripples read best around 0.8-1.5. Internally clamped to what the sim grid can carry stably (raise resolution or sim rate if you hit the clamp and need faster waves).")]
    public float waveSpeed = 0.9f;

    [Tooltip("Fraction of wave energy kept per second. Lower = ripples die out sooner (rings stay local to the bobber instead of crossing the pond). Tune live in play mode.")]
    [Range(0.01f, 0.999f)]
    public float waveDamping = 0.55f;

    [Tooltip("Fraction of raw surface displacement kept per second — bleeds off any standing residue so the surface always settles perfectly flat.")]
    [Range(0.01f, 0.999f)]
    public float heightDamping = 0.85f;

    [Tooltip("Width of the absorbing border band as a fraction of the map, where waves fade out instead of reflecting off the edge.")]
    [Range(0.02f, 0.3f)]
    public float borderFade = 0.12f;

    [Header("Simulation")]
    [Tooltip("Fixed sim steps per second. Wave speed is tied to this being steady. Paired with the resolution: 90Hz is what lets the 768 grid carry 0.9 m/s waves without hitting the stability clamp.")]
    [Range(30, 120)]
    public int simRate = 90;

    [Tooltip("Max sim steps in one frame, so a hitch can't cascade into more hitching. Excess sim time is dropped. Only used in smooth mode (displayFps = 0).")]
    [Range(1, 4)]
    public int maxStepsPerFrame = 3;

    [Header("Ghibli Stepping")]
    [Tooltip("Presentation frame rate for the ripple surface — the anime 'on twos' look. The surface HOLDS each frame and snaps forward this many times per second (12 = classic hand-drawn cadence). Physics still runs at simRate underneath — each snap advances one display frame of waves as a burst of stable substeps — so ripples keep their real speed and only the visible update is stepped. Set 0 for smooth, per-frame updates.")]
    [Range(0, 30)]
    public float displayFps = 24f;

    public static WaterRippleSimRenderer Instance { get; private set; }

    const int MaxStampsPerStep = 16;
    // Ceiling on substeps advanced in a single display snap, so a low displayFps / high
    // simRate combo (or a hitch) can't fire an unbounded blit burst in one frame.
    const int MaxSubStepsPerBurst = 20;

    static readonly int MapID        = Shader.PropertyToID("_WaterRippleMap");
    static readonly int CenterID     = Shader.PropertyToID("_WaterRippleCenter");
    static readonly int ParamsID     = Shader.PropertyToID("_WaterRippleParams");
    static readonly int DeltaUVID    = Shader.PropertyToID("_DeltaUV");
    static readonly int WaveCoeffID  = Shader.PropertyToID("_WaveCoeff");
    static readonly int VelDampID    = Shader.PropertyToID("_VelocityDamp");
    static readonly int HeightDampID = Shader.PropertyToID("_HeightDamp");
    static readonly int BorderID     = Shader.PropertyToID("_BorderFadeUV");
    static readonly int StampsID     = Shader.PropertyToID("_Stamps");
    static readonly int StampCountID = Shader.PropertyToID("_StampCount");

    Material simMat;
    RenderTexture rtA;
    RenderTexture rtB;
    Transform anchor;
    Vector2 simCenter;
    float timeAccumulator;
    float displayAccumulator;
    bool ready;
    bool pushedIdle;

    // Queued impulses in world space: x,y = world XZ, z = radius (m), w = strength.
    // Converted to map UV at consumption so an anchor shift between queue and step
    // can't misplace them.
    readonly List<Vector4> stampQueue = new List<Vector4>();
    readonly Vector4[] stampBuffer = new Vector4[MaxStampsPerStep];

    /// <summary>Anchor the sim to a transform (the bobber). Null pauses the sim and hides the map.</summary>
    public static void SetRippleAnchor(Transform t)
    {
        if (Instance != null) Instance.SetAnchor(t);
    }

    /// <summary>Push the water surface at a world position. Strength is signed sim-height units (~0.1 subtle nudge, ~1 big splash); radius in meters.</summary>
    public static void AddImpulse(Vector3 worldPos, float radiusMeters, float strength)
    {
        if (Instance != null) Instance.QueueStamp(worldPos, radiusMeters, strength);
    }

    void OnEnable()
    {
        if (Instance == null) Instance = this;
        Init();
        PushIdle();
    }

    void OnDisable()
    {
        PushIdle();
        Cleanup();
        if (Instance == this) Instance = null;
    }

    void Init()
    {
        Shader simShader = Resources.Load<Shader>("WaterRippleSim");
        if (simShader == null) simShader = Shader.Find("Hidden/WaterRippleSim");
        if (simShader == null)
        {
            Debug.LogWarning("[WaterRippleSimRenderer] Hidden/WaterRippleSim shader not found. It should live in a Resources folder (Shaders/Ripples/Resources) so builds include it.");
            return;
        }

        simMat = new Material(simShader) { hideFlags = HideFlags.HideAndDontSave };

        var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGHalf, 0)
        {
            sRGB = false,
            useMipMap = false,
            autoGenerateMips = false
        };
        rtA = new RenderTexture(desc) { name = "WaterRipple_A", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        rtB = new RenderTexture(desc) { name = "WaterRipple_B", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        rtA.Create();
        rtB.Create();
        ClearMap();

        ready = true;
    }

    void Cleanup()
    {
        ready = false;
        if (rtA != null) { rtA.Release(); DestroyImmediate(rtA); rtA = null; }
        if (rtB != null) { rtB.Release(); DestroyImmediate(rtB); rtB = null; }
        if (simMat != null) { DestroyImmediate(simMat); simMat = null; }
        stampQueue.Clear();
    }

    void SetAnchor(Transform t)
    {
        if (t == anchor) return;
        anchor = t;
        if (anchor != null)
        {
            // Fresh anchor (a new cast): stale waves from the previous spot make no sense
            // here, so start flat rather than letting a giant UV shift smear them out.
            simCenter = new Vector2(anchor.position.x, anchor.position.z);
            ClearMap();
            stampQueue.Clear();
            timeAccumulator = 0f;
            displayAccumulator = 0f;
        }
    }

    void QueueStamp(Vector3 worldPos, float radiusMeters, float strength)
    {
        if (!ready || anchor == null) return;
        if (stampQueue.Count >= 64) return; // runaway emitter guard
        stampQueue.Add(new Vector4(worldPos.x, worldPos.z, radiusMeters, strength));
    }

    void ClearMap()
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rtA;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = rtB;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = prev;
    }

    void LateUpdate()
    {
        if (!ready) return;

        // No anchor (nothing fishing-related in the water): idle. Publish a black map with
        // the enable flag at 0 once, so the water renders perfectly flat for free.
        if (anchor == null)
        {
            PushIdle();
            return;
        }

        float stepDt = 1f / Mathf.Max(1, simRate);

        if (displayFps <= 0f)
        {
            // Smooth mode: advance and publish every frame.
            timeAccumulator = Mathf.Min(timeAccumulator + Time.deltaTime, stepDt * maxStepsPerFrame);
            if (timeAccumulator >= stepDt)
            {
                Vector2 shift = ConsumeAnchorShift();
                bool first = true;
                while (timeAccumulator >= stepDt)
                {
                    timeAccumulator -= stepDt;
                    RunStep(first ? shift : Vector2.zero, stepDt);
                    first = false;
                }
                PushGlobals();
            }
            return;
        }

        // Ghibli "on twos" mode: hold the frozen surface, then snap it forward on a fixed
        // low-fps display clock. Each snap advances one display frame of physics as a burst
        // of stable substeps, so waves travel at the right speed but the visible surface only
        // updates displayFps times a second.
        float displayInterval = 1f / displayFps;
        displayAccumulator += Time.deltaTime;
        if (displayAccumulator < displayInterval) return; // still holding the current frame

        // Advance exactly one display frame; drop the rest so a hitch can't cascade into a
        // slow-motion catch-up. Any leftover ≥ one interval fires again next frame.
        displayAccumulator = Mathf.Min(displayAccumulator - displayInterval, displayInterval);

        int subSteps = Mathf.Clamp(Mathf.RoundToInt(displayInterval / stepDt), 1, MaxSubStepsPerBurst);
        Vector2 burstShift = ConsumeAnchorShift();
        for (int i = 0; i < subSteps; i++)
        {
            RunStep(i == 0 ? burstShift : Vector2.zero, stepDt);
        }
        PushGlobals();
    }

    // World-anchoring shift since the last sim advance: how far the anchor (bobber) moved,
    // in UV, so the frozen wavefronts stay pinned to world space across the snap.
    Vector2 ConsumeAnchorShift()
    {
        Vector2 newCenter = new Vector2(anchor.position.x, anchor.position.z);
        Vector2 deltaUV = (newCenter - simCenter) / Mathf.Max(0.01f, worldSize);
        simCenter = newCenter;
        return deltaUV;
    }

    void RunStep(Vector2 deltaUV, float stepDt)
    {
        float texelWorld = worldSize / Mathf.Max(1, resolution);
        // CFL stability bound for the explicit wave equation: coeff must stay below ~0.5
        // or the sim explodes. Clamping trades requested wave speed for stability.
        float waveCoeff = Mathf.Min(0.49f, Mathf.Pow(waveSpeed * stepDt / texelWorld, 2f));

        simMat.SetVector(DeltaUVID, new Vector4(deltaUV.x, deltaUV.y, 0f, 0f));
        simMat.SetFloat(WaveCoeffID, waveCoeff);
        simMat.SetFloat(VelDampID, Mathf.Pow(waveDamping, stepDt));
        simMat.SetFloat(HeightDampID, Mathf.Pow(heightDamping, stepDt));
        simMat.SetFloat(BorderID, borderFade);

        // Consume up to MaxStampsPerStep queued impulses (that's a per-step budget, not a
        // live-ripple cap — waves persist in the texture). Leftovers carry to the next step.
        int count = 0;
        int consumed = 0;
        for (int i = 0; i < stampQueue.Count && count < MaxStampsPerStep; i++)
        {
            Vector4 s = stampQueue[i];
            consumed++;
            Vector2 uv = (new Vector2(s.x, s.y) - simCenter) / Mathf.Max(0.01f, worldSize) + new Vector2(0.5f, 0.5f);
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) continue; // outside the map
            stampBuffer[count++] = new Vector4(uv.x, uv.y, s.z / Mathf.Max(0.01f, worldSize), s.w);
        }
        stampQueue.RemoveRange(0, consumed);
        for (int i = count; i < MaxStampsPerStep; i++) stampBuffer[i] = Vector4.zero;
        simMat.SetVectorArray(StampsID, stampBuffer);
        simMat.SetInt(StampCountID, count);

        Graphics.Blit(rtA, rtB, simMat);
        (rtA, rtB) = (rtB, rtA);
    }

    void PushGlobals()
    {
        pushedIdle = false;
        Shader.SetGlobalTexture(MapID, rtA);
        Shader.SetGlobalVector(CenterID, new Vector4(simCenter.x, simCenter.y, 0f, 0f));
        Shader.SetGlobalVector(ParamsID, new Vector4(
            1f / Mathf.Max(0.01f, worldSize),
            1f / Mathf.Max(1, resolution),
            1f, 0f));
    }

    void PushIdle()
    {
        if (pushedIdle) return;
        pushedIdle = true;
        Shader.SetGlobalTexture(MapID, Texture2D.blackTexture);
        Shader.SetGlobalVector(ParamsID, Vector4.zero); // z = 0 disables the ripple contribution
    }

    // Self-bootstrap so no scene/prefab wiring is needed: if no scene-authored instance
    // exists, spawn the sim + the gameplay binder once and keep them across scene loads.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<WaterRippleSimRenderer>(FindObjectsInactive.Include) != null) return;
        var go = new GameObject("WaterRippleSim");
        go.AddComponent<WaterRippleSimRenderer>();
        go.AddComponent<WaterRippleBinder>();
        DontDestroyOnLoad(go);
    }
}
