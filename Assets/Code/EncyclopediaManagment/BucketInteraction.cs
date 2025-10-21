using UnityEngine;

public class BucketInteraction : MonoBehaviour
{
    public GameObject vendorUIPanel;
    public Camera bucketCamera; // Optional: a special camera
    public Camera mainCamera;

    private bool isBucketOpen = false;

    void Start()
    {
        // Start with everything closed
        vendorUIPanel.SetActive(false);
        if (bucketCamera != null) bucketCamera.gameObject.SetActive(false);
    }

    // Call this from your player's interaction script (e.g., OnMouseDown, OnTriggerEnter)
    public void ToggleBucketView()
    {
        isBucketOpen = !isBucketOpen;

        if (isBucketOpen)
        {
            OpenBucket();
        }
        else
        {
            CloseBucket();
        }
    }

    void OpenBucket()
    {
        vendorUIPanel.SetActive(true);
        Time.timeScale = 0f; // Pause the game
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (bucketCamera != null)
        {
            mainCamera.gameObject.SetActive(false);
            bucketCamera.gameObject.SetActive(true);
        }
    }

    void CloseBucket()
    {
        vendorUIPanel.SetActive(false);
        Time.timeScale = 1f; // Resume the game
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (bucketCamera != null)
        {
            mainCamera.gameObject.SetActive(true);
            bucketCamera.gameObject.SetActive(false);
        }
    }
}