using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class InventoryFloatingItem : MonoBehaviour
{
    private Rigidbody2D rb;

    [Header("Float Settings")]
    public float floatStrength = 50f;
    public float maxSpeed = 100f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        rb.gravityScale = 0f;
        rb.linearDamping = 1f;
        rb.angularDamping = 1f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        // Ensure the body is simulated
        rb.simulated = true;
    }

    public void Init(Vector2 initialForce)
    {
        if (rb != null)
        {
            // Reset velocity just in case
            rb.linearVelocity = Vector2.zero;
            rb.AddForce(initialForce, ForceMode2D.Impulse);

            if ((rb.constraints & RigidbodyConstraints2D.FreezeRotation) == 0)
            {
                rb.AddTorque(Random.Range(-10f, 10f));
            }
        }
    }

    // Reverting to FixedUpdate since time is no longer paused.
    // This provides smoother and more consistent physics behavior.
    void FixedUpdate()
    {
        if (rb == null) return;

        // Apply tiny random forces to keep it alive
        Vector2 randomDir = Random.insideUnitCircle.normalized;

        // Standard physics force application
        rb.AddForce(randomDir * floatStrength);

        // Clamp speed
        if (rb.linearVelocity.magnitude > maxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
        }
    }
}