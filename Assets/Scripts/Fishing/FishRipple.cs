using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FishRipple : MonoBehaviour
{
    public enum FishState { Wandering, Attracted, Scared, Nibbling, Striking, Grabbing, Despawning, Hunting }

    [Header("Movement")]
    public float swimSpeed = 1.5f;
    public float attractSpeed = 1.2f;
    public float scareSpeed = 5f;
    public float wanderRadius = 3f;
    [Tooltip("Slow forward drift (m/s) used instead of a dead stop whenever the fish 'pauses' — wander rests, post-scare recovery, approach hesitations. Fish never stand fully still.")]
    public float pauseDriftSpeed = 0.2f;

    [Header("Turning")]
    [Tooltip("Fish always swim along their own facing, so heading turn rate (deg/sec) sets the arc they carve. Lower = wider, lazier wandering curves.")]
    public float wanderTurnRate = 70f;
    [Tooltip("Heading turn rate (deg/sec) while approaching the bobber — agile enough to weave but still curved.")]
    public float attractTurnRate = 160f;
    [Tooltip("Heading turn rate (deg/sec) during a committed lure strike. High = near-straight charge.")]
    public float strikeTurnRate = 400f;
    [Tooltip("Heading turn rate (deg/sec) while fleeing. Very high = sharp panic darts.")]
    public float scareTurnRate = 720f;

    [Header("Body Chain")]
    [Tooltip("How quickly the trailing body relaxes straight behind the head, per second. Lower = the body holds its curves longer and follows the head more lazily (floppier); higher = stiffer fish. Tunable live in play mode.")]
    public float bodyStraightenRate = 2.5f;

    [Header("Wander Weave")]
    [Tooltip("How far wandering fish bow side to side while cruising, in meters — lazy S-curves instead of straight beelines. 0 disables it.")]
    public float wanderWeaveAmplitude = 0.6f;
    [Tooltip("Weave cycles per second. Keep low (0.2–0.5) for natural meandering.")]
    public float wanderWeaveFrequency = 0.35f;

    [Header("Schooling")]
    [Tooltip("Personal space radius in meters: wandering fish steer away from schoolmates closer than this instead of phasing through them.")]
    public float separationRadius = 0.8f;
    [Tooltip("How strongly the wander path bends away from nearby fish.")]
    public float separationStrength = 1.2f;

    [Header("Predator Hunting")]
    [Tooltip("How far this predator spots prey (species listed in its FishPreset 'prey' list) before giving chase. Non-predators ignore all of these.")]
    public float huntRadius = 6f;
    [Tooltip("Swim speed while chasing prey — set above swimSpeed so a predator can actually run prey down.")]
    public float huntSpeed = 3f;
    [Tooltip("Heading turn rate (deg/sec) while chasing. Agile — sits between the lazy wander turn and a full strike.")]
    public float huntTurnRate = 220f;
    [Tooltip("Distance from the prey at which the lunge lands: the prey bolts directly away from this predator and the predator breaks off.")]
    public float huntCatchRange = 1.5f;
    [Tooltip("If a chased fish escapes beyond this distance the predator gives up. Keep above huntRadius so a fish that's just out of sight isn't dropped instantly.")]
    public float huntLeashRadius = 9f;
    [Tooltip("Seconds a predator rests (back to wandering) after scaring or losing prey before it can hunt again — paces the chase/scatter/regroup rhythm.")]
    public float huntCooldown = 3f;

    [Header("Natural Curiosity")]
    [Tooltip("How strongly the fish drifts toward the bobber while wandering (0 = none, 1 = strong).")]
    [Range(0f, 1f)]
    public float naturalAttraction = 0.05f;
    [Tooltip("Fish won't naturally drift closer than this distance to the bobber.")]
    public float naturalAttractionMinDist = 5f;

    [Header("Attracted Movement")]
    [Tooltip("How far the fish weaves side-to-side while approaching the bobber.")]
    public float weaveAmplitude = 2f;
    [Tooltip("How often the fish changes its weave direction.")]
    public float weaveIntervalMin = 0.5f;
    public float weaveIntervalMax = 1.5f;
    [Tooltip("Fish pauses briefly while approaching. Min seconds.")]
    public float attractPauseMin = 0.3f;
    [Tooltip("Fish pauses briefly while approaching. Max seconds.")]
    public float attractPauseMax = 1.2f;
    [Tooltip("Chance each weave cycle that the fish pauses.")]
    [Range(0f, 1f)]
    public float attractPauseChance = 0.3f;

    [Header("Commit Behavior")]
    [Tooltip("Distance at which the lead starts committing — weave amplitude and pause chance fade out and speed ramps up the closer it gets to the bobber. Must be larger than nibbleRange.")]
    public float commitDistance = 3f;
    [Tooltip("How much commit damps the weave amplitude near the bobber. 1 = weave shrinks to zero at nibbleRange, 0 = weave unchanged.")]
    [Range(0f, 1f)] public float commitWeaveDamping = 0.85f;
    [Tooltip("Bonus added to attractSpeed at full commit (dist == nibbleRange). 0.6 = 60% faster on final approach.")]
    public float commitSpeedBoost = 0.6f;
    [Tooltip("How far the fish drifts away when the lead is lost (caught, reeled, scared).")]
    public float loseInterestDriftDistance = 3f;

    [Header("Wake")]
    [Tooltip("Wake/trail prefab that follows the fish on the water surface.")]
    public GameObject wakePrefab;
    [Tooltip("Spawn the wake/trail. Off for now — the swimming silhouette makes it redundant; flip back on any time without losing the prefab reference.")]
    public bool spawnWake = false;

    [Header("Underwater Model")]
    [Tooltip("Clearance between the water line and the HIGHEST point of the species model. The model is auto-sunk from its renderer bounds, so no fish can poke above the surface regardless of prefab pivot or size.")]
    public float modelDepth = 0.35f;
    [Tooltip("Uniform scale applied to the spawned species model.")]
    public float modelScale = 1f;
    [Tooltip("Local rotation offset (euler) for the spawned model, for prefabs whose forward axis isn't +Z. The fish FBXs face -Z, hence the 180 default.")]
    public Vector3 modelRotationOffset = new Vector3(0f, 180f, 0f);
    [Tooltip("Material that blacks out the swimming model — fish in the water are always obscured. Use an UNLIT shader so lighting can't shade the silhouette. Leave empty for a runtime black URP/Unlit material.")]
    public Material silhouetteMaterial;

    [Header("Obstacle Avoidance")]
    [Tooltip("Layers that count as obstacles fish cannot swim through.")]
    public LayerMask obstacleLayers;
    [Tooltip("How high above the water surface to start the raycast from.")]
    public float obstacleRayHeight = 5f;

    [Header("Ranges")]
    [Tooltip("Distance at which the lead fish starts nibbling the bobber. Keep small so the fish has to almost touch the bobber to bite.")]
    public float nibbleRange = 0.5f;
    [Tooltip("If the player presses Attract while the fish is closer than this, the fish spooks. Keep ≥ nibbleRange.")]
    public float scareRange = 1.0f;
    [Tooltip("How far wandering fish stay from the bobber when another fish is being attracted.")]
    public float bobberAvoidRadius = 3f;
    [Tooltip("Distance at which a non-lead 'follower' fish hovers from the bobber. Should be slightly larger than nibbleRange so followers crowd in but never bite.")]
    public float followerHoverDistance = 1.4f;

    [Header("Awareness (Passive)")]
    [Tooltip("Fallback awareness radius — used only when the species preset's awarenessRadius is 0. The fish notices the bobber and auto-attracts (as a follower) whenever the bobber sits within the radius; drifts back to wandering once it leaves.")]
    public float actionRadius = 8f;

    [Header("Lure Strike (TP Dash & Grab)")]
    [Tooltip("Swim speed during a lure strike — the committed dash after the bite roll succeeds. TP charges at roughly double the approach speed.")]
    public float strikeSpeed = 3.5f;
    [Tooltip("3D distance from the lure (snout to lure) at which the dash connects and the fish clamps onto it (the grab). Stays small so the fish has to actually reach the lure.")]
    public float strikeLeapRange = 0.9f;
    [Tooltip("Horizontal distance from the lure at which the fish starts arcing UP toward it, so the head rises to meet the lure instead of staying at swim depth. The body trails the arc as a rope.")]
    public float strikeRiseDistance = 1.6f;
    [Tooltip("How fast (m/s) the head rises/sinks toward the lure's height during the dash and grab. High enough to complete the arc before contact.")]
    public float strikeRiseSpeed = 4f;

    [Header("Lure Grab (TP Bite Window)")]
    [Tooltip("Base seconds the fish keeps the lure in its mouth, swimming on in its dash direction, before it spits it — the player's reaction window. Scaled per species by size (bigger fish spit faster).")]
    public float grabHoldDuration = 0.6f;
    [Tooltip("Speed (m/s) the fish swims while carrying the lure during the grab. The lure is towed along to its mouth.")]
    public float grabForwardSpeed = 2.2f;
    [Tooltip("How much further below the grab height the fish drags the lure over the hold (TP fish pull the lure DOWN). 0 keeps it level.")]
    public float grabPullDownDepth = 0.25f;
    [Tooltip("After spitting the lure on a missed bite, the fish coasts this far along its dash heading — 'finishing the dash' — before normal wandering resumes.")]
    public float grabReleaseFollowThrough = 2.5f;
    [Tooltip("Seconds a fish ignores the lure after spitting it, so a missed bite doesn't instantly wheel back into another grab.")]
    public float grabReleaseAvoidTime = 2.5f;

    [Header("Aim Glimpse Indicator")]
    [Tooltip("Maximum distance from the camera at which the fish glimpse can appear while aiming. Set 0 to disable the distance check.")]
    public float aimRevealRange = 15f;
    [Tooltip("Minimum seconds between glimpses of this fish while aiming.")]
    public float glimpseIntervalMin = 1.5f;
    [Tooltip("Maximum seconds between glimpses of this fish while aiming.")]
    public float glimpseIntervalMax = 4f;
    [Tooltip("How long a single glimpse stays fully visible (seconds), not counting fades.")]
    public float glimpseDurationMin = 0.25f;
    [Tooltip("How long a single glimpse stays fully visible (seconds), not counting fades.")]
    public float glimpseDurationMax = 0.6f;
    [Tooltip("Seconds the glimpse takes to fade in.")]
    public float glimpseFadeIn = 0.35f;
    [Tooltip("Seconds the glimpse takes to fade out.")]
    public float glimpseFadeOut = 0.6f;
    [Tooltip("Debug: keep the bait indicator fully visible at all times, ignoring aim-mode gating and the glimpse fade cycle.")]
    public bool alwaysShowIndicator = false;
    [Tooltip("Master switch for the bait-preference image above the fish. Off for now; flip on to bring the aim glimpses back (alwaysShowIndicator still works on top of it).")]
    public bool enableBaitIndicator = false;

    [Header("Timing")]
    public float scareCooldown = 5f;
    public float wanderPauseMin = 0.5f;
    public float wanderPauseMax = 2f;

    [Header("Lifetime")]
    [Tooltip("Seconds this fish hangs around before it moves on: it swims off, dives and fades " +
             "out (the same getaway an escaped catch plays) and the zone spawns a replacement. " +
             "The clock pauses entirely while a bobber/lure is in this pool's water — fish " +
             "never leave during any stage of fishing — and expiry only fires from calm " +
             "wandering, never mid-flee. Set 0 to disable.")]
    public float lifetimeSeconds = 45f;

    [HideInInspector] public FishPreset preset;

    // Fired once when the lifetime expires and the swim-off begins. The zone uses it to drop
    // this fish from its roster (it's no longer catchable) and spawn the replacement.
    public event System.Action<FishRipple> OnDespawnStarted;

    public FishState CurrentState => currentState;
    public bool IsFollower => isFollower;
    // Seconds left in the current lure grab (the reaction window), 0 when not gripping.
    public float GrabHoldRemaining => currentState == FishState.Grabbing ? Mathf.Max(0f, grabTimer) : 0f;

    private FishState currentState = FishState.Wandering;
    private bool isFollower;
    private Collider zoneBounds;
    private float waterSurfaceY;
    private Transform bobberTransform;

    // Wandering
    private Vector3 wanderTarget;
    private bool hasWanderTarget;
    private float wanderPauseTimer;
    private float weavePhase;

    // Attracted weaving
    private Vector3 weaveOffset;
    private float weaveTimer;
    private float attractPauseTimer;

    // Scared
    private Vector3 scareDirection;
    private float scareTimer;

    // Lifetime despawn — mirrors HookedFishController's escape swim-off: burst away from the
    // player, diving and fading out, then despawn for good. A predator catching its prey routes
    // through the same exit, but faster and aimed away from the hunter (DespawnFleeSpeed).
    private const float DespawnDuration = 1.8f;
    private const float DespawnSpeed = 2.6f;
    private const float DespawnFleeSpeed = 4f;
    private const float DespawnDiveDepth = 0.35f;
    private float ageTimer;
    private float despawnTimer;
    private Vector3 despawnDirection;
    private float despawnSpeed;

    // Bobber avoidance
    private bool shouldAvoidBobber;

    // Predator hunting: the prey this fish is currently chasing, and a rest timer after a chase
    // ends so it doesn't instantly re-lock the fish it just scared off.
    private FishRipple preyTarget;
    private float huntCooldownTimer;

    // Lure brain: scales the passive awareness radius (1 = normal). The brain raises it while
    // the lure is twitching so movement draws fish from farther away, TP-style.
    private float awarenessScale = 1f;

    // TP dash & grab. The dash (Striking) charges the lure with a 3D thrash; on contact the fish
    // clamps onto the lure (Grabbing) and holds it for grabTimer seconds — the reaction window.
    // onGrabStart opens that window (zone → rod); onGrabReleased closes it if the hold lapses.
    // The zone calls ConfirmGrab() when the player reacts in time. No HookFish happens until then,
    // so a missed bite just releases the real fish — it never disappears or fades.
    private System.Action<FishRipple> onGrabStartCallback;
    private System.Action<FishRipple> onGrabReleasedCallback;
    private Vector3 dashDirection = Vector3.forward;
    private float grabTimer;
    // The head's height during a strike/grab is integrated separately (SteerToward re-pins y to
    // the surface each frame), so the fish can arc up to the lure and dip while gripping.
    private float strikeHeadY;
    private float grabStartHeadY;
    // Cached BobberController for the lure being tracked, so a grabbing fish can tow it.
    private BobberController bobberCtrl;
    // After spitting the lure, the fish ignores it for this long so the missed bite coasts through
    // its follow-through instead of instantly wheeling back. Only gates re-attraction, not steering,
    // so the coast actually carries past the lure rather than veering away from it.
    private float reengageCooldown;

    // Wake
    private GameObject activeWakeInstance;

    // Anti-stuck rescue: wandering fish that make no progress for a while (spawned against
    // or nudged into level geometry the obstacle mask can't see) get relocated.
    private Vector3 stuckAnchor;
    private float stuckTimer;

    // Shared reference to the zone's live fish list, for separation steering.
    private List<FishRipple> school;

    public void SetSchool(List<FishRipple> school)
    {
        this.school = school;
    }

    private readonly FishGlimpseIndicator glimpse = new FishGlimpseIndicator();
    private FishNibbleBehavior nibble;
    private FishModelVisual modelVisual;

    public void Initialize(Collider bounds, float surfaceY, GameObject aimIndicatorPrefab)
    {
        zoneBounds = bounds;
        waterSurfaceY = surfaceY;

        // The ripple prefab root is pitched -90° so its surface quad lies flat, but the fish
        // host must be level: the model spawn, sink-below-water measurement and the yaw-based
        // steering/body-bend all assume a yaw-only transform. Random yaw for spawn variety.
        transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        weavePhase = Random.Range(0f, Mathf.PI * 2f);

        if (nibble == null) nibble = new FishNibbleBehavior(transform);

        // Place at a random point within the zone
        Vector3 startPos = FishMovementHelpers.GetRandomPointInBounds(zoneBounds, transform.position, waterSurfaceY, obstacleLayers, obstacleRayHeight);
        startPos.y = waterSurfaceY;
        transform.position = startPos;
        stuckAnchor = startPos;
        stuckTimer = 0f;

        glimpse.Initialize(aimIndicatorPrefab, transform, preset);

        // Spawn the species model under the ripple so the fish has a visible body.
        if (modelVisual == null) modelVisual = new FishModelVisual(transform);
        modelVisual.Spawn(preset, modelDepth, modelScale, modelRotationOffset, silhouetteMaterial);

        // Spawn wake
        if (spawnWake && wakePrefab != null)
        {
            Vector3 wakePos = new Vector3(startPos.x, waterSurfaceY, startPos.z);
            activeWakeInstance = Instantiate(wakePrefab, wakePos, wakePrefab.transform.rotation);
        }

        PickNewWanderTarget();
    }

    public void SetWaterSurfaceY(float y)
    {
        waterSurfaceY = y;
        nibble?.SetWaterSurfaceY(y);
    }

    public void SetAvoidBobber(bool avoid)
    {
        shouldAvoidBobber = avoid;
    }

    public void SetBobberTransform(Transform bobber)
    {
        bobberTransform = bobber;
        bobberCtrl = bobber != null ? bobber.GetComponent<BobberController>() : null;
    }

    public void ClearBobberTransform()
    {
        bobberTransform = null;
        bobberCtrl = null;
        isFollower = false;
        if (currentState == FishState.Attracted)
        {
            currentState = FishState.Wandering;
            PickNewWanderTarget();
        }
    }

    public void AttractToBobber()
    {
        if (currentState == FishState.Scared || currentState == FishState.Nibbling
            || currentState == FishState.Despawning) return;
        if (bobberTransform == null) return;

        float dist = GetHorizontalDistance(transform.position, bobberTransform.position);

        if (dist < scareRange)
        {
            Scare();
            return;
        }

        currentState = FishState.Attracted;
        weaveTimer = 0f;
        weaveOffset = Vector3.zero;
        attractPauseTimer = 0f;
    }

    public void SetFollower(bool follower)
    {
        isFollower = follower;
    }

    public void SetAwarenessScale(float scale)
    {
        awarenessScale = Mathf.Max(0.01f, scale);
    }

    // Species-specific sight range wins; the prefab's actionRadius is the fallback. The lure
    // brain scales the result up while the lure is moving.
    private float BaseAwarenessRadius =>
        preset != null && preset.awarenessRadius > 0f ? preset.awarenessRadius : actionRadius;
    private float EffectiveActionRadius => BaseAwarenessRadius * awarenessScale;

    // Lure-brain strike: the fish has won its bite roll and commits — a fast 3D dash at the lure,
    // no weaving. onGrabStart fires when the dash connects and the fish clamps onto the lure (the
    // reaction window opens); onGrabReleased fires if the grip lapses with no player response (the
    // fish spits the lure and swims off). No bite is committed until the zone calls ConfirmGrab().
    public void StartLureStrike(System.Action<FishRipple> onGrabStart, System.Action<FishRipple> onGrabReleased)
    {
        if (currentState == FishState.Scared || currentState == FishState.Nibbling
            || currentState == FishState.Despawning) return;
        if (bobberTransform == null) return;

        onGrabStartCallback = onGrabStart;
        onGrabReleasedCallback = onGrabReleased;
        isFollower = false;
        dashDirection = GetFlatForward();
        strikeHeadY = transform.position.y; // start at current swim depth; the dash arcs it up
        currentState = FishState.Striking;
    }

    // The dash connected: clamp onto the lure and start the hold/reaction window. The fish now
    // carries the lure (tows it to its mouth) and keeps swimming on in its dash heading. The zone
    // is told via onGrabStart so it can open the rod's window and freeze further bite rolls.
    private void BeginGrab()
    {
        dashDirection = GetFlatForward();
        grabStartHeadY = strikeHeadY;
        SizeClass size = preset != null ? preset.sizeClass : SizeClass.Medium;
        grabTimer = Mathf.Max(0.05f, grabHoldDuration * LureBiteBrain.ReactionWindowMultiplier(size));
        currentState = FishState.Grabbing;
        bobberCtrl?.BeginGrabTow();
        onGrabStartCallback?.Invoke(this);
    }

    // The hold lapsed with no response: spit the lure (hand it back to physics) and coast on along
    // the dash heading before normal wandering resumes — the fish "finishes its dash" instead of
    // teleporting away.
    private void ReleaseGrab()
    {
        var notify = onGrabReleasedCallback;
        onGrabStartCallback = null;
        onGrabReleasedCallback = null;
        bobberCtrl?.EndGrabTow();

        currentState = FishState.Wandering;
        Vector3 ahead = GetHeadPosition() + dashDirection * grabReleaseFollowThrough;
        ahead.y = waterSurfaceY;
        wanderTarget = ClampToBounds(ahead);
        hasWanderTarget = true;
        reengageCooldown = grabReleaseAvoidTime;

        notify?.Invoke(this);
    }

    // The player reacted in time. Returns true while the fish is still gripping (the zone then
    // turns this fish into the hooked fish); false if the grab already lapsed this frame. The tow
    // ends here so BobberController.HookFish takes the lure over cleanly.
    public bool ConfirmGrab()
    {
        if (currentState != FishState.Grabbing) return false;
        bobberCtrl?.EndGrabTow();
        grabTimer = 0f;
        onGrabStartCallback = null;
        onGrabReleasedCallback = null;
        return true;
    }

    public void CancelLureStrike()
    {
        if (currentState != FishState.Striking && currentState != FishState.Grabbing) return;
        if (currentState == FishState.Grabbing) onGrabReleasedCallback?.Invoke(this);
        onGrabStartCallback = null;
        onGrabReleasedCallback = null;
        bobberCtrl?.EndGrabTow();
        currentState = FishState.Wandering;
        PickNewWanderTarget();
    }

    public void StopFollowing()
    {
        if (!isFollower) return;
        isFollower = false;

        if (currentState == FishState.Attracted)
        {
            currentState = FishState.Wandering;
            // Drift to a wander target away from the bobber so the school visually scatters.
            if (bobberTransform != null)
            {
                Vector3 awayDir = transform.position - bobberTransform.position;
                awayDir.y = 0f;
                if (awayDir.sqrMagnitude < 0.01f)
                {
                    Vector2 rand = Random.insideUnitCircle.normalized;
                    awayDir = new Vector3(rand.x, 0f, rand.y);
                }
                else
                {
                    awayDir.Normalize();
                }
                wanderTarget = transform.position + awayDir * loseInterestDriftDistance;
                wanderTarget.y = waterSurfaceY;
                wanderTarget = ClampToBounds(wanderTarget);
                hasWanderTarget = true;
            }
            else
            {
                PickNewWanderTarget();
            }
        }
    }

    public void Scare()
    {
        // A despawning fish is already on its way out — nothing left to spook.
        if (currentState == FishState.Despawning) return;

        // Cancel the bobber's nibble sequence if this fish was nibbling
        if (currentState == FishState.Nibbling && bobberTransform != null)
        {
            BobberController bobber = bobberTransform.GetComponent<BobberController>();
            if (bobber != null) bobber.CancelNibbleSequence();
            FishingEvents.OnFishBite -= OnFishBiteHide;
            nibble?.Stop();
        }

        Vector3 awayDir;
        if (bobberTransform != null)
        {
            awayDir = (transform.position - bobberTransform.position);
            awayDir.y = 0;
            awayDir.Normalize();
        }
        else
        {
            Vector2 rand = Random.insideUnitCircle.normalized;
            awayDir = new Vector3(rand.x, 0, rand.y);
        }

        scareDirection = awayDir;
        scareTimer = scareCooldown;
        scareJinkTimer = 0f;
        isFollower = false;
        // Spooked mid-grab: hand the lure back and close the reaction window before fleeing so
        // the zone/rod don't hang.
        if (currentState == FishState.Grabbing) onGrabReleasedCallback?.Invoke(this);
        onGrabStartCallback = null;
        onGrabReleasedCallback = null;
        bobberCtrl?.EndGrabTow();
        currentState = FishState.Scared;
        FishingEvents.OnFishScared?.Invoke();
    }

    private void OnEnable()
    {
        FishingEvents.OnStartAiming += ShowIndicator;
        FishingEvents.OnStopAiming += HideIndicator;
    }

    private void OnDisable()
    {
        FishingEvents.OnStartAiming -= ShowIndicator;
        FishingEvents.OnStopAiming -= HideIndicator;
        FishingEvents.OnFishBite -= OnFishBiteHide;
        nibble?.UnsubscribeAll();
    }

    private void ShowIndicator()
    {
        if (enableBaitIndicator) glimpse.ShowOnAim(GetGlimpseSettings());
    }
    private void HideIndicator() => glimpse.HideOnAim();

    private FishGlimpseIndicator.Settings GetGlimpseSettings()
    {
        return new FishGlimpseIndicator.Settings
        {
            alwaysShowIndicator = alwaysShowIndicator,
            aimRevealRange = aimRevealRange,
            glimpseIntervalMin = glimpseIntervalMin,
            glimpseIntervalMax = glimpseIntervalMax,
            glimpseDurationMin = glimpseDurationMin,
            glimpseDurationMax = glimpseDurationMax,
            glimpseFadeIn = glimpseFadeIn,
            glimpseFadeOut = glimpseFadeOut,
        };
    }

    void Update()
    {
        // Keep on water surface — unless dashing, gripping the lure or despawning, where the
        // strike thrash / grab dip / dive-away own the height.
        Vector3 pos = transform.position;
        bool ownsHeight = currentState == FishState.Striking
                          || currentState == FishState.Grabbing
                          || currentState == FishState.Despawning;
        if (!ownsHeight) pos.y = waterSurfaceY;

        // Keep wake following the fish
        if (activeWakeInstance != null)
        {
            activeWakeInstance.transform.position = new Vector3(pos.x, waterSurfaceY, pos.z);
        }
        transform.position = pos;

        if (enableBaitIndicator) glimpse.Tick(transform.position, GetGlimpseSettings());

        // Lifetime: the clock only runs while the player ISN'T fishing this pool. The moment
        // a bobber/lure is in the water here (bobberTransform is set on every fish in the
        // zone), aging pauses — so no fish slips away mid-cast, mid-attract, mid-nibble or
        // mid-fight, and there's no mass exodus of over-aged fish when the bobber leaves.
        // Expiry itself additionally only fires from calm wandering, never mid-flee.
        if (lifetimeSeconds > 0f && currentState != FishState.Despawning && bobberTransform == null)
        {
            ageTimer += Time.deltaTime;
            if (ageTimer >= lifetimeSeconds && currentState == FishState.Wandering)
                BeginDespawn();
        }

        // Cool-down after spitting a lure on a missed bite: ignore the lure briefly so the fish
        // coasts through its follow-through instead of immediately wheeling into another grab.
        if (reengageCooldown > 0f) reengageCooldown -= Time.deltaTime;

        // Rest between hunts so a predator doesn't re-lock the prey it just scared the same frame.
        if (huntCooldownTimer > 0f) huntCooldownTimer -= Time.deltaTime;

        switch (currentState)
        {
            case FishState.Wandering:
                UpdateWandering();
                break;
            case FishState.Attracted:
                UpdateAttracted();
                break;
            case FishState.Scared:
                UpdateScared();
                break;
            case FishState.Nibbling:
                nibble?.Tick(scareSpeed);
                break;
            case FishState.Striking:
                UpdateStriking();
                break;
            case FishState.Grabbing:
                UpdateGrabbing();
                break;
            case FishState.Despawning:
                UpdateDespawning();
                break;
            case FishState.Hunting:
                UpdateHunting();
                break;
        }

        UpdateStuckRescue();

        // Drive the procedural sway: agitated states beat the tail faster, which doubles as
        // a readable telegraph (a striking fish visibly thrashes toward the lure). The body is a
        // true 3D rope, so it trails the head's arc on its own when the fish rises to the lure.
        modelVisual?.Tick(Time.deltaTime, GetSwayIntensity(), bodyStraightenRate);
    }

    // Wandering fish that haven't moved meaningfully for several seconds are wedged in
    // geometry (spawned inside a rock, or terrain that isn't on the obstacle mask): swimming
    // can't free them because every heading is blocked, so relocate to a fresh valid point.
    // The window comfortably exceeds the longest legitimate wander pause.
    private void UpdateStuckRescue()
    {
        if (currentState != FishState.Wandering)
        {
            stuckTimer = 0f;
            stuckAnchor = transform.position;
            return;
        }

        if (FishMovementHelpers.GetHorizontalDistance(transform.position, stuckAnchor) > 0.15f)
        {
            stuckTimer = 0f;
            stuckAnchor = transform.position;
            return;
        }

        stuckTimer += Time.deltaTime;
        if (stuckTimer < 4f) return;
        stuckTimer = 0f;

        // Only a fish that's genuinely wedged (no clear water around it) gets relocated.
        // A merely indecisive fish picks a fresh target instead of popping across the pond.
        if (FishMovementHelpers.HasClearanceAt(transform.position, 0.4f, obstacleLayers, waterSurfaceY, obstacleRayHeight))
        {
            PickNewWanderTarget();
            return;
        }

        Vector3 fresh = FishMovementHelpers.GetRandomPointInBounds(zoneBounds, transform.position, waterSurfaceY, obstacleLayers, obstacleRayHeight);
        fresh.y = waterSurfaceY;
        transform.position = fresh;
        stuckAnchor = fresh;
        PickNewWanderTarget();
    }

    private float GetSwayIntensity()
    {
        switch (currentState)
        {
            case FishState.Attracted:  return 1.5f;
            case FishState.Nibbling:   return 0.6f;
            case FishState.Scared:     return 2.5f;
            case FishState.Hunting:    return 2.2f;  // aggressive pursuit, just short of a lure strike
            case FishState.Striking:   return 2.8f;
            case FishState.Grabbing:   return 3f;   // hardest thrash — the fish is wrestling the lure
            case FishState.Despawning: return 2.6f; // same urgent tail-beat as the escape swim-off
            default:                   return 1f;
        }
    }

    // The dash: a fast, committed charge at the lure. As it closes it arcs UP toward the lure's
    // real height so the head rises to meet it (the body trails the arc as a 3D rope). On contact
    // (snout within reach of the lure) the fish clamps on and the grab begins.
    private void UpdateStriking()
    {
        if (bobberTransform == null)
        {
            CancelLureStrike();
            return;
        }

        Vector3 lure = bobberTransform.position;            // real 3D lure position
        Vector3 lureFlat = new Vector3(lure.x, waterSurfaceY, lure.z);

        // Contact is measured snout-to-lure in full 3D: the fish must actually rise to the lure,
        // not just be horizontally under it.
        Vector3 snout = modelVisual != null ? modelVisual.MouthWorldPosition : GetHeadPosition();
        if (Vector3.Distance(snout, lure) < Mathf.Max(strikeLeapRange, nibbleRange))
        {
            BeginGrab();
            return;
        }

        // Horizontal chase toward the lure.
        SteerToward(lureFlat, strikeSpeed, strikeTurnRate);
        dashDirection = GetFlatForward();

        // Vertical arc: once within strikeRiseDistance, climb toward the height that places the
        // snout on the lure; beyond that, stay at swim depth. The closer it gets, the higher it
        // rises — a smooth upward arc into the lure.
        float horiz = GetHorizontalDistance(GetHeadPosition(), lureFlat);
        float targetY = horiz <= strikeRiseDistance ? HostYToPlaceSnoutAt(lure.y) : waterSurfaceY;
        strikeHeadY = Mathf.MoveTowards(strikeHeadY, targetY, strikeRiseSpeed * Time.deltaTime);
        ApplyStrikeHeadY();
    }

    // The grab: the fish has the lure in its mouth and keeps swimming on in its dash heading,
    // towing the lure along (and dragging it a little deeper over the hold). When the hold lapses
    // it spits the lure and coasts away; ConfirmGrab() (player reacted) ends it as a real bite.
    private void UpdateGrabbing()
    {
        if (bobberTransform == null)
        {
            ReleaseGrab();
            return;
        }

        grabTimer -= Time.deltaTime;

        // Keep swimming forward along the dash heading, carrying the lure.
        Vector3 step = dashDirection * (grabForwardSpeed * Time.deltaTime);
        Vector3 newPos = ClampToBounds(transform.position + step);
        transform.position = newPos;
        transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(dashDirection.x, dashDirection.z) * Mathf.Rad2Deg, 0f);

        // Drag the lure down a touch as the hold runs out (TP fish pull the lure under).
        float held = grabHoldDuration > 0.001f ? 1f - Mathf.Clamp01(grabTimer / grabHoldDuration) : 1f;
        strikeHeadY = Mathf.MoveTowards(strikeHeadY, grabStartHeadY - grabPullDownDepth * held,
                                        strikeRiseSpeed * Time.deltaTime);
        ApplyStrikeHeadY();

        // Tow the lure to the fish's mouth so it travels with the fish.
        if (bobberCtrl != null && modelVisual != null)
            bobberCtrl.SetGrabTowTarget(modelVisual.MouthWorldPosition);

        if (grabTimer <= 0f) ReleaseGrab();
    }

    // Applies the separately-integrated strike head height (SteerToward re-pins y to the surface,
    // so this runs last and owns the height during the dash/grab).
    private void ApplyStrikeHeadY()
    {
        Vector3 p = transform.position;
        p.y = strikeHeadY;
        transform.position = p;
    }

    // The host Y that places the rendered snout at worldY. The model is sunk below the host, so
    // we read the live host→snout vertical gap and offset by it (adapts to any species/depth).
    private float HostYToPlaceSnoutAt(float worldY)
    {
        float snoutY = modelVisual != null ? modelVisual.MouthWorldPosition.y : transform.position.y;
        float hostAboveSnout = transform.position.y - snoutY;
        return worldY + hostAboveSnout;
    }

    private void UpdateWandering()
    {
        // Passive auto-attract: if the bobber is sitting inside this fish's awareness radius
        // (and we're not being told to give the lead fish space), wake up and approach as a follower.
        // Bait mismatch is *not* gated here — wrong-species fish still gather visually so the pond
        // doesn't look empty. Only the bite/lead promotion in FishingZone is gated by bait.
        // Tackle mismatch IS gated: a lure-only species ignores a bait bobber entirely (and vice
        // versa) — it neither schools around it nor gets recruited by the lure brain.
        if (bobberTransform != null && !shouldAvoidBobber && reengageCooldown <= 0f && BaseAwarenessRadius > 0f
            && BobberInventory.PresetRespondsToEquippedTackle(preset))
        {
            float bobberDist = GetHorizontalDistance(transform.position, bobberTransform.position);
            if (bobberDist <= EffectiveActionRadius)
            {
                AttractToBobber();
                if (currentState == FishState.Attracted)
                {
                    isFollower = true;          // self-attracted fish never nibble until promoted by FishingZone
                    return;
                }
            }
        }

        // Predators give chase. Bobber interaction always wins — the auto-attract above already
        // returned if the tackle pulled this fish in — and a predator told to give the bobber
        // space (mid-catch) stays calm. Otherwise a wandering predator that spots prey in its
        // school locks on and hunts. This even interrupts a wander rest, so a resting pike pounces.
        if (huntCooldownTimer <= 0f && !shouldAvoidBobber
            && preset != null && preset.IsPredator && TryAcquirePrey())
        {
            currentState = FishState.Hunting;
            return;
        }

        if (wanderPauseTimer > 0f)
        {
            wanderPauseTimer -= Time.deltaTime;
            // Resting fish drift slowly instead of dead-stopping — always alive, never a
            // statue — and crowded resters gently spread apart.
            Vector3 driftTarget = GetHeadPosition() + GetFlatForward() * 2f
                                  + ComputeSeparation() * separationStrength;
            SteerToward(driftTarget, pauseDriftSpeed, wanderTurnRate * 0.5f);
            return;
        }

        if (!hasWanderTarget)
        {
            PickNewWanderTarget();
        }

        // Arrival and reachability are judged from the HEAD — the steered agent. Judging
        // from the centre while the head seeks the point lets a big fish circle its target
        // with the head on it and the centre forever outside the arrive radius.
        Vector3 headPos = GetHeadPosition();
        float dist = GetHorizontalDistance(headPos, wanderTarget);

        if (dist < 0.45f)
        {
            hasWanderTarget = false;
            wanderPauseTimer = Random.Range(wanderPauseMin, wanderPauseMax);
            return;
        }

        // A target inside the turning radius at a bad angle can only be circled — drop it
        // for a fresh one. Reads as the fish changing its mind rather than looping.
        Vector3 toWanderTarget = wanderTarget - headPos;
        toWanderTarget.y = 0f;
        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;
        float turnRadius = swimSpeed / Mathf.Max(wanderTurnRate * Mathf.Deg2Rad, 0.01f);
        if (dist < turnRadius && Vector3.Angle(flatForward, toWanderTarget) > 45f)
        {
            hasWanderTarget = false;
            return;
        }

        // If another fish is being attracted, steer away from the bobber
        if (shouldAvoidBobber && bobberTransform != null)
        {
            float bobberDist = GetHorizontalDistance(transform.position, bobberTransform.position);
            if (bobberDist < bobberAvoidRadius)
            {
                Vector3 awayFromBobber = (transform.position - bobberTransform.position);
                awayFromBobber.y = 0;
                awayFromBobber.Normalize();
                SteerToward(transform.position + awayFromBobber * 2f, swimSpeed, attractTurnRate);
                hasWanderTarget = false;
                return;
            }
        }

        // Blend wander target toward bobber if nearby (natural curiosity) — skipped for fish
        // that don't respond to the equipped tackle; they shouldn't creep toward it either.
        Vector3 moveTarget = wanderTarget;
        if (!shouldAvoidBobber && bobberTransform != null && naturalAttraction > 0f
            && BobberInventory.PresetRespondsToEquippedTackle(preset))
        {
            float bobberDist = GetHorizontalDistance(transform.position, bobberTransform.position);
            if (bobberDist > naturalAttractionMinDist)
            {
                Vector3 bobberPos = bobberTransform.position;
                bobberPos.y = waterSurfaceY;
                moveTarget = Vector3.Lerp(wanderTarget, bobberPos, naturalAttraction);
            }
        }

        // Personal space: bend the path away from nearby schoolmates so fish don't phase
        // through each other while milling around.
        moveTarget += ComputeSeparation() * separationStrength;

        // Lazy S-curves: bow the path sideways with a slow per-fish sine so cruising reads
        // as weaving rather than a beeline. Fades out near the target so arrival stays clean.
        if (wanderWeaveAmplitude > 0f)
        {
            Vector3 toMove = moveTarget - headPos;
            toMove.y = 0f;
            if (toMove.sqrMagnitude > 0.01f)
            {
                Vector3 perp = Vector3.Cross(Vector3.up, toMove.normalized);
                float fade = Mathf.Clamp01(dist / 1.5f);
                float weave = Mathf.Sin(Time.time * wanderWeaveFrequency * Mathf.PI * 2f + weavePhase);
                moveTarget += perp * (weave * wanderWeaveAmplitude * fade);
            }
        }

        // Glide into the stop instead of swimming full tilt until the arrival check trips.
        float arriveGlide = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(dist / 0.8f));
        SteerToward(moveTarget, swimSpeed * arriveGlide, wanderTurnRate);
    }

    private void UpdateAttracted()
    {
        if (bobberTransform == null)
        {
            currentState = FishState.Wandering;
            PickNewWanderTarget();
            return;
        }

        Vector3 bobberPos = bobberTransform.position;
        bobberPos.y = waterSurfaceY;

        float dist = GetHorizontalDistance(transform.position, bobberPos);

        // Self-attracted followers drift back to wandering once the bobber leaves the awareness radius.
        if (BaseAwarenessRadius > 0f && isFollower && dist > EffectiveActionRadius)
        {
            StopFollowing();
            return;
        }

        // Head-based: the steering converges the head onto the bobber, so the bite trigger
        // must measure from there too — the centre trails half a body behind and may never
        // come within nibbleRange on bigger fish.
        if (!isFollower && GetHorizontalDistance(GetHeadPosition(), bobberPos) < nibbleRange)
        {
            StartNibbling();
            return;
        }

        // Commit ramp: as the lead closes on the bobber, weave/pauses fade out and speed ramps up,
        // so the approach reads as a deliberate closing rather than indecisive hovering. The fish
        // never gives up on its own — only player misplay (scare/reel) can break the approach.
        float commitSpan = Mathf.Max(0.01f, commitDistance - nibbleRange);
        float commitFactor = isFollower ? 0f : Mathf.Clamp01(1f - (dist - nibbleRange) / commitSpan);
        float effectiveWeaveAmp = weaveAmplitude * (1f - commitFactor * commitWeaveDamping);
        float effectivePauseChance = attractPauseChance * (1f - commitFactor);
        float effectiveSpeed = attractSpeed * (1f + commitFactor * commitSpeedBoost);

        if (attractPauseTimer > 0f)
        {
            attractPauseTimer -= Time.deltaTime;
            // Hesitating fish hover with a slow drift rather than freezing mid-water.
            SteerToward(GetHeadPosition() + GetFlatForward() * 2f, pauseDriftSpeed * 0.5f, attractTurnRate * 0.25f);
            return;
        }

        weaveTimer -= Time.deltaTime;
        if (weaveTimer <= 0f)
        {
            Vector3 toBobber = (bobberPos - transform.position);
            toBobber.y = 0;
            if (toBobber.sqrMagnitude > 0.01f)
            {
                Vector3 perp = Vector3.Cross(toBobber.normalized, Vector3.up);
                weaveOffset = perp * Random.Range(-effectiveWeaveAmp, effectiveWeaveAmp);
            }

            weaveTimer = Random.Range(weaveIntervalMin, weaveIntervalMax);

            if (Random.value < effectivePauseChance)
            {
                attractPauseTimer = Random.Range(attractPauseMin, attractPauseMax);
            }
        }

        // Followers hover at followerHoverDistance from the bobber rather than crowding the hook.
        Vector3 approachCenter = bobberPos;
        if (isFollower)
        {
            Vector3 fromBobber = transform.position - bobberPos;
            fromBobber.y = 0f;
            float fromBobberDist = fromBobber.magnitude;
            Vector3 outward = fromBobberDist > 0.01f
                ? fromBobber / fromBobberDist
                : new Vector3(Mathf.Cos(GetInstanceID()), 0f, Mathf.Sin(GetInstanceID()));
            approachCenter = bobberPos + outward * followerHoverDistance;
        }

        Vector3 targetPos = approachCenter + weaveOffset;
        targetPos.y = waterSurfaceY;

        SteerToward(targetPos, effectiveSpeed, attractTurnRate);
    }

    private float scareJinkTimer;

    private void UpdateScared()
    {
        scareTimer -= Time.deltaTime;

        float fleePhase = scareCooldown - 1.5f;
        if (scareTimer > fleePhase)
        {
            // Jink direction randomly every so often
            scareJinkTimer -= Time.deltaTime;
            if (scareJinkTimer <= 0f)
            {
                float jinkAngle = Random.Range(-60f, 60f);
                scareDirection = Quaternion.Euler(0, jinkAngle, 0) * scareDirection;
                scareDirection.y = 0;
                scareDirection.Normalize();
                scareJinkTimer = Random.Range(0.15f, 0.35f);
            }

            // Vary speed each frame for a panicked feel
            float burstSpeed = scareSpeed * Random.Range(0.6f, 1.3f);
            Vector3 probe = transform.position + scareDirection * (burstSpeed * Time.deltaTime);
            probe.y = waterSurfaceY;
            probe = ClampToBounds(probe);

            if (IsObstacleAt(probe))
            {
                scareDirection = Vector3.Cross(scareDirection, Vector3.up).normalized;
                if (scareDirection.sqrMagnitude < 0.01f)
                    scareDirection = -scareDirection;
                scareJinkTimer = 0f;
            }
            else
            {
                SteerToward(transform.position + scareDirection * 2f, burstSpeed, scareTurnRate);
            }
        }
        else if (scareTimer > 0f)
        {
            // Recovery tail: catch breath with a slow drift instead of freezing in place.
            SteerToward(GetHeadPosition() + GetFlatForward() * 2f, pauseDriftSpeed, wanderTurnRate);
        }

        if (scareTimer <= 0f)
        {
            currentState = FishState.Wandering;
            PickNewWanderTarget();
        }
    }

    // The chase: a predator charges the prey it locked onto. When it closes within huntCatchRange
    // the prey bolts directly away from this predator (a real flee, not a bobber/random scatter)
    // and the predator breaks off to rest. If the prey outruns the leash, the hunt is abandoned.
    private void UpdateHunting()
    {
        // Only ever chase a calm, wandering fish. The moment the prey gets pulled into anything
        // else — the player's bobber, another predator, leaving — the predator breaks off, so a
        // hunt never snatches a fish the player is actively working.
        if (preyTarget == null || preyTarget.currentState != FishState.Wandering)
        {
            EndHunt();
            return;
        }

        Vector3 preyPos = preyTarget.transform.position;
        float dist = GetHorizontalDistance(GetHeadPosition(), preyPos);

        // Caught up: the prey bolts straight away from this predator and vanishes — diving and
        // fading out (the same exit a lifetime despawn plays). The zone is notified the instant the
        // despawn begins, so a replacement fish spawns to take its place while the fade runs.
        if (dist < huntCatchRange)
        {
            preyTarget.BeginDespawn(transform.position);
            EndHunt();
            return;
        }

        // Outran the leash — abandon the chase.
        if (dist > huntLeashRadius)
        {
            EndHunt();
            return;
        }

        preyPos.y = waterSurfaceY;
        SteerToward(preyPos, huntSpeed, huntTurnRate);
    }

    // Scan the shared school for the nearest huntable prey within huntRadius. Only calm, wandering
    // prey are eligible — fish already fleeing, leaving, or drawn to the player's tackle are left
    // alone so a hunt picks a fresh target and never disrupts active fishing.
    private bool TryAcquirePrey()
    {
        if (school == null) return false;

        FishRipple closest = null;
        float bestSqr = huntRadius * huntRadius;
        for (int i = 0; i < school.Count; i++)
        {
            FishRipple other = school[i];
            if (other == null || other == this) continue;
            if (!preset.Hunts(other.preset)) continue;
            if (other.currentState != FishState.Wandering) continue;

            Vector3 diff = other.transform.position - transform.position;
            diff.y = 0f;
            float sqr = diff.sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                closest = other;
            }
        }

        preyTarget = closest;
        return closest != null;
    }

    private void EndHunt()
    {
        preyTarget = null;
        huntCooldownTimer = huntCooldown;
        currentState = FishState.Wandering;
        PickNewWanderTarget();
    }

    // This fish leaves for good, and the zone is told at once so the replacement spawns while the
    // fade plays. Two callers, two moods: calm lifetime expiry (fleeFrom == null) keeps the current
    // heading and cruises off — wheeling around to flee reads as unnatural for an unbothered fish;
    // a prey fish a predator just caught (fleeFrom == the predator's position) bursts directly away
    // from it, faster. Either way it dives into the murk and fades out.
    private void BeginDespawn(Vector3? fleeFrom = null)
    {
        currentState = FishState.Despawning;
        despawnTimer = 0f;
        isFollower = false;
        onGrabStartCallback = null;
        onGrabReleasedCallback = null;

        // Pick the swim-off heading, then prefer one with clear water: probe the mid-point of the
        // swim-off and yaw around until one fits. A blocked heading just means the fade plays
        // against the shore — acceptable worst case, so the heading stands if nothing better.
        Vector3 heading;
        if (fleeFrom.HasValue)
        {
            Vector3 away = transform.position - fleeFrom.Value;
            away.y = 0f;
            heading = away.sqrMagnitude > 0.0001f ? away.normalized : GetFlatForward();
            despawnSpeed = DespawnFleeSpeed;
        }
        else
        {
            heading = GetFlatForward();
            despawnSpeed = DespawnSpeed;
        }

        despawnDirection = heading;
        float probeDistance = despawnSpeed * DespawnDuration * 0.5f;
        float[] yawOffsets = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };
        for (int i = 0; i < yawOffsets.Length; i++)
        {
            Vector3 candidate = Quaternion.Euler(0f, yawOffsets[i], 0f) * heading;
            if (!IsObstacleAt(transform.position + candidate * probeDistance))
            {
                despawnDirection = candidate;
                break;
            }
        }

        // Only the diving body remains: the surface ripple, glimpse indicator and wake all
        // read as "fish here, catchable" and this one no longer is.
        var particles = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
        glimpse.ForceHide();
        if (activeWakeInstance != null)
        {
            Destroy(activeWakeInstance);
            activeWakeInstance = null;
        }

        OnDespawnStarted?.Invoke(this);
    }

    private void UpdateDespawning()
    {
        despawnTimer += Time.deltaTime;
        float t = despawnTimer / DespawnDuration;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(despawnDirection.x, despawnDirection.z) * Mathf.Rad2Deg, 0f);
        Vector3 pos = transform.position + despawnDirection * (despawnSpeed * Time.deltaTime);
        pos.y = waterSurfaceY - DespawnDiveDepth * t;
        transform.position = pos;

        modelVisual?.SetFadeAlpha(1f - t);
    }

    private void StartNibbling()
    {
        currentState = FishState.Nibbling;

        if (nibble == null) nibble = new FishNibbleBehavior(transform);
        nibble.Begin(bobberTransform, waterSurfaceY, preset);

        // FishRipple also wants to hide visuals once the bite resolves. Subscribed here
        // (not in FishNibbleBehavior) because HideVisuals is a FishRipple concern.
        FishingEvents.OnFishBite += OnFishBiteHide;
    }

    // Called externally (e.g. FishingZone) just before the final nibble — prevents pull-back.
    public void MarkBiteImminent() => nibble?.MarkBiteImminent();

    private void OnFishBiteHide(BobberController bobber)
    {
        if (bobberTransform != null && bobber.transform == bobberTransform)
        {
            HideVisuals();
            FishingEvents.OnFishBite -= OnFishBiteHide;
            nibble?.Stop();
        }
    }

    private void HideVisuals()
    {
        // Disable all particle systems on this object and children
        var particles = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // Disable all renderers on this object and children
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = false;
        }

        glimpse.ForceHide();

        // Destroy wake
        if (activeWakeInstance != null)
        {
            Destroy(activeWakeInstance);
            activeWakeInstance = null;
        }
    }

    // Fish swim head-first: the HEAD is the steered agent — it turns toward the target at a
    // capped rate and only ever advances along its own heading — and the body centre is
    // placed a fixed distance behind it, trailer-style. Steering the head rather than the
    // centre matters: pivoting around the head swings the centre sideways, so steering by
    // the centre's bearing feeds that swing back into the next turn, which reads as jitter
    // and endless orbiting. The head can't move sideways, so the loop can't form.
    //
    // The final centre position is validated against bounds/obstacles BEFORE anything is
    // applied — a turn can never push the body into a rock. Rotation stays yaw-only and the
    // result is pinned to the water surface: strictly horizontal swimming.
    private void SteerToward(Vector3 target, float speed, float turnDegPerSec)
    {
        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        float headOffset = modelVisual != null ? modelVisual.HeadForwardOffset : 0f;
        Vector3 head = transform.position + flatForward * headOffset;

        // Pre-emptive avoidance: look ahead of the head, and if that water is blocked,
        // steer for the open side BEFORE contact — the fish arcs away from shores and rocks
        // instead of stalling nose-first against them.
        float lookahead = Mathf.Max(speed * 0.75f, 0.3f);
        if (FishMovementHelpers.IsObstacleAt(head + flatForward * lookahead, obstacleLayers, waterSurfaceY, obstacleRayHeight))
        {
            Vector3 leftDir = Quaternion.Euler(0f, -50f, 0f) * flatForward;
            Vector3 rightDir = Quaternion.Euler(0f, 50f, 0f) * flatForward;
            bool leftFree = !FishMovementHelpers.IsObstacleAt(head + leftDir * lookahead, obstacleLayers, waterSurfaceY, obstacleRayHeight);
            bool rightFree = !FishMovementHelpers.IsObstacleAt(head + rightDir * lookahead, obstacleLayers, waterSurfaceY, obstacleRayHeight);

            Vector3 toGoal = target - head;
            toGoal.y = 0f;
            Vector3 avoidDir;
            if (leftFree && rightFree)
                avoidDir = Vector3.SignedAngle(flatForward, toGoal, Vector3.up) < 0f ? leftDir : rightDir;
            else if (leftFree) avoidDir = leftDir;
            else if (rightFree) avoidDir = rightDir;
            else avoidDir = -flatForward; // pocket dead ahead: turn around

            target = head + avoidDir * 2f;
            turnDegPerSec *= 2f;
        }

        float deltaYaw = 0f;
        Vector3 toTarget = target - head;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.0001f)
        {
            // A target closer than the turning diameter can never be reached at this turn
            // rate — the fish orbits it forever at a steady ~90° bearing. Real fish simply
            // pivot harder when the goal is right beside them: raise the effective turn
            // rate until the turning circle fits the distance (capped so it can't snap).
            float turnRadius = speed / Mathf.Max(turnDegPerSec * Mathf.Deg2Rad, 0.01f);
            float targetDist = toTarget.magnitude;
            if (targetDist < 2f * turnRadius)
                turnDegPerSec *= Mathf.Min(2f * turnRadius / Mathf.Max(targetDist, 0.05f), 8f);

            float signedAngle = Vector3.SignedAngle(flatForward, toTarget, Vector3.up);
            float maxStep = turnDegPerSec * Time.deltaTime;
            deltaYaw = Mathf.Clamp(signedAngle, -maxStep, maxStep);
        }

        // Rebuild a level, yaw-only rotation every frame (never rotate incrementally): the
        // host can then never accumulate pitch or roll, no matter what orientation the
        // prefab spawned with.
        float yawDeg = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
        Quaternion newRotation = Quaternion.Euler(0f, yawDeg + deltaYaw, 0f);
        Vector3 newForward = newRotation * Vector3.forward;

        Vector3 newHead = head + newForward * (speed * Time.deltaTime);
        Vector3 newPos = newHead - newForward * headOffset;
        newPos = FishMovementHelpers.ClampToBounds(newPos, zoneBounds);
        newPos.y = waterSurfaceY;

        // Check the centre AND the head — the head pokes half a body ahead, and it's the
        // part that ends up visibly buried in rocks if only the centre is validated.
        Vector3 newHeadPos = newPos + newForward * headOffset;
        if (!FishMovementHelpers.IsObstacleAt(newPos, obstacleLayers, waterSurfaceY, obstacleRayHeight)
            && !FishMovementHelpers.IsObstacleAt(newHeadPos, obstacleLayers, waterSurfaceY, obstacleRayHeight))
        {
            transform.rotation = newRotation;
            transform.position = newPos;
            return;
        }

        // Blocked this frame: keep turning, and keep MOVING — slide along the nearest open
        // direction at half speed rather than freezing. Only a fish boxed in on every side
        // stays put, and the stuck rescue eventually relocates those.
        transform.rotation = newRotation;
        if (currentState == FishState.Wandering) hasWanderTarget = false;

        float slideDistance = speed * 0.5f * Time.deltaTime;
        float preferredSide = deltaYaw >= 0f ? 1f : -1f;
        for (int step = 1; step <= 4; step++)
        {
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? preferredSide : -preferredSide;
                Vector3 slideDir = Quaternion.Euler(0f, 45f * step * sign, 0f) * newForward;
                Vector3 slidePos = transform.position + slideDir * slideDistance;
                slidePos = FishMovementHelpers.ClampToBounds(slidePos, zoneBounds);
                slidePos.y = waterSurfaceY;
                Vector3 slideHead = slidePos + newForward * headOffset;
                if (!FishMovementHelpers.IsObstacleAt(slidePos, obstacleLayers, waterSurfaceY, obstacleRayHeight)
                    && !FishMovementHelpers.IsObstacleAt(slideHead, obstacleLayers, waterSurfaceY, obstacleRayHeight))
                {
                    transform.position = slidePos;
                    return;
                }
            }
        }
    }

    private void PickNewWanderTarget()
    {
        wanderTarget = FishMovementHelpers.GetRandomPointInBounds(zoneBounds, transform.position, waterSurfaceY, obstacleLayers, obstacleRayHeight);
        wanderTarget.y = waterSurfaceY;
        hasWanderTarget = true;
    }

    private Vector3 GetFlatForward()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
    }

    // Personal-space steering: a push away from schoolmates within separationRadius,
    // stronger the closer they are. Wandering-only — attracted fish crowd the bobber on
    // purpose. O(n²) over a zone's handful of fish is negligible.
    private Vector3 ComputeSeparation()
    {
        if (school == null || separationStrength <= 0f) return Vector3.zero;

        Vector3 push = Vector3.zero;
        for (int i = 0; i < school.Count; i++)
        {
            FishRipple other = school[i];
            if (other == null || other == this) continue;
            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;
            float dist = away.magnitude;
            if (dist >= separationRadius || dist < 0.0001f) continue;
            push += away / dist * (1f - dist / separationRadius);
        }
        return push;
    }

    // The steered head point — where the fish is actually going. Movement targets should be
    // judged from here, not the transform centre, which trails half a body length behind.
    private Vector3 GetHeadPosition()
    {
        float headOffset = modelVisual != null ? modelVisual.HeadForwardOffset : 0f;
        return transform.position + GetFlatForward() * headOffset;
    }

    // Local wrappers so the state-machine bodies (UpdateWandering, UpdateScared) read the same
    // as before. They forward to FishMovementHelpers but capture this fish's bounds and surface.
    private Vector3 ClampToBounds(Vector3 pos) => FishMovementHelpers.ClampToBounds(pos, zoneBounds);
    private bool IsObstacleAt(Vector3 pos) => FishMovementHelpers.IsObstacleAt(pos, obstacleLayers, waterSurfaceY, obstacleRayHeight);
    private float GetHorizontalDistance(Vector3 a, Vector3 b) => FishMovementHelpers.GetHorizontalDistance(a, b);

    void OnDestroy()
    {
        if (activeWakeInstance != null) Destroy(activeWakeInstance);
    }

    // Scene-view tuning aid: cyan = the radius at which this fish sees a bobber/lure
    // (species awarenessRadius, or the prefab fallback). Yellow appears at runtime when the
    // lure brain has boosted awareness (lure recently moved). Red = scare range.
    private void OnDrawGizmosSelected()
    {
        float baseRadius = BaseAwarenessRadius;
        if (baseRadius <= 0f) return;

        Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, baseRadius);

        if (Application.isPlaying && awarenessScale > 1.001f)
        {
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, EffectiveActionRadius);
        }

        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, scareRange);
    }
}
