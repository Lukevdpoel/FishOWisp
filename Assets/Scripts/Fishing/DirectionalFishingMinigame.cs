using UnityEngine;
using UnityEngine.UI;

public class DirectionalFishingMinigame : MonoBehaviour
{
    [Header("UI References")]
    public RectTransform fishDirectionIndicator;
    public RectTransform playerInputIndicator;
    public Image playerInputImage;

    [Header("Settings")]
    public float rotationSpeed = 5f;
    public float alignmentThreshold = 30f;
    public Color correctColor = Color.yellow;
    public Color wrongColor = Color.white;
    public float inputSmoothing = 15f;

    [Header("Gameplay Balance")]
    public float progressGainRate = 15f;
    public float progressLossRate = 10f;

    private float currentFishAngle = 0f;
    private float currentPlayerAngle = 0f;
    private float targetPlayerAngle = 0f;
    private float targetFishAngle = 0f;

    private Transform trackingTarget;
    private Camera mainCamera;
    private RectTransform myRectTransform;

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
        currentPlayerAngle = 0f;
        targetPlayerAngle = 0f;

        // FIX: Ensure cursor is visible so player can see their position relative to the bobber
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void Deactivate()
    {
        gameObject.SetActive(false);

        // Reset cursor state
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
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
            targetFishAngle = angle - 90f;
        }
    }

    // NEW: Enforce cursor state every frame to prevent flickering if other scripts fight it
    private void LateUpdate()
    {
        if (gameObject.activeInHierarchy)
        {
            if (Cursor.visible == false || Cursor.lockState != CursorLockMode.None)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }
    }

    public float UpdateMinigame(float currentProgress, float maxProgress)
    {
        // Re-enforce cursor visibility in update loop in case PlayerController fights it
        // Note: Added LateUpdate above for extra stability, but keeping this doesn't hurt.
        if (Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        FollowTarget();

        currentFishAngle = Mathf.MoveTowardsAngle(currentFishAngle, targetFishAngle, rotationSpeed * Time.deltaTime * 50f);

        ProcessPlayerInput();

        UpdateUI();

        if (IsAligned)
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