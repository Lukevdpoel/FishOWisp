using UnityEngine;

public class CameraTrigger : MonoBehaviour
{
    [Header("Camera To Activate")]
    public Camera zoneCamera;
    public Camera defaultCamera;

    // The interact key lives on the InputBindings asset (keyboardMouse.interact) — read via KeyInput.

    [Header("UI Settings")]
    public Sprite promptIcon;

    private bool playerInZone = false;
    private bool isZoneCameraActive = false;
    private PlayerController currentPlayer;

    private void Start()
    {
        if (zoneCamera != null)
        {
            // Initial setup: Ensure the zone camera and its listener are off at the start
            var audioListener = zoneCamera.GetComponent<AudioListener>();
            if (audioListener != null) audioListener.enabled = false;

            zoneCamera.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (playerInZone)
        {
            // Toggling waits until any charge jump has fully landed (hint hidden too).
            if (currentPlayer != null && currentPlayer.IsJumping) return;

            // Tell the HUD prompt bar an interact is available (toggles the zone camera both ways).
            InteractHint.Ping();

            if (KeyInput.InteractPressed || GamepadInput.InteractPressed)
            {
                ToggleCamera();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = true;
            currentPlayer = other.GetComponent<PlayerController>();

            if (InteractionPromptUI.Instance != null)
            {
                InteractionPromptUI.Instance.Show(other.transform, promptIcon);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = false;

            if (InteractionPromptUI.Instance != null)
            {
                InteractionPromptUI.Instance.Hide();
            }

            if (isZoneCameraActive)
            {
                ReturnToDefaultCamera();
            }

            currentPlayer = null;
        }
    }

    private void ToggleCamera()
    {
        if (isZoneCameraActive)
            ReturnToDefaultCamera();
        else
            SwitchToZoneCamera();
    }

    private void SwitchToZoneCamera()
    {
        if (currentPlayer != null) currentPlayer.areControlsLocked = true;

        // 1. Disable the current main camera (and its listener)
        if (Camera.main != null && Camera.main != zoneCamera)
        {
            // Optional: You might want to disable the default listener explicitly if you don't disable the whole object
            // var mainListener = Camera.main.GetComponent<AudioListener>();
            // if (mainListener != null) mainListener.enabled = false;

            Camera.main.gameObject.SetActive(false);
        }

        // 2. Enable the Zone Camera
        if (zoneCamera != null)
        {
            zoneCamera.gameObject.SetActive(true);
            zoneCamera.tag = "MainCamera";
            isZoneCameraActive = true;

            // --- FIX: Explicitly Re-enable the AudioListener ---
            // Even if the GameObject is active, the component might still be disabled from Start()
            var audioListener = zoneCamera.GetComponent<AudioListener>();
            if (audioListener != null) audioListener.enabled = true;
        }

        // Hide prompt while in camera mode
        if (InteractionPromptUI.Instance != null)
            InteractionPromptUI.Instance.Hide();
    }

    private void ReturnToDefaultCamera()
    {
        if (currentPlayer != null) currentPlayer.areControlsLocked = false;

        // 1. Disable the Zone Camera
        if (zoneCamera != null)
        {
            // --- FIX: Disable listener before deactivating object ---
            var audioListener = zoneCamera.GetComponent<AudioListener>();
            if (audioListener != null) audioListener.enabled = false;

            zoneCamera.gameObject.SetActive(false);
        }

        // 2. Reactivate Default Camera
        if (defaultCamera != null)
        {
            defaultCamera.gameObject.SetActive(true);
            defaultCamera.tag = "MainCamera";

            // --- FIX: Ensure Default Listener is active ---
            var defaultListener = defaultCamera.GetComponent<AudioListener>();
            if (defaultListener != null) defaultListener.enabled = true;
        }

        isZoneCameraActive = false;

        // Show prompt again above player
        if (playerInZone && InteractionPromptUI.Instance != null && currentPlayer != null)
        {
            InteractionPromptUI.Instance.Show(currentPlayer.transform, promptIcon);
        }
    }
}