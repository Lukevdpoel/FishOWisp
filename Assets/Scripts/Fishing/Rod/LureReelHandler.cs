using UnityEngine;

// Twilight-Princess-style lure mechanic.
//
// Two intents drive the lure:
//   • REEL HOLD (LMB / RT held) — continuous pull toward the rod. This is "cranking the reel"
//     and is how the lure actually moves home. Force-based and mass-independent; ticked from
//     FixedUpdate so the pull is frame-rate independent.
//   • WIGGLE (A/D) — each fresh press is a YANK: a sudden tug toward the rod, arced to the
//     player's left (A) or right (D), that decays exponentially into a soft watery drift.
//     Same-side presses steer the arc harder but the heading is hard-capped near the
//     straight lure→rod line, and holding a key is still just one yank (edge-triggered).
//
// All forces are purely horizontal. The lure's buoyancy + pitch (in BobberController) stay
// untouched.
//
// Whether a fish bites is NOT decided here — each yank fires FishingEvents.OnLureTugged and
// the crank state goes out via OnLureReelChanged; the LureBiteBrain (owned by FishingZone)
// turns that activity into TP-style notice/hover/bite-roll behavior on the visible fish.
//
// Outcomes:
//   Continue        — keep ticking next frame
//   RetractRequested— lure reached catch range without a bite; rod should auto-retract
public class LureReelHandler
{
    [System.Serializable]
    public struct Settings
    {
        [Header("Reel Hold")]
        [Tooltip("Continuous acceleration (m/s²) applied toward the rod while reel is held (LMB / RT). " +
                 "This is the 'cranking the reel' force — the primary way the lure travels " +
                 "back. Terminal pull-in speed ≈ this ÷ the prefab's waterDrag (5 on the " +
                 "lure), so 30 ≈ 6 m/s, reached in ~0.2s.")]
        public float reelHoldForce;
        [Tooltip("Continuous nose-alignment rate while cranking: angular acceleration (rad/s² " +
                 "per rad of offset) turning the lure tip to face the rod. Inertia-independent. " +
                 "6 snaps the nose around in well under a second against the angular damping.")]
        public float reelAlignRate;

        [Header("Yank")]
        [Tooltip("Speed (m/s) the tug snaps the lure to the instant a yank lands. The hit is " +
                 "deliberately sudden — pullDecayRate handles the slowdown afterwards.")]
        public float yankPullSpeed;
        [Tooltip("Exponential decay rate (1/s) of the tug speed when no input follows. Higher " +
                 "= the jolt dies faster. 2.5 ≈ down to ~8% after 1s, with a watery tail.")]
        public float pullDecayRate;
        [Tooltip("How far (deg) one A/D press steers the pull off the straight-to-rod line. " +
                 "A arcs toward the player's left, D toward the right.")]
        public float yankArcAngleDeg;
        [Tooltip("Hard cap (deg) on deviation from the straight line to the rod, no matter " +
                 "how many same-side presses stack up.")]
        public float maxArcAngleDeg;
        [Tooltip("Exponential rate (1/s) at which the arc heading relaxes back toward the " +
                 "straight line — line tension straightening the path between presses.")]
        public float headingRecenterRate;
        [Tooltip("Target angular velocity gain (rad/s per rad of offset) from each alignment " +
                 "impulse. Inertia-compensated so the visible result is the same regardless of " +
                 "lure inertia. 0.6 = lure rotates ~35° per yank at 90° offset, less when closer.")]
        public float yankAlignmentImpulse;

        [Header("Lure Water Damping (override applied each tick)")]
        [Tooltip("Angular damping in water. Lower = rotation persists after yanks → more " +
                 "overshoot in the arc drift. Default 1.5 = slightly less than prefab. " +
                 "Linear damping is deliberately NOT overridden: Unity has a single linear " +
                 "damping scalar, and lowering it for horizontal drift also un-damps the " +
                 "vertical buoyancy spring, which catapults the lure out of the water. " +
                 "Horizontal drift is handled by the managed tug channel instead.")]
        public float lureAngularDamping;

        [Header("Retract")]
        [Tooltip("Distance from the waterline nearest the player at which a bite-less lure auto-retracts " +
                 "(and the crank/yank forces stop). Measured to the shore, not the player, so a lure cast " +
                 "from well back on the bank still completes its reel-in once it reaches the water's edge.")]
        public float retractDistance;

