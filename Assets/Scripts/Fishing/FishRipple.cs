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
    [Tooltip("Master switch for the two above-fish interest symbols: the notice '!' and the investigating '...' animation.")]
    public bool enableInterestIndicators = true;
    [Tooltip("The notice symbol (animated text, TextMeshPro — no art) shown for a beat the instant this fish first notices the bobber/lure (Wandering -> Attracted).")]
    public string noticeText = "!";
    [Tooltip("Color of the notice text.")]
    public Color noticeColor = Color.white;
    [Tooltip("Font size of the notice text (TextMeshPro world size).")]
    public float noticeFontSize = 8f;
    [Tooltip("One-shot sound cue played once when this fish notices the bobber/lure. Leave empty for a silent notice.")]
    public AudioClip noticeSound;
    [Tooltip("Volume for the notice sound cue.")]
    [Range(0f, 1f)] public float noticeSoundVolume = 1f;
    [Tooltip("Seconds the notice stays up before it fades out.")]
    public float noticeDuration = 1f;
    [Tooltip("The investigating '...' is animated text (TextMeshPro), cycled in code as . -> .. -> ... — no art needed. This is how fast it steps, in cycles/second.")]
    public float investigateDotSpeed = 3f;
    [Tooltip("Color of the investigating '...' dots.")]
    public Color investigateDotColor = Color.white;
    [Tooltip("Font size of the investigating '...' dots (TextMeshPro world size).")]
    public float investigateDotFontSize = 6f;
    [Tooltip("World-units above the fish the indicators float. The fish rides on the water line, so this lifts the symbols clear of the surface.")]
    public float indicatorHeight = 0.7f;
    [Tooltip("Uniform world scale applied to the indicator sprites.")]
    public float indicatorScale = 0.5f;
    [Tooltip("Seconds each indicator takes to fade in/out (0 = pop instantly).")]
    public float indicatorFadeTime = 0.15f;
    [Tooltip("SpriteRenderer sorting order for the indicators (the '!' draws one order above the '...').")]
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

    // Lure-brain strike: the fish has won its bite roll and commits — a fast 3D dash at the lure,
    // no weaving. onGrabStart fires when the dash connects and the fish clamps onto the lure (the
    // reaction window opens); onGrabReleased fires if the grip lapses with no player response (the
    // fish spits the lure and swims off). No bite is committed until the zone calls ConfirmGrab().
    public void StartLureStrike(System.Action<FishRipple> onGrabStart, System.Action<FishRipple> onGrabReleased)
    {
        // Already committed (Striking/Grabbing) or unavailable — don't restart a dash. Guarding
        // Striking also stops a re-roll firing while the fish is mid lure-nibble pass.
        if (currentState == FishState.Scared || currentState == FishState.Nibbling
            || currentState == FishState.Striking || currentState == FishState.Grabbing
            || currentState == FishState.Despawning) return;
        if (bobberTransform == null) return;

        onGrabStartCallback = onGrabStart;
        onGrabReleasedCallback = onGrabReleased;
        isFollower = false;
        dashDirection = GetFlatForward();
        strikeHeadY = transform.position.y; // start at current swim depth; the dash arcs it up
        // Progress-ramped rise, same as the bobber leap: the nose-up starts with the dash.
        strikeStartHoriz = GetHorizontalDistance(GetHeadPosition(), bobberTransform.position);
        strikeGiveUpTimer = StrikeGiveUpSeconds();
        currentState = FishState.Striking;
    }

    // Generous worst case for the dash: the run-up at dash speed plus slack for the rise and one
    // missed pass. A clean strike connects in a fraction of this.
    private float StrikeGiveUpSeconds() =>
        3f + strikeStartHoriz / Mathf.Max(0.5f, strikeSpeed);

    // A non-committing lure nibble: the fish darts THROUGH the lure (twitching it) and out to a
    // point past it, then drops back to hovering, still interested. The lure brain rolls this on a
    // response that isn't a full bite, so the player has to keep tugging to re-roll for a real bite.
    // isFollower is left untouched so the fish stays a hovering chaser when the pass ends (clearing
    // it would let UpdateAttracted mistake this lure chaser for a bait lead and start bobber-nibbling).
    public void StartLureNibble()
    {
        if (currentState == FishState.Scared || currentState == FishState.Striking
            || currentState == FishState.Grabbing || currentState == FishState.Despawning) return;
        if (bobberTransform == null) return;

        Vector3 toLure = bobberTransform.position - transform.position;
        toLure.y = 0f;
        if (toLure.sqrMagnitude < 0.0001f) toLure = GetFlatForward();
        toLure.Normalize();

        Vector3 lureFlat = new Vector3(bobberTransform.position.x, waterSurfaceY, bobberTransform.position.z);
        nibblePassTarget = lureFlat + toLure * Mathf.Max(0.3f, nibblePassDistance);
        nibblePassBrushed = false;
        nibblePassTimer = 2f;
        nibblePass = true;
        dashDirection = GetFlatForward();
        strikeHeadY = transform.position.y;
        // Same progress-ramped rise as a real strike, so the tease's nose-up begins with the dart.
        strikeStartHoriz = GetHorizontalDistance(GetHeadPosition(), bobberTransform.position);
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
        // A bobber bite that's missed (window lapsed) coasts off as a wandering fish; drop the
        // bite-hide hook so a LATER bite on this same bobber can't hide this now-innocent fish.
        // Harmless for the lure path (those fish never subscribed).
        FishingEvents.OnFishBite -= OnFishBiteHide;

        currentState = FishState.Wandering;
        Vector3 ahead = GetHeadPosition() + dashDirection * grabReleaseFollowThrough;
        ahead.y = waterSurfaceY;
        wanderTarget = ClampToBounds(ahead);
        BeginWanderLeg();
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
        nibblePass = false;
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

    // Drives the two above-fish interest symbols. The '!' fires once on the Wandering -> Attracted
    // "notice" edge (plus the one-shot sound); the '...' plays while the fish is investigating the
    // tackle — circling/hovering it (Attracted), working a bobber (Nibbling) or teasing a lure
    // (IsLureNibbling) — and both hide once it commits to the bite, gets scared or swims off.
    private void UpdateInterestIndicators()
    {
        if (!enableInterestIndicators)
        {
            prevIndicatorState = currentState;
            return;
        }

        FishInterestIndicator.Settings s = GetInterestSettings();

        if (prevIndicatorState == FishState.Wandering && currentState == FishState.Attracted)
        {
            interestIndicator.TriggerNotice(in s);
            PlayNoticeSound();
        }

        bool investigating = currentState == FishState.Attracted
                             || currentState == FishState.Nibbling
                             || IsLureNibbling;
        // Anchor over the fish's HEAD, not the body centre or the host pivot. The body centre sits
        // mid-fish (marker floats over the middle) and the pivot sits behind the mesh (marker swings
        // off to the side as the fish turns). Bias forward from the body centre toward the rendered
        // snout so the symbol rides above the head. Keep the host's Y (the water line) so
        // indicatorHeight still measures from the surface as tuned.
        Vector3 markerPos = transform.position;
        if (modelVisual != null)
        {
            Vector3 c = modelVisual.VisualCenterWorldPosition;
            Vector3 m = modelVisual.MouthWorldPosition;
            // ~60% of the way from centre to snout lands on the head rather than the nose tip.
            Vector3 head = Vector3.Lerp(c, m, 0.6f);
            markerPos = new Vector3(head.x, transform.position.y, head.z);
        }
        interestIndicator.Tick(markerPos, investigating, in s);

        prevIndicatorState = currentState;
    }

    private void PlayNoticeSound()
    {
        if (noticeSound == null) return;
        // Debounce globally so a school noticing together plays the cue once, not layered N times.
        if (Time.unscaledTime - lastNoticeSoundTime < NoticeSoundMinInterval) return;
        lastNoticeSoundTime = Time.unscaledTime;
        AudioSource.PlayClipAtPoint(noticeSound, transform.position, noticeSoundVolume);
    }

    private FishInterestIndicator.Settings GetInterestSettings()
    {
        return new FishInterestIndicator.Settings
        {
            noticeText = noticeText,
            noticeColor = noticeColor,
            noticeFontSize = noticeFontSize,
            noticeDuration = noticeDuration,
            dotCyclesPerSecond = investigateDotSpeed,
            dotColor = investigateDotColor,
            dotFontSize = investigateDotFontSize,
            fadeTime = indicatorFadeTime,
            heightAboveFish = indicatorHeight,
            scale = indicatorScale,
            sortingOrder = indicatorSortingOrder,
        };
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

    // Wandering fish that haven't moved meaningfully for several seconds are wedged in
    // geometry (spawned inside a rock, or terrain that isn't on the obstacle mask): swimming
    // can't free them because every heading is blocked, so relocate to a fresh valid point.
    // A fish sitting out a pause isn't stuck — full rests deliberately hold a dead stop for
    // longer than the rescue window, so the check only runs while the fish should be moving.
    private void UpdateStuckRescue()
    {
        if (currentState != FishState.Wandering || wanderPauseTimer > 0f)
        {
            stuckTimer = 0f;
            stuckAnchor = transform.position;
            return;
        }

        // NET displacement, not raw motion: a fish locked in a tight endless circle "moves" every
        // frame but goes nowhere, so a small threshold here let it reset the anchor forever and
        // dodge the rescue. 0.75m over the 4s window still resets instantly for any fish making
        // real progress (even the 0.5 m/s arrival glide covers 2m in that time), but catches
        // orbits up to ~0.75m across as stuck — they get a fresh wander target below.
        if (FishMovementHelpers.GetHorizontalDistance(transform.position, stuckAnchor) > 0.75f)
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
            // A resting wanderer's tail winds down to a soft idle flap — clearly slower than
            // cruising but still visibly beating; restBlend is 0 outside a rest, so normal
            // cruising keeps the calm 1.
            case FishState.Wandering:  return Mathf.Lerp(1f, 0.35f, restBlend);
            case FishState.Attracted:  return 1.5f;
            // Calm tail while loitering between passes; mid-dash matches the Striking thrash (2.8) so a
            // tease dash and a real bite look identical in their buildup.
            case FishState.Nibbling:   return nibble != null && nibble.IsDashing ? 2.8f : 0.8f;
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

        if (nibblePass)
        {
            UpdateLureNibblePass();
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

        // Contact never landed in the worst-case dash time: the bobber is somewhere the dash can't
        // actually reach. Give up and swim off (with the post-grab re-engage cooldown, so the fish
        // doesn't immediately wheel back and repeat the doomed dash).
        strikeGiveUpTimer -= Time.deltaTime;
        if (strikeGiveUpTimer <= 0f)
        {
            reengageCooldown = grabReleaseAvoidTime;
            CancelLureStrike();
            return;
        }

        // Horizontal chase toward the lure.
        SteerToward(lureFlat, strikeSpeed, strikeTurnRate);
        dashDirection = GetFlatForward();

        // Vertical arc up to the lure, ramped by leap PROGRESS so the jump begins the instant the leap
        // launches (its run-up can be several metres). Reach full height over the first ~60% of the
        // leap, then hold it for the final approach, so the snout is already up at the lure when
        // contact lands instead of still climbing. Same for the bobber bite and the lure strike.
        float horiz = GetHorizontalDistance(GetHeadPosition(), lureFlat);
        float riseT = strikeStartHoriz > 0.01f ? 1f - Mathf.Clamp01(horiz / (strikeStartHoriz * 0.6f)) : 1f;
        float targetY = Mathf.Lerp(waterSurfaceY, HostYToPlaceSnoutAt(lure.y), riseT);
        strikeHeadY = Mathf.MoveTowards(strikeHeadY, targetY, strikeRiseSpeed * Time.deltaTime);
        ApplyStrikeHeadY();
    }

    // The lure nibble pass: same fast dart as a strike, but it drives THROUGH the lure to an
    // overshoot point instead of clamping on. It rises to brush the lure once (twitching it), then
    // settles back to swim depth and, on reaching the far point, returns to hovering — still a
    // chaser, so the next tug can re-roll. No grab, no reaction window.
    private void UpdateLureNibblePass()
    {
        Vector3 lure = bobberTransform.position;
        Vector3 lureFlat = new Vector3(lure.x, waterSurfaceY, lure.z);

        // Brush the lure once on the way through — a quick twitch, the lure's "a fish bumped it".
        Vector3 snout = modelVisual != null ? modelVisual.MouthWorldPosition : GetHeadPosition();
        if (!nibblePassBrushed && Vector3.Distance(snout, lure) < Mathf.Max(strikeLeapRange, nibbleRange))
        {
            nibblePassBrushed = true;
            bobberCtrl?.PlayNibbleWobble(GetFlatForward());
        }

        // Dart through the lure toward the overshoot point past it.
        SteerToward(nibblePassTarget, strikeSpeed, strikeTurnRate);
        dashDirection = GetFlatForward();

        // Up-and-over arc tied to PROGRESS so the tease's rise begins with the dart and matches a real
        // lure strike's buildup: ramp up to the lure over the approach (full by ~60%, hold to it),
        // then ease back down over the overshoot — a clean forward arc, never a drop in place, and no
        // early tell the player could read.
        float horiz = GetHorizontalDistance(GetHeadPosition(), lureFlat);
        float riseT = !nibblePassBrushed
            ? (strikeStartHoriz > 0.01f ? 1f - Mathf.Clamp01(horiz / (strikeStartHoriz * 0.6f)) : 1f)
            : Mathf.Clamp01(GetHorizontalDistance(GetHeadPosition(), nibblePassTarget) / Mathf.Max(0.2f, nibblePassDistance));
        float targetY = Mathf.Lerp(waterSurfaceY, HostYToPlaceSnoutAt(lure.y), riseT);
        strikeHeadY = Mathf.MoveTowards(strikeHeadY, targetY, strikeRiseSpeed * Time.deltaTime);
        ApplyStrikeHeadY();

        // Out the far side (or the safety cap lapsed if the overshoot was blocked) — drop back to
        // hovering, ready to be teased into another roll.
        nibblePassTimer -= Time.deltaTime;
        if (GetHorizontalDistance(GetHeadPosition(), nibblePassTarget) <= 0.3f || nibblePassTimer <= 0f)
        {
            nibblePass = false;
            strikeHeadY = waterSurfaceY;
            currentState = FishState.Attracted;
            weaveTimer = 0f;
            weaveOffset = Vector3.zero;
            attractPauseTimer = 0f;
        }
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

        // Keep swimming forward along the dash heading, carrying the lure. Unlike every other
        // moving state this never routed through SteerToward, so a grab towing the lure toward
        // shore/rocks had no obstacle check at all — hold position (still counting down the grab
        // timer toward ReleaseGrab/ConfirmGrab) rather than plow into terrain.
        Vector3 step = dashDirection * (grabForwardSpeed * Time.deltaTime);
        Vector3 newPos = ClampToBounds(transform.position + step);
        if (!IsObstacleAt(newPos))
        {
            transform.position = newPos;
        }
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
        // versa) — it neither schools around it nor gets recruited by the lure brain. An EMPTY hook
        // (no bait, not a lure) is also gated for species that won't bite baitless, so the visual
        // gather matches the bite gate — a baitless hook doesn't draw a curious crowd it can't catch.
        if (bobberTransform != null && !shouldAvoidBobber && reengageCooldown <= 0f && BaseAwarenessRadius > 0f
            && BobberInventory.PresetRespondsToEquippedTackle(preset)
            && !BaitInventory.EmptyHookRejects(preset))
        {
            float bobberDist = GetHorizontalDistance(transform.position, bobberTransform.position);
            if (bobberDist <= EffectiveActionRadius && IsWithinAwarenessCone(bobberTransform.position))
            {
                AttractToBobber(false);         // passive curiosity — never spooks itself for drifting close
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

        // Post-catch coast: the predator sails straight on along its lunge heading, shedding
        // speed exponentially, and hands over to normal wandering once it's back at cruise
        // pace. No steering — this is the follow-through of the charge, not a new decision.
        if (huntCoastSpeed > 0f)
        {
            huntCoastSpeed *= Mathf.Exp(-Mathf.Max(0.1f, huntCoastDecay) * Time.deltaTime);
            if (huntCoastSpeed <= swimSpeed)
            {
                huntCoastSpeed = 0f; // decayed to cruise pace — wandering takes over seamlessly
            }
            else
            {
                Vector3 coast = transform.position + GetFlatForward() * (huntCoastSpeed * Time.deltaTime);
                coast = ClampToBounds(coast);
                coast.y = waterSurfaceY;
                if (IsObstacleAt(coast))
                {
                    huntCoastSpeed = 0f; // shore dead ahead: drop the coast, steering resumes
                }
                else
                {
                    transform.position = coast;
                    return;
                }
            }
        }

        if (wanderPauseTimer > 0f)
        {
            wanderPauseTimer -= Time.deltaTime;

            if (isResting)
            {
                // Full rest: glide to a dead stop. restBlend ramps the speed down to zero (from
                // the arrive-glide speed, so the stop continues the arrival's deceleration
                // seamlessly) and softens the tail via GetSwayIntensity / the flap-amplitude
                // fade. NO steering here — the fish coasts dead straight along its heading and
                // then simply sits. Steering while nearly stationary (even toward a gentle drift
                // point) lets a sideways separation push rotate the fish in place, which reads
                // as tail-chasing; a resting fish holds its pose and only the sine wave moves.
                restBlend = Mathf.MoveTowards(restBlend, 1f, Time.deltaTime / Mathf.Max(0.05f, restEaseSeconds));
                float restSpeed = Mathf.Lerp(swimSpeed * 0.35f, 0f, restBlend);
                if (restSpeed > 0.01f)
                {
                    Vector3 coast = transform.position + GetFlatForward() * (restSpeed * Time.deltaTime);
                    coast = ClampToBounds(coast);
                    coast.y = waterSurfaceY;
                    if (!IsObstacleAt(coast)) transform.position = coast;
                }
                return;
            }

            // Drift pause: slow forward motion instead of a stop — and crowded pausers
            // gently spread apart.
            Vector3 driftTarget = GetHeadPosition() + GetFlatForward() * 2f
                                  + ComputeSeparation() * separationStrength
                                  + ComputeSchooling();
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
            // Roll whether this pause is a full rest (glide to a halt, tail winds down) or the
            // usual slow drift. Rests draw from their own, longer duration range.
            isResting = Random.value < restChance;
            wanderPauseTimer = isResting
                ? Random.Range(restDurationRange.x, restDurationRange.y)
                : Random.Range(wanderPauseMin, wanderPauseMax);
            return;
        }

        // Progress watchdog (see the field comment): a cruising fish closes metres per second,
        // so several seconds without ANY gain on the target means it's trapped on an orbit or a
        // detour that will never converge. Re-rolling perturbs the equilibrium — and a target
        // rolled near the shoal is one cohesion agrees with, so the mill breaks up. Reads as the
        // fish changing its mind rather than looping.
        if (dist < wanderBestDist - 0.05f)
        {
            wanderBestDist = dist;
            wanderNoProgressTimer = 0f;
        }
        else
        {
            wanderNoProgressTimer += Time.deltaTime;
            if (wanderNoProgressTimer > 4f)
            {
                hasWanderTarget = false;
                return;
            }
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
            && BobberInventory.PresetRespondsToEquippedTackle(preset)
            && !BaitInventory.EmptyHookRejects(preset))
        {
            float bobberDist = GetHorizontalDistance(transform.position, bobberTransform.position);
            if (bobberDist > naturalAttractionMinDist && IsWithinAwarenessCone(bobberTransform.position))
            {
                Vector3 bobberPos = bobberTransform.position;
                bobberPos.y = waterSurfaceY;
                moveTarget = Vector3.Lerp(wanderTarget, bobberPos, naturalAttraction);
            }
        }

        // Personal space: bend the path away from nearby schoolmates so fish don't phase
        // through each other while milling around.
        moveTarget += ComputeSeparation() * separationStrength;

        // Same-species shoaling: flagged species drift toward their own kind and fall into a
        // shared heading, so a pond holding several of one fish reads as a loose school.
        moveTarget += ComputeSchooling();

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

        // Waking from a rest: ease restBlend back down over the same ramp the glide-down used,
        // so the fish visibly picks its tail back up and accelerates off the stop instead of
        // launching at full cruise from a standstill.
        if (restBlend > 0f)
        {
            isResting = false;
            restBlend = Mathf.MoveTowards(restBlend, 0f, Time.deltaTime / Mathf.Max(0.05f, restEaseSeconds));
        }

        // Glide into the stop instead of swimming full tilt until the arrival check trips.
        float arriveGlide = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(dist / 0.8f));
        SteerToward(moveTarget, swimSpeed * arriveGlide * (1f - restBlend), wanderTurnRate);
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

        // Self-attracted followers drift back to wandering once the bobber leaves the awareness
        // radius — or once an empty hook can no longer interest them (e.g. bait removed mid-cast),
        // so a fish that was hovering leaves cleanly instead of lingering on a hook it won't bite.
        if (BaseAwarenessRadius > 0f && isFollower
            && (dist > EffectiveActionRadius || BaitInventory.EmptyHookRejects(preset)))
        {
            StopFollowing();
            return;
        }

        // Head-based: the steering converges the head onto the bobber, so the hand-off must
        // measure from there too — the centre trails half a body behind and may never come
        // within range on bigger fish. The lead stops approaching at nibbleStartRange (room to
        // circle) and the dash-and-pass nibble behavior takes over from there. Scaled by size so a
        // big fish begins orbiting farther out, matching its wider (size-scaled) circle radius.
        if (!isFollower && GetHorizontalDistance(GetHeadPosition(), bobberPos) < nibbleStartRange * NibbleSpaceScale)
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
            // Hesitating fish hover with a slow drift rather than freezing mid-water — and keep
            // their personal space, so a crowd pausing around the bobber doesn't overlap.
            SteerToward(GetHeadPosition() + GetFlatForward() * 2f + ComputeSeparation() * separationStrength,
                        pauseDriftSpeed * 0.5f, attractTurnRate * 0.25f);
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

        // Personal space around the tackle: followers ringing the bobber steer around each other
        // instead of phasing through. Fades out with the lead's commit ramp so its final run at
        // the bobber is never deflected by a schoolmate parked on the approach line.
        Vector3 targetPos = approachCenter + weaveOffset
                            + ComputeSeparation() * (separationStrength * (1f - commitFactor));
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
            }

            // Always steer (even when the probe above just deflected scareDirection) — SteerToward
            // does its own obstacle lookahead and gracefully holds/slides when boxed in. Skipping
            // this call on a blocked frame used to freeze the transform's rotation entirely while
            // scareDirection kept getting reflected underneath it (and the forced jink-timer reset
            // below re-randomized it again next frame) — that mismatch between "what's decided" and
            // "what's visibly turned" is what read as the fish's head spasming near a corner.
            // Separation keeps a school that bolts together from darting through each other.
            SteerToward(transform.position + scareDirection * 2f + ComputeSeparation() * separationStrength,
                        burstSpeed, scareTurnRate);
        }
        else if (scareTimer > 0f)
        {
            // Recovery tail: catch breath with a slow drift instead of freezing in place.
            SteerToward(GetHeadPosition() + GetFlatForward() * 2f + ComputeSeparation() * separationStrength,
                        pauseDriftSpeed, wanderTurnRate);
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
            // Carry the lunge's momentum through the catch: EndHunt drops back to Wandering,
            // where the post-catch coast bleeds this off exponentially instead of the predator
            // grinding to cruise pace the frame it touches the prey.
            huntCoastSpeed = huntSpeed;
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
        // Personal space minus the prey itself, FADED OUT over the final approach. Exempting only
        // the prey isn't enough: prey often swims inside a school, and its schoolmates' pushes
        // deflect the chase target off the whole clump — the predator hangs just outside
        // huntCatchRange circling the school's edge forever. Far out it weaves around bystanders;
        // inside ~2× catch range the chase wins and it barrels through the school to the prey.
        float sepFade = Mathf.Clamp01((dist - huntCatchRange) / Mathf.Max(0.5f, huntCatchRange));
        SteerToward(preyPos + ComputeSeparation(preyTarget) * (separationStrength * sepFade),
                    huntSpeed, huntTurnRate);
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
        nibble.Begin(bobberTransform, waterSurfaceY, preset, BuildNibbleSettings());

        // FishRipple also wants to hide visuals once the bite resolves. Subscribed here
        // (not in FishNibbleBehavior) because HideVisuals is a FishRipple concern. Remove first so
        // a fish that nibbles, misses, then nibbles again can't stack duplicate subscriptions.
        FishingEvents.OnFishBite -= OnFishBiteHide;
        FishingEvents.OnFishBite += OnFishBiteHide;
    }

    // After enough nibble passes the fish commits to the bite. The zone launches this with the
    // SAME grab callbacks the lure uses, so a bobber bite runs the identical dash → clamp → carry →
    // react-window flow: a fast 3D dash at the bobber, then it clamps on and tows the bobber along
    // (BeginGrabTow) so the bobber gets visibly dragged under and away — never a snap to a still
    // bobber. The reaction window is the lure's: react in time and the grab commits to a real bite
    // (ConfirmGrab → HookFish), miss it and the fish lets go and swims off (no cast-fail). State is
    // wired directly to bypass StartLureStrike's Nibbling guard.
    public void StartBobberBiteStrike(System.Action<FishRipple> onGrabStart, System.Action<FishRipple> onGrabReleased)
    {
        if (currentState != FishState.Nibbling) return;
        if (bobberTransform == null || bobberCtrl == null) return;
        nibble?.Stop();

        onGrabStartCallback = onGrabStart;
        onGrabReleasedCallback = onGrabReleased;
        isFollower = false;
        dashDirection = GetFlatForward();
        strikeHeadY = transform.position.y;
        // Ramp the leap's rise by progress from HERE (the run-up can be metres), so the jump starts
        // with the leap instead of waiting until the fish is almost on the bobber.
        strikeStartHoriz = GetHorizontalDistance(GetHeadPosition(), bobberTransform.position);
        strikeGiveUpTimer = StrikeGiveUpSeconds();
        currentState = FishState.Striking;
    }

    // True only on the nibbling lead once it has circled/dashed enough to commit. The zone polls
    // this to launch the bobber bite with the lure's grab-window callbacks.
    public bool NibbleReadyToBite =>
        currentState == FishState.Nibbling && nibble != null && nibble.ReadyToBite;

    // Bigger fish need a wider berth to carve the orbit and the dash; scale the orbit and the
    // ranges that ride with it (start/pass/touch) by size class. Speeds and the gap/count cadence
    // stay as authored — a big fish circling the same linear speed just laps lazier, which fits.
    private float NibbleSpaceScale =>
        SizeClassHelper.GetNibbleSpaceScale(preset != null ? preset.sizeClass : SizeClass.Medium);

    private FishNibbleBehavior.Settings BuildNibbleSettings()
    {
        float spaceScale = NibbleSpaceScale;
        return new FishNibbleBehavior.Settings
        {
            circleRadius = nibbleCircleRadius * spaceScale,
            circleSpeed = nibbleCircleSpeed,
            dashSpeed = nibbleDashSpeed,
            touchRadius = nibbleTouchRadius * spaceScale,
            passDistance = nibblePassDistance * spaceScale,
            nibbleGapRange = nibbleGapRange,
            nibblesBeforeBite = nibblesBeforeBite,
            turnRate = nibbleTurnRate,
            radiusJitter = nibbleRadiusJitter,
            wanderStrength = nibbleWanderStrength,
            dashRise = nibbleDashRise,
            // How far the snout sits below the host, so the dash can peak with the snout on the bobber
            // exactly like the bite leap (HostYToPlaceSnoutAt). Falls back to modelDepth if unmeasured.
            snoutDepth = modelVisual != null
                ? Mathf.Max(0f, transform.position.y - modelVisual.MouthWorldPosition.y)
                : modelDepth,
            obstacleLayers = obstacleLayers,
            obstacleRayHeight = obstacleRayHeight,
        };
    }

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
            float signedAngle = Vector3.SignedAngle(flatForward, toTarget, Vector3.up);
            if (targetDist < 2f * turnRadius)
            {
                turnDegPerSec *= Mathf.Min(2f * turnRadius / Mathf.Max(targetDist, 0.05f), 8f);

                // Even pivoting 8x harder can leave a VERY close, off-axis target inside the
                // turning circle — the fish then carves an endless tail-chasing orbit around it
                // (steering offsets like separation/schooling can hold such a point right beside
                // the fish indefinitely). Real fish brake into tight turns: the turning radius is
                // proportional to speed, so slow down until the circle fits and every target
                // becomes reachable. Only fires for genuinely off-axis targets — a close point
                // dead ahead needs no turn, so the approach isn't slowed.
                float boostedRadius = speed / Mathf.Max(turnDegPerSec * Mathf.Deg2Rad, 0.01f);
                if (targetDist < 2f * boostedRadius && Mathf.Abs(signedAngle) > 30f)
                    speed *= Mathf.Max(targetDist / (2f * boostedRadius), 0.15f);
            }
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
        BeginWanderLeg();
    }

    // Every fresh wander leg (random roll or a hand-authored target) re-arms the no-progress
    // watchdog alongside setting the target.
    private void BeginWanderLeg()
    {
        hasWanderTarget = true;
        wanderBestDist = float.MaxValue;
        wanderNoProgressTimer = 0f;
    }

    private Vector3 GetFlatForward()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
    }

    // Personal-space steering: a push away from schoolmates within separationRadius,
    // stronger the closer they are. Applied in every free-swimming state (wander, attracted
    // hover, flee, hunt) — only committed bite dashes (Nibbling/Striking/Grabbing) skip it so
    // contact ranges still land. `ignore` exempts one fish from the scan: a hunting predator
    // must never be pushed off its own prey. O(n²) over a zone's handful of fish is negligible.
    private Vector3 ComputeSeparation(FishRipple ignore = null)
    {
        if (school == null || separationStrength <= 0f) return Vector3.zero;

        Vector3 push = Vector3.zero;
        for (int i = 0; i < school.Count; i++)
        {
            FishRipple other = school[i];
            if (other == null || other == this || other == ignore) continue;
            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;
            float dist = away.magnitude;
            if (dist >= separationRadius || dist < 0.0001f) continue;
            push += away / dist * (1f - dist / separationRadius);
        }
        return push;
    }

    // Same-species shoaling: boids cohesion + alignment, layered on top of the all-fish
    // separation above. Only fires for species that opt in (preset.Schools) and only toward
    // calm, wandering fish of the SAME species within schoolPerceptionRadius — so a lone fish of
    // a schooling species, or one surrounded only by other species, just wanders. Returns a
    // world-space steering offset folded into the wander target, same convention as separation.
    private Vector3 ComputeSchooling()
    {
        if (school == null || preset == null || !preset.Schools) return Vector3.zero;
        if (schoolCohesionStrength <= 0f && schoolAlignmentStrength <= 0f) return Vector3.zero;

        float perceptionSqr = schoolPerceptionRadius * schoolPerceptionRadius;
        Vector3 centerSum = Vector3.zero;
        Vector3 headingSum = Vector3.zero;
        int neighbors = 0;

        for (int i = 0; i < school.Count; i++)
        {
            FishRipple other = school[i];
            if (other == null || other == this) continue;
            if (other.preset != preset) continue;                    // same species only
            if (other.currentState != FishState.Wandering) continue; // school with calm fish only

            Vector3 diff = other.transform.position - transform.position;
            diff.y = 0f;
            if (diff.sqrMagnitude > perceptionSqr) continue;

            centerSum += other.transform.position;
            headingSum += other.GetFlatForward();
            neighbors++;
        }

        if (neighbors == 0) return Vector3.zero;

        Vector3 steer = Vector3.zero;

        // Cohesion: steer toward the shoal's average position, PROPORTIONAL to how far this fish
        // has strayed (capped at perception). A fixed nudge is too weak to overcome the random
        // wander target, so a strayed fish would never rejoin — the distance scaling makes a
        // far fish commit hard to returning while one already in the shoal just mingles.
        if (schoolCohesionStrength > 0f)
        {
            Vector3 toCenter = (centerSum / neighbors) - transform.position;
            toCenter.y = 0f;
            float dist = toCenter.magnitude;
            if (dist > 0.0001f)
                steer += toCenter / dist * Mathf.Min(dist, schoolPerceptionRadius) * schoolCohesionStrength;
        }

        // Alignment: nudge toward the shoal's shared heading.
        if (schoolAlignmentStrength > 0f)
        {
            Vector3 avgHeading = headingSum / neighbors;
            avgHeading.y = 0f;
            if (avgHeading.sqrMagnitude > 0.0001f)
                steer += avgHeading.normalized * schoolAlignmentStrength;
        }

        return steer;
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
