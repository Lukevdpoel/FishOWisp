using UnityEngine;

public class CameraTrigger : MonoBehaviour
{
    [Header("Camera To Activate")]
    public Camera zoneCamera;
    public Camera defaultCamera;

    [Header("Input Settings")]
    public KeyCode interactKey = KeyCode.E;

    [Header("UI Settings")]
    public Sprite promptIcon;

    private bool playerInZone = false;
    private bool isZoneCameraActive = false;
    private PlayerController currentPlayer;

    private void Start()
    {
        if (zoneCamera != null)
        {
            zoneCamera.gameObject.SetActive(false);
            var audioListener = zoneCamera.GetComponent<AudioListener>();
            if (audioListener != null) audioListener.enabled = false;
        }
    }

    private void Update()
    {
        if (playerInZone)
        {
            if (Input.GetKeyDown(interactKey))
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

            // --- CHANGED: Pass the player's transform so the UI can follow it ---
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

        if (Camera.main != null && Camera.main != zoneCamera)
            Camera.main.gameObject.SetActive(false);

        if (zoneCamera != null)
        {
            zoneCamera.gameObject.SetActive(true);
            zoneCamera.tag = "MainCamera";
            isZoneCameraActive = true;
        }

        // Hide prompt while in camera mode
        if (InteractionPromptUI.Instance != null)
            InteractionPromptUI.Instance.Hide();
    }

    private void ReturnToDefaultCamera()
    {
        if (currentPlayer != null) currentPlayer.areControlsLocked = false;

        if (zoneCamera != null) zoneCamera.gameObject.SetActive(false);

        if (defaultCamera != null)
        {
            defaultCamera.gameObject.SetActive(true);
            defaultCamera.tag = "MainCamera";
        }

        isZoneCameraActive = false;

        // Show prompt again above player
        if (playerInZone && InteractionPromptUI.Instance != null && currentPlayer != null)
        {
            InteractionPromptUI.Instance.Show(currentPlayer.transform, promptIcon);
        }
    }
}