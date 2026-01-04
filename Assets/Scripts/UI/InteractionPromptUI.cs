using UnityEngine;
using UnityEngine.UI;
using System.Collections; // Required for Coroutines

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

    [Header("Animation Settings")]
    [Tooltip("How long (in seconds) the pop-in/pop-out takes.")]
    public float animationDuration = 0.2f;
    [Tooltip("Controls the 'bounciness' of the pop-in.")]
    public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Transform currentTarget;
    private Coroutine currentAnimationRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;

        if (promptContainer != null)
        {
            // 1. Force Pivot to Bottom-Center (0.5, 0) so it grows upwards
            RectTransform rt = promptContainer.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.pivot = new Vector2(0.5f, 0f);
            }

            // 2. Initialize state: Hidden and Scaled Down
            promptContainer.transform.localScale = Vector3.zero;
            promptContainer.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        if (promptContainer.activeSelf && currentTarget != null && Camera.main != null)
        {
            // Calculate Position
            Vector3 targetPosition = currentTarget.position + worldOffset;
            Vector3 screenPos = Camera.main.WorldToScreenPoint(targetPosition);

            // Handle objects behind the camera
            if (screenPos.z < 0)
            {
                promptContainer.transform.position = new Vector3(-1000, -1000, 0);
            }
            else
            {
                promptContainer.transform.position = screenPos;
            }
        }
    }

    public void Show(Transform trackTarget, Sprite icon = null)
    {
        currentTarget = trackTarget;

        if (icon != null && promptImage != null)
        {
            promptImage.sprite = icon;
        }

        // Stop any currently running hide/show animation to avoid conflicts
        if (currentAnimationRoutine != null) StopCoroutine(currentAnimationRoutine);

        // Start animating UP
        currentAnimationRoutine = StartCoroutine(AnimateScale(true));
    }

    public void Hide()
    {
        // Stop any currently running hide/show animation
        if (currentAnimationRoutine != null) StopCoroutine(currentAnimationRoutine);

        // Start animating DOWN
        currentAnimationRoutine = StartCoroutine(AnimateScale(false));
    }

    private IEnumerator AnimateScale(bool isShowing)
    {
        float timer = 0f;
        Vector3 initialScale = promptContainer.transform.localScale;
        Vector3 targetScale = isShowing ? Vector3.one : Vector3.zero;

        // If we are showing, enable the object immediately so we can see it grow
        if (isShowing && promptContainer != null)
            promptContainer.SetActive(true);

        while (timer < animationDuration)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / animationDuration);

            // Evaluate the animation curve
            // Note: If scaleCurve is default, it works like a normal Lerp. 
            // If you edit the curve in Inspector, you can get a bounce effect.
            float curveValue = scaleCurve != null ? scaleCurve.Evaluate(progress) : progress;

            if (promptContainer != null)
            {
                promptContainer.transform.localScale = Vector3.Lerp(initialScale, targetScale, curveValue);
            }

            yield return null;
        }

        // Ensure we end at exact target scale
        if (promptContainer != null)
            promptContainer.transform.localScale = targetScale;

        // If we were hiding, disable the object AFTER it has shrunk
        if (!isShowing)
        {
            if (promptContainer != null) promptContainer.SetActive(false);
            currentTarget = null;
        }

        currentAnimationRoutine = null;
    }
}