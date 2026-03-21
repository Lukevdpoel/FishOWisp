using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

public class FishingRodController : MonoBehaviour
{
    public enum FishingState { Idle, Charging, WaitingForBite, FishOnTheLine, FightingFish, Reeling, InspectingCatch, Cooldown }

    [Header("State")]
    [SerializeField] private FishingState currentState = FishingState.Idle;

    [Header("Object References")]
    public GameObject danglingBobber;
    public Transform playerModel;
    public DirectionalFishingMinigame minigameUI;

    [Header("Catch Presentation")]
    public Transform fishHoldPoint;
    public float turnToCameraSpeed = 10f;
    [Tooltip("How fast the visual fish model spins while inspecting it (degrees per second).")]
    public float fishRotationSpeed = 45f; // <--- NEW SETTING

    [Header("Animation Settings")]
    [Tooltip("The BOOLEAN parameter in your Animator. True = Hold Fish, False = Put Away.")]
    public string showCatchAnimBool = "ShowCatch";

    public Animator playerAnimator;
    public string failAnimationTrigger = "FailHook";
    public string reelingDuringFightBool = "isreelingduringfight";

    [Header("Cooldowns & Timing")]
    public float failCooldown = 2.0f;
    public float reelInDuration = 1.5f;
    public float reelInArcHeight = 5.0f;
    public float reactionTime = 1.5f;
    [Tooltip("Input buffer: You must wait this many seconds after catching before you can click to dismiss.")]
    public float minInspectionTime = 0.5f;

    [Header("Fish Fight Settings")]
    public float maxFightProgress = 100f;

    private Coroutine fishFightCoroutine;
    private Coroutine fishEscapeCoroutine;
    private BobberController bobberInWater;
    private BobberController activeBobber;

    private float currentFightProgress;
    private CaughtFish caughtFishInstance = null;
    private GameObject heldFishVisual;
    private PlayerController playerController;
    private float inspectionStartTime;

    private int hashShowCatch;
    private int hashFail;
    private int hashReelingDuringFight;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        if (playerController == null) playerController = GetComponentInParent<PlayerController>();
        if (playerController == null) Debug.LogError("FishingRodController: PlayerController missing!");

