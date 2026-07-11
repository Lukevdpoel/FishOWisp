using UnityEngine;

public class SaveRenderTexture : MonoBehaviour
{
    public RenderTexture TextureToSave;


    public string SavePath = "Assets/ART/Textures/MySavedImage";

    void LateUpdate()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            SaveImage();
        }
    }

    void SaveImage()
    {

        Debug.Log("Saved image with path: " + SavePath);
        SaveTextureToFileUtility.SaveRenderTextureToFile(TextureToSave, SavePath);
    }
}