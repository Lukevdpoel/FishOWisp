using UnityEngine;
using UnityEngine.Rendering;

public class ModelViewer : GenericSingleton<ModelViewer>
{
    [Header("Setup")]
    public Transform modelContainer;
    public Transform camera;

    [Header("Uncaught Display")]
    [Tooltip("Unlit black silhouette for fish the player hasn't caught yet — the same material " +
             "the underwater swimmers wear (M_FishSilhouette_Sway), so the display sway keeps " +
             "driving its sine. Leave empty to fall back to a plain unlit black (the sway then " +
             "deforms the mesh on the CPU instead).")]
    public Material uncaughtSilhouetteMaterial;

    private GameObject currentModel;
    private static Material fallbackSilhouette;

    private void Start()
    {
        // Ensure camera is disabled on start to save performance
        if (camera != null)
        {
            camera.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Swap the model being displayed by instantiating the provided prefab.
    /// Pass null to clear the model and disable the camera.
    /// Pass revealed = false for a fish the player hasn't caught: the model renders as the
    /// same unlit black silhouette the underwater swimmers use, still swaying.
    /// </summary>
    public void ShowModel(FishPreset prefab, bool revealed = true)
    {
        // Destroy previous model if it exists
        if (currentModel != null)
        {
            Destroy(currentModel);
        }

        // If no prefab is provided, disable the camera and return
        if (prefab == null)
        {
            if (camera != null) camera.gameObject.SetActive(false);
            return;
        }

        // We have a fish to show, so enable the camera
        if (camera != null)
        {
            camera.gameObject.SetActive(true);
            // Update camera position based on the fish's specific view distance
            camera.localPosition = new Vector3(0, 0, -SizeClassHelper.GetCameraViewDistance(prefab.sizeClass));
        }

        // Instantiate new model as child of container
        if (prefab.fishPrefab != null)
        {
            currentModel = Instantiate(prefab.fishPrefab, modelContainer);
            currentModel.transform.localPosition = Vector3.zero;
            currentModel.transform.localRotation = Quaternion.identity;
            currentModel.transform.localScale = Vector3.one;

            // Set layer for the specific camera rendering (Layer 22 as per your previous code)
            MeshRenderer[] renderers = currentModel.GetComponentsInChildren<MeshRenderer>();
            foreach (MeshRenderer renderer in renderers)
            {
                renderer.gameObject.layer = 22;
            }

            // Mystery entry: black out the whole model before FishDisplaySway is added, so
            // the sway picks up (and instances) the silhouette material for its sine clock.
            if (!revealed) ApplySilhouette(renderers);

            // The notebook holds Time.timeScale at 0 while this viewer is visible — any
            // Animator the fish prefab carries must tick on unscaled time to keep moving.
            foreach (Animator anim in currentModel.GetComponentsInChildren<Animator>(true))
            {
                anim.updateMode = AnimatorUpdateMode.UnscaledTime;
            }

            // Display idle: the same procedural swim the fish has in the water, scaled to
            // calm museum-pose values from the species' own swim settings — eels undulate,
            // pikes barely flex. Runs on unscaled time so it keeps swaying while the game
            // is paused. Harmless no-op until the fish shader carries the FishSway function.
            FishDisplaySway sway = currentModel.AddComponent<FishDisplaySway>();
            sway.frequency = prefab.swimFrequency * 0.6f;
            sway.bodyWaves = prefab.swimBodyWaves;
            sway.waveAmplitude = prefab.swimWaveAmplitude * 0.5f;
            sway.maskStart = prefab.swimMaskStart;
        }
    }

    /// <summary>
    /// Helper method to clear the viewer and turn off the camera.
    /// </summary>
    public void HideViewer()
    {
        ShowModel(null);
    }

    // Same treatment as the underwater swimmers (FishModelVisual.ApplySilhouette): every
    // material slot goes flat black and the renderers opt out of anything lighting could
    // use to shade the shape.
    private void ApplySilhouette(MeshRenderer[] renderers)
    {
        Material mat = uncaughtSilhouetteMaterial != null ? uncaughtSilhouetteMaterial : GetFallbackSilhouette();
        foreach (MeshRenderer renderer in renderers)
        {
            Material[] mats = new Material[renderer.sharedMaterials.Length];
            for (int m = 0; m < mats.Length; m++) mats[m] = mat;
            renderer.sharedMaterials = mats;

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    private static Material GetFallbackSilhouette()
    {
        if (fallbackSilhouette == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            fallbackSilhouette = new Material(shader) { name = "FishSilhouette (viewer runtime)" };
            if (fallbackSilhouette.HasProperty("_BaseColor"))
                fallbackSilhouette.SetColor("_BaseColor", Color.black);
            if (fallbackSilhouette.HasProperty("_Color"))
                fallbackSilhouette.SetColor("_Color", Color.black);
        }
        return fallbackSilhouette;
    }
}