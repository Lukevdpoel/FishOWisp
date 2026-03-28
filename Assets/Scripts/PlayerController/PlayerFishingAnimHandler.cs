using UnityEngine;

public class PlayerFishingAnimHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerController playerController;

    [Header("Fishing Animations")]
    [SerializeField] private string startChargingAnim = "StartCharging";
    [SerializeField] private string throwAnim = "Throw";
    [SerializeField] private string reelInAnim = "ReelIn";
    [SerializeField] private string isFightingAnimBool = "IsFighting";
    [SerializeField] private string isReelingDuringFightAnimBool = "IsReelingDuringFight";

    public bool IsFightingFish { get; private set; }
    public bool IsCasting { get; private set; }
    public bool IsAiming { get; private set; }
    public Transform ActiveBobberTransform { get; private set; }
    public bool IsBountyBoardActive { get; private set; }
    public Transform ActiveBountyBoard { get; private set; }

    private int hashStartCharging;
    private int hashThrow;
    private int hashReelIn;
    private int hashIsFighting;
    private int hashIsReelingDuringFight;

    private void Awake()
    {
        hashStartCharging = Animator.StringToHash(startChargingAnim);
        hashThrow = Animator.StringToHash(throwAnim);
        hashReelIn = Animator.StringToHash(reelInAnim);
        hashIsFighting = Animator.StringToHash(isFightingAnimBool);
        hashIsReelingDuringFight = Animator.StringToHash(isReelingDuringFightAnimBool);

        if (playerController == null) playerController = GetComponent<PlayerController>();
    }

    private void OnEnable()
    {
        FishingEvents.OnStartCharging += PlayStartChargingAnim;
        FishingEvents.OnThrowBobber += PlayThrowAnim;
        FishingEvents.OnHookFishSuccess += PlayReelInAnim;
        FishingEvents.OnFishFightBegin += StartFightingAnimation;
        FishingEvents.OnCancelFishing += StopFightingAnimation;
        FishingEvents.OnFishFightEnd += OnFishFightEnd;
        FishingEvents.OnStartReeling += HandleSuccessfulCatchAnimation;
        FishingEvents.OnStartCharging += OnCastStart;
        FishingEvents.OnCancelCharging += OnCastEnd;
        FishingEvents.OnCancelFishing += OnCastEnd;
        FishingEvents.OnThrowBobber += OnThrowBobber;
        FishingEvents.OnStartReelingDuringFight += StartReelingDuringFightAnim;
        FishingEvents.OnStopReelingDuringFight += StopReelingDuringFightAnim;
        FishingEvents.OnBobberLandedInWater += OnBobberLanded;
        FishingEvents.OnStartAiming += OnStartAiming;
        FishingEvents.OnStopAiming += OnStopAiming;

        BountyBoard.OnBountyBoardStateChange += HandleBountyBoard;
        DialogueManager.OnDialogueStateChange += OnDialogueStateChanged;
    }

    private void OnDisable()
    {
        FishingEvents.OnStartCharging -= PlayStartChargingAnim;
        FishingEvents.OnThrowBobber -= PlayThrowAnim;
        FishingEvents.OnHookFishSuccess -= PlayReelInAnim;
        FishingEvents.OnFishFightBegin -= StartFightingAnimation;
        FishingEvents.OnCancelFishing -= StopFightingAnimation;
        FishingEvents.OnFishFightEnd -= OnFishFightEnd;
        FishingEvents.OnStartReeling -= HandleSuccessfulCatchAnimation;
        FishingEvents.OnStartCharging -= OnCastStart;
        FishingEvents.OnCancelCharging -= OnCastEnd;
        FishingEvents.OnCancelFishing -= OnCastEnd;
        FishingEvents.OnThrowBobber -= OnThrowBobber;
        FishingEvents.OnStartReelingDuringFight -= StartReelingDuringFightAnim;
        FishingEvents.OnStopReelingDuringFight -= StopReelingDuringFightAnim;
        FishingEvents.OnBobberLandedInWater -= OnBobberLanded;
        FishingEvents.OnStartAiming -= OnStartAiming;
        FishingEvents.OnStopAiming -= OnStopAiming;

        BountyBoard.OnBountyBoardStateChange -= HandleBountyBoard;
        DialogueManager.OnDialogueStateChange -= OnDialogueStateChanged;
    }

    private void OnCastStart() => IsCasting = true;
    private void OnCastEnd() { IsCasting = false; IsFightingFish = false; }
    private void OnStartAiming() => IsAiming = true;
    private void OnStopAiming() => IsAiming = false;
    private void OnThrowBobber(Vector3 direction, float force) => IsCasting = false;
    private void OnBobberLanded(BobberController bobber) { ActiveBobberTransform = bobber.transform; }
    private void OnFishFightEnd(bool success) { IsFightingFish = false; StopFightingAnimation(); }

    private void PlayStartChargingAnim() { if (animator) animator.SetTrigger(hashStartCharging); }
    private void PlayThrowAnim(Vector3 direction, float force) { if (animator) animator.SetTrigger(hashThrow); }
    private void PlayReelInAnim() { if (animator) animator.SetTrigger(hashReelIn); }
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
