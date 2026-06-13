using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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

    [Header("Water Impact Absorption")]
    [Tooltip("Fraction of the bobber's downward velocity that survives the moment it touches water. " +
             "The buoyancy spring is intentionally light, so without absorption the cast's plunge " +
             "rebounds violently. 0 = no plunge at all, 1 = no absorption (original bouncy behavior). " +
             "0.15 = the bobber briefly dips ~15cm and settles. Only the downward Y is dampened; " +
             "horizontal drift is preserved so the bobber still glides slightly after a long cast.")]
    [Range(0f, 1f)] public float waterImpactVelocityRetention = 0.15f;

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
    [Tooltip("How hard the struggling fish pulls the line straight away from the player (acceleration). Makes struggles visibly take line.")]
    public float strugglePullAwayForce = 2.5f;
    [Tooltip("Max distance the fish can drag its tether anchor away from the bite point. Caps how much line a struggle can take; reeling wins it back, tug-of-war style.")]
    public float maxPullAwayDistance = 3f;
    [Tooltip("Speed (m/s) the tether anchor itself drifts away from the player during a struggle, so gained line sticks instead of springing back to the bite point.")]
    public float pullAwayAnchorSpeed = 0.5f;

    [Header("Fight Jump")]
    [Tooltip("Chance per struggle burst that the hooked fish leaps out of the water. 0 disables jumps.")]
    [Range(0f, 1f)] public float fightJumpChance = 0.15f;
    [Tooltip("Vertical launch speed (m/s) of the leap. The leap is purely ballistic (buoyancy is " +
             "kept out of it), so breach height ≈ jumpUpSpeed²/(2·9.81) minus the submerge depth. " +
             "At the default 1.5 m depth, ~5.6 pokes the fish ~0.1 m clear of the surface for a " +
             "brief writhe — raise for a bigger leap, lower for a flopping head-poke (below ~5.45 " +
             "it stays under the surface).")]
    public float jumpUpSpeed = 5.6f;
    [Tooltip("Horizontal speed (m/s) carried along the current struggle direction so the leap arcs sideways instead of straight up.")]
    public float jumpForwardSpeed = 1.2f;
    [Tooltip("Minimum seconds between leaps.")]
    public float jumpCooldown = 5f;
    [Tooltip("Safety net: if the fish hasn't splashed back down after this many seconds (e.g. the leap never broke the surface), fight state is restored anyway.")]
    public float maxJumpDuration = 2.5f;

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
    [Tooltip("Silhouette material for the hooked fish that replaces the bobber/lure at the bite. Assign the Fish Silhouette Sway material; when left empty it's found by shader name at runtime.")]
    public Material hookedFishSilhouetteMaterial;

    [Header("Camera")]
    [Tooltip("Child Transform that defines the camera pose used while the bobber is in the water. Parent it under the bobber and position/rotate it however the in-water camera should sit. Leave null to fall back to the formula in PlayerCameraController.")]
    [SerializeField] private Transform cameraAnchor;
    public Transform CameraAnchor => cameraAnchor;

    [Header("Line Attachment")]
    [Tooltip("Child Transform where the fishing line connects to this bobber. Leave null to attach at the bobber's root.")]
    [SerializeField] private Transform lineAttachPoint;
    public Transform LineAttachPoint => lineAttachPoint != null ? lineAttachPoint : transform;

    // Raw attach transform for the hooked-fish visual: while a fish is on the line it parks
    // this on the fish's mouth every frame (and restores it on release), so the rope visibly
    // runs to the snout instead of the hidden bobber.
    public Transform LineAttachTransform => lineAttachPoint;

    // Internal State
    private Rigidbody rb;
    private AudioSource audioSource;
    private bool isInWater = false;
    private bool isInFlight = false;
    private bool hasSplashed = false;
    private bool hasPlayedSplashSound = false;
    private bool hasPlayedImpactSound = false;
    private float initialLinearDamping; // Unity 6 Name
    private float initialAngularDamping;
    private float waterSurfaceY;

    // Adjacent fishing zones can have overlapping water trigger volumes at their seam. Water
    // state must therefore be reference-counted: the bobber is only "out of the water" once it
    // has left ALL volumes, otherwise exiting one volume while floating in the other kills
    // buoyancy and the bobber sinks through the seam.
    private readonly HashSet<Collider> waterVolumes = new HashSet<Collider>();

    private CaughtFish hookedFish;
    private GameObject activeFishModel;
    private static Material sharedHookedSilhouette;

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
    private Vector3 initialStruggleAnchor;
    private bool hasStruggleAnchor = false;
    private Transform playerTransform;

    private bool jumpActive = false;
    private float jumpStartTime;
    private float nextJumpAllowedTime;

    public CaughtFish HookedFish => hookedFish;
    public GameObject ActiveFishModel => activeFishModel;
    public Transform PlayerTransform => playerTransform;
    public bool IsStruggling => isStruggling;
    public Vector3 StruggleDirection => struggleDirection;
    public bool IsInWater => isInWater;
    public float WaterSurfaceY => waterSurfaceY;
    // True while a hooked fish is mid-leap (airborne). HookedFishController reads this to free
    // its body chain's vertical lock so the tail trails the head's arc, then re-locks on landing.
    public bool IsHookedFishJumping => jumpActive;

    // Set externally (LureReelHandler) to drive the "speedboat" visual: positive pitch raises
    // the LineAttachPoint side, bob offset bobs the buoyancy target up/down. Both are read by
    // ApplyBuoyancy each FixedUpdate. Reset to 0 in ResetForCast so a parked bobber sits level.
    [System.NonSerialized] public float lureVisualPitchDeg = 0f;
    [System.NonSerialized] public float lureBobOffset = 0f;

    [Header("Grab Tow")]
    [Tooltip("Max speed (m/s) the lure is dragged to follow a fish that has grabbed it during a " +
             "TP bite. Keep above the fish's grab swim speed so the lure stays at its mouth. While " +
             "towed, buoyancy/struggle are suspended and the fish drives the lure; normal physics " +
             "resume the instant it lets go or the bite is set. The reel handler is never touched.")]
    public float grabTowMaxSpeed = 6f;

    // True while a grabbing fish is towing the lure to its mouth (set via BeginGrabTow/EndGrabTow).
    [System.NonSerialized] private bool isGrabTowed;
    [System.NonSerialized] private Vector3 grabTowTarget;

    [Header("Lure Inertia Override")]
    [Tooltip("If > 0, overrides Unity's auto-computed inertia tensor with this uniform value " +
             "when the bobber enters water. Lures need this — Unity's default for a small " +
             "sphere is tiny (~0.03), which makes AddForceAtPosition at the LineAttachPoint " +
             "spin the lure ~16× too fast. 0.2-0.4 gives a controlled response. Leave 0 on " +
             "regular bobbers to keep their hand-tuned nibble/struggle behavior unchanged.")]
    public float waterInertiaTensorOverride = 0f;

    public void SetPlayerTransform(Transform player) { playerTransform = player; }

    // ----- TP grab tow: a fish that clamped onto the lure drags it along until it lets go -----
    public void BeginGrabTow() { isGrabTowed = true; grabTowTarget = transform.position; }
    public void SetGrabTowTarget(Vector3 worldPos) { grabTowTarget = worldPos; }
    public void EndGrabTow() { isGrabTowed = false; }

    /// <summary>
    /// Slide the struggle tether anchor by a world-space delta. Called by the rod while the
    /// player is actively reeling, so the fish's "home" spot moves with it instead of yanking
    /// the bobber back toward the original hook point as it gets close to the player.
    /// </summary>
    public void ShiftStruggleAnchor(Vector3 delta)
    {
        if (!hasStruggleAnchor) return;
        struggleAnchor += delta;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        audioSource = GetComponent<AudioSource>();

        // UNITY 6 FIX: Use 'linearDamping' instead of 'drag'
        if (rb != null)
        {
            initialLinearDamping = rb.linearDamping;
            initialAngularDamping = rb.angularDamping;
        }
    }

    void Start()
    {
        // Air-tumble torque is now applied explicitly on launch via ApplyAirTumbleTorque(). Applying
        // it here used to be OK because the bobber was destroyed each cast, but with a persistent
        // instance + Free angular motion on the joint, spawn-time torque spins the bobber forever.
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
        // A fish that grabbed the lure drives it to its mouth: drag the rigidbody along the
        // fish's path and suspend buoyancy/struggle for the hold. Released the instant the fish
        // spits it or the bite is set, where normal physics resume.
        if (isGrabTowed)
        {
            if (rb != null && !rb.isKinematic)
                rb.MovePosition(Vector3.MoveTowards(rb.position, grabTowTarget, grabTowMaxSpeed * Time.fixedDeltaTime));
            return;
        }

        // End the leap on the bobber's own descent rather than waiting on the water trigger:
        // a low writhe may never fully clear the trigger volume, so EnterWater wouldn't fire.
        // Once the fish is past its apex (falling) and back under the surface — or the timeout
        // trips — restore the submerged fight state. EnterWater's splash path also calls
        // EndFightJump for full breaches; whichever happens first wins, the other no-ops.
        if (jumpActive)
        {
            bool timedOut = Time.time - jumpStartTime > maxJumpDuration;
            bool fallenBack = rb != null && rb.linearVelocity.y <= 0f
                              && transform.position.y <= waterSurfaceY;
            if (timedOut || fallenBack)
            {
                if (isInWater && hookedFish != null)
                {
                    isSubmerged = true;
                    if (rb != null) rb.linearDamping = waterDrag;
                }
                EndFightJump();
            }
        }

        if (isInWater)
        {
            ApplyBuoyancy();
            if (isStruggling)
            {
                UpdateStruggleMovement();
            }
        }
        else if (isInFlight)
        {
            // Extra gravity makes the cast arc snappy instead of moon-floaty. Only applied during
            // active flight — the bobber is always dynamic now (joint-tethered), so we must NOT
            // apply this while it dangles from the rod or it would yank against the joint.
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
        if (other.CompareTag(waterTag))
        {
            waterVolumes.Add(other);
            waterSurfaceY = HighestWaterSurfaceY();
            if (!isInWater)
            {
                EnterWater();
            }
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag(waterTag) && isInWater)
        {
            // Adding here too heals any Enter event missed while crossing a zone seam.
            waterVolumes.Add(other);
            waterSurfaceY = HighestWaterSurfaceY();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(waterTag)) return;

        waterVolumes.Remove(other);
        if (!isInWater) return;

        // Enter/Exit ordering at the seam between two overlapping water volumes is not
        // guaranteed, so when the bookkeeping says we left the last volume, physically probe
        // before sinking — the bobber may still be inside a neighboring volume.
        waterVolumes.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
        if (waterVolumes.Count == 0) ProbeForWaterVolumes();
        if (waterVolumes.Count > 0)
        {
            waterSurfaceY = HighestWaterSurfaceY();
            return;
        }

        isInWater = false;
        isSubmerged = false;
        SetStruggleActive(false);
        // Keep the tether anchor while a fish is on the line: a fight jump exits the water for
        // a moment, and losing the anchor here would permanently disable the tether/pull-away
        // tug-of-war for the rest of the fight. (The fight loop re-enables struggling each
        // frame; the anchor is the only state that wouldn't come back on its own.)
        if (hookedFish == null) hasStruggleAnchor = false;

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

    private float HighestWaterSurfaceY()
    {
        // Overlapping volumes can sit at slightly different heights; the bobber floats on the
        // highest surface it is currently touching.
        float top = float.MinValue;
        foreach (Collider c in waterVolumes)
        {
            if (c == null) continue;
            top = Mathf.Max(top, c.bounds.max.y);
        }
        return top > float.MinValue ? top : waterSurfaceY;
    }

    private void ProbeForWaterVolumes()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, 0.1f, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].CompareTag(waterTag))
                waterVolumes.Add(hits[i]);
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
        // solve in cleaner issue should not be allowed to enter water while reeling after canceling fishing.
        if (FindFirstObjectByType<FishingRodController>().currentState == FishingRodController.FishingState.Reeling)
            return;
        isInWater = true;
        isInFlight = false;

        // UNITY 6 FIX: Apply Water Drag
        if (rb != null)
        {
            rb.linearDamping = waterDrag;
            rb.angularDamping = waterAngularDamping;

            // Absorb the cast's plunge so the lightly-damped buoyancy spring doesn't catapult
            // the bobber back up. Only the downward component is killed; preserve any upward
            // motion (unlikely on entry but harmless) and horizontal glide.
            Vector3 v = rb.linearVelocity;
            if (v.y < 0f) v.y *= Mathf.Clamp01(waterImpactVelocityRetention);
            rb.linearVelocity = v;
            // Also dump residual spin from the air tumble so the bobber doesn't roll on landing.
            rb.angularVelocity *= 0.25f;

            // Lure-only: override the auto-computed inertia tensor so off-center pulls at the
            // LineAttachPoint produce a controlled rotation rather than spinning the lure wildly.
            if (waterInertiaTensorOverride > 0f)
            {
                rb.inertiaTensor = Vector3.one * waterInertiaTensorOverride;
            }
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

        if (hookedFish != null)
        {
            // Splash-down from a fight jump: dive back to the fight depth with its own splash
            // and sound, but do NOT re-announce the landing — FishingZone's bite brain would
            // start a second bite cycle on a bobber that already has a fish on the line.
            isSubmerged = true;
            SpawnEffect(impactPrefab, impactLifetime);
            if (audioSource != null && waterEntrySound != null)
            {
                audioSource.PlayOneShot(waterEntrySound, waterEntryVolumeScale);
            }
            EndFightJump();
            return;
        }

        Debug.LogWarning("dobber landed in water calling event");

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
        // Bob offset added to the rest float height drives the visual bounce while a lure is
        // being nudged — buoyancy follows the moving target naturally.
        float restFloatHeight = floatHeight + (isSubmerged ? 0f : lureBobOffset);
        float effectiveFloatHeight = isSubmerged ? biteSubmergeDepth : restFloatHeight;
        float targetY = waterSurfaceY - effectiveFloatHeight;
        float depth = targetY - transform.position.y;

        if (depth > 0)
        {
            // UNITY 6 FIX: Use 'linearVelocity' instead of 'velocity'
            Vector3 force = Vector3.up * (depth * buoyancyForce - rb.linearVelocity.y * bounceDamp);
            rb.AddForce(force, ForceMode.Acceleration);
        }

        // Pitch X is driven by lureVisualPitchDeg (0 = level, positive = LineAttachPoint side
        // rises). Heading Y is preserved; roll Z is held at 0.
        Quaternion targetRotation = Quaternion.Euler(lureVisualPitchDeg, rb.rotation.eulerAngles.y, 0);
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
            TryFightJump();
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

            // The fish takes line: a steady pull straight away from the player, with the
            // tether anchor dragged along so the gained distance sticks instead of
            // springing back to the bite point. The pull is capped by how far the anchor
            // has progressed away from the original bite spot (measured along the current
            // away direction, so line the player reels back can be taken again — a real
            // tug-of-war), and stops against obstacles and the fishing line's own tether.
            float pulledAway = Vector3.Dot(struggleAnchor - initialStruggleAnchor, toBobber);
            if (strugglePullAwayForce > 0f && pulledAway < maxPullAwayDistance)
            {
                rb.AddForce(toBobber * strugglePullAwayForce, ForceMode.Acceleration);

                if (hasStruggleAnchor && pullAwayAnchorSpeed > 0f)
                {
                    bool blocked = obstacleCheckLayers != 0
                        && Physics.Raycast(transform.position + Vector3.up * 0.5f, toBobber,
                                           obstacleCheckDistance, obstacleCheckLayers);
                    if (!blocked)
                        struggleAnchor += toBobber * (pullAwayAnchorSpeed * Time.fixedDeltaTime);
                }
            }

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

    // Rolled once per struggle burst: occasionally the hooked fish leaps clean out of the
    // water. Only the heavy in-water damping comes off so the launch velocity carries up
    // cleanly — buoyancy is deliberately left targeting the bite depth (NOT the surface), so
    // it adds no upward boost and the breach height is governed solely by jumpUpSpeed. (Aiming
    // buoyancy at the surface here was what sent the fish way too high.) The trigger exit/enter
    // pair handles the splash, and OnHookedFishJumpChanged lets the rope go slack mid-air.
    private void TryFightJump()
    {
        if (jumpActive || fightJumpChance <= 0f || hookedFish == null) return;
        if (rb == null || rb.isKinematic) return;
        if (Time.time < nextJumpAllowedTime) return;
        if (Random.value > fightJumpChance) return;

        jumpActive = true;
        jumpStartTime = Time.time;
        nextJumpAllowedTime = Time.time + jumpCooldown;

        rb.linearDamping = initialLinearDamping;

        Vector3 horizontal = struggleDirection * jumpForwardSpeed;
        rb.linearVelocity = new Vector3(horizontal.x, jumpUpSpeed, horizontal.z);

        SpawnEffect(strugglePrefab, struggleLifetime);
        FishingEvents.OnHookedFishJumpChanged?.Invoke(true);
    }

    private void EndFightJump()
    {
        if (!jumpActive) return;
        jumpActive = false;
        FishingEvents.OnHookedFishJumpChanged?.Invoke(false);
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

        if (BaitInventory.Instance != null)
        {
            BaitItem equipped = BaitInventory.Instance.SelectedBait;
            if (equipped != null)
            {
                if (!BaitInventory.Instance.TryConsume(equipped, 1))
                {
                    Debug.LogWarning($"[BobberController] Tried to consume {equipped.displayName} but the player had none.");
                }
            }
        }

        hookedFish = new CaughtFish(fishPreset);
        Debug.Log($"{hookedFish.GetDisplayName()} is on the line!");

        // From the bite onward the line holds a fish, not a float: the bobber/lure stops
        // rendering and the hooked silhouette (chain + sway) thrashes at the hook point.
        AttachHookedFishVisual();

        FishingEvents.OnFishBite?.Invoke(this);

        struggleAnchor = transform.position;
        initialStruggleAnchor = struggleAnchor;
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
        if (hookedFish == null) return;

        // The hooked visual normally attaches at the bite (HookFish); this is a safety net
        // for any path that reaches the reel-in without it, and keeps the OnFishHooked
        // event timing unchanged for listeners.
        AttachHookedFishVisual();
        FishingEvents.OnFishHooked?.Invoke(hookedFish);
    }

    // The fish got away (missed hook window or lost fight): hand the hooked visual its
    // freedom so it swims off and fades instead of vanishing with the destroyed bobber.
    public void ReleaseHookedFishToEscape()
    {
        if (activeFishModel == null) return;
        HookedFishController hooked = activeFishModel.GetComponent<HookedFishController>();
        if (hooked != null) hooked.BeginEscape();
        activeFishModel = null;
        if (bobberVisuals != null) bobberVisuals.SetActive(true);
    }

    // Hides the bobber/lure visuals and spawns the chain+sway hooked fish with its mouth
    // pinned to the hook point. Registered as activeFishModel so the existing reset path
    // (restore visuals, destroy model) cleans it up on catch, escape and cancel alike.
    private void AttachHookedFishVisual()
    {
        if (activeFishModel != null || hookedFish == null) return;

        if (bobberVisuals != null) bobberVisuals.SetActive(false);

        Material mat = hookedFishSilhouetteMaterial;
        if (mat == null)
        {
            if (sharedHookedSilhouette == null)
            {
                Shader swayShader = Shader.Find("FishOWisp/Fish Silhouette Sway");
                if (swayShader != null)
                    sharedHookedSilhouette = new Material(swayShader) { name = "HookedFishSilhouette (runtime)" };
            }
            mat = sharedHookedSilhouette;
        }

        activeFishModel = HookedFishController.Attach(this, hookedFish.preset, mat).gameObject;
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

    public void ResetForCast()
    {
        // FishingRodController disables this component during its reel-in arc. Re-enable so FixedUpdate
        // (extra gravity, buoyancy) runs again on the next cast.
        enabled = true;

        CancelNibbleSequence();
        StopAllCoroutines();

        // If the fight ended mid-leap, tell the rope the fish is down so it doesn't stay slack.
        EndFightJump();

        isInWater = false;
        waterVolumes.Clear();
        isInFlight = false;
        isSubmerged = false;
        hasSplashed = false;
        hasPlayedSplashSound = false;
        hasPlayedImpactSound = false;
        isStruggling = false;
        hasStruggleAnchor = false;
        hookedFish = null;
        isGrabTowed = false;
        lureVisualPitchDeg = 0f;
        lureBobOffset = 0f;

        if (activeFishModel != null)
        {
            Destroy(activeFishModel);
            activeFishModel = null;
        }
        if (bobberVisuals != null) bobberVisuals.SetActive(true);

        if (activeWakeInstance != null)
        {
            Destroy(activeWakeInstance);
            activeWakeInstance = null;
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.linearDamping = initialLinearDamping;
            rb.angularDamping = initialAngularDamping;
        }
    }

    public void ApplyAirTumbleTorque()
    {
        if (rb != null && !rb.isKinematic)
        {
            rb.AddTorque(Random.insideUnitSphere * airTumbleTorque, ForceMode.Impulse);
        }
    }

    public void BeginFlight()
    {
        isInFlight = true;
    }

    void OnDestroy()
    {
        StopAllCoroutines();
        if (activeWakeInstance != null) Destroy(activeWakeInstance);
    }
}