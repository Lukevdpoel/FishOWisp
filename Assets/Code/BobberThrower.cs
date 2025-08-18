// Refined and Optimized Script
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(CharacterController), typeof(PlayerController))]
public class ObjectThrower : MonoBehaviour
{
    // Made enum public for better access from other scripts
    public enum ThrowerState { Ready, Charging, Thrown, Reeling }
    private ThrowerState currentState = ThrowerState.Ready;

    [Header("Gameplay")]
    public GameObject objectToHide;

    [Header("Throwing")]
    public GameObject throwablePrefab;
    public Transform bobberSpawnPoint;
    public float minThrowForce = 5f, maxThrowForce = 20f, chargeRate = 10f;
    public float reelInArcHeight = 2f;
    public float reelInStartDelay = 0.1f;
    public float reelInAnimationTime = 0.3f;

    [Header("Rope")]
    public VerletRope rope;
    public Transform ropeConnectionPoint;
    public float maxRopeDistance = 30f;

    [Header("UI")]
    public Slider chargeSlider;
    public GameObject bobberIndicator;

    [Header("Aim")]
    public LayerMask aimLayerMask;

    [Header("Animation")]
    public Animator animator;
    public string chargingAnimTrigger = "StartCharging";
    public string throwAnimTrigger = "Throw";
    public string reelInAnimTrigger = "ReelIn";

    // --- Public Properties for External Scripts ---
    public ThrowerState CurrentState => currentState;
    // FIX 1: Added a public property to replace the old 'isCharging' variable.
    public bool IsCharging => currentState == ThrowerState.Charging;

    // --- Private Variables ---
    private float currentThrowForce;
    private Vector3 throwDirection;
    private GameObject activeBobber;
    // FIX 2: Re-added the 'bobberInWater' variable needed by an external script.
    private bool bobberInWater = false;

    // --- Cached Components ---
    private CharacterController characterController;
    private Rigidbody rb;
    private PlayerController playerController;
    private Camera mainCamera;

    void Awake()
    {
        characterController = GetComponent<CharacterController>();
        rb = GetComponent<Rigidbody>();
        playerController = GetComponent<PlayerController>();
        mainCamera = Camera.main;
    }

    void Start()
    {
        SetupSlider(minThrowForce, maxThrowForce);
        SetUIActive(chargeSlider?.gameObject, false);
        SetUIActive(bobberIndicator, false);

        if (!animator) Debug.LogWarning("Animator not assigned.");
        if (ropeConnectionPoint == null) ropeConnectionPoint = bobberSpawnPoint;
    }

    void Update()
    {
        switch (currentState)
        {
            case ThrowerState.Ready:
                if (Input.GetKeyDown(KeyCode.Mouse0)) StartCharging();
                break;
            case ThrowerState.Charging:
                HandleCharging();
                break;
            case ThrowerState.Thrown:
                CheckRopeDistance();
                if (Input.GetKeyDown(KeyCode.Mouse0)) StartReelIn();
                break;
            case ThrowerState.Reeling:
                break;
        }
    }

    private void StartCharging()
    {
        currentState = ThrowerState.Charging;
        playerController.NotifyOfAction();
        currentThrowForce = minThrowForce;

        SetUIActive(chargeSlider?.gameObject, true);
        SetUIActive(bobberIndicator, true);
        DisableMovement();
        animator?.SetTrigger(chargingAnimTrigger);
    }

    private void HandleCharging()
    {
        currentThrowForce = Mathf.Min(currentThrowForce + chargeRate * Time.deltaTime, maxThrowForce);
        if (chargeSlider) chargeSlider.value = currentThrowForce;

        UpdateAimAndRotation();
        UpdateBobberIndicator();
        playerController.NotifyOfAction();

        if (Input.GetKeyUp(KeyCode.Mouse0)) ThrowObject();
    }

    private void ThrowObject()
    {
        currentState = ThrowerState.Thrown;

        activeBobber = Instantiate(throwablePrefab, bobberSpawnPoint.position, Quaternion.identity);
        objectToHide?.SetActive(false);
        rope?.SetupRope(ropeConnectionPoint, activeBobber.transform);

        if (activeBobber.TryGetComponent(out Rigidbody thrownRb))
        {
            thrownRb.AddForce(throwDirection * currentThrowForce, ForceMode.VelocityChange);
        }

        ResetChargeUI();
        EnableMovement();
        animator?.SetTrigger(throwAnimTrigger);
    }

