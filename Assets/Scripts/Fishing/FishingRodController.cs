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

    [Header("Component References")]
    [SerializeField] private CatchInspectionHandler inspectionHandler;

    [Header("Animation Settings")]
    public Animator playerAnimator;
    public string failAnimationTrigger = "FailHook";

    [Header("Cooldowns & Timing")]
    public float failCooldown = 2.0f;
    public float reelInDuration = 1.5f;
    public float reelInArcHeight = 5.0f;
    public float reactionTime = 1.5f;

    [Header("Auto-Reel Settings")]
    [Tooltip("If the player walks further than this from where they threw, the line auto-reels back in.")]
    public float maxDistanceFromCast = 15f;

    [Header("Fish Fight Settings")]
    public float maxFightProgress = 100f;
    public float initialFightProgress = 30f;
    public float fallbackFightProgressRate = 10f;

    private Coroutine fishFightCoroutine;
    private Coroutine fishEscapeCoroutine;
    private BobberController bobberInWater;
    private BobberController activeBobber;

    private float currentFightProgress;
    private CaughtFish caughtFishInstance = null;
    private PlayerController playerController;
    private bool wasReelingLastFrame;
    private bool isAiming;
    private Vector3 castOriginPosition;

    private int hashFail;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        if (playerController == null) playerController = GetComponentInParent<PlayerController>();
        if (playerController == null) Debug.LogError("FishingRodController: PlayerController missing!");

        hashFail = Animator.StringToHash(failAnimationTrigger);
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
        CheckCastDistance();

        if (currentState == FishingState.InspectingCatch && inspectionHandler != null)
        {
            inspectionHandler.UpdateInspection(playerModel);
        }
    }

    private void CheckCastDistance()
    {
        if (currentState != FishingState.WaitingForBite) return;

        float distSqr = (transform.position - castOriginPosition).sqrMagnitude;
        if (distSqr > maxDistanceFromCast * maxDistanceFromCast)
        {
            Debug.Log("Walked too far from cast — auto-reeling.");
            currentState = FishingState.Reeling;
            StartCoroutine(ReelInBobberRoutine(null));
        }
    }

    private void HandleInput()
    {
        if (Time.timeScale == 0f) return;

        if (currentState != FishingState.InspectingCatch)
        {
            if (playerController != null && playerController.areControlsLocked) return;
        }

        // --- Aim Mode (RMB) - works in any non-fight/non-inspection state ---
        if (Input.GetKeyDown(KeyCode.Mouse1) && !isAiming)
        {
            isAiming = true;
            FishingEvents.OnStartAiming?.Invoke();
        }

        if (Input.GetKeyUp(KeyCode.Mouse1) && isAiming)
        {
            StopAiming();
        }

        // --- LMB actions ---
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
                case FishingState.InspectingCatch:
                    TryFinishInspection();
                    break;
            }
        }

        if (Input.GetKeyUp(KeyCode.Mouse0) && currentState == FishingState.Charging)
        {
            FishingEvents.OnCancelCharging?.Invoke();
        }

        // --- Fish Attraction (E key) ---
        if (Input.GetKeyDown(KeyCode.E) && currentState == FishingState.WaitingForBite)
        {
            FishingEvents.OnAttractFish?.Invoke();
        }
    }

    private void StopAiming()
    {
        if (!isAiming) return;
        isAiming = false;
        FishingEvents.OnStopAiming?.Invoke();
    }

    private void StartCharging()
    {
        if (playerController != null && playerController.areControlsLocked) return;
        if (bobberInWater != null || activeBobber != null) return;
        currentState = FishingState.Charging; FishingEvents.OnStartCharging?.Invoke();
    }
    private void HandleThrow(Vector3 direction, float force)
    {
        danglingBobber?.SetActive(false);
        currentState = FishingState.WaitingForBite;
        castOriginPosition = transform.position;
    }
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
        FishingEvents.OnStopReelingDuringFight?.Invoke();
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
        FishingEvents.OnHookFishSuccess?.Invoke(); currentState = FishingState.FightingFish; currentFightProgress = initialFightProgress;
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
        wasReelingLastFrame = false;
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
            else { currentFightProgress += Time.deltaTime * fallbackFightProgressRate; }

            bool isGainingProgress = currentFightProgress > lastProgress;
            if (isGainingProgress != wasReelingLastFrame)
            {
                if (isGainingProgress) FishingEvents.OnStartReelingDuringFight?.Invoke();
                else FishingEvents.OnStopReelingDuringFight?.Invoke();
                wasReelingLastFrame = isGainingProgress;
            }

            lastProgress = currentFightProgress;
            FishingEvents.OnFishFightProgressUpdate?.Invoke(currentFightProgress, maxFightProgress);
            yield return null;
        }
        FishingEvents.OnStopReelingDuringFight?.Invoke();
        if (currentFightProgress >= maxFightProgress) WinFishFight();
        else { if (minigameUI != null) minigameUI.Deactivate(); StartCoroutine(FailRoutine()); }
    }
    private void CancelFishingAction()
    {
        if (currentState != FishingState.Cooldown)
        {
            StopAiming();
            if (bobberInWater != null) Destroy(bobberInWater.gameObject);
            ResetFishingState();
        }
    }

    private IEnumerator ReelInBobberRoutine(CaughtFish fishToInventory)
    {
        FishingEvents.OnStartReeling?.Invoke();

        BobberController bobberToReelController = null;
        if (activeBobber != null) bobberToReelController = activeBobber;
        else if (bobberInWater != null) bobberToReelController = bobberInWater;

        Transform reelTarget = playerModel;
        if (fishToInventory != null && inspectionHandler != null)
        {
            Transform holdPoint = inspectionHandler.GetFishHoldPoint();
            if (holdPoint != null) reelTarget = holdPoint;
        }
        if (reelTarget == null) reelTarget = transform;

        if (bobberToReelController != null)
        {
            bobberToReelController.enabled = false;
            Rigidbody bobberRb = bobberToReelController.GetComponent<Rigidbody>();
            if (bobberRb != null) bobberRb.isKinematic = true;

            Transform bobberToReelTransform = bobberToReelController.transform;
            Vector3 startPos = bobberToReelTransform.position;
            Vector3 controlPoint = (startPos + reelTarget.position) * 0.5f;
            float highestY = Mathf.Max(startPos.y, reelTarget.position.y);
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
                                       (easedT * easedT * reelTarget.position);
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
        if (fishToInventory != null && inspectionHandler != null)
        {
            currentState = FishingState.InspectingCatch;
            danglingBobber?.SetActive(true);
            caughtFishInstance = fishToInventory;
            inspectionHandler.BeginInspection(fishToInventory, playerModel);
        }
        else
        {
            ResetFishingState();
        }
    }

    private void TryFinishInspection()
    {
        if (inspectionHandler == null) return;

        CaughtFish fish;
        if (inspectionHandler.TryFinishInspection(out fish))
        {
            caughtFishInstance = null;
            ResetFishingState();
        }
    }

    private void ResetFishingState()
    {
        if (currentState == FishingState.Idle) return;

        if (playerController != null)
        {
            playerController.SetCatchCamera(false);
            playerController.areControlsLocked = false;
        }

        if (inspectionHandler != null) inspectionHandler.ForceCleanup();

        StopAiming();
        currentState = FishingState.Idle;
        danglingBobber?.SetActive(true);
        fishFightCoroutine = null;
        fishEscapeCoroutine = null;
        activeBobber = null;
        bobberInWater = null;
        caughtFishInstance = null;
        if (minigameUI != null) minigameUI.Deactivate();
    }
}
