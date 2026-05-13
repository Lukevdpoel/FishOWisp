using UnityEngine;

[ExecuteAlways]
public class CookieGenerator : MonoBehaviour
{
    [Header("Assignments")]
    public Light targetLight;           // Assign Directional Light
    public RenderTexture cookieRT;      // Assign DynamicCookie_RT
    public Material generatorMaterial;  // Assign Mat_CookieGen
    public Texture cloudNoiseTexture;   // Assign your noise image here!

    [Header("Animation")]
    public float scrollSpeed = 0.1f;
    public float erosionSpeed = 0.5f;
    [Range(0f, 1f)] public float erosionAmount = 0.2f;

    // Property IDs
    private int offsetID;
    private int cutoffID;

    void OnEnable()
    {
        // CRITICAL: We are back to using "_Offset" and "_Cutoff"
        // The texture is handled automatically by Blit into "_MainTex"
        offsetID = Shader.PropertyToID("_Offset");
        cutoffID = Shader.PropertyToID("_Cutoff");

        if (targetLight != null && cookieRT != null)
        {
            targetLight.cookie = cookieRT;
        }
    }

    void Update()
    {
        if (cookieRT == null || generatorMaterial == null || cloudNoiseTexture == null) return;
#if UNITY_EDITOR
        if (UnityEditor.EditorApplication.isPaused) return;
#endif

        // 1. Force Cookie Assignment
        if (!Application.isPlaying && targetLight != null && targetLight.cookie != cookieRT)
        {
            targetLight.cookie = cookieRT;
        }

        // 2. Calculate Values
        float scroll = Time.time * scrollSpeed;
        float erosion = 0.5f + Mathf.Sin(Time.time * erosionSpeed) * erosionAmount;

        // 3. Update Material Properties
        // We set the Offset and Cutoff manually
        generatorMaterial.SetVector(offsetID, new Vector2(scroll, scroll * 0.5f));
        generatorMaterial.SetFloat(cutoffID, erosion);

        // 4. The Magic Fix
        // By passing 'cloudNoiseTexture' here, Unity forces it into the '_MainTex' slot
        // of your shader, overwriting any nulls.
        Graphics.Blit(cloudNoiseTexture, cookieRT, generatorMaterial);
    }
}