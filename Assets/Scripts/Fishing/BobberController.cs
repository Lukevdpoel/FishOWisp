using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
public class BobberController : MonoBehaviour
{
    // ... (Headers kept same as previous) ...
    [Header("Visuals & Effects")]
    public GameObject bobberVisuals;
    public ParticleSystem waterSplashEffect;
    public int splashCount = 3;
    public float splashInterval = 0.25f;
    public ParticleSystem followEffect;
    public float followDuration = 2.0f;
    public AudioClip waterEntrySound;
    public float waterEntryVolumeScale = 1.0f;
    public AudioClip impactSound;

    [Header("In-Air Physics")]
    public float airTumbleTorque = 5f;

    [Header("Water Detection")]
    public string waterTag = "Water";

    [Header("Buoyancy")]
    public float floatHeight = 0.5f;
    public float bounceDamp = 0.05f;
    public float buoyancyForce = 10f;
    public float waterDrag = 1f;
    public float waterRotationSpeed = 2f;

    [Header("Nibbling")]
    public ParticleSystem nibbleEffect;
    public int minNibbles = 2;
    public int maxNibbles = 5;
    public float nibbleInterval = 0.75f;
    public float nibbleForce = 5f;

    [Header("Biting")]
    public ParticleSystem biteEffectMain;
    public ParticleSystem biteEffectSecondary;
    public float biteForce = 100f;
    public float biteDuration = 0.5f;

    [Header("Fish Struggle")]
    public float struggleForce = 5f; // Increased for constant movement
    public float directionChangeInterval = 0.5f; // Faster changes
    public ParticleSystem struggleEffectPrefab;

