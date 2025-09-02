using UnityEngine;

public class RodCasting : MonoBehaviour
{
    [Header("References")]
    public Transform throwOrigin;
    [Tooltip("The visual model of the player to rotate.")]
    public Transform playerModel;

    [Header("Casting Power")]
    public float minThrowForce = 5f;
    public float maxThrowForce = 25f;
    public float chargeRate = 10f;

    private float currentThrowForce;
    private Vector3 throwDirection;
    private bool isCharging = false;
    private Camera mainCamera;

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
        }
    }

    private void BeginCharging()
    {
        isCharging = true;
        currentThrowForce = minThrowForce;
        FishingEvents.OnToggleChargeUI?.Invoke(true);
    }

    private void ChargeThrow()
    {
        currentThrowForce = Mathf.Min(currentThrowForce + chargeRate * Time.deltaTime, maxThrowForce);
        FishingEvents.OnUpdateChargeUI?.Invoke(currentThrowForce, maxThrowForce);
    }

    private void ReleaseCharge()
    {
        if (!isCharging) return;
        isCharging = false;
        FishingEvents.OnToggleChargeUI?.Invoke(false);
        FishingEvents.OnThrowBobber?.Invoke(throwDirection, currentThrowForce);
    }

    private void Cancel()
    {
        if (!isCharging) return;
        isCharging = false;
        FishingEvents.OnToggleChargeUI?.Invoke(false);
    }

    private void UpdateAimAndRotation()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        Vector3 direction = ray.direction;

        // This line removes the vertical (Y) component of the aim.
        direction.y = 0;

        throwDirection = direction.normalized;

        if (playerModel != null && throwDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(throwDirection);
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetRot, Time.deltaTime * 10f);
        }
    }
}