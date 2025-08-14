using UnityEngine;
using UnityEngine.UI;

public class ObjectThrower : MonoBehaviour
{
    [Header("Gameplay")]
    public GameObject objectToHide;

    [Header("Throwing")]
    public GameObject throwablePrefab;
    public Transform throwPoint;
    public float minThrowForce = 5f, maxThrowForce = 20f, chargeRate = 10f;

    [Header("Cast Line")]
    public LineToThrownObject castLine;
    public Transform castPoint;

    [Header("UI")]
    public Slider chargeSlider;
    public GameObject bobberIndicator;

    [Header("Aim")]
    public LayerMask aimLayerMask;

    [Header("Animation")]
    public Animator animator;
    public string chargingAnimTrigger = "StartCharging", throwAnimTrigger = "Throw", reelInAnimTrigger = "ReelIn";
    public float reelInAnimationTime = 0.6f;

    private bool readyToThrow = true, bobberInWater = false, isCharging = false, isReelingIn = false;
    private float currentThrowForce;
    private Vector3 throwDirection;
    private GameObject activeBobber;
    private CharacterController characterController;
    private Rigidbody rb;
    private Transform playerTransform;

    void Start()
    {
        playerTransform = transform;
        characterController = GetComponent<CharacterController>();
        rb = GetComponent<Rigidbody>();

        SetupSlider(minThrowForce, maxThrowForce);
        SetUIActive(chargeSlider, false);
        SetUIActive(bobberIndicator, false);

        if (castLine && castPoint) castLine.startPoint = castPoint;
        if (!animator) Debug.LogWarning("Animator not assigned.");
    }

    void Update()
    {
        HandleInput();
    }

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.Mouse0))
        {
            if (isReelingIn)
                return;

            if (activeBobber)
            {
                ReelInAndReset();
                return;
            }

            if (readyToThrow && !activeBobber)
                StartCharging();
        }

        if (Input.GetKey(KeyCode.Mouse0) && isCharging)
        {
            ChargeThrow();
            UpdateAimAndRotation();
            UpdateBobberIndicator();
        }

        if (Input.GetKeyUp(KeyCode.Mouse0) && isCharging)
        {
            if (!activeBobber && !isReelingIn)
                ThrowObject();

            ResetChargeState();
        }
    }

    void StartCharging()
    {
        if (activeBobber || isReelingIn) return;

        isCharging = true;
        currentThrowForce = minThrowForce;

        SetUIActive(chargeSlider, true);
        SetUIActive(bobberIndicator, true);
        DisableMovement();

        animator?.SetTrigger(chargingAnimTrigger);
    }

    void ChargeThrow()
    {
        currentThrowForce = Mathf.Min(currentThrowForce + chargeRate * Time.deltaTime, maxThrowForce);
        if (chargeSlider) chargeSlider.value = currentThrowForce;
    }

    void ThrowObject()
    {
        if (activeBobber || isReelingIn) return;

        GameObject thrownObject = Instantiate(throwablePrefab, throwPoint.position, Quaternion.Euler(180, 0, 0));
        activeBobber = thrownObject;

        if (thrownObject.TryGetComponent(out Bobber bobber))
            bobber.thrower = this;

        if (thrownObject.TryGetComponent(out Rigidbody thrownRb))
            thrownRb.AddForce(throwDirection * currentThrowForce, ForceMode.VelocityChange);

        if (castLine)
        {
            Transform anchor = thrownObject.transform.Find("LineAnchor");
            castLine.AttachTo(anchor ? anchor : thrownObject.transform);
        }

        EnableMovement();
        animator?.SetTrigger(throwAnimTrigger);
    }

    void ReelInAndReset()
    {
        isReelingIn = true;
        readyToThrow = false;

        animator?.SetTrigger(reelInAnimTrigger);
        Invoke(nameof(DestroyActiveBobber), reelInAnimationTime / 2f);
        Invoke(nameof(FinishReelIn), reelInAnimationTime);

        castLine?.Detach();
        SetUIActive(chargeSlider, false);
        SetUIActive(bobberIndicator, false);

        EnableMovement();
        objectToHide?.SetActive(true);

        ResetChargeState();
        bobberInWater = false;
    }

    void UpdateAimAndRotation()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Vector3 point = Physics.Raycast(ray, out var hit, 100f, aimLayerMask) ? hit.point : ray.origin + ray.direction * 50f;

        throwDirection = (point - throwPoint.position).WithY(0).normalized;

        if (throwDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(throwDirection);
            playerTransform.rotation = Quaternion.Lerp(playerTransform.rotation, targetRot, Time.deltaTime * 10f);
        }
    }

    void UpdateBobberIndicator()
    {
        if (bobberIndicator)
        {
            Vector3 predictedPoint = throwPoint.position + throwDirection * currentThrowForce;
            bobberIndicator.transform.position = predictedPoint + Vector3.up * 0.1f;
        }
    }

    void DestroyActiveBobber()
    {
        if (activeBobber) Destroy(activeBobber);
        activeBobber = null;
    }

    void FinishReelIn()
    {
        isReelingIn = false;
        readyToThrow = true;
    }

    void DisableMovement()
    {
        if (characterController) characterController.enabled = false;
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }

    void EnableMovement()
    {
        if (characterController) characterController.enabled = true;
        if (rb) rb.isKinematic = false;
    }

    void SetupSlider(float min, float max)
    {
        if (!chargeSlider) return;
        chargeSlider.minValue = min;
        chargeSlider.maxValue = max;
        chargeSlider.value = min;
    }

    void SetUIActive(Component uiElement, bool state)
    {
        if (uiElement) uiElement.gameObject.SetActive(state);
    }

    void SetUIActive(GameObject obj, bool state)
    {
        if (obj) obj.SetActive(state);
    }

    void ResetChargeState()
    {
        isCharging = false;
        currentThrowForce = 0f;
        if (chargeSlider) chargeSlider.value = minThrowForce;
    }

    public void NotifyBobberInWater()
    {
        bobberInWater = true;
        objectToHide?.SetActive(false);
    }
}

static class VectorExtensions
{
    public static Vector3 WithY(this Vector3 v, float y)
    {
        v.y = y;
        return v;
    }
}