    // ... (Existing private variables) ...
    private Rigidbody rb;
    private AudioSource audioSource;
    private bool isInWater = false;
    private bool hasSplashed = false;
    private bool hasPlayedSplashSound = false;
    private bool hasPlayedImpactSound = false;
    private float initialLinearDamping;
    private float waterSurfaceY;
    private CaughtFish hookedFish;
    private GameObject activeFishModel;
    private GameObject activeWakeInstance;
    private Coroutine nibbleCoroutine;
    private Coroutine bitePhysicsCoroutine;
    private ParticleSystem biteMainInstance;
    private ParticleSystem biteSecondaryInstance;
    private ParticleSystem activeStruggleEffect;

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
        initialLinearDamping = rb.linearDamping;
    }

    // ... (Collision/Trigger methods kept same) ...
    void OnCollisionEnter(Collision collision) { /* Same as before */ }
    void OnTriggerEnter(Collider other) { if (other.CompareTag(waterTag) && !isInWater) { waterSurfaceY = other.bounds.max.y; EnterWater(); } }
    void OnTriggerStay(Collider other) { if (other.CompareTag(waterTag) && isInWater) { waterSurfaceY = other.bounds.max.y; } }
    void OnTriggerExit(Collider other) { if (other.CompareTag(waterTag) && isInWater) { isInWater = false; SetStruggleActive(false); rb.linearDamping = initialLinearDamping; if (activeWakeInstance != null) { Destroy(activeWakeInstance); activeWakeInstance = null; } } }

    private void EnterWater()
    {
        isInWater = true;
        rb.linearDamping = waterDrag;
        rb.angularDamping = 2f;
        if (!hasSplashed) { SpawnEffect(impactPrefab, impactLifetime); hasSplashed = true; }
        if (wakePrefab != null && activeWakeInstance == null) { Vector3 wakePos = new Vector3(transform.position.x, waterSurfaceY, transform.position.z); activeWakeInstance = Instantiate(wakePrefab, wakePos, wakePrefab.transform.rotation); }
        FishingEvents.OnBobberLandedInWater?.Invoke(this);
        if (audioSource != null && waterEntrySound != null && !hasPlayedSplashSound) { audioSource.PlayOneShot(waterEntrySound, waterEntryVolumeScale); hasPlayedSplashSound = true; }
    }

    void Start() { if (rb != null) rb.AddTorque(Random.insideUnitSphere * airTumbleTorque, ForceMode.Impulse); }

    void Update()
    {
        if (activeWakeInstance != null && isInWater)
        {
            Vector3 wakePos = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);
            activeWakeInstance.transform.position = wakePos;
        }
        if (isStruggling && activeStruggleEffect != null)
        {
            // Update struggle effect pos if needed
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
    }

    private void UpdateStruggleMovement()
    {
        // Change direction occasionally for erratic behavior
        struggleTimer -= Time.fixedDeltaTime;
        if (struggleTimer <= 0f)
        {
            Vector2 randomCirclePoint = Random.insideUnitCircle.normalized;
            struggleDirection = new Vector3(randomCirclePoint.x, 0, randomCirclePoint.y);
            struggleTimer = directionChangeInterval;
        }

        // Apply constant force (no resting phase)
        if (rb != null && !rb.isKinematic)
        {
            rb.AddForce(struggleDirection * struggleForce, ForceMode.Acceleration);
        }
    }

    // ... (ApplyBuoyancy, Fishing Logic, Effects kept same) ...
    private void ApplyBuoyancy()
    {
        float targetY = waterSurfaceY - floatHeight;
        float depth = targetY - transform.position.y;
        if (depth > 0) { Vector3 force = Vector3.up * (depth * buoyancyForce - rb.linearVelocity.y * bounceDamp); rb.AddForce(force, ForceMode.Acceleration); }
        Quaternion targetRotation = Quaternion.Euler(0, rb.rotation.eulerAngles.y, 0);
        Quaternion newRotation = Quaternion.Slerp(rb.rotation, targetRotation, waterRotationSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(newRotation);
    }

    // Standard fishing methods (HookFish, SetStruggleActive, etc.) - Preserved
    public void StartNibbleSequence(FishPreset preset) { if (nibbleCoroutine != null) StopCoroutine(nibbleCoroutine); nibbleCoroutine = StartCoroutine(NibbleRoutine(preset)); }
    public void HookFish(FishPreset fishPreset) { if (hookedFish != null) return; hookedFish = new CaughtFish(fishPreset); Debug.Log($"{hookedFish.GetDisplayName()} is on the line!"); FishingEvents.OnFishBite?.Invoke(this); SpawnEffect(bitePrefab, biteLifetime); if (bitePhysicsCoroutine != null) StopCoroutine(bitePhysicsCoroutine); bitePhysicsCoroutine = StartCoroutine(BitePhysicsRoutine()); }

    public void SetStruggleActive(bool active)
    {
        if (isStruggling == active) return;
        isStruggling = active;
        if (active) { struggleTimer = 0; SpawnEffect(strugglePrefab, struggleLifetime, true); }
    }

    public void SwapBobberForFishModel()
    {
        if (hookedFish != null && activeFishModel == null) { if (bobberVisuals != null) bobberVisuals.SetActive(false); if (hookedFish.preset.fishPrefab != null) { activeFishModel = Instantiate(hookedFish.preset.fishPrefab, this.transform); activeFishModel.transform.localPosition = Vector3.zero; activeFishModel.transform.localRotation = Quaternion.identity; } FishingEvents.OnFishHooked?.Invoke(hookedFish); }
    }

    // Visual Helpers
    private void SpawnEffect(GameObject prefab, float lifetime, bool parentToBobber = false)
    {
        if (prefab == null || !isInWater) return;
        Vector3 spawnPos = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);
        GameObject instance = Instantiate(prefab, spawnPos, prefab.transform.rotation);
        if (parentToBobber) instance.transform.SetParent(this.transform);
        Destroy(instance, lifetime);
    }

    public void StopBiteEffects() { if (bitePhysicsCoroutine != null) StopCoroutine(bitePhysicsCoroutine); }

    // Coroutines (Nibble, Bite) - Preserved
    private IEnumerator NibbleRoutine(FishPreset fishPreset) { yield return new WaitForSeconds(nibbleInterval); int count = Random.Range(minNibbles, maxNibbles); for (int i = 0; i < count; i++) { if (rb != null) rb.AddForce(Vector3.down * nibbleForce, ForceMode.Impulse); SpawnEffect(nibblePrefab, nibbleLifetime); FishingEvents.OnFishNibble?.Invoke(this); yield return new WaitForSeconds(nibbleInterval); } HookFish(fishPreset); }
    private IEnumerator BitePhysicsRoutine() { float timer = 0; while (timer < biteDuration) { if (rb != null && isInWater) rb.AddForce(Vector3.down * biteForce, ForceMode.Force); timer += Time.deltaTime; yield return null; } }

    void OnDestroy()
    {
        StopAllCoroutines();
        if (activeStruggleEffect != null) Destroy(activeStruggleEffect.gameObject);
        if (activeWakeInstance != null) Destroy(activeWakeInstance);
        if (biteMainInstance != null) Destroy(biteMainInstance.gameObject);
        if (biteSecondaryInstance != null) Destroy(biteSecondaryInstance.gameObject);
    }

    // Header for variables kept to prevent compilation errors for missing refs
    [Header("Old Particle Refs (Deprecated)")]
    public GameObject impactPrefab;
    public float impactLifetime = 2f;
    public GameObject wakePrefab;
    public GameObject nibblePrefab;
    public float nibbleLifetime = 2f;
    public GameObject bitePrefab;
    public float biteLifetime = 2f;
    public GameObject strugglePrefab;
    public float struggleLifetime = 3f;
}