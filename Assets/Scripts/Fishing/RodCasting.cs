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

    [Header("Physics Prediction")]
    [Tooltip("MUST MATCH the 'Extra Gravity' value in your BobberController script.")]
    public float bobberExtraGravity = 30f;

    [Header("Aiming")]
    [Tooltip("The angle (in degrees) from the center to the edge of the allowed aiming cone.")]
    public float aimConeAngle = 45f;
    [Tooltip("How sensitive the aiming is to horizontal mouse movement.")]
    public float aimSensitivity = 2f;
    [Tooltip("How smoothly the aim follows the mouse.")]
    public float aimSmoothing = 10f;
    [Tooltip("How fast the player model rotates to face the aim direction.")]
    public float playerAimRotationSpeed = 10f;

    private float currentThrowForce;
    private Vector3 throwDirection;
    private Vector3 targetAimDirection;
    private bool isCharging = false;
    private Camera mainCamera;

    private CastingTargetController activeCastingTarget;
    private int chargeDirection = 1;

    void Awake()
    {
        mainCamera = Camera.main;
    }

    private void OnEnable()
    {
        FishingEvents.OnStartCharging += BeginCharging;
        FishingEvents.OnCancelCharging += ReleaseCharge;
        FishingEvents.OnCancelFishing += Cancel;
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
        currentThrowForce = minThrowForce;
        chargeDirection = 1;

        targetAimDirection = mainCamera.transform.forward;
        targetAimDirection.y = 0;
        targetAimDirection.Normalize();
        throwDirection = targetAimDirection;

        FishingEvents.OnToggleChargeUI?.Invoke(true);

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
        currentThrowForce += chargeRate * chargeDirection * Time.deltaTime;

        if (currentThrowForce >= maxThrowForce)
        {
            currentThrowForce = maxThrowForce;
            chargeDirection = -1;
        }
        else if (currentThrowForce <= minThrowForce)
        {
            currentThrowForce = minThrowForce;
            chargeDirection = 1;
        }

        FishingEvents.OnUpdateChargeUI?.Invoke(currentThrowForce, maxThrowForce);
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
        float mouseX = Input.GetAxis("Mouse X") * aimSensitivity;
        Quaternion rotation = Quaternion.AngleAxis(mouseX, Vector3.up);
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