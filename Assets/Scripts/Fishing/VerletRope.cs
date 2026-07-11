using System.Collections.Generic;
using UnityEngine;

// Run after PlayerController.LateUpdate (squash/stretch, airborne tumble) and FishingRodBend.LateUpdate
// finish writing to the rod-tip transform chain. If we sampled rodTip.position before those wrote their
// LateUpdate changes, point 0 of the rope would lag a frame behind the rendered rod tip — visible as a
// disconnect during charge-squash and bounce impacts.
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(LineRenderer))]
public class VerletRope : MonoBehaviour
{
    public struct RopePoint
    {
        public Vector3 currentPosition;
        public Vector3 previousPosition;
        public bool isLocked;
    }

    private Transform rodTip;
    private Transform bobber;

    [Header("Rope Settings")]
    public int segmentCount = 35;
    public int constraintIterations = 20;

    [Header("Physics")]
    public Vector3 gravity = new Vector3(0f, -9.81f, 0f);

    [Header("Slack After Landing")]
    [Tooltip("Baseline slack reduction once the bobber is sitting in the water (0 = original full slack, 1 = fully straight). The nibble pulse is layered on top.")]
    [Range(0f, 1f)] public float landedTightenAmount = 0.25f;

    [Header("Nibble Tighten Pulse")]
    [Tooltip("How much the line tightens on each individual nibble (0 = no change, 1 = fully straight).")]
    [Range(0f, 1f)] public float nibbleTightenAmount = 0.6f;
    [Tooltip("Seconds for a nibble's tighten pulse to decay back to baseline.")]
    public float nibbleTightenDecay = 0.4f;

    [Header("Tug Tighten Pulse")]
    [Tooltip("How much the line straightens on each lure tug / yank (0 = no change, 1 = fully straight).")]
    [Range(0f, 1f)] public float tugTightenAmount = 0.85f;
    [Tooltip("Seconds for a tug's straighten pulse to slacken back to baseline.")]
    public float tugTightenDecay = 0.35f;

    [Header("Lure Reel Tension")]
    [Tooltip("How taut the line is held while the player cranks the reel on a lure (0 = no change, 1 = fully straight).")]
    [Range(0f, 1f)] public float reelTightenAmount = 0.9f;
    [Tooltip("Seconds for the line to pull taut once cranking starts.")]
    public float reelTightenAttack = 0.12f;
    [Tooltip("Seconds for the line to slacken back once cranking stops.")]
    public float reelTightenRelease = 0.4f;

    [Header("Fight Jump Slack")]
    [Tooltip("How slack the taut fight line goes while the hooked fish is airborne (0 = stays dead straight, 1 = fully simulated rope).")]
    [Range(0f, 1f)] public float jumpSlackAmount = 0.85f;
    [Tooltip("Seconds for the line to go slack when the fish leaves the water.")]
    public float jumpSlackAttack = 0.08f;
    [Tooltip("Seconds for the line to pull straight again after the fish lands.")]
    public float jumpSlackRelease = 0.6f;

