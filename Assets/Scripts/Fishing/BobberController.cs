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
    public float waterAngularDamping = 2f;
    public float waterRotationSpeed = 2f;

    [Header("Nibble Settings")]
    public int minNibbles = 2;
    public int maxNibbles = 5;
    public float nibbleInterval = 0.75f;
    public float nibbleForce = 5f;

    [Header("Bite Settings")]
    [Tooltip("How far below the water surface the bobber sits while a fish is hooked. Replaces the floatHeight target during bite/fight.")]
    public float biteSubmergeDepth = 1.5f;
    [Tooltip("Delay before the bite event fires, to let the final nibble jab play out visually.")]
    public float biteDelay = 0.15f;

    [Header("Struggle Settings")]
    public float struggleForce = 2f;
    [Tooltip("Min/max time the fish holds a single struggle burst before picking a new one.")]
    public Vector2 struggleHoldRange = new Vector2(0.4f, 2.5f);
    [Tooltip("Max random angular offset from the perpendicular axis, in degrees. Breaks the pure left/right metronome while keeping the side cue readable.")]
    public float maxAngleOffsetDegrees = 35f;
    [Tooltip("Chance the fish keeps the same side (left/right) on a new burst instead of flipping. 0 = always alternate.")]
    [Range(0f, 1f)] public float repeatSideChance = 0.25f;
    [Tooltip("Random multiplier applied to struggleForce per burst. Lets weak tugs and hard jabs mix.")]
    public Vector2 forceMultiplierRange = new Vector2(0.5f, 1.6f);

    [Header("Struggle Tether")]
    [Tooltip("Radius of the AOE circle around the hook point that the fish prefers to stay within.")]
    public float tetherRadius = 3f;
    [Tooltip("Strength of the pull-back force applied when the bobber drifts beyond the tether radius. Acts only on the overshoot — feels free inside the radius.")]
    public float tetherReturnForce = 4f;
    [Tooltip("Fraction of tetherRadius past which new struggle bursts are biased to swim back toward the anchor. 0 = always biased inward, 1 = never biased.")]
    [Range(0f, 1f)] public float tetherInwardBiasThreshold = 0.7f;

    [Header("Struggle Obstacle Detection")]
    [Tooltip("Layers that count as obstacles the fish cannot swim through.")]
    public LayerMask obstacleCheckLayers;
    [Tooltip("How far ahead to check for obstacles in the struggle direction.")]
    public float obstacleCheckDistance = 2f;

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
    private bool isSubmerged = false;

    private bool isStruggling = false;
    private Vector3 struggleDirection;
    private float struggleTimer;
    private int currentStruggleSide = 1; // 1 or -1
    private float currentStruggleForceMultiplier = 1f;
    private Vector3 struggleAnchor;
    private bool hasStruggleAnchor = false;
    private Transform playerTransform;

    public CaughtFish HookedFish => hookedFish;
    public GameObject ActiveFishModel => activeFishModel;
    public Vector3 StruggleDirection => struggleDirection;

    public void SetPlayerTransform(Transform player) { playerTransform = player; }

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
            isSubmerged = false;
            SetStruggleActive(false);
            hasStruggleAnchor = false;

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
            rb.angularDamping = waterAngularDamping;
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
        float effectiveFloatHeight = isSubmerged ? biteSubmergeDepth : floatHeight;
        float targetY = waterSurfaceY - effectiveFloatHeight;
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
        // Compute perpendicular axis to the player→bobber line
        Vector3 toBobber = transform.position - (playerTransform != null ? playerTransform.position : transform.position - Vector3.forward);
        toBobber.y = 0f;
        if (toBobber.sqrMagnitude < 0.01f) toBobber = Vector3.forward;
        toBobber.Normalize();

        Vector3 perpendicular = Vector3.Cross(toBobber, Vector3.up).normalized;

        struggleTimer -= Time.fixedDeltaTime;
        if (struggleTimer <= 0f)
        {
            int inwardSide = ChooseInwardStruggleSide(perpendicular);
            if (inwardSide != 0)
            {
                currentStruggleSide = inwardSide;
            }
            else if (Random.value > repeatSideChance)
            {
                currentStruggleSide = -currentStruggleSide;
            }
            StartNewStruggleBurst(perpendicular);
        }

        // Check for obstacles ahead — flip side and pick a fresh burst if blocked
        if (obstacleCheckLayers != 0)
        {
            Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(rayOrigin, struggleDirection, obstacleCheckDistance, obstacleCheckLayers))
            {
                currentStruggleSide = -currentStruggleSide;
                StartNewStruggleBurst(perpendicular);
            }
        }

        if (rb != null && !rb.isKinematic)
        {
            rb.AddForce(struggleDirection * struggleForce * currentStruggleForceMultiplier, ForceMode.Acceleration);
            ApplyTetherPullBack();
        }
    }

    private int ChooseInwardStruggleSide(Vector3 perpendicular)
    {
        if (!hasStruggleAnchor || tetherRadius <= 0f) return 0;

        Vector3 offset = transform.position - struggleAnchor;
        offset.y = 0f;
        float dist = offset.magnitude;
        if (dist < tetherRadius * tetherInwardBiasThreshold) return 0;

        Vector3 toAnchor = -offset / dist;
        return Vector3.Dot(perpendicular, toAnchor) >= 0f ? 1 : -1;
    }

    private void ApplyTetherPullBack()
    {
        if (!hasStruggleAnchor || tetherRadius <= 0f || tetherReturnForce <= 0f) return;

        Vector3 offset = transform.position - struggleAnchor;
        offset.y = 0f;
        float dist = offset.magnitude;
        if (dist <= tetherRadius) return;

        Vector3 returnDir = -offset / dist;
        float overshoot = dist - tetherRadius;
        rb.AddForce(returnDir * tetherReturnForce * overshoot, ForceMode.Acceleration);
    }

    private void StartNewStruggleBurst(Vector3 perpendicular)
    {
        float angleOffset = Random.Range(-maxAngleOffsetDegrees, maxAngleOffsetDegrees);
        Vector3 baseDir = perpendicular * currentStruggleSide;
        struggleDirection = Quaternion.AngleAxis(angleOffset, Vector3.up) * baseDir;

        currentStruggleForceMultiplier = Random.Range(forceMultiplierRange.x, forceMultiplierRange.y);
        struggleTimer = Random.Range(struggleHoldRange.x, struggleHoldRange.y);
    }

    // ------------------------------------------------------------------------
    // FISHING LOGIC
    // ------------------------------------------------------------------------
    public void StartNibbleSequence(FishPreset preset)
    {
        if (nibbleCoroutine != null) StopCoroutine(nibbleCoroutine);
        nibbleCoroutine = StartCoroutine(NibbleRoutine(preset));
    }

    public void CancelNibbleSequence()
    {
        if (nibbleCoroutine != null)
        {
            StopCoroutine(nibbleCoroutine);
            nibbleCoroutine = null;
        }
    }

    public void HookFish(FishPreset fishPreset)
    {
        if (hookedFish != null) return;

        hookedFish = new CaughtFish(fishPreset);
        Debug.Log($"{hookedFish.GetDisplayName()} is on the line!");
        FishingEvents.OnFishBite?.Invoke(this);

        struggleAnchor = transform.position;
        hasStruggleAnchor = true;

        SpawnEffect(bitePrefab, biteLifetime);

        isSubmerged = true;
    }

    public void PlayAttractJiggle()
    {
        if (rb != null && isInWater && !rb.isKinematic)
        {
            rb.AddForce(Vector3.down * nibbleForce * 0.5f, ForceMode.Impulse);
        }
        SpawnEffect(nibblePrefab, nibbleLifetime);
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

        // Signal that the bite is about to happen, then do the final nibble
        FishingEvents.OnBiteImminent?.Invoke(this);
        if (rb != null) rb.AddForce(Vector3.down * nibbleForce, ForceMode.Impulse);
        SpawnEffect(nibblePrefab, nibbleLifetime);
        FishingEvents.OnFishNibble?.Invoke(this);
        yield return new WaitForSeconds(biteDelay);

        HookFish(fishPreset);
    }

    public void StopBiteEffects()
    {
        isSubmerged = false;
    }

    void OnDestroy()
    {
        StopAllCoroutines();
        if (activeWakeInstance != null) Destroy(activeWakeInstance);
    }
}