using UnityEngine;
using System.Collections; // Required for Coroutines

public class Bobber : MonoBehaviour
{
    [Header("Effects")]
    public ParticleSystem waterSplashEffect; // Assign your particle effect prefab here
    public int splashCount = 3; // How many times the splash effect will play
    public float splashInterval = 0.15f; // The delay between each splash

    [Header("Follow Effect")]
    public ParticleSystem followEffect; // Assign the particle effect that will follow the bobber
    public float followDuration = 2.0f; // How long the effect will follow the bobber

    [Header("Water Detection")]
    public LayerMask waterLayer;
    public string waterTag = "Water";

    [Header("Buoyancy Settings")]
    public float floatHeight = 0.5f;
    public float bounceDamp = 0.05f;
    public float waterDrag = 1f;
    public float buoyancyForce = 10f;

    [HideInInspector]
    public ObjectThrower thrower;

    private Rigidbody rb;
    private bool isInWater = false;
    private float waterSurfaceY;
    private bool hasNotifiedThrower = false;

    private bool hasSplashed = false;
    private float initialSplashHeight; // Stores the height of the first splash

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        transform.rotation = Random.rotation;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (isInWater) return;

        // Check if the collision is with the water layer
        if (((1 << collision.gameObject.layer) & waterLayer) != 0)
        {
            HandleWaterEntry(collision.GetContact(0).point.y);

            // Additional logic for collision-based water entry
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;

            Vector3 contactPoint = collision.GetContact(0).point;
            transform.position = new Vector3(
                transform.position.x,
                contactPoint.y,
                transform.position.z
            );

            transform.rotation = Quaternion.Euler(0f, transform.rotation.eulerAngles.y, 0f);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(waterTag))
        {
            HandleWaterEntry(other.bounds.max.y);
            rb.isKinematic = false; // Ensure rigidbody is not kinematic for buoyancy
        }
    }

    /// <summary>
    /// A centralized method to handle the logic when the bobber enters the water.
    /// </summary>
    /// <param name="surfaceY">The Y coordinate of the water surface.</param>
    void HandleWaterEntry(float surfaceY)
    {
        if (isInWater) return; // Prevent this from running multiple times

        isInWater = true;
        waterSurfaceY = surfaceY;

        PlaySplashEffect();
        PlayFollowEffect(); // Play the new follow effect

        NotifyThrower();
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

    void PlaySplashEffect()
    {
        if (hasSplashed || waterSplashEffect == null) return;

        // Store the height of the very first impact
        initialSplashHeight = transform.position.y;
        StartCoroutine(SplashRoutine());
        hasSplashed = true;
    }

    /// <summary>
    /// Spawns the splash effect multiple times, maintaining the initial impact height.
    /// </summary>
    private IEnumerator SplashRoutine()
    {
        for (int i = 0; i < splashCount; i++)
        {
            // Create a new position using the bobber's current X/Z and the stored initial Y
            Vector3 splashPos = new Vector3(transform.position.x, initialSplashHeight, transform.position.z);
            Instantiate(waterSplashEffect, splashPos, Quaternion.identity);
            yield return new WaitForSeconds(splashInterval);
        }
    }

    /// <summary>
    /// Instantiates the follow effect and starts the coroutine to manage its lifetime.
    /// </summary>
    void PlayFollowEffect()
    {
        if (followEffect == null) return;

        // Instantiate the effect at the initial splash height, but do NOT parent it.
        Vector3 effectPos = new Vector3(transform.position.x, initialSplashHeight, transform.position.z);
        ParticleSystem followInstance = Instantiate(followEffect, effectPos, Quaternion.identity);
        StartCoroutine(FollowRoutine(followInstance));
    }

    /// <summary>
    /// Makes the particle effect follow the bobber's X/Z position at a fixed height for a set duration.
    /// </summary>
    private IEnumerator FollowRoutine(ParticleSystem effectInstance)
    {
        float startTime = Time.time;
        float initialY = effectInstance.transform.position.y; // The fixed height

        // Follow the bobber for the specified duration
        while (Time.time < startTime + followDuration)
        {
            // Update the effect's position to follow the bobber on the X and Z axes, but keep the initial Y
            effectInstance.transform.position = new Vector3(transform.position.x, initialY, transform.position.z);
            yield return null; // Wait for the next frame
        }

        // Stop the particle emission
        effectInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        // Wait for the remaining particles to die out
        yield return new WaitWhile(() => effectInstance.IsAlive(true));

        // Destroy the particle system game object
        Destroy(effectInstance.gameObject);
    }


    void NotifyThrower()
    {
        if (hasNotifiedThrower || thrower == null) return;

        Debug.Log("🌊 Bobber notifying ObjectThrower: landed in water.");
        thrower.NotifyBobberInWater();
        hasNotifiedThrower = true;
    }
}
