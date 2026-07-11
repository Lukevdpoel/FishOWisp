using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// A small HUD popup that fades in to announce the player's current world state:
//
//     <LOCATION>     - the current location in caps (depends on the scene)
//     2:30 PM · Day  - smaller line: the in-game time (AM/PM) plus the current Day/Night state
//     <weather>      - reserved line, stays blank until a weather system feeds it
//
// It triggers on the day<->night flip (that's the cue to pop); the second line shows the clock
// time at that moment alongside the Day/Night word, so a glance tells you both. Weather doesn't
// exist yet, but the layout already reserves a line for it and NotifyWeatherChanged(...) is the
// single call a future weather system makes to trigger the same popup.
//
// The popup always renders the *current* full world state, so every trigger simply refreshes
// all three lines and replays the fade. That means a day/night change and a weather change
// landing together produce one tidy popup showing everything, not two competing ones.
//
// Day/night truth comes from WorldStateManager.IsNight. Forced changes (vendor lantern, etc.)
// raise OnWorldStateChanged, but a *natural* dusk/dawn crossing in Auto mode raises nothing,
// so we also poll IsNight each frame to catch it.
public class WorldStatePopup : GenericSingleton<WorldStatePopup>
{
    [Serializable]
    public struct SceneLocation
    {
        [Tooltip("Scene file name, no path or extension. e.g. \"HUTBUILT\".")]
        public string sceneName;

        [Tooltip("Location label shown in the popup. e.g. \"The Hut\". Drawn in caps when Uppercase Location is on.")]
        public string displayName;
    }

    [Header("Location Names (per scene)")]
    [Tooltip("Fill in a display name for each scene. The active scene's name is matched against these entries.")]
    public List<SceneLocation> sceneLocations = new List<SceneLocation>();

    [Tooltip("Optional override. If set, this name is used instead of the per-scene lookup — handy when a " +
             "copy of the popup lives inside a single scene and you'd rather type the name directly.")]
    public string explicitLocationName = "";

    [Tooltip("Shown when the active scene isn't in the list and no explicit name is set.")]
    public string fallbackLocationName = "Unknown";

    public bool uppercaseLocation = true;

    [Header("Time Display")]
    [Tooltip("On = 24-hour clock (\"14:30\"). Off = 12-hour clock with AM/PM (\"2:30 PM\").")]
    public bool use24HourClock = false;

    [Header("Timing (seconds)")]
    public float fadeInDuration = 0.4f;
    public float holdDuration = 2.5f;
    public float fadeOutDuration = 0.7f;
    [Tooltip("Use unscaled time so the popup still animates while the game is paused (Time.timeScale = 0).")]
    public bool useUnscaledTime = true;

    [Header("Behaviour")]
    [Tooltip("Also play the popup once when a scene finishes loading, announcing where you've arrived. " +
             "Off by default — the popup is otherwise only triggered by day/night (and later weather) changes.")]
    public bool showOnSceneLoad = false;

    [Header("UI References (auto-built if left empty)")]
    public CanvasGroup canvasGroup;
    public TMP_Text locationText;
    public TMP_Text timeText;
    [Tooltip("Reserved for weather. Kept in the layout (blank) until NotifyWeatherChanged feeds it a value.")]
    public TMP_Text weatherText;
    [Tooltip("When the references above are empty, build a simple default popup at runtime so this works as " +
             "soon as it's dropped into a scene. Turn off once you've wired your own styled UI.")]
    public bool buildDefaultUIIfMissing = true;

    [Tooltip("Optional banner image drawn behind the texts in the AUTO-BUILT popup. Leave empty for no " +
             "background. (Ignored when you wire your own UI references — put the banner in your own hierarchy.)")]
    public Sprite bannerSprite;
    public Color bannerColor = Color.white;

