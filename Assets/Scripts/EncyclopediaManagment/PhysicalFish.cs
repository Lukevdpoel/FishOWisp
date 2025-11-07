using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class PhysicalFish : MonoBehaviour
{
    public CaughtFish CurrentFish { get; private set; }

    // This variable will be assigned *the first time* it's needed
    private Rigidbody rb;

    // --- NEW HELPER FUNCTION ---
    // This will get the Rigidbody and store it.
    // If it's already stored, it just returns it.
    private Rigidbody GetRb()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }
        return rb;
    }

    public void Initialize(CaughtFish fish)
    {
        CurrentFish = fish;
    }

    public void EnableSwarmMode()
    {
        // Use the helper to safely get the Rigidbody
        Rigidbody myRb = GetRb();
        myRb.isKinematic = true;
        myRb.useGravity = false;

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = true;
        }
    }

    public void EnableDragMode()
    {
        // Use the helper here too
        Rigidbody myRb = GetRb();
        myRb.isKinematic = true;
        myRb.useGravity = false;
    }

    public void EnablePhysicsMode()
    {
        // And here
        Rigidbody myRb = GetRb();
        myRb.isKinematic = false;
        myRb.useGravity = true;
    }
}