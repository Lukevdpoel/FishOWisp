using UnityEngine;

public class RodCasting : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The TIP of the rod. Used for the actual bobber spawn position.")]
    public Transform throwOrigin;

    [Tooltip("NEW: A stationary point (child of Player Root) where the line starts. Prevents wobbling during animation.")]
    public Transform predictionOrigin; // <--- NEW FIELD

    [Tooltip("The visual model of the player to rotate.")]
    public Transform playerModel;

    [Header("Aiming Target")]
    [Tooltip("Prefab for the trajectory arc and landing sprite. Must have a CastingTargetController component.")]
    public GameObject castingTargetPrefab;

    [Header("Casting Power")]
    public float minThrowForce = 5f;
    public float maxThrowForce = 25f;
    public float chargeRate = 10f;

    [Tooltip("Gamepad only. Shapes how trigger pressure maps to throw force. 1 = linear (raw " +
             "trigger pull). Higher values bend the curve so the lower part of the trigger's " +
             "travel maps to gentle, short casts and you have to squeeze hard for a long one. " +
             "Because bobber distance grows with the SQUARE of force, a light press already " +
             "throws far on a linear map, so 2-3 here gives a far more even distance gradient.")]
    [Range(1f, 4f)] public float gamepadChargeCurve = 2.5f;

    [Tooltip("Gamepad only. How fast the live throw force chases the trigger pressure, per " +
             "second. Lower = smoother and more forgiving of trigger jitter (the charge bar you " +
             "see is exactly what gets cast, so it just settles a touch slower); higher = " +
             "snappier. 0 = instant, no smoothing.")]
    public float gamepadChargeSmoothing = 12f;

    [Header("Physics Prediction")]
    [Tooltip("MUST MATCH the 'Extra Gravity' value in your BobberController script.")]
    public float bobberExtraGravity = 30f;

    [Header("Aiming")]
    [Tooltip("The angle (in degrees) from the center to the edge of the allowed aiming cone.")]
    public float aimConeAngle = 45f;
    [Tooltip("How sensitive the aiming is to horizontal mouse movement.")]
    public float aimSensitivity = 2f;
    [Tooltip("Degrees per second the aim swings at full right-stick deflection.")]
    public float gamepadAimSpeed = 90f;
    [Tooltip("How smoothly the aim follows the mouse.")]
    public float aimSmoothing = 10f;
    [Tooltip("How fast the player model rotates to face the aim direction.")]
    public float playerAimRotationSpeed = 10f;

    private float currentThrowForce;
    private Vector3 throwDirection;
    private Vector3 targetAimDirection;
    private bool isCharging = false;
    // Latched at BeginCharging so a mid-charge device switch can't change how the force builds:
    // gamepad charges scale force by trigger pressure, keyboard charges ramp it over time.
    private bool chargingWithGamepad;
    private Camera mainCamera;

    private CastingTargetController activeCastingTarget;

    void Awake()
    {
        mainCamera = Camera.main;
    }

    private void OnEnable()
    {
        FishingEvents.OnStartCharging += BeginCharging;
        FishingEvents.OnCancelCharging += ReleaseCharge;
        FishingEvents.OnCancelFishing += Cancel;

        // After a scene swap, ensure stale charge state from a previous scene can't
        // leave the casting target or charge UI hanging in the new scene.
        isCharging = false;
        FishingEvents.OnToggleChargeUI?.Invoke(false);
        DestroyCastingTarget();
    }

    private void OnDisable()
    {
        FishingEvents.OnStartCharging -= BeginCharging;
        FishingEvents.OnCancelCharging -= ReleaseCharge;
        FishingEvents.OnCancelFishing -= Cancel;
    }

    void Update()
    {
        if (isCharging)
        {
            UpdateAimAndRotation();
            ChargeThrow();

            if (activeCastingTarget != null)
            {
                // --- FIX: Use predictionOrigin for the visual line ---
                // If predictionOrigin is missing, it safely falls back to throwOrigin
                Vector3 startPos = predictionOrigin != null ? predictionOrigin.position : throwOrigin.position;

                activeCastingTarget.UpdateTrajectory(startPos, throwDirection, currentThrowForce, bobberExtraGravity);
            }
        }
    }

    private void BeginCharging()
    {
        isCharging = true;
        chargingWithGamepad = GamepadInput.IsGamepadActive;
        currentThrowForce = minThrowForce;

        targetAimDirection = mainCamera.transform.forward;
        targetAimDirection.y = 0;
        targetAimDirection.Normalize();
        throwDirection = targetAimDirection;

        FishingEvents.OnToggleChargeUI?.Invoke(true);
        FishingEvents.OnChargeProgressNormalized?.Invoke(0f);

        if (castingTargetPrefab != null && activeCastingTarget == null)
        {
            GameObject targetInstance = Instantiate(castingTargetPrefab, Vector3.zero, Quaternion.identity);
            activeCastingTarget = targetInstance.GetComponent<CastingTargetController>();
            if (activeCastingTarget != null)
            {
                activeCastingTarget.Show();
            }
            else
            {
                Debug.LogError("Casting Target Prefab is missing the CastingTargetController script!");
                Destroy(targetInstance);
            }
        }
    }

    private void ChargeThrow()
    {
        if (chargingWithGamepad)
        {
            // Pressure-driven, live: the force tracks the trigger pull every frame — up AND down
            // — so the player dials distance in by how hard they hold RT. Press A while still
            // holding to cast at this value (FishingRodController); letting the trigger go fully
            // cancels the charge. Because A commits with the trigger still down, there's no
            // spring-back to bleed the force, so the live value is exactly what gets thrown.
            //
            // The raw trigger pull is bent by gamepadChargeCurve before mapping to force:
            // distance grows with the square of force, so a linear pull makes even a light press
            // throw far and crams the usable distance range into the bottom of the trigger. The
            // exponent spreads that range across the whole travel for a consistent gradient. A
            // little smoothing then chases the target so trigger jitter doesn't snap the force.
            float pull = Mathf.InverseLerp(GamepadInput.TriggerPressPoint, 1f, GamepadInput.ThrowChargeAnalog);
            float shaped = Mathf.Pow(pull, gamepadChargeCurve);
            float target = Mathf.Lerp(minThrowForce, maxThrowForce, shaped);
            currentThrowForce = gamepadChargeSmoothing > 0f
                ? Mathf.Lerp(currentThrowForce, target, 1f - Mathf.Exp(-gamepadChargeSmoothing * Time.deltaTime))
                : target;
        }
        else
        {
            currentThrowForce += chargeRate * Time.deltaTime;
            if (currentThrowForce > maxThrowForce) currentThrowForce = maxThrowForce;
        }

        FishingEvents.OnUpdateChargeUI?.Invoke(currentThrowForce, maxThrowForce);

        float range = maxThrowForce - minThrowForce;
        float normalized = range > 0.001f ? (currentThrowForce - minThrowForce) / range : 0f;
        FishingEvents.OnChargeProgressNormalized?.Invoke(Mathf.Clamp01(normalized));
    }

    private void ReleaseCharge()
    {
        if (!isCharging) return;
        isCharging = false;
        FishingEvents.OnToggleChargeUI?.Invoke(false);
        FishingEvents.OnThrowBobber?.Invoke(throwDirection, currentThrowForce);
        DestroyCastingTarget();
    }

    private void Cancel()
    {
        if (!isCharging) return;
        isCharging = false;
        FishingEvents.OnToggleChargeUI?.Invoke(false);
        DestroyCastingTarget();
    }

    private void DestroyCastingTarget()
    {
        if (activeCastingTarget != null)
        {
            Destroy(activeCastingTarget.gameObject);
            activeCastingTarget = null;
        }
    }

    private void UpdateAimAndRotation()
    {
        float aimDelta = Input.GetAxis("Mouse X") * aimSensitivity
                       + GamepadInput.Look.x * gamepadAimSpeed * Time.deltaTime;
        Quaternion rotation = Quaternion.AngleAxis(aimDelta, Vector3.up);
        targetAimDirection = rotation * targetAimDirection;

        Vector3 coneCenter = mainCamera.transform.forward;
        coneCenter.y = 0;
        coneCenter.Normalize();

        float angleToCenter = Vector3.Angle(coneCenter, targetAimDirection);
        if (angleToCenter > aimConeAngle)
        {
            float side = Mathf.Sign(Vector3.Cross(coneCenter, targetAimDirection).y);
            Quaternion clampRotation = Quaternion.AngleAxis(aimConeAngle * side, Vector3.up);
            targetAimDirection = clampRotation * coneCenter;
        }

        Quaternion currentRot = Quaternion.LookRotation(throwDirection);
        Quaternion targetRot = Quaternion.LookRotation(targetAimDirection);
        throwDirection = Quaternion.Slerp(currentRot, targetRot, Time.deltaTime * aimSmoothing) * Vector3.forward;

        if (playerModel != null && throwDirection.sqrMagnitude > 0.01f)
        {
            Quaternion playerTargetRot = Quaternion.LookRotation(throwDirection);
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, playerTargetRot, Time.deltaTime * playerAimRotationSpeed);
        }
    }
}