using UnityEngine;

public class DestroyOnLayerHit : MonoBehaviour
{
    [Tooltip("Layer that triggers destruction")]
    public string targetLayerName = "Ground";  // Layer name to detect

    [Tooltip("Particle effect prefab to spawn on hit")]
    public GameObject hitEffectPrefab;        // Particle effect prefab

    [Tooltip("Second prefab to spawn on hit")]
    public GameObject secondPrefab;           // Another prefab to spawn

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer(targetLayerName))
        {
            ContactPoint contact = collision.contacts[0];

            // Spawn particle effect (let it clean itself up or destroy after 0.5s)
            if (hitEffectPrefab != null)
            {
                GameObject effect = Instantiate(hitEffectPrefab, contact.point, Quaternion.identity);
                // If your particle system doesn't auto destroy, uncomment next line:
                // Destroy(effect, 0.5f);
            }

            // Spawn second prefab and destroy it after 1 second
            if (secondPrefab != null)
            {
                GameObject second = Instantiate(secondPrefab, contact.point, Quaternion.identity);
                Destroy(second, 0.3f); // Destroy after 1 second
            }

            // Destroy the thrown object
            Destroy(gameObject);
        }
    }
}
