using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
public class BobberController : MonoBehaviour
{
    [Header("Visuals & Effects")]
    public GameObject bobberVisuals;
    public ParticleSystem waterSplashEffect;
    public int splashCount = 3;
    public float splashInterval = 0.25f;
    public ParticleSystem followEffect;
    public float followDuration = 2.0f;

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
    public float struggleForce = 2f;
    public float directionChangeInterval = 1.0f;
    // NEW: Add a field for the struggle particle effect prefab
    [Tooltip("Particle effect to play at the water's surface while the fish is struggling.")]
    public ParticleSystem struggleEffectPrefab;

    private Rigidbody rb;
    private bool isInWater = false;
    private bool hasSplashed = false;
    private float initialLinearDamping;
    private float waterSurfaceY;
    private CaughtFish hookedFish;
    private GameObject activeFishModel;
    private Coroutine nibbleCoroutine;
    private Coroutine biteEffectCoroutine;
    private ParticleSystem biteMainInstance;
    private ParticleSystem biteSecondaryInstance;

    private bool isStruggling = false;
    private Vector3 struggleDirection;
    private float struggleTimer;

    // NEW: A reference to the currently active struggle effect
    private ParticleSystem activeStruggleEffect;

    public CaughtFish HookedFish => hookedFish;
    public GameObject ActiveFishModel => activeFishModel;

    public void SetStruggleActive(bool active)
    {
        isStruggling = active;
        if (active)
        {
            struggleTimer = 0; // Reset timer to get a new direction immediately

            // NEW: Instantiate the struggle effect if it's assigned and not already active
            if (struggleEffectPrefab != null && activeStruggleEffect == null)
            {
                activeStruggleEffect = Instantiate(struggleEffectPrefab, transform.position, Quaternion.identity);
            }
        }
        else
        {
            // NEW: Stop and destroy the struggle effect when the struggle ends
            if (activeStruggleEffect != null)
            {
                activeStruggleEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(activeStruggleEffect.gameObject, 5f); // Destroy after particles have faded
                activeStruggleEffect = null;
            }
        }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        initialLinearDamping = rb.linearDamping;
    }