        hashShowCatch = Animator.StringToHash(showCatchAnimBool);
        hashFail = Animator.StringToHash(failAnimationTrigger);
        hashReelingDuringFight = Animator.StringToHash(reelingDuringFightBool);
    }

    private void OnEnable()
    {
        FishingEvents.OnThrowBobber += HandleThrow;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnCancelFishing += CancelFishingAction;
        FishingEvents.OnBobberLandedInWater += HandleBobberLanded;
    }

    private void OnDisable()
    {
        FishingEvents.OnThrowBobber -= HandleThrow;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnCancelFishing -= CancelFishingAction;
        FishingEvents.OnBobberLandedInWater -= HandleBobberLanded;
    }

    private void HandleBobberLanded(BobberController bobber)
    {
        if (bobberInWater != null && bobberInWater != bobber) Destroy(bobberInWater.gameObject);
        bobberInWater = bobber;
    }

    void Update()
    {
        HandleInput();

        // ---------------------------------------------------------
        // INSPECTION PHASE LOGIC
        // ---------------------------------------------------------
        if (currentState == FishingState.InspectingCatch)
        {
            // 1. ROTATION LOGIC: Player turns to face camera
            if (playerModel != null)
            {
                Vector3 camForward = Camera.main.transform.forward;
                camForward.y = 0;
                if (camForward != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(-camForward);
                    playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetRotation, Time.deltaTime * turnToCameraSpeed);
                }
            }

            // 2. FISH SPIN LOGIC: The actual fish visual spins slowly (NEW)
            if (heldFishVisual != null)
            {
                // Rotate around World Y axis so it looks good even if player is turning
                heldFishVisual.transform.Rotate(Vector3.up, fishRotationSpeed * Time.deltaTime, Space.World);
            }
        }
    }

    private void HandleInput()
    {
        if (Time.timeScale == 0f) return;

        // Respect locks unless we are in the catch phase
        if (currentState != FishingState.InspectingCatch)
        {
            if (playerController != null && playerController.areControlsLocked) return;
        }

        if (Input.GetKeyDown(KeyCode.Mouse0))
        {
            switch (currentState)
            {
                case FishingState.Idle:
                    StartCharging();
                    break;
                case FishingState.WaitingForBite:
                    Debug.Log("Reeling back the line.");
                    currentState = FishingState.Reeling;
                    StartCoroutine(ReelInBobberRoutine(null));
                    break;
                case FishingState.FishOnTheLine:
                    HookFishAndStartFight();
                    break;

                // ---------------------------------------------------------
                // CLICK TO FINISH
                // ---------------------------------------------------------
                case FishingState.InspectingCatch:
                    if (Time.time > inspectionStartTime + minInspectionTime)
                    {
                        FinishInspection();
                    }
                    break;
            }
        }

        if (Input.GetKeyUp(KeyCode.Mouse0) && currentState == FishingState.Charging)
        {
            FishingEvents.OnCancelCharging?.Invoke();
        }
    }

    private void StartCharging()
    {
        if (playerController != null && playerController.areControlsLocked) return;
        if (bobberInWater != null || activeBobber != null) return;
        currentState = FishingState.Charging; FishingEvents.OnStartCharging?.Invoke();
    }
    private void HandleThrow(Vector3 direction, float force) { danglingBobber?.SetActive(false); currentState = FishingState.WaitingForBite; }
    private void HandleFishBite(BobberController bobber)
    {
        if (currentState != FishingState.WaitingForBite || bobber != bobberInWater) return;
        currentState = FishingState.FishOnTheLine; activeBobber = bobber;
        fishEscapeCoroutine = StartCoroutine(FishEscapeTimer());
    }
    private IEnumerator FishEscapeTimer() { yield return new WaitForSeconds(reactionTime); FailToHookFish(); }
    private void FailToHookFish() { if (fishEscapeCoroutine != null) StopCoroutine(fishEscapeCoroutine); StartCoroutine(FailRoutine()); }

    private IEnumerator FailRoutine()
    {
        if (minigameUI != null) minigameUI.Deactivate();
        if (playerAnimator != null) playerAnimator.SetBool(hashReelingDuringFight, false);
        if (bobberInWater != null) { Destroy(bobberInWater.gameObject); bobberInWater = null; }
        activeBobber = null;
        FishingEvents.OnCancelFishing?.Invoke();
        currentState = FishingState.Cooldown;
        if (playerAnimator != null) playerAnimator.SetTrigger(hashFail);
        yield return new WaitForSeconds(failCooldown);
        currentState = FishingState.Idle; danglingBobber?.SetActive(true);
    }
    private void HookFishAndStartFight()
    {
        if (fishEscapeCoroutine != null) StopCoroutine(fishEscapeCoroutine);
        FishingEvents.OnHookFishSuccess?.Invoke(); currentState = FishingState.FightingFish; currentFightProgress = 30f;
        if (activeBobber != null) { Rigidbody rb = activeBobber.GetComponent<Rigidbody>(); if (rb != null) { rb.isKinematic = false; activeBobber.SetStruggleActive(true); } }
        if (minigameUI != null) { minigameUI.Activate(); if (activeBobber != null) minigameUI.SetTrackingTarget(activeBobber.transform); }
        FishingEvents.OnFishFightBegin?.Invoke(activeBobber.HookedFish.preset); fishFightCoroutine = StartCoroutine(FishFightRoutine());
    }
    private void WinFishFight()
    {
        currentState = FishingState.Reeling;
        if (fishFightCoroutine != null) StopCoroutine(fishFightCoroutine);
        if (activeBobber != null) activeBobber.SetStruggleActive(false);
        if (minigameUI != null) minigameUI.Deactivate();
        caughtFishInstance = activeBobber.HookedFish;
        FishingEvents.OnFishFightEnd?.Invoke(true);
        if (activeBobber != null) activeBobber.SwapBobberForFishModel();

        StartCoroutine(ReelInBobberRoutine(caughtFishInstance));
    }
    private IEnumerator FishFightRoutine()
    {
        float lastProgress = currentFightProgress;
        while (currentFightProgress < maxFightProgress && currentFightProgress > 0)
        {
            if (minigameUI != null)
            {
                if (activeBobber != null)
                {
                    minigameUI.SetFishDirectionFromVector(activeBobber.StruggleDirection);
                    activeBobber.SetStruggleActive(!minigameUI.IsResting);
                }
                currentFightProgress = minigameUI.UpdateMinigame(currentFightProgress, maxFightProgress);
            }
            else { currentFightProgress += Time.deltaTime * 10f; }

            if (playerAnimator != null)
            {
                bool isGainingProgress = currentFightProgress > lastProgress;
                playerAnimator.SetBool(hashReelingDuringFight, isGainingProgress);
            }
            lastProgress = currentFightProgress;
            FishingEvents.OnFishFightProgressUpdate?.Invoke(currentFightProgress, maxFightProgress);
            yield return null;
        }
        if (playerAnimator != null) playerAnimator.SetBool(hashReelingDuringFight, false);
        if (currentFightProgress >= maxFightProgress) WinFishFight();
        else { if (minigameUI != null) minigameUI.Deactivate(); StartCoroutine(FailRoutine()); }
    }
    private void CancelFishingAction()
    {
        if (currentState != FishingState.Cooldown)
        {
            if (bobberInWater != null) Destroy(bobberInWater.gameObject);
            ResetFishingState(null);
        }
    }

    private IEnumerator ReelInBobberRoutine(CaughtFish fishToInventory)
    {
        FishingEvents.OnStartReeling?.Invoke();

        BobberController bobberToReelController = null;
        if (activeBobber != null) bobberToReelController = activeBobber;
        else if (bobberInWater != null) bobberToReelController = bobberInWater;

        Transform target = (fishToInventory != null && fishHoldPoint != null) ? fishHoldPoint : playerModel;
        if (target == null) target = transform;

        if (bobberToReelController != null)
        {
            bobberToReelController.enabled = false;
            Rigidbody bobberRb = bobberToReelController.GetComponent<Rigidbody>();
            if (bobberRb != null) bobberRb.isKinematic = true;

            Transform bobberToReelTransform = bobberToReelController.transform;
            Vector3 startPos = bobberToReelTransform.position;
            Vector3 controlPoint = (startPos + target.position) * 0.5f;
            float highestY = Mathf.Max(startPos.y, target.position.y);
            controlPoint.y = highestY + reelInArcHeight;

            float elapsed = 0f;
            while (elapsed < reelInDuration)
            {
                if (bobberToReelTransform != null)
                {
                    float t = elapsed / reelInDuration;
                    float easedT = t * t * (3f - 2f * t);
                    float oneMinusEasedT = 1f - easedT;
                    Vector3 position = (oneMinusEasedT * oneMinusEasedT * startPos) +
                                       (2f * oneMinusEasedT * easedT * controlPoint) +
                                       (easedT * easedT * target.position);
                    bobberToReelTransform.position = position;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (bobberToReelTransform != null) Destroy(bobberToReelTransform.gameObject);
        }

        HandleReelingCompleted(fishToInventory);
    }

    private void HandleReelingCompleted(CaughtFish fishToInventory)
    {
        if (fishToInventory != null)
        {
            currentState = FishingState.InspectingCatch;
            danglingBobber?.SetActive(true);

            inspectionStartTime = Time.time;

            if (playerController != null)
            {
                playerController.areControlsLocked = true;
                playerController.SetCatchCamera(true);
            }

            if (playerAnimator != null)
            {
                playerAnimator.SetBool(hashShowCatch, true);
            }

            if (fishHoldPoint != null && fishToInventory.preset.fishPrefab != null)
            {
                heldFishVisual = Instantiate(fishToInventory.preset.fishPrefab, fishHoldPoint);
                heldFishVisual.transform.localPosition = Vector3.zero;
                heldFishVisual.transform.localRotation = Quaternion.identity;
                SetLayerRecursively(heldFishVisual, playerModel.gameObject.layer);
            }
        }
        else
        {
            ResetFishingState(null);
        }
    }

    private void FinishInspection()
    {
        if (caughtFishInstance != null) PlayerInventory.Instance.AddFish(caughtFishInstance);
        if (heldFishVisual != null) Destroy(heldFishVisual);

        if (playerAnimator != null)
        {
            playerAnimator.SetBool(hashShowCatch, false);
        }

        ResetFishingState(null);
    }

    private void ResetFishingState(CaughtFish fishToInventory)
    {
        if (currentState == FishingState.Idle && fishToInventory == null) return;

        // --- FIXED: FORCE CAMERA RESET ---
        if (playerController != null)
        {
            playerController.SetCatchCamera(false);
            playerController.areControlsLocked = false;
        }
        // ---------------------------------

        currentState = FishingState.Idle;
        danglingBobber?.SetActive(true);
        fishFightCoroutine = null;
        fishEscapeCoroutine = null;
        activeBobber = null;
        bobberInWater = null;
        caughtFishInstance = null;
        if (minigameUI != null) minigameUI.Deactivate();
    }

    void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            if (child == null) continue;
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }
}