using UnityEngine;

/// <summary>
/// Makes the moss overlay sample its parent (the base rock's) lightmap, so the underlying-albedo edge
/// is lit identically to the real surface.
///
/// Needed because Unity re-assigns Renderer.lightmapIndex on scene load, on entering Play mode, and on
/// every re-bake, based on the baked LightingData asset. The overlay is non-static so it's not in that
/// data and gets reset to "not lightmapped" (falls back to light probes). The parent rock IS in the
/// baked data and keeps its valid index, so we just re-copy it onto the overlay whenever it drifts.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
[DisallowMultipleComponent]
public class MossLightmapLink : MonoBehaviour
{
    private Renderer m_Self;
    private Renderer m_Parent;

    private void OnEnable()
    {
        Cache();
        Apply();
    }

    private void Cache()
    {
        m_Self = GetComponent<Renderer>();
        m_Parent = transform.parent != null ? transform.parent.GetComponent<Renderer>() : null;
    }

    // Runs in edit mode too (ExecuteAlways). Cheap: only writes when the index actually drifted.
    private void LateUpdate()
    {
        Apply();
    }

    private void Apply()
    {
        if (m_Self == null || m_Parent == null)
        {
            Cache();
            if (m_Self == null || m_Parent == null)
                return;
        }

        if (m_Self.lightmapIndex != m_Parent.lightmapIndex ||
            m_Self.lightmapScaleOffset != m_Parent.lightmapScaleOffset)
        {
            m_Self.lightmapIndex = m_Parent.lightmapIndex;
            m_Self.lightmapScaleOffset = m_Parent.lightmapScaleOffset;
        }
    }
}
