using UnityEngine;

public class SaveRenderTexture : MonoBehaviour
{
    public RenderTexture TextureToSave;

    // Change this from just a folder to a full file path (without the extension)
    public string SavePath = "Assets/ART/Textures/MySavedImage"; // Example path

    void LateUpdate()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            SaveImage();
        }
    }

    void SaveImage()
    {
        // Use the new SavePath variable here
        Debug.Log("Saved image with path: " + SavePath);
        SaveTextureToFileUtility.SaveRenderTextureToFile(TextureToSave, SavePath);
    }
}