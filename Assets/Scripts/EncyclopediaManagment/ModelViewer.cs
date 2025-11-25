using UnityEngine;

public class ModelViewer : GenericSingleton<ModelViewer>
{

    [Header("Setup")]
    public Transform modelContainer;
    public Transform camera;

    private GameObject currentModel;


    /// <summary>
    /// Swap the model being displayed by instantiating the provided prefab.
    /// </summary>
    public void ShowModel(FishPreset prefab)
    {
        // Destroy previous model if it exists
        if (currentModel != null)
        {
            Destroy(currentModel);
        }

        if (prefab == null)
        {
            return;
        }
            Debug.Log(prefab.ToString());
        Debug.Log(camera.ToString());
        camera.transform.localPosition = new Vector3(0, 0, -prefab.cameraviewdistance);

        // Instantiate new model as child of container
        // Corrected 'fishprefab' to 'fishPrefab' to match the FishPreset script.
        currentModel = Instantiate(prefab.fishPrefab, modelContainer);
        currentModel.transform.localPosition = Vector3.zero;
        currentModel.transform.localRotation = Quaternion.identity;
        currentModel.transform.localScale = Vector3.one;
        MeshRenderer[] renderers = currentModel.GetComponentsInChildren<MeshRenderer>();

        foreach (MeshRenderer renderer in renderers)
        {
            renderer.gameObject.layer = 22;
        }

    }
}
