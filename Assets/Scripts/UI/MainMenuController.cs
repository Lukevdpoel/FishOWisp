using System.Collections;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities; // CallOnce extension on onAnyButtonPress

/// <summary>
/// Startup / title screen that overlays the live HUTBUILT scene. On boot it shows the logo +
/// "press any button to play" while a Cinemachine MenuDolly vcam flies through the (still living)
/// world. The first button press hands the camera to the gameplay orbit camera in ONE move: the
/// brain is switched off on the spot, the orbit pose is seeded from the PlayerFollow vcam's
/// authored pose (which fixes the resting framing/distance), and a single handoff ease carries the
/// camera from wherever the flythrough left it straight into the live orbit pose over
/// <see cref="cameraBlendDuration"/>. The PlayerFollow vcam is never made live — it only donates
/// its pose. (Previously the brain first blended MenuDolly -> PlayerFollow and a second ease then
/// re-targeted the orbit pose; the camera visibly stopped between the two moves.)
///
/// While the menu is up, player input is gated by DISABLING the input components (the world stays
/// alive, so a timeScale freeze isn't used): <see cref="playerController"/> plus everything in
/// <see cref="gameplayToSuspend"/> (FishingRodController, NoteMenu, InventoryUI). The day/night
/// banner is held off via <see cref="WorldStatePopup.Suppressed"/> and replayed once, after a beat,
/// post-start as a gentle welcome.
///
/// Place this on the MainMenu ROOT object, with the menu Canvas as a CHILD — FinishStart hides the
/// canvas but keeps this object alive so the delayed-welcome coroutine can finish.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Menu UI")]
    [Tooltip("CanvasGroup on the menu Canvas (logo + prompt). Should live on a CHILD of this object.")]
    [SerializeField] private CanvasGroup menuGroup;
    [Tooltip("The 'press any button to play' label. Pulses while the menu is up.")]
    [SerializeField] private TMP_Text pressPrompt;
    [Tooltip("Full-screen BLACK overlay (CanvasGroup on an opaque Image) drawn ON TOP of the menu + " +
             "world. Starts opaque at boot and fades to clear, so the title reveals from black. Put it " +
             "on its own Canvas (or as the last/topmost child) with a sort order above the menu. " +
             "Optional — leave empty for the old instant pop-in.")]
    [SerializeField] private CanvasGroup blackFade;

    [Header("Camera (Cinemachine)")]
    [Tooltip("The CinemachineBrain on the main camera. Enabled while the menu drives the camera; " +
             "disabled at handoff so the custom PlayerCameraController owns the camera again. " +
             "MUST be the CinemachineBrain component itself, not the Camera GameObject.")]
    [SerializeField] private CinemachineBrain cinemachineBrain;
    [Tooltip("The MenuDolly virtual camera GameObject (spline flythrough). Active at boot.")]
    [SerializeField] private GameObject menuDollyVcam;
    [Tooltip("The PlayerFollow virtual camera GameObject (over-the-shoulder). Never made live — its " +
             "authored transform is the pose the gameplay orbit camera is seeded from at the press.")]
    [SerializeField] private GameObject playerFollowVcam;

    [Header("Gameplay Suspension")]
    [Tooltip("Player controller — disabled during the menu, then enabled at button press (but kept " +
             "locked) so it settles onto the ground while the camera eases in, and finally unlocked " +
             "once the ease completes.")]
    [SerializeField] private PlayerController playerController;
    [Tooltip("Other input components disabled while the menu is up and re-enabled at handoff " +
             "(e.g. FishingRodController, NoteMenu, InventoryUI).")]
    [SerializeField] private MonoBehaviour[] gameplayToSuspend;
    [Tooltip("Optional. The player's visual root (meshes). Hidden during the menu and revealed at " +
             "button press, so the flythrough never shows the player. Leave empty if you instead " +
             "frame the menu camera away from the player yourself.")]
    [SerializeField] private GameObject playerVisual;

    [Header("Timing (seconds)")]
    [Tooltip("How long the single camera ease from the flythrough pose into the gameplay orbit pose " +
             "takes after the press. Also how long the player stays locked. Larger = gentler/slower. " +
             "0 = snap.")]
    [SerializeField] private float cameraBlendDuration = 2.5f;
    [Tooltip("How long the menu UI takes to fade out on start. 0 = snap.")]
    [SerializeField] private float menuFadeDuration = 0.6f;
    [Tooltip("How long the black overlay takes to fade IN the title (black -> clear) at boot. 0 = snap.")]
    [SerializeField] private float fadeInDuration = 1.5f;
    [Tooltip("Delay after control transfers before the day/night welcome banner plays, so it isn't harsh.")]
    [SerializeField] private float welcomePopupDelay = 1.5f;
    [Tooltip("FALLBACK only (a camera reference was missing at the press, so the single-move handoff " +
             "couldn't run): short ease from wherever the camera is at handoff into the live orbit pose.")]
    [SerializeField] private float cameraHandoffEase = 0.4f;

    [Header("Prompt Pulse")]
    [SerializeField] private float promptPulseSpeed = 2f;
    [SerializeField, Range(0f, 1f)] private float promptMinAlpha = 0.3f;
    [SerializeField, Range(0f, 1f)] private float promptMaxAlpha = 1f;

    private bool started;

    // True when the press-time single-move handoff ran (TryBeginCameraBlend) — the orbit camera
    // already owns the camera, so FinishStart must not re-seed it. False = fallback path.
    private bool singleBlendStarted;

    // True once the startup menu has run this play session. Being static, it survives scene loads,
    // so returning to this scene from another (e.g. back from CAVE) skips the title and drops straight
    // into gameplay. Reset once per play session below (so a fresh launch shows the menu again).
    private static bool hasBootedThisSession;

    /// <summary>
    /// True from boot until the camera has fully handed off to the player (i.e. through the title
    /// flythrough and the MenuDolly -> PlayerFollow blend, cleared in <see cref="FinishStart"/>).
    /// Gameplay UIs that read input directly in their own Update — the notebook, the gear/tackle
    /// menu, the pause menu — check this so they can't be opened while the title is still up. They
    /// live in a different prefab than the player, so unlike the fishing scripts they can't be wired
    /// into <see cref="gameplayToSuspend"/>; this static is how they self-gate. False in scenes with
    /// no title menu and on a return visit (menu skipped), so it never blocks normal gameplay.
    /// </summary>
    public static bool IsTitleSequenceActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        hasBootedThisSession = false;
        IsTitleSequenceActive = false;
    }

    /// <summary>
    /// Force the title screen to play again on the NEXT load of this scene within the same play
    /// session. Normally the menu shows only once per session (see <see cref="hasBootedThisSession"/>);
    /// call this right before reloading the boot scene to replay it — used by the Ctrl+R "restart to
    /// boot menu" debug shortcut in DebugProgressReset.
    /// </summary>
    public static void RequestShowOnNextLoad() => hasBootedThisSession = false;

    private void Awake()
    {
        // The cinemachineBrain scene reference has a habit of coming unwired (it ends up null in
        // HUTBUILT, e.g. after a prefab revert). A null brain silently kills the single-move handoff
        // (TryBeginCameraBlend bails) AND leaves the live brain fighting the orbit camera at handoff.
        // Since the brain always lives on the main camera, just resolve it ourselves when the field
        // is empty so the handoff is robust to the wiring.
        if (cinemachineBrain == null)
        {
            if (Camera.main != null) cinemachineBrain = Camera.main.GetComponent<CinemachineBrain>();
            if (cinemachineBrain == null) cinemachineBrain = FindFirstObjectByType<CinemachineBrain>(FindObjectsInactive.Include);
        }

        // Only run the title on the FIRST load of this scene per session. On a return visit, make
        // sure nothing menu-related is left driving the camera, then bow out so gameplay starts
        // immediately.
        if (hasBootedThisSession)
        {
            if (cinemachineBrain != null) cinemachineBrain.enabled = false;
            if (menuDollyVcam != null) menuDollyVcam.SetActive(false);
            if (playerFollowVcam != null) playerFollowVcam.SetActive(false);
            if (blackFade != null) blackFade.gameObject.SetActive(false); // no fade on a return visit
            WorldStatePopup.Suppressed = false;
            IsTitleSequenceActive = false; // no title on a return visit — gameplay UIs stay usable
            gameObject.SetActive(false);
            return;
        }
        hasBootedThisSession = true;

        // Block the input-driven gameplay UIs (notebook / gear menu / pause) until handoff.
        IsTitleSequenceActive = true;

        // Hold the day/night banner off the title screen (lowered after start, see WelcomeRoutine).
        WorldStatePopup.Suppressed = true;

        // Camera: brain drives the flythrough through the MenuDolly vcam.
        if (cinemachineBrain != null) cinemachineBrain.enabled = true;
        if (menuDollyVcam != null) menuDollyVcam.SetActive(true);
        if (playerFollowVcam != null) playerFollowVcam.SetActive(false);

        // Gate player input by suspending the components that read it (world stays alive). The
        // player stays disabled (and hidden, if a visual root is assigned) until the player presses
        // to start, so the flythrough shows the world without the player standing inertly in it.
        if (playerController != null) playerController.enabled = false;
        if (playerVisual != null) playerVisual.SetActive(false);
        SetSuspended(true);

        if (menuGroup != null)
        {
            menuGroup.alpha = 1f;
            menuGroup.gameObject.SetActive(true);
        }

        // Cover the screen with black from frame 1 so the title reveals from black (Start fades it out).
        if (blackFade != null)
        {
            blackFade.alpha = 1f;
            blackFade.gameObject.SetActive(true);
        }
    }

    private IEnumerator Start()
    {
        // Skip a couple of frames so a key that happened to be down at boot doesn't instantly start.
        yield return null;
        yield return null;

        // Fade the black overlay out, revealing the menu + live world. Runs concurrently with the
        // input wait below, so the player can still press to start during the fade.
        StartCoroutine(FadeInFromBlack());

        // Fires on the next press of any control on any device (keyboard / mouse / any gamepad brand).
        InputSystem.onAnyButtonPress.CallOnce(_ => BeginStart());
    }

    private void Update()
    {
        if (started || pressPrompt == null) return;
        float pulse = Mathf.Sin(Time.unscaledTime * promptPulseSpeed) * 0.5f + 0.5f;
        pressPrompt.alpha = Mathf.Lerp(promptMinAlpha, promptMaxAlpha, pulse);
    }

    private IEnumerator FadeInFromBlack()
    {
        if (blackFade == null) yield break;

        if (fadeInDuration <= 0f)
        {
            blackFade.alpha = 0f;
            blackFade.gameObject.SetActive(false);
            yield break;
        }

        float t = 0f;
        while (t < fadeInDuration)
        {
            // Unscaled: the fade shouldn't care if anything has touched timeScale.
            t += Time.unscaledDeltaTime;
            blackFade.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(t / fadeInDuration));
            yield return null;
        }
        blackFade.alpha = 0f;
        blackFade.gameObject.SetActive(false); // disable so it never eats raycasts once clear
    }

    private void BeginStart()
    {
        if (started) return;
        started = true;

        // Bring the player in NOW, at the start of the camera ease: reveal it, enable it so gravity
        // seats it on the ground, but keep movement/interaction locked. Because this happens while
        // the camera is still travelling, the settle finishes off-screen and the player looks
        // planted by the time the camera arrives.
        if (playerVisual != null) playerVisual.SetActive(true);
        if (playerController != null)
        {
            playerController.LockControls(true);
            playerController.enabled = true;
        }

        singleBlendStarted = TryBeginCameraBlend();
        if (!singleBlendStarted && playerController != null)
        {
            // Fallback: the brain keeps the camera until FinishStart, so suppress the orbit
            // camera's writes (and the cursor) exactly like the old two-step handoff did.
            playerController.suppressCameraControl = true;
        }

        StartCoroutine(StartSequence());
    }

    // Single-move handoff. The orbit camera takes ownership of the camera right here, at the press:
    // the brain is switched off (leaving the camera at the flythrough pose it currently sits at), the
    // gameplay orbit's RESTING pose is seeded from the PlayerFollow vcam's authored world pose, and
    // one handoff ease then carries the camera from the flythrough pose into that resting pose. The
    // camera is NOT moved here — that's what lets the ease start from the real flythrough position.
    //
    // Seeding takes the resting ANGLES from the vcam's rotation (a fixed behind-and-looking-down
    // orientation) rather than from the camera's position relative to the player: the player was only
    // just enabled and hasn't settled onto the ground, so a position-derived angle would land the
    // camera off to the side. See PlayerCameraController.SeedRestingPoseFromMenuVcam.
    //
    // Returns false when a reference the seeding needs is missing; FinishStart then falls back to
    // grabbing the camera wherever it is once the timed window elapses.
    private bool TryBeginCameraBlend()
    {
        if (cinemachineBrain == null || playerFollowVcam == null || playerController == null)
            return false;

        cinemachineBrain.enabled = false;

        Transform followPose = playerFollowVcam.transform;
        playerController.SeedCameraFromMenuVcam(followPose.position, followPose.rotation);

        // Captures the flythrough pose (the camera hasn't moved) as the ease's start; the orbit
        // camera lerps out of it toward the seeded resting pose from the next UpdateCamera on.
        playerController.BeginCameraHandoffBlend(cameraBlendDuration);
        return true;
    }

    private IEnumerator StartSequence()
    {
        // Fade the menu out while the camera makes its single ease to the player, then unlock once
        // the ease has finished. (In the fallback this is just a timed grace window before
        // FinishStart takes the camera wherever it is.)
        float t = 0f;
        while (t < cameraBlendDuration)
        {
            t += Time.deltaTime;
            if (menuGroup != null)
                menuGroup.alpha = menuFadeDuration <= 0f
                    ? 0f
                    : Mathf.Lerp(1f, 0f, Mathf.Clamp01(t / menuFadeDuration));
            yield return null;
        }

        FinishStart();
    }

    private void FinishStart()
    {
        // Camera has arrived at the player — the gameplay UIs may now respond to input.
        IsTitleSequenceActive = false;

        if (menuGroup != null)
        {
            menuGroup.alpha = 0f;
            menuGroup.gameObject.SetActive(false);
        }

        if (!singleBlendStarted)
        {
            // Fallback (a camera reference was missing at the press): stop the brain and take the
            // camera wherever it is now, with the short ease absorbing the takeover.
            if (cinemachineBrain != null) cinemachineBrain.enabled = false;
            if (playerController != null)
            {
                playerController.ReinitializeCamera();
                playerController.BeginCameraHandoffBlend(cameraHandoffEase);
                playerController.suppressCameraControl = false;
            }
        }
        // In the single-move path the orbit camera has owned the camera since the press — nothing
        // to hand over; just unlock the (already-settled) player.
        if (playerController != null)
        {
            playerController.LockControls(false);
            playerController.enabled = true;
        }
        SetSuspended(false);

        if (menuDollyVcam != null) menuDollyVcam.SetActive(false);
        if (playerFollowVcam != null) playerFollowVcam.SetActive(false);

        StartCoroutine(WelcomeRoutine());
    }

    private IEnumerator WelcomeRoutine()
    {
        yield return new WaitForSeconds(welcomePopupDelay);
        WorldStatePopup.Suppressed = false;
        if (WorldStatePopup.Instance != null) WorldStatePopup.Instance.Show();
    }

    private void SetSuspended(bool suspended)
    {
        if (gameplayToSuspend == null) return;
        foreach (var mb in gameplayToSuspend)
            if (mb != null) mb.enabled = !suspended;
    }

    // Safety: if the menu object is torn down before the welcome lowers the flag, don't leave the
    // popup permanently muted — or the gameplay UIs permanently blocked — for the rest of the session.
    private void OnDisable()
    {
        if (started) WorldStatePopup.Suppressed = false;
        IsTitleSequenceActive = false;
    }
}
