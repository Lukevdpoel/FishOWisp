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

    private FishPreset caughtFishPreset = null;

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

        caughtFishPreset = activeBobber.HookedFish.preset;

        if (wasReelingLastFrame)
        {
            FishingEvents.OnStopReelingDuringFight?.Invoke();
            wasReelingLastFrame = false;
        }

        // activeBobber.SetStruggleActive(false); // <-- REMOVED (from your original script)

        FishingEvents.OnFishFightEnd?.Invoke(true);

        if (activeBobber != null)
        {
            activeBobber.SwapBobberForFishModel();
        }

        StartCoroutine(ReelInBobberRoutine(caughtFishPreset));
    }

    private IEnumerator ReelInBobberRoutine(FishPreset fishToInventory)
    {
        FishingEvents.OnStartReeling?.Invoke();

        // 1. Find the correct bobber to reel in
        BobberController bobberToReelController = null;
        if (activeBobber != null)
        {
            bobberToReelController = activeBobber;
        }
        else if (bobberInWater != null)
        {
            bobberToReelController = bobberInWater;
        }

        // 2. Check if we found a bobber
        if (bobberToReelController != null)
        {
            // 3. Disable the bobber's script and physics
            bobberToReelController.enabled = false;
            Rigidbody bobberRb = bobberToReelController.GetComponent<Rigidbody>();
            if (bobberRb != null)
            {
                bobberRb.isKinematic = true;
            }

            Transform target;
            if (playerModel != null)
            {
                target = playerModel; // Use player model if assigned
            }
            else
            {
                Debug.LogWarning("Player Model not assigned in FishingRodController! Reeling in to danglingBobber.");
                target = danglingBobber.transform; // Default fallback
            }

            // 4. Set up points for the Bezier curve
            Transform bobberToReelTransform = bobberToReelController.transform;
            Vector3 startPos = bobberToReelTransform.position;   // Start point (P0)

            // Calculate the midpoint in X and Z only
            Vector3 controlPoint = (startPos + target.position) * 0.5f;

            // Set the height relative to the *highest* of the two points
            float highestY = Mathf.Max(startPos.y, target.position.y);
            controlPoint.y = highestY + reelInArcHeight;

            // 5. Move along the curve over time
            float elapsed = 0f;
            while (elapsed < reelInDuration)
            {
                if (bobberToReelTransform != null)
                {
                    // Calculate linear 't' (0 to 1)
                    float t = elapsed / reelInDuration;

                    // Apply the Smoothstep formula to 't' to get an eased value
                    float easedT = t * t * (3f - 2f * t);
                    float oneMinusEasedT = 1f - easedT;

                    // Use 'easedT' in the Bezier formula instead of 't'
                    Vector3 position = (oneMinusEasedT * oneMinusEasedT * startPos) +
                                       (2f * oneMinusEasedT * easedT * controlPoint) +
                                       (easedT * easedT * target.position);

                    bobberToReelTransform.position = position;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            // 6. Clean up the bobber
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
            // --- MODIFICATION: Changed back to a direct void call ---
            ResetFishingState(null);
        }
    }

    private void HandleReelingCompleted(FishPreset fishToInventory)
    {
        // --- MODIFICATION: Changed back to a direct void call ---
        ResetFishingState(fishToInventory);
    }

    // --- MODIFICATION: This is now a 'void' method again, not a coroutine ---
    private void ResetFishingState(FishPreset fishToInventory)
    {
        // Guard clause: If we are already Idle and this is just a cleanup call (no fish), exit.
        if (currentState == FishingState.Idle && fishToInventory == null)
        {
            return;
        }

        // 1. Reset the state and show the bobber.
        currentState = FishingState.Idle;
        danglingBobber?.SetActive(true);
        Debug.Log("State: Idle (Reset)");

        // 2. Clean up all event listeners and state variables.
        if (wasReelingLastFrame)
        {
            FishingEvents.OnStopReelingDuringFight?.Invoke();
            wasReelingLastFrame = false;
        }

        fishFightCoroutine = null;
        fishEscapeCoroutine = null;
        activeBobber = null;
        bobberInWater = null;
        caughtFishPreset = null;

        // 3. Add the fish to the inventory.
        // If this PlayerInventory.Instance.AddFish() call is slow,
        // it *will* freeze the game for a moment.
        if (fishToInventory != null)
        {
            PlayerInventory.Instance.AddFish(fishToInventory);
        }
    }
}