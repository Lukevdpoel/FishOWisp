using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Collects every active <see cref="LightDecalToMoss"/> in the scene and uploads their box matrices,
/// colours and shared texture to the moss shader (Fur/Shell/Lit Baked) as global arrays each frame.
/// Auto-created on demand; hidden and not saved into the scene.
/// </summary>
[ExecuteAlways]
public class MossDecalManager : MonoBehaviour
{
    public const int MaxDecals = 16; // must match MOSS_DECAL_MAX in Param.hlsl

    private static MossDecalManager s_Instance;
    public static bool HasInstance => s_Instance != null;
    public static MossDecalManager Instance => s_Instance;

    private static readonly int k_Matrices = Shader.PropertyToID("_MossDecalMatrices");
    private static readonly int k_Colors = Shader.PropertyToID("_MossDecalColors");
    private static readonly int k_Count = Shader.PropertyToID("_MossDecalCount");
    private static readonly int k_Tex = Shader.PropertyToID("_MossDecalTex");

    private readonly List<LightDecalToMoss> m_Decals = new List<LightDecalToMoss>();
    private readonly Matrix4x4[] m_Matrices = new Matrix4x4[MaxDecals];
    private readonly Vector4[] m_Colors = new Vector4[MaxDecals];

    public static MossDecalManager GetOrCreate()
    {
        if (s_Instance != null)
            return s_Instance;

        s_Instance = FindObjectOfType<MossDecalManager>();
        if (s_Instance == null)
        {
            var go = new GameObject("MossDecalManager") { hideFlags = HideFlags.HideAndDontSave };
            s_Instance = go.AddComponent<MossDecalManager>();
        }
        return s_Instance;
    }

    private void OnEnable() => s_Instance = this;

    public void Register(LightDecalToMoss d)
    {
        if (d != null && !m_Decals.Contains(d))
            m_Decals.Add(d);
    }

    public void Unregister(LightDecalToMoss d) => m_Decals.Remove(d);

    private void LateUpdate()
    {
        int count = 0;
        Texture tex = null;

        for (int i = 0; i < m_Decals.Count && count < MaxDecals; i++)
        {
            var d = m_Decals[i];
            if (d == null || !d.isActiveAndEnabled)
                continue;

            m_Matrices[count] = d.GetMatrix();
            m_Colors[count] = d.GetColor();
            if (tex == null)
                tex = d.GetTexture();
            count++;
        }

        Shader.SetGlobalInt(k_Count, count);
        if (count > 0)
        {
            Shader.SetGlobalMatrixArray(k_Matrices, m_Matrices);
            Shader.SetGlobalVectorArray(k_Colors, m_Colors);
            if (tex != null)
                Shader.SetGlobalTexture(k_Tex, tex);
        }
    }
}
