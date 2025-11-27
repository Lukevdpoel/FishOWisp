using UnityEngine;

public class FishCollisionHandler : MonoBehaviour
{
    // Layer mask to define what destroys the fish (e.g., Water)
    public LayerMask destroyLayerMask;

    private void OnTriggerEnter(Collider other)
    {
        // Check if the object we hit is in the layer mask
        if (((1 << other.gameObject.layer) & destroyLayerMask) != 0)
        {
            Debug.Log($"Fish hit {other.gameObject.name} (Layer: {LayerMask.LayerToName(other.gameObject.layer)}) and was returned to the depths.");
            Destroy(gameObject);
        }
    }

    // Fallback: Also check OnCollisionEnter if the water has a non-trigger collider
    private void OnCollisionEnter(Collision collision)
    {
        if (((1 << collision.gameObject.layer) & destroyLayerMask) != 0)
        {
            Debug.Log($"Fish hit {collision.gameObject.name} and was returned to the depths.");
            Destroy(gameObject);
        }
    }
}