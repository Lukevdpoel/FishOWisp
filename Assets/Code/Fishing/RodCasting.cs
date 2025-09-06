using UnityEngine;

public class RodCasting : MonoBehaviour
{
    [Header("References")]
    public Transform throwOrigin;
    [Tooltip("The visual model of the player to rotate.")]
    public Transform playerModel;

    [Header("Aiming Target")]
    [Tooltip("Prefab for the trajectory arc and landing sprite. Must have a CastingTargetController component.")]
    public GameObject castingTargetPrefab;

    [Header("Casting Power")]
    public float minThrowForce = 5f;
    public float maxThrowForce = 25f;
    public float chargeRate = 10f;

    [Header("Aiming")]
    [Tooltip("The angle (in degrees) from the center to the edge of the allowed aiming cone.")]
    public float aimConeAngle = 45f;
    [Tooltip("How sensitive the aiming is to horizontal mouse movement.")]
    public float aimSensitivity = 2f;
    [Tooltip("How smoothly the aim follows the mouse. Higher values are faster and less smooth.")]
    public float aimSmoothing = 10f;

    private float currentThrowForce;
    private Vector3 throwDirection;
    private Vector3 targetAimDirection; // The direction the mouse is aiming for
    private bool isCharging = false;
    private Camera mainCamera;

    private CastingTargetController activeCastingTarget;
    private int chargeDirection = 1; // 1 for filling, -1 for emptying

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
                // The trajectory line now uses the smoothed throwDirection
                activeCastingTarget.UpdateTrajectory(throwOrigin.position, throwDirection, currentThrowForce);
            }
        }
    }

    private void BeginCharging()
    {
        isCharging = true;
        currentThrowForce = minThrowForce;
        chargeDirection = 1;

        // Initialize both aim directions to be straight ahead from the camera
        targetAimDirection = mainCamera.transform.forward;
        targetAimDirection.y = 0;
        targetAimDirection.Normalize();
        throwDirection = targetAimDirection; // Start the smoothed direction at the target

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
        // 1. Get mouse movement and apply it to the "target" direction
        float mouseX = Input.GetAxis("Mouse X") * aimSensitivity;
        Quaternion rotation = Quaternion.AngleAxis(mouseX, Vector3.up);
        targetAimDirection = rotation * targetAimDirection;

        // 2. Define the center of the aiming cone
        Vector3 coneCenter = mainCamera.transform.forward;
        coneCenter.y = 0;
        coneCenter.Normalize();

        // 3. Clamp the "target" direction to the cone's limits
        float angleToCenter = Vector3.Angle(coneCenter, targetAimDirection);
        if (angleToCenter > aimConeAngle)
        {
            float side = Mathf.Sign(Vector3.Cross(coneCenter, targetAimDirection).y);
            Quaternion clampRotation = Quaternion.AngleAxis(aimConeAngle * side, Vector3.up);
            targetAimDirection = clampRotation * coneCenter;
        }

        // 4. Smoothly interpolate the actual throwDirection towards the targetAimDirection
        Quaternion currentRot = Quaternion.LookRotation(throwDirection);
        Quaternion targetRot = Quaternion.LookRotation(targetAimDirection);
        throwDirection = Quaternion.Slerp(currentRot, targetRot, Time.deltaTime * aimSmoothing) * Vector3.forward;

        // 5. Rotate the player model to face the smoothed aim direction
        if (playerModel != null && throwDirection.sqrMagnitude > 0.01f)
        {
            Quaternion playerTargetRot = Quaternion.LookRotation(throwDirection);
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, playerTargetRot, Time.deltaTime * 10f);
        }
    }
}