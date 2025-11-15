using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class PhysicalFish : MonoBehaviour
{
    public CaughtFish CurrentFish { get; private set; }
    private Rigidbody rb;

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

    // --- NEW METHOD ---
    // This is the state for when the InventoryBoid script is in control
    public void EnableFlockMode()
    {
        Rigidbody myRb = GetRb();
        myRb.isKinematic = true;
        myRb.useGravity = false;

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = true; // Keep collider on for raycasting
        }
    }

    public void EnableDragMode()
    {
        Rigidbody myRb = GetRb();
        myRb.isKinematic = true;
        myRb.useGravity = false;
    }

    public void EnablePhysicsMode()
    {
        // This probably won't be used, but good to keep
        Rigidbody myRb = GetRb();
        myRb.isKinematic = false;
        myRb.useGravity = true;
    }
}