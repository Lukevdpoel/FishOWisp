using UnityEngine;

public class FishingController : MonoBehaviour
{
    [Header("Bobber Settings")]
    public GameObject bobberPrefab;
    public Transform castPoint;           // Where the bobber spawns (e.g., hand or rod tip)
    public float throwForce = 5f;         // Controls how far it goes forward
    public float downwardForce = 5f;      // Makes it fall faster
    public float drag = 1f;               // Optional: slows horizontal motion

    [Header("Animation")]
    public Animator animator;

    void Start()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    // Call this to start casting (e.g., input handler)
    public void StartCasting()
    {
        animator.SetTrigger("CastTrigger");
    }

    // This function is triggered by an Animation Event at the right moment
    public void ThrowBobber()
    {
        if (bobberPrefab == null || castPoint == null)
        {
            Debug.LogWarning("Missing bobber prefab or cast point.");
            return;
        }

        GameObject bobber = Instantiate(bobberPrefab, castPoint.position, Quaternion.identity);

        Rigidbody rb = bobber.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Optional: apply drag to slow horizontal movement
            rb.linearDamping = drag;

            // Apply forward and downward forces separately
            rb.AddForce(castPoint.forward * throwForce, ForceMode.VelocityChange);     // Forward motion
            rb.AddForce(Vector3.down * downwardForce, ForceMode.Acceleration);         // Extra gravity
        }
    }


public void Update()
    {
        if ( Input.GetMouseButtonDown(0))
        {
            StartCasting();
            
        }
        {
            
        }
    }
}