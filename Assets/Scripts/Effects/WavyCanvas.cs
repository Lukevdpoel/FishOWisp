using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Underwater wave for UI. Put this on a Canvas (or any parent of one) and every child Graphic
// gets a gentle sine-wave displacement, like the whole menu is seen through water.
//
// How: a UGUI Image is a single quad (4 verts), so a shader can't bend it — instead a hidden
// WavyGraphicEffect (BaseMeshEffect, same plumbing as Outline/Shadow) is attached to each child
// Graphic at runtime; it tessellates the mesh into a grid and offsets the vertices. TMP text
// ignores mesh effects entirely, so text is animated through TMP's own vertex-data path
// (cache clean verts, push displaced copies with UpdateVertexData — the VertexJitter pattern).
//
// The wave phase is computed in CANVAS space, so all elements ride the same water instead of
// each wobbling independently. All size knobs are fractions of the canvas height, so the same
// numbers work on the fullscreen overlay HUD and on the tiny world-space notebook page canvases
// (which are ~0.9 units tall).
//
// Notes:
//  - Runtime only (play mode). The helper effects are hidden, not saved, and removed on disable.
//  - Button raycasts still use the undisplaced rects; keep amplitude subtle and nobody notices.
//  - Turn OFF "Pixel Perfect" on the Canvas or it will fight the sub-pixel motion.
[DisallowMultipleComponent]
public class WavyCanvas : MonoBehaviour
{
    const float TwoPi = Mathf.PI * 2f;

    [Header("Wave Feel (sizes are fractions of canvas height)")]
    [Tooltip("How far pixels are pushed at most, as a fraction of canvas height. 0.006 ≈ 6px on a 1080p overlay canvas. Keep it subtle — big values reveal gaps at panel edges.")]
    [Range(0f, 0.05f)] public float amplitude = 0.006f;
    [Tooltip("Length of one wave, as a fraction of canvas height. Bigger = broad lazy swells, smaller = tight shimmering ripples.")]
    [Range(0.02f, 2f)] public float wavelength = 0.35f;
    [Tooltip("Wave cycles per second. Underwater reads best slow (0.2 - 0.5).")]
    public float speed = 0.35f;
    [Tooltip("Vertical bob relative to the horizontal sway. 0 = pure side-to-side, 1 = equal both ways (more 'floaty').")]
    [Range(0f, 1f)] public float verticalSway = 0.6f;

    [Header("Quality")]
    [Tooltip("Target tessellation cell size, as a fraction of canvas height. Smaller = smoother bending on big panels but more verts rebuilt per frame. Text and small icons barely need any.")]
    [Range(0.01f, 0.5f)] public float meshDetail = 0.06f;

    [Header("What To Affect")]
    [Tooltip("Also wave TextMesh Pro text (via TMP vertex animation). Off = text stays crisp while panels sway behind it, which often reads better.")]
    public bool affectText = true;
    [Tooltip("Pick up UI elements spawned after enable (rescans children twice a second). Turn off for static canvases to skip the scan cost.")]
    public bool autoDetectNewElements = true;

    [Header("Timing")]
    [Tooltip("Keep waving while the game is paused via Time.timeScale = 0 (the notebook/pause menus run on unscaled time).")]
    public bool useUnscaledTime = true;

    // --- shared per-frame wave state, read by the WavyGraphicEffects during canvas rebuild ---
    internal RectTransform CanvasRect { get; private set; }
    internal float MaxEdgeSqr { get; private set; }
    internal bool IsAnimating => isActiveAndEnabled && amplitude > 0f;

    float _time;
    float _nextScanAt;
    float _k, _amp, _phase1, _phase2, _phase3, _phase4;

    readonly List<Graphic> _graphics = new List<Graphic>();
    readonly List<Graphic> _scanBuffer = new List<Graphic>();
    readonly List<TMP_Text> _tmpScanBuffer = new List<TMP_Text>();

    class TmpState
    {
        public TMP_Text text;
        public TMP_MeshInfo[] cleanVerts; // null = needs recache (text changed / first frame)
    }
    readonly List<TmpState> _tmpStates = new List<TmpState>();
    readonly HashSet<TMP_Text> _tmpKnown = new HashSet<TMP_Text>();

