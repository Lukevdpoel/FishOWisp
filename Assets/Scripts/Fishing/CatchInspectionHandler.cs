using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The Twilight-Princess-style catch showcase: the caught fish is never re-instantiated — the
// silhouette fish already on the line is hoisted out of the water by the reel-in arc and left
// HANGING nose-up below the rod tip while the catch camera swings around to the player's front.
// Mid-transition the silhouette material is swapped for the prefab's real colors (the hidden
// switch), and the fish's name + description fade in. Confirm hands the fish to the inventory
// and lets FishingRodController end the line's catch-hold, which parks the bobber and disposes
// of the hanging visual in one existing path (BobberController.ResetForCast).
public class CatchInspectionHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private PlayerController playerController;

    [Header("Inspection Settings")]
    [SerializeField] private float turnToCameraSpeed = 10f;
    [Tooltip("Degrees per second the hanging fish spins around the line, showing off every side.")]
    [SerializeField] private float fishRotationSpeed = 45f;
    [SerializeField] private float minInspectionTime = 0.5f;

    [Header("Hanging Catch")]
    [Tooltip("How far below the rod tip the line's end (the fish's mouth) hangs during the showcase.")]
    [SerializeField] private float hangLineLength = 0.5f;
    [Tooltip("Seconds into the inspection before the fish's silhouette swaps to its real materials. " +
             "Timed to land mid camera transition so the switch is hidden by the move.")]
    [SerializeField] private float materialRevealDelay = 0.45f;

    [Header("Animation")]
    [SerializeField] private string showCatchAnimBool = "ShowCatch";

    [Header("Catch Info Text")]
    [Tooltip("Optional: TMP text for the caught fish's name + size. Left empty, a plain one is " +
             "created at runtime so it works with no wiring — assign your own here later to restyle.")]
    [SerializeField] private TMP_Text fishNameText;
    [Tooltip("Optional: TMP text for the species' encyclopedia description. Runtime fallback as above.")]
    [SerializeField] private TMP_Text fishDescriptionText;
    [Tooltip("Optional background image sitting BEHIND the name + description (e.g. your text banner). " +
             "Fades in and out together with those labels. Author + position it yourself; leave empty " +
             "for no backdrop.")]
    [SerializeField] private Graphic catchInfoBackdrop;

    // NOTE: the "you learned this fish's bait preferences!" message is intentionally NOT shown here.
    // Bait is learned by zoom-researching a fish (FishResearchScanner), which pops that banner once
    // per species at the moment of the reveal. Catching only shows the fish's name + description.

    [Tooltip("Seconds the name/description texts take to fade in and to fade out.")]
    [SerializeField] private float bannerFadeTime = 0.4f;

    public bool IsInspecting { get; private set; }

    // The hang spin lives on the HookedFishController; the rod passes this through when the
    // catch reel starts, so the inspector tunable here keeps working.
    public float FishSpinSpeed => fishRotationSpeed;

    private CaughtFish caughtFish;
    private float inspectionStartTime;
    private int hashShowCatch;

    // The line rig the caught fish hangs from: the (invisible) bobber the fish is attached to,
    // held below the rod tip every frame so the hang tracks the ShowCatch pose and the player's
    // turn toward the camera.
    private BobberController hangBobber;
    private Transform hangRodTip;
    private HookedFishController hangingFish;
    private bool hasRevealedColors;

    private void Awake()
    {
        hashShowCatch = Animator.StringToHash(showCatchAnimBool);
        if (playerController == null) playerController = GetComponent<PlayerController>();
        // Start every showcase element hidden; they fade in during inspection. All are optional —
        // wire the ones you author (name / description text + their backdrop).
        if (fishNameText != null) fishNameText.alpha = 0f;
        if (fishDescriptionText != null) fishDescriptionText.alpha = 0f;
        SetGraphicAlpha(catchInfoBackdrop, 0f);
    }

    // Called by FishingRodController when the catch reel-in starts, before BeginInspection:
    // the arc needs the hang position as its moving target, and the hooked fish needs its hang
    // pose kicked off so it swings nose-up during the hoist itself.
    public void ConfigureHang(BobberController bobber, Transform rodTip)
    {
        hangBobber = bobber;
        hangRodTip = rodTip;
        hangingFish = bobber != null && bobber.ActiveFishModel != null
            ? bobber.ActiveFishModel.GetComponent<HookedFishController>()
            : null;
        hangingFish?.BeginHang(fishRotationSpeed);

        // The fight is over — cut the struggle/tension loop now. The bobber's own audio state
        // machine is dormant while its component is disabled for the reel + showcase, and the
        // ResetForCast that used to silence it is deferred until the showcase ends.
        bobber?.StopFightAudioLoops();
    }

    // Where the line's end (the fish's mouth) hangs: straight below the rod tip, tracked live so
    // the ShowCatch animation and the player's turn to camera carry the fish with them.
    public Vector3 GetHangPosition()
    {
        if (hangRodTip != null) return hangRodTip.position + Vector3.down * hangLineLength;
        return transform.position + Vector3.up * 1.6f; // no rod tip wired — hold it at chest height
    }

    public void BeginInspection(CaughtFish fish, Transform playerModel)
    {
        IsInspecting = true;
        caughtFish = fish;
        inspectionStartTime = Time.time;
        hasRevealedColors = false;
        FishingEvents.OnCatchInspectionStarted?.Invoke();

        if (fish != null && fish.preset != null)
        {
            if (fishNameText != null) fishNameText.text = fish.GetDisplayName();
            if (fishDescriptionText != null) fishDescriptionText.text = fish.preset.description;
        }

        if (playerController != null)
        {
            playerController.areControlsLocked = true;
            playerController.SetCatchCamera(true);
        }

        if (playerAnimator != null)
        {
            playerAnimator.SetBool(hashShowCatch, true);
        }
    }

    public void UpdateInspection(Transform playerModel)
    {
        if (!IsInspecting) return;

        if (playerModel != null)
        {
            Vector3 camForward = Camera.main.transform.forward;
            camForward.y = 0;
            if (camForward != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(-camForward);
                playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetRotation, Time.deltaTime * turnToCameraSpeed);
            }
        }

        // Keep the (invisible, kinematic) bobber pinned below the rod tip — the hanging fish and
        // the verlet line both follow it, so the whole rig rides the ShowCatch pose.
        if (hangBobber != null)
        {
            hangBobber.transform.position = GetHangPosition();
        }

        // The hidden switch: swap silhouette for real colors once the camera transition is
        // underway, so the change is masked by the move instead of reading as a pop.
        if (!hasRevealedColors && Time.time >= inspectionStartTime + materialRevealDelay)
        {
            hasRevealedColors = true;
            hangingFish?.RevealTrueColors();
        }
    }

    // Fade the showcase texts toward shown (while inspecting) or hidden (once inspection ends).
    // Runs every frame — not only during UpdateInspection, which the rod stops calling the moment
    // the state leaves InspectingCatch — so the texts fade out cleanly instead of freezing at
    // full alpha. Realtime so it survives any time pause.
    private void Update()
    {
        FadeLabel(fishNameText, IsInspecting);
        FadeLabel(fishDescriptionText, IsInspecting);
        FadeGraphic(catchInfoBackdrop, IsInspecting);
    }

    private void FadeLabel(TMP_Text label, bool shown)
    {
        if (label == null) return;
        float target = shown ? 1f : 0f;
        if (Mathf.Approximately(label.alpha, target)) return;
        label.alpha = Mathf.MoveTowards(
            label.alpha, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, bannerFadeTime));
    }

    // Same easing as the labels, but driven on a wired backdrop Image/Graphic's own colour alpha so
    // the background you author fades in lockstep with the text sitting on top of it.
    private void FadeGraphic(Graphic g, bool shown)
    {
        if (g == null) return;
        float target = shown ? 1f : 0f;
        if (Mathf.Approximately(g.color.a, target)) return;
        SetGraphicAlpha(g, Mathf.MoveTowards(
            g.color.a, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, bannerFadeTime)));
    }

    private static void SetGraphicAlpha(Graphic g, float a)
    {
        if (g == null) return;
        Color c = g.color;
        c.a = a;
        g.color = c;
    }

    public bool TryFinishInspection(out CaughtFish fish)
    {
        fish = null;
        if (!IsInspecting) return false;
        if (Time.time <= inspectionStartTime + minInspectionTime) return false;

        fish = caughtFish;
        if (fish != null) PlayerInventory.Instance.AddFish(fish);
        Cleanup();
        return true;
    }

    public void ForceCleanup()
    {
        if (!IsInspecting) return;
        Cleanup();
    }

    private void Cleanup()
    {
        if (playerAnimator != null) playerAnimator.SetBool(hashShowCatch, false);

        IsInspecting = false;
        caughtFish = null;
        hangBobber = null;
        hangRodTip = null;
        hangingFish = null;
        FishingEvents.OnCatchInspectionEnded?.Invoke();
    }
}
