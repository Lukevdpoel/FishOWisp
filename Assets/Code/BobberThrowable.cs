using UnityEngine;

public class BobberThrowable : MonoBehaviour
{
    public LayerMask waterLayer;                   // Set this to only the Water layer
    public Animator animator;                      // Animator on the bobber prefab
    public string waterAnimationState = "BobInWater"; // Name of animation or trigger
    public float sinkOffset = 0.1f;                // How much to lower into water

    public ObjectThrower thrower;                  // Assigned on spawn to communicate back

    private Rigidbody rb;
    private bool isInWater = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (isInWater) return;

        if (((1 << collision.gameObject.layer) & waterLayer) != 0)
        {
            isInWater = true;

            // Stop physics
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            // Snap slightly below the water surface
            Vector3 contactPoint = collision.GetContact(0).point;
            transform.position = new Vector3(
                transform.position.x,
                contactPoint.y - sinkOffset,
                transform.position.z
            );

            // Reset rotation to upright
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            // Play animation
            if (animator != null)
            {
                animator.Play(waterAnimationState);
            }

            // 🔁 Step 5: Notify the thrower
            if (thrower != null)
            {
                thrower.NotifyBobberInWater();
            }
        }
    }
}
