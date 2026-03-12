using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

public class TextureAtlasPacker : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Drag the parent GameObjects (your 8 variants) here.")]
    public GameObject[] objectsToCombine;
    public int maxAtlasSize = 4096;
    public int padding = 2;
    public string shaderName = "Universal Render Pipeline/Lit";

    [Header("Save Settings")]
    [Tooltip("The folder path in your project where the new assets will be saved.")]
    public string saveFolder = "Assets/OptimizedRocks";

    [ContextMenu("Pack Textures, Update UVs, and SAVE")]
    public void PackAndMap()
    {
        List<MeshRenderer> allRenderers = new List<MeshRenderer>();
        List<MeshFilter> allFilters = new List<MeshFilter>();
        List<Texture2D> uniqueTextures = new List<Texture2D>();

        // 1. Collect components
        foreach (GameObject rootObj in objectsToCombine)
        {
            MeshRenderer[] renderers = rootObj.GetComponentsInChildren<MeshRenderer>();
            MeshFilter[] filters = rootObj.GetComponentsInChildren<MeshFilter>();

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].sharedMaterial != null && renderers[i].sharedMaterial.mainTexture != null)
                {
                    Texture2D tex = renderers[i].sharedMaterial.mainTexture as Texture2D;
                    if (tex != null)
                    {
                        allRenderers.Add(renderers[i]);
                        allFilters.Add(filters[i]);

                        if (!uniqueTextures.Contains(tex))
                            uniqueTextures.Add(tex);
                    }
                }
            }
        }

        if (uniqueTextures.Count == 0) return;

        // 2. Pack the textures
        Texture2D atlas = new Texture2D(maxAtlasSize, maxAtlasSize);
        Rect[] atlasRects = atlas.PackTextures(uniqueTextures.ToArray(), padding, maxAtlasSize);

        Dictionary<Texture2D, Rect> textureToRectMap = new Dictionary<Texture2D, Rect>();
        for (int i = 0; i < uniqueTextures.Count; i++)
        {
            textureToRectMap.Add(uniqueTextures[i], atlasRects[i]);
        }

#if UNITY_EDITOR
        // --- THIS IS THE NEW SAVING LOGIC ---

        // Create the directory if it doesn't exist
        if (!Directory.Exists(saveFolder))
        {
            Directory.CreateDirectory(saveFolder);
        }

        // 3a. Save the Atlas Texture as a PNG
        byte[] bytes = atlas.EncodeToPNG();
        string texPath = saveFolder + "/RockAtlas.png";
        File.WriteAllBytes(texPath, bytes);
        AssetDatabase.ImportAsset(texPath);

        // Load the saved texture from the hard drive so we reference the permanent one
        Texture2D savedAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        // 3b. Create and Save the Material
        Material combinedMaterial = new Material(Shader.Find(shaderName));
        combinedMaterial.mainTexture = savedAtlas;
        string matPath = saveFolder + "/RockCombinedMaterial.mat";
        AssetDatabase.CreateAsset(combinedMaterial, matPath);

        // 4. Remap UVs and Save the newly generated Meshes
        for (int i = 0; i < allRenderers.Count; i++)
        {
            // Instantiate a copy of the original mesh so we don't overwrite the original asset
            Mesh newMesh = Instantiate(allFilters[i].sharedMesh);
            newMesh.name = allFilters[i].sharedMesh.name + "_Remapped";

            Vector2[] uvs = newMesh.uv;
            Texture2D originalTex = allRenderers[i].sharedMaterial.mainTexture as Texture2D;
            Rect uvRect = textureToRectMap[originalTex];

            for (int j = 0; j < uvs.Length; j++)
            {
                uvs[j].x = Mathf.Lerp(uvRect.xMin, uvRect.xMax, uvs[j].x);
                uvs[j].y = Mathf.Lerp(uvRect.yMin, uvRect.yMax, uvs[j].y);
            }

            newMesh.uv = uvs;

            // Save the mesh to the hard drive
            string meshPath = saveFolder + $"/RemappedMesh_{allFilters[i].gameObject.name}_{i}.asset";
            AssetDatabase.CreateAsset(newMesh, meshPath);

            // Assign the saved permanent assets back to the objects
            allFilters[i].sharedMesh = newMesh;
            allRenderers[i].sharedMaterial = combinedMaterial;
        }

        // Force Unity to save the files to disk and refresh the project window
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Success! Saved {uniqueTextures.Count} textures, the material, and {allFilters.Count} meshes into '{saveFolder}'");
#endif
    }
}