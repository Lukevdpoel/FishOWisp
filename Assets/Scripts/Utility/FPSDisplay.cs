using UnityEngine;
using TMPro; // Use UnityEngine.UI if using Legacy Text

public class FPSDisplay : MonoBehaviour
{
    [Tooltip("Optional. Drag a TextMeshProUGUI here to drive a UI label. If left empty, the FPS is drawn directly in the screen corner via OnGUI.")]
    public TextMeshProUGUI fpsText; // Change to Text if using Legacy Text

    [Tooltip("How often (in seconds) the FPS readout updates.")]
    public float updateInterval = 0.5f;

    [Header("Corner overlay (used when fpsText is empty)")]
    [Tooltip("Which screen corner the overlay sits in.")]
    public TextAnchor corner = TextAnchor.UpperLeft;

    [Tooltip("Pixel padding from the chosen corner.")]
    public Vector2 padding = new Vector2(10f, 10f);

    [Tooltip("Font size of the corner overlay.")]
    public int fontSize = 22;

    private float accumulatedTime = 0f;
    private int frames = 0;
    private float timeLeft;

    private float currentFps = 0f;
    private string fpsLabel = "";   // formatted once per readout update, not per OnGUI draw
    private GUIStyle overlayStyle;

    void Start()
    {
        timeLeft = updateInterval;
    }

    void Update()
    {
        timeLeft -= Time.unscaledDeltaTime;
        accumulatedTime += Time.timeScale / Time.unscaledDeltaTime;
        frames++;

        if (timeLeft <= 0f)
        {
            currentFps = accumulatedTime / frames;
            fpsLabel = currentFps.ToString("F1") + " FPS";

            if (fpsText != null)
            {
                fpsText.text = fpsLabel;
                fpsText.color = ColorForFps(currentFps);
            }

            timeLeft = updateInterval;
            accumulatedTime = 0f;
            frames = 0;
        }
    }

    void OnGUI()
    {
        // Only draw the corner overlay when no UI label is wired up.
        if (fpsText != null) return;

        if (overlayStyle == null)
        {
            overlayStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        }
        overlayStyle.fontSize = fontSize;
        overlayStyle.alignment = corner;
        overlayStyle.normal.textColor = ColorForFps(currentFps);

        Rect rect = new Rect(padding.x, padding.y,
                             Screen.width - padding.x * 2f,
                             Screen.height - padding.y * 2f);

        // Cheap drop shadow for readability over bright scenes.
        Color prev = overlayStyle.normal.textColor;
        overlayStyle.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), fpsLabel, overlayStyle);
        overlayStyle.normal.textColor = prev;

        GUI.Label(rect, fpsLabel, overlayStyle);
    }

    private Color ColorForFps(float fps)
    {
        if (fps < 30) return Color.red;
        if (fps < 60) return Color.yellow;
        return Color.green;
    }
}
