using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class FishRipple : MonoBehaviour
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
    [Tooltip("Same-species shoaling (boids cohesion + alignment). Only species whose FishPreset has " +
             "'Schools Together' checked use it, and only toward their own kind — the separation above " +
             "still applies to every fish for personal space. How far this fish looks for same-species " +
             "schoolmates. A fish that strays past this can't see the shoal and won't rejoin, so size it " +
             "roughly to the pond's radius — it's a cheap same-species/wandering-only scan, so err large.")]
    public float schoolPerceptionRadius = 8f;
    [Tooltip("How hard a schooling fish steers back toward the shoal's centre, PER METRE it has strayed " +
             "(the pull grows with distance and is capped at schoolPerceptionRadius). ~1 means a fish near " +
             "the edge of perception commits almost fully to rejoining; lower keeps the shoal looser. 0 disables cohesion.")]
    public float schoolCohesionStrength = 0.8f;
    [Tooltip("How strongly a schooling fish matches the average heading of nearby same-species fish, so the shoal swims as one. 0 disables alignment.")]
    public float schoolAlignmentStrength = 0.6f;

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
    [Tooltip("How quickly the predator sheds its charge speed after a lunge connects, per second " +
             "(exponential). It coasts on through where the prey bolted, slowing until it's back " +
             "at wander pace, instead of stopping dead on the touch. Lower = longer, farther coast.")]
    public float huntCoastDecay = 0.9f;

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

    [Header("Bobber Nibbling (Dash & Pass)")]
    [Tooltip("Distance at which the lead fish stops approaching and starts 'working' the bobber — circling it and darting past. Give it room to circle: keep above nibbleRange and below commitDistance.")]
    public float nibbleStartRange = 1.3f;
    [Tooltip("Radius the lead fish orbits the bobber at between nibble passes.")]
    public float nibbleCircleRadius = 1.0f;
    [Tooltip("Cruise speed while circling the bobber between passes.")]
    public float nibbleCircleSpeed = 1.3f;
    [Tooltip("Speed of a nibble / bite dash — the committed dart at the bobber.")]
    public float nibbleDashSpeed = 3.5f;
    [Tooltip("How close the fish must get on a dash to brush the bobber (wobble) or, on the bite dash, take it.")]
    public float nibbleTouchRadius = 0.45f;
    [Tooltip("How far past the bobber a nibble dash carries before the fish loops back to circling.")]
    public float nibblePassDistance = 1.2f;
    [Tooltip("Random seconds of circling between nibble passes — keep it relaxed so nibbles feel natural, not rapid-fire. [min, max].")]
    public Vector2 nibbleGapRange = new Vector2(2f, 5f);
    [Tooltip("Random number of nibble passes the fish makes before it commits to the bite. [min, max].")]
    public Vector2 nibblesBeforeBite = new Vector2(2f, 4f);
    [Tooltip("Max heading turn rate (deg/sec) while circling/dashing the bobber. Caps how fast the " +
             "fish can swing around so the trailing body never folds back through itself. Lower = " +
             "wider, lazier arcs; too low and a dash can't aim at the bobber.")]
    public float nibbleTurnRate = 200f;
    [Tooltip("How much the orbit radius breathes in and out over time (0 = fixed ring, 0.4 = ±40%). " +
             "Higher feels more like aimless milling near the bobber.")]
    [Range(0f, 1f)] public float nibbleRadiusJitter = 0.35f;
    [Tooltip("How erratic the circling path is — sideways noise on the orbit. 0 = clean circle, " +
             "higher bends the path in and out for a natural, non-mechanical mill.")]
    [Range(0f, 1.5f)] public float nibbleWanderStrength = 0.4f;
    [Tooltip("How far (meters) the fish noses UP toward the bobber at the closest point of a nibble " +
             "dash, then sinks back as it passes — so a nibble visibly rises at the bobber like a " +
             "bite does instead of skimming flat underneath it. 0 = stay flat (old behavior).")]
    public float nibbleDashRise = 0.3f;

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

    [Header("Interest Indicators")]
    [Tooltip("Master switch for the above-fish interest flame: the notice pop and the investigating glow.")]
    public bool enableInterestIndicators = true;
    [Tooltip("Flame quad prefab (Flame_Texture_Wobble material, typically with BillboardSprite + FlameLag). Instantiated lazily per engaging fish. Nothing shows while this is empty.")]
    public GameObject interestFlamePrefab;
    [Tooltip("Warmer flame colors shown the instant this fish first notices the bobber/lure (Wandering -> Attracted). Holds for Notice Duration, then crossfades back to the material's own colors (the investigate look — tune that on the flame material itself).")]
    public FishInterestIndicator.FlamePalette noticeFlame = new FishInterestIndicator.FlamePalette
    {
        core = new Color(1f, 1f, 1f, 1f),
        outer = new Color(1f, 0.85f, 0.3f, 1f),
        glow = new Color(2f, 1.2f, 0.2f, 1f), // HDR: old (1,0.6,0.1) x2 glowStrength baked into the color
    };
    [Tooltip("Seconds the notice <-> base-color crossfade takes.")]
    public float flameBlendTime = 0.35f;
    [Tooltip("One-shot sound cue played once when this fish notices the bobber/lure. Leave empty for a silent notice.")]
    public AudioClip noticeSound;
    [Tooltip("Volume for the notice sound cue.")]
    [Range(0f, 1f)] public float noticeSoundVolume = 1f;
    [Tooltip("Seconds the notice palette holds before crossfading to the investigate palette.")]
    public float noticeDuration = 1f;
    [Tooltip("World-units above the fish the flame floats. The fish rides on the water line, so this lifts it clear of the surface.")]
    public float indicatorHeight = 0.7f;
    [Tooltip("Uniform world scale multiplier applied on top of the flame prefab's authored scale.")]
    public float indicatorScale = 0.5f;
    [Tooltip("Seconds the flame takes to fade in/out (0 = pop instantly).")]
    public float indicatorFadeTime = 0.15f;
    [Tooltip("Renderer sorting order for the flame.")]
    public int indicatorSortingOrder = 100;

    [Header("Timing")]
    public float scareCooldown = 5f;
    public float wanderPauseMin = 0.5f;
    public float wanderPauseMax = 2f;

    [Header("Wander Rest (Full Stop)")]
    [Tooltip("Chance that a wander pause becomes a full REST: the fish glides to a dead stop and " +
             "its tail winds down to nearly still until it moves off again. The remaining pauses " +
             "keep the old slow pauseDriftSpeed drift. 0 disables rests entirely.")]
    [Range(0f, 1f)] public float restChance = 0.7f;
    [Tooltip("How long a full rest lasts, in seconds [min, max]. Separate from the drift-pause " +
             "range so rests can sit noticeably longer — a stopped fish needs a beat to read.")]
    public Vector2 restDurationRange = new Vector2(5f, 12f);
    [Tooltip("Seconds the fish takes to glide from swimming down to fully stopped when a rest " +
             "begins, and to pick its speed back up when it ends. The tail-beat winds down and " +
             "back up over the same ramp.")]
    public float restEaseSeconds = 1.2f;

    [Header("Lifetime")]
    [Tooltip("Seconds this fish hangs around before it moves on: it swims off, dives and fades " +
             "out (the same getaway an escaped catch plays) and the zone spawns a replacement. " +
             "The clock pauses entirely while a bobber/lure is in this pool's water — fish " +
             "never leave during any stage of fishing — and expiry only fires from calm " +
             "wandering, never mid-flee. Set 0 to disable.")]
    public float lifetimeSeconds = 45f;

    [Tooltip("Random ± spread (seconds) added to each fish's lifetime when it spawns, so a school " +
             "that appears together doesn't all swim off at the same instant. With lifetime 45 and " +
             "jitter 12 each fish lives 33–57s. 0 = every fish shares the exact same clock.")]
    public float lifetimeJitterSeconds = 12f;

    [Header("Spawn")]
    [Tooltip("Seconds the fish takes to dissolve IN when it spawns — the reverse of the swim-off " +
             "dive's fade-out. It's an ordered dither (the fish stays opaque), so it materialises " +
             "cleanly underneath the water instead of ghosting on the surface. 0 = appear instantly. " +
             "Tunable live in play mode.")]
    public float spawnFadeInSeconds = 2.5f;

    [HideInInspector] public FishPreset preset;

    // Fired once when the lifetime expires and the swim-off begins. The zone uses it to drop
    // this fish from its roster (it's no longer catchable) and spawn the replacement.
    public event System.Action<FishRipple> OnDespawnStarted;

    public FishState CurrentState => currentState;
    public bool IsFollower => isFollower;
    // Seconds left in the current lure grab (the reaction window), 0 when not gripping.
    public float GrabHoldRemaining => currentState == FishState.Grabbing ? Mathf.Max(0f, grabTimer) : 0f;
    // True briefly after the fish lets go of a missed grab — it's coasting away on its release
    // follow-through and ignoring the tackle. The zone leaves it alone on the reel-in so it swims
    // off calmly (like the lure's spat fish) instead of being re-scared into a jitter at the spot
    // it just released, where the away-from-bobber direction would be near-zero.
    public bool IsRecoveringFromGrab => reengageCooldown > 0f;
    // True while the fish is mid lure-nibble pass (a tease dart that returns to hovering, not a
    // committed strike). The lure brain keeps such a fish as an in-ring chaser rather than dropping
    // or committing it.
    public bool IsLureNibbling => nibblePass;

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
    // Progress watchdog on the current wander leg: closest the head has come to wanderTarget,
    // and how long since that improved. The schooling/separation offsets can hold a stable
    // equilibrium the fish orbits forever (boids "milling") without ever closing on its target —
    // those orbits are metres wide, so the net-displacement stuck rescue never sees them, and the
    // turn-radius drop below never fires because the TARGET stays far away. No progress for a few
    // seconds means the goal is unreachable from here: drop it for a fresh roll.
    private float wanderBestDist;
    private float wanderNoProgressTimer;

    // Wander rest: 0 = swimming normally, 1 = fully halted. Ramps up over restEaseSeconds when a
    // rest pause starts and back down when it ends, driving both the glide-to-stop and the tail
    // wind-down (GetSwayIntensity + the flap-amplitude fade on the Tick call). Any state change
    // out of Wandering (scared, attracted, hunting...) snaps it clear so reactions stay sharp.
    private bool isResting;
    private float restBlend;

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
    // Per-fish random shift on lifetimeSeconds, rolled once at spawn (Initialize) so schoolmates
    // expire at staggered times rather than all at once. Added to the live lifetimeSeconds, so
    // tuning the base in play mode still nudges every fish while keeping their relative spread.
    private float lifetimeOffset;
    private float despawnTimer;
    private Vector3 despawnDirection;
    private float despawnSpeed;

    // Bobber avoidance
    private bool shouldAvoidBobber;

    // Predator hunting: the prey this fish is currently chasing, and a rest timer after a chase
    // ends so it doesn't instantly re-lock the fish it just scared off.
    private FishRipple preyTarget;
    private float huntCooldownTimer;
    // Post-catch momentum: set to huntSpeed when a lunge connects, then bled off exponentially
    // by the coast in UpdateWandering until it's back under swimSpeed. 0 = not coasting.
    private float huntCoastSpeed;

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
    // Lure nibble pass: a non-committing tease. The fish darts THROUGH the lure (brushing it for a
    // twitch) out to a point past it, then returns to hovering — the lure equivalent of the bobber's
    // dash-past nibble. Runs inside the Striking state (nibblePass true) so it inherits all the
    // strike-state handling, but it never grabs; on reaching the overshoot it drops back to Attracted.
    private bool nibblePass;
    private Vector3 nibblePassTarget;
    private bool nibblePassBrushed;
    private float nibblePassTimer; // safety cap so a blocked overshoot can't strand the fish mid-pass
    // Same safety cap for the committed dash: if the strike's snout-to-bobber contact can never
    // land (bobber tucked against geometry the dash keeps steering around, or at a height the arc
    // can't reach), the fish gives up instead of circling the bobber in Striking forever — which,
    // on the lure path, also blocks every other chaser's bite roll (AnyStriking).
    private float strikeGiveUpTimer;
    // The head's height during a strike/grab is integrated separately (SteerToward re-pins y to
    // the surface each frame), so the fish can arc up to the lure and dip while gripping.
    private float strikeHeadY;
    private float grabStartHeadY;
    // Horizontal distance to the lure/bobber at a leap's launch, so EVERY strike and tease (bobber
    // dash, bobber bite, lure strike, lure nibble pass) ramps its rise by leap PROGRESS — beginning
    // the instant the leap starts — instead of waiting on proximity. This makes a tease and a real
    // bite build up identically on both tackle types.
    private float strikeStartHoriz;
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
    private readonly FishInterestIndicator interestIndicator = new FishInterestIndicator();
    private FishNibbleBehavior nibble;
    private FishModelVisual modelVisual;

    // Previous frame's state, so UpdateInterestIndicators can catch the Wandering -> Attracted
    // "notice" edge (and only that edge — a fish returning to Attracted from a lure nibble pass
    // comes from Striking, so the '!' never re-fires mid-engagement).
    private FishState prevIndicatorState = FishState.Wandering;
    // Global guard so a whole school noticing the same frame doesn't flam the notice sound into a
    // phasing mess — the visuals still fire per-fish, only the audio is debounced.
    private static float lastNoticeSoundTime = -10f;
    private const float NoticeSoundMinInterval = 0.08f;

    public void Initialize(Collider bounds, float surfaceY, GameObject aimIndicatorPrefab)
    {
        zoneBounds = bounds;
        waterSurfaceY = surfaceY;

        // Stagger this fish's despawn so a school spawned in the same frame doesn't leave together.
        lifetimeOffset = Random.Range(-lifetimeJitterSeconds, lifetimeJitterSeconds);

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
        if (spawnFadeInSeconds > 0f) modelVisual.BeginSpawnFadeIn(spawnFadeInSeconds);

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

    public void AttractToBobber() => AttractToBobber(true);

    // allowScare separates the PLAYER deliberately calling a fish in — pressing Attract right on
    // top of one spooks it, which is intended — from the PASSIVE auto-attract a wandering fish
    // does on its own when the bobber drifts into its awareness radius. A fish must never spook
    // ITSELF just for drifting close out of curiosity (two fish creeping in before any lead exists
    // would both flee for no reason), so the passive path passes allowScare=false: too-close then
    // just means "don't attract this frame," not "flee."
    public void AttractToBobber(bool allowScare)
    {
        // Mid-bite states (Nibbling working the bobber, or the Striking/Grabbing dash-and-carry)
        // own the fish — a stray Attract press must not yank it back out of a committed bite.
        if (currentState == FishState.Scared || currentState == FishState.Nibbling
            || currentState == FishState.Striking || currentState == FishState.Grabbing
            || currentState == FishState.Despawning) return;
        if (bobberTransform == null) return;

        float dist = GetHorizontalDistance(transform.position, bobberTransform.position);

        if (dist < scareRange)
        {
            if (allowScare) Scare();
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

    // Cone-of-vision gate on top of the radius above: a species with awarenessConeAngle > 0 only
    // notices a bobber/lure within that field of view centred on its current heading, so aim
    // matters — a wide-radius species swimming away no longer out-notices a closer, better-aimed-at
    // species just because its circle happens to be bigger. 0 (the FishPreset default) keeps the
    // old omnidirectional behavior so untouched species/prefabs are unaffected.
    private bool IsWithinAwarenessCone(Vector3 targetPos)
    {
        float coneAngle = preset != null ? preset.awarenessConeAngle : 0f;
        if (coneAngle <= 0f) return true;

        Vector3 toTarget = targetPos - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return true;

        return Vector3.Angle(GetFlatForward(), toTarget) <= coneAngle * 0.5f;
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
                BeginWanderLeg();
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

        // Leaving any bite attempt — working the bobber (Nibbling) or mid dash-and-carry
        // (Striking/Grabbing) — drops the bite-hide hook and stops the nibble, so a later bite on
        // this bobber can't hide this now-fleeing fish.
        if (bobberTransform != null
            && (currentState == FishState.Nibbling || currentState == FishState.Striking
                || currentState == FishState.Grabbing))
        {
            BobberController bobber = bobberTransform.GetComponent<BobberController>();
            if (bobber != null) bobber.CancelNibbleSequence();
            FishingEvents.OnFishBite -= OnFishBiteHide;
            nibble?.Stop();
        }

        Vector3 awayDir = bobberTransform != null
            ? transform.position - bobberTransform.position
            : Vector3.zero;
        awayDir.y = 0f;
        // On top of the bobber (e.g. spooked right after releasing a grab from the bobber's spot)
        // the away vector is ~zero and would leave the fish fleeing nowhere — jittering in place at
        // the scared tail-beat. Fall back to its current heading, then a random direction, so a
        // spooked fish always has somewhere to go.
        if (awayDir.sqrMagnitude < 0.0001f) awayDir = GetFlatForward();
        if (awayDir.sqrMagnitude < 0.0001f)
        {
            Vector2 rand = Random.insideUnitCircle.normalized;
            awayDir = new Vector3(rand.x, 0f, rand.y);
        }
        awayDir.Normalize();

        scareDirection = awayDir;
        scareTimer = scareCooldown;
        scareJinkTimer = 0f;
        isFollower = false;
        // Spooked mid-grab: hand the lure back and close the reaction window before fleeing so
        // the zone/rod don't hang.
        if (currentState == FishState.Grabbing) onGrabReleasedCallback?.Invoke(this);
        onGrabStartCallback = null;
        onGrabReleasedCallback = null;
        nibblePass = false;
        bobberCtrl?.EndGrabTow();
        currentState = FishState.Scared;
        FishingEvents.OnFishScared?.Invoke();
    }

    // Live registry of every active fish, kept in lockstep with enable/disable. The research
    // scanner (FishResearchScanner) reads this to find the fish under the aim reticle in screen
    // space — fish have no body collider, so a physics raycast wouldn't hit them.
    public static readonly List<FishRipple> Active = new List<FishRipple>();

    private void OnEnable()
    {
        if (!Active.Contains(this)) Active.Add(this);
        FishingEvents.OnStartAiming += ShowIndicator;
        FishingEvents.OnStopAiming += HideIndicator;
    }

    private void OnDisable()
    {
        Active.Remove(this);
        FishingEvents.OnStartAiming -= ShowIndicator;
        FishingEvents.OnStopAiming -= HideIndicator;
        FishingEvents.OnFishBite -= OnFishBiteHide;
        nibble?.UnsubscribeAll();
        interestIndicator.Cleanup();
    }

    void Update()
    {
        // Keep on water surface — unless dashing, gripping the lure, despawning, or nibbling, where
        // the strike thrash / grab dip / dive-away / nibble up-and-over arc own the height instead.
        Vector3 pos = transform.position;
        bool ownsHeight = currentState == FishState.Striking
                          || currentState == FishState.Grabbing
                          || currentState == FishState.Despawning
                          || currentState == FishState.Nibbling;
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
            float effectiveLifetime = Mathf.Max(0.1f, lifetimeSeconds + lifetimeOffset);
            if (ageTimer >= effectiveLifetime && currentState == FishState.Wandering)
                BeginDespawn();
        }

        // Cool-down after spitting a lure on a missed bite: ignore the lure briefly so the fish
        // coasts through its follow-through instead of immediately wheeling into another grab.
        if (reengageCooldown > 0f) reengageCooldown -= Time.deltaTime;

        // Rest between hunts so a predator doesn't re-lock the prey it just scared the same frame.
        if (huntCooldownTimer > 0f) huntCooldownTimer -= Time.deltaTime;

        // Anything that pulls the fish out of Wandering (noticing the bobber, a hunt, a scare)
        // snaps the rest and any post-catch coast clear — a startled fish reacts at full drive.
        if (currentState != FishState.Wandering && (isResting || restBlend > 0f || huntCoastSpeed > 0f))
        {
            isResting = false;
            restBlend = 0f;
            huntCoastSpeed = 0f;
        }

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
                // Circle/dash the bobber. Once it's worked the bobber enough, nibble.ReadyToBite
                // flips (Tick then no-ops) and the zone launches the bite via NibbleReadyToBite.
                nibble?.Tick();
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

        UpdateInterestIndicators();

        // Drive the procedural sway: agitated states beat the tail faster, which doubles as
        // a readable telegraph (a striking fish visibly thrashes toward the lure). The body is a
        // true 3D rope, so it trails the head's arc on its own when the fish rises to the lure.
        // A resting fish also relaxes the flap AMPLITUDE (not just the beat rate) to about half,
        // so the idle flap reads as a gentle sculling rather than full-power swimming in place.
        modelVisual?.Tick(Time.deltaTime, GetSwayIntensity(), bodyStraightenRate,
                          1f, 1f - restBlend * 0.5f);
    }

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
