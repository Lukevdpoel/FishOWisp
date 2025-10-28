using UnityEngine;

// These ensure the Rigidbody and Collider are present,
// just like PlayerInventory.cs adds them.
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class PhysicalFish : MonoBehaviour
{
    // This holds the data (like name and value) for this 3D model
    public CaughtFish CurrentFish { get; private set; }

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    // Called by PlayerInventory.cs when spawned
    public void Initialize(CaughtFish fish)
    {
        CurrentFish = fish;
    }

    // Called by BucketSwarm.cs when added to the swarm
    public void EnableSwarmMode()
    {
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    // Called by our new FishDragger script
    public void EnableDragMode()
    {
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    // Called when the fish is dropped somewhere *other* than the sell zone
    public void EnablePhysicsMode()
    {
        rb.isKinematic = false;
        rb.useGravity = true;
    }
}