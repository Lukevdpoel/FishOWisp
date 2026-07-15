using System.Collections;
using UnityEngine;

// Part of PlayerController (partial class). Serialized fields live in PlayerController.cs.
public partial class PlayerController
{
    private void HandleMovement()
    {
        float yVelocity = targetVelocity.y;

        var jumpPhase = chargeJump.Phase;
        if (jumpPhase == ChargeJumpController.JumpPhase.Charging)
        {
            // Locked in place while charging — bleed off any horizontal momentum. The player can
            // still aim, though: HandleRotation steers the model from the move input, and the
            // launch fires along the model's forward.
            Vector3 horiz = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
            horiz = Vector3.MoveTowards(horiz, Vector3.zero, deceleration * Time.deltaTime);
            targetVelocity = new Vector3(horiz.x, yVelocity, horiz.z);
            return;
        }

        if (jumpPhase == ChargeJumpController.JumpPhase.Launched || jumpPhase == ChargeJumpController.JumpPhase.Bounced)
        {
            // Trajectory mostly locked once launched — gravity still applies via HandleGravity,
            // and ChargeJumpController.Tick applies the slight air steering.
            return;
        }

        Vector3 moveDirection = GetMoveDirection(out float inputMagnitude);
        bool hasMovementInput = inputMagnitude > 0.1f;

        // Sprint is hold-to-run on both devices: keep Shift (keyboard) or the bound pad
        // control held to turn walking into running, release it to drop back to a walk.
        // (No need to gate on inputDisabled — GetMoveDirection already zeroes input then.)
        isSprinting = hasMovementInput
                      && (KeyInput.SprintHeld || GamepadInput.SprintHeld);

        float currentSpeed = (isSprinting ? sprintSpeed : walkSpeed) * Mathf.Clamp01(inputMagnitude);
        Vector3 desiredHorizontal = moveDirection * currentSpeed;

        Vector3 currentHorizontal = new Vector3(targetVelocity.x, 0f, targetVelocity.z);

        bool freshPress = hasMovementInput && !hadMovementInputLast;
        if (freshPress && currentHorizontal.magnitude < tapKickThreshold)
            currentHorizontal = moveDirection * tapKickSpeed;
        hadMovementInputLast = hasMovementInput;

        float rate = hasMovementInput ? acceleration : deceleration;
        Vector3 newHorizontal = Vector3.MoveTowards(currentHorizontal, desiredHorizontal, rate * Time.deltaTime);

        targetVelocity = new Vector3(newHorizontal.x, yVelocity, newHorizontal.z);
    }

    // Camera-relative horizontal direction of the current move input, shared by walking, the
    // charge-jump aim, and air steering. Returns Vector3.zero (and inputMagnitude 0) when input
    // is disabled or there's no meaningful deflection. Keyboard wins when both devices are active;
    // otherwise the left stick drives, and its deflection sets inputMagnitude so small tilts read
    // as slower input than a full push.
    private Vector3 GetMoveDirection(out float inputMagnitude)
    {
        inputMagnitude = 0f;

        bool inputDisabled = InventoryUI.IsInventoryOpen || NoteMenu.IsNotebookOpen || areControlsLocked || isLockedOnFish;
        if (inputDisabled) return Vector3.zero;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector2 moveInput = Vector2.ClampMagnitude(new Vector2(h, v), 1f);
        if (moveInput.sqrMagnitude < 0.01f) moveInput = GamepadInput.Move;

        inputMagnitude = moveInput.magnitude;
        if (inputMagnitude <= 0.1f) { inputMagnitude = 0f; return Vector3.zero; }

        Transform camTransform;
        if (useStaticCamera && staticCameraTarget != null)
            camTransform = staticCameraTarget;
        else
            camTransform = cameraController != null ? cameraController.CameraTransform : transform;

        Vector3 camForward = camTransform.forward;
        Vector3 camRight = camTransform.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        return (camForward * moveInput.y + camRight * moveInput.x).normalized;
    }

