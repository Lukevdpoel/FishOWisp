using UnityEngine;

/// <summary>
/// A simple utility script to connect a VerletRope between two specified points.
/// This is useful for creating decorative or static ropes in the scene
/// without needing the ObjectThrower script.
/// </summary>
[RequireComponent(typeof(VerletRope))]
public class RopeConnector : MonoBehaviour
{
    [Header("Rope Connection Points")]
    [Tooltip("The Transform that the start of the rope will be attached to.")]
    public Transform ropeStartPoint;

    [Tooltip("The Transform that the end of the rope will be attached to.")]
    public Transform ropeEndPoint;

    // A private reference to the rope script on this same GameObject.
    private VerletRope verletRope;

    /// <summary>
    /// Called when the script instance is being loaded.
    /// We use Awake to ensure the rope component is found before Start is called.
    /// </summary>
    void Awake()
    {
        // Get the VerletRope component attached to this GameObject.
        verletRope = GetComponent<VerletRope>();
    }

    /// <summary>
    /// Called on the frame when a script is enabled before any of the Update methods are called the first time.
    /// </summary>
    void Start()
    {
        // Check if both connection points and the rope script have been assigned in the Inspector.
        if (ropeStartPoint == null || ropeEndPoint == null)
        {
            Debug.LogError("Rope Connector Error: One or both of the connection points are not assigned. Please assign them in the Inspector.", this.gameObject);
            return; // Stop the script from running further to prevent errors.
        }

        if (verletRope == null)
        {
            // This check is technically redundant because of [RequireComponent],
            // but it's good practice for clarity and preventing potential issues.
            Debug.LogError("Rope Connector Error: Could not find the VerletRope script on this GameObject.", this.gameObject);
            return;
        }

        // If everything is set up correctly, initialize the rope to connect the two points.
        verletRope.SetupRope(ropeStartPoint, ropeEndPoint);
    }
}