    [Header("Cast Flight Coil")]
    [Tooltip("While the bobber flies, render the line as a stretched spring/coil trailing behind it, " +
             "which winds up on launch and unwinds as it lands (then the normal straighten-in-water " +
             "takes over). Purely a render shape — the rope simulation is untouched.")]
    public bool coilOnCast = true;
    [Tooltip("Number of full helical turns wound along the WHOLE line during flight. Since the coil is " +
             "concentrated near the bobber (see Coil Bobber Fraction), only that share of the turns is " +
             "actually visible — keep this fairly high. Raise Coil Render Points if loops look faceted.")]
    public float coilTurns = 8f;
    [Tooltip("Radius of the coil, in metres — how far the spring bulges off the rod-tip→bobber axis.")]
    public float coilRadius = 0.35f;
    [Tooltip("How straight the coil's axis is held: 0 uses the fully-sagging simulated rope as the spine " +
             "(droops behind the bobber), 1 winds around the straight rod-tip→bobber chord (clean spring).")]
    [Range(0f, 1f)] public float coilSpineStraighten = 0.6f;
    [Tooltip("Fraction of the line nearest the BOBBER that coils. The coil is fattest at the bobber and " +
             "smoothly relaxes into a straight line toward the rod tip. e.g. 0.4 = only the bobber-side " +
             "40% springs, the rest is a normal line.")]
    [Range(0.05f, 1f)] public float coilBobberFraction = 0.4f;
    [Tooltip("Short taper right at the bobber end (as a fraction of the line) so the spring meets the " +
             "bobber cleanly instead of ending on a full loop.")]
    [Range(0f, 0.3f)] public float coilEndTaper = 0.06f;
    [Tooltip("Seconds for the coil to wind up to full radius right after the cast.")]
    public float coilRampInTime = 0.1f;
    [Tooltip("How fast the coil radius decays over the flight (per-second e-folding); higher = unwinds sooner.")]
    [Range(0f, 4f)] public float coilDecayRate = 0.5f;
    [Tooltip("Seconds for the coil to fully relax once the bobber lands / the flight ends.")]
    public float coilRelease = 0.25f;
    [Tooltip("Turns per second the whole coil rotates as it flies, for a little life. 0 = static spring.")]
    public float coilSpinSpeed = 1.5f;
    [Tooltip("How much the throw charge scales the coil: 0 = full spring on every cast, 1 = fully " +
             "proportional so a barely-charged flick has no coil and a full charge has the full spring. " +
             "Scales both the radius and the number of loops.")]
    [Range(0f, 1f)] public float coilChargeInfluence = 1f;
    [Tooltip("How many points the coil is drawn with. Independent of Segment Count (the physics rope), " +
             "so the spring stays smooth without paying for extra rope simulation. ~16 per turn is plenty.")]
    public int coilRenderPoints = 64;

    [Header("Line-Snap Flicker (fish fight)")]
    [Tooltip("How sharply the snap warning escalates with line tension: the fraction of time the rope is " +
             "INVISIBLE each flicker cycle is tension^exponent, so low tension barely blinks and near-snap " +
             "the rope is almost entirely gone. At tension 1 (the snap itself) it is fully invisible.")]
    public float snapFlickerExponent = 2.5f;
    [Tooltip("Flicker rate (Hz) when the warning first starts.")]
    public float snapFlickerMinHz = 6f;
    [Tooltip("Flicker rate (Hz) at the edge of snapping.")]
    public float snapFlickerMaxHz = 14f;
    [Tooltip("Seconds for the rope to fade (flicker) back to fully visible on the rod after the line snaps " +
             "or the fight otherwise ends.")]
    public float snapRecoverDuration = 1.2f;

    private LineRenderer lineRenderer;
    private List<RopePoint> ropePoints = new List<RopePoint>();
    private float segmentLength;
    private bool isInitialized = false;
    private bool isLineTight = false;
    private bool hasLanded = false;
    private float nibbleTightness = 0f;
    private float tugTightness = 0f;
    private float reelTightness = 0f;
    private bool isLureReelHeld = false;
    private bool isFishAirborne = false;
    private float fightSlack = 0f;
    private Vector3[] positionsCache;

    // Snap-warning flicker state. snapTension is the live fight tension (0..1) from
    // FishFightHandler; snapDuty is the fraction of each flicker cycle the rope renders.
    // After a snap (or any fight end) the duty ramps back to 1 over snapRecoverDuration —
    // the "fade back into visibility on the rod". snapRecoverT >= 1 means live-tension mode.
    private float snapTension = 0f;
    private float snapDuty = 1f;
    private float snapRecoverFrom = 1f;
    private float snapRecoverT = 1f;

    // External hide (ball-jump). The snap flicker writes lineRenderer.enabled every LateUpdate,
    // so callers can't just toggle the renderer — it would be stomped a frame later. They set
    // this instead and every enabled-write in here ANDs it in.
    private bool externallyHidden = false;