    void OnEnable()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            Debug.LogWarning($"[WavyCanvas] No Canvas found on/above/below '{name}' — disabling.", this);
            enabled = false;
            return;
        }
        CanvasRect = (RectTransform)canvas.transform;

        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        Scan();
        _nextScanAt = _time + 0.5f;
    }

    void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);

        // Removing the effects dirties each Graphic, so they rebuild clean on their own.
        var effects = GetComponentsInChildren<WavyGraphicEffect>(true);
        for (int i = 0; i < effects.Length; i++)
            if (effects[i].driver == this)
                Destroy(effects[i]);
        _graphics.Clear();

        // Push the cached clean vertices back so text doesn't freeze mid-wave.
        for (int i = 0; i < _tmpStates.Count; i++)
            RestoreText(_tmpStates[i]);
        _tmpStates.Clear();
        _tmpKnown.Clear();
    }

    void Update()
    {
        _time += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        float unit = Mathf.Max(1e-4f, CanvasRect.rect.height);
        float t = _time * speed * TwoPi;
        _k = TwoPi / Mathf.Max(1e-4f, wavelength * unit);
        _amp = amplitude * unit / 1.35f; // the two stacked sines below peak at 1.35
        _phase1 = t;
        _phase2 = -1.6f * t + 1.3f;
        _phase3 = -1.3f * t + 2.7f;
        _phase4 = 0.7f * t + 5.1f;
        float maxEdge = Mathf.Max(1e-3f, meshDetail * unit);
        MaxEdgeSqr = maxEdge * maxEdge;

        if (autoDetectNewElements && _time >= _nextScanAt)
        {
            _nextScanAt = _time + 0.5f;
            Scan();
        }

        // Force each Graphic to rebuild this frame so ModifyMesh re-samples the wave.
        for (int i = _graphics.Count - 1; i >= 0; i--)
        {
            var g = _graphics[i];
            if (g == null) { _graphics.RemoveAt(i); continue; }
            if (g.isActiveAndEnabled) g.SetVerticesDirty();
        }

        if (affectText) AnimateTexts();
    }

    /// <summary>Call after spawning UI under this canvas if autoDetectNewElements is off.</summary>
    public void Refresh() => Scan();

    void Scan()
    {
        _graphics.Clear();
        GetComponentsInChildren(true, _scanBuffer);
        for (int i = 0; i < _scanBuffer.Count; i++)
        {
            var g = _scanBuffer[i];
            // TMP ignores IMeshModifier — its text + sprite submeshes go through the vertex path below.
            if (g is TMP_Text || g is TMP_SubMeshUI) continue;

            var fx = g.GetComponent<WavyGraphicEffect>();
            if (fx == null)
            {
                fx = g.gameObject.AddComponent<WavyGraphicEffect>();
                fx.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
            }
            fx.driver = this;
            _graphics.Add(g);
        }
        _scanBuffer.Clear();

        if (!affectText) return;
        GetComponentsInChildren(true, _tmpScanBuffer);
        for (int i = 0; i < _tmpScanBuffer.Count; i++)
        {
            var t = _tmpScanBuffer[i];
            if (_tmpKnown.Add(t))
                _tmpStates.Add(new TmpState { text = t });
        }
        _tmpScanBuffer.Clear();
    }

    // ---------------- wave function ----------------

    // p is a position in canvas-local space. Two stacked sines per axis at unrelated
    // frequencies/phases so the motion never visibly loops — reads as water, not a metronome.
    internal Vector3 SampleOffset(Vector3 p)
    {
        float x = Mathf.Sin(p.y * _k + _phase1)
                + 0.35f * Mathf.Sin(p.y * _k * 2.7f + _phase2);
        float y = verticalSway * (Mathf.Cos(p.x * _k * 0.8f + _phase3)
                + 0.35f * Mathf.Sin(p.x * _k * 2.2f + _phase4));
        return new Vector3(x * _amp, y * _amp, 0f);
    }

    // ---------------- TMP text path ----------------

    void AnimateTexts()
    {
        for (int i = _tmpStates.Count - 1; i >= 0; i--)
        {
            var st = _tmpStates[i];
            if (st.text == null) { _tmpStates.RemoveAt(i); continue; }
            var text = st.text;
            if (!text.isActiveAndEnabled) continue;

            var ti = text.textInfo;
            if (ti == null || ti.meshInfo == null) continue;

            if (st.cleanVerts == null || st.cleanVerts.Length != ti.meshInfo.Length)
            {
                // CopyMeshInfoVertexData dereferences every meshInfo's vertex array; a text whose
                // geometry TMP hasn't generated yet (null vertices) would throw. Skip until ready.
                bool ready = true;
                for (int m = 0; m < ti.meshInfo.Length; m++)
                    if (ti.meshInfo[m].vertices == null) { ready = false; break; }
                if (!ready) continue;
                st.cleanVerts = ti.CopyMeshInfoVertexData();
            }

            Matrix4x4 toCanvas = CanvasRect.worldToLocalMatrix * text.transform.localToWorldMatrix;
            Matrix4x4 fromCanvas = toCanvas.inverse;

            bool dirty = false;
            for (int m = 0; m < ti.meshInfo.Length; m++)
            {
                var src = st.cleanVerts[m].vertices;
                var dst = ti.meshInfo[m].vertices;
                if (src == null || dst == null) continue;
                if (src.Length != dst.Length) { st.cleanVerts = null; break; } // recache next frame

                for (int v = 0; v < src.Length; v++)
                {
                    Vector3 cp = toCanvas.MultiplyPoint3x4(src[v]);
                    dst[v] = fromCanvas.MultiplyPoint3x4(cp + SampleOffset(cp));
                }
                dirty = true;
            }
            if (dirty) text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }
    }

    void OnTextChanged(Object obj)
    {
        // TMP regenerated this text's geometry fresh (content/layout change) — the old clean copy
        // is stale, recache from the fresh verts on the next Update.
        var t = obj as TMP_Text;
        if (t == null) return;
        for (int i = 0; i < _tmpStates.Count; i++)
        {
            if (_tmpStates[i].text == t) { _tmpStates[i].cleanVerts = null; return; }
        }
    }

    void RestoreText(TmpState st)
    {
        if (st.text == null || st.cleanVerts == null) return;
        var ti = st.text.textInfo;
        if (ti == null || ti.meshInfo == null || ti.meshInfo.Length != st.cleanVerts.Length) return;

        for (int m = 0; m < ti.meshInfo.Length; m++)
        {
            var src = st.cleanVerts[m].vertices;
            var dst = ti.meshInfo[m].vertices;
            if (src != null && dst != null && src.Length == dst.Length)
                System.Array.Copy(src, dst, src.Length);
        }
        st.text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }
}

