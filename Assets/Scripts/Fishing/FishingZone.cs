using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public class FishingZone : MonoBehaviour
{
    public FishPool fishPool;

    [Header("Fish Spawning")]
    [Tooltip("Prefab for the fish ripple effect. Must have a FishRipple component.")]
    public GameObject fishRipplePrefab;
    [Tooltip("Prefab for the UI indicator shown above fish when aiming.")]
    public GameObject aimIndicatorPrefab;
    [Tooltip("Maximum number of fish that can be in the zone at once.")]
    public int maxFishCount = 5;

    [Header("Respawn Timing")]
    [Tooltip("Minimum seconds before a new fish spawns.")]
    public float respawnTimeMin = 10f;
    [Tooltip("Maximum seconds before a new fish spawns.")]
    public float respawnTimeMax = 30f;

    [Header("Bobber Splash Scare")]
    [Tooltip("Fish within this radius of the bobber landing spot get scared.")]
    public float splashScareRadius = 4f;

    [Header("Water Detection")]
    [Tooltip("Tag used to find the water collider for surface height.")]
    public string waterTag = "Water";

    private Collider zoneCollider;
    private Collider waterCollider;
    private float waterSurfaceY;
    private BobberController currentBobber;
    private List<FishRipple> activeFish = new List<FishRipple>();
    [Header("Scare Settings")]
    [Tooltip("Max attract presses allowed within the scare window before the fish is scared.")]
    public int maxAttractsBeforeScare = 3;
    [Tooltip("Time window in seconds for tracking attract spam.")]
    public float scareWindow = 2f;

    private FishRipple currentlyAttractedFish;
    private float respawnTimer;
    private List<float> attractTimestamps = new List<float>();

    void Awake()
    {
        zoneCollider = GetComponent<Collider>();
        zoneCollider.isTrigger = true;
    }

    void Start()
    {
        FindWaterSurface();
        SpawnInitialFish();
        ResetRespawnTimer();
    }

    private void FindWaterSurface()
    {
        Bounds zoneBounds = zoneCollider.bounds;
        Collider[] overlapping = Physics.OverlapBox(zoneBounds.center, zoneBounds.extents, Quaternion.identity);

        for (int i = 0; i < overlapping.Length; i++)
        {
            if (overlapping[i].CompareTag(waterTag))
            {
                waterCollider = overlapping[i];
                waterSurfaceY = waterCollider.bounds.max.y;
                return;
            }
        }

        Debug.LogWarning($"FishingZone '{fishPool?.poolName}': No collider tagged '{waterTag}' found. Using zone collider top as water surface.");
        waterSurfaceY = zoneBounds.max.y;
    }

    private void OnEnable()
    {
        FishingEvents.OnAttractFish += HandleAttract;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnStartReeling += HandleReelIn;
        FishingEvents.OnCancelFishing += HandleReelIn;
    }

    private void OnDisable()
    {
        FishingEvents.OnAttractFish -= HandleAttract;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnStartReeling -= HandleReelIn;
        FishingEvents.OnCancelFishing -= HandleReelIn;
    }

    void Update()
    {
        CleanupNullFish();

        // Check if currently attracted fish was scared or otherwise reset
        if (currentlyAttractedFish != null)
        {
            if (currentlyAttractedFish.CurrentState != FishRipple.FishState.Attracted
                && currentlyAttractedFish.CurrentState != FishRipple.FishState.Nibbling)
            {
                currentlyAttractedFish = null;
                SetOtherFishAvoidance(false);
            }
        }

        // Respawn fish over time
        if (activeFish.Count < maxFishCount)
        {
            respawnTimer -= Time.deltaTime;
            if (respawnTimer <= 0f)
            {
                SpawnOneFish();
                ResetRespawnTimer();
            }
        }
    }

    private void SpawnInitialFish()
    {
        if (fishPool == null || fishPool.availableFish.Count == 0 || fishRipplePrefab == null) return;

        int initialCount = Random.Range(1, maxFishCount + 1);
        for (int i = 0; i < initialCount; i++)
        {
            SpawnOneFish();
        }
    }

    private void SpawnOneFish()
    {
        if (fishPool == null || fishPool.availableFish.Count == 0 || fishRipplePrefab == null) return;
        if (activeFish.Count >= maxFishCount) return;

        GameObject rippleObj = Instantiate(fishRipplePrefab, transform);
        FishRipple ripple = rippleObj.GetComponent<FishRipple>();

        if (ripple == null)
        {
            Debug.LogError("FishRipple prefab is missing the FishRipple component!");
            Destroy(rippleObj);
            return;
        }

        ripple.preset = fishPool.availableFish[Random.Range(0, fishPool.availableFish.Count)];
        ripple.Initialize(zoneCollider, waterSurfaceY, aimIndicatorPrefab);

        if (currentBobber != null)
            ripple.SetBobberTransform(currentBobber.transform);

        activeFish.Add(ripple);
    }

    private void ResetRespawnTimer()
    {
        respawnTimer = Random.Range(respawnTimeMin, respawnTimeMax);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent<BobberController>(out var bobber))
        {
            Rigidbody bobberRb = bobber.GetComponent<Rigidbody>();
            if (bobberRb != null && bobberRb.isKinematic)
                return;

            Debug.Log($"Bobber entered pool: {fishPool.poolName}");
            currentBobber = bobber;

            Vector3 splashPos = bobber.transform.position;
            for (int i = 0; i < activeFish.Count; i++)
            {
                if (activeFish[i] == null) continue;

                activeFish[i].SetBobberTransform(bobber.transform);

                float dist = HorizontalDistance(activeFish[i].transform.position, splashPos);
                if (dist < splashScareRadius)
                {
                    activeFish[i].Scare();
                }
            }
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag(waterTag))
        {
            waterSurfaceY = other.bounds.max.y;
            for (int i = 0; i < activeFish.Count; i++)
            {
                if (activeFish[i] != null)
                    activeFish[i].SetWaterSurfaceY(waterSurfaceY);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent<BobberController>(out var bobber) && bobber == currentBobber)
        {
            Debug.Log($"Bobber exited pool: {fishPool.poolName}");
            ClearBobber();
        }
    }

    private void HandleAttract()
    {
        if (currentBobber == null) return;

        // Track spam
        float now = Time.time;
        attractTimestamps.RemoveAll(t => now - t > scareWindow);
        attractTimestamps.Add(now);

        // If a fish is already attracted or nibbling, keep interacting with it
        if (currentlyAttractedFish != null)
        {
            // Pressing attract during nibbling scares the fish away
            if (currentlyAttractedFish.CurrentState == FishRipple.FishState.Nibbling)
            {
                currentlyAttractedFish.Scare();
                currentlyAttractedFish = null;
                attractTimestamps.Clear();
                SetOtherFishAvoidance(false);
            }
            // Spam check — scare the attracted fish
            else if (attractTimestamps.Count > maxAttractsBeforeScare)
            {
                currentlyAttractedFish.Scare();
                currentlyAttractedFish = null;
                attractTimestamps.Clear();
                SetOtherFishAvoidance(false);
            }
            else
            {
                // Notify the fish the player is still engaging — resets lose-interest timer
                currentlyAttractedFish.NotifyAttractInput();

                // Re-call attract — this handles the "too close = scare" check inside FishRipple
                currentlyAttractedFish.AttractToBobber();

                // If it got scared from being too close, clear it
                if (currentlyAttractedFish.CurrentState == FishRipple.FishState.Scared)
                {
                    currentlyAttractedFish = null;
                    attractTimestamps.Clear();
                    SetOtherFishAvoidance(false);
                }
            }
        }
        else
        {
            // No fish attracted yet — find nearest wandering fish
            FishRipple nearest = null;
            float nearestDist = float.MaxValue;

            for (int i = 0; i < activeFish.Count; i++)
            {
                FishRipple fish = activeFish[i];
                if (fish == null) continue;
                if (fish.CurrentState != FishRipple.FishState.Wandering) continue;

                float dist = HorizontalDistance(fish.transform.position, currentBobber.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = fish;
                }
            }

            if (nearest != null)
            {
                nearest.AttractToBobber();
                if (nearest.CurrentState == FishRipple.FishState.Attracted)
                {
                    currentlyAttractedFish = nearest;
                    attractTimestamps.Clear();
                    attractTimestamps.Add(now);
                    SetOtherFishAvoidance(true);
                }
            }
        }

        // Bobber jiggle feedback
        if (currentBobber != null)
        {
            currentBobber.PlayAttractJiggle();
        }
    }

    private void HandleReelIn()
    {
        if (currentBobber == null) return;

        Vector3 bobberPos = currentBobber.transform.position;
        for (int i = 0; i < activeFish.Count; i++)
        {
            if (activeFish[i] == null) continue;
            float dist = HorizontalDistance(activeFish[i].transform.position, bobberPos);
            if (dist < splashScareRadius || activeFish[i].CurrentState == FishRipple.FishState.Nibbling
                || activeFish[i].CurrentState == FishRipple.FishState.Attracted)
            {
                activeFish[i].Scare();
            }
        }

        currentlyAttractedFish = null;
        SetOtherFishAvoidance(false);
    }

    private void HandleFishBite(BobberController bobber)
    {
        if (bobber != currentBobber) return;

        // Remove the fish that was nibbling (it got caught)
        if (currentlyAttractedFish != null)
        {
            RemoveFish(currentlyAttractedFish);
            currentlyAttractedFish = null;
            SetOtherFishAvoidance(false);
        }
    }

    public void RemoveFish(FishRipple fish)
    {
        activeFish.Remove(fish);
        if (fish != null)
            Destroy(fish.gameObject);
    }

    private void CleanupNullFish()
    {
        activeFish.RemoveAll(f => f == null);
    }

    private void ClearBobber()
    {
        currentBobber = null;
        currentlyAttractedFish = null;
        attractTimestamps.Clear();
        for (int i = 0; i < activeFish.Count; i++)
        {
            if (activeFish[i] != null)
            {
                activeFish[i].SetAvoidBobber(false);
                activeFish[i].ClearBobberTransform();
            }
        }
    }

    private void SetOtherFishAvoidance(bool avoid)
    {
        for (int i = 0; i < activeFish.Count; i++)
        {
            if (activeFish[i] != null && activeFish[i] != currentlyAttractedFish)
            {
                activeFish[i].SetAvoidBobber(avoid);
            }
        }
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
