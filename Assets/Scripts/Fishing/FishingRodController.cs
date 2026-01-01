using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

public class FishingRodController : MonoBehaviour
{
    public enum FishingState { Idle, Charging, WaitingForBite, FishOnTheLine, FightingFish, Reeling, Cooldown }

    [Header("State")]
    [SerializeField] private FishingState currentState = FishingState.Idle;

    [Header("Object References")]
    public GameObject danglingBobber;
    public Transform playerModel;
    public DirectionalFishingMinigame minigameUI;

    [Header("Animation & Cooldowns")]
    public Animator playerAnimator;
    public string failAnimationTrigger = "FailHook";
    public float failCooldown = 2.0f;
    public float reelInDuration = 1.5f;
    public float reelInArcHeight = 5.0f;

    [Header("Timing")]
    public float reactionTime = 1.5f;

    [Header("Fish Fight Settings")]
    public float maxFightProgress = 100f;

    private Coroutine fishFightCoroutine;
    private Coroutine fishEscapeCoroutine;

    private BobberController bobberInWater;
    private BobberController activeBobber;

    private float currentFightProgress;
    private CaughtFish caughtFishInstance = null;

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
        if (bobberInWater != null && bobberInWater != bobber)
        {
            Destroy(bobberInWater.gameObject);
        }
        bobberInWater = bobber;
    }

    void Update()
    {
        HandleInput();
    }

    private void HandleInput()
    {
        if (Time.timeScale == 0f) return;

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
            }
        }

        if (Input.GetKeyUp(KeyCode.Mouse0) && currentState == FishingState.Charging)
        {
            FishingEvents.OnCancelCharging?.Invoke();
        }
    }

    private void StartCharging()
    {
        currentState = FishingState.Charging;
        FishingEvents.OnStartCharging?.Invoke();
    }

    private void HandleThrow(Vector3 direction, float force)
    {
        danglingBobber?.SetActive(false);
        currentState = FishingState.WaitingForBite;
    }

    private void HandleFishBite(BobberController bobber)
    {
        if (currentState != FishingState.WaitingForBite || bobber != bobberInWater) return;

        currentState = FishingState.FishOnTheLine;
        activeBobber = bobber;
        Debug.Log("A fish is biting! Hook it now!");
        fishEscapeCoroutine = StartCoroutine(FishEscapeTimer());
    }

    private IEnumerator FishEscapeTimer()
    {
        yield return new WaitForSeconds(reactionTime);
        FailToHookFish();
    }

    private void FailToHookFish()
    {
        Debug.Log("Too slow! The fish got away!");
        if (fishEscapeCoroutine != null)
        {
            StopCoroutine(fishEscapeCoroutine);
            fishEscapeCoroutine = null;
        }
        StartCoroutine(FailRoutine());
    }

    private IEnumerator FailRoutine()
    {
        if (minigameUI != null) minigameUI.Deactivate();

        if (bobberInWater != null)
        {
            Destroy(bobberInWater.gameObject);
            bobberInWater = null;
        }
        activeBobber = null;

        FishingEvents.OnCancelFishing?.Invoke();
        currentState = FishingState.Cooldown;
        Debug.Log("State: Cooldown (Failed)");

        if (playerAnimator != null && !string.IsNullOrEmpty(failAnimationTrigger))
            playerAnimator.SetTrigger(failAnimationTrigger);

        yield return new WaitForSeconds(failCooldown);

        currentState = FishingState.Idle;
        danglingBobber?.SetActive(true);
        Debug.Log("State: Idle (Reset from Cooldown)");
    }

    private void HookFishAndStartFight()
    {
        if (fishEscapeCoroutine != null)
        {
            StopCoroutine(fishEscapeCoroutine);
            fishEscapeCoroutine = null;
        }

        FishingEvents.OnHookFishSuccess?.Invoke();
        currentState = FishingState.FightingFish;
        currentFightProgress = 30f; // Start with some progress buffer

        Debug.Log("Hooked! State: Fighting Fish (Directional Mode)!");

        if (activeBobber != null)
        {
            Rigidbody rb = activeBobber.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                activeBobber.SetStruggleActive(true);
            }
        }

        if (minigameUI != null)
        {
            minigameUI.Activate();
            if (activeBobber != null)
            {
                minigameUI.SetTrackingTarget(activeBobber.transform);
            }
        }

        FishingEvents.OnFishFightBegin?.Invoke(activeBobber.HookedFish.preset);
        fishFightCoroutine = StartCoroutine(FishFightRoutine());
    }

    private void WinFishFight()
    {
        currentState = FishingState.Reeling;

        if (fishFightCoroutine != null)
        {
            StopCoroutine(fishFightCoroutine);
            fishFightCoroutine = null;
        }

        if (activeBobber != null)
        {
            activeBobber.SetStruggleActive(false);
        }

        if (minigameUI != null) minigameUI.Deactivate();

        caughtFishInstance = activeBobber.HookedFish;

        FishingEvents.OnFishFightEnd?.Invoke(true);

        if (activeBobber != null)
        {
            activeBobber.SwapBobberForFishModel();
        }

        StartCoroutine(ReelInBobberRoutine(caughtFishInstance));
    }

    private IEnumerator ReelInBobberRoutine(CaughtFish fishToInventory)
    {
        FishingEvents.OnStartReeling?.Invoke();

        BobberController bobberToReelController = null;
        if (activeBobber != null) bobberToReelController = activeBobber;
        else if (bobberInWater != null) bobberToReelController = bobberInWater;

        if (bobberToReelController != null)
        {
            bobberToReelController.enabled = false;
            Rigidbody bobberRb = bobberToReelController.GetComponent<Rigidbody>();
            if (bobberRb != null) bobberRb.isKinematic = true;

            Transform target;
            if (playerModel != null) target = playerModel;
            else target = danglingBobber.transform;

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

    private IEnumerator FishFightRoutine()
    {
        while (currentFightProgress < maxFightProgress && currentFightProgress > 0)
        {
            if (minigameUI != null)
            {
                if (activeBobber != null)
                {
                    // Update direction
                    minigameUI.SetFishDirectionFromVector(activeBobber.StruggleDirection);

                    // --- NEW: Toggle bobber struggling based on rest state ---
                    activeBobber.SetStruggleActive(!minigameUI.IsResting);
                }

                currentFightProgress = minigameUI.UpdateMinigame(currentFightProgress, maxFightProgress);
            }
            else
            {
                // Fallback auto-win if no UI
                currentFightProgress += Time.deltaTime * 10f;
            }

            FishingEvents.OnFishFightProgressUpdate?.Invoke(currentFightProgress, maxFightProgress);
            yield return null;
        }

        if (currentFightProgress >= maxFightProgress)
        {
            WinFishFight();
        }
        else
        {
            Debug.Log("Fish escaped during fight (Progress reached 0)!");
            if (minigameUI != null) minigameUI.Deactivate();
            StartCoroutine(FailRoutine());
        }
    }

    private void CancelFishingAction()
    {
        if (currentState != FishingState.Cooldown)
        {
            if (bobberInWater != null) Destroy(bobberInWater.gameObject);
            ResetFishingState(null);
        }
    }

    private void HandleReelingCompleted(CaughtFish fishToInventory)
    {
        ResetFishingState(fishToInventory);
    }

    private void ResetFishingState(CaughtFish fishToInventory)
    {
        if (currentState == FishingState.Idle && fishToInventory == null) return;

        currentState = FishingState.Idle;
        danglingBobber?.SetActive(true);
        Debug.Log("State: Idle (Reset)");

        fishFightCoroutine = null;
        fishEscapeCoroutine = null;
        activeBobber = null;
        bobberInWater = null;
        caughtFishInstance = null;

        if (minigameUI != null) minigameUI.Deactivate();

        if (fishToInventory != null)
        {
            PlayerInventory.Instance.AddFish(fishToInventory);
        }
    }
}