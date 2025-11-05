using UnityEngine;

public class PlayerPositionToShader : MonoBehaviour
{
    [Tooltip("The player or object that will push the vines.")]
    public Transform playerTransform;

    // The name of the property in your shader
    private static readonly int PlayerPosID = Shader.PropertyToID("_PlayerPosition");

    private Material vineMaterial;

    void Start()
    {
        // Get the material from the Renderer on this GameObject
        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            // Use .material to get a unique instance for this object
            vineMaterial = rend.material;
        }
    }

    void Update()
    {
        if (vineMaterial != null && playerTransform != null)
        {
            // Send the player's world position to the shader
            vineMaterial.SetVector(PlayerPosID, playerTransform.position);
        }
    }
}