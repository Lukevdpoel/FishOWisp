using UnityEngine;

// Procedural rod steering for the whip cast, driving ONE transform: the authored parent that
// holds both arms and the rod. While aiming, the Animator's two-handed rod-hold state (see
// PlayerFishingAnimHandler.holdRodBoolParam) poses the arms under this pivot; every frame AFTER
// the Animator, this rotates the pivot with the gesture pull — back raises the rod overhead,
// forward dips it, sideways sweeps it — and snaps it through a power-scaled forward swing when
// the whip fires.
//
// Return-to-rest rule: whenever the player is NOT actively steering the rod, the pivot eases
// smoothly back to its authored default. Holding a sustained pull counts as active (the rod
// stays wound up as long as the pull is held out); only a relaxed/released gesture — or the end
// of the aim/swing — lets it return. Zero offset = the Animator's pose untouched, so "default"
// is always exactly what the hold state (or locomotion) authored.
public class ProceduralCastArms : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The RodCasting on the player. Empty = found on this object's root at Awake.")]
    [SerializeField] private RodCasting rodCasting;
    [Tooltip("The parent transform holding BOTH arms and the rod. Rotated about its own pivot after the Animator each frame — place the pivot where the rod should hinge (chest/shoulder).")]
    [SerializeField] private Transform armsPivot;
    [Tooltip("Reference frame for the lean axes (its up/right). Empty = RodCasting's playerModel.")]
    [SerializeField] private Transform referenceRoot;

    [Header("Wind-Up Pose (degrees at full pull)")]
    [Tooltip("Pulling BACK raises the rod overhead behind the player, up to this angle.")]
    [SerializeField] private float maxBackPitch = 75f;
    [Tooltip("Pulling FORWARD dips the rod down in front, up to this angle.")]
    [SerializeField] private float maxForwardPitch = 45f;
    [Tooltip("Pulling SIDEWAYS sweeps the rod left/right, up to this angle.")]
    [SerializeField] private float maxSideYaw = 55f;
    [Tooltip("Flip if the rod pitches the wrong way vertically (depends on the pivot's authored orientation — same idea as FishingRodBend.bendDirection). Applies to the wind-up AND the throw swing.")]
    [SerializeField] private float pitchDirection = -1f;
    [Tooltip("How snappily the pivot follows an active pull.")]
    [SerializeField] private float poseLerpSpeed = 14f;

    [Header("Return To Rest")]
    [Tooltip("Pull magnitude (0..1) above which the player counts as actively steering the rod. Below it the pivot eases back to the authored default — a held-out pull stays active forever; only a relaxed gesture returns.")]
    [SerializeField] private float activePullThreshold = 0.05f;
    [Tooltip("How fast the pivot eases home when the gesture is slack (and after the aim/swing ends). Lower = lazier settle.")]
    [SerializeField] private float returnLerpSpeed = 6f;

    [Header("Throw Swing")]
    [Tooltip("Peak forward-swing angle at full power (along the push direction).")]
    [SerializeField] private float swingPitch = 95f;
    [Tooltip("A minimum-power whip still swings this fraction of the full arc.")]
    [Range(0f, 1f)] [SerializeField] private float minSwingScale = 0.5f;
    [SerializeField] private float swingDuration = 0.32f;
    [Tooltip("0..1 over the swing: how far toward the forward pose the rod is. Snap out fast, settle partway; the return-to-rest ease takes it home from wherever the curve ends.")]
    [SerializeField] private AnimationCurve swingCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 6f), new Keyframe(0.25f, 1f, 0f, 0f), new Keyframe(1f, 0.35f, -0.5f, 0f));

    private float currentPitch;
    private float currentYaw;

    // The pivot's authored rest pose. The offset is applied ABSOLUTELY from this base every
    // frame (never onto the pivot's current rotation — that would accumulate and spin forever),
    // so angles at zero are exactly the authored default and the arc is hard-bounded by the
    // max pull/swing angles. Consequence: don't animate armsPivot itself in clips — animate its
    // children; the pivot belongs to this script.
    private Quaternion defaultLocalRotation;
    private bool defaultCaptured;

    private float swingTimer = float.PositiveInfinity;
    private float swingPower;
    private Vector2 swingDir = Vector2.up;
    private float swingStartPitch, swingStartYaw;

    private bool SwingActive => swingTimer < swingDuration;

    private void Awake()
    {
        if (rodCasting == null)
        {
            Transform root = transform.root != null ? transform.root : transform;
            rodCasting = root.GetComponentInChildren<RodCasting>(includeInactive: true);
        }
    }

    private void OnEnable()
    {
        // (Re)capture the rest pose on every enable so inspector tweaks made while disabled
        // become the new default.
        if (armsPivot != null)
        {
            defaultLocalRotation = armsPivot.localRotation;
            defaultCaptured = true;
        }
        if (rodCasting != null) rodCasting.ThrowGesture += HandleThrowGesture;
    }

    private void OnDisable()
    {
        if (rodCasting != null) rodCasting.ThrowGesture -= HandleThrowGesture;
    }

    private void HandleThrowGesture(float power, Vector2 pushDir)
    {
        swingTimer = 0f;
        swingPower = power;
        swingDir = pushDir.sqrMagnitude > 0.001f ? pushDir.normalized : Vector2.up;
        swingStartPitch = currentPitch;
        swingStartYaw = currentYaw;
    }

    private void LateUpdate()
    {
        if (armsPivot == null) return;
        if (!defaultCaptured)
        {
            defaultLocalRotation = armsPivot.localRotation;
            defaultCaptured = true;
        }
        float dt = Time.deltaTime;
        bool aiming = rodCasting != null && rodCasting.IsAiming;

        if (SwingActive)
        {
            swingTimer += dt;
            float k = swingCurve.Evaluate(Mathf.Clamp01(swingTimer / swingDuration));
            float scale = Mathf.Lerp(minSwingScale, 1f, swingPower);
            // The swing travels along the push: a straight-forward whip is all pitch, a
            // sideways whip sweeps yaw through center instead.
            currentPitch = Mathf.LerpUnclamped(swingStartPitch, swingDir.y * swingPitch * scale * pitchDirection, k);
            currentYaw = Mathf.LerpUnclamped(swingStartYaw, swingDir.x * swingPitch * scale, k);
        }
        else
        {
            // Active steering holds the wound-up pose (a sustained pull never relaxes on its
            // own); anything else — slack gesture, aim over, swing finished — eases the pivot
            // smoothly back to the authored default.
            Vector2 pull = aiming ? rodCasting.PullNormalized : Vector2.zero;
            bool active = aiming && pull.magnitude >= activePullThreshold;

            float targetPitch = 0f, targetYaw = 0f;
            float rate = returnLerpSpeed;
            if (active)
            {
                // pull.y < 0 is a backward pull (uses the maxBackPitch amplitude); pitchDirection
                // decides which world way that tilts, since it depends on the pivot's authoring.
                targetPitch = (pull.y < 0f ? pull.y * maxBackPitch : pull.y * maxForwardPitch) * pitchDirection;
                targetYaw = pull.x * maxSideYaw;
                rate = poseLerpSpeed;
            }

            float t = 1f - Mathf.Exp(-dt * rate);
            currentPitch = Mathf.Lerp(currentPitch, targetPitch, t);
            currentYaw = Mathf.Lerp(currentYaw, targetYaw, t);
        }

        // Absolute pose from the authored rest rotation — NEVER relative to the pivot's current
        // rotation, which would accumulate frame over frame into an endless spin. Zero angles
        // therefore land exactly back on the default, and the reachable arc is bounded by the
        // pull/swing maxima.
        armsPivot.localRotation = defaultLocalRotation;
        if (Mathf.Abs(currentPitch) < 0.01f && Mathf.Abs(currentYaw) < 0.01f) return;

        Transform frame = referenceRoot != null ? referenceRoot
                        : rodCasting != null && rodCasting.playerModel != null ? rodCasting.playerModel
                        : transform;
        Quaternion offset = Quaternion.AngleAxis(currentYaw, frame.up)
                          * Quaternion.AngleAxis(currentPitch, frame.right);
        armsPivot.rotation = offset * armsPivot.rotation;
    }
}
