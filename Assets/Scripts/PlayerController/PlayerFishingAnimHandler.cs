using UnityEngine;

public class PlayerFishingAnimHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerController playerController;

    [Header("Fishing Animations")]
    [Tooltip("Play the authored StartCharging windup and Throw clips on cast. OFF now that the " +
             "whip cast poses the arms/rod procedurally (ProceduralCastArms) — the body stays in " +
             "locomotion/idle underneath. NOTE with this off: if the waiting-for-bite pose is only " +
             "reachable through the Throw state in the Animator, add a Locomotion/AnyState " +
             "transition on IsWaitingForBite so the wait pose still engages after a cast.")]
    [SerializeField] private bool playCastAnimations = false;
    [Tooltip("Animator BOOL set true while the whip cast is being aimed: transition into the " +
             "two-handed rod-hold state on it (and back out on false). ProceduralCastArms then " +
             "rotates the arms' parent pivot on top of that held pose. Leave empty to disable " +
             "until the state exists in the Animator.")]
    [SerializeField] private string holdRodBoolParam = "IsHoldingRod";
    [SerializeField] private string startChargingAnim = "StartCharging";
    [SerializeField] private string throwAnim = "Throw";
    [SerializeField] private string reelInAnim = "ReelIn";
    [SerializeField] private string isFightingAnimBool = "IsFighting";
    [SerializeField] private string isReelingDuringFightAnimBool = "IsReelingDuringFight";
    [SerializeField] private string isWaitingForBiteAnimBool = "IsWaitingForBite";
    [SerializeField] private string rodDirectionAnimFloat = "RodDirection";
    public string attractAnim = "Attract";
    public string biteReactionAnim = "BiteReaction";

    [Header("Charge Cancel")]
    [Tooltip("Base locomotion/idle state the cast animation snaps to when a charge is cancelled " +
             "(let go of RT mid-charge). Must match the default state name in the Animator.")]
    [SerializeField] private string defaultLocomotionState = "Locomotion";
    [Tooltip("Blend time used when a cancelled charge returns to the default pose. Keep tiny for " +
             "an effectively immediate cut.")]
    [SerializeField] private float cancelTransitionDuration = 0.05f;

    public bool IsFightingFish { get; private set; }
    public bool IsCasting { get; private set; }
    public bool IsAiming { get; private set; }
    // True from the start of a reel-in until it completes (the catch arc, auto-reel, etc.). Used to
    // keep the cursor locked through the whole fishing loop, including the post-catch reel arc.
    public bool IsReeling { get; private set; }
    public Transform ActiveBobberTransform { get; private set; }
    public bool IsBountyBoardActive { get; private set; }
    public Transform ActiveBountyBoard { get; private set; }

    private int hashHoldRod;
    private int hashStartCharging;
    private int hashThrow;
    private int hashReelIn;
    private int hashIsFighting;
    private int hashIsReelingDuringFight;
    private int hashIsWaitingForBite;
    private int hashRodDirection;
    private int hashAttract;
    private int hashBiteReaction;
    private int hashLocomotion;

    private void Awake()
    {
        hashLocomotion = Animator.StringToHash(defaultLocomotionState);
        hashHoldRod = string.IsNullOrEmpty(holdRodBoolParam) ? 0 : Animator.StringToHash(holdRodBoolParam);
        hashStartCharging = Animator.StringToHash(startChargingAnim);
        hashThrow = Animator.StringToHash(throwAnim);
        hashReelIn = Animator.StringToHash(reelInAnim);
        hashIsFighting = Animator.StringToHash(isFightingAnimBool);
        hashIsReelingDuringFight = Animator.StringToHash(isReelingDuringFightAnimBool);
        hashIsWaitingForBite = Animator.StringToHash(isWaitingForBiteAnimBool);
        hashRodDirection = Animator.StringToHash(rodDirectionAnimFloat);
        hashAttract = Animator.StringToHash(attractAnim);
        hashBiteReaction = Animator.StringToHash(biteReactionAnim);

        if (playerController == null) playerController = GetComponent<PlayerController>();
    }

    private void OnEnable()
    {
        FishingEvents.OnStartCharging += PlayStartChargingAnim;
        FishingEvents.OnThrowBobber += PlayThrowAnim;
        FishingEvents.OnHookFishSuccess += OnHookSuccess;
        FishingEvents.OnFishFightBegin += StartFightingAnimation;
        FishingEvents.OnCancelFishing += StopFightingAnimation;
        FishingEvents.OnFishFightEnd += OnFishFightEnd;
        FishingEvents.OnStartReeling += HandleSuccessfulCatchAnimation;
        FishingEvents.OnStartReeling += SetReelingTrue;
        FishingEvents.OnReelingCompleted += SetReelingFalse;
        FishingEvents.OnStartCharging += OnCastStart;
        FishingEvents.OnCancelCharging += OnCastEnd;
        FishingEvents.OnCancelFishing += OnCastEnd;
        FishingEvents.OnCancelFishing += OnFishingCanceled;
        FishingEvents.OnThrowBobber += OnThrowBobber;
        FishingEvents.OnStartReelingDuringFight += StartReelingDuringFightAnim;
        FishingEvents.OnStopReelingDuringFight += StopReelingDuringFightAnim;
        FishingEvents.OnBobberLandedInWater += OnBobberLanded;
        FishingEvents.OnStartAiming += OnStartAiming;
        FishingEvents.OnStopAiming += OnStopAiming;
        FishingEvents.OnRodDirectionUpdate += OnRodDirectionUpdate;
        FishingEvents.OnAttractFish += PlayAttractAnim;
        FishingEvents.OnFishBite += PlayBiteReactionAnim;
        FishingEvents.OnChargeCanceled += SnapToDefaultPose;

        BountyBoard.OnBountyBoardStateChange += HandleBountyBoard;
        DialogueManager.OnDialogueStateChange += OnDialogueStateChanged;
    }

    private void OnDisable()
    {
        FishingEvents.OnStartCharging -= PlayStartChargingAnim;
        FishingEvents.OnThrowBobber -= PlayThrowAnim;
        FishingEvents.OnHookFishSuccess -= OnHookSuccess;
        FishingEvents.OnFishFightBegin -= StartFightingAnimation;
        FishingEvents.OnCancelFishing -= StopFightingAnimation;
        FishingEvents.OnFishFightEnd -= OnFishFightEnd;
        FishingEvents.OnStartReeling -= HandleSuccessfulCatchAnimation;
        FishingEvents.OnStartReeling -= SetReelingTrue;
        FishingEvents.OnReelingCompleted -= SetReelingFalse;
        FishingEvents.OnStartCharging -= OnCastStart;
        FishingEvents.OnCancelCharging -= OnCastEnd;
        FishingEvents.OnCancelFishing -= OnCastEnd;
        FishingEvents.OnCancelFishing -= OnFishingCanceled;
        FishingEvents.OnThrowBobber -= OnThrowBobber;
        FishingEvents.OnStartReelingDuringFight -= StartReelingDuringFightAnim;
        FishingEvents.OnStopReelingDuringFight -= StopReelingDuringFightAnim;
        FishingEvents.OnBobberLandedInWater -= OnBobberLanded;
        FishingEvents.OnStartAiming -= OnStartAiming;
        FishingEvents.OnStopAiming -= OnStopAiming;
        FishingEvents.OnRodDirectionUpdate -= OnRodDirectionUpdate;
        FishingEvents.OnAttractFish -= PlayAttractAnim;
        FishingEvents.OnFishBite -= PlayBiteReactionAnim;
        FishingEvents.OnChargeCanceled -= SnapToDefaultPose;

        BountyBoard.OnBountyBoardStateChange -= HandleBountyBoard;
        DialogueManager.OnDialogueStateChange -= OnDialogueStateChanged;
    }

    private void OnCastStart() => IsCasting = true;
    private void OnCastEnd() { IsCasting = false; IsFightingFish = false; }
    private void OnFishingCanceled() { SetWaitingForBite(false); IsReeling = false; SetHoldRod(false); ClearStaleTriggers(); }

    private void SetReelingTrue() => IsReeling = true;
    private void SetReelingFalse() => IsReeling = false;

    // Charge cancelled (let go of RT mid-charge): the StartCharging windup otherwise lingers in
    // its state until the exit-time transition fires, so force the default locomotion pose for an
    // immediate return to idle. Reset the windup/throw triggers first so a latched one can't yank
    // the animator straight back out of locomotion.
    private void SnapToDefaultPose()
    {
        IsCasting = false;
        SetHoldRod(false);
        if (animator == null) return;
        animator.ResetTrigger(hashStartCharging);
        animator.ResetTrigger(hashThrow);
        // With the cast clips disabled the animator never left locomotion — nothing to snap out of.
        if (playCastAnimations) animator.CrossFadeInFixedTime(hashLocomotion, cancelTransitionDuration, 0);
    }

    // Triggers latch forever if no transition consumes them, and a latched ReelIn hijacks the
    // next cast: the Throw state has an instant ReelIn-conditioned exit, so the throw animation
    // gets replaced by the reel animation. Cancel/fail is exactly where one-shots can be left
    // dangling (the fight pose is entered via the IsFighting bool, abandoning pending triggers),
    // so sweep them all here.
    private void ClearStaleTriggers()
    {
        if (animator == null) return;
        animator.ResetTrigger(hashReelIn);
        animator.ResetTrigger(hashAttract);
        animator.ResetTrigger(hashBiteReaction);
    }

    // At hook time the ReelIn trigger can never play: OnFishFightBegin fires in the same frame
    // and the AnyState→fight transition outranks it, so SetTrigger here would only latch and
    // corrupt a later cast (the post-loss "casting plays the reel animation" bug). Only the
    // waiting-for-bite flag needs clearing.
    private void OnHookSuccess() { SetWaitingForBite(false); }
    private void OnStartAiming() => IsAiming = true;
    private void OnStopAiming() => IsAiming = false;
    private void OnThrowBobber(Vector3 direction, float force) { IsCasting = false; SetWaitingForBite(true); }
    private void OnBobberLanded(BobberController bobber) { ActiveBobberTransform = bobber.transform; }
    private void OnFishFightEnd(bool success) { IsFightingFish = false; StopFightingAnimation(); }

    private void SetWaitingForBite(bool value) { if (animator) animator.SetBool(hashIsWaitingForBite, value); }

    // Hold-rod state gate. Checked once against the animator's real parameter list so a missing
    // (not-yet-authored) bool disables the feature silently instead of spamming warnings.
    private bool holdRodChecked;
    private bool holdRodAvailable;

    private void SetHoldRod(bool holding)
    {
        if (animator == null || hashHoldRod == 0) return;
        if (!holdRodChecked)
        {
            holdRodChecked = true;
            foreach (var p in animator.parameters)
                if (p.nameHash == hashHoldRod && p.type == AnimatorControllerParameterType.Bool)
                { holdRodAvailable = true; break; }
        }
        if (holdRodAvailable) animator.SetBool(hashHoldRod, holding);
    }

    private void PlayStartChargingAnim()
    {
        SetHoldRod(true);
        if (animator && playCastAnimations) animator.SetTrigger(hashStartCharging);
    }
    // ResetTrigger(ReelIn) first: the Throw state exits instantly into Reel when ReelIn is set,
    // so a stale ReelIn would swallow the entire throw animation. The stale-trigger sweep runs
    // even when cast clips are disabled — a latched ReelIn must never survive into the next cast.
    private void PlayThrowAnim(Vector3 direction, float force) { SetHoldRod(false); if (animator) { animator.ResetTrigger(hashReelIn); if (playCastAnimations) animator.SetTrigger(hashThrow); } }
    private void PlayReelInAnim() { SetWaitingForBite(false); if (animator) animator.SetTrigger(hashReelIn); }
    private void PlayAttractAnim() { if (animator) animator.SetTrigger(hashAttract); }
    private void PlayBiteReactionAnim(BobberController b) { SetWaitingForBite(false); if (animator) animator.SetTrigger(hashBiteReaction); }
    private void StartReelingDuringFightAnim() { if (animator) animator.SetBool(hashIsReelingDuringFight, true); }
    private void StopReelingDuringFightAnim() { if (animator) animator.SetBool(hashIsReelingDuringFight, false); }

    private void StartFightingAnimation(FishPreset fish)
    {
        IsFightingFish = true;
        if (animator) animator.SetBool(hashIsFighting, true);
    }

    private void StopFightingAnimation()
    {
        IsFightingFish = false;
        if (animator) animator.SetBool(hashIsFighting, false);
    }

    private void OnRodDirectionUpdate(float direction)
    {
        if (animator) animator.SetFloat(hashRodDirection, direction);
    }

    private void HandleSuccessfulCatchAnimation() { StopFightingAnimation(); PlayReelInAnim(); }

    private void OnDialogueStateChanged(bool isOpen)
    {
        if (playerController != null) playerController.LockControls(isOpen);
    }

    private void HandleBountyBoard(bool isOpen, Transform boardTransform)
    {
        IsBountyBoardActive = isOpen;
        ActiveBountyBoard = boardTransform;
        if (playerController != null) playerController.LockControls(isOpen);
    }
}