    // While true, Show() is a no-op. The main menu raises this on boot so a dusk/dawn crossing during
    // the live flythrough can't pop the banner over the title screen, then lowers it (and fires one
    // welcome Show()) a beat after the player presses to play. Reset each play session below so a
    // domain-reload-off run never boots stuck-suppressed.
    public static bool Suppressed { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSuppressed() => Suppressed = false;

    private bool _initialized;
    private bool _lastIsNight;
    private string _currentWeather = "";   // empty = no weather yet; the weather line stays blank but keeps its slot
    private Coroutine _animRoutine;

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return; // duplicate; GenericSingleton is tearing this one down

        if (NeedsUI() && buildDefaultUIIfMissing) BuildDefaultUI();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            // A pure announcement — never let it intercept clicks.
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void OnEnable()
    {
        WorldStateManager.OnWorldStateChanged += HandleWorldStateChanged;
        if (showOnSceneLoad) SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        WorldStateManager.OnWorldStateChanged -= HandleWorldStateChanged;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Start()
    {
        // Record the starting day/night state so we don't fire a popup on the very first frame.
        _lastIsNight = CurrentIsNight();
        _initialized = true;
        if (showOnSceneLoad) Show();
    }

    private void Update()
    {
        // The world clock slides past dusk/dawn with no event of its own, so watch for the flip.
        if (!_initialized) return;
        bool night = CurrentIsNight();
        if (night != _lastIsNight)
        {
            _lastIsNight = night;
            Show();
        }
    }

    // Forced day/night (vendor lantern) and clock skips raise this. Treat it the same way as the
    // poll: only act when the day/night result actually changed.
    private void HandleWorldStateChanged()
    {
        if (!_initialized) { _lastIsNight = CurrentIsNight(); return; }
        bool night = CurrentIsNight();
        if (night != _lastIsNight)
        {
            _lastIsNight = night;
            Show();
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Show();

    private static bool CurrentIsNight()
        => WorldStateManager.Instance != null && WorldStateManager.Instance.IsNight;

    // The world's effective hour: WorldStateManager honors forced day/night overrides and falls
    // back to the live GameTimeManager clock, so this matches what the sky actually looks like.
    private static float CurrentHour()
    {
        if (WorldStateManager.Instance != null) return WorldStateManager.Instance.GetEffectiveHour();
        if (GameTimeManager.Instance != null) return GameTimeManager.Instance.CurrentHour;
        return 12f;
    }

    // ---- Public triggers -------------------------------------------------------------------

    // Future weather system entry point. Call this whenever the weather changes; pass the label
    // you want shown (e.g. "Rain", "Fog"), or "" to clear it. Plays the popup with the new state.
    public void NotifyWeatherChanged(string weatherDisplayName)
    {
        _currentWeather = weatherDisplayName ?? "";
        Show();
    }

    // Refresh all three lines from the current world state and play the fade.
    [ContextMenu("Preview Popup")]
    public void Show()
    {
        // Held down by the main menu while the title screen is up (see Suppressed above).
        if (Suppressed && Application.isPlaying) return;

        RefreshContent();
        if (canvasGroup == null) return;

        if (!Application.isPlaying)
        {
            // Coroutines don't run in edit mode; show a static preview instead.
            canvasGroup.alpha = 1f;
            return;
        }

        if (!isActiveAndEnabled) return;
        if (_animRoutine != null) StopCoroutine(_animRoutine);
        _animRoutine = StartCoroutine(AnimateRoutine());
    }

    // ---- Content ---------------------------------------------------------------------------

    private void RefreshContent()
    {
        if (locationText != null)
        {
            string loc = ResolveLocationName();
            locationText.text = uppercaseLocation ? loc.ToUpperInvariant() : loc;
        }

        if (timeText != null)
        {
            string dayNight = CurrentIsNight() ? "Night" : "Day";
            timeText.text = $"{FormatTime(CurrentHour())}  ·  {dayNight}";
        }

        if (weatherText != null)
            weatherText.text = _currentWeather; // blank until weather is set; keeps its layout slot either way
    }

    // Turns a 0-24 float hour into a clock string, honoring the 12/24-hour toggle.
    private string FormatTime(float hour)
    {
        hour = Mathf.Repeat(hour, 24f);
        int hours = Mathf.FloorToInt(hour);
        int minutes = Mathf.FloorToInt((hour - hours) * 60f);
        if (minutes >= 60) { minutes = 0; hours = (hours + 1) % 24; } // guard float rounding landing on :60

        if (use24HourClock)
            return $"{hours:00}:{minutes:00}";

        string period = hours >= 12 ? "PM" : "AM";
        int twelve = hours % 12;
        if (twelve == 0) twelve = 12;
        return $"{twelve}:{minutes:00} {period}";
    }

    private string ResolveLocationName()
    {
        if (!string.IsNullOrWhiteSpace(explicitLocationName)) return explicitLocationName;

        string scene = SceneManager.GetActiveScene().name;
        foreach (var entry in sceneLocations)
        {
            if (entry.sceneName == scene && !string.IsNullOrWhiteSpace(entry.displayName))
                return entry.displayName;
        }
        return fallbackLocationName;
    }

    // ---- Animation -------------------------------------------------------------------------

    private IEnumerator AnimateRoutine()
    {
        yield return Fade(canvasGroup.alpha, 1f, fadeInDuration);

        float t = 0f;
        while (t < holdDuration)
        {
            t += Delta();
            yield return null;
        }

        yield return Fade(1f, 0f, fadeOutDuration);
        _animRoutine = null;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            canvasGroup.alpha = to;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Delta();
            canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        canvasGroup.alpha = to;
    }

    private float Delta() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    // ---- Default UI builder ----------------------------------------------------------------

    private bool NeedsUI() => canvasGroup == null || locationText == null || timeText == null;

    // Builds a no-frills top-centre popup so the component works the moment it's added. Replace
    // it with your own styled UI (assign the references above) whenever you're ready; this mirrors
    // the exact hierarchy you'd build by hand: Canvas > Popup(CanvasGroup) > three stacked texts.
    private void BuildDefaultUI()
    {
        var canvasGO = new GameObject("WorldStatePopupCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // above gameplay HUD, below hard transitions

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // The faded group, anchored to the top-centre of the screen.
        var panelGO = new GameObject("Popup", typeof(RectTransform), typeof(CanvasGroup));
        panelGO.transform.SetParent(canvasGO.transform, false);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 1f);
        panelRT.pivot = new Vector2(0.5f, 1f);
        panelRT.anchoredPosition = new Vector2(0f, -80f);
        panelRT.sizeDelta = new Vector2(900f, 220f);
        canvasGroup = panelGO.GetComponent<CanvasGroup>();

        // Optional banner behind the texts — added first so it sits behind them, stretched to the panel.
        if (bannerSprite != null)
        {
            var bannerGO = new GameObject("Banner", typeof(RectTransform), typeof(Image));
            bannerGO.transform.SetParent(panelRT, false);
            var brt = bannerGO.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            var bimg = bannerGO.GetComponent<Image>();
            bimg.sprite = bannerSprite;
            bimg.type = Image.Type.Sliced;
            bimg.color = bannerColor;
            bimg.raycastTarget = false;
        }

        // Stacked from the top of the panel: location (big), day/night (medium), weather (small).
        locationText = CreateText(panelRT, "LocationText", 72f, FontStyles.Bold,
            new Color(1f, 1f, 1f, 1f), topY: -10f, height: 90f);
        timeText = CreateText(panelRT, "TimeText", 36f, FontStyles.Normal,
            new Color(1f, 1f, 1f, 0.85f), topY: -100f, height: 50f);
        weatherText = CreateText(panelRT, "WeatherText", 30f, FontStyles.Normal,
            new Color(1f, 1f, 1f, 0.7f), topY: -150f, height: 44f); // reserved slot, starts blank
    }

    private static TMP_Text CreateText(RectTransform parent, string name, float fontSize,
        FontStyles style, Color color, float topY, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        // Stretch full width of the panel, fixed height, positioned from the panel's top edge.
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = new Vector2(0f, topY);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Top;
        tmp.raycastTarget = false;
        return tmp;
    }
}
