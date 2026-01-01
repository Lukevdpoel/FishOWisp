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
    // MODIFIED: Changed reference to the new Osu minigame
    public OsuFishingMinigame minigameUI;

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
        if (bobberInWater != null && bobberInWater != bobber) Destroy(bobberInWater.gameObject);
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
                case FishingState.Idle: StartCharging(); break;
                case FishingState.WaitingForBite:
                    Debug.Log("Reeling back the line.");
                    currentState = FishingState.Reeling;
                    StartCoroutine(ReelInBobberRoutine(null));
                    break;
                case FishingState.FishOnTheLine: HookFishAndStartFight(); break;
            }
        }

        if (Input.GetKeyUp(KeyCode.Mouse0) && currentState == FishingState.Charging)
        {
            FishingEvents.OnCancelCharging?.Invoke();
        }
    }

    private void StartCharging() { currentState = FishingState.Charging; FishingEvents.OnStartCharging?.Invoke(); }
    private void HandleThrow(Vector3 direction, float force) { danglingBobber?.SetActive(false); currentState = FishingState.WaitingForBite; }

    private void HandleFishBite(BobberController bobber)
    {
        if (currentState != FishingState.WaitingForBite || bobber != bobberInWater) return;
        currentState = FishingState.FishOnTheLine;
        activeBobber = bobber;
        fishEscapeCoroutine = StartCoroutine(FishEscapeTimer());
    }

    private IEnumerator FishEscapeTimer()
    {
        yield return new WaitForSeconds(reactionTime);
        FailToHookFish();
    }

    private void FailToHookFish()
    {
        if (fishEscapeCoroutine != null) { StopCoroutine(fishEscapeCoroutine); fishEscapeCoroutine = null; }
        StartCoroutine(FailRoutine());
    }

    private IEnumerator FailRoutine()
    {
        if (minigameUI != null) minigameUI.Deactivate();
        if (bobberInWater != null) { Destroy(bobberInWater.gameObject); bobberInWater = null; }
        activeBobber = null;

        FishingEvents.OnCancelFishing?.Invoke();
        currentState = FishingState.Cooldown;
        if (playerAnimator != null && !string.IsNullOrEmpty(failAnimationTrigger)) playerAnimator.SetTrigger(failAnimationTrigger);

        yield return new WaitForSeconds(failCooldown);

        currentState = FishingState.Idle;
        danglingBobber?.SetActive(true);
    }

    private void HookFishAndStartFight()
    {
        if (fishEscapeCoroutine != null) { StopCoroutine(fishEscapeCoroutine); fishEscapeCoroutine = null; }

        FishingEvents.OnHookFishSuccess?.Invoke();
        currentState = FishingState.FightingFish;
        currentFightProgress = 30f; // Initial buffer

        // Ensure bobber physics are active for struggle movement
        if (activeBobber != null)
        {
            Rigidbody rb = activeBobber.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = false; activeBobber.SetStruggleActive(true); }
        }

        if (minigameUI != null)
        {
            minigameUI.Activate();
            if (activeBobber != null) minigameUI.SetTrackingTarget(activeBobber.transform);
        }

        FishingEvents.OnFishFightBegin?.Invoke(activeBobber.HookedFish.preset);
        fishFightCoroutine = StartCoroutine(FishFightRoutine());
    }

    private void WinFishFight()
    {
        currentState = FishingState.Reeling;
        if (fishFightCoroutine != null) { StopCoroutine(fishFightCoroutine); fishFightCoroutine = null; }

        if (activeBobber != null) activeBobber.SetStruggleActive(false);
        if (minigameUI != null) minigameUI.Deactivate();

        caughtFishInstance = activeBobber.HookedFish;
        FishingEvents.OnFishFightEnd?.Invoke(true);
        if (activeBobber != null) activeBobber.SwapBobberForFishModel();

        StartCoroutine(ReelInBobberRoutine(caughtFishInstance));
    }

    private IEnumerator ReelInBobberRoutine(CaughtFish fishToInventory)
    {
        FishingEvents.OnStartReeling?.Invoke();
        BobberController bobberToReelController = activeBobber != null ? activeBobber : bobberInWater;

        if (bobberToReelController != null)
        {
            bobberToReelController.enabled = false;
            Rigidbody bobberRb = bobberToReelController.GetComponent<Rigidbody>();
            if (bobberRb != null) bobberRb.isKinematic = true;

            Transform target = playerModel != null ? playerModel : danglingBobber.transform;
            Transform bobberToReelTransform = bobberToReelController.transform;
            Vector3 startPos = bobberToReelTransform.position;
            Vector3 controlPoint = (startPos + target.position) * 0.5f + Vector3.up * reelInArcHeight;

            float elapsed = 0f;
            while (elapsed < reelInDuration)
            {
                if (bobberToReelTransform != null)
                {
                    float t = elapsed / reelInDuration;
                    // Bezier curve reel in
                    Vector3 m1 = Vector3.Lerp(startPos, controlPoint, t);
                    Vector3 m2 = Vector3.Lerp(controlPoint, target.position, t);
                    bobberToReelTransform.position = Vector3.Lerp(m1, m2, t);
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
                // Update minigame (it handles drain internally)
                currentFightProgress = minigameUI.UpdateMinigame(currentFightProgress, maxFightProgress);
            }
            else
            {
                // Fallback auto-drain for testing
                currentFightProgress -= Time.deltaTime * 5f;
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