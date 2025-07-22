using UnityEngine;

public class Bobber : MonoBehaviour
{
    [Header("Water Detection")]
    public LayerMask waterLayer;
    public string waterTag = "Water";

    [Header("Buoyancy Settings")]
    public float floatHeight = 0.5f;
    public float bounceDamp = 0.05f;
    public float waterDrag = 1f;
    public float buoyancyForce = 10f;

    private Rigidbody rb;
    private bool isInWater = false;
    private float waterSurfaceY;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (isInWater) return;

        if (((1 << collision.gameObject.layer) & waterLayer) != 0)
        {
            isInWater = true;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;

            Vector3 contactPoint = collision.GetContact(0).point;
            transform.position = new Vector3(
                transform.position.x,
                contactPoint.y,
                transform.position.z
            );

            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(waterTag))
        {
            isInWater = true;
            waterSurfaceY = other.bounds.max.y;
            rb.isKinematic = false; // Enable buoyancy
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag(waterTag))
        {
            waterSurfaceY = other.bounds.max.y;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(waterTag))
        {
            isInWater = false;
        }
    }

    void FixedUpdate()
    {
        if (!isInWater) return;

        float targetY = waterSurfaceY - floatHeight;
        float depth = targetY - transform.position.y;

        if (depth > 0)
        {
            Vector3 force = Vector3.up * (depth * buoyancyForce - rb.linearVelocity.y * bounceDamp);
            rb.AddForce(force, ForceMode.Acceleration);
        }

        rb.linearDamping = waterDrag;

        Quaternion upright = Quaternion.Euler(0f, transform.rotation.eulerAngles.y, 0f);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, upright, Time.fixedDeltaTime * 2f));
    }
}
