using System.Collections;
using UnityEngine;

/// <summary>
/// Continuously and smoothly animates the scale of this GameObject on the X and Z axes.
/// It picks a new random scale, lerps to it over [scaleDuration] seconds,
/// waits for [pauseDuration] seconds, and then repeats.
/// </summary>
public class SmoothRandomScaler : MonoBehaviour
{
    [Header("X-Axis Scale Limits")]
    [Tooltip("The minimum possible scale value for the X-axis.")]
    public float minScaleX = 0.5f;

    [Tooltip("The maximum possible scale value for the X-axis.")]
    public float maxScaleX = 1.5f;

    [Header("Z-Axis Scale Limits")]
    [Tooltip("The minimum possible scale value for the Z-axis.")]
    public float minScaleZ = 0.5f;

    [Tooltip("The maximum possible scale value for the Z-axis.")]
    public float maxScaleZ = 1.5f;

    [Header("Animation Settings")]
    [Tooltip("How long (in seconds) each smooth scale change should take.")]
    public float scaleDuration = 1.0f;

    [Tooltip("How long (in seconds) to wait at the target scale before changing again.")]
    public float pauseDuration = 0.5f;

    // We store the original Y scale so we don't change it.
    private float originalYScale;

    /// <summary>
    /// This method is called when the script instance is being loaded.
    /// </summary>
    void Start()
    {
        // 1. Store the original Y scale so we never modify it.
        originalYScale = transform.localScale.y;

        // 2. Start the continuous scaling coroutine.
        StartCoroutine(SmoothScaleLoop());
    }

    /// <summary>
    /// A coroutine that loops forever, picking a new scale and animating to it.
    /// </summary>
    private IEnumerator SmoothScaleLoop()
    {
        // This loop will run for the entire lifetime of the object.
        while (true)
        {
            // --- This part is the animation ---

            // 1. Store the scale we are *starting* from.
            Vector3 startScale = transform.localScale;

            // 2. Pick a new *target* scale.
            float randomX = Random.Range(minScaleX, maxScaleX);
            float randomZ = Random.Range(minScaleZ, maxScaleZ);
            Vector3 targetScale = new Vector3(randomX, originalYScale, randomZ);

            // 3. Animate from startScale to targetScale over [scaleDuration] seconds.
            float elapsedTime = 0f;
            while (elapsedTime < scaleDuration)
            {
                // Calculate how far along the animation we are (a value from 0 to 1).
                float t = elapsedTime / scaleDuration;

                // Optional: Add easing for a smoother feel (starts slow, speeds up, ends slow)
                t = t * t * (3f - 2f * t); // This is a "SmoothStep" function

                // Apply the new scale.
                transform.localScale = Vector3.Lerp(startScale, targetScale, t);

                // Wait for the next frame.
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            // 4. Snap to the final scale to ensure it's precise.
            transform.localScale = targetScale;

            // --- This part is the pause ---

            // 5. Wait for [pauseDuration] seconds before looping again.
            yield return new WaitForSeconds(pauseDuration);
        }
    }
}