using UnityEngine;
using UnityEngine.UI;

public class DirectionalFishingMinigame : MonoBehaviour
{
    [Header("UI References")]
    public RectTransform fishDirectionIndicator; // The arrow showing where fish is going
    public RectTransform playerInputIndicator;   // The arrow showing player input
    public Image playerInputImage;               // To change color

    [Header("Settings")]
    public float rotationSpeed = 5f;             // How fast the fish changes direction
    public float alignmentThreshold = 30f;       // Degrees of tolerance for "Correct" input
    public Color correctColor = Color.yellow;
    public Color wrongColor = Color.white;
    public float mouseSensitivity = 2f;
    [Tooltip("Smoothing factor for player input rotation. Higher is faster/snappier.")]
    public float inputSmoothing = 15f;

    [Header("Gameplay Balance")]
    public float progressGainRate = 15f;
    public float progressLossRate = 10f;

    private float currentFishAngle = 0f;
    private float currentPlayerAngle = 0f;
    private float targetPlayerAngle = 0f;
    private float targetFishAngle = 0f;

    // Tracking Variables
    private Transform trackingTarget;
    private Camera mainCamera;
    private RectTransform myRectTransform;

    // State
    public bool IsAligned { get; private set; }

    private void Awake()
    {
        mainCamera = Camera.main;
        myRectTransform = GetComponent<RectTransform>();
    }

    public void Activate()
    {
        gameObject.SetActive(true);
        currentFishAngle = 0f;
        currentPlayerAngle = 0f; // Reset player angle on start
        targetPlayerAngle = 0f;
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
            float angle = Mathf.Atan2(worldDirection.z, worldDirection.x) * Mathf.Rad2Deg;
            targetFishAngle = angle - 90f; // Adjust offset to match your sprite's "Up"
        }
    }

    public float UpdateMinigame(float currentProgress, float maxProgress)
    {
        // 0. Update Position Overlay
        FollowTarget();

        // 1. Update Fish Behavior
        currentFishAngle = Mathf.MoveTowardsAngle(currentFishAngle, targetFishAngle, rotationSpeed * Time.deltaTime * 50f);

        // 2. Process Player Input
        ProcessPlayerInput();

        // 3. Update Visuals
        UpdateUI();

        // 4. Calculate Progress
        if (IsAligned && Input.GetMouseButton(0))
        {
            return Mathf.Clamp(currentProgress + (progressGainRate * Time.deltaTime), 0f, maxProgress);
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
            myRectTransform.position = screenPos;
        }
    }

    private void ProcessPlayerInput()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        if (new Vector2(mouseX, mouseY).sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(mouseY, mouseX) * Mathf.Rad2Deg;
            // Align: Atan2(y,x) -> 0 is Right. 90 is Up.
            targetPlayerAngle = angle - 90f;
        }

        // Apply Smoothing
        currentPlayerAngle = Mathf.LerpAngle(currentPlayerAngle, targetPlayerAngle, Time.deltaTime * inputSmoothing);

        // Check Alignment (Compare angles)
        float angleDifference = Mathf.DeltaAngle(currentFishAngle, currentPlayerAngle);
        IsAligned = Mathf.Abs(angleDifference) <= alignmentThreshold;
    }

    private void UpdateUI()
    {
        if (fishDirectionIndicator)
            fishDirectionIndicator.localRotation = Quaternion.Euler(0, 0, currentFishAngle);

        if (playerInputIndicator)
            playerInputIndicator.localRotation = Quaternion.Euler(0, 0, currentPlayerAngle);

        if (playerInputImage)
        {
            playerInputImage.color = IsAligned ? correctColor : wrongColor;
        }
    }
}