        [Header("Visual")]
        [Tooltip("How fast (1/sec) the speedboat visual fades after each yank. 4 ≈ 0.25s decay.")]
        public float intensityDecayRate;
        [Tooltip("Pitch angle (deg) the lure tips up at full intensity. Keep small to avoid " +
                 "fighting buoyancy.")]
        public float visualPitchAngleDeg;
        [Tooltip("Vertical bounce amplitude (m) at full intensity. Subtle is best.")]
        public float visualBobAmplitude;
        [Tooltip("Vertical bounce frequency (Hz).")]
        public float visualBobFrequencyHz;

        [Header("Popper (surface chatter — Popper lures only)")]
        [Tooltip("Nose-bounce amplitude (deg) for a Popper lure: while being tugged or reeled its " +
                 "front pitches up/down this far to read as a chattering surface popper (scaled by " +
                 "movement intensity, so a still popper sits level). Applied directly (bypasses the " +
                 "buoyancy slerp) so the high-frequency pop is actually visible. 0 = no chatter. " +
                 "Reel/yank physics are unaffected — this is purely visual.")]
        public float popperBounceAmplitudeDeg;
        [Tooltip("Nose-bounce frequency (Hz) for a Popper lure. Higher = faster surface chatter.")]
        public float popperBounceFrequencyHz;
        [Tooltip("Seconds between splash particles spawned at a Popper lure's nose WHILE IT'S MOVING " +
                 "(being tugged or reeled). A still popper makes no splash. ≤ 0 disables the splashes " +
                 "(the bounce still plays). The splash prefab is set on the BobberController (falls " +
                 "back to its nibble splash).")]
        public float popperSplashInterval;
    }

    public enum Outcome { Continue, RetractRequested }

    // Below this tug speed (m/s) we stop steering the velocity and release the lure to the
    // water drag. Kept low because the prefab's waterDrag (5) kills whatever we hand off
    // almost immediately — the visible watery tail lives in the managed decay above this.
    private const float PullCutoffSpeed = 0.05f;

    // Below this movement intensity the popper is treated as still — no splashes spawn and the
    // nose bounce has faded to nothing. Keeps the long decay tail of a tug from dribbling splashes.
    private const float PopperSplashMinIntensity = 0.1f;

    private float nudgeIntensity;
    private float prevLateralInput;
    private float pullSpeed;
    private float headingDeg; // signed offset of the pull from the lure→rod line; + = player's left
    private bool reelHeldBroadcast; // last state sent via OnLureReelChanged
    private float popperSplashTimer; // counts down to the next popper nose-splash

    public void Reset()
    {
        nudgeIntensity = 0f;
        prevLateralInput = 0f;
        pullSpeed = 0f;
        headingDeg = 0f;
        popperSplashTimer = 0f;
        BroadcastReelHeld(false);
    }

    private void BroadcastReelHeld(bool held)
    {
        if (reelHeldBroadcast == held) return;
        reelHeldBroadcast = held;
        FishingEvents.OnLureReelChanged?.Invoke(held);
    }

    // Snap the lure on water landing so the LineAttachPoint side faces the rod.
    public static void OrientForInitialPose(BobberController lure, Transform rodReference)
    {
        if (lure == null || rodReference == null) return;
        Rigidbody rb = lure.GetComponent<Rigidbody>();
        if (rb == null) return;
        Transform attach = lure.LineAttachPoint;
        if (attach == null || attach == lure.transform) return;

        Vector3 towardRod = rodReference.position - lure.transform.position;
        towardRod.y = 0f;
        if (towardRod.sqrMagnitude < 0.0001f) return;
        towardRod.Normalize();

        Vector3 currentAttachDir = attach.position - lure.transform.position;
        currentAttachDir.y = 0f;
        if (currentAttachDir.sqrMagnitude < 0.0001f) return;
        currentAttachDir.Normalize();

        Quaternion delta = Quaternion.FromToRotation(currentAttachDir, towardRod);
        rb.MoveRotation(delta * rb.rotation);
    }

    public Outcome Tick(
        BobberController activeBobber,
        Vector3 reelTarget,
        bool reelHeld,
        float lateralInput,
        bool isPopper,
        in Settings s)
    {
        if (activeBobber == null) return Outcome.Continue;
        if (!activeBobber.IsInWater) return Outcome.Continue;

        Rigidbody rb = activeBobber.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic) return Outcome.Continue;

        float dt = Time.deltaTime;

        // Apply the angular damping override each tick (cheap; ensures consistent behavior).
        // Skip if 0. Linear damping stays at the prefab's waterDrag — see the Settings tooltip;
        // we do NOT override inertia either, both can destabilize buoyancy.
        if (s.lureAngularDamping > 0f) rb.angularDamping = s.lureAngularDamping;

