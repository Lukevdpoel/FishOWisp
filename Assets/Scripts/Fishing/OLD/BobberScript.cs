using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
public class Bobber : MonoBehaviour
{
    [Header("Visuals")]
    [Tooltip("Assign the child GameObject that contains the bobber's model/mesh.")]
    public GameObject bobberVisuals; // The visual part of the bobber to hide

    // --- Existing Bobber Fields ---
    [Header("Effects")]
    public ParticleSystem waterSplashEffect;
    public int splashCount = 3;
    public float splashInterval = 0.15f;
    public ParticleSystem followEffect;
    public float followDuration = 2.0f;

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
    private float initialSplashHeight;

    [Header("Fishing State")]
    public GameObject hookedFish { get; private set; }


    void Start()
    {
        rb = GetComponent<Rigidbody>();
        transform.rotation = Random.rotation;
    }

    /// <summary>
    /// This method is called by the FishPoolArea when a fish bites.
    /// </summary>
    public void HookFish(GameObject fishInstance)
    {
        if (hookedFish != null)
        {
            Debug.LogWarning("Bobber already has a fish hooked! Ignoring new one.");
            Destroy(fishInstance);
            return;
        }

        hookedFish = fishInstance;
        hookedFish.SetActive(false);
        Debug.Log($"Bobber has hooked a {hookedFish.name}! Waiting for reel.");

        // --- CORRECTED: This now finds the ObjectThrower script ---
        ObjectThrower objectThrower = FindObjectOfType<ObjectThrower>();
        if (objectThrower != null)
        {
            objectThrower.SignalFishBite(this);
        }
        else
        {
            Debug.LogError("ObjectThrower not found in scene! The fish will never appear.");
        }
    }

    /// <summary>
    /// Call this method from your main fishing script when the player successfully
    /// reacts to a bite to begin reeling. This will show the fish.
    /// </summary>
    public void StartReeling()
    {
        Debug.Log("<b>Step 3:</b> StartReeling() method was called on the Bobber.", this.gameObject);

        if (hookedFish == null)
        {
            Debug.LogError("Attempted to start reeling, but hookedFish is null! This shouldn't happen.", this.gameObject);
            return;
        }

        Debug.Log("Reeling started! Swapping bobber with fish model.");

        // Now, make the fish visible and attach it to the bobber's position.
        hookedFish.SetActive(true);
        Debug.Log($"<b>Step 4:</b> Fish '{hookedFish.name}' has been set to active.", hookedFish);

        // --- Diagnostic Check ---
        Renderer fishRenderer = hookedFish.GetComponentInChildren<Renderer>();
        if (fishRenderer == null)
        {
            Debug.LogWarning($"The fish prefab '{hookedFish.name}' does not have a Renderer component in its children. It will be invisible.", hookedFish);
        }
        else if (!fishRenderer.enabled)
        {
            Debug.LogWarning($"The fish model '{hookedFish.name}' was activated, but its Renderer component is disabled. You may need to enable it on the prefab.", hookedFish);
        }

        hookedFish.transform.SetParent(this.transform);
        hookedFish.transform.localPosition = Vector3.zero;
        hookedFish.transform.localRotation = Quaternion.identity;

        if (bobberVisuals != null)
        {
            bobberVisuals.SetActive(false);
        }
    }


    /// <summary>
    /// Call this when the fish is caught or gets away to clean up.
    /// </summary>
    public void ClearHookedFish()
    {
        if (hookedFish != null)
        {
            Destroy(hookedFish);
            hookedFish = null;
            Debug.Log("Hooked fish has been cleared.");

            if (bobberVisuals != null)
            {
                bobberVisuals.SetActive(true);
            }
        }
    }

    // --- All existing physics and effects methods remain below ---

    void OnCollisionEnter(Collision collision)
    {
        if (isInWater) return;

        if (((1 << collision.gameObject.layer) & waterLayer) != 0)
        {
            HandleWaterEntry(collision.GetContact(0).point.y);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;

            Vector3 contactPoint = collision.GetContact(0).point;
            transform.position = new Vector3(transform.position.x, contactPoint.y, transform.position.z);
            transform.rotation = Quaternion.Euler(0f, transform.rotation.eulerAngles.y, 0f);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(waterTag))
        {
            HandleWaterEntry(other.bounds.max.y);
            rb.isKinematic = false;
        }
    }

    void HandleWaterEntry(float surfaceY)
    {
        if (isInWater) return;

        isInWater = true;
        waterSurfaceY = surfaceY;

        PlaySplashEffect();
        PlayFollowEffect();
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
        initialSplashHeight = transform.position.y;
        StartCoroutine(SplashRoutine());
        hasSplashed = true;
    }

    private IEnumerator SplashRoutine()
    {
        for (int i = 0; i < splashCount; i++)
        {
            Vector3 splashPos = new Vector3(transform.position.x, initialSplashHeight, transform.position.z);
            Instantiate(waterSplashEffect, splashPos, Quaternion.identity);
            yield return new WaitForSeconds(splashInterval);
        }
    }

    void PlayFollowEffect()
    {
        if (followEffect == null) return;
        Vector3 effectPos = new Vector3(transform.position.x, initialSplashHeight, transform.position.z);
        ParticleSystem followInstance = Instantiate(followEffect, effectPos, Quaternion.identity);
        StartCoroutine(FollowRoutine(followInstance));
    }



    private IEnumerator FollowRoutine(ParticleSystem effectInstance)
    {
        float startTime = Time.time;
        float initialY = effectInstance.transform.position.y;

        while (Time.time < startTime + followDuration)
        {
            if (effectInstance == null) yield break;
            effectInstance.transform.position = new Vector3(transform.position.x, initialY, transform.position.z);
            yield return null;
        }

        if (effectInstance != null)
        {
            effectInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            yield return new WaitWhile(() => effectInstance.IsAlive(true));
            Destroy(effectInstance.gameObject);
            Debug.Log("Destroyed effect");
        }
    }

    void NotifyThrower()
    {
        if (hasNotifiedThrower || thrower == null) return;
        Debug.Log("🌊 Bobber notifying ObjectThrower: landed in water.");
        thrower.NotifyBobberInWater();
        hasNotifiedThrower = true;
    }
}