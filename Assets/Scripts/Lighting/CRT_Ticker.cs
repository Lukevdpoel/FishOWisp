using UnityEngine;

[ExecuteAlways] // Runs in Edit Mode and Play Mode
public class CRT_Ticker : MonoBehaviour
{
    public CustomRenderTexture customTexture;
    public Material updateMaterial; // Optional: Only if you need to pass Time manually

    void Update()
    {
        if (customTexture == null) return;

        // 1. Force the texture to initialize if it hasn't (fixes black screen on start)
        if (!customTexture.IsCreated())
        {
            customTexture.Initialize();
        }

        // 2. The "Kick" - Force an update request every frame
        // This overrides Unity's decision to "sleep" the texture.
        customTexture.Update();

        // 3. (Optional) Force Time Update
        // Sometimes ShaderGraph "Time" node gets stuck in CRTs. 
        // If it's still not moving, uncomment the line below and assign the material.
        // updateMaterial.SetFloat("_TimeVar", Time.time); 
    }
}