// Hidden helper added to each child Graphic by WavyCanvas. Tessellates the graphic's mesh so
// there are enough vertices to bend, then displaces them with the driver's shared wave — all in
// canvas space, so neighbouring elements stay continuous with each other.
[DisallowMultipleComponent]
public class WavyGraphicEffect : BaseMeshEffect
{
    [System.NonSerialized] public WavyCanvas driver;

    const int MaxSubdivDepth = 12;     // longest-edge bisection: 2^12 tris max per source tri
    const int MaxOutputVerts = 63000;  // stay under the 65535 mesh limit

    static readonly List<UIVertex> s_in = new List<UIVertex>(256);
    static readonly List<UIVertex> s_out = new List<UIVertex>(1024);

    float _maxEdgeSqr;

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || driver == null || !driver.IsAnimating) return;
        var canvasRect = driver.CanvasRect;
        if (canvasRect == null) return;

        s_in.Clear();
        vh.GetUIVertexStream(s_in);
        int count = s_in.Count;
        if (count < 3) return;

        Matrix4x4 toCanvas = canvasRect.worldToLocalMatrix * transform.localToWorldMatrix;
        Matrix4x4 fromCanvas = toCanvas.inverse;

        // Work in canvas space so edge lengths and wave phase are consistent across all elements.
        for (int i = 0; i < count; i++)
        {
            var v = s_in[i];
            v.position = toCanvas.MultiplyPoint3x4(v.position);
            s_in[i] = v;
        }

        _maxEdgeSqr = driver.MaxEdgeSqr;
        s_out.Clear();
        for (int i = 0; i + 2 < count; i += 3)
            Subdivide(s_in[i], s_in[i + 1], s_in[i + 2], 0);

        for (int i = 0; i < s_out.Count; i++)
        {
            var v = s_out[i];
            Vector3 p = v.position;
            v.position = fromCanvas.MultiplyPoint3x4(p + driver.SampleOffset(p));
            s_out[i] = v;
        }

        vh.Clear();
        vh.AddUIVertexTriangleStream(s_out);
    }

    // Splits the longest edge at its midpoint until every edge fits the target cell size.
    // Midpoint splits share vertices along shared edges, so no cracks between the halves.
    void Subdivide(in UIVertex a, in UIVertex b, in UIVertex c, int depth)
    {
        if (depth < MaxSubdivDepth && s_out.Count < MaxOutputVerts)
        {
            float ab = (a.position - b.position).sqrMagnitude;
            float bc = (b.position - c.position).sqrMagnitude;
            float ca = (c.position - a.position).sqrMagnitude;

            if (ab > _maxEdgeSqr && ab >= bc && ab >= ca)
            {
                UIVertex m = Mid(a, b);
                Subdivide(a, m, c, depth + 1);
                Subdivide(m, b, c, depth + 1);
                return;
            }
            if (bc > _maxEdgeSqr && bc >= ca)
            {
                UIVertex m = Mid(b, c);
                Subdivide(a, b, m, depth + 1);
                Subdivide(a, m, c, depth + 1);
                return;
            }
            if (ca > _maxEdgeSqr)
            {
                UIVertex m = Mid(c, a);
                Subdivide(a, b, m, depth + 1);
                Subdivide(m, b, c, depth + 1);
                return;
            }
        }

        s_out.Add(a);
        s_out.Add(b);
        s_out.Add(c);
    }

    static UIVertex Mid(in UIVertex a, in UIVertex b)
    {
        UIVertex v = default;
        v.position = (a.position + b.position) * 0.5f;
        v.normal = (a.normal + b.normal) * 0.5f;
        v.tangent = (a.tangent + b.tangent) * 0.5f;
        v.color = Color32.Lerp(a.color, b.color, 0.5f);
        v.uv0 = (a.uv0 + b.uv0) * 0.5f;
        v.uv1 = (a.uv1 + b.uv1) * 0.5f;
        v.uv2 = (a.uv2 + b.uv2) * 0.5f;
        v.uv3 = (a.uv3 + b.uv3) * 0.5f;
        return v;
    }
}
