using UnityEngine;

/// <summary>
/// Sets the player's world position as a global shader property so all foliage
/// shaders can react to it without per-material updates.
/// Attach this to the Player GameObject.
/// </summary>
public class FoliageInteractionSetter : MonoBehaviour
{
    [Tooltip("Radius of the interaction effect. Match to your sphere collider radius.")]
    public float interactionRadius = 1.5f;

    [Tooltip("How strongly foliage bends away.")]
    public float bendStrength = 0.3f;

    [Tooltip("How much foliage flattens downward when the player is near.")]
    public float flattenStrength = 0.15f;

    private static readonly int PlayerPosID = Shader.PropertyToID("_PlayerPosition");
    private static readonly int InteractionRadiusID = Shader.PropertyToID("_PlayerInteractionRadius");
    private static readonly int BendStrengthID = Shader.PropertyToID("_PlayerBendStrength");
    private static readonly int FlattenStrengthID = Shader.PropertyToID("_FlattenStrength");

    void Update()
    {
        Shader.SetGlobalVector(PlayerPosID, transform.position);
        Shader.SetGlobalFloat(InteractionRadiusID, interactionRadius);
        Shader.SetGlobalFloat(BendStrengthID, bendStrength);
        Shader.SetGlobalFloat(FlattenStrengthID, flattenStrength);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
        Gizmos.DrawSphere(transform.position, interactionRadius);
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}
