using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Pokémon-Legends-Arceus-style fish "research". While the player aims (right-mouse zoom — the rod's
// aim, FishingEvents.OnStartAiming/OnStopAiming) this locks onto a fish and the camera keeps it
// framed (PlayerCameraController.SetAimTrackTarget) until aim is released.
//
// FOCUS MODE (two-phase):
//   1. ACQUIRE — with no fish locked yet, the fish nearest the screen-centre reticle is picked up
//      the moment it's framed there, exactly like before.
//   2. STICKY FOCUS — once locked, the lock no longer follows the reticle. Instead the normal look
//      controls (mouse / right stick) STEER BETWEEN FISH: flick up / down / left / right and focus
//      jumps to the fish sitting in that direction from the one you're on. The camera auto-frames
//      the fish, so the look input is free for this. Releasing aim drops the lock.
//
// LEARNING BAIT — holding focus on ONE fish for `studyDuration` seconds (a charging window, with an
// optional charge sound) registers a zoom on that species and, the first time, reveals its bait
// preferences in the notebook and pops an on-screen "you learned this fish's bait preferences!"
// banner. Counting is PER DISTINCT FISH, not per lock — re-studying the same individual never
// re-counts, so researching a species means observing many different fish. Switching fish or losing
// the lock resets the charge. Fish have no body collider, so both the reticle acquire and the
// directional switch project each FishRipple.Active to screen space rather than raycasting.
public class FishResearchScanner : MonoBehaviour
{
    [Header("Lock Detection")]
    [Tooltip("Max distance (world units, along the camera's view) a fish can be and still be researchable while aiming.")]
    public float maxLockRange = 25f;
    [Tooltip("Radius from the reticle (as a fraction of screen HEIGHT, 0..1) within which a fish counts as 'under the reticle' when ACQUIRING the first lock.")]
    [Range(0.01f, 0.6f)] public float reticleRadiusFraction = 0.18f;
    [Tooltip("How far ABOVE screen centre the acquire reticle sits (fraction of screen HEIGHT). The player stands in front of dead-centre, so the detect point is raised into clear space above them. 0 = dead centre, negative = below.")]
    [Range(-0.4f, 0.4f)] public float reticleVerticalOffset = 0.13f;

    [Header("Focus Study (Learn Bait)")]
    [Tooltip("Seconds the player must keep focus on ONE fish before it registers — the charge window that learns the species' bait preference. Reset by switching fish or losing the lock.")]
    public float studyDuration = 3f;
    [Tooltip("Optional AudioSource used to play the charge sound while studying a fish. Set its clip below (or leave the source's own clip). Looped automatically.")]
    public AudioSource studyAudioSource;
    [Tooltip("Charge sound played (looped) while the study window is filling. Leave empty for a silent charge.")]
    public AudioClip studyChargeClip;
    [Tooltip("Optional radial/bar Image (Filled type) that fills 0..1 over the study window as focus charges.")]
    public Image studyProgressFill;

    [Header("\"Learned Bait\" Banner")]
    [Tooltip("CanvasGroup of the on-screen message shown when the player learns a fish's bait preference. Its alpha is driven here (fades in/out); leave empty to skip the banner.")]
    public CanvasGroup learnedBannerGroup;
    [Tooltip("Text element inside the banner, set to the message below when it pops. Optional.")]
    public TMP_Text learnedBannerText;
    [Tooltip("The message shown when a bait preference is learned.")]
    public string learnedMessage = "You learned this fish's bait preferences!";
    [Tooltip("Seconds the banner stays fully up before it fades out.")]
    public float bannerHoldTime = 2.5f;
    [Tooltip("Seconds the banner takes to fade in and to fade out.")]
    public float bannerFadeTime = 0.4f;

    [Header("Fish Switching")]
    [Tooltip("Mouse-move magnitude (per frame) that counts as a directional flick to switch fish.")]
    public float mouseSwitchThreshold = 3f;
    [Tooltip("Right-stick magnitude (0..1) that counts as a directional flick to switch fish.")]
    [Range(0.1f, 1f)] public float stickSwitchThreshold = 0.5f;
    [Tooltip("Minimum seconds between switches, so one gesture never skips several fish at once.")]
    public float switchCooldown = 0.3f;
    [Tooltip("How forgiving the direction match is when picking the next fish, in degrees to either side of the flick. Wider = easier to grab a fish that isn't perfectly in line.")]
    [Range(15f, 90f)] public float switchAngleTolerance = 65f;

    private Camera cam;
    private PlayerCameraController camController;
    private FishingRodController rod;
    private bool aiming;

    private FishRipple lockedFish;
    private float studyTime;

    // Re-arm gate + cooldown so one flick switches exactly one fish (see UpdateSwitching).
    private bool switchArmed = true;
    private float switchCooldownTimer;

    // Banner fade state (driven on realtime so it survives Time.timeScale changes).
    private float bannerTimer;

    // Fish instances already studied this session, so re-focusing the same individual never advances
    // the species count again. (Instances are destroyed/recreated as fish despawn, so new fish get
    // fresh IDs and do count — exactly the "research many individuals" loop we want.)
    private readonly HashSet<int> studiedInstances = new HashSet<int>();

    private void OnEnable()
    {
        FishingEvents.OnStartAiming += HandleStartAiming;
        FishingEvents.OnStopAiming += HandleStopAiming;
    }

    private void OnDisable()
    {
        FishingEvents.OnStartAiming -= HandleStartAiming;
        FishingEvents.OnStopAiming -= HandleStopAiming;
        ClearLock();
    }

    private void HandleStartAiming() => aiming = true;
    private void HandleStopAiming() { aiming = false; ClearLock(); }

    private void Update()
    {
        UpdateBanner();

        if (switchCooldownTimer > 0f) switchCooldownTimer -= Time.deltaTime;

        if (!aiming) { StopCharge(); return; }
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // Refuse to research while a cast is out — you can still aim/zoom, but the analyze lock
        // only counts free-swimming fish, never the ones your bobber/lure is already working.
        if (rod == null) rod = FindFirstObjectByType<FishingRodController>();
        if (rod != null && rod.IsCastActive) { ClearLock(); return; }

        // A locked fish that despawned drops the lock and falls back to reticle acquire.
        if (lockedFish == null)
        {
            FishRipple acquired = FindFishUnderReticle();
            if (acquired != null) SetLock(acquired);
            else StopCharge();
            return;
        }

        // Sticky focus: the look controls steer between fish instead of moving the (fish-tracking)
        // camera. A successful switch resets the study charge, so this runs before charging.
        if (UpdateSwitching()) return;

        // Hold focus to charge. Once this individual has already been studied, it just stays locked
        // (no re-charge, no re-count) — move to a fresh fish to research more of the species.
        int id = lockedFish.GetInstanceID();
        if (studiedInstances.Contains(id)) { StopCharge(); return; }

        studyTime += Time.deltaTime;
        DriveCharge(studyTime / Mathf.Max(0.01f, studyDuration));

        if (studyTime >= studyDuration)
        {
            studiedInstances.Add(id);
            StopCharge();
            if (FishEncyclopediaManager.Instance != null && lockedFish.preset != null)
            {
                bool learnedBait = FishEncyclopediaManager.Instance.RegisterFishZoom(lockedFish.preset);
                if (learnedBait) ShowBanner();
            }
        }
    }

    // Read the look controls; on a directional flick (re-armed, off cooldown) switch focus to the
    // fish lying in that screen direction from the current one. Returns true if focus switched.
    private bool UpdateSwitching()
    {
        Vector2 mouse = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        Vector2 stick = GamepadInput.Look;

        bool mouseFlick = mouse.magnitude >= mouseSwitchThreshold;
        bool stickFlick = stick.magnitude >= stickSwitchThreshold;

        // Re-arm once the controls have settled back toward centre, so a single flick (or one held
        // stick push) only steps once rather than racing through the whole shoal.
        if (!mouseFlick && stick.magnitude < stickSwitchThreshold * 0.5f) switchArmed = true;

        if (!switchArmed || switchCooldownTimer > 0f) return false;

        Vector2 dir = stickFlick ? stick : (mouseFlick ? mouse : Vector2.zero);
        if (dir.sqrMagnitude < 0.0001f) return false;

        FishRipple next = FindFishInDirection(dir.normalized);
        if (next == null) return false;

        SetLock(next);
        switchArmed = false;
        switchCooldownTimer = switchCooldown;
        return true;
    }

    // Nearest fish to the reticle (raised above screen centre) that's in front of the camera and in
    // range — used only to ACQUIRE the first lock.
    private FishRipple FindFishUnderReticle()
    {
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * (0.5f + reticleVerticalOffset));
        float reticlePx = Screen.height * reticleRadiusFraction;

        FishRipple best = null;
        float bestDist = float.MaxValue;

        List<FishRipple> list = FishRipple.Active;
        for (int i = 0; i < list.Count; i++)
        {
            FishRipple f = list[i];
            if (f == null || f.preset == null) continue;

            Vector3 sp = cam.WorldToScreenPoint(f.transform.position);
            if (sp.z <= 0f || sp.z > maxLockRange) continue; // behind the camera or too far

            float d = Vector2.Distance(new Vector2(sp.x, sp.y), center);
            if (d > reticlePx) continue;
            if (d < bestDist) { bestDist = d; best = f; }
        }
        return best;
    }

    // The fish that best lies in screen direction `dir` from the currently locked fish: within the
    // angle tolerance, preferring a close direction match and, as a tiebreak, the nearer fish.
    private FishRipple FindFishInDirection(Vector2 dir)
    {
        if (lockedFish == null) return null;
        Vector3 fromSp = cam.WorldToScreenPoint(lockedFish.transform.position);
        if (fromSp.z <= 0f) return null;
        Vector2 from = new Vector2(fromSp.x, fromSp.y);

        float minDot = Mathf.Cos(switchAngleTolerance * Mathf.Deg2Rad);

        FishRipple best = null;
        float bestScore = float.NegativeInfinity;

        List<FishRipple> list = FishRipple.Active;
        for (int i = 0; i < list.Count; i++)
        {
            FishRipple f = list[i];
            if (f == null || f == lockedFish || f.preset == null) continue;

            Vector3 sp = cam.WorldToScreenPoint(f.transform.position);
            if (sp.z <= 0f || sp.z > maxLockRange) continue;

            Vector2 delta = new Vector2(sp.x, sp.y) - from;
            float dist = delta.magnitude;
            if (dist < 1f) continue; // essentially on top of the current fish

            float dot = Vector2.Dot(delta / dist, dir);
            if (dot < minDot) continue;

            // Direction match dominates; screen distance is only a fine tiebreak (nearer preferred).
            float score = dot * 1000f - dist;
            if (score > bestScore) { bestScore = score; best = f; }
        }
        return best;
    }

    private void SetLock(FishRipple fish)
    {
        lockedFish = fish;
        studyTime = 0f;
        StopCharge();
        if (camController == null) camController = FindFirstObjectByType<PlayerCameraController>();
        if (camController != null)
            camController.SetAimTrackTarget(fish != null ? fish.transform : null);
    }

    private void ClearLock()
    {
        lockedFish = null;
        studyTime = 0f;
        StopCharge();
        if (camController != null) camController.SetAimTrackTarget(null);
    }

    // ----- Charge feedback (sound + progress fill) -----

    private void DriveCharge(float progress01)
    {
        progress01 = Mathf.Clamp01(progress01);
        if (studyProgressFill != null)
        {
            if (!studyProgressFill.enabled) studyProgressFill.enabled = true;
            studyProgressFill.fillAmount = progress01;
        }
        if (studyAudioSource != null && !studyAudioSource.isPlaying)
        {
            if (studyChargeClip != null) studyAudioSource.clip = studyChargeClip;
            studyAudioSource.loop = true;
            if (studyAudioSource.clip != null) studyAudioSource.Play();
        }
    }

    private void StopCharge()
    {
        if (studyProgressFill != null)
        {
            studyProgressFill.fillAmount = 0f;
            studyProgressFill.enabled = false;
        }
        if (studyAudioSource != null && studyAudioSource.isPlaying) studyAudioSource.Stop();
    }

    // ----- "Learned bait" banner -----

    private void ShowBanner()
    {
        if (learnedBannerGroup == null) return;
        if (learnedBannerText != null) learnedBannerText.text = learnedMessage;
        bannerTimer = bannerHoldTime + bannerFadeTime;
    }

    private void UpdateBanner()
    {
        if (learnedBannerGroup == null) return;

        float dt = Time.unscaledDeltaTime;
        if (bannerTimer > 0f) bannerTimer -= dt;

        float target = bannerTimer <= 0f ? 0f
                     : (bannerTimer > bannerFadeTime ? 1f : Mathf.Clamp01(bannerTimer / bannerFadeTime));
        learnedBannerGroup.alpha = Mathf.MoveTowards(
            learnedBannerGroup.alpha, target, dt / Mathf.Max(0.01f, bannerFadeTime));
    }
}