    public void SetExternallyHidden(bool hidden)
    {
        externallyHidden = hidden;
        // Apply immediately both ways: while the rope is inactive LateUpdate early-returns and
        // the flicker never runs, so waiting for it would leave the line invisible after un-hide.
        if (lineRenderer != null) lineRenderer.enabled = !hidden;
    }

    // Cast flight coil: driven by FishingLine.BeginFlightCoil right after a cast's SetupRope (an
    // explicit call rather than the OnThrowBobber event, so it can't be clobbered by SetupRope's
    // own reset if the two fire in the wrong order). Eases out on landing/bite/fight.
    private bool isFlyingCoil = false;
    private float coilTime = 0f;
    private float coilStrength = 0f;
    private float coilSpinPhase = 0f;
    private float coilCharge = 1f; // normalized throw charge for this cast (scales radius + turns)
    private Vector3[] coilSpine;   // high-res spine sampled along the low-res sim rope
    private Vector3[] coilCache;   // final helix positions handed to the LineRenderer

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }

    private void OnEnable()
    {
        FishingEvents.OnBobberLandedInWater += HandleBobberLandedInWater;
        FishingEvents.OnFishNibble += HandleFishNibble;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnFishFightBegin += HandleFishFightBegin;
        FishingEvents.OnFishFightEnd += HandleFishFightEnd;
        FishingEvents.OnLureTugged += HandleLureTugged;
        FishingEvents.OnLureReelChanged += HandleLureReelChanged;
        FishingEvents.OnHookedFishJumpChanged += HandleHookedFishJumpChanged;
        FishingEvents.OnLineTensionChanged += HandleLineTension;
        FishingEvents.OnCancelFishing += HandleCancelFishing;
    }

    private void OnDisable()
    {
        FishingEvents.OnBobberLandedInWater -= HandleBobberLandedInWater;
        FishingEvents.OnFishNibble -= HandleFishNibble;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnFishFightBegin -= HandleFishFightBegin;
        FishingEvents.OnFishFightEnd -= HandleFishFightEnd;
        FishingEvents.OnLureTugged -= HandleLureTugged;
        FishingEvents.OnLureReelChanged -= HandleLureReelChanged;
        FishingEvents.OnHookedFishJumpChanged -= HandleHookedFishJumpChanged;
        FishingEvents.OnLineTensionChanged -= HandleLineTension;
        FishingEvents.OnCancelFishing -= HandleCancelFishing;
    }

    private void HandleHookedFishJumpChanged(bool airborne)
    {
        isFishAirborne = airborne;
    }

    private void HandleFishNibble(BobberController b)
    {
        nibbleTightness = nibbleTightenAmount;
    }

    private void HandleLureTugged()
    {
        tugTightness = tugTightenAmount;
    }

    private void HandleLureReelChanged(bool held)
    {
        isLureReelHeld = held;
    }

    private void HandleFishBite(BobberController b)
    {
        isLineTight = true;
        nibbleTightness = 0f;
        isFlyingCoil = false;
    }

    private void HandleBobberLandedInWater(BobberController landed)
    {
        if (!isInitialized) return;
        hasLanded = true;
        isFlyingCoil = false; // splashdown: let the spring unwind, then the water-straighten takes over
    }

    private void HandleFishFightBegin(FishPreset fish)
    {
        isLineTight = true;
        isFlyingCoil = false;
    }

    private void HandleFishFightEnd(bool success)
    {
        isLineTight = false;
        isFishAirborne = false;
        BeginSnapRecover();
    }

    // Live tension while a fish fight runs. Receiving tension puts the flicker in live mode
    // (cancels any recover ramp) so reeling the fish back visibly calms the rope again.
    private void HandleLineTension(float tension)
    {
        snapTension = Mathf.Clamp01(tension);
        snapRecoverT = 1f;
    }

    // A fight that ends by cancel/fail never fires OnFishFightEnd — the snap case lands here
    // (FailRoutine → OnCancelFishing), with the rope fully invisible. Fade it back in.
    private void HandleCancelFishing() => BeginSnapRecover();

    // Ramp the visibility duty from wherever it is now back to fully visible, so a snapped
    // (invisible) line flicker-fades back into view on the rod instead of popping.
    private void BeginSnapRecover()
    {
        snapTension = 0f;
        if (snapDuty >= 0.999f) { snapRecoverT = 1f; return; } // already solid — nothing to fade
        snapRecoverFrom = snapDuty;
        snapRecoverT = 0f;
    }

    public void SetupRope(Transform rodTip, Transform bobber)
    {
        this.rodTip = rodTip;
        this.bobber = bobber;

        ropePoints.Clear();

        for (int i = 0; i <= segmentCount; i++)
        {
            ropePoints.Add(new RopePoint
            {
                currentPosition = rodTip.position,
                previousPosition = rodTip.position,
                isLocked = (i == 0)
            });
        }

        positionsCache = new Vector3[ropePoints.Count];
        isInitialized = true;
        hasLanded = false;

        // A re-setup is a fresh line (new cast or the bobber parked back on the rod) — clear
        // all transient tension state. Without this, a fight that ends by parking the bobber
        // (lost fight, missed hook) leaves isLineTight stuck on and the dangling rope renders
        // as a dead-straight 2-point line.
        isLineTight = false;
        nibbleTightness = 0f;
        tugTightness = 0f;
        reelTightness = 0f;
        isFishAirborne = false;
        fightSlack = 0f;

        // Fresh line: kill any coil. A cast re-arms it via BeginFlightCoil right after this call.
        isFlyingCoil = false;
        coilStrength = 0f;
        coilTime = 0f;
        coilSpinPhase = 0f;

        // Snap flicker: a park right after a snapped line arrives here mid-recover — leave that
        // ramp running so the rope fades back in on the rod. Anything else (fresh cast with the
        // rope somehow not solid) gets a recover started as a safety net; never snap to visible.
        snapTension = 0f;
        if (snapDuty < 0.999f && snapRecoverT >= 1f) BeginSnapRecover();
    }

    // Called by FishingLine immediately after SetupRope on a cast, so the ordering is deterministic
    // (SetupRope clears coil state, then this arms it). Parking the bobber never calls this, so the
    // dangling line stays a plain rope. chargeNormalized (0..1) scales the spring to the throw power.
    public void BeginFlightCoil(float chargeNormalized = 1f)
    {
        if (!coilOnCast) return;
        isFlyingCoil = true;
        coilTime = 0f;
        coilSpinPhase = 0f;
        coilCharge = Mathf.Clamp01(chargeNormalized);
    }

    public void DeactivateRope()
    {
        isInitialized = false;
        isLineTight = false;
        hasLanded = false;
        nibbleTightness = 0f;
        tugTightness = 0f;
        reelTightness = 0f;
        isLureReelHeld = false;
        isFishAirborne = false;
        fightSlack = 0f;
        isFlyingCoil = false;
        coilStrength = 0f;
        snapTension = 0f;
        snapDuty = 1f;
        snapRecoverT = 1f;
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
            lineRenderer.enabled = !externallyHidden;
        }
    }

    void LateUpdate()
    {
        if (!isInitialized) return;

        // --- FIXED: Added Safety Check ---
        // If the bobber or rod has been destroyed, stop immediately to prevent MissingReferenceException.
        if (bobber == null || rodTip == null)
        {
            DeactivateRope();
            return;
        }
        // ---------------------------------

        if (nibbleTightness > 0f)
        {
            nibbleTightness = Mathf.Max(0f, nibbleTightness - Time.deltaTime / Mathf.Max(0.001f, nibbleTightenDecay));
        }
        if (tugTightness > 0f)
        {
            tugTightness = Mathf.Max(0f, tugTightness - Time.deltaTime / Mathf.Max(0.001f, tugTightenDecay));
        }

        // Sustained tension while cranking: ramp up fast on attack, ease off on release.
        float reelTarget = isLureReelHeld ? reelTightenAmount : 0f;
        float reelRampTime = reelTarget > reelTightness ? reelTightenAttack : reelTightenRelease;
        reelTightness = Mathf.MoveTowards(
            reelTightness, reelTarget, Time.deltaTime / Mathf.Max(0.001f, reelRampTime));

        // Fight-jump slack: snaps in when the hooked fish leaves the water, eases back out
        // after the splash-down so the line smoothly pulls straight again.
        float slackTarget = isFishAirborne ? jumpSlackAmount : 0f;
        float slackRampTime = slackTarget > fightSlack ? jumpSlackAttack : jumpSlackRelease;
        fightSlack = Mathf.MoveTowards(
            fightSlack, slackTarget, Time.deltaTime / Mathf.Max(0.001f, slackRampTime));

        // Cast flight coil: wind up while flying, relax on release. Advance the spin phase only
        // while there's a coil to spin so a parked/idle line never drifts its phase.
        if (isFlyingCoil) coilTime += Time.deltaTime;
        float coilTarget = isFlyingCoil ? 1f : 0f;
        float coilRampTime = coilTarget > coilStrength ? coilRampInTime : coilRelease;
        coilStrength = Mathf.MoveTowards(coilStrength, coilTarget, Time.deltaTime / Mathf.Max(0.001f, coilRampTime));
        if (coilStrength > 0f) coilSpinPhase += coilSpinSpeed * Time.deltaTime * Mathf.PI * 2f;

        // Simulate every frame — even while the fight line renders straight — so the rope
        // points keep tracking the endpoints. When jump slack kicks in mid-fight the simulated
        // rope is current instead of frozen at its pre-fight pose.
        Simulate();

        if (isLineTight)
        {
            if (fightSlack <= 0f) DrawTightLine();
            else DrawSimulatedRope(1f - fightSlack);
        }
        else if (coilStrength > 0.001f)
        {
            DrawCoilRope();
        }
        else
        {
            DrawSimulatedRope(0f);
        }

        UpdateSnapFlicker();
    }

    // Snap-warning flicker: while the fight handler feeds tension, the rope blinks invisible
    // for tension^exponent of each cycle — barely a shimmer at low tension, near-black right
    // before the snap, fully gone AT the snap. After the fight ends the duty ramps back to 1
    // (BeginSnapRecover), so a snapped line flicker-fades back into view on the rod.
    private void UpdateSnapFlicker()
    {
        if (snapRecoverT < 1f)
        {
            snapRecoverT = Mathf.Min(1f, snapRecoverT + Time.deltaTime / Mathf.Max(0.001f, snapRecoverDuration));
            snapDuty = Mathf.Lerp(snapRecoverFrom, 1f, snapRecoverT);
        }
        else
        {
            snapDuty = 1f - Mathf.Pow(snapTension, Mathf.Max(0.1f, snapFlickerExponent));
        }

        bool visible;
        if (snapDuty >= 0.999f) visible = true;
        else if (snapDuty <= 0.001f) visible = false;
        else
        {
            // Square wave: visible for snapDuty of each cycle, blinking faster as it worsens.
            float hz = Mathf.Lerp(snapFlickerMinHz, snapFlickerMaxHz, 1f - snapDuty);
            visible = (Time.time * hz) % 1f < snapDuty;
        }
        lineRenderer.enabled = visible && !externallyHidden;
    }

    private void Simulate()
    {
        // Safety check is now handled in LateUpdate, so we can access .position safely here
        float currentRopeLength = Vector3.Distance(rodTip.position, bobber.position);
        segmentLength = currentRopeLength / segmentCount;

        float deltaTime = Time.deltaTime;

        for (int i = 0; i < ropePoints.Count; i++)
        {
            RopePoint point = ropePoints[i];
            if (point.isLocked) continue;

            Vector3 velocity = point.currentPosition - point.previousPosition;
            point.previousPosition = point.currentPosition;

            point.currentPosition += velocity + gravity * (deltaTime * deltaTime);
            ropePoints[i] = point;
        }

        for (int i = 0; i < constraintIterations; i++)
        {
            ApplyConstraints();
        }
    }

    private void ApplyConstraints()
    {
        RopePoint firstPoint = ropePoints[0];
        firstPoint.currentPosition = rodTip.position;
        ropePoints[0] = firstPoint;

        RopePoint lastPoint = ropePoints[ropePoints.Count - 1];
        lastPoint.currentPosition = bobber.position;
        ropePoints[ropePoints.Count - 1] = lastPoint;

        for (int i = 0; i < ropePoints.Count - 1; i++)
        {
            RopePoint point1 = ropePoints[i];
            RopePoint point2 = ropePoints[i + 1];

            Vector3 delta = point2.currentPosition - point1.currentPosition;
            float distance = delta.magnitude;

            if (distance == 0) continue;

            float error = distance - segmentLength;
            Vector3 correction = delta.normalized * error;

            if (!point1.isLocked)
                point1.currentPosition += correction * 0.5f;
            if (!point2.isLocked)
                point2.currentPosition -= correction * 0.5f;

            ropePoints[i] = point1;
            ropePoints[i + 1] = point2;
        }
    }

    // minTightness floors the straight-line blend — the fight line during a jump passes
    // (1 - fightSlack) here so the rope stays partially taut and re-tightens smoothly.
    private void DrawSimulatedRope(float minTightness)
    {
        int count = ropePoints.Count;
        lineRenderer.positionCount = count;
        int last = count - 1;

        float baseline = hasLanded ? landedTightenAmount : 0f;
        float effectiveTightness = Mathf.Max(
            Mathf.Max(Mathf.Max(baseline, reelTightness), minTightness),
            Mathf.Max(nibbleTightness, tugTightness));
        bool blend = effectiveTightness > 0f && last > 0;

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = ropePoints[i].currentPosition;
            if (blend)
            {
                float t = (float)i / last;
                Vector3 straight = Vector3.Lerp(rodTip.position, bobber.position, t);
                pos = Vector3.Lerp(pos, straight, effectiveTightness);
            }
            positionsCache[i] = pos;
        }
        lineRenderer.SetPositions(positionsCache);
    }

    private void DrawTightLine()
    {
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, rodTip.position);
        lineRenderer.SetPosition(1, bobber.position);
    }

    // Renders the line as a stretched spring during a cast: a helix wound around a spine that runs
    // from the rod tip to the bobber. The spine blends between the naturally-sagging simulated rope
    // (trails behind the bobber) and the straight chord (clean spring) via coilSpineStraighten, and
    // is sampled at coilRenderPoints resolution so the spring stays smooth even though the physics
    // rope is low-poly. A non-twisting (parallel-transported) frame keeps the coil from flipping as
    // the spine curves, and the amplitude tapers to zero at both ends so it meets the rod tip and
    // bobber cleanly.
    private void DrawCoilRope()
    {
        if (ropePoints.Count < 2) { DrawSimulatedRope(0f); return; }

        int m = Mathf.Max(8, coilRenderPoints);
        if (coilSpine == null || coilSpine.Length != m) { coilSpine = new Vector3[m]; coilCache = new Vector3[m]; }
        int last = m - 1;

        // Pass 1: build the smooth spine by resampling the sim rope and blending toward the chord.
        for (int j = 0; j < m; j++)
        {
            float t = (float)j / last;
            Vector3 sim = SampleSimRope(t);
            Vector3 straight = Vector3.Lerp(rodTip.position, bobber.position, t);
            coilSpine[j] = Vector3.Lerp(sim, straight, coilSpineStraighten);
        }

        // Scale the whole spring by throw charge: a weak flick barely coils, a full charge gives the
        // full spring. Applied to both radius (amp) and loop count (turns) so a small coil isn't a
        // tight buzz of loops crammed into a tiny radius.
        float chargeScale = Mathf.Lerp(1f, coilCharge, coilChargeInfluence);
        float turns = coilTurns * chargeScale;

        float ramp = coilRampInTime > 0f ? Mathf.Clamp01(coilTime / coilRampInTime) : 1f;
        float amp = coilRadius * chargeScale * coilStrength * ramp * Mathf.Exp(-coilDecayRate * coilTime);

        // Pass 2: wind a non-twisting helix frame along the spine.
        Vector3 prevTangent = CoilTangent(0, last);
        Vector3 normal = Vector3.ProjectOnPlane(Vector3.up, prevTangent);
        if (normal.sqrMagnitude < 1e-5f) normal = Vector3.ProjectOnPlane(Vector3.right, prevTangent);
        normal.Normalize();

        for (int j = 0; j < m; j++)
        {
            float t = (float)j / last;

            Vector3 tangent = CoilTangent(j, last);
            Quaternion swing = Quaternion.FromToRotation(prevTangent, tangent);
            normal = swing * normal;
            normal = Vector3.ProjectOnPlane(normal, tangent);
            if (normal.sqrMagnitude < 1e-5f) normal = Vector3.ProjectOnPlane(Vector3.up, tangent);
            normal.Normalize();
            Vector3 binormal = Vector3.Cross(tangent, normal);
            prevTangent = tangent;

            float env = CoilEnvelope(t);
            float phase = 2f * Mathf.PI * turns * t + coilSpinPhase;
            Vector3 offset = amp * env * (Mathf.Cos(phase) * normal + Mathf.Sin(phase) * binormal);
            coilCache[j] = coilSpine[j] + offset;
        }

        // Pin the ends exactly to the rod tip and bobber (the taper already brings them close).
        coilCache[0] = rodTip.position;
        coilCache[last] = bobber.position;
        lineRenderer.positionCount = m;
        lineRenderer.SetPositions(coilCache);
    }

    // Position along the simulated rope at normalised parameter t (0 = rod tip, 1 = bobber),
    // linearly interpolating between the low-res sim points.
    private Vector3 SampleSimRope(float t)
    {
        int segLast = ropePoints.Count - 1;
        float f = Mathf.Clamp01(t) * segLast;
        int i0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, segLast);
        int i1 = Mathf.Min(i0 + 1, segLast);
        return Vector3.Lerp(ropePoints[i0].currentPosition, ropePoints[i1].currentPosition, f - i0);
    }

    private Vector3 CoilTangent(int j, int last)
    {
        Vector3 a = coilSpine[Mathf.Max(0, j - 1)];
        Vector3 b = coilSpine[Mathf.Min(last, j + 1)];
        Vector3 t = b - a;
        if (t.sqrMagnitude < 1e-6f) t = bobber.position - rodTip.position;
        if (t.sqrMagnitude < 1e-6f) return Vector3.forward;
        return t.normalized;
    }

    // Amplitude weighting along the line (t: 0 = rod tip, 1 = bobber). Zero toward the rod, growing to
    // full near the bobber over the coilBobberFraction nearest it, then a short taper right at the
    // bobber so the spring attaches cleanly.
    private float CoilEnvelope(float t)
    {
        float frac = Mathf.Clamp01(coilBobberFraction);
        if (frac <= 0f) return 0f;

        float start = 1f - frac;                       // tip-side edge of the coil region
        if (t <= start) return 0f;

        float u = (t - start) / frac;                  // 0 at the region edge, 1 at the bobber
        float head = Mathf.SmoothStep(0f, 1f, u);      // relax into the straight line toward the rod
        float tail = coilEndTaper > 0f
            ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) / coilEndTaper))
            : 1f;                                      // clean attach right at the bobber
        return head * tail;
    }
}