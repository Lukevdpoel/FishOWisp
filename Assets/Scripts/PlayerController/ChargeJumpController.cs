using System;
using UnityEngine;

// Charge-jump state machine: None → Charging → Launched → (optional Bounced) → None.
//
//   Charging: hold the jump key. Squash builds up. Releasing launches.
//   Launched: ballistic flight with locked horizontal velocity.
//   Bounced:  one rebound after first landing, lower energy.
//
// Owns the launch math and the state. Calls into PlayerSquashStretch (bounce-impact trigger) on
// transitions, so callers don't need to. The in-air ball tumble is owned by PlayerController.HandleRotation.
public class ChargeJumpController
{
    public enum JumpPhase { None, Charging, Launched, Bounced }

    public struct JumpConfig
    {
        public float maxChargeTime;
        public float minLaunchCharge;
        public AnimationCurve forwardByCharge;
        public AnimationCurve upwardByCharge;
        public float bounceForwardMultiplier;
        public float bounceUpwardMultiplier;
        // Horizontal acceleration (units/s^2) applied toward the steer input while airborne.
        // Redirects momentum but never pushes speed past the launch/bounce cap, so it reads as
        // a slight in-air course correction rather than free flight. 0 disables steering.
        public float airSteerAccel;
        // Fraction of speed retained when ricocheting off a mid-air obstacle (0..1).
        public float wallBounceRestitution;
        // A hit surface bounces the player when its normal.y is below this; flatter (more upward)
        // surfaces are treated as ground and left to the landing bounce.
        public float wallBounceMaxSurfaceY;
        // Chain jump: a jump press buffered within this many seconds BEFORE the fully-charged jump's
        // landing touchdown relaunches at the same velocity instead of taking the weak rebound.
        // 0 disables the pre-landing chain.
        public float chainJumpWindow;
        // The tighter window AFTER the bounce: a press this many seconds past touchdown still chains.
        // Kept shorter than chainJumpWindow so a late press is harder to land. 0 disables post-bounce chaining.
        public float chainJumpWindowAfterBounce;
        // Minimum charge normal (0..1) for a launch to count as "fully charged" and so chain-eligible.
        public float chainFullChargeThreshold;
    }

    private JumpPhase phase = JumpPhase.None;
    private float chargeTimer;
    private float launchHorizontalSpeed;
    private float launchUpwardSpeed;
    private Vector3 launchDirection = Vector3.forward;
    private bool airborneSinceLaunch;
    // Upper bound on horizontal speed during air steering — the speed set at the last launch
    // or bounce, so steering can curve the path but not accelerate beyond the original throw.
    private float airSteerSpeedCap;

    // --- Chain jump ---
    // Whether the active jump was launched from a full charge. Only a fully-charged jump can be
    // chained, and each chain re-applies that same full velocity so the chain can keep going.
    private bool launchedAtFullCharge;
    // Time.time of the most recent in-air jump press and of the most recent landing bounce. A press
    // within cfg.chainJumpWindow of the bounce (either side) relaunches at full velocity.
    private float lastJumpPressTime = float.NegativeInfinity;
    private float lastBounceTime = float.NegativeInfinity;
    // Raised on a chain so PlayerController can fire the elastic launch stretch even when the phase
    // doesn't change (Launched→Launched), which its phase-edge detection would otherwise miss.
    private bool chainStretchPending;
    // Set once a mid-air wall/obstacle ricochet happens — it kills chaining for the rest of the jump
    // sequence (the combo only survives clean landing bounces). Cleared only by a fresh charged launch,
    // not by a chain relaunch, so a single wall clip ends the run until the player recharges.
    private bool wallBouncedSinceLaunch;
    // How many times the CURRENT run has chained (perfect-timed relaunches since the charged launch).
    // Reset to 0 on every fresh charged launch, incremented on each ChainJump. Broadcast on
    // OnJumpChainExtended so listeners (e.g. AchievementManager) can reward long chains without this
    // class knowing anything about achievements.
    private int chainCount;

    /// <summary>
    /// Raised each time an active jump successfully chains, carrying the running chain length (1 for the
    /// first chain off a charged launch, 2 for the next, ...). Static because ChargeJumpController is a
    /// plain (non-Unity) object owned by PlayerController; a static event is the simplest hook for a
    /// persistent listener. Resets implicitly on the next charged launch (the counter restarts at 0).
    /// </summary>
    public static event Action<int> OnJumpChainExtended;

    private CharacterController characterController;
    private Animator animator;
    private int hashBallIn;
    private int hashBallOut;
    private int hashBallToForm;
    private PlayerSquashStretch squash;
    private Action notifyAction;

    public JumpPhase Phase => phase;
    public float ChargeTimer => chargeTimer;

    // One-shot: true on the frame a chain relaunch happened. PlayerController polls this to fire the
    // elastic launch stretch on a same-phase chain (Launched→Launched), which its phase-edge
    // detection can't see. Reading it clears it.
    public bool ConsumeChainStretch()
    {
        if (!chainStretchPending) return false;
        chainStretchPending = false;
        return true;
    }

    public void Init(CharacterController cc, Animator anim, int hashBallIn, int hashBallOut, int hashBallToForm,
                     PlayerSquashStretch squash, Action notifyAction)
    {
        this.characterController = cc;
        this.animator = anim;
        this.hashBallIn = hashBallIn;
        this.hashBallOut = hashBallOut;
        this.hashBallToForm = hashBallToForm;
        this.squash = squash;
        this.notifyAction = notifyAction;
    }

    // Called from PlayerController.Update. Mutates targetVelocity on launch/bounce.
    // modelForward: the player model's forward (preferred for launch direction).
    // fallbackForward: PlayerController.transform.forward, used if the model is unrotated.
    // steerDir: camera-relative horizontal input direction (zero when none), used to nudge the
    //           airborne trajectory while Launched/Bounced.
    public void Tick(ref Vector3 targetVelocity,
                     Vector3 modelForward, Vector3 fallbackForward, Vector3 steerDir,
                     bool inputDisabled, in JumpConfig cfg)
    {
        if (characterController == null) return;

        if (inputDisabled && phase == JumpPhase.Charging)
        {
            // Charge interrupted before launch (UI opened, controls locked, etc). The body is mid/holding
            // BallMorphIn, so settle it into BallForm then morph back out — otherwise it'd be stranded in
            // the half-balled hold pose (BallMorphIn has no direct exit; BallForm -> BallMorphOut does).
            phase = JumpPhase.None;
            chargeTimer = 0f;
            if (animator != null)
            {
                animator.SetTrigger(hashBallToForm);
                animator.SetTrigger(hashBallOut);
            }
            return;
        }

        // Buffer in-air jump presses for chaining. Recording the time lets a press shortly BEFORE the
        // landing bounce still chain (checked at touchdown in the Launched case); presses AFTER the
        // bounce are handled live in the Bounced case.
        bool jumpPressed = !inputDisabled && (KeyInput.JumpPressed || GamepadInput.JumpPressed);
        if (jumpPressed && (phase == JumpPhase.Launched || phase == JumpPhase.Bounced))
            lastJumpPressTime = Time.time;

        switch (phase)
        {
            case JumpPhase.None:
                if (!inputDisabled && characterController.isGrounded
                    && (KeyInput.JumpPressed || GamepadInput.JumpPressed))
                {
                    // Morph into the ball as the charge begins: BallMorphIn plays then holds on its last
                    // frame (it no longer auto-advances to BallForm — that waits for BallToForm on release).
                    phase = JumpPhase.Charging;
                    chargeTimer = 0f;
                    if (animator != null) animator.SetTrigger(hashBallIn);
                    notifyAction?.Invoke();
                }
                break;

            case JumpPhase.Charging:
                chargeTimer = Mathf.Min(chargeTimer + Time.deltaTime, cfg.maxChargeTime);
                // Launch when no device holds jump anymore — either may have started the charge.
                if (!KeyInput.JumpHeld && !GamepadInput.JumpHeld)
                {
                    LaunchJump(chargeTimer / cfg.maxChargeTime, ref targetVelocity,
                               modelForward, fallbackForward, in cfg);
                }
                break;

            case JumpPhase.Launched:
                ApplyAirSteer(ref targetVelocity, steerDir, in cfg);
                if (!characterController.isGrounded) airborneSinceLaunch = true;
                else if (airborneSinceLaunch)
                {
                    // Landing. A jump press buffered within the pre-landing window relaunches at full
                    // velocity (perfect bounce); otherwise take the normal weak rebound.
                    if (CanChain(Time.time - lastJumpPressTime, cfg.chainJumpWindow))
                        ChainJump(ref targetVelocity, in cfg);
                    else
                        BounceOnLand(ref targetVelocity, in cfg);
                }
                break;

            case JumpPhase.Bounced:
                // A jump press within the tighter post-bounce window also relaunches at full velocity.
                if (jumpPressed && CanChain(Time.time - lastBounceTime, cfg.chainJumpWindowAfterBounce))
                {
                    ChainJump(ref targetVelocity, in cfg);
                    break;
                }
                ApplyAirSteer(ref targetVelocity, steerDir, in cfg);
                if (!characterController.isGrounded)
                {
                    airborneSinceLaunch = true;
                }
                else if (airborneSinceLaunch)
                {
                    // Morph-out already fired at the bounce (BounceOnLand) — by now the player is
                    // mid/after morph-out, so this transition just ends the jump. HandleRotation
                    // eases the model back upright from here.
                    phase = JumpPhase.None;
                    airborneSinceLaunch = false;
                }
                break;
        }
    }

    // Charging-phase deceleration applied by PlayerController.HandleMovement. Exposes the
    // current charge normal so squash/stretch can author its scale via Tick.
    public float ChargeNormalized(float maxChargeTime)
    {
        if (maxChargeTime <= 0f) return 0f;
        return Mathf.Clamp01(chargeTimer / maxChargeTime);
    }

    private void LaunchJump(float chargeNorm, ref Vector3 targetVelocity,
                            Vector3 modelForward, Vector3 fallbackForward, in JumpConfig cfg)
    {
        // Capture chain eligibility from the raw charge (before the min-launch floor below) — only a
        // jump released at (near) full charge can be chained off its landing bounce.
        launchedAtFullCharge = chargeNorm >= cfg.chainFullChargeThreshold;
        wallBouncedSinceLaunch = false;
        chainCount = 0; // a fresh charged launch starts a new chain run
        lastJumpPressTime = float.NegativeInfinity;
        lastBounceTime = float.NegativeInfinity;

        chargeNorm = Mathf.Clamp01(Mathf.Max(chargeNorm, cfg.minLaunchCharge));
        launchHorizontalSpeed = cfg.forwardByCharge.Evaluate(chargeNorm);
        launchUpwardSpeed = cfg.upwardByCharge.Evaluate(chargeNorm);

        Vector3 fwd = modelForward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = fallbackForward;
        launchDirection = fwd.normalized;

        Vector3 horiz = launchDirection * launchHorizontalSpeed;
        targetVelocity = new Vector3(horiz.x, launchUpwardSpeed, horiz.z);
        airSteerSpeedCap = launchHorizontalSpeed;

        phase = JumpPhase.Launched;
        airborneSinceLaunch = false;
        // Release: advance the held BallMorphIn into BallForm (the morph-in already played during the charge).
        if (animator != null) animator.SetTrigger(hashBallToForm);
        notifyAction?.Invoke();
    }

    private void BounceOnLand(ref Vector3 targetVelocity, in JumpConfig cfg)
    {
        lastBounceTime = Time.time; // arm the post-bounce chain window
        // Steering may have curved the launch direction by now — bounce off the current heading
        // rather than the original launch vector so the rebound continues where the player aimed.
        Vector3 currentHoriz = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
        if (currentHoriz.sqrMagnitude > 0.001f) launchDirection = currentHoriz.normalized;

        Vector3 horiz = launchDirection * launchHorizontalSpeed * cfg.bounceForwardMultiplier;
        targetVelocity = new Vector3(horiz.x, launchUpwardSpeed * cfg.bounceUpwardMultiplier, horiz.z);
        airSteerSpeedCap = launchHorizontalSpeed * cfg.bounceForwardMultiplier;
        phase = JumpPhase.Bounced;
        airborneSinceLaunch = false;
        squash.TriggerBounceImpact();
        // Morph back to normal on this last bounce: the player un-balls during the rebound and lands
        // as the character, rather than staying a ball until the final settle.
        if (animator != null) animator.SetTrigger(hashBallOut);
    }

    // True when a fully-charged jump can chain off an event — a press buffered just before landing,
    // or a press just after the bounce — that happened within `window` seconds (each side has its own).
    private bool CanChain(float secondsSinceEvent, float window)
    {
        return window > 0f
            && launchedAtFullCharge
            && !wallBouncedSinceLaunch
            && secondsSinceEvent >= 0f
            && secondsSinceEvent <= window;
    }

    // Relaunches the active jump at its original full-charge velocity without re-charging, triggered
    // by a well-timed jump press around the landing bounce. The chained jump keeps the full velocity,
    // so launchedAtFullCharge stays true and the player can keep the bounce combo going.
    private void ChainJump(ref Vector3 targetVelocity, in JumpConfig cfg)
    {
        bool reBallNeeded = phase == JumpPhase.Bounced; // the bounce already started morphing out

        // Continue along the current heading — steering or wall bounces may have curved it.
        Vector3 currentHoriz = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
        if (currentHoriz.sqrMagnitude > 0.001f) launchDirection = currentHoriz.normalized;

        Vector3 horiz = launchDirection * launchHorizontalSpeed;
        targetVelocity = new Vector3(horiz.x, launchUpwardSpeed, horiz.z);
        airSteerSpeedCap = launchHorizontalSpeed;

        phase = JumpPhase.Launched;
        airborneSinceLaunch = false;
        lastJumpPressTime = float.NegativeInfinity; // consume the press
        lastBounceTime = float.NegativeInfinity;
        chainStretchPending = true;

        chainCount++;
        OnJumpChainExtended?.Invoke(chainCount);

        squash.TriggerBounceImpact();

        // Re-ball only if the landing bounce already began morphing the body back to normal. From
        // Launched the body is still in BallForm, so no animator change is needed there.
        if (animator != null && reBallNeeded)
        {
            animator.SetTrigger(hashBallIn);     // AnyState → BallMorphIn
            animator.SetTrigger(hashBallToForm); // BallMorphIn → BallForm
        }
        notifyAction?.Invoke();
    }

    // Ricochets the player off a mid-air obstacle. Called from PlayerController.OnControllerColliderHit
    // for every contact, so it self-gates: it only acts while airborne in a jump, only on wall/overhang
    // surfaces (steeper than wallBounceMaxSurfaceY — floors are left to the landing bounce), and only
    // when actually moving into the surface (so it can't re-reflect every frame while sliding away).
    public void HandleColliderHit(Vector3 hitNormal, ref Vector3 targetVelocity, in JumpConfig cfg)
    {
        if (phase != JumpPhase.Launched && phase != JumpPhase.Bounced) return;
        if (characterController == null || characterController.isGrounded) return;
        if (hitNormal.y >= cfg.wallBounceMaxSurfaceY) return;
        if (Vector3.Dot(targetVelocity, hitNormal) >= 0f) return;

        // A mid-air ricochet breaks the chain combo — no more chaining until the next charged launch.
        wallBouncedSinceLaunch = true;

        // Angle-correct reflection across the surface, scaled by restitution for impact energy loss.
        targetVelocity = Vector3.Reflect(targetVelocity, hitNormal) * cfg.wallBounceRestitution;

        // The hit bled off momentum — drop the steer cap to the new horizontal speed so air steering
        // can't recover the lost energy. Also re-point launchDirection so a later ground bounce
        // continues along the ricochet heading.
        Vector3 horiz = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
        float horizSpeed = horiz.magnitude;
        airSteerSpeedCap = Mathf.Min(airSteerSpeedCap, horizSpeed);
        launchHorizontalSpeed = horizSpeed;
        if (horiz.sqrMagnitude > 0.001f) launchDirection = horiz.normalized;

        squash.TriggerBounceImpact();
    }

    // Nudges the airborne horizontal velocity toward steerDir, clamped to the speed set at the
    // last launch/bounce. The clamp is what keeps it a gentle course correction: input can rotate
    // the velocity vector but never grow it past the original throw.
    private void ApplyAirSteer(ref Vector3 targetVelocity, Vector3 steerDir, in JumpConfig cfg)
    {
        if (cfg.airSteerAccel <= 0f || steerDir.sqrMagnitude < 0.001f) return;
        if (characterController.isGrounded) return;

        Vector3 horiz = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
        horiz += steerDir * cfg.airSteerAccel * Time.deltaTime;
        horiz = Vector3.ClampMagnitude(horiz, airSteerSpeedCap);
        targetVelocity = new Vector3(horiz.x, targetVelocity.y, horiz.z);
    }
}
