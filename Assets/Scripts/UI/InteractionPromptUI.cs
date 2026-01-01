using UnityEngine;
using UnityEngine.UI;

public class InteractionPromptUI : MonoBehaviour
{
    public static InteractionPromptUI Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("The GameObject containing the image.")]
    public GameObject promptContainer;
    public Image promptImage;

    [Header("Positioning")]
    [Tooltip("How high above the player should the icon float?")]
    public Vector3 worldOffset = new Vector3(0, 2.2f, 0);

    [Tooltip("Check this if your Canvas is set to 'Screen Space - Overlay'. Uncheck for 'Screen Space - Camera'.")]
    public bool isOverlayCanvas = true;

    private Transform currentTarget;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;

        Hide();
    }

    private void LateUpdate()
    {
        if (promptContainer.activeSelf && currentTarget != null && Camera.main != null)
        {
            // 1. Calculate where the "Head" is in world space
            Vector3 targetPosition = currentTarget.position + worldOffset;

            // 2. Convert to Screen Space
            Vector3 screenPos = Camera.main.WorldToScreenPoint(targetPosition);

            // 3. Check if target is behind the camera (standard issue with WorldToScreenPoint)
            if (screenPos.z < 0)
            {
                // Hide if behind camera
                promptContainer.transform.position = new Vector3(-1000, -1000, 0);
            }
            else
            {
                // 4. Update UI position
                promptContainer.transform.position = screenPos;
            }
        }
    }

    /// <summary>
    /// Shows the prompt tracking a specific target (usually the player).
    /// </summary>
    public void Show(Transform trackTarget, Sprite icon = null)
    {
        currentTarget = trackTarget;

        if (promptContainer != null)
            promptContainer.SetActive(true);

        if (icon != null && promptImage != null)
        {
            promptImage.sprite = icon;
        }
    }

    public void Hide()
    {
        if (promptContainer != null)
            promptContainer.SetActive(false);

        currentTarget = null;
    }
}