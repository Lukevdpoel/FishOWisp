using UnityEngine;

public class BobberBuoyancy : MonoBehaviour
{
    public float floatHeight = 0.5f;     // How high above the water the bobber should float
    public float bounceDamp = 0.05f;     // Damping to prevent jitter
    public float waterDrag = 1f;
    public float buoyancyForce = 10f;

    private Rigidbody rb;
    private bool inWater = false;
    private float waterSurfaceY;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (inWater)
        {
            float targetY = waterSurfaceY - floatHeight;
            float depth = targetY - transform.position.y;


            if (depth > 0)
            {
                Vector3 force = Vector3.up * (depth * buoyancyForce - rb.linearVelocity.y * bounceDamp);
                rb.AddForce(force, ForceMode.Acceleration);
            }

            rb.linearDamping = waterDrag;

            // Keep bobber upright
            Quaternion upright = Quaternion.Euler(0f, transform.rotation.eulerAngles.y, 0f);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, upright, Time.fixedDeltaTime * 2f));
        }
        else
        {
            rb.linearDamping = 0f;
        }
    }

    // Called when entering the water trigger
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Water"))
        {
            inWater = true;
            waterSurfaceY = other.bounds.max.y; // Assume top of collider is water surface
        }
    }

    // Called when staying in the water trigger
    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Water"))
        {
            inWater = true;
            waterSurfaceY = other.bounds.max.y;
        }
    }

    // Called when exiting the water trigger
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Water"))
        {
            inWater = false;
        }
    }
}
