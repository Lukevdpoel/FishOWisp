using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Marks a light-decal projector as one that should project onto the moss (Fur/Shell/Lit Baked).
/// Put this next to a DecalProjector. It registers with <see cref="MossDecalManager"/>, which gathers
/// all of them and uploads their box transforms + colours to the moss shader each frame, so the moss
/// adds the projected light in its own shading. This works on the transparent moss (soft fade intact)
/// because it doesn't rely on the decal render order or the depth buffer.
///
/// All moss-decals share one texture (assign it on any of them or leave blank to use the projector
/// material's main texture); position, colour and intensity are per-decal.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class LightDecalToMoss : MonoBehaviour
{
    [Tooltip("Texture to project. If empty, uses the DecalProjector material's main texture. Shared across all moss decals.")]
    public Texture decalTexture;

    [ColorUsage(false, true)]
    public Color color = Color.white;

    [Min(0f)]
    public float intensity = 1f;

    [Tooltip("Projector box size (w, h, depth). Ignored if a DecalProjector is present (its size is used).")]
    public Vector3 size = Vector3.one;
    public Vector3 pivot = Vector3.zero;

    private void OnEnable() => MossDecalManager.GetOrCreate().Register(this);
    private void OnDisable() { if (MossDecalManager.HasInstance) MossDecalManager.Instance.Unregister(this); }

    /// <summary>world -> [0,1]^3 projector box matrix.</summary>
    public Matrix4x4 GetMatrix()
    {
        Vector3 s = size;
        Vector3 p = pivot;

        var projector = GetComponent<DecalProjector>();
        if (projector != null)
        {
            s = projector.size;
            p = projector.pivot;
        }

        s = new Vector3(Mathf.Max(s.x, 1e-4f), Mathf.Max(s.y, 1e-4f), Mathf.Max(s.z, 1e-4f));

        return Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0.5f)) *
               Matrix4x4.Scale(new Vector3(1f / s.x, 1f / s.y, 1f / s.z)) *
               Matrix4x4.Translate(-p) *
               transform.worldToLocalMatrix;
    }

    public Vector4 GetColor() => new Vector4(color.r, color.g, color.b, intensity);

    public Texture GetTexture()
    {
        if (decalTexture != null)
            return decalTexture;
        var projector = GetComponent<DecalProjector>();
        return projector != null && projector.material != null ? projector.material.mainTexture : null;
    }
}
