using UnityEngine;
using System.Collections;

public class FishingRodController : MonoBehaviour
{
    public enum FishingState { Idle, Charging, WaitingForBite, FishOnTheLine, FightingFish, Reeling }

    [Header("State")]
    [SerializeField] private FishingState currentState = FishingState.Idle;

    [Header("Object References")]
    public GameObject danglingBobber;

    [Header("Timing")]
    [Tooltip("How many seconds the player has to react to a bite.")]
    public float reactionTime = 1.5f;

    [Header("Fish Fight Mini-Game")]
    [Tooltip("How much progress is needed to catch the fish. Can be modified by fish rarity later.")]
    public float maxFightProgress = 100f;
    [Tooltip("How fast the progress bar fills when reeling correctly.")]
    public float reelInRate = 25f;
    [Tooltip("How fast the progress bar drains if you reel while the fish is struggling.")]
    public float strugglePenaltyRate = 35f;
    [Tooltip("Minimum time the fish will struggle for.")]
    public float minStruggleTime = 1.5f;
    [Tooltip("Maximum time the fish will struggle for.")]
    public float maxStruggleTime = 3.0f;
    [Tooltip("Minimum time the player has to reel in.")]
    public float minReelWindow = 1.0f;
    [Tooltip("Maximum time the player has to reel in.")]
    public float maxReelWindow = 2.5f;

    private Coroutine fishFightCoroutine;
    private Coroutine fishEscapeCoroutine;
    private BobberController activeBobber;
    private float currentFightProgress;
    private bool isFishInStrugglePhase;

    // MODIFIED: Re-added the listener for OnCancelFishing for our fail state.
    private void OnEnable()
    {
        FishingEvents.OnThrowBobber += HandleThrow;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnCancelFishing += CancelFishingAction;
        FishingEvents.OnReelingCompleted += ResetFishingState;
    }

    // MODIFIED: Re-added the listener for OnCancelFishing.
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

    // MODIFIED: This now correctly makes the fish get away.
    private IEnumerator FishEscapeTimer()
    {
        yield return new WaitForSeconds(reactionTime);

        // This code runs ONLY if the player failed to click in time.
        FailToHookFish();
    }

    // NEW: A dedicated method for when the player fails the reaction check.
    private void FailToHookFish()
    {
        Debug.Log("Too slow! The fish got away!");

        // This event will tell other scripts (like FishingLine) to destroy the bobber instantly.
        FishingEvents.OnCancelFishing?.Invoke();
    }

    private void HookFishAndStartFight()
    {
        if (fishEscapeCoroutine != null)
        {
            StopCoroutine(fishEscapeCoroutine);
            fishEscapeCoroutine = null;
        }

        currentState = FishingState.FightingFish;
        currentFightProgress = 0f;
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

    // NEW: This handler for the OnCancelFishing event is needed again.
    private void CancelFishingAction()
    {
        ResetFishingState();
    }

    private void ResetFishingState()
    {
        if (currentState == FishingState.Idle) return;

        StopAllCoroutines();
        fishFightCoroutine = null;
        fishEscapeCoroutine = null;

        activeBobber = null;

        currentState = FishingState.Idle;
        danglingBobber?.SetActive(true);
        Debug.Log("State: Idle (Reset)");
    }
}