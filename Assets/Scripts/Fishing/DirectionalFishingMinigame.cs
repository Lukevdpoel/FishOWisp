using UnityEngine;
using UnityEngine.UI;

public class DirectionalFishingMinigame : MonoBehaviour
{
    [Header("UI References")]
    public RectTransform fishDirectionIndicator;
    public RectTransform playerInputIndicator;
    public Image playerInputImage;
    [Tooltip("Optional: An icon or text that appears when the fish is resting to prompt clicking.")]
    public GameObject clickPromptIcon;

    [Header("Settings")]
    public float rotationSpeed = 5f;
    public float alignmentThreshold = 30f;
    public float inputSmoothing = 15f;

    [Header("Phase Settings")]
    public float struggleDuration = 5.0f;
    public float restDuration = 3.0f;

    [Header("Scoring")]
    public float clickFillAmount = 5.0f; // How much progress per click during rest
    public float progressLossRate = 10f; // How fast it drains when misaligned (Struggle)

    [Header("Colors")]
    public Color alignedColor = Color.green;
    public Color misalignedColor = Color.red;
    public Color restingColor = Color.blue; // Color of the center piece during rest

    // State
    public bool IsResting { get; private set; }
    public bool IsAligned { get; private set; }

    private float currentFishAngle = 0f;
    private float currentPlayerAngle = 0f;
    private float targetPlayerAngle = 0f;
    private float targetFishAngle = 0f;
    private Vector3 currentFishWorldDirection;

    private float phaseTimer;
    private Vector3 initialScale; // Store the original size

    private Transform trackingTarget;
    private Camera mainCamera;
    private RectTransform myRectTransform;

    private void Awake()
    {
        mainCamera = Camera.main;
        myRectTransform = GetComponent<RectTransform>();

        // Capture the scale you set in the editor so we don't overwrite it
        if (playerInputImage != null)
        {
            initialScale = playerInputImage.transform.localScale;
        }
        else
        {
            initialScale = Vector3.one;
        }
    }

    public void Activate()
    {
        gameObject.SetActive(true);
        currentFishAngle = 0f;
        currentPlayerAngle = 0f;
        targetPlayerAngle = 0f;
        currentFishWorldDirection = Vector3.forward;

        // Start in Resting mode
        IsResting = true;
        phaseTimer = restDuration;

        UpdateUI();

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void Deactivate()
    {
        gameObject.SetActive(false);
    }

    public void SetTrackingTarget(Transform target)
    {
        trackingTarget = target;
    }

    public void SetFishDirectionFromVector(Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude > 0.01f)
        {
            currentFishWorldDirection = worldDirection.normalized;
        }
    }

    public float UpdateMinigame(float currentProgress, float maxProgress)
    {
        if (Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        FollowTarget();
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
            // Switch Phase
            IsResting = !IsResting;
            phaseTimer = IsResting ? restDuration : struggleDuration;

            // Visual Update for phase switch
            UpdateUI();
        }
    }

    private float HandleRestPhase(float currentProgress, float maxProgress)
    {
        UpdateUI();

        if (Input.GetMouseButtonDown(0))
        {
            currentProgress += clickFillAmount;
            // Pulse based on INITIAL scale, not Vector3.one
            if (playerInputImage != null)
                playerInputImage.transform.localScale = initialScale * 1.2f;
        }

        // Lerp back to INITIAL scale
        if (playerInputImage != null)
            playerInputImage.transform.localScale = Vector3.Lerp(playerInputImage.transform.localScale, initialScale, Time.deltaTime * 10f);

        return Mathf.Clamp(currentProgress, 0f, maxProgress);
    }

    private float HandleStrugglePhase(float currentProgress, float maxProgress)
    {
        UpdateFishTargetAngle();
        currentFishAngle = Mathf.MoveTowardsAngle(currentFishAngle, targetFishAngle, rotationSpeed * Time.deltaTime * 50f);
        ProcessPlayerInput();

        UpdateUI();
        // Reset to INITIAL scale
        if (playerInputImage != null)
            playerInputImage.transform.localScale = initialScale;

        if (IsAligned)
        {
            return currentProgress;
        }
        else
        {
            return Mathf.Clamp(currentProgress - (progressLossRate * Time.deltaTime), -1f, maxProgress);
        }
    }

    private void FollowTarget()
    {
        if (trackingTarget != null && mainCamera != null && myRectTransform != null)
        {
            Vector3 screenPos = mainCamera.WorldToScreenPoint(trackingTarget.position);
            if (screenPos.z > 0)
            {
                myRectTransform.position = screenPos;
            }
        }
    }

    private void UpdateFishTargetAngle()
    {
        if (trackingTarget == null || mainCamera == null) return;

        Vector3 worldStart = trackingTarget.position;
        Vector3 worldEnd = worldStart + currentFishWorldDirection;
        Vector2 screenStart = mainCamera.WorldToScreenPoint(worldStart);
        Vector2 screenEnd = mainCamera.WorldToScreenPoint(worldEnd);
        Vector2 screenDir = (screenEnd - screenStart);

        if (screenDir.sqrMagnitude > 0.001f)
        {
            float angle = Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg;
            targetFishAngle = angle - 90f;
        }
    }

    private void ProcessPlayerInput()
    {
        Vector2 mousePos = Input.mousePosition;
        Vector2 centerPos = myRectTransform.position;
        Vector2 direction = mousePos - centerPos;

        if (direction.sqrMagnitude > 100f)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            targetPlayerAngle = angle - 90f;
        }

        currentPlayerAngle = Mathf.LerpAngle(currentPlayerAngle, targetPlayerAngle, Time.deltaTime * inputSmoothing);
        float angleDifference = Mathf.DeltaAngle(currentFishAngle, currentPlayerAngle);
        IsAligned = Mathf.Abs(angleDifference) <= alignmentThreshold;
    }

    private void UpdateUI()
    {
        if (IsResting)
        {
            if (fishDirectionIndicator) fishDirectionIndicator.gameObject.SetActive(false);
            if (playerInputIndicator) playerInputIndicator.gameObject.SetActive(false);
            if (clickPromptIcon) clickPromptIcon.SetActive(true);

            if (playerInputImage) playerInputImage.color = restingColor;
        }
        else
        {
            if (fishDirectionIndicator)
            {
                fishDirectionIndicator.gameObject.SetActive(true);
                fishDirectionIndicator.localRotation = Quaternion.Euler(0, 0, currentFishAngle);
            }
            if (playerInputIndicator)
            {
                playerInputIndicator.gameObject.SetActive(true);
                playerInputIndicator.localRotation = Quaternion.Euler(0, 0, currentPlayerAngle);
            }
            if (clickPromptIcon) clickPromptIcon.SetActive(false);

            if (playerInputImage)
            {
                playerInputImage.color = IsAligned ? alignedColor : misalignedColor;
            }
        }
    }
}