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
    [Tooltip("The transform of the player's body. The fish will be reeled to this point.")]
    public Transform playerModel;

    [Header("Animation & Cooldowns")]
    public Animator playerAnimator;
    public string failAnimationTrigger = "FailHook";
    public float failCooldown = 2.0f;
    [Tooltip("How many seconds the reel-in animation takes.")]
    public float reelInDuration = 1.5f;

    [Tooltip("How high (in world units) the arc of the fish reel-in should be.")]
    public float reelInArcHeight = 5.0f;

    [Header("Timing")]
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

    private BobberController bobberInWater;
    private BobberController activeBobber;

    private float currentFightProgress;
    private bool isFishInStrugglePhase;
    private bool wasReelingLastFrame = false;

    // --- MODIFIED: Store the actual fish object, not just the preset ---
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

        if (currentState == FishingState.FightingFish)
        {
            bool isPressingReel = Input.GetKey(KeyCode.Mouse0);
            bool canReel = !isFishInStrugglePhase;
            bool isReelingThisFrame = isPressingReel && canReel;

            if (isReelingThisFrame && !wasReelingLastFrame)
                FishingEvents.OnStartReelingDuringFight?.Invoke();
            else if (!isReelingThisFrame && wasReelingLastFrame)
                FishingEvents.OnStopReelingDuringFight?.Invoke();

            wasReelingLastFrame = isReelingThisFrame;
        }
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

        if (currentState == FishingState.FightingFish && Input.GetKey(KeyCode.Mouse0))
        {
            if (isFishInStrugglePhase)
                currentFightProgress -= strugglePenaltyRate * Time.deltaTime;
            else
                currentFightProgress += reelInRate * Time.deltaTime;

            currentFightProgress = Mathf.Clamp(currentFightProgress, 0, maxFightProgress);
            FishingEvents.OnFishFightProgressUpdate?.Invoke(currentFightProgress, maxFightProgress);

            if (currentFightProgress >= maxFightProgress)
                WinFishFight();
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
        if (bobberInWater != null)
        {
            Destroy(bobberInWater.gameObject);
        }

        FishingEvents.OnCancelFishing?.Invoke();
        currentState = FishingState.Cooldown;
        Debug.Log("State: Cooldown");

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
        currentFightProgress = 0f;
        wasReelingLastFrame = false;
        Debug.Log("Hooked! State: Fighting Fish!");
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

        // --- MODIFIED: Capture the specific CaughtFish instance ---
        caughtFishInstance = activeBobber.HookedFish;

        if (wasReelingLastFrame)
        {
            FishingEvents.OnStopReelingDuringFight?.Invoke();
            wasReelingLastFrame = false;
        }

        FishingEvents.OnFishFightEnd?.Invoke(true);

        if (activeBobber != null)
        {
            activeBobber.SwapBobberForFishModel();
        }

        StartCoroutine(ReelInBobberRoutine(caughtFishInstance));
    }

    // --- MODIFIED: Accepts CaughtFish instead of FishPreset ---
    private IEnumerator ReelInBobberRoutine(CaughtFish fishToInventory)
    {
        FishingEvents.OnStartReeling?.Invoke();

        BobberController bobberToReelController = null;
        if (activeBobber != null)
        {
            bobberToReelController = activeBobber;
        }
        else if (bobberInWater != null)
        {
            bobberToReelController = bobberInWater;
        }

        if (bobberToReelController != null)
        {
            bobberToReelController.enabled = false;
            Rigidbody bobberRb = bobberToReelController.GetComponent<Rigidbody>();
            if (bobberRb != null)
            {
                bobberRb.isKinematic = true;
            }

            Transform target;
            if (playerModel != null)
            {
                target = playerModel;
            }
            else
            {
                Debug.LogWarning("Player Model not assigned in FishingRodController! Reeling in to danglingBobber.");
                target = danglingBobber.transform;
            }

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

            if (bobberToReelTransform != null)
            {
                Destroy(bobberToReelTransform.gameObject);
            }
        }

        HandleReelingCompleted(fishToInventory);
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
            if (bobberInWater != null)
            {
                Destroy(bobberInWater.gameObject);
            }
            ResetFishingState(null);
        }
    }

    // --- MODIFIED: Accepts CaughtFish ---
    private void HandleReelingCompleted(CaughtFish fishToInventory)
    {
        ResetFishingState(fishToInventory);
    }

    // --- MODIFIED: Accepts CaughtFish ---
    private void ResetFishingState(CaughtFish fishToInventory)
    {
        if (currentState == FishingState.Idle && fishToInventory == null)
        {
            return;
        }

        currentState = FishingState.Idle;
        danglingBobber?.SetActive(true);
        Debug.Log("State: Idle (Reset)");

        if (wasReelingLastFrame)
        {
            FishingEvents.OnStopReelingDuringFight?.Invoke();
            wasReelingLastFrame = false;
        }

        fishFightCoroutine = null;
        fishEscapeCoroutine = null;
        activeBobber = null;
        bobberInWater = null;
        caughtFishInstance = null; // Clear local reference

        // 3. Add the fish to the inventory.
        // --- MODIFIED: Calls the overload that takes the existing CaughtFish instance ---
        if (fishToInventory != null)
        {
            PlayerInventory.Instance.AddFish(fishToInventory);
        }
    }
}