    public void SetFacing(Quaternion worldRotation)
    {
        if (playerModel == null) return;
        Vector3 flatForward = worldRotation * Vector3.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) return;
        Quaternion flat = Quaternion.LookRotation(flatForward.normalized);
        playerModel.rotation = flat;
        targetModelRotation = flat;
    }

    private void HandleRotation()
    {
        if (areControlsLocked) return;

        if (chargeJump.Phase == ChargeJumpController.JumpPhase.Charging)
        {
            // While charging, the player is pinned in place but free to re-aim with the normal
            // directional input. Face the input direction so the launch (which fires along the
            // model's forward) goes where they point; hold the current aim when there's no input.
            Vector3 aimDir = GetMoveDirection(out _);
            if (aimDir.sqrMagnitude > 0.001f)
                targetModelRotation = Quaternion.LookRotation(aimDir);
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetModelRotation, rotationSpeed * Time.deltaTime);
            return;
        }

        if (chargeJump.Phase == ChargeJumpController.JumpPhase.Launched)
        {
            if (enableBallTumble)
            {
                // Ball-form tumble is applied in LateUpdate (PlayerBallTumble.TickReset + ApplySpin) —
                // after squash/stretch and the Animator — so it spins about the mesh center without being
                // overwritten, and around the camera read so the view stays stable.
                return;
            }

            // Arc-look (initial flight only): face the travel heading and pitch the nose along the arc —
            // up while rising, down while falling — so the model arcs like a leaping slime. The pitch is
            // the velocity's elevation angle. Once it bounces, the branch below rights it back upright.
            Vector3 horizVel = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
            float horizMag = horizVel.magnitude;
            if (horizMag > 0.01f)
            {
                Quaternion yaw = Quaternion.LookRotation(horizVel / horizMag, Vector3.up);
                if (enableJumpArcLook)
                {
                    // Negative pitch about local right = nose up (Unity's +X rotation pitches down).
                    float pitchDeg = Mathf.Atan2(targetVelocity.y, horizMag) * Mathf.Rad2Deg * jumpArcPitchScale;
                    targetModelRotation = yaw * Quaternion.AngleAxis(-pitchDeg, Vector3.right);
                }
                else
                {
                    targetModelRotation = yaw;
                }
            }
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetModelRotation, rotationSpeed * Time.deltaTime);
            return;
        }

        if (chargeJump.Phase != ChargeJumpController.JumpPhase.None)
        {
            // Bounced: the arc is done — reset to the default upright pose (facing the travel heading,
            // no pitch) and hold it, so the rebound and final landing settle on its feet instead of
            // arcing again. Clearing the pitch here also stops a pitched rotation lingering into the
            // grounded state if horizontal speed is low at touchdown.
            Vector3 horizVel = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
            if (horizVel.sqrMagnitude > 0.01f)
                targetModelRotation = Quaternion.LookRotation(horizVel.normalized);
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetModelRotation, rotationSpeed * Time.deltaTime);
            return;
        }

        // Grounded / no jump: re-arm the tumble so the next launch rolls a fresh axis.
        ballTumble.Disarm();

        if (isLockedOnFish && fishLockTarget != null)
        {
            Vector3 dirToFish = fishLockTarget.position - playerModel.position;
            dirToFish.y = 0f;
            if (dirToFish.sqrMagnitude > 0.001f)
                targetModelRotation = Quaternion.LookRotation(dirToFish.normalized);
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetModelRotation, rotationSpeed * Time.deltaTime);
            return;
        }

        if (fishingAnimHandler != null && fishingAnimHandler.IsCasting)
        {
            targetModelRotation = playerModel.rotation;
            return;
        }

        if (new Vector3(targetVelocity.x, 0, targetVelocity.z).magnitude > 0.1f)
        {
            Vector3 lookDirection = new Vector3(targetVelocity.x, 0, targetVelocity.z);
            targetModelRotation = Quaternion.LookRotation(lookDirection);
        }

        playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetModelRotation, rotationSpeed * Time.deltaTime);
    }

    // Toggles the configured renderers off while a charge jump is active (ball form) and back on
    // when it ends. Only flips on state change. GameObjects/physics stay untouched.
    private void UpdateJumpVisuals()
    {
        // Charge-only sprite: hidden strictly while the charge is HELD, back the moment it's released
        // (launch) or cancelled — a tighter window than the group below, which spans the whole flight.
        // Polled like the rest so no release/cancel path can leave it stuck hidden.
        if (hideSpriteWhileCharging != null)
        {
            bool show = chargeJump.Phase != ChargeJumpController.JumpPhase.Charging;
            if (hideSpriteWhileCharging.enabled != show) hideSpriteWhileCharging.enabled = show;
        }

        // Hidden while balling up: morphing in / holding during the charge (Charging) and flying
        // (Launched). They reappear at the bounce (Bounced), where the morph-out animation plays.
        bool hide = chargeJump.Phase == ChargeJumpController.JumpPhase.Charging
                 || chargeJump.Phase == ChargeJumpController.JumpPhase.Launched;
        if (hide == jumpVisualsHidden) return;
        jumpVisualsHidden = hide;

        if (hideRenderersDuringJump != null)
            foreach (var r in hideRenderersDuringJump)
                if (r != null) r.enabled = !hide;

        // Let runtime-spawned visuals (the instantiated bobber + line) hide themselves.
        BallJumpVisibilityChanged?.Invoke(hide);
    }

    private void HandleGravity()
    {
        if (characterController.isGrounded && targetVelocity.y < 0f) targetVelocity.y = groundedDownForce;
        else targetVelocity.y += gravity * Time.deltaTime;
    }

    private void HandleAnimation()
    {
        if (animator)
        {
            float horizontalSpeed = new Vector3(targetVelocity.x, 0, targetVelocity.z).magnitude;

            if (areControlsLocked || InventoryUI.IsInventoryOpen || NoteMenu.IsNotebookOpen || isLockedOnFish)
            {
                horizontalSpeed = 0f;
            }

            animator.SetFloat(hashSpeed, horizontalSpeed, speedAnimDampTime, Time.deltaTime);
        }
    }

    private void HandleIdleAnimation()
    {
        bool isMoving = !areControlsLocked && !InventoryUI.IsInventoryOpen &&
                        (new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).magnitude > 0.1f
                         || GamepadInput.Move.magnitude > 0.1f);
        bool isRotatingCamera = !areControlsLocked && !InventoryUI.IsInventoryOpen
                                && (Input.GetMouseButton(1) || GamepadInput.Look.magnitude > 0.1f);
        if (InventoryUI.IsInventoryOpen || NoteMenu.IsNotebookOpen) { isMoving = false; isRotatingCamera = false; }

        if (isMoving || isRotatingCamera || isLockedOnFish) NotifyOfAction();
        else idleTimer += Time.deltaTime;

        if (idleTimer >= sitDownHoldTime && allowSitdown)
        {
            animator.SetTrigger(hashSitdown);
            allowSitdown = false;
        }
    }

    private void TickChargeJump()
    {
        bool inputDisabled = InventoryUI.IsInventoryOpen || NoteMenu.IsNotebookOpen || areControlsLocked || isLockedOnFish;

        // The press that closes a menu must not double as a jump: while input is disabled the latch
        // arms, and it only clears once the jump button is fully up afterwards.
        if (inputDisabled)
            jumpBlockedUntilRelease = true;
        else if (jumpBlockedUntilRelease && !KeyInput.JumpHeld && !GamepadInput.JumpHeld)
            jumpBlockedUntilRelease = false;

        Vector3 modelForward = playerModel ? playerModel.forward : transform.forward;
        Vector3 steerDir = GetMoveDirection(out _);
        var cfg = BuildJumpConfig();
        chargeJump.Tick(ref targetVelocity, modelForward, transform.forward, steerDir,
                        inputDisabled || jumpBlockedUntilRelease, in cfg);
    }

    private ChargeJumpController.JumpConfig BuildJumpConfig()
    {
        return new ChargeJumpController.JumpConfig
        {
            maxChargeTime = maxChargeTime,
            minLaunchCharge = minLaunchCharge,
            forwardByCharge = forwardByCharge,
            upwardByCharge = upwardByCharge,
            bounceForwardMultiplier = bounceForwardMultiplier,
            bounceUpwardMultiplier = bounceUpwardMultiplier,
            airSteerAccel = airSteerAccel,
            wallBounceRestitution = wallBounceRestitution,
            wallBounceMaxSurfaceY = wallBounceMaxSurfaceY,
            chainJumpWindow = chainJumpWindow,
            chainJumpWindowAfterBounce = chainJumpWindowAfterBounce,
            chainFullChargeThreshold = chainFullChargeThreshold,
        };
    }

}
