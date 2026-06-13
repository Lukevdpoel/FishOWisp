using UnityEngine;

// The "aim glimpse" UI indicator that briefly appears above a fish while the player is in
// aim mode. Four-phase state machine: Hidden → FadingIn → Holding → FadingOut → Hidden,
// re-triggering on a random interval. Out-of-camera-range or aim-mode-off pauses the cycle.
//
// Owned by FishRipple. It calls Initialize() in Awake, ShowOnAim()/HideOnAim() on the
// fishing events, and Tick() each frame. Config values stay on FishRipple as SerializeFields
// so prefab inspector values survive — they get passed in via a Settings struct each tick.
public class FishGlimpseIndicator
{
    public struct Settings
    {
        public bool alwaysShowIndicator;
        public float aimRevealRange;
        public float glimpseIntervalMin;
        public float glimpseIntervalMax;
        public float glimpseDurationMin;
        public float glimpseDurationMax;
        public float glimpseFadeIn;
        public float glimpseFadeOut;
    }

    private enum GlimpsePhase { Hidden, FadingIn, Holding, FadingOut }

    private GameObject indicatorInstance;
    private Canvas indicatorCanvas;
    private CanvasGroup indicatorCanvasGroup;
    private bool isAimingActive;
    private float glimpseTimer;
    private float glimpseAlpha;
    private GlimpsePhase glimpsePhase = GlimpsePhase.Hidden;

    public bool HasIndicator => indicatorInstance != null;
    public GameObject IndicatorInstance => indicatorInstance;

    // Instantiates the indicator under hostTransform and applies the FishPreset to the
    // FishAimIndicator child if present. Safe to call with prefab == null (no-op).
    public void Initialize(GameObject prefab, Transform hostTransform, FishPreset preset)
    {
        if (prefab == null) return;

        indicatorInstance = Object.Instantiate(prefab, hostTransform);
        indicatorInstance.transform.localPosition = Vector3.zero;

        indicatorCanvas = indicatorInstance.GetComponentInChildren<Canvas>();
        if (indicatorCanvas != null)
        {
            indicatorCanvas.overrideSorting = true;
            indicatorCanvas.sortingOrder = 1000;
        }

        indicatorCanvasGroup = indicatorInstance.GetComponentInChildren<CanvasGroup>();
        if (indicatorCanvasGroup == null && indicatorCanvas != null)
            indicatorCanvasGroup = indicatorCanvas.gameObject.AddComponent<CanvasGroup>();

        FishAimIndicator aimIndicator = indicatorInstance.GetComponentInChildren<FishAimIndicator>(true);
        if (aimIndicator != null) aimIndicator.ApplyPreset(preset);

        if (indicatorCanvasGroup != null) indicatorCanvasGroup.alpha = 0f;
        indicatorInstance.SetActive(false);
    }

    public void ShowOnAim(in Settings s)
    {
        isAimingActive = true;
        EnterHidden(Random.Range(s.glimpseIntervalMin, s.glimpseIntervalMax));
    }

    public void HideOnAim()
    {
        isAimingActive = false;
        EnterHidden(0f);
    }

    // Force-hide for permanent state changes (fish caught, fight ends, etc.).
    public void ForceHide()
    {
        if (indicatorInstance != null) indicatorInstance.SetActive(false);
    }

    public void Tick(Vector3 fishPosition, in Settings s)
    {
        if (indicatorInstance == null) return;

        if (s.alwaysShowIndicator)
        {
            if (glimpseAlpha != 1f) glimpseAlpha = 1f;
            glimpsePhase = GlimpsePhase.Holding;
            ApplyGlimpseAlpha();
            FaceCamera();
            return;
        }

        if (!isAimingActive)
        {
            if (glimpseAlpha != 0f)
            {
                glimpseAlpha = 0f;
                ApplyGlimpseAlpha();
            }
            return;
        }

        bool inRange = IsWithinRevealRange(fishPosition, s.aimRevealRange);
        if (!inRange)
        {
            // Out of range — fade out if currently visible, then idle hidden until back in range.
            if (glimpsePhase == GlimpsePhase.FadingIn || glimpsePhase == GlimpsePhase.Holding)
            {
                glimpsePhase = GlimpsePhase.FadingOut;
            }
            else if (glimpsePhase == GlimpsePhase.Hidden)
            {
                glimpseTimer = Random.Range(s.glimpseIntervalMin, s.glimpseIntervalMax);
            }
        }

        glimpseTimer -= Time.deltaTime;

        switch (glimpsePhase)
        {
            case GlimpsePhase.Hidden:
                if (inRange && glimpseTimer <= 0f)
                {
                    glimpsePhase = GlimpsePhase.FadingIn;
                    glimpseTimer = Mathf.Max(0.0001f, s.glimpseFadeIn);
                }
                break;

            case GlimpsePhase.FadingIn:
            {
                float dur = Mathf.Max(0.0001f, s.glimpseFadeIn);
                float t = 1f - Mathf.Clamp01(glimpseTimer / dur);
                glimpseAlpha = Mathf.SmoothStep(0f, 1f, t);
                if (glimpseTimer <= 0f)
                {
                    glimpseAlpha = 1f;
                    glimpsePhase = GlimpsePhase.Holding;
                    glimpseTimer = Random.Range(s.glimpseDurationMin, s.glimpseDurationMax);
                }
                break;
            }

            case GlimpsePhase.Holding:
                glimpseAlpha = 1f;
                if (glimpseTimer <= 0f)
                {
                    glimpsePhase = GlimpsePhase.FadingOut;
                    glimpseTimer = Mathf.Max(0.0001f, s.glimpseFadeOut);
                }
                break;

            case GlimpsePhase.FadingOut:
            {
                float dur = Mathf.Max(0.0001f, s.glimpseFadeOut);
                float t = Mathf.Clamp01(glimpseTimer / dur);
                glimpseAlpha = Mathf.SmoothStep(0f, 1f, t);
                if (glimpseTimer <= 0f)
                {
                    EnterHidden(Random.Range(s.glimpseIntervalMin, s.glimpseIntervalMax));
                    return;
                }
                break;
            }
        }

        ApplyGlimpseAlpha();
        if (glimpseAlpha > 0.001f) FaceCamera();
    }

    private void EnterHidden(float waitSeconds)
    {
        glimpsePhase = GlimpsePhase.Hidden;
        glimpseTimer = waitSeconds;
        glimpseAlpha = 0f;
        ApplyGlimpseAlpha();
    }

    private void ApplyGlimpseAlpha()
    {
        if (indicatorInstance == null) return;
        if (indicatorCanvasGroup != null) indicatorCanvasGroup.alpha = glimpseAlpha;
        bool shouldBeActive = glimpseAlpha > 0.001f;
        if (indicatorInstance.activeSelf != shouldBeActive)
            indicatorInstance.SetActive(shouldBeActive);
    }

    private bool IsWithinRevealRange(Vector3 fishPosition, float aimRevealRange)
    {
        if (aimRevealRange <= 0f) return true;
        Camera cam = Camera.main;
        if (cam == null) return true;
        float sqr = (fishPosition - cam.transform.position).sqrMagnitude;
        return sqr <= aimRevealRange * aimRevealRange;
    }

    private void FaceCamera()
    {
        if (indicatorInstance == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        indicatorInstance.transform.rotation = Quaternion.LookRotation(
            indicatorInstance.transform.position - cam.transform.position
        );
    }
}
