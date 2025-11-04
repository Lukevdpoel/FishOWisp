using UnityEngine;

public class PlayerPositionSender : MonoBehaviour
{
    // Assign your player's Transform in the Inspector
    public Transform playerTransform;

    // These values will also be sent to the shader
    // You can also set them in the Material, but this is easier to control
    [SerializeField] private float flattenRadius = 2.0f;
    [SerializeField] private float flattenStrength = 1.0f;

    // This is the "Reference" name we will use in Shader Graph
    private static readonly int PlayerPosID = Shader.PropertyToID("_PlayerPosition");
    private static readonly int FlattenRadiusID = Shader.PropertyToID("_FlattenRadius");
    private static readonly int FlattenStrengthID = Shader.PropertyToID("_FlattenStrength");

    void Update()
    {
        if (playerTransform != null)
        {
            // Send the player's world position to the shader
            Shader.SetGlobalVector(PlayerPosID, playerTransform.position);

            // Send the settings
            Shader.SetGlobalFloat(FlattenRadiusID, flattenRadius);
            Shader.SetGlobalFloat(FlattenStrengthID, flattenStrength);
        }
    }
}