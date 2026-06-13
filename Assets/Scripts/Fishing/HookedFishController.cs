using UnityEngine;

// The fish on the line: spawned by BobberController.HookFish the moment a bite lands (the
// bait and lure paths both converge there). The bobber/lure visuals are hidden so the line
// runs straight to the fish's mouth, and the body uses the same trailing-chain + sine sway
// as the swimming silhouettes — the head is pinned to the hook point, so every struggle of
// the bobber and every reel drag whips the body around emergently.
//
// Lifecycle: parented under the bobber, and registered as the bobber's activeFishModel, so
// every existing cleanup path (catch completed, escape, cancel, bobber destroyed) disposes
// of it without new wiring.
public class HookedFishController : MonoBehaviour
{
    private const float HookedModelDepth = 0.06f;
    private const float ThrashYawAmplitude = 38f;
    private const float ThrashFrequency = 1.7f;
    private const float AwayFaceTurnRate = 360f; // deg/sec the base heading tracks away-from-player
    private const float SweepAmplitude = 35f;    // slow side-to-side hunt for an escape angle
    private const float SweepFrequency = 0.25f;  // sweep cycles per second
    private const float BodyStraightenRate = 3.5f; // taut, straining body — not a relaxed trail
    private const float JumpThrashFreqMul = 2.6f;   // mid-leap the tail wrenches this much faster
    private const float JumpThrashAmpMul = 1.7f;    // ...and this much wider, for an airborne flail

    // Escape swim-off when the fish gets away: burst away from the player, diving and
    // fading out, then despawn for good.
    private const float EscapeLinger = 1f;   // beat before it bolts (a second after a failed fight)
    private const float EscapeDuration = 1.8f;
    private const float EscapeSpeed = 2.6f;
    private const float EscapeDiveDepth = 0.35f;

    private BobberController bobber;
    private FishModelVisual modelVisual;
    private float thrashPhase;
    private float sweepPhase;
    private float baseYaw;
    private float intensityTimer;
    private float currentIntensity = 2.2f;

    // The rope ends at the bobber's line-attach transform; while hooked we park it on the
    // fish's mouth (front-most mesh point) and restore its original pose on release.
    private Transform lineAttach;
    private Vector3 lineAttachLocalPos;
    private Quaternion lineAttachLocalRot;
    private bool hasLineAttach;

    private bool escaping;
    private Vector3 escapeDir;
    private float escapeTimer;
    private float escapeWaterY;

    // True while the player is actively reeling — during fight calm phases or the final
    // reel-in. A reeled fish resists hard; an unreeled, unstruggling fish stays calm.
    private bool isReeling;

    private void OnEnable()
    {
        FishingEvents.OnStartReelingDuringFight += HandleReelStart;
        FishingEvents.OnStopReelingDuringFight += HandleReelStop;
        FishingEvents.OnStartReeling += HandleReelStart;
    }

    private void OnDisable()
    {
        FishingEvents.OnStartReelingDuringFight -= HandleReelStart;
        FishingEvents.OnStopReelingDuringFight -= HandleReelStop;
        FishingEvents.OnStartReeling -= HandleReelStart;
    }

    private void HandleReelStart() => isReeling = true;
    private void HandleReelStop() => isReeling = false;

    public static HookedFishController Attach(BobberController bobber, FishPreset preset,
                                              Material silhouetteMaterial)
    {
        GameObject go = new GameObject("HookedFish");
        go.transform.SetParent(bobber.transform, false);
        go.transform.position = bobber.transform.position;

        HookedFishController controller = go.AddComponent<HookedFishController>();
        controller.bobber = bobber;
        controller.baseYaw = Random.Range(0f, 360f);
        controller.thrashPhase = Random.Range(0f, Mathf.PI * 2f);
        controller.sweepPhase = Random.Range(0f, Mathf.PI * 2f);
        go.transform.rotation = Quaternion.Euler(0f, controller.baseYaw, 0f);

        controller.modelVisual = new FishModelVisual(go.transform);
        controller.modelVisual.Spawn(preset, HookedModelDepth, 1f,
                                     new Vector3(0f, 180f, 0f), silhouetteMaterial);

        controller.lineAttach = bobber.LineAttachTransform;
        if (controller.lineAttach != null)
        {
            controller.lineAttachLocalPos = controller.lineAttach.localPosition;
            controller.lineAttachLocalRot = controller.lineAttach.localRotation;
            controller.hasLineAttach = true;
        }
        return controller;
    }

    // The fish got away: let go of the line and bobber, then swim off, dive and fade out.
    // Called by BobberController before the persistent bobber is parked back on the rod —
    // the visual unparents itself so the park teleport doesn't drag it along mid-getaway.
    public void BeginEscape()
    {
        if (escaping) return;
        escaping = true;
        escapeTimer = 0f;
        escapeWaterY = bobber != null && bobber.IsInWater ? bobber.WaterSurfaceY : transform.position.y;

        // Swim off the way it was already headed (the away-from-player angle it held while
        // fighting) rather than snapping to a fresh flee direction — reads as the fish simply
        // carrying on once it shakes the hook.
        escapeDir = Quaternion.Euler(0f, baseYaw, 0f) * Vector3.forward;
        escapeDir.y = 0f;
        escapeDir = escapeDir.sqrMagnitude < 0.001f
            ? new Vector3(Random.value - 0.5f, 0f, Random.value - 0.5f).normalized
            : escapeDir.normalized;

        // Give the line back to the bobber before letting go of it.
        if (hasLineAttach && lineAttach != null)
        {
            lineAttach.localPosition = lineAttachLocalPos;
            lineAttach.localRotation = lineAttachLocalRot;
            hasLineAttach = false;
        }
        transform.SetParent(null, true);
        bobber = null;
    }

    // LateUpdate so the fish follows wherever the bobber's physics/reel moved it this frame.
    private void LateUpdate()
    {
        if (escaping)
        {
            UpdateEscape();
            return;
        }

        if (bobber == null)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 hook = bobber.transform.position;
        // In the water the fish thrashes at the surface; once the catch is yanked out OR the
        // fish is mid-leap, it follows the bobber's real height through the air. (A low writhe
        // may not fully clear the water trigger, so we can't rely on IsInWater alone to lift it.)
        if (bobber.IsInWater && !bobber.IsHookedFishJumping) hook.y = bobber.WaterSurfaceY;

        // The fish fights the line facing AWAY from the player — pulling against the hook,
        // and dragged tail-first when the player reels. The thrash wrenches around that
        // base heading.
        Transform player = bobber.PlayerTransform != null
            ? bobber.PlayerTransform
            : (Camera.main != null ? Camera.main.transform : null);
        if (player != null)
        {
            Vector3 away = transform.position - player.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.001f)
            {
                float awayYaw = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
                baseYaw = Mathf.MoveTowardsAngle(baseYaw, awayYaw, AwayFaceTurnRate * Time.deltaTime);
            }
        }

        // Tail effort follows the fight state: a fish mid-leap flails hardest, a struggling fish
        // thrashes in frantic bursts, a reeled fish resists hard and steady (dragged backward,
        // still fighting), and a fish that's neither holds its away-angle calmly with a slow tail.
        // FishModelVisual smooths the transitions, so the states blend instead of popping.
        bool jumping = bobber.IsHookedFishJumping;
        if (jumping)
        {
            currentIntensity = 3f;
            intensityTimer = 0f;
        }
        else if (bobber.IsStruggling)
        {
            intensityTimer -= Time.deltaTime;
            if (intensityTimer <= 0f)
            {
                currentIntensity = Random.Range(2.4f, 3f);
                intensityTimer = Random.Range(0.4f, 0.9f);
            }
        }
        else if (isReeling)
        {
            currentIntensity = 2.4f;
            intensityTimer = 0f;
        }
        else
        {
            currentIntensity = 1f;
            intensityTimer = 0f;
        }

        // The body's vertical follow is automatic now: the head (host) rides the bobber's
        // airborne height during a leap, and the trailing-chain rope arches behind it on its own
        // (and flattens back as the head settles). No freedom ramp needed.

        // The pull-away pose: the heading is pinned away from the player at all times. On
        // top of it, a slow sweep hunts side to side for an escape angle, and the fast
        // thrash wrench scales with effort — a calm fish holds its angle with barely a
        // tremor, a struggling one wrenches violently. Mid-leap the wrench cranks up (faster
        // and wider) so the airborne fish visibly flails instead of arcing stiffly.
        float thrashFreqMul = jumping ? JumpThrashFreqMul : 1f;
        float thrashAmpMul = jumping ? JumpThrashAmpMul : 1f;
        sweepPhase += Time.deltaTime * SweepFrequency * Mathf.PI * 2f;
        thrashPhase += Time.deltaTime * ThrashFrequency * thrashFreqMul * Mathf.PI * 2f * (currentIntensity * 0.5f);
        float thrashScale = Mathf.Clamp(Mathf.InverseLerp(1.1f, 2.6f, currentIntensity), 0.08f, 1f);
        float yaw = baseYaw
                  + Mathf.Sin(sweepPhase) * SweepAmplitude
                  + Mathf.Sin(thrashPhase) * ThrashYawAmplitude * thrashAmpMul * thrashScale;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // Mouth on the hook: the host sits one head-offset behind the hook point so the
        // chain's pinned head lands exactly on the line's end.
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
        float headOffset = modelVisual != null ? modelVisual.HeadForwardOffset : 0f;
        transform.position = hook - forward * headOffset;

        modelVisual?.Tick(Time.deltaTime, currentIntensity, BodyStraightenRate);

        // Park the rope's end on the rendered snout, wherever the chain put it this frame.
        if (hasLineAttach && lineAttach != null && modelVisual != null)
            lineAttach.position = modelVisual.MouthWorldPosition;
    }

    // Burst away from the player while diving into the murk and fading out — then gone,
    // leaving the zone's headcount free for a fresh spawn.
    private void UpdateEscape()
    {
        escapeTimer += Time.deltaTime;

        // Beat before the bolt: for the first EscapeLinger seconds the freed fish just hangs there
        // thrashing — so after a failed fight it lingers a moment before swimming off (a second
        // later), instead of vanishing the instant it shakes the hook.
        if (escapeTimer < EscapeLinger)
        {
            transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(escapeDir.x, escapeDir.z) * Mathf.Rad2Deg, 0f);
            modelVisual?.Tick(Time.deltaTime, 2.6f, BodyStraightenRate);
            return;
        }

        float t = (escapeTimer - EscapeLinger) / EscapeDuration;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(escapeDir.x, escapeDir.z) * Mathf.Rad2Deg, 0f);
        Vector3 pos = transform.position + escapeDir * (EscapeSpeed * Time.deltaTime);
        pos.y = escapeWaterY - EscapeDiveDepth * t;
        transform.position = pos;

        modelVisual?.Tick(Time.deltaTime, 2.6f, BodyStraightenRate);
        modelVisual?.SetFadeAlpha(1f - t);
    }

    private void OnDestroy()
    {
        if (hasLineAttach && lineAttach != null)
        {
            lineAttach.localPosition = lineAttachLocalPos;
            lineAttach.localRotation = lineAttachLocalRot;
        }
        modelVisual?.Despawn();
    }
}