        // Yank detection: fresh press OR direction switch while held.
        const float threshold = 0.5f;
        bool isPressed = Mathf.Abs(lateralInput) > threshold;
        bool wasPressed = Mathf.Abs(prevLateralInput) > threshold;
        bool freshPress = isPressed && !wasPressed;
        bool signChanged = isPressed && wasPressed
                           && Mathf.Sign(lateralInput) != Mathf.Sign(prevLateralInput);
        bool yankNow = freshPress || signChanged;
        prevLateralInput = lateralInput;

        // Cranking the reel (LMB) owns the lure's travel — kill any residual tug so the
        // velocity override below never brakes the crank's accumulated speed.
        if (reelHeld) pullSpeed = 0f;
        BroadcastReelHeld(reelHeld);

        Vector3 bobberPos = rb.position;
        // "Home" for the reel is the waterline between the player and the lure (reelTarget,
        // computed by the rod), not the player themselves. A lure cast while the player stands
        // back from the bank would otherwise beach at the shore and stall just short of
        // retractDistance, never finishing the reel-in. When the player is at/over the water the
        // target collapses to the player, so behavior there is unchanged. Only this reference
        // point moves — the reel/yank forces, decay and buoyancy are all untouched.
        Vector3 toTarget = reelTarget - bobberPos;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;
        Vector3 toRodDirHoriz = dist > 0.001f ? toTarget / dist : Vector3.zero;

        // Continuous reel-in while the player holds LMB. This is the primary travel mechanism —
        // the wiggle-only design was unreadable, so cranking the reel actually does what it
        // looks like it does. Acceleration mode → mass-independent and frame-rate safe.
        if (reelHeld && dist > s.retractDistance && toRodDirHoriz != Vector3.zero)
        {
            if (s.reelHoldForce > 0f)
            {
                rb.AddForce(toRodDirHoriz * s.reelHoldForce, ForceMode.Acceleration);
            }

            // Keep the nose pointed at the rod while cranking — a proportional torque spring
            // against the angular damping, so it settles facing home rather than oscillating.
            if (s.reelAlignRate > 0f)
            {
                Vector3 tipDir = GetTipDirectionHorizontal(activeBobber);
                float offsetDeg = Vector3.SignedAngle(toRodDirHoriz, tipDir, Vector3.up);
                rb.AddTorque(
                    new Vector3(0f, -offsetDeg * Mathf.Deg2Rad * s.reelAlignRate, 0f),
                    ForceMode.Acceleration);
            }

            // Hold the speedboat pose while planing home — same visual channel as the yank
            // (pitch + bob via nudgeIntensity). The decay below takes over the moment the
            // crank stops, so it settles level exactly like a yank does.
            nudgeIntensity = 1f;
        }

        if (yankNow && dist > s.retractDistance && dist > 0.001f)
        {
            // 1) Steer, then tug along the new heading. Positive heading = player's left
            //    (rotating the lure→rod direction by +Y), so A (negative axis) arcs left and
            //    D arcs right. Same-side presses stack up to the cap; holding a key is still
            //    just one yank thanks to the edge detection above.
            headingDeg = Mathf.Clamp(
                headingDeg - Mathf.Sign(lateralInput) * s.yankArcAngleDeg,
                -s.maxArcAngleDeg, s.maxArcAngleDeg);

            Vector3 pullDir = Quaternion.AngleAxis(headingDeg, Vector3.up) * toRodDirHoriz;

            // Snap straight to full tug speed — the "sudden and hard" half of the pull; the
            // exponential decay below is the slowdown half. Skipped while cranking (LMB),
            // which owns the velocity; the yank still aligns the nose and signals the brain.
            if (!reelHeld && s.yankPullSpeed > 0f)
            {
                pullSpeed = s.yankPullSpeed;
            }

            // 2) Angular alignment — nose into the arc, not flat at the rod. Proportional to
            //    the current off-axis offset, zero when aligned. Scaled by the rigidbody's
            //    current inertia so the angular velocity gain per yank is the same regardless
            //    of inertia tensor (auto or overridden on prefab).
            if (s.yankAlignmentImpulse > 0f)
            {
                Vector3 tipDir = GetTipDirectionHorizontal(activeBobber);
                float offsetDeg = Vector3.SignedAngle(pullDir, tipDir, Vector3.up);
                float effectiveInertia = Mathf.Max(0.001f, rb.inertiaTensor.y);
                float angularImpulse =
                    -offsetDeg * Mathf.Deg2Rad * s.yankAlignmentImpulse * effectiveInertia;
                rb.AddTorque(new Vector3(0f, angularImpulse, 0f), ForceMode.Impulse);
            }

            nudgeIntensity = 1f;
            // The brain (LureBiteBrain via FishingZone) hears this and counts it as lure
            // movement — each yank arms one bite roll on hovering fish.
            FishingEvents.OnLureTugged?.Invoke();
        }

