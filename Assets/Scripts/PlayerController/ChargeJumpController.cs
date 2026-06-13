using System;
using UnityEngine;

// Charge-jump state machine: None → Charging → Launched → (optional Bounced) → None.
//
//   Charging: hold the jump key. Squash builds up. Releasing launches.
//   Launched: ballistic flight with locked horizontal velocity.
//   Bounced:  one rebound after first landing, lower energy.
//
// Owns the launch math and the state. Calls into PlayerAirborneTumble (seed/reset/onBounce)
// and PlayerSquashStretch (bounce-impact trigger) on transitions, so callers don't need to.
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
        // Forwarded to PlayerAirborneTumble.Seed on launch.
        public float tumbleDegPerUnitSpeed;
        public float tumbleAxisJitter;
        public float tumbleSpeedJitter;
    }

    private JumpPhase phase = JumpPhase.None;
    private float chargeTimer;
    private float launchHorizontalSpeed;
    private float launchUpwardSpeed;
    private Vector3 launchDirection = Vector3.forward;
    private bool airborneSinceLaunch;

    private CharacterController characterController;
    private Animator animator;
    private int hashBallIn;
    private int hashBallOut;
    private PlayerAirborneTumble tumble;
    private PlayerSquashStretch squash;
    private Action notifyAction;

    public JumpPhase Phase => phase;
    public float ChargeTimer => chargeTimer;

    public void Init(CharacterController cc, Animator anim, int hashBallIn, int hashBallOut,
                     PlayerAirborneTumble tumble, PlayerSquashStretch squash,
                     Action notifyAction)
    {
        this.characterController = cc;
        this.animator = anim;
        this.hashBallIn = hashBallIn;
        this.hashBallOut = hashBallOut;
        this.tumble = tumble;
        this.squash = squash;
        this.notifyAction = notifyAction;
    }

    // Called from PlayerController.Update. Mutates targetVelocity on launch/bounce.
    // modelForward: the player model's forward (preferred for launch direction).
    // fallbackForward: PlayerController.transform.forward, used if the model is unrotated.
    public void Tick(ref Vector3 targetVelocity,
                     Vector3 modelForward, Vector3 fallbackForward,
                     bool inputDisabled, in JumpConfig cfg)
    {
        if (characterController == null) return;

        if (inputDisabled && phase == JumpPhase.Charging)
        {
            phase = JumpPhase.None;
            chargeTimer = 0f;
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
                    tumble.ResetRotation();
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
                if (!characterController.isGrounded) airborneSinceLaunch = true;
                else if (airborneSinceLaunch) BounceOnLand(ref targetVelocity, in cfg);
                break;

            case JumpPhase.Bounced:
                if (!characterController.isGrounded)
                {
                    airborneSinceLaunch = true;
                }
                else if (airborneSinceLaunch)
                {
                    phase = JumpPhase.None;
                    airborneSinceLaunch = false;
                    tumble.ResetRotation();
                    if (animator != null) animator.SetTrigger(hashBallOut);
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

        phase = JumpPhase.Launched;
        airborneSinceLaunch = false;
        tumble.Seed(launchHorizontalSpeed, cfg.tumbleDegPerUnitSpeed, cfg.tumbleAxisJitter, cfg.tumbleSpeedJitter);
        notifyAction?.Invoke();
    }

    private void BounceOnLand(ref Vector3 targetVelocity, in JumpConfig cfg)
    {
        Vector3 horiz = launchDirection * launchHorizontalSpeed * cfg.bounceForwardMultiplier;
        targetVelocity = new Vector3(horiz.x, launchUpwardSpeed * cfg.bounceUpwardMultiplier, horiz.z);
        phase = JumpPhase.Bounced;
        airborneSinceLaunch = false;
        tumble.OnBounce(cfg.bounceForwardMultiplier);
        squash.TriggerBounceImpact();
    }
}