    private void StartReelIn()
    {
        if (activeBobber == null)
        {
            ResetState();
            return;
        }

        currentState = ThrowerState.Reeling;
        playerController.NotifyOfAction();
        animator?.SetTrigger(reelInAnimTrigger);
        StartCoroutine(ReelInBobberArc());
    }

    private IEnumerator ReelInBobberArc()
    {
        yield return new WaitForSeconds(reelInStartDelay);

        if (activeBobber == null)
        {
            ResetState();
            yield break;
        }

        if (activeBobber.TryGetComponent<Rigidbody>(out var bobberRb))
        {
            bobberRb.isKinematic = true;
        }

        Vector3 startPoint = activeBobber.transform.position;
        float journeyTime = Mathf.Max(0.01f, reelInAnimationTime - reelInStartDelay);
        float elapsedTime = 0f;

        while (elapsedTime < journeyTime)
        {
            if (activeBobber == null)
            {
                ResetState();
                yield break;
            }

            float t = elapsedTime / journeyTime;
            Vector3 endPoint = ropeConnectionPoint.position;
            Vector3 controlPoint = (startPoint + endPoint) / 2f + Vector3.up * reelInArcHeight;
            Vector3 m1 = Vector3.Lerp(startPoint, controlPoint, t);
            Vector3 m2 = Vector3.Lerp(controlPoint, endPoint, t);
            activeBobber.transform.position = Vector3.Lerp(m1, m2, t);

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        rope?.DeactivateRope();
        Destroy(activeBobber);
        activeBobber = null;

        if (objectToHide != null)
        {
            objectToHide.transform.SetParent(ropeConnectionPoint);
            objectToHide.transform.localPosition = Vector3.zero;
            objectToHide.transform.localRotation = Quaternion.identity;
            objectToHide.SetActive(true);
        }

        ResetState();
    }

    private void CheckRopeDistance()
    {
        if (activeBobber != null && Vector3.Distance(ropeConnectionPoint.position, activeBobber.transform.position) > maxRopeDistance)
        {
            StartReelIn();
        }
    }

    // --- Helper & Public Methods ---

    // FIX 2: Re-added the public method so other scripts can call it.
    public void NotifyBobberInWater()
    {
        bobberInWater = true;
    }

    private void UpdateAimAndRotation()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 point = Physics.Raycast(ray, out var hit, 100f, aimLayerMask) ? hit.point : ray.origin + ray.direction * 50f;

        throwDirection = (point - bobberSpawnPoint.position);
        throwDirection.y = 0;
        throwDirection.Normalize();

        if (throwDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(throwDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
        }
    }

    private void UpdateBobberIndicator()
    {
        if (bobberIndicator)
        {
            Vector3 predictedPoint = bobberSpawnPoint.position + throwDirection * currentThrowForce;
            bobberIndicator.transform.position = predictedPoint + Vector3.up * 0.1f;
        }
    }

    private void ResetChargeUI()
    {
        currentThrowForce = minThrowForce;
        if (chargeSlider) chargeSlider.value = minThrowForce;
        SetUIActive(chargeSlider?.gameObject, false);
        SetUIActive(bobberIndicator, false);
    }

    private void ResetState()
    {
        EnableMovement();
        ResetChargeUI();
        // FIX 2: Added reset for the 'bobberInWater' flag.
        bobberInWater = false;
        currentState = ThrowerState.Ready;
    }

    private void DisableMovement()
    {
        if (characterController) characterController.enabled = false;
        if (rb) rb.isKinematic = true;
    }

    private void EnableMovement()
    {
        if (characterController) characterController.enabled = true;
        if (rb) rb.isKinematic = false;
    }

    private void SetupSlider(float min, float max)
    {
        if (!chargeSlider) return;
        chargeSlider.minValue = min;
        chargeSlider.maxValue = max;
        chargeSlider.value = min;
    }

    private void SetUIActive(GameObject obj, bool state)
    {
        if (obj) obj.SetActive(state);
    }
}