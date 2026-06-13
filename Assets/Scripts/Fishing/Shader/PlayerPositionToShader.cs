using UnityEngine;

public class PlayerPositionToShader : MonoBehaviour
{
    [Tooltip("The player or object that will push the vines.")]
    public Transform playerTransform;

    // The name of the property in your shader
    private static readonly int PlayerPosID = Shader.PropertyToID("_PlayerPosition");

    private Material vineMaterial;

    private const float MoveEpsilonSqr = 0.000001f;
    private Vector3 lastWrittenPos;
    private bool hasWritten;

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
        if (vineMaterial == null || playerTransform == null) return;

        Vector3 pos = playerTransform.position;
        if (hasWritten && (pos - lastWrittenPos).sqrMagnitude < MoveEpsilonSqr) return;

        vineMaterial.SetVector(PlayerPosID, pos);
        lastWrittenPos = pos;
        hasWritten = true;
    }
}