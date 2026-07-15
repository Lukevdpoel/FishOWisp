using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shellshader.EditorTools
{
    /// <summary>
    /// Bakes a mesh into a stacked "shell" mesh for the geometry-shader-free shader
    /// "Fur/Shell/Lit Baked". The source geometry is duplicated N times; each copy's vertices
    /// carry their shell index in UV3.x and the normalized layer (index / shellCount) in UV3.y.
    /// The vertex shader offsets each copy outward along the normal, so the whole shell stack
    /// renders in a single draw call with no geometry shader.
    /// </summary>
    public class ShellMeshBaker : EditorWindow
    {
        [SerializeField] private Mesh sourceMesh;
        [SerializeField] private int shellCount = 8;
        [SerializeField] private float boundsPadding = 0.1f;
        [SerializeField] private bool createOverlayChild = true;
        [SerializeField] private Material overlayMaterial;

        [MenuItem("Tools/Shell Fur/Bake Shell Mesh")]
        private static void Open() => GetWindow<ShellMeshBaker>("Shell Mesh Baker");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Bakes N stacked copies of a mesh (shell index in UV3) for the \"Fur/Shell/Lit Baked\" " +
                "vertex-shader shader. No geometry shader at runtime.\n\n" +
                "The baked mesh is an OVERLAY: the base object keeps its own mesh + material; the moss " +
                "renders on a separate child object with this mesh + a moss material (discarding where " +
                "unpainted). Do NOT put the moss material as a 2nd slot on the base mesh.\n\n" +
                "Bounds Padding should be >= _ShellStep * (ShellCount-1) so the pushed-out shells " +
                "aren't frustum-culled at screen edges.",
                MessageType.Info);

            if (sourceMesh == null)
                sourceMesh = TryGetSelectedMesh();

            sourceMesh = (Mesh)EditorGUILayout.ObjectField("Source Mesh", sourceMesh, typeof(Mesh), false);
            shellCount = Mathf.Clamp(EditorGUILayout.IntField("Shell Count", shellCount), 1, 64);
            boundsPadding = Mathf.Max(0f, EditorGUILayout.FloatField("Bounds Padding", boundsPadding));
            createOverlayChild = EditorGUILayout.Toggle("Create moss overlay child", createOverlayChild);
            using (new EditorGUI.DisabledScope(!createOverlayChild))
                overlayMaterial = (Material)EditorGUILayout.ObjectField(
                    "Moss Material", overlayMaterial, typeof(Material), false);

            if (sourceMesh != null)
            {
                long verts = (long)sourceMesh.vertexCount * shellCount;
                EditorGUILayout.LabelField("Baked vertex count", $"{verts:N0}" +
                    (verts > 65000 ? "  (32-bit index)" : ""));
            }

            using (new EditorGUI.DisabledScope(sourceMesh == null))
            {
                if (GUILayout.Button("Bake"))
                    Bake();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Batch", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Multi-select painted objects (e.g. all your rocks) and bake them all at once with the " +
                "settings above. Skips anything already baked (has a MossShellOverlay child) or with no " +
                "paint found. Uses the Moss Material for every overlay.",
                MessageType.None);

            int selCount = Selection.gameObjects.Length;
            using (new EditorGUI.DisabledScope(selCount == 0))
            {
                if (GUILayout.Button($"Bake Selected Objects ({selCount})"))
                    BakeSelection();
            }
        }

        private static Mesh TryGetSelectedMesh()
        {
            var go = Selection.activeGameObject;
            if (go == null) return null;
            var mf = go.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        private void Bake()
        {
            GameObject go = Selection.activeGameObject;

            // ALWAYS prefer the selected object's own mesh so the Source Mesh field can't go stale
            // (baking one rock's mesh onto a different rock is exactly the "wrong shape/place" bug).
            Mesh mesh = sourceMesh;
            if (go != null)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    mesh = mf.sharedMesh;
            }
            if (mesh == null)
            {
                Debug.LogWarning("[ShellMeshBaker] No mesh to bake — select an object with a MeshFilter, or set Source Mesh.");
                return;
            }

            // Polybrush (in AdditionalVertexStream mode) keeps painted colors off the main mesh, in
            // MeshRenderer.additionalVertexStreams. Capture them here so they bake into a real color
            // buffer — otherwise the mask reads empty at runtime and all shells get culled in Play mode.
            Color[] paintColors = GatherPaintColors(go, mesh);

            Mesh baked = BuildShellMesh(mesh, shellCount, boundsPadding, paintColors);
            string path = SaveBaked(baked, mesh.name);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ShellMeshBaker] Baked '{path}' — {baked.vertexCount:N0} verts, {shellCount} shells.", baked);

            if (createOverlayChild && go != null)
                CreateOverlay(go, baked, overlayMaterial);

            EditorGUIUtility.PingObject(baked);
        }

        private void BakeSelection()
        {
            GameObject[] objs = Selection.gameObjects;
            int baked = 0;
            var skipped = new List<string>();
            try
            {
                for (int i = 0; i < objs.Length; i++)
                {
                    GameObject go = objs[i];
                    EditorUtility.DisplayProgressBar("Baking Shell Meshes",
                        $"{go.name} ({i + 1}/{objs.Length})", (float)i / objs.Length);

                    if (TryBakeObject(go, out string reason)) baked++;
                    else skipped.Add($"{go.name} — {reason}");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            string msg = $"[ShellMeshBaker] Batch done: baked {baked}, skipped {skipped.Count}.";
            if (skipped.Count > 0)
                msg += "\nSkipped:\n  " + string.Join("\n  ", skipped);
            Debug.Log(msg);
        }

        /// <summary>Bakes one object's painted mesh into a shell overlay. Returns false (with a reason) if skipped.</summary>
        private bool TryBakeObject(GameObject go, out string reason)
        {
            reason = null;
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) { reason = "no MeshFilter/mesh"; return false; }
            if (go.transform.Find("MossShellOverlay") != null) { reason = "already baked"; return false; }

            Color[] colors = GatherPaintColors(go, mf.sharedMesh, verbose: false);
            if (colors == null) { reason = "no paint found"; return false; }

            Mesh baked = BuildShellMesh(mf.sharedMesh, shellCount, boundsPadding, colors);
            SaveBaked(baked, mf.sharedMesh.name);
            CreateOverlay(go, baked, overlayMaterial);
            return true;
        }

        private string SaveBaked(Mesh baked, string srcName)
        {
            const string folder = "Assets/Shellshader/BakedMoss";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/Shellshader", "BakedMoss");
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{srcName}_Shell{shellCount}.asset");
            AssetDatabase.CreateAsset(baked, path);
            return path;
        }

        /// <summary>
        /// Returns the painted vertex colors for the mesh, preferring Polybrush's
        /// additionalVertexStreams (its edit-time paint target) and falling back to the mesh's own
        /// colors. Returns null if neither has a matching color buffer.
        /// </summary>
        private static Color[] GatherPaintColors(GameObject selected, Mesh mesh, bool verbose = true)
        {
            int vc = mesh.vertexCount;

            var mr = selected != null ? selected.GetComponent<MeshRenderer>() : null;
            if (mr != null && mr.additionalVertexStreams != null)
            {
                Color[] streamColors = mr.additionalVertexStreams.colors;
                if (streamColors != null && streamColors.Length == vc)
                {
                    if (verbose) Debug.Log("[ShellMeshBaker] Captured paint from Polybrush additionalVertexStreams.");
                    return streamColors;
                }
            }

            Color[] meshColors = mesh.colors;
            if (meshColors != null && meshColors.Length == vc)
            {
                if (verbose) Debug.Log("[ShellMeshBaker] Using vertex colors baked into the source mesh.");
                return meshColors;
            }

            if (verbose)
                Debug.LogWarning("[ShellMeshBaker] No painted vertex colors found — moss will default to " +
                    "fully covered (white). If you painted with Polybrush, keep the painted object selected " +
                    "when baking so its additionalVertexStreams can be read.");
            return null;
        }

        private static void CreateOverlay(GameObject baseObj, Mesh bakedMesh, Material mossMat)
        {
            var overlay = new GameObject("MossShellOverlay");
            Undo.RegisterCreatedObjectUndo(overlay, "Create Moss Overlay");
            overlay.transform.SetParent(baseObj.transform, false);
            overlay.transform.localPosition = Vector3.zero;
            overlay.transform.localRotation = Quaternion.identity;
            overlay.transform.localScale = Vector3.one;

            overlay.AddComponent<MeshFilter>().sharedMesh = bakedMesh;
            var mr = overlay.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mossMat; // may be null; assign a "Fur/Shell/Lit Baked" material in the inspector
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // shader has no ShadowCaster pass anyway
            // NOTE: the soft edge blends into _UnderMap (the surface's albedo). Because all these rocks
            // share ONE atlas texture, assign that atlas to the moss material's "Underlying Albedo"
            // slot ONCE — each overlay samples it at its own UVs. (Per-object texture assignment isn't
            // possible here: MaterialPropertyBlock can't override textures under the SRP Batcher.)

            // Make the overlay sample the base object's LIGHTMAP so the underlying-albedo edge is lit
            // the same way as the real (lightmapped) surface — else it falls back to light probes and
            // the edge colour never matches. Requires the base to be baked FIRST and the baked mesh to
            // carry the base's UV1 (it does). A MossLightmapLink component re-applies the parent's
            // lightmap at runtime, because Unity RESETS a non-static renderer's lightmapIndex on
            // scene load / Play / re-bake (a one-time set here wouldn't survive).
            var baseRenderer = baseObj.GetComponent<Renderer>();
            if (baseRenderer != null && baseRenderer.lightmapIndex >= 0 && baseRenderer.lightmapIndex < 65534)
            {
                mr.lightmapIndex = baseRenderer.lightmapIndex;
                mr.lightmapScaleOffset = baseRenderer.lightmapScaleOffset;
            }
            overlay.AddComponent<MossLightmapLink>();

            if (mossMat == null)
                Debug.LogWarning("[ShellMeshBaker] Overlay created without a moss material — assign a " +
                    "'Fur/Shell/Lit Baked' material to MossShellOverlay's MeshRenderer.", overlay);

            Selection.activeGameObject = overlay;
        }

        /// <summary>Builds the stacked shell mesh. Public so it can be driven from other tools/tests.</summary>
        /// <param name="colorOverride">Painted vertex colors to bake in (e.g. from Polybrush). If null, falls back to src.colors, then white.</param>
        public static Mesh BuildShellMesh(Mesh src, int shells, float padding, Color[] colorOverride = null)
        {
            shells = Mathf.Clamp(shells, 1, 64);
            int vc = src.vertexCount;

            Vector3[] srcVerts = src.vertices;
            Vector3[] srcNormals = src.normals;
            Vector4[] srcTangents = src.tangents;
            Color[] srcColors = (colorOverride != null && colorOverride.Length == vc) ? colorOverride : src.colors;
            var srcUV = new List<Vector2>();
            src.GetUVs(0, srcUV);
            var srcUV1 = new List<Vector2>();
            src.GetUVs(1, srcUV1); // lightmap UVs — carried so the overlay can share the base's lightmap

            bool hasNormals = srcNormals != null && srcNormals.Length == vc;
            bool hasTangents = srcTangents != null && srcTangents.Length == vc;
            bool hasUV = srcUV.Count == vc;
            bool hasUV1 = srcUV1.Count == vc;
            bool hasColors = srcColors != null && srcColors.Length == vc;

            int total = vc * shells;
            var verts = new List<Vector3>(total);
            var normals = hasNormals ? new List<Vector3>(total) : null;
            var tangents = hasTangents ? new List<Vector4>(total) : null;
            var uv0 = hasUV ? new List<Vector2>(total) : null;
            var uv1 = hasUV1 ? new List<Vector2>(total) : null;
            var colors = new List<Color>(total);
            var shellData = new List<Vector2>(total);

            for (int s = 0; s < shells; s++)
            {
                float layer01 = (float)s / shells;
                for (int v = 0; v < vc; v++)
                {
                    verts.Add(srcVerts[v]);
                    if (hasNormals) normals.Add(srcNormals[v]);
                    if (hasTangents) tangents.Add(srcTangents[v]);
                    if (hasUV) uv0.Add(srcUV[v]);
                    if (hasUV1) uv1.Add(srcUV1[v]);
                    colors.Add(hasColors ? srcColors[v] : Color.white);
                    shellData.Add(new Vector2(s, layer01));
                }
            }

            var mesh = new Mesh { name = $"{src.name}_Shell{shells}" };
            mesh.indexFormat = total > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            if (hasNormals) mesh.SetNormals(normals);
            if (hasTangents) mesh.SetTangents(tangents);
            if (hasUV) mesh.SetUVs(0, uv0);
            if (hasUV1) mesh.SetUVs(1, uv1);
            mesh.SetColors(colors);
            mesh.SetUVs(3, shellData);

            mesh.subMeshCount = src.subMeshCount;
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                int[] srcTris = src.GetTriangles(sm);
                var tris = new List<int>(srcTris.Length * shells);
                for (int s = 0; s < shells; s++)
                {
                    int offset = s * vc;
                    for (int t = 0; t < srcTris.Length; t++)
                        tris.Add(srcTris[t] + offset);
                }
                mesh.SetTriangles(tris, sm);
            }

            if (!hasNormals) mesh.RecalculateNormals();
            if (!hasTangents) mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            // Shells push outward in the vertex shader beyond the source bounds; pad so they
            // don't get frustum-culled at screen edges.
            Bounds b = mesh.bounds;
            b.Expand(padding * 2f);
            mesh.bounds = b;

            return mesh;
        }
    }
}
