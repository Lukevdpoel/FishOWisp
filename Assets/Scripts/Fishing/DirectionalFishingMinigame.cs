using UnityEngine;

public class DirectionalFishingMinigame : MonoBehaviour
{
    [Header("Rod Control")]
    [Tooltip("How sensitive the rod is to mouse movement.")]
    public float rodSensitivity = 0.15f;
    [Tooltip("How quickly the rod drifts back to center when the player isn't moving the mouse.")]
    public float rodReturnSpeed = 1.5f;
    [Tooltip("Dead zone - rod direction magnitude must exceed this to count as pointing a direction.")]
    public float rodDeadZone = 0.15f;

    [Header("Phase Settings")]
    public float struggleDuration = 5.0f;
    public float restDuration = 3.0f;

    [Header("Scoring")]
    [Tooltip("How fast the hidden reel meter fills per second while the player holds reel during the rest phase.")]
    public float holdFillRate = 20f;
    public float progressLossRate = 10f;
    public float progressLossFloor = -1f;

    // State
    public bool IsResting { get; private set; }
    public bool IsAligned { get; private set; }
    /// <summary>
    /// True while the player is actively holding the reel button during a rest phase.
    /// FishingRodController reads this to physically pull the bobber toward the player.
    /// </summary>
    public bool IsReeling { get; private set; }

    /// <summary>
    /// Current rod direction from -1 (left) to 1 (right). Driven by mouse X delta.
    /// Use this to drive rod animations or visual feedback.
    /// </summary>
    public float RodDirection { get; private set; }

    private float fishScreenDirection; // -1 left, 1 right
    private float phaseTimer;
    private Transform trackingTarget;
    private Camera mainCamera;

    public void Activate()
    {
        enabled = true;
        RodDirection = 0f;
        fishScreenDirection = 0f;
        IsReeling = false;

        IsResting = true;
        phaseTimer = restDuration;

        mainCamera = Camera.main;
    }

    public void Deactivate()
    {
        enabled = false;
    }

    public void SetTrackingTarget(Transform target)
    {
        trackingTarget = target;
    }

    public void SetFishDirectionFromVector(Vector3 worldDirection)
    {
        if (mainCamera == null || trackingTarget == null) return;
        if (worldDirection.sqrMagnitude < 0.001f) return;

        Vector3 worldStart = trackingTarget.position;
        Vector3 worldEnd = worldStart + worldDirection;
        Vector2 screenStart = mainCamera.WorldToScreenPoint(worldStart);
        Vector2 screenEnd = mainCamera.WorldToScreenPoint(worldEnd);
        float screenDeltaX = screenEnd.x - screenStart.x;

        if (Mathf.Abs(screenDeltaX) > 0.5f)
        {
            fishScreenDirection = Mathf.Sign(screenDeltaX);
        }
    }

    public float UpdateMinigame(float currentProgress, float maxProgress)
    {
        HandlePhases();

        if (IsResting)
        {
            return HandleRestPhase(currentProgress, maxProgress);
        }
        else
        {
            return HandleStrugglePhase(currentProgress, maxProgress);
        }
    }

    private void HandlePhases()
    {
        phaseTimer -= Time.deltaTime;
        if (phaseTimer <= 0f)
        {
            IsResting = !IsResting;
            phaseTimer = IsResting ? restDuration : struggleDuration;

        }
    }

    private float HandleRestPhase(float currentProgress, float maxProgress)
    {
        ProcessPlayerInput();

        FishingEvents.OnRodDirectionUpdate?.Invoke(RodDirection);

        IsReeling = Input.GetMouseButton(0);
        if (IsReeling)
        {
            currentProgress += holdFillRate * Time.deltaTime;
        }

        return Mathf.Clamp(currentProgress, 0f, maxProgress);
    }

    private float HandleStrugglePhase(float currentProgress, float maxProgress)
    {
        ProcessPlayerInput();

        FishingEvents.OnRodDirectionUpdate?.Invoke(RodDirection);

        // No reeling progress during a struggle — the fish is fighting back.
        IsReeling = false;

        if (IsAligned)
        {
            return currentProgress;
        }
        else
        {
            return Mathf.Clamp(currentProgress - (progressLossRate * Time.deltaTime), progressLossFloor, maxProgress);
        }
    }

    private void ProcessPlayerInput()
    {
        float mouseX = Input.GetAxis("Mouse X");

        // Accumulate mouse movement into rod direction
        RodDirection += mouseX * rodSensitivity;

        // Drift back to center when mouse is idle
        if (Mathf.Abs(mouseX) < 0.01f)
        {
            RodDirection = Mathf.MoveTowards(RodDirection, 0f, rodReturnSpeed * Time.deltaTime);
        }

        RodDirection = Mathf.Clamp(RodDirection, -1f, 1f);

        // Aligned when rod points same direction as fish and exceeds dead zone
        if (Mathf.Abs(fishScreenDirection) > 0.1f)
        {
            IsAligned = Mathf.Sign(RodDirection) == Mathf.Sign(fishScreenDirection)
                        && Mathf.Abs(RodDirection) > rodDeadZone;
        }
        else
        {
            // Fish not moving strongly - consider aligned
            IsAligned = true;
        }
    }
}
