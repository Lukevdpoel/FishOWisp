using UnityEngine;

public class SaveRenderTexture : MonoBehaviour
{
    public RenderTexture TextureToSave;
    public string Path = "Assets/ART/Textures/";

    void LateUpdate()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            SaveImage();
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void SaveImage()
    {
        Debug.Log("Saved image with path: " + Path);
        SaveTextureToFileUtility.SaveRenderTextureToFile(TextureToSave, Path);
    }
}
