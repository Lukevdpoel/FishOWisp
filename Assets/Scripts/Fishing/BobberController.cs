using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
public class BobberController : MonoBehaviour
{
    // ========================================================================
    // 1. IMPACT SPLASH (When bobber hits water)
    // ========================================================================
    [Header("1. Impact Splash")]
    [Tooltip("Prefab spawned ONCE when the bobber first lands in the water.")]
    public GameObject impactPrefab;
    [Tooltip("How long the impact prefab lasts.")]
    public float impactLifetime = 2.0f;

    // ========================================================================
    // 2. WAKE / TRAIL (Continuous while in water)
    // ========================================================================
    [Header("2. Wake / Trail")]
    [Tooltip("Particle/Prefab that follows the bobber on the water surface.")]
    public GameObject wakePrefab;

    // ========================================================================
    // 3. NIBBLE SPLASH (When fish touches bobber)
    // ========================================================================
    [Header("3. Nibble Splash")]
    [Tooltip("Prefab spawned each time a fish nibbles.")]
    public GameObject nibblePrefab;
    [Tooltip("How long the nibble prefab lasts.")]
    public float nibbleLifetime = 2.0f;

    // ========================================================================
    // 4. BITE SPLASH (When fish hooks)
    // ========================================================================
    [Header("4. Bite Splash")]
    [Tooltip("Prefab spawned once when the fish actually bites/hooks.")]
    public GameObject bitePrefab;
    [Tooltip("How long the bite prefab lasts.")]
    public float biteLifetime = 2.0f;

    // ========================================================================
    // 5. STRUGGLE SPLASH (When fish starts fighting)
    // ========================================================================
    [Header("5. Struggle Splash")]
    [Tooltip("Prefab spawned when the fish starts struggling.")]
    public GameObject strugglePrefab;
    [Tooltip("How long the struggle prefab lasts.")]
    public float struggleLifetime = 3.0f;

    [Header("Audio")]
    public AudioClip waterEntrySound;
    public float waterEntryVolumeScale = 1.0f;
    public AudioClip impactSound;

    [Header("In-Air Physics")]
    public float airTumbleTorque = 5f;

    [Tooltip("Extra downward force applied when in the air to prevent 'floatiness'. Increase this to make it fall faster.")]
    public float extraGravity = 30f; // --- NEW: Fix for "Moon Gravity" ---

    [Header("Water Detection")]
    public string waterTag = "Water";

    [Header("Buoyancy")]
    public float floatHeight = 0.5f;
    public float bounceDamp = 0.05f;
    public float buoyancyForce = 10f;
    public float waterDrag = 1f;
    public float waterRotationSpeed = 2f;

    [Header("Nibble Settings")]
    public int minNibbles = 2;
    public int maxNibbles = 5;
    public float nibbleInterval = 0.75f;
    public float nibbleForce = 5f;

    [Header("Bite Settings")]
    public float biteForce = 100f;
    public float biteDuration = 0.5f;

    [Header("Struggle Settings")]
    public float struggleForce = 2f;
    public float directionChangeInterval = 1.0f;

    [Header("Visuals")]
    public GameObject bobberVisuals;

    // Internal State
    private Rigidbody rb;
    private AudioSource audioSource;
    private bool isInWater = false;
    private bool hasSplashed = false;
    private bool hasPlayedSplashSound = false;
    private bool hasPlayedImpactSound = false;
    private float initialLinearDamping; // Unity 6 Name
    private float waterSurfaceY;

    private CaughtFish hookedFish;
    private GameObject activeFishModel;

    // Track the active wake instance
    private GameObject activeWakeInstance;

    private Coroutine nibbleCoroutine;
    private Coroutine bitePhysicsCoroutine;

    private bool isStruggling = false;
    private Vector3 struggleDirection;
    private float struggleTimer;

    public CaughtFish HookedFish => hookedFish;
    public GameObject ActiveFishModel => activeFishModel;
    public Vector3 StruggleDirection => struggleDirection;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        audioSource = GetComponent<AudioSource>();

