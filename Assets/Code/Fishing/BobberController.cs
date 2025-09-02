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
    [Tooltip("How much the bobber tumbles in the air when cast.")]
    public float airTumbleTorque = 5f;

    [Header("Water Detection")]
    public string waterTag = "Water";

    [Header("Buoyancy")]
    public float floatHeight = 0.5f;
    public float bounceDamp = 0.05f;
    public float buoyancyForce = 10f;
    public float waterDrag = 1f;
    [Tooltip("How quickly the bobber rights itself in the water.")]
    public float waterRotationSpeed = 2f;

    private Rigidbody rb;
    private bool isInWater = false;
    private bool hasSplashed = false;
    private float initialLinearDamping;
    private float waterSurfaceY;
    private CaughtFish hookedFish;
    private GameObject activeFishModel;

    public CaughtFish HookedFish => hookedFish;
    public GameObject ActiveFishModel => activeFishModel;


    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        initialLinearDamping = rb.linearDamping;
    }

    void Start()
    {
        if (rb != null)
        {
            Vector3 randomTorque = Random.insideUnitSphere * airTumbleTorque;
            rb.AddTorque(randomTorque, ForceMode.Impulse);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(waterTag))
        {
            if (!isInWater)
            {
                waterSurfaceY = other.bounds.max.y;
                EnterWater();
            }
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
        if (other.CompareTag(waterTag))
        {
            if (isInWater)
            {
                isInWater = false;
                rb.linearDamping = initialLinearDamping;
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

    void FixedUpdate()
    {
        if (isInWater)
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
    }

    public void HookFish(FishPreset fishPreset)
    {
        if (hookedFish != null) return;
        hookedFish = new CaughtFish(fishPreset);
        Debug.Log($"{hookedFish.GetDisplayName()} is on the line!");
        FishingEvents.OnFishBite?.Invoke(this);
    }

    // REMOVED: The OnEnable and OnDisable methods are no longer needed here.

    // CHANGED: This method is now public and has a new name. It will be called by FishingLine.
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
}