// Refined and Optimized Script
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(CharacterController), typeof(PlayerController))]
public class ObjectThrower : MonoBehaviour
{
    // Defines the possible states the fishing mechanic can be in.
    public enum FishingState { Idle, Charging, WaitingForBite, FishOnLine, Reeling }
    private FishingState currentState = FishingState.Idle;

    [Header("Gameplay")]
    public GameObject objectToHide;

    [Header("Throwing")]
    public GameObject throwablePrefab; // This is your Bobber Prefab
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

    // --- Private Variables ---
    private float currentThrowForce;
    private Vector3 throwDirection;
    private Bobber activeBobber; // Now stores the Bobber script directly
    private bool isBobberSettled = false; // FIXED: Flag to prevent instant reeling

    // --- Cached Components ---
    private CharacterController characterController;
    private PlayerController playerController;
    private Camera mainCamera;

    void Awake()
    {
        characterController = GetComponent<CharacterController>();
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
        // Input handling based on the current fishing state
        if (Input.GetKeyDown(KeyCode.Mouse0))
        {
            switch (currentState)
            {
                case FishingState.Idle:
                    StartCharging();
                    break;
                case FishingState.WaitingForBite:
                    StartReeling();
                    break;
                case FishingState.FishOnLine:
                    StartReeling();
                    break;
            }
        }

        if (Input.GetKeyUp(KeyCode.Mouse0))
        {
            if (currentState == FishingState.Charging)
            {
                ThrowObject();
            }
        }

        if (Input.GetKeyDown(KeyCode.Mouse1) && currentState != FishingState.Idle)
        {
            ResetState();
        }

        if (currentState == FishingState.Charging)
        {
            HandleCharging();
        }

        if (currentState == FishingState.WaitingForBite || currentState == FishingState.FishOnLine)
        {
            CheckRopeDistance();
        }
    }

    public void SignalFishBite(Bobber bobber)
    {
        if (bobber == activeBobber && currentState == FishingState.WaitingForBite)
        {
            Debug.Log("<b>Step 1:</b> A fish is on the line! Waiting for player to click.", this.gameObject);
            currentState = FishingState.FishOnLine;
        }
    }

    private void StartCharging()
    {
        currentState = FishingState.Charging;
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
    }

    private void ThrowObject()
    {
        GameObject bobberInstance = Instantiate(throwablePrefab, bobberSpawnPoint.position, Quaternion.identity);
        activeBobber = bobberInstance.GetComponent<Bobber>();

        if (activeBobber == null)
        {
            Debug.LogError("The throwablePrefab is missing the Bobber script!");
            Destroy(bobberInstance);
            ResetState();
            return;
        }

        currentState = FishingState.WaitingForBite;
        objectToHide?.SetActive(false);
        rope?.SetupRope(ropeConnectionPoint, activeBobber.transform);

        if (activeBobber.TryGetComponent(out Rigidbody thrownRb))
        {
            thrownRb.AddForce(throwDirection * currentThrowForce, ForceMode.VelocityChange);
        }

        ResetChargeUI();
        EnableMovement();
        animator?.SetTrigger(throwAnimTrigger);

        // FIXED: Start a coroutine to add a delay before the distance check is active.
        StartCoroutine(SettleBobberDelay());
    }

    // FIXED: New coroutine to prevent the bobber from being reeled in instantly.
    private IEnumerator SettleBobberDelay()
    {
        yield return new WaitForSeconds(0.5f); // Wait half a second
        if (currentState == FishingState.WaitingForBite || currentState == FishingState.FishOnLine)
        {
            isBobberSettled = true;
        }
    }

    private void StartReeling()
    {
        if (activeBobber == null)
        {
            ResetState();
            return;
        }

        if (activeBobber.hookedFish != null)
        {
            Debug.Log("<b>Step 2:</b> Fish is hooked! Telling bobber to swap models.", this.gameObject);
            activeBobber.StartReeling();
        }
        else
        {
            Debug.Log("Reeling in the empty bobber.", this.gameObject);
        }

        currentState = FishingState.Reeling;
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

        // FIXED: Explicitly specify the component type <Rigidbody>
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
        // FIXED: Only check the distance if the bobber has had time to settle.
        if (isBobberSettled && activeBobber != null && Vector3.Distance(ropeConnectionPoint.position, activeBobber.transform.position) > maxRopeDistance)
        {
            StartReeling();
        }
    }

    public void NotifyBobberInWater()
    {
        // This function is called by the Bobber. We can add logic here if needed.
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
        rope?.DeactivateRope();

        if (activeBobber != null)
        {
            Destroy(activeBobber.gameObject);
            activeBobber = null;
        }

        EnableMovement();
        ResetChargeUI();
        currentState = FishingState.Idle;
        isBobberSettled = false; // FIXED: Reset the settled flag
        StopAllCoroutines(); // Stop any running coroutines like the settle delay
    }

    private void DisableMovement()
    {
        if (characterController) characterController.enabled = false;
    }

    private void EnableMovement()
    {
        if (characterController) characterController.enabled = true;
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