        // UNITY 6 FIX: Use 'linearDamping' instead of 'drag'
        if (rb != null)
        {
            initialLinearDamping = rb.linearDamping;
        }
    }

    void Start()
    {
        if (rb != null)
        {
            rb.AddTorque(Random.insideUnitSphere * airTumbleTorque, ForceMode.Impulse);
        }
    }

    void Update()
    {
        // Keep the wake at the water surface, following the bobber
        if (activeWakeInstance != null && isInWater)
        {
            Vector3 wakePos = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);
            activeWakeInstance.transform.position = wakePos;
        }
    }

    void FixedUpdate()
    {
        if (isInWater)
        {
            ApplyBuoyancy();
            if (isStruggling)
            {
                UpdateStruggleMovement();
            }
        }
        else
        {
            // --- NEW: APPLY EXTRA GRAVITY ---
            // This pushes the bobber down faster when it is flying through the air.
            if (rb != null && !rb.isKinematic)
            {
                rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
            }
        }
    }

    // ------------------------------------------------------------------------
    // WATER ENTRY LOGIC
    // ------------------------------------------------------------------------
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(waterTag) && !isInWater)
        {
            waterSurfaceY = other.bounds.max.y;
            EnterWater();
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag(waterTag) && isInWater)
        {
            waterSurfaceY = other.bounds.max.y;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(waterTag) && isInWater)
        {
            isInWater = false;
            SetStruggleActive(false);

            // UNITY 6 FIX: Reset Damping
            if (rb != null)
            {
                rb.linearDamping = initialLinearDamping;
            }

            // Destroy Wake when leaving water
            if (activeWakeInstance != null)
            {
                Destroy(activeWakeInstance);
                activeWakeInstance = null;
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!isInWater && !hasPlayedImpactSound)
        {
            if (!collision.gameObject.CompareTag(waterTag))
            {
                if (audioSource != null && impactSound != null)
                {
                    audioSource.PlayOneShot(impactSound);
                }
                hasPlayedImpactSound = true;
            }
        }
    }

    private void EnterWater()
    {
        isInWater = true;

        // UNITY 6 FIX: Apply Water Drag
        if (rb != null)
        {
            rb.linearDamping = waterDrag;
            rb.angularDamping = 2f; // Was angularDrag
        }

        // 1. TRIGGER IMPACT SPLASH
        if (!hasSplashed)
        {
            SpawnEffect(impactPrefab, impactLifetime);
            hasSplashed = true;
        }

        // 2. SPAWN WAKE / TRAIL
        if (wakePrefab != null && activeWakeInstance == null)
        {
            Vector3 wakePos = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);
            activeWakeInstance = Instantiate(wakePrefab, wakePos, wakePrefab.transform.rotation);
        }

        FishingEvents.OnBobberLandedInWater?.Invoke(this);

        if (audioSource != null && waterEntrySound != null && !hasPlayedSplashSound)
        {
            audioSource.PlayOneShot(waterEntrySound, waterEntryVolumeScale);
            hasPlayedSplashSound = true;
        }
    }

    // ------------------------------------------------------------------------
    // PHYSICS & MOVEMENT
    // ------------------------------------------------------------------------
    private void ApplyBuoyancy()
    {
        float targetY = waterSurfaceY - floatHeight;
        float depth = targetY - transform.position.y;

        if (depth > 0)
        {
            // UNITY 6 FIX: Use 'linearVelocity' instead of 'velocity'
            Vector3 force = Vector3.up * (depth * buoyancyForce - rb.linearVelocity.y * bounceDamp);
            rb.AddForce(force, ForceMode.Acceleration);
        }

        Quaternion targetRotation = Quaternion.Euler(0, rb.rotation.eulerAngles.y, 0);
        Quaternion newRotation = Quaternion.Slerp(rb.rotation, targetRotation, waterRotationSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(newRotation);
    }

    private void UpdateStruggleMovement()
    {
        struggleTimer -= Time.fixedDeltaTime;
        if (struggleTimer <= 0f)
        {
            Vector2 randomCirclePoint = Random.insideUnitCircle.normalized;
            struggleDirection = new Vector3(randomCirclePoint.x, 0, randomCirclePoint.y);
            struggleTimer = directionChangeInterval;
        }

        if (rb != null && !rb.isKinematic)
        {
            rb.AddForce(struggleDirection * struggleForce, ForceMode.Acceleration);
        }
    }

    // ------------------------------------------------------------------------
    // FISHING LOGIC
    // ------------------------------------------------------------------------
    public void StartNibbleSequence(FishPreset preset)
    {
        if (nibbleCoroutine != null) StopCoroutine(nibbleCoroutine);
        nibbleCoroutine = StartCoroutine(NibbleRoutine(preset));
    }

    public void HookFish(FishPreset fishPreset)
    {
        if (hookedFish != null) return;

        hookedFish = new CaughtFish(fishPreset);
        Debug.Log($"{hookedFish.GetDisplayName()} is on the line!");
        FishingEvents.OnFishBite?.Invoke(this);

        SpawnEffect(bitePrefab, biteLifetime);

        if (bitePhysicsCoroutine != null) StopCoroutine(bitePhysicsCoroutine);
        bitePhysicsCoroutine = StartCoroutine(BitePhysicsRoutine());
    }

    public void SetStruggleActive(bool active)
    {
        if (isStruggling == active) return;

        isStruggling = active;
        if (active)
        {
            struggleTimer = 0;
            SpawnEffect(strugglePrefab, struggleLifetime, true);
        }
    }

    public void SwapBobberForFishModel()
    {
        if (hookedFish != null && activeFishModel == null)
        {
            if (bobberVisuals != null) bobberVisuals.SetActive(false);

            if (hookedFish.preset.fishPrefab != null)
            {
                activeFishModel = Instantiate(hookedFish.preset.fishPrefab, this.transform);
                activeFishModel.transform.localPosition = Vector3.zero;
                activeFishModel.transform.localRotation = Quaternion.identity;
            }
            FishingEvents.OnFishHooked?.Invoke(hookedFish);
        }
    }

    // ------------------------------------------------------------------------
    // EFFECT ROUTINES
    // ------------------------------------------------------------------------

    private void SpawnEffect(GameObject prefab, float lifetime, bool parentToBobber = false)
    {
        if (prefab == null || !isInWater) return;

        Vector3 spawnPos = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);
        Quaternion spawnRot = prefab.transform.rotation;

        GameObject instance = Instantiate(prefab, spawnPos, spawnRot);

        if (parentToBobber)
        {
            instance.transform.SetParent(this.transform);
        }

        Destroy(instance, lifetime);
    }

    private IEnumerator NibbleRoutine(FishPreset fishPreset)
    {
        yield return new WaitForSeconds(nibbleInterval);
        int nibbleCount = Random.Range(minNibbles, maxNibbles + 1);

        for (int i = 0; i < nibbleCount; i++)
        {
            if (rb != null) rb.AddForce(Vector3.down * nibbleForce, ForceMode.Impulse);
            SpawnEffect(nibblePrefab, nibbleLifetime);
            FishingEvents.OnFishNibble?.Invoke(this);
            yield return new WaitForSeconds(nibbleInterval);
        }
        HookFish(fishPreset);
    }

    private IEnumerator BitePhysicsRoutine()
    {
        float timer = 0f;
        while (timer < biteDuration)
        {
            if (rb != null && isInWater)
            {
                rb.AddForce(Vector3.down * biteForce, ForceMode.Force);
            }
            timer += Time.deltaTime;
            yield return null;
        }
    }

    public void StopBiteEffects()
    {
        if (bitePhysicsCoroutine != null) StopCoroutine(bitePhysicsCoroutine);
    }

    void OnDestroy()
    {
        StopAllCoroutines();
        if (activeWakeInstance != null) Destroy(activeWakeInstance);
    }
}