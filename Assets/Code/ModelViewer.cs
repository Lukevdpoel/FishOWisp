using UnityEngine;

public class ModelViewer : MonoBehaviour
{
    public static ModelViewer Instance { get; private set; }

    [Header("Setup")]
    public Transform modelContainer;
    public Transform camera;

    private GameObject currentModel;

    void Awake()
    {
        // Singleton enforcement
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

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

        camera.transform.localPosition = new Vector3(0, 0, -prefab.cameraviewdistance);

        // Instantiate new model as child of container
        currentModel = Instantiate(prefab.fishprefab, modelContainer);
        currentModel.transform.localPosition = Vector3.zero;
        currentModel.transform.localRotation = Quaternion.identity;
        currentModel.transform.localScale = Vector3.one;
    }
}