    // NEW: Added an Update method to keep the particle effect at the water's surface
    void Update()
    {
        // Keep the active struggle effect positioned at the water surface below the bobber
        if (isStruggling && activeStruggleEffect != null && isInWater)
        {
            Vector3 effectPosition = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);
            activeStruggleEffect.transform.position = effectPosition;
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

    // (The rest of the script remains exactly the same as the previous version)
    #region Unchanged Methods
    void Start() { if (rb != null) { rb.AddTorque(Random.insideUnitSphere * airTumbleTorque, ForceMode.Impulse); } }
    public void HookFish(FishPreset fishPreset) { if (hookedFish != null) return; hookedFish = new CaughtFish(fishPreset); Debug.Log($"{hookedFish.GetDisplayName()} is on the line!"); FishingEvents.OnFishBite?.Invoke(this); if (biteEffectCoroutine != null) StopCoroutine(biteEffectCoroutine); biteEffectCoroutine = StartCoroutine(BiteEffectRoutine()); }
    private void UpdateStruggleMovement() { struggleTimer -= Time.fixedDeltaTime; if (struggleTimer <= 0f) { Vector2 randomCirclePoint = Random.insideUnitCircle.normalized; struggleDirection = new Vector3(randomCirclePoint.x, 0, randomCirclePoint.y); struggleTimer = directionChangeInterval; } if (rb != null && !rb.isKinematic) { rb.AddForce(struggleDirection * struggleForce, ForceMode.Acceleration); } }
    private void ApplyBuoyancy() { float targetY = waterSurfaceY - floatHeight; float depth = targetY - transform.position.y; if (depth > 0) { Vector3 force = Vector3.up * (depth * buoyancyForce - rb.linearVelocity.y * bounceDamp); rb.AddForce(force, ForceMode.Acceleration); } Quaternion targetRotation = Quaternion.Euler(0, rb.rotation.eulerAngles.y, 0); Quaternion newRotation = Quaternion.Slerp(rb.rotation, targetRotation, waterRotationSpeed * Time.fixedDeltaTime); rb.MoveRotation(newRotation); }
    void OnDestroy() { StopAllCoroutines(); }
    void OnTriggerEnter(Collider other) { if (other.CompareTag(waterTag) && !isInWater) { waterSurfaceY = other.bounds.max.y; EnterWater(); } }
    void OnTriggerStay(Collider other) { if (other.CompareTag(waterTag) && isInWater) { waterSurfaceY = other.bounds.max.y; } }
    void OnTriggerExit(Collider other) { if (other.CompareTag(waterTag) && isInWater) { isInWater = false; SetStruggleActive(false); rb.linearDamping = initialLinearDamping; } }
    private void EnterWater() { isInWater = true; rb.linearDamping = 2f; rb.angularDamping = 2f; rb.linearDamping = waterDrag; PlayEffects(); FishingEvents.OnBobberLandedInWater?.Invoke(this); }
    private void PlayEffects() { if (hasSplashed) return; if (waterSplashEffect != null) StartCoroutine(SplashRoutine()); if (followEffect != null) { ParticleSystem f = Instantiate(followEffect, transform.position, Quaternion.identity); StartCoroutine(FollowRoutine(f)); } hasSplashed = true; }
    private IEnumerator SplashRoutine() { Vector3 p = new Vector3(transform.position.x, waterSurfaceY, transform.position.z); for (int i = 0; i < splashCount; i++) { p.x = transform.position.x; p.z = transform.position.z; Instantiate(waterSplashEffect, p, Quaternion.identity); yield return new WaitForSeconds(splashInterval); } }
    private IEnumerator FollowRoutine(ParticleSystem e) { float s = Time.time; Vector3 p = new Vector3(transform.position.x, waterSurfaceY, transform.position.z); while (Time.time < s + followDuration) { if (e == null) yield break; p.x = transform.position.x; p.z = transform.position.z; e.transform.position = p; yield return null; } if (e != null) { e.Stop(true, ParticleSystemStopBehavior.StopEmitting); Destroy(e.gameObject, 5f); } }
    public void SwapBobberForFishModel() { if (hookedFish != null && activeFishModel == null) { if (bobberVisuals != null) { bobberVisuals.SetActive(false); } if (hookedFish.preset.fishPrefab != null) { activeFishModel = Instantiate(hookedFish.preset.fishPrefab, this.transform); activeFishModel.transform.localPosition = Vector3.zero; activeFishModel.transform.localRotation = Quaternion.identity; } FishingEvents.OnFishHooked?.Invoke(hookedFish); } }
    public void StartNibbleSequence(FishPreset p) { if (nibbleCoroutine != null) StopCoroutine(nibbleCoroutine); nibbleCoroutine = StartCoroutine(NibbleRoutine(p)); }
    public void StopBiteEffects() { if (biteEffectCoroutine != null) StopCoroutine(biteEffectCoroutine); if (biteMainInstance != null) { biteMainInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting); Destroy(biteMainInstance.gameObject, 3f); } if (biteSecondaryInstance != null) { biteSecondaryInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting); Destroy(biteSecondaryInstance.gameObject, 3f); } }
    private IEnumerator NibbleRoutine(FishPreset f) { yield return new WaitForSeconds(nibbleInterval); int c = Random.Range(minNibbles, maxNibbles + 1); for (int i = 0; i < c; i++) { if (rb != null) rb.AddForce(Vector3.down * nibbleForce, ForceMode.Impulse); if (nibbleEffect != null && isInWater) { Vector3 p = new Vector3(transform.position.x, waterSurfaceY, transform.position.z); Instantiate(nibbleEffect, p, Quaternion.identity); } FishingEvents.OnFishNibble?.Invoke(this); yield return new WaitForSeconds(nibbleInterval); } HookFish(f); }
    private IEnumerator BiteEffectRoutine() { float t = 0f; Vector3 p = new Vector3(transform.position.x, waterSurfaceY, transform.position.z); if (biteEffectMain != null && isInWater) biteMainInstance = Instantiate(biteEffectMain, p, Quaternion.identity); if (biteEffectSecondary != null && isInWater) biteSecondaryInstance = Instantiate(biteEffectSecondary, p, Quaternion.identity); while (t < biteDuration) { if (rb != null && isInWater) rb.AddForce(Vector3.down * biteForce, ForceMode.Force); t += Time.deltaTime; yield return null; } if (biteMainInstance != null) { biteMainInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting); Destroy(biteMainInstance.gameObject, 3f); } if (biteSecondaryInstance != null) { biteSecondaryInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting); Destroy(biteSecondaryInstance.gameObject, 3f); } }
    #endregion
}