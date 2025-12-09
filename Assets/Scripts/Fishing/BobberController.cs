using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
public class BobberController : MonoBehaviour
{
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
    public float struggleForce = 2f;
    public float directionChangeInterval = 1.0f;
    [Tooltip("Particle effect to play at the water's surface while the fish is struggling.")]
    public ParticleSystem struggleEffectPrefab;

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
    private Coroutine nibbleCoroutine;
    private Coroutine biteEffectCoroutine;
    private ParticleSystem biteMainInstance;
    private ParticleSystem biteSecondaryInstance;

    private bool isStruggling = false;
    private Vector3 struggleDirection;
    private float struggleTimer;

    private ParticleSystem activeStruggleEffect;

    public CaughtFish HookedFish => hookedFish;
    public GameObject ActiveFishModel => activeFishModel;

    // --- NEW: Public accessor for the struggle direction ---
    public Vector3 StruggleDirection => struggleDirection;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        audioSource = GetComponent<AudioSource>();
        initialLinearDamping = rb.linearDamping;
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

    public void SetStruggleActive(bool active)
    {
        isStruggling = active;
        if (active)
        {
            struggleTimer = 0;

            if (struggleEffectPrefab != null && activeStruggleEffect == null)
            {
                activeStruggleEffect = Instantiate(struggleEffectPrefab, transform.position, Quaternion.identity);
            }
        }
        else
        {
            if (activeStruggleEffect != null)
            {
                activeStruggleEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(activeStruggleEffect.gameObject, 5f);
                activeStruggleEffect = null;
            }
        }
    }

    void Update()
    {
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

    private void EnterWater()
    {
        isInWater = true;
        rb.linearDamping = 2f;
        rb.angularDamping = 2f;
        rb.linearDamping = waterDrag;
        PlayEffects();
        FishingEvents.OnBobberLandedInWater?.Invoke(this);

        if (audioSource != null && waterEntrySound != null && !hasPlayedSplashSound)
        {
            audioSource.PlayOneShot(waterEntrySound, waterEntryVolumeScale);
            hasPlayedSplashSound = true;
        }
    }

    void Start()
    {
        if (rb != null)
        {
            rb.AddTorque(Random.insideUnitSphere * airTumbleTorque, ForceMode.Impulse);
        }
    }

    public void HookFish(FishPreset fishPreset)
    {
        if (hookedFish != null) return;

        hookedFish = new CaughtFish(fishPreset);
        Debug.Log($"{hookedFish.GetDisplayName()} is on the line!");
        FishingEvents.OnFishBite?.Invoke(this);

        if (biteEffectCoroutine != null)
        {
            StopCoroutine(biteEffectCoroutine);
        }
        biteEffectCoroutine = StartCoroutine(BiteEffectRoutine());
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

    private void ApplyBuoyancy()
    {
        float targetY = waterSurfaceY - floatHeight;
        float depth = targetY - transform.position.y;

        if (depth > 0)
        {
            Vector3 force = Vector3.up * (depth * buoyancyForce - rb.linearVelocity.y * bounceDamp);
            rb.AddForce(force, ForceMode.Acceleration);
        }

        Quaternion targetRotation = Quaternion.Euler(0, rb.rotation.eulerAngles.y, 0);
        Quaternion newRotation = Quaternion.Slerp(rb.rotation, targetRotation, waterRotationSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(newRotation);
    }

    void OnDestroy()
    {
        StopAllCoroutines();

        // --- FIX: Cleanup orphaned effects immediately ---
        if (activeStruggleEffect != null)
        {
            Destroy(activeStruggleEffect.gameObject);
        }

        // Also cleanup bite effects if they are still running
        if (biteMainInstance != null) Destroy(biteMainInstance.gameObject);
        if (biteSecondaryInstance != null) Destroy(biteSecondaryInstance.gameObject);
    }

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
            rb.linearDamping = initialLinearDamping;
        }
    }

    private void PlayEffects()
    {
        if (hasSplashed) return;

        if (waterSplashEffect != null)
        {
            StartCoroutine(SplashRoutine());
        }

        if (followEffect != null)
        {
            ParticleSystem followInstance = Instantiate(followEffect, transform.position, Quaternion.identity);
            StartCoroutine(FollowRoutine(followInstance));
        }
        hasSplashed = true;
    }

    private IEnumerator SplashRoutine()
    {
        Vector3 splashPosition = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);
        for (int i = 0; i < splashCount; i++)
        {
            splashPosition.x = transform.position.x;
            splashPosition.z = transform.position.z;
            Instantiate(waterSplashEffect, splashPosition, Quaternion.identity);
            yield return new WaitForSeconds(splashInterval);
        }
    }

