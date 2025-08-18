using UnityEngine;

public class FishingRodController : MonoBehaviour
{
    public enum FishingState { Idle, Charging, WaitingForBite, FishOnLine, Reeling }

    [Header("State")]
    [SerializeField] private FishingState currentState = FishingState.Idle;

    [Header("Object References")]
    public GameObject danglingBobber;

    private void OnEnable()
    {
        FishingEvents.OnThrowBobber += HandleThrow;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnStartReeling += HandleStartReeling;
        FishingEvents.OnCancelFishing += ResetFishingState;
        FishingEvents.OnReelingCompleted += ResetFishingState;
    }

    private void OnDisable()
    {
        FishingEvents.OnThrowBobber -= HandleThrow;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnStartReeling -= HandleStartReeling;
        FishingEvents.OnCancelFishing -= ResetFishingState;
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
                case FishingState.FishOnLine:
                    FishingEvents.OnStartReeling?.Invoke();
                    break;
            }
        }

        if (Input.GetKeyUp(KeyCode.Mouse0))
        {
            if (currentState == FishingState.Charging)
            {
                FishingEvents.OnCancelCharging?.Invoke();
            }
        }

        /* This block was removed to disable canceling with the Escape key.
        if (Input.GetKeyDown(KeyCode.Escape) && currentState != FishingState.Idle)
        {
            FishingEvents.OnCancelFishing?.Invoke();
        }
        */
    }

    private void StartCharging()
    {
        currentState = FishingState.Charging;
        FishingEvents.OnStartCharging?.Invoke();
        Debug.Log("State: Charging");
    }

    private void HandleThrow(Vector3 direction, float force)
    {
        danglingBobber?.SetActive(false);
        currentState = FishingState.WaitingForBite;
        Debug.Log("State: Waiting For Bite");
    }

    private void HandleFishBite(BobberController bobber)
    {
        if (currentState == FishingState.WaitingForBite)
        {
            currentState = FishingState.FishOnLine;
            Debug.Log("State: Fish On Line!");
        }
    }

    private void HandleStartReeling()
    {
        if (currentState == FishingState.WaitingForBite || currentState == FishingState.FishOnLine)
        {
            currentState = FishingState.Reeling;
            Debug.Log("State: Reeling");
        }
    }

    private void ResetFishingState()
    {
        if (currentState == FishingState.Idle) return;

        StopAllCoroutines();
        currentState = FishingState.Idle;
        danglingBobber?.SetActive(true);
        Debug.Log("State: Idle (Reset)");
    }
}