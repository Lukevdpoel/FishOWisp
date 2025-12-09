using UnityEngine;
using TMPro; // Use UnityEngine.UI if using Legacy Text

public class FPSDisplay : MonoBehaviour
{
    [Tooltip("Drag the TextMeshProUGUI component here.")]
    public TextMeshProUGUI fpsText; // Change to Text if using Legacy Text

    [Tooltip("How often (in seconds) the FPS text updates.")]
    public float updateInterval = 0.5f;

    private float accumulatedTime = 0f;
    private int frames = 0;
    private float timeLeft;

    void Start()
    {
        if (fpsText == null)
        {
            fpsText = GetComponent<TextMeshProUGUI>();
        }
        timeLeft = updateInterval;
    }

    void Update()
    {
        timeLeft -= Time.unscaledDeltaTime;
        accumulatedTime += Time.timeScale / Time.unscaledDeltaTime;
        frames++;

        if (timeLeft <= 0f)
        {
            float fps = accumulatedTime / frames;
            // Format to 1 decimal place (e.g., "60.0 FPS")
            string format = System.String.Format("{0:F1} FPS", fps);

            if (fpsText != null)
            {
                fpsText.text = format;

                // Optional: Change color based on performance
                if (fps < 30) fpsText.color = Color.red;
                else if (fps < 60) fpsText.color = Color.yellow;
                else fpsText.color = Color.green;
            }

            timeLeft = updateInterval;
            accumulatedTime = 0f;
            frames = 0;
        }
    }
}