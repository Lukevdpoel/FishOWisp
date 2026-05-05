using UnityEngine;

public class ModelViewer : GenericSingleton<ModelViewer>
{
    [Header("Setup")]
    public Transform modelContainer;
    public Transform camera;

    private GameObject currentModel;

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
    /// </summary>
    public void ShowModel(FishPreset prefab)
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
        }
    }

    /// <summary>
    /// Helper method to clear the viewer and turn off the camera.
    /// </summary>
    public void HideViewer()
    {
        ShowModel(null);
    }
}