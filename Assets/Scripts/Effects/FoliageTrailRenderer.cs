using UnityEngine;

/// <summary>
/// Maintains a small RenderTexture trail map centered on the player.
/// Each update: ping-pong blit through a fade+stamp shader. The trail
/// shifts in UV space to stay anchored to world XZ as the player moves.
///
/// Exposes globals consumed by the grass shader:
///   _GrassTrailMap     (Texture)  - R8, 0..1 = recently trampled
///   _GrassTrailCenter  (Vector4)  - xy = player XZ this frame
///   _GrassTrailParams  (Vector4)  - x = 1 / worldSize
///
/// Attach to the Player.
/// </summary>
[ExecuteAlways]
public class FoliageTrailRenderer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Optional. If null, uses this transform.")]
    public Transform player;

    [Header("Coverage")]
    [Tooltip("World-space size of the trail area, in meters. Player stays at center.")]
    public float worldSize = 20f;

    [Tooltip("RT resolution. 256 is plenty for grass. 512 if you want sharper edges.")]
    [Range(64, 512)]
    public int resolution = 256;

    [Header("Trail")]
    [Tooltip("Fraction of trail intensity remaining after 1 second. 1 = never fades, 0.1 = very fast fade. Frame-rate independent.")]
    [Range(0.05f, 0.999f)]
    public float fadePerSecond = 0.6f;

    [Tooltip("Stamp radius as a fraction of worldSize. 0.05 with worldSize=20 -> ~1m radius.")]
    [Range(0.005f, 0.2f)]
    public float stampRadiusUV = 0.05f;

    [Range(0f, 1f)]
    public float stampStrength = 1f;

    [Header("Performance")]
    [Tooltip("Update every N frames. 2 = 30Hz at 60fps. 1 = every frame.")]
    [Range(1, 4)]
    public int updateInterval = 2;

    static readonly int TrailMapID    = Shader.PropertyToID("_GrassTrailMap");
    static readonly int TrailCenterID = Shader.PropertyToID("_GrassTrailCenter");
    static readonly int TrailParamsID = Shader.PropertyToID("_GrassTrailParams");
    static readonly int FadeAmountID  = Shader.PropertyToID("_FadeAmount");
    static readonly int DeltaUVID     = Shader.PropertyToID("_DeltaUV");
    static readonly int StampRadID    = Shader.PropertyToID("_StampRadiusUV");
    static readonly int StampStrID    = Shader.PropertyToID("_StampStrength");

    Shader updateShader;
    Material updateMat;
    RenderTexture rtA;
    RenderTexture rtB;
    Vector2 lastCenter;
    float lastTickTime;
    int frameCounter;
    bool ready;

    void OnEnable()
    {
        Init();
    }

    void OnDisable()
    {
        Cleanup();
    }

    void Init()
    {
        updateShader = Shader.Find("Hidden/GrassTrailUpdate");
        if (updateShader == null)
        {
            Debug.LogWarning("[FoliageTrailRenderer] Hidden/GrassTrailUpdate shader not found. Make sure it's included in Always Included Shaders or Resources.");
            return;
        }

        updateMat = new Material(updateShader) { hideFlags = HideFlags.HideAndDontSave };

        var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.R8, 0)
        {
            sRGB = false,
            useMipMap = false,
            autoGenerateMips = false
        };
        rtA = new RenderTexture(desc) { name = "GrassTrail_A", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        rtB = new RenderTexture(desc) { name = "GrassTrail_B", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        rtA.Create();
        rtB.Create();
        ClearRT(rtA);
        ClearRT(rtB);

        lastCenter = GetXZ();
        lastTickTime = Time.time;
        ready = true;
    }

    void Cleanup()
    {
        ready = false;
        if (rtA != null) { rtA.Release(); DestroyImmediate(rtA); rtA = null; }
        if (rtB != null) { rtB.Release(); DestroyImmediate(rtB); rtB = null; }
        if (updateMat != null) { DestroyImmediate(updateMat); updateMat = null; }
    }

    static void ClearRT(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = prev;
    }

    Transform Target => player != null ? player : transform;

    Vector2 GetXZ()
    {
        Vector3 p = Target.position;
        return new Vector2(p.x, p.z);
    }

    void LateUpdate()
    {
        if (!ready) return;

        frameCounter++;
        bool tick = (frameCounter % updateInterval) == 0;

        if (tick)
        {
            Vector2 newCenter = GetXZ();
            Vector2 delta = newCenter - lastCenter;
            Vector2 deltaUV = delta / worldSize;
            lastCenter = newCenter;

            // Time-based fade so the trail looks identical across frame rates.
            float dt = Mathf.Max(0f, Time.time - lastTickTime);
            lastTickTime = Time.time;
            float effectiveFade = Mathf.Pow(fadePerSecond, dt);

            updateMat.SetFloat(FadeAmountID, effectiveFade);
            updateMat.SetVector(DeltaUVID, new Vector4(deltaUV.x, deltaUV.y, 0, 0));
            updateMat.SetFloat(StampRadID, stampRadiusUV);
            updateMat.SetFloat(StampStrID, stampStrength);

            Graphics.Blit(rtA, rtB, updateMat);
            (rtA, rtB) = (rtB, rtA);
        }

        PushGlobals();
    }

    void PushGlobals()
    {
        Vector2 c = GetXZ();
        Shader.SetGlobalTexture(TrailMapID, rtA);
        Shader.SetGlobalVector(TrailCenterID, new Vector4(c.x, c.y, 0, 0));
        Shader.SetGlobalVector(TrailParamsID, new Vector4(1f / Mathf.Max(0.01f, worldSize), 0, 0, 0));
    }

    void OnDrawGizmosSelected()
    {
        var t = Target;
        if (t == null) return;
        Vector3 p = t.position;
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.4f);
        Gizmos.DrawWireCube(new Vector3(p.x, p.y, p.z), new Vector3(worldSize, 0.05f, worldSize));
    }
}
