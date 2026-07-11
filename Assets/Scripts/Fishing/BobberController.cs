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

    // (The old "5. Struggle Splash" fight particles are gone on purpose: the fish-control
    // fight has no particle effects — the feedback is the fish itself, the audio loops and
    // the rod bend.)

    [Header("Audio")]
    public AudioClip waterEntrySound;
    public float waterEntryVolumeScale = 1.0f;
    public AudioClip impactSound;

    [Header("Cast / Fight Audio")]
    [Tooltip("Looping fly-cast sound played the instant the bobber/lure is thrown, and stopped the " +
             "moment it hits the water. Drop the fly-fishing mp3 here.")]
    public AudioClip castFlySound;
    [Range(0f, 1f)] public float castFlyVolume = 1f;
    [Tooltip("Looping sound while a hooked fish is actively STRUGGLING (fighting the line).")]
    public AudioClip fishStruggleSound;
    [Range(0f, 1f)] public float fishStruggleVolume = 1f;
    [Tooltip("Looping sound while a fish is on the line but NOT struggling — i.e. it has bitten and is " +
             "hooked/resting/being reeled in without a fight.")]
    public AudioClip fishHookedIdleSound;
    [Range(0f, 1f)] public float fishHookedIdleVolume = 1f;

    [Header("In-Air Physics")]
    public float airTumbleTorque = 5f;

    [Header("In-Air Steering")]
    [Tooltip("Let the player nudge the bobber/lure mid-flight with the move stick / WASD (controls " +
             "are locked to the rod during a cast, so these axes are free). Steering is relative to " +
             "the flight direction: left/right curves the arc sideways, up/down carries it further " +
             "or drops it shorter. Works on both the spiral and plain ballistic flight paths.")]
    public bool airSteerEnabled = true;
    [Tooltip("Sideways steering strength (m/s² at full stick). ~8 over a typical 1s flight drifts " +
             "the landing spot a few metres — a nudge, not a guided missile.")]
    public float airSteerSideAccel = 8f;
    [Tooltip("Forward/back steering strength (m/s² at full stick). Push up to carry the cast " +
             "further, pull down to drop it short. The line's max-cast joint limit still caps " +
             "how far it can be extended.")]
    public float airSteerForwardAccel = 6f;

    [Tooltip("Extra downward force applied when in the air to prevent 'floatiness'. Increase this to make it fall faster.")]
    public float extraGravity = 30f; // --- NEW: Fix for "Moon Gravity" ---

    [Header("Cast Spiral")]
    [Tooltip("Corkscrew the bobber/lure through the air on a cast. The ballistic centreline (and " +
             "therefore the landing spot / arc) is preserved — the bobber just orbits that line as it " +
             "flies, and the Verlet rope trails the helix and straightens on landing on its own. " +
             "Turn off to fall back to the plain ballistic arc + random air tumble.")]
    public bool spiralOnCast = true;
    [Tooltip("Radius of the corkscrew, in metres. This is how far the bobber swings off the straight " +
             "flight line at the start of the cast.")]
    public float spiralRadius = 0.6f;
    [Tooltip("How many full revolutions the bobber makes around the flight line per second.")]
    public float spiralFrequency = 3f;
    [Tooltip("Seconds for the spiral to grow from zero to full radius right after launch, so it eases " +
             "out of the rod tip instead of snapping sideways.")]
    public float spiralRampInTime = 0.15f;
    [Tooltip("How fast the spiral radius decays over the flight (per-second e-folding). Higher = the " +
             "corkscrew tightens toward the centreline as it travels; 0 = holds full radius until it lands.")]
    [Range(0f, 4f)] public float spiralDecayRate = 0.4f;
    [Tooltip("How much the throw charge scales the spiral: 0 = same corkscrew on every cast, 1 = fully " +
             "proportional so a barely-charged flick barely spirals and a full charge gets the full radius.")]
    [Range(0f, 1f)] public float spiralChargeInfluence = 1f;
    [Tooltip("How fast (higher = snappier) the bobber swings into its trailing flight pose — line-attach " +
             "point pointing back toward the player, 'feet' leading into the throw, so it drags the line " +
             "after it. 0 leaves it whatever rotation it launched at.")]
    public float spiralOrientSpeed = 12f;

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
    [Tooltip("Downward impulse for the slight, brief wobble when a fish brushes past the bobber on a " +
             "nibble pass (the new dash-past behavior). Keep well below nibbleForce — it's a clip, not a jab.")]
    public float nibbleWobbleForce = 1.5f;
    [Tooltip("Peak tilt (degrees) kicked into the bobber/lure when a fish brushes it on a nibble dash. " +
             "A crisp rotational jiggle layered on top of the buoyancy settle, so the wobble reads " +
             "clearly even on a lure whose strong buoyancy quickly damps a plain positional dip.")]
    public float nibbleWobbleDeg = 12f;
    [Tooltip("How fast the nibble wobble dies down (higher = snappier).")]
    public float nibbleWobbleDecay = 7f;
    [Tooltip("Nibble wobble oscillation rate — how many times it tips back and forth as it decays.")]
    public float nibbleWobbleFrequency = 9f;

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
             "kept out of it), so breach height ≈ jumpUpSpeed²/(2·9.81) minus the fight submerge depth. " +
             "Kept low on purpose (RDR2-style): ~3.6 lifts only about a quarter of the fish clear of " +
             "the surface before it falls back. Pair it with jumpForwardSpeed so the pop reads as a " +
             "shallow diagonal arc, not a full vertical breach — raise for a bigger leap.")]
    public float jumpUpSpeed = 3.62f;
    [Tooltip("Horizontal speed (m/s) carried along the current struggle direction so the leap arcs sideways instead of straight up. Keep it close to jumpUpSpeed for the shallow, mostly-diagonal RDR2 arc.")]
    public float jumpForwardSpeed = 2.05f;
    [Tooltip("Minimum seconds between leaps.")]
    public float jumpCooldown = 5f;
    [Tooltip("Safety net: if the fish hasn't splashed back down after this many seconds (e.g. the leap never broke the surface), fight state is restored anyway.")]
    public float maxJumpDuration = 2.5f;
    [Tooltip("Speeds up the whole leap without changing its shape: the launch velocity is scaled by this and matching extra gravity is added, so the fish pops up and splashes back faster while reaching the same height and landing spot. 1 = unchanged, 1.2 = ~20% faster.")]
    [Range(1f, 2f)] public float jumpSpeedMultiplier = 1.1f;

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
    // Dedicated looping sources so the cast whir and the fight loop can start/stop cleanly without
    // disturbing the one-shot splash/impact playback on the main audioSource. Added in Awake.
    private AudioSource castLoopSource;
    private AudioSource fightLoopSource;
    private bool isInWater = false;
    private bool isInFlight = false;
    private bool hasSplashed = false;
    private bool hasPlayedSplashSound = false;
    private bool hasPlayedImpactSound = false;
    private float initialLinearDamping; // Unity 6 Name
    private float initialAngularDamping;
    private float waterSurfaceY;

    // Cast-spiral state. The bobber is steered along a ballistic guide (integrated here so the
    // centreline keeps its old arc) plus a rotating perpendicular offset. The launch velocity is
    // handed in by FishingLine (SetCastLaunchVelocity) rather than read from the rigidbody: an
    // AddForce(VelocityChange) is only applied during the physics step AFTER FixedUpdate, so
    // rb.linearVelocity is still zero when the first flight FixedUpdate runs. Cleared in ResetForCast.
    private bool spiralInitialized;
    private float spiralTime;
    private Vector3 spiralFwd, spiralAxisA, spiralAxisB;
    private Vector3 spiralGuidePos, spiralGuideVel;
    private Vector3 pendingLaunchVelocity;
    private float pendingCastCharge = 1f; // normalized throw charge, scales the path spiral

    // True when the cast spiral will actively drive flight, so FishingLine hands us the launch
    // velocity to steer with instead of throwing the bobber with a physics impulse.
    public bool SpiralDrivesFlight => spiralOnCast && spiralRadius > 0f;

    // Called by FishingLine right after it computes the throw. Seeds the spiral guide's initial
    // velocity so the corkscrew centreline matches the intended cast.
    public void SetCastLaunchVelocity(Vector3 velocity) { pendingLaunchVelocity = velocity; }

    // Normalized (0..1) throw charge for this cast, so a weak flick spirals less than a full throw.
    public void SetCastCharge(float normalized) { pendingCastCharge = Mathf.Clamp01(normalized); }

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

    // Set by FishFightHandler while the hooked fish is in a fighting burst. Drives the
    // struggle audio loop and the hooked visual's thrash WITHOUT engaging the old
    // self-swimming struggle forces (isStruggling/UpdateStruggleMovement), which the
    // fish-control fight replaces — those stay dormant during a fight.
    [System.NonSerialized] public bool fightBurst;

    // Strike heading hint: the world yaw (deg) the real zone fish was dashing in when it took
    // the tackle. FishingZone records it just before the hook lands (bite-imminent on the
    // bobber path, the grab commit on the lure path); the hooked silhouette then spawns facing
    // this, so the bite dash flows straight into the fight with no direction snap. Cleared
    // each cast.
    private float strikeYaw;
    private bool hasStrikeYaw;
    public bool HasStrikeHeading => hasStrikeYaw;
    public float StrikeHeadingYaw => strikeYaw;
    public void SetStrikeHeading(float yawDegrees) { strikeYaw = yawDegrees; hasStrikeYaw = true; }

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

    // Popper surface chatter (set by LureReelHandler while a Popper lure floats). isPopperLure
    // routes ApplyBuoyancy to apply popperBounceDeg — a fast nose pitch oscillation — DIRECTLY
    // instead of through the buoyancy slerp, which would smooth the high-frequency pop away.
    [System.NonSerialized] public bool isPopperLure = false;
    [System.NonSerialized] public float popperBounceDeg = 0f;
    private float popperLeanPitch; // smoothed speedboat lean for the popper path (kept clean of the bounce)

    // Transient nibble wobble: a decaying tilt applied on top of the buoyancy rotation when a fish
    // brushes the bobber/lure, so the touch reads as a visible jiggle even when buoyancy would damp
    // a positional dip away (lures float stiffly). Set by PlayNibbleWobble, spent in ApplyBuoyancy.
    private float nibbleWobbleAmp;
    private float nibbleWobblePhase;

    [Header("Popper Splash")]
    [Tooltip("Splash particle spawned repeatedly at the popper's nose (LineAttachPoint) while it's " +
             "being tugged/reeled. Leave empty to reuse the Nibble Splash prefab. Only used by Popper-style lures.")]
    public GameObject popperSplashPrefab;
    [Tooltip("How long each popper splash instance lasts.")]
    public float popperSplashLifetime = 1.0f;

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

        castLoopSource = CreateLoopSource();
        fightLoopSource = CreateLoopSource();
    }

    // Builds a looping AudioSource that mirrors the main source's 3D/mixer settings so the extra
    // loops attenuate with distance the same way, without needing to be authored on the prefab.
    private AudioSource CreateLoopSource()
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = true;
        if (audioSource != null)
        {
            s.spatialBlend = audioSource.spatialBlend;
            s.rolloffMode = audioSource.rolloffMode;
            s.minDistance = audioSource.minDistance;
            s.maxDistance = audioSource.maxDistance;
            s.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
        }
        return s;
    }

    private void PlayCastFly()
    {
        if (castLoopSource == null || castFlySound == null) return;
        castLoopSource.clip = castFlySound;
        castLoopSource.volume = castFlyVolume;
        castLoopSource.Play();
    }

    private void StopCastFly()
    {
        if (castLoopSource != null && castLoopSource.isPlaying) castLoopSource.Stop();
    }

    // The catch is decided — silence the struggle/tension loops right away. This component is
    // disabled through the catch reel + hang showcase (so Update can't drive UpdateFightAudio),
    // and ResetForCast — which normally stops the loops — is deferred until the showcase ends,
    // which left the reeling/tension loop playing over the whole fish presentation.
    public void StopFightAudioLoops()
    {
        StopCastFly();
        if (fightLoopSource != null)
        {
            if (fightLoopSource.isPlaying) fightLoopSource.Stop();
            fightLoopSource.clip = null;
        }
    }

    // Keeps the fight loop in sync with the bobber's state: struggle clip while the hooked fish
    // fights, the calmer hooked clip while it's on the line but resting/being reeled, silence
    // otherwise. Driven every frame from Update so it follows the struggle toggling for free.
    private void UpdateFightAudio()
    {
        if (fightLoopSource == null) return;

        AudioClip desired = null;
        float volume = 1f;
        if (hookedFish != null)
        {
            if (isStruggling || fightBurst) { desired = fishStruggleSound; volume = fishStruggleVolume; }
            else { desired = fishHookedIdleSound; volume = fishHookedIdleVolume; }
        }

        if (desired == null)
        {
            if (fightLoopSource.isPlaying) fightLoopSource.Stop();
            fightLoopSource.clip = null;
            return;
        }

        fightLoopSource.volume = volume;
        if (fightLoopSource.clip != desired)
        {
            fightLoopSource.clip = desired;
            fightLoopSource.Play();
        }
        else if (!fightLoopSource.isPlaying)
        {
            fightLoopSource.Play();
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

        UpdateFightAudio();
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

        // Time-warp the leap: extra gravity matched to the launch-velocity scale (jumpSpeedMultiplier)
        // so the arc keeps its exact shape/height/landing spot but plays out faster. Scaling velocity
        // by s and gravity by s² replays the same trajectory s× quicker, so extra = (s²-1)*g. Buoyancy
        // is inert above bite depth during a leap, so this is the only vertical force shaping the arc.
        if (jumpActive && jumpSpeedMultiplier > 1f && rb != null && !rb.isKinematic)
        {
            float extraJumpGravity = (jumpSpeedMultiplier * jumpSpeedMultiplier - 1f) * Physics.gravity.magnitude;
            rb.AddForce(Vector3.down * extraJumpGravity, ForceMode.Acceleration);
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
                if (spiralOnCast && spiralRadius > 0f)
                {
                    ApplyCastSpiral();
                }
                else
                {
                    rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
                    rb.AddForce(ReadAirSteerAccel(rb.linearVelocity), ForceMode.Acceleration);
                }
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
        // The fly-cast whir ends the instant the bobber touches water, regardless of what happens next.
        StopCastFly();

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
            // Splash-down from a fight jump: dive back to the fight depth with its own sound
            // (no particle — the fight is effect-free), and do NOT re-announce the landing —
            // FishingZone's bite brain would start a second bite cycle on a bobber that
            // already has a fish on the line.
            isSubmerged = true;
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

        // Decaying nibble wobble: a quick oscillating tilt layered on top of the buoyancy rotation
        // so a fish brushing the bobber/lure visibly jiggles it. Advanced/decayed here (FixedUpdate)
        // where the rotation is actually written.
        float wobble = 0f;
        if (nibbleWobbleAmp > 0.05f)
        {
            nibbleWobblePhase += nibbleWobbleFrequency * Time.fixedDeltaTime * Mathf.PI * 2f;
            wobble = nibbleWobbleAmp * Mathf.Sin(nibbleWobblePhase);
            nibbleWobbleAmp *= Mathf.Exp(-nibbleWobbleDecay * Time.fixedDeltaTime);
        }

        // Pitch X is driven by lureVisualPitchDeg (0 = level, positive = LineAttachPoint side
        // rises). Heading Y is preserved; roll Z is held at 0.
        if (isPopperLure && !isSubmerged)
        {
            // Popper: smooth the slow lean ourselves (kept clean of the bounce), then add the fast
            // nose chatter on top and apply it directly — a slerp at waterRotationSpeed would damp
            // the high-frequency pop into nothing. Heading/roll handled exactly as the slerp path.
            popperLeanPitch = Mathf.Lerp(popperLeanPitch, lureVisualPitchDeg,
                                         1f - Mathf.Exp(-waterRotationSpeed * Time.fixedDeltaTime));
            Quaternion popRotation =
                Quaternion.Euler(popperLeanPitch + popperBounceDeg + wobble, rb.rotation.eulerAngles.y, wobble * 0.5f);
            rb.MoveRotation(popRotation);
        }
        else
        {
            Quaternion targetRotation = Quaternion.Euler(lureVisualPitchDeg, rb.rotation.eulerAngles.y, 0);
            Quaternion newRotation = Quaternion.Slerp(rb.rotation, targetRotation, waterRotationSpeed * Time.fixedDeltaTime);
            // Layer the wobble on AFTER the slerp (in the bobber's local space) so the crisp jiggle
            // isn't smoothed away by the buoyancy slerp toward the upright target.
            if (Mathf.Abs(wobble) > 0.01f)
                newRotation *= Quaternion.Euler(wobble, 0f, wobble * 0.5f);
            rb.MoveRotation(newRotation);
        }
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
            TryFightJump(struggleDirection);
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

    // Rolled once per fight burst: occasionally the hooked fish leaps clean out of the water,
    // arcing along horizontalDir (the direction it is currently swimming). Only the heavy
    // in-water damping comes off so the launch velocity carries up cleanly — buoyancy is
    // deliberately left targeting the bite depth (NOT the surface), so it adds no upward boost
    // and the breach height is governed solely by jumpUpSpeed. (Aiming buoyancy at the surface
    // here was what sent the fish way too high.) The trigger exit/enter pair handles the splash,
    // and OnHookedFishJumpChanged lets the rope go slack mid-air. Public so the fish-control
    // fight (FishFightHandler) can trigger it at the top of a burst; the roll/cooldown and all
    // the leap tuning stay on this component.
    public void TryFightJump(Vector3 horizontalDir)
    {
        if (jumpActive || fightJumpChance <= 0f || hookedFish == null) return;
        if (rb == null || rb.isKinematic) return;
        if (Time.time < nextJumpAllowedTime) return;
        if (Random.value > fightJumpChance) return;

        jumpActive = true;
        jumpStartTime = Time.time;
        nextJumpAllowedTime = Time.time + jumpCooldown;

        rb.linearDamping = initialLinearDamping;

        horizontalDir.y = 0f;
        if (horizontalDir.sqrMagnitude > 0.0001f) horizontalDir.Normalize();
        else horizontalDir = Vector3.zero;
        Vector3 horizontal = horizontalDir * jumpForwardSpeed;
        // Whole launch scaled together so the arc shape is preserved; the matching extra gravity in
        // FixedUpdate then replays that same arc faster (see jumpSpeedMultiplier).
        rb.linearVelocity = new Vector3(horizontal.x, jumpUpSpeed, horizontal.z) * jumpSpeedMultiplier;

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

    // Consume one unit of the equipped bait — the fish has taken it, whether on a successful hook
    // or when it gets away with it on a missed bobber bite. No-op for lures (no bait selected).
    public void ConsumeEquippedBait()
    {
        if (BaitInventory.Instance == null) return;
        BaitItem equipped = BaitInventory.Instance.SelectedBait;
        if (equipped == null) return;
        if (!BaitInventory.Instance.TryConsume(equipped, 1))
            Debug.LogWarning($"[BobberController] Tried to consume {equipped.displayName} but the player had none.");
    }

    public void HookFish(FishPreset fishPreset)
    {
        if (hookedFish != null) return;

        ConsumeEquippedBait();

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

    // A fish brushed the bobber while darting past on a nibble: a single small downward tap so the
    // bobber dips and settles quickly — a slight, short wobble, not the old in-place jab.
    // fishForward adds a slight lateral nudge matching the fish's swim direction so the dip reads
    // as a physical brush rather than a straight-down jab.
    public void PlayNibbleWobble(Vector3 fishForward)
    {
        if (rb != null && isInWater && !rb.isKinematic)
        {
            Vector3 impulse = Vector3.down * nibbleWobbleForce + fishForward * (nibbleWobbleForce * 0.25f);
            rb.AddForce(impulse, ForceMode.Impulse);
        }
        // Crisp visible tilt on top of the dip — reads clearly on a stiffly-floating lure too.
        nibbleWobbleAmp = nibbleWobbleDeg;
        nibbleWobblePhase = 0f;
        SpawnEffect(nibblePrefab, nibbleLifetime);
        FishingEvents.OnFishNibble?.Invoke(this);
    }

    // Spawns one splash at the popper's nose (the LineAttachPoint, i.e. the chattering front),
    // riding the water surface. Called on a cadence by LureReelHandler while a Popper lure is being
    // tugged/reeled. Falls back to the nibble splash when no dedicated popper splash is assigned.
    public void PlayPopperSplash()
    {
        if (!isInWater) return;
        GameObject prefab = popperSplashPrefab != null ? popperSplashPrefab : nibblePrefab;
        if (prefab == null) return;

        Transform nose = LineAttachPoint;
        Vector3 spawnPos = new Vector3(nose.position.x, waterSurfaceY, nose.position.z);
        GameObject instance = Instantiate(prefab, spawnPos, prefab.transform.rotation);
        Destroy(instance, popperSplashLifetime);
    }

    public void SetStruggleActive(bool active)
    {
        if (isStruggling == active) return;

        isStruggling = active;
        if (active) struggleTimer = 0;
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

        // Silence the cast whir and any fight loop — a fresh cast/park starts from quiet.
        StopCastFly();
        if (fightLoopSource != null && fightLoopSource.isPlaying) fightLoopSource.Stop();

        // If the fight ended mid-leap, tell the rope the fish is down so it doesn't stay slack.
        EndFightJump();

        isInWater = false;
        waterVolumes.Clear();
        isInFlight = false;
        spiralInitialized = false;
        pendingLaunchVelocity = Vector3.zero;
        pendingCastCharge = 1f;
        isSubmerged = false;
        hasSplashed = false;
        hasPlayedSplashSound = false;
        hasPlayedImpactSound = false;
        isStruggling = false;
        fightBurst = false;
        hasStrikeYaw = false;
        hasStruggleAnchor = false;
        hookedFish = null;
        isGrabTowed = false;
        lureVisualPitchDeg = 0f;
        lureBobOffset = 0f;
        isPopperLure = false;
        popperBounceDeg = 0f;
        popperLeanPitch = 0f;
        nibbleWobbleAmp = 0f;
        nibbleWobblePhase = 0f;

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

    // Mid-flight steering acceleration from the move axes, expressed relative to the current
    // horizontal travel direction (stick left = curve left across the arc, stick up = carry
    // further). Reads input state directly — GetAxisRaw and GamepadInput.Move are level-based,
    // so sampling them in FixedUpdate is safe (same pattern as FishingRodController.TickLure).
    private Vector3 ReadAirSteerAccel(Vector3 travelVelocity)
    {
        if (!airSteerEnabled) return Vector3.zero;
        if (NoteMenu.IsNotebookOpen || Time.timeScale == 0f) return Vector3.zero;

        float side = Mathf.Clamp(Input.GetAxisRaw("Horizontal") + GamepadInput.Move.x, -1f, 1f);
        float fore = Mathf.Clamp(Input.GetAxisRaw("Vertical") + GamepadInput.Move.y, -1f, 1f);
        if (Mathf.Abs(side) < 0.01f && Mathf.Abs(fore) < 0.01f) return Vector3.zero;

        Vector3 fwd = travelVelocity;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) return Vector3.zero;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        return right * (side * airSteerSideAccel) + fwd * (fore * airSteerForwardAccel);
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
        PlayCastFly();
    }

    // Corkscrew the bobber through the air. We integrate a ballistic "guide" position with the same
    // total gravity the free bobber would feel (so the centreline arc — and thus the landing spot —
    // is unchanged), then steer the rigidbody to guide + a rotating offset perpendicular to the
    // flight line. Steering by velocity toward the target from the ACTUAL position each step keeps
    // it self-correcting (Unity's own useGravity add-on gets absorbed) and still swept, so the water
    // trigger fires normally. The Verlet rope tracks the bobber's attach point, so it trails the
    // helix for free and straightens on landing via its existing landed-tighten path.
    private void ApplyCastSpiral()
    {
        float dt = Time.fixedDeltaTime;

        if (!spiralInitialized)
        {
            spiralGuidePos = rb.position;
            // Use the velocity FishingLine handed us — NOT rb.linearVelocity, which the launch
            // impulse hasn't populated yet on this first FixedUpdate (see field comment).
            spiralGuideVel = pendingLaunchVelocity.sqrMagnitude > 0.0001f ? pendingLaunchVelocity : rb.linearVelocity;
            spiralFwd = spiralGuideVel.sqrMagnitude > 0.0001f ? spiralGuideVel.normalized : transform.forward;
            spiralAxisA = Vector3.Cross(spiralFwd, Vector3.up);
            if (spiralAxisA.sqrMagnitude < 0.0001f) spiralAxisA = Vector3.Cross(spiralFwd, Vector3.right);
            spiralAxisA.Normalize();
            spiralAxisB = Vector3.Cross(spiralFwd, spiralAxisA).normalized;
            spiralTime = 0f;
            spiralInitialized = true;
        }

        Vector3 gTotal = Physics.gravity + Vector3.down * extraGravity;
        spiralGuideVel += gTotal * dt;
        // Mid-air steering bends the ballistic centreline itself, so the corkscrew and the Verlet
        // rope ride along with the curved arc for free.
        spiralGuideVel += ReadAirSteerAccel(spiralGuideVel) * dt;
        spiralGuidePos += spiralGuideVel * dt;
        spiralTime += dt;

        float ramp = spiralRampInTime > 0f ? Mathf.Clamp01(spiralTime / spiralRampInTime) : 1f;
        float chargeScale = Mathf.Lerp(1f, pendingCastCharge, spiralChargeInfluence);
        float amp = spiralRadius * chargeScale * ramp * Mathf.Exp(-spiralDecayRate * spiralTime);
        float theta = spiralTime * spiralFrequency * Mathf.PI * 2f;
        Vector3 offset = amp * (Mathf.Cos(theta) * spiralAxisA + Mathf.Sin(theta) * spiralAxisB);

        Vector3 target = spiralGuidePos + offset;
        rb.linearVelocity = (target - rb.position) / dt;

        // Hold a trailing flight pose: line-attach point back toward the player, feet leading, so the
        // bobber drags the line after it instead of tumbling randomly.
        OrientTrailingAlong(-spiralFwd, dt);
    }

    // Rotate the bobber so its line-attach axis points along attachWorldDir (during a cast this is the
    // direction back toward the player), with a roll lock so a consistent side stays up rather than
    // spinning free. Same fixed-geometry trick as DangleRestRotation, aimed along flight instead of up.
    private void OrientTrailingAlong(Vector3 attachWorldDir, float dt)
    {
        if (rb == null || spiralOrientSpeed <= 0f) return;

        Transform attach = LineAttachPoint;
        if (attach == null || attach == transform) return;
        if (attachWorldDir.sqrMagnitude < 0.0001f) return;
        attachWorldDir.Normalize();

        // Local (rotation-invariant) direction from the bobber origin to its line-attach point.
        Vector3 localAttachDir = transform.InverseTransformPoint(attach.position);
        if (localAttachDir.sqrMagnitude < 0.0001f) return;
        localAttachDir.Normalize();

        // 1) Aim the attach axis backward along the flight line.
        Quaternion rot = Quaternion.FromToRotation(localAttachDir, attachWorldDir);

        // 2) Lock the free spin about that axis so a fixed side faces up.
        Vector3 localSide = Vector3.ProjectOnPlane(Vector3.up, localAttachDir);
        if (localSide.sqrMagnitude < 0.0001f) localSide = Vector3.ProjectOnPlane(Vector3.forward, localAttachDir);
        if (localSide.sqrMagnitude > 0.0001f)
        {
            Vector3 sideNow = Vector3.ProjectOnPlane(rot * localSide.normalized, attachWorldDir);
            Vector3 upFlat = Vector3.ProjectOnPlane(Vector3.up, attachWorldDir);
            if (sideNow.sqrMagnitude > 0.0001f && upFlat.sqrMagnitude > 0.0001f)
                rot = Quaternion.FromToRotation(sideNow.normalized, upFlat.normalized) * rot;
        }

        Quaternion newRotation = Quaternion.Slerp(rb.rotation, rot, spiralOrientSpeed * dt);
        rb.MoveRotation(newRotation);
    }

    void OnDestroy()
    {
        StopAllCoroutines();
        if (activeWakeInstance != null) Destroy(activeWakeInstance);
    }
}