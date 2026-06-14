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
        public KeyCode chargeJumpKey;
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

    private CharacterController characterController;
    private Animator animator;
    private int hashBallIn;
    private int hashBallOut;
    private PlayerSquashStretch squash;
    private Action notifyAction;

    public JumpPhase Phase => phase;
    public float ChargeTimer => chargeTimer;

    public void Init(CharacterController cc, Animator anim, int hashBallIn, int hashBallOut,
                     PlayerSquashStretch squash, Action notifyAction)
    {
        this.characterController = cc;
        this.animator = anim;
        this.hashBallIn = hashBallIn;
        this.hashBallOut = hashBallOut;
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
            // Charge interrupted before launch (UI opened, controls locked, etc). We already fired
            // BallTransitionIn entering the charge, so morph back out — otherwise the player would
            // be stranded in ball form once the BallForm hold state is wired up.
            phase = JumpPhase.None;
            chargeTimer = 0f;
            if (animator != null) animator.SetTrigger(hashBallOut);
            return;
        }

        switch (phase)
        {
            case JumpPhase.None:
                if (!inputDisabled && characterController.isGrounded
                    && (Input.GetKeyDown(cfg.chargeJumpKey) || GamepadInput.JumpPressed))
                {
                    phase = JumpPhase.Charging;
                    chargeTimer = 0f;
                    if (animator != null) animator.SetTrigger(hashBallIn);
                    notifyAction?.Invoke();
                }
                break;

            case JumpPhase.Charging:
                chargeTimer = Mathf.Min(chargeTimer + Time.deltaTime, cfg.maxChargeTime);
                // Launch when no device holds jump anymore — either may have started the charge.
                if (!Input.GetKey(cfg.chargeJumpKey) && !GamepadInput.JumpHeld)
                {
                    LaunchJump(chargeTimer / cfg.maxChargeTime, ref targetVelocity,
                               modelForward, fallbackForward, in cfg);
                }
                break;

            case JumpPhase.Launched:
                ApplyAirSteer(ref targetVelocity, steerDir, in cfg);
                if (!characterController.isGrounded) airborneSinceLaunch = true;
                else if (airborneSinceLaunch) BounceOnLand(ref targetVelocity, in cfg);
                break;

            case JumpPhase.Bounced:
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
        notifyAction?.Invoke();
    }

    private void BounceOnLand(ref Vector3 targetVelocity, in JumpConfig cfg)
    {
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