        // --- Tug decay: the sudden hit dies off exponentially, never linearly. The heading
        // relaxes toward the straight line at its own rate, and the to-rod direction itself
        // rotates as the lure moves — together they bend each tug into an arc.
        pullSpeed *= Mathf.Exp(-s.pullDecayRate * dt);
        headingDeg *= Mathf.Exp(-s.headingRecenterRate * dt);

        if (pullSpeed > PullCutoffSpeed && dist > s.retractDistance)
        {
            Vector3 driftDir = Quaternion.AngleAxis(headingDeg, Vector3.up) * toRodDirHoriz;
            Vector3 vel = rb.linearVelocity;
            // Direct set, Y preserved for buoyancy. Below the cutoff we deliberately leave
            // the last set velocity alone so the water damping carries a lingering drift.
            rb.linearVelocity = new Vector3(driftDir.x * pullSpeed, vel.y, driftDir.z * pullSpeed);
        }
        else if (pullSpeed <= PullCutoffSpeed)
        {
            pullSpeed = 0f;
        }

        nudgeIntensity = Mathf.Max(0f, nudgeIntensity - s.intensityDecayRate * dt);

        activeBobber.lureVisualPitchDeg = s.visualPitchAngleDeg * nudgeIntensity;
        activeBobber.lureBobOffset =
            Mathf.Sin(Time.time * s.visualBobFrequencyHz * Mathf.PI * 2f)
            * s.visualBobAmplitude * nudgeIntensity;

        // Popper surface chatter: a constant fast nose-bounce written into a separate channel the
        // BobberController applies directly (the buoyancy slerp would damp this frequency away).
        // Layers on top of the speedboat lean above without touching the reel/yank physics.
        UpdatePopperVisual(activeBobber, isPopper, dt, in s);

        if (dist <= s.retractDistance)
        {
            ClearVisualState(activeBobber);
            return Outcome.RetractRequested;
        }
        return Outcome.Continue;
    }

    // Drives the popper's constant surface chatter (nose bounce + periodic nose-splash). Writes the
    // instantaneous bounce into BobberController.popperBounceDeg and flags isPopperLure so its
    // ApplyBuoyancy applies the bounce directly. A basic lure (isPopper false) clears the channel.
    private void UpdatePopperVisual(BobberController lure, bool isPopper, float dt, in Settings s)
    {
        if (!isPopper)
        {
            lure.isPopperLure = false;
            lure.popperBounceDeg = 0f;
            popperSplashTimer = 0f;
            return;
        }

        lure.isPopperLure = true;

        // The chatter only plays while the lure is actually moving (being tugged or reeled).
        // nudgeIntensity is the same movement signal that drives the speedboat lean — 1 on a
        // yank/crank, decaying to 0 when motion stops — so the bounce fades in on a tug and dies
        // down as the lure settles. A still popper sits level: no bounce, no splash.
        lure.popperBounceDeg =
            Mathf.Sin(Time.time * s.popperBounceFrequencyHz * Mathf.PI * 2f)
            * s.popperBounceAmplitudeDeg * nudgeIntensity;

        if (s.popperSplashInterval > 0f && nudgeIntensity > PopperSplashMinIntensity)
        {
            popperSplashTimer -= dt;
            if (popperSplashTimer <= 0f)
            {
                lure.PlayPopperSplash();
                popperSplashTimer = s.popperSplashInterval;
            }
        }
        else
        {
            // Still: keep the timer armed at 0 so the first tug splashes promptly.
            popperSplashTimer = 0f;
        }
    }

    private void ClearVisualState(BobberController lure)
    {
        if (lure != null)
        {
            lure.lureVisualPitchDeg = 0f;
            lure.lureBobOffset = 0f;
            lure.isPopperLure = false;
            lure.popperBounceDeg = 0f;
        }
        nudgeIntensity = 0f;
        pullSpeed = 0f;
        headingDeg = 0f;
        popperSplashTimer = 0f;
        BroadcastReelHeld(false);
    }

    private static Vector3 GetTipDirectionHorizontal(BobberController lure)
    {
        Transform attach = lure != null ? lure.LineAttachPoint : null;
        if (lure == null || attach == null || attach == lure.transform) return Vector3.forward;
        Vector3 dir = attach.position - lure.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return Vector3.forward;
        return dir.normalized;
    }
}
