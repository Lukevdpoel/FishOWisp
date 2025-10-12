using UnityEngine;
using System.Collections;

public class FishingRodController : MonoBehaviour
{
    public enum FishingState { Idle, Charging, WaitingForBite, FishOnTheLine, FightingFish, Reeling, Cooldown }

    [Header("State")]
    [SerializeField] private FishingState currentState = FishingState.Idle;

    [Header("Object References")]
    public GameObject danglingBobber;

    [Header("Animation & Cooldowns")]
    [Tooltip("The animator for the player/rod.")]
    public Animator playerAnimator;
    [Tooltip("The name of the trigger parameter in the animator for the fail animation.")]
    public string failAnimationTrigger = "FailHook";
    [Tooltip("How many seconds casting is disabled after failing to hook a fish.")]
    public float failCooldown = 2.0f;

    [Header("Timing")]
    [Tooltip("How many seconds the player has to react to a bite.")]
    public float reactionTime = 1.5f;

    [Header("Fish Fight Mini-Game")]
    public float maxFightProgress = 100f;
    public float reelInRate = 25f;
    public float strugglePenaltyRate = 35f;
    public float minStruggleTime = 1.5f;
    public float maxStruggleTime = 3.0f;
    public float minReelWindow = 1.0f;
    public float maxReelWindow = 2.5f;

    private Coroutine fishFightCoroutine;
    private Coroutine fishEscapeCoroutine;
    private BobberController activeBobber;
    private float currentFightProgress;
    private bool isFishInStrugglePhase;
    private bool wasReelingLastFrame = false;

    private void OnEnable()
    {
        FishingEvents.OnThrowBobber += HandleThrow;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnCancelFishing += CancelFishingAction;
        FishingEvents.OnReelingCompleted += ResetFishingState;
    }

    private void OnDisable()
    {
        FishingEvents.OnThrowBobber -= HandleThrow;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnCancelFishing -= CancelFishingAction;
        FishingEvents.OnReelingCompleted -= ResetFishingState;
    }

    void Update()
    {
        HandleInput();

        if (currentState == FishingState.FightingFish)
        {
            bool isPressingReel = Input.GetKey(KeyCode.Mouse0);
            bool canReel = !isFishInStrugglePhase;
            bool isReelingThisFrame = isPressingReel && canReel;

            if (isReelingThisFrame && !wasReelingLastFrame)
            {
                FishingEvents.OnStartReelingDuringFight?.Invoke();
            }
            else if (!isReelingThisFrame && wasReelingLastFrame)
            {
                FishingEvents.OnStopReelingDuringFight?.Invoke();
            }

            wasReelingLastFrame = isReelingThisFrame;
        }
    }

    private void HandleInput()
    {
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
                    FishingEvents.OnStartReeling?.Invoke();
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

        if (currentState == FishingState.FightingFish && Input.GetKey(KeyCode.Mouse0))
        {
            if (isFishInStrugglePhase)
            {
                currentFightProgress -= strugglePenaltyRate * Time.deltaTime;
            }
            else
            {
                currentFightProgress += reelInRate * Time.deltaTime;
            }

            currentFightProgress = Mathf.Clamp(currentFightProgress, 0, maxFightProgress);
            FishingEvents.OnFishFightProgressUpdate?.Invoke(currentFightProgress, maxFightProgress);

            if (currentFightProgress >= maxFightProgress)
            {
                WinFishFight();
            }
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
        if (currentState != FishingState.WaitingForBite) return;

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
        FishingEvents.OnCancelFishing?.Invoke();
        currentState = FishingState.Cooldown;
        Debug.Log("State: Cooldown");

        if (playerAnimator != null && !string.IsNullOrEmpty(failAnimationTrigger))
        {
            playerAnimator.SetTrigger(failAnimationTrigger);
        }

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
        currentFightProgress = 0f;
        wasReelingLastFrame = false;
        Debug.Log("Hooked! State: Fighting Fish!");
        FishingEvents.OnFishFightBegin?.Invoke(activeBobber.HookedFish.preset);
        fishFightCoroutine = StartCoroutine(FishFightRoutine());
    }

    private void WinFishFight()
    {
        if (fishFightCoroutine != null)
        {
            StopCoroutine(fishFightCoroutine);
            fishFightCoroutine = null;
        }

        // --- THIS IS THE ADDED LINE ---
        PlayerInventory.Instance.AddFish(activeBobber.HookedFish);
        // --- END ADDED LINE ---

        if (wasReelingLastFrame)
        {
            FishingEvents.OnStopReelingDuringFight?.Invoke();
            wasReelingLastFrame = false;
        }

        activeBobber.SetStruggleActive(false);
        FishingEvents.OnFishFightEnd?.Invoke(true);
        currentState = FishingState.Reeling;
        FishingEvents.OnStartReeling?.Invoke();
    }

    private IEnumerator FishFightRoutine()
    {
        while (currentFightProgress < maxFightProgress)
        {
            isFishInStrugglePhase = true;
            activeBobber.SetStruggleActive(true);
            float struggleDuration = Random.Range(minStruggleTime, maxStruggleTime);
            yield return new WaitForSeconds(struggleDuration);

            if (currentState != FishingState.FightingFish) yield break;

            isFishInStrugglePhase = false;
            activeBobber.SetStruggleActive(false);
            float reelWindowDuration = Random.Range(minReelWindow, maxReelWindow);
            yield return new WaitForSeconds(reelWindowDuration);

            if (currentState != FishingState.FightingFish) yield break;
        }
    }

    private void CancelFishingAction()
    {
        if (currentState != FishingState.Cooldown)
        {
            ResetFishingState();
        }
    }

    private void ResetFishingState()
    {
        if (currentState == FishingState.Idle) return;

        if (wasReelingLastFrame)
        {
            FishingEvents.OnStopReelingDuringFight?.Invoke();
            wasReelingLastFrame = false;
        }

        fishFightCoroutine = null;
        fishEscapeCoroutine = null;
        activeBobber = null;
        currentState = FishingState.Idle;
        danglingBobber?.SetActive(true);
        Debug.Log("State: Idle (Reset)");
    }
}