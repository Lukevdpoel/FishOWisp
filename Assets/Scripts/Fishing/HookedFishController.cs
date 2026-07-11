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
    private const float SweepAmplitude = 35f;    // slow side-to-side hunt for an escape angle
    private const float SweepFrequency = 0.25f;  // sweep cycles per second
    // How fast the trailing chain relaxes straight behind the head. High on purpose: a hooked
    // fish strains as one taut piece, so the thrash reads as the whole body wrenching. Low
    // values let the chain lag the yaw swings and visibly CURL the body around the pinned
    // nose/bobber (the hang pose uses 30 for the same reason).
    private const float BodyStraightenRate = 14f;
    private const float JumpThrashFreqMul = 2.6f;   // mid-leap the tail wrenches this much faster
    private const float JumpThrashAmpMul = 1.7f;    // ...and this much wider, for an airborne flail

    // Body tail-beat (the shader sway flap) boost, ON TOP of the intensity-driven clock. A
    // struggling fish doesn't just whip its whole body around (the thrash above) — its tail also
    // beats drastically faster and wider. Tune these to taste.
    private const float StruggleFlapFreqMul = 2.2f; // tail flaps this much faster while struggling
    private const float StruggleFlapAmpMul  = 1.5f; // ...and this much wider, for visible power
    private const float JumpFlapFreqMul = 2.8f;     // mid-leap the body flap cranks up further
    private const float JumpFlapAmpMul  = 1.8f;     // ...and wider still

    // Escape swim-off when the fish gets away: burst away from the player, diving and
    // fading out, then despawn for good.
    private const float EscapeLinger = 1f;   // beat before it bolts (a second after a failed fight)
    private const float EscapeDuration = 1.8f;
    private const float EscapeSpeed = 2.6f;
    private const float EscapeDiveDepth = 0.35f;

    // Caught hang (TP-style showcase): the fish is hoisted out of the water and dangles
    // nose-up from the line while the player inspects it.
    private const float HangSwingUpDuration = 0.3f; // seconds to swing from the fight pose to vertical
    private const float HangIntensity = 1.3f;       // tail effort during the swing-up
    // The chain must not lazily chase the swinging head into the vertical — that read as the fish
    // slowly folding into place. At this rate the body snaps straight behind the head within a few
    // frames, so the whole fish rotates to vertical essentially as one piece.
    private const float HangStraightenRate = 30f;
    private const float HangIdleIntensity = 3f;     // tail-beat clock on display — a quick, frantic kick
    private const float HangIdleFlapAmp = 0.7f;     // ...at most of the swim amplitude — clearly flapping
    // On the line the flap is a FLOP, not the swim cycle: body-waves eases to ~0, which removes
    // the travelling wave so the tail kicks side to side as one piece — a caught fish flopping,
    // not a fish swimming in mid-air.
    private const float HangFlopBodyWaves = 0.15f;

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

    // Caught-hang state: while true the fish ignores the fight logic and hangs vertically
    // from the hook point, slowly spinning around the line so the player sees every side.
    // The swing-up is a blend that COMPLETES (hangBlend 0→1): once it hits 1 the rotation is
    // set exactly to the vertical spin pose every frame — an exponential chase of the spinning
    // target would never converge and left the fish tilted and precessing like a pendulum.
    private bool hanging;
    private float hangSpinSpeed;
    private float hangSpinYaw;
    private float hangBlend;
    private Quaternion hangStartRotation;

    // True while the player is actively reeling — during fight calm phases or the final
    // reel-in. A reeled fish resists hard; an unreeled, unstruggling fish stays calm.
    private bool isReeling;

    // Player-driven heading (the fish-control fight): while set, FishFightHandler supplies the
    // swim yaw each tick and the away-from-player pull plus the escape-angle sweep are
    // suppressed, so the body reads the player's tank steering truthfully. The thrash wobble
    // and tail effort still layer on top, keyed off the bobber's fight-burst state.
    private float drivenYaw;
    private bool isDriven;
    private bool snapChainPending;

    // The fish's current base heading (deg, without the thrash/sweep wobble). The fight
    // handler seeds its steering heading from this at the hook-set, so the fish follows
    // through the direction it already held instead of snapping to a fresh one.
    public float CurrentBaseYaw => baseYaw;

    public void SetDrivenHeading(float yawDegrees)
    {
        // First driven frame = the hook-set: lay the body out dead straight along the new
        // heading, wiping whatever bend the bite window left in the chain.
        if (!isDriven) snapChainPending = true;
        drivenYaw = yawDegrees;
        isDriven = true;
    }

    public void ClearDrivenHeading() { isDriven = false; }

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

        // The head is pinned to the hook, so the spawn yaw decides how the whole body lies
        // around the bobber. Spawn mid-follow-through: face the direction the REAL fish was
        // dashing when it took the tackle (recorded by FishingZone just before the hook), so
        // the visible strike flows into the hooked silhouette — and later into the fight —
        // with no direction snap. When no strike heading was recorded (debug hooks, exotic
        // paths), fall back to facing away from the player/camera, which at least never
        // pivots the body around the pinned nose the way a random yaw did.
        if (bobber.HasStrikeHeading)
        {
            controller.baseYaw = bobber.StrikeHeadingYaw;
        }
        else
        {
            Transform viewpoint = bobber.PlayerTransform != null
                ? bobber.PlayerTransform
                : (Camera.main != null ? Camera.main.transform : null);
            Vector3 awayDir = viewpoint != null
                ? bobber.transform.position - viewpoint.position : Vector3.zero;
            awayDir.y = 0f;
            controller.baseYaw = awayDir.sqrMagnitude > 0.001f
                ? Mathf.Atan2(awayDir.x, awayDir.z) * Mathf.Rad2Deg
                : Random.Range(0f, 360f);
        }

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

    // The catch is won: from here the fish hangs nose-up from the hook point (the bobber, hoisted
    // to the rod tip by the reel-in arc) and slowly spins around the line — the Twilight-Princess
    // showcase pose. Called at the start of the catch reel so the fish swings into the vertical
    // during the hoist itself. The chain keeps ticking, so the body dangles and the tail keeps
    // swaying right through the material reveal and for the whole showcase.
    public void BeginHang(float spinDegreesPerSecond)
    {
        if (hanging) return;
        hanging = true;
        hangSpinSpeed = spinDegreesPerSecond;
        hangBlend = 0f;
        hangStartRotation = transform.rotation;
        // Seed the spin so the swing up to vertical is a pure nose-up pitch, no extra twist:
        // pitching up from heading baseYaw leaves the fish's back facing AWAY from that heading.
        hangSpinYaw = baseYaw + 180f;
        modelVisual?.SetVerticalHang(true);
        modelVisual?.SetBodyWavesTarget(HangFlopBodyWaves);
    }

    // The hidden material switch: silhouette → the prefab's real colors. Timed by the catch
    // inspection to land mid-camera-transition so the swap never reads as a pop. The sway/chain
    // deformation normally carries straight over to the CPU skinning path (which seats the model
    // on the line axis itself before capturing its rest pose), so the flap continues in true
    // colors. Only when a mesh isn't CPU-readable does the fish go rigid — then the recenter
    // must happen here: the raw transform pose renders, and its baked swimming offsets
    // (waterline sink, mesh pivot) would hang the fish off-axis, sweeping a cone around the
    // line as it spins.
    public void RevealTrueColors()
    {
        if (modelVisual == null) return;
        bool swayContinues = modelVisual.RevealTrueMaterials();
        if (!swayContinues) modelVisual.AlignSnoutToForwardAxis();
    }

    private void UpdateHang()
    {
        if (bobber == null)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 hook = bobber.transform.position;

        // Nose-up, slowly rotating around the vertical line axis. LookRotation's up-hint sets
        // which way the fish's back faces, so advancing it spins the dangling body in place.
        hangSpinYaw += hangSpinSpeed * Time.deltaTime;
        Quaternion target = Quaternion.LookRotation(
            Vector3.up, Quaternion.AngleAxis(hangSpinYaw, Vector3.up) * Vector3.forward);

        // Swing up over a fixed duration, then LOCK to the exact vertical pose — the fish must
        // hang dead vertical on the line, with only the intentional spin; the life comes from
        // the body sway below, not from wobbling the whole transform.
        hangBlend = Mathf.MoveTowards(hangBlend, 1f, Time.deltaTime / HangSwingUpDuration);
        float ease = hangBlend * hangBlend * (3f - 2f * hangBlend);
        transform.rotation = hangBlend >= 1f ? target : Quaternion.Slerp(hangStartRotation, target, ease);

        // Mouth on the hook: the host sits one head-offset back along its (now mostly vertical)
        // forward so the snout lands on the line's end.
        float headOffset = modelVisual != null ? modelVisual.HeadForwardOffset : 0f;
        transform.position = hook - transform.forward * headOffset;

        // Tail effort settles with the swing-up into a lively display FLOP: the clock eases to an
        // agitated beat, the amplitude settles near the swim sway, and body-waves (set toward ~0
        // in BeginHang) turns the travelling swim wave into a whole-tail kick. The material
        // reveal hands this deformation over to the CPU skinning path, so the flop carries on in
        // true colors uninterrupted.
        modelVisual?.Tick(Time.deltaTime, Mathf.Lerp(HangIntensity, HangIdleIntensity, ease),
                          HangStraightenRate, 1f, Mathf.Lerp(1f, HangIdleFlapAmp, ease));

        if (hasLineAttach && lineAttach != null && modelVisual != null)
            lineAttach.position = modelVisual.MouthWorldPosition;
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

        if (hanging)
        {
            UpdateHang();
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

        if (isDriven)
        {
            // Fish-control fight: the handler already rate-limits the heading (player steer
            // vs. fight-burst wrench), so the body follows it directly.
            baseYaw = drivenYaw;
        }
        // Not driven = the bite reaction window: HOLD the spawn heading (the strike dash the
        // zone recorded), so the fish keeps straining in the direction it hit the tackle from
        // until the fight takes over — no turn toward "away from the player" here, or the
        // hook-set would inherit a direction the bite never had. The sweep/thrash below still
        // wrench around that held heading.

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
        else if (bobber.IsStruggling || bobber.fightBurst)
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

        // On top of the base heading (strike follow-through during the bite window, the
        // driven steering during the fight), a slow sweep hunts side to side for an escape
        // angle, and the fast thrash wrench scales with effort — a calm fish holds its angle
        // with barely a tremor, a struggling one wrenches violently. Mid-leap the wrench
        // cranks up (faster and wider) so the airborne fish visibly flails.
        float thrashFreqMul = jumping ? JumpThrashFreqMul : 1f;
        float thrashAmpMul = jumping ? JumpThrashAmpMul : 1f;
        sweepPhase += Time.deltaTime * SweepFrequency * Mathf.PI * 2f;
        thrashPhase += Time.deltaTime * ThrashFrequency * thrashFreqMul * Mathf.PI * 2f * (currentIntensity * 0.5f);
        float thrashScale = Mathf.Clamp(Mathf.InverseLerp(1.1f, 2.6f, currentIntensity), 0.08f, 1f);
        // No escape-angle sweep while the player steers the fish — the heading must read true.
        float sweep = isDriven ? 0f : Mathf.Sin(sweepPhase) * SweepAmplitude;
        float yaw = baseYaw
                  + sweep
                  + Mathf.Sin(thrashPhase) * ThrashYawAmplitude * thrashAmpMul * thrashScale;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // Mouth on the hook: the host sits one head-offset behind the hook point so the
        // chain's pinned head lands exactly on the line's end.
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
        float headOffset = modelVisual != null ? modelVisual.HeadForwardOffset : 0f;
        transform.position = hook - forward * headOffset;

        // Tail-beat boost: a struggling/leaping fish flaps its tail drastically faster and wider,
        // not just thrashing its whole body around. A calm or steadily-reeled fish keeps the
        // species' base flap. FishModelVisual smooths the amplitude ramp.
        float flapFreqMul = 1f;
        float flapAmpMul = 1f;
        if (jumping) { flapFreqMul = JumpFlapFreqMul; flapAmpMul = JumpFlapAmpMul; }
        else if (bobber.IsStruggling || bobber.fightBurst) { flapFreqMul = StruggleFlapFreqMul; flapAmpMul = StruggleFlapAmpMul; }

        // Hook-set: the pose above is final for this frame — lay the body out straight along
        // it before the chain ticks, so the fight opens with a clean, straight fish.
        if (snapChainPending)
        {
            modelVisual?.SnapChainStraight();
            snapChainPending = false;
        }

        modelVisual?.Tick(Time.deltaTime, currentIntensity, BodyStraightenRate, flapFreqMul, flapAmpMul);

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