    private IEnumerator FollowRoutine(ParticleSystem effectInstance)
    {
        float startTime = Time.time;
        Vector3 effectPosition = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);

        while (Time.time < startTime + followDuration)
        {
            if (effectInstance == null) yield break;

            effectPosition.x = transform.position.x;
            effectPosition.z = transform.position.z;
            effectInstance.transform.position = effectPosition;
            yield return null;
        }

        if (effectInstance != null)
        {
            effectInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(effectInstance.gameObject, 5f);
        }
    }

    public void SwapBobberForFishModel()
    {
        if (hookedFish != null && activeFishModel == null)
        {
            if (bobberVisuals != null)
            {
                bobberVisuals.SetActive(false);
            }

            if (hookedFish.preset.fishPrefab != null)
            {
                activeFishModel = Instantiate(hookedFish.preset.fishPrefab, this.transform);
                activeFishModel.transform.localPosition = Vector3.zero;
                activeFishModel.transform.localRotation = Quaternion.identity;
            }
            FishingEvents.OnFishHooked?.Invoke(hookedFish);
        }
    }

    public void StartNibbleSequence(FishPreset preset)
    {
        if (nibbleCoroutine != null)
        {
            StopCoroutine(nibbleCoroutine);
        }
        nibbleCoroutine = StartCoroutine(NibbleRoutine(preset));
    }

    public void StopBiteEffects()
    {
        if (biteEffectCoroutine != null)
        {
            StopCoroutine(biteEffectCoroutine);
        }

        if (biteMainInstance != null)
        {
            biteMainInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(biteMainInstance.gameObject, 3f);
        }

        if (biteSecondaryInstance != null)
        {
            biteSecondaryInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(biteSecondaryInstance.gameObject, 3f);
        }
    }

    private IEnumerator NibbleRoutine(FishPreset fishPreset)
    {
        yield return new WaitForSeconds(nibbleInterval);
        int nibbleCount = Random.Range(minNibbles, maxNibbles + 1);

        for (int i = 0; i < nibbleCount; i++)
        {
            if (rb != null)
            {
                rb.AddForce(Vector3.down * nibbleForce, ForceMode.Impulse);
            }

            if (nibbleEffect != null && isInWater)
            {
                Vector3 effectPosition = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);
                Instantiate(nibbleEffect, effectPosition, Quaternion.identity);
            }
            FishingEvents.OnFishNibble?.Invoke(this);
            yield return new WaitForSeconds(nibbleInterval);
        }
        HookFish(fishPreset);
    }

    private IEnumerator BiteEffectRoutine()
    {
        float timer = 0f;
        Vector3 effectPosition = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);

        if (biteEffectMain != null && isInWater)
        {
            biteMainInstance = Instantiate(biteEffectMain, effectPosition, Quaternion.identity);
        }

        if (biteEffectSecondary != null && isInWater)
        {
            biteSecondaryInstance = Instantiate(biteEffectSecondary, effectPosition, Quaternion.identity);
        }

        while (timer < biteDuration)
        {
            if (rb != null && isInWater)
            {
                rb.AddForce(Vector3.down * biteForce, ForceMode.Force);
            }
            timer += Time.deltaTime;
            yield return null;
        }

        if (biteMainInstance != null)
        {
            biteMainInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(biteMainInstance.gameObject, 3f);
        }

        if (biteSecondaryInstance != null)
        {
            biteSecondaryInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(biteSecondaryInstance.gameObject, 3f);
        }
    }
}