using UnityEngine;

public class FishingRodBend : MonoBehaviour
{
    [Header("Bend Bones (in order along the rod)")]
    [SerializeField] private Transform rodStart;
    [SerializeField] private Transform rodMid;
    [SerializeField] private Transform rodTip;

    [Tooltip("How much of the total bend each bone takes. Compounds along the chain — tip gets the most curve.")]
    [SerializeField] private float startWeight = 0.3f;
    [SerializeField] private float midWeight = 0.6f;
    [SerializeField] private float tipWeight = 1.0f;

    [Tooltip("Flip if the rod bends the wrong way (e.g. upward instead of downward under load).")]
    [SerializeField] private float bendDirection = 1f;

    [Header("Idle Sway")]
    [Tooltip("Constant ambient sway when no fishing activity. Set 0 to disable.")]
    [SerializeField] private float idleAmplitudeDeg = 1.2f;
    [SerializeField] private float idleFrequencyHz = 0.45f;

    [Header("Passive Tension (bobber pulling)")]
    [Tooltip("Distance at which bobber tension produces max bend.")]
    [SerializeField] private float tensionMaxDistance = 6f;
    [SerializeField] private float tensionMaxBendDeg = 6f;

    [Header("Event Bend Amounts (degrees at tip)")]
    [SerializeField] private float nibbleBendDeg = 9f;
    [SerializeField] private float biteImminentBendDeg = 4f;
    [SerializeField] private float biteBendDeg = 22f;
    [SerializeField] private float fightBendDeg = 18f;
    [Tooltip("Extra bend added while the player is actively reeling during the fight.")]
    [SerializeField] private float reelingBoostDeg = 6f;
    [Tooltip("Extra bend added while a struggle phase is active (the fish is fighting back).")]
    [SerializeField] private float struggleBoostDeg = 8f;
    [Tooltip("Extra bend at the rod's full side extreme (|direction| = 1) during the fight. Scales " +
             "with how far the rod is pushed to a side, so cranking it over visibly loads the rod up.")]
    [SerializeField] private float extremeBendDeg = 10f;
    [SerializeField] private float castWhipDeg = -14f;

    [Header("Nibble Envelope")]
    [Tooltip("How long the nibble bend takes to ramp up to peak (seconds). Higher = softer pull.")]
    [SerializeField] private float nibbleAttackTime = 0.18f;
    [Tooltip("How long the bend holds at peak before releasing (seconds).")]
    [SerializeField] private float nibbleHoldTime = 0.05f;
    [Tooltip("How long the bend takes to release back to neutral (seconds). Higher = slower recovery.")]
    [SerializeField] private float nibbleReleaseTime = 0.45f;

    [Header("Response Tuning")]
    [Tooltip("Smooth-damp time for the sustained bend (lower = snappier).")]
    [SerializeField] private float smoothTime = 0.12f;
    [Tooltip("How fast nibble/cast impulses decay (higher = faster).")]
    [SerializeField] private float transientDecay = 7f;

    [Header("Aim Toward Bobber")]
    [Tooltip("If on, the rod bends in the direction of the bobber. If off, it always bends along the rod's local Z (old behavior).")]
    [SerializeField] private bool aimTowardBobber = true;
    [Tooltip("Smoothing time for the bend axis. Higher = more stable but slower to track a moving bobber. Prevents frame-to-frame axis flips.")]
    [SerializeField] private float axisSmoothTime = 0.18f;
    [Tooltip("Below this cross-product magnitude (sin of the angle between rod and bobber-direction), the rod is treated as already-aligned and falls back to the default bend axis. Prevents jitter when the rod is pointing near the bobber.")]
    [SerializeField] private float axisStabilityThreshold = 0.15f;
    [Tooltip("Stop aiming at the bobber once it's reeled within this distance (m) of the rod. Below it the rod " +
             "bends along its default forward plane instead, so a fish reeled right up to the tip can't flip the " +
             "bend axis and make the rod bow backward toward the camera.")]
    [SerializeField] private float minAimDistance = 1.5f;
    [Tooltip("Only aim at the bobber while it's in front of the rod (dot of rod-forward vs. direction-to-bobber " +
             "above this). Once a reeled-in fish draws level with or behind the tip, the rod keeps its forward bend.")]
    [SerializeField] private float aimForwardDotThreshold = 0.0f;

    private enum BendState { None, WaitingForBite, BiteImminent, Bite, Fighting }
    private BendState state = BendState.None;
    private bool isReelingDuringFight;
    private bool isStruggling;
    private float rodSideAmount; // |rod direction|, 0 = centered, 1 = full side extreme

    private BobberController activeBobber;

    private Quaternion startBind, midBind, tipBind;

    private float currentBend;
    private float bendVelocity;
    private float transientBend;
    private float swayPhase;
    private float nibbleTimer = -1f;

    private Vector3 smoothedBendAxis;
    private bool axisInitialized;

    private void Awake()
    {
        if (rodStart != null) startBind = rodStart.localRotation;
        if (rodMid != null) midBind = rodMid.localRotation;
        if (rodTip != null) tipBind = rodTip.localRotation;
        swayPhase = Random.value * Mathf.PI * 2f;
    }

    private void OnEnable()
    {
        FishingEvents.OnBobberLandedInWater += HandleBobberLanded;
        FishingEvents.OnFishNibble += HandleNibble;
        FishingEvents.OnBiteImminent += HandleBiteImminent;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnFishFightBegin += HandleFightBegin;
        FishingEvents.OnFishFightEnd += HandleFightEnd;
        FishingEvents.OnStartReelingDuringFight += HandleReelStart;
        FishingEvents.OnStopReelingDuringFight += HandleReelStop;
        FishingEvents.OnFishStruggleStateChanged += HandleStruggleStateChanged;
        FishingEvents.OnRodDirectionUpdate += HandleRodDirection;
        FishingEvents.OnReelingCompleted += HandleReelingCompleted;
        FishingEvents.OnCancelFishing += HandleCancel;
        FishingEvents.OnThrowBobber += HandleThrow;
    }

    private void OnDisable()
    {
        FishingEvents.OnBobberLandedInWater -= HandleBobberLanded;
        FishingEvents.OnFishNibble -= HandleNibble;
        FishingEvents.OnBiteImminent -= HandleBiteImminent;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnFishFightBegin -= HandleFightBegin;
        FishingEvents.OnFishFightEnd -= HandleFightEnd;
        FishingEvents.OnStartReelingDuringFight -= HandleReelStart;
        FishingEvents.OnStopReelingDuringFight -= HandleReelStop;
        FishingEvents.OnFishStruggleStateChanged -= HandleStruggleStateChanged;
        FishingEvents.OnRodDirectionUpdate -= HandleRodDirection;
        FishingEvents.OnReelingCompleted -= HandleReelingCompleted;
        FishingEvents.OnCancelFishing -= HandleCancel;
        FishingEvents.OnThrowBobber -= HandleThrow;
    }

    private void HandleBobberLanded(BobberController bobber)
    {
        activeBobber = bobber;
        if (state == BendState.None) state = BendState.WaitingForBite;
    }

    private void HandleNibble(BobberController _) => nibbleTimer = 0f;
    private void HandleBiteImminent(BobberController _) => state = BendState.BiteImminent;
    private void HandleFishBite(BobberController _) => state = BendState.Bite;
    private void HandleFightBegin(FishPreset _) => state = BendState.Fighting;
    private void HandleFightEnd(bool _) { isReelingDuringFight = false; isStruggling = false; rodSideAmount = 0f; }
    private void HandleReelStart() => isReelingDuringFight = true;
    private void HandleReelStop() => isReelingDuringFight = false;
    private void HandleStruggleStateChanged(bool struggling) => isStruggling = struggling;
    private void HandleRodDirection(float direction) => rodSideAmount = Mathf.Abs(direction);

    private void HandleReelingCompleted()
    {
        ResetState();
    }

    private void HandleCancel()
    {
        ResetState();
    }

    private void HandleThrow(Vector3 _, float __)
    {
        transientBend += castWhipDeg;
    }

    private void ResetState()
    {
        state = BendState.None;
        isReelingDuringFight = false;
        isStruggling = false;
        rodSideAmount = 0f;
        activeBobber = null;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;

        swayPhase += dt * idleFrequencyHz * Mathf.PI * 2f;
        float idle = Mathf.Sin(swayPhase) * idleAmplitudeDeg;

        float tension = 0f;
        Transform tensionReference = rodTip != null ? rodTip : (rodMid != null ? rodMid : rodStart);
        if (state == BendState.Fighting)
        {
            // Under fight load the rod stays fully bowed no matter how close the fish is reeled — a
            // hooked fish drawn near pulls just as hard, so distance must never relax the bend.
            tension = tensionMaxBendDeg;
        }
        else if (activeBobber != null && tensionMaxDistance > 0.001f && tensionReference != null)
        {
            float dist = Vector3.Distance(activeBobber.transform.position, tensionReference.position);
            tension = Mathf.Clamp01(dist / tensionMaxDistance) * tensionMaxBendDeg;
        }

        float stateBend = 0f;
        switch (state)
        {
            case BendState.WaitingForBite: stateBend = 0f; break;
            case BendState.BiteImminent: stateBend = biteImminentBendDeg; break;
            case BendState.Bite: stateBend = biteBendDeg; break;
            case BendState.Fighting:
                // Reeling and struggling rarely overlap, so these add cleanly; the side-extreme term
                // loads the rod up further the harder it's cranked to either side.
                stateBend = fightBendDeg
                    + (isReelingDuringFight ? reelingBoostDeg : 0f)
                    + (isStruggling ? struggleBoostDeg : 0f)
                    + rodSideAmount * extremeBendDeg;
                break;
        }

        float target = idle + tension + stateBend;
        currentBend = Mathf.SmoothDamp(currentBend, target, ref bendVelocity, smoothTime);

        transientBend = Mathf.Lerp(transientBend, 0f, 1f - Mathf.Exp(-transientDecay * dt));

        float nibbleBend = UpdateNibbleEnvelope(dt);

        float total = (currentBend + transientBend + nibbleBend) * bendDirection;

        Vector3 bendAxisWorld = ComputeBendAxisWorld();

        // The bend (and every stacked boost in `total`) is split across the three segments; deeper
        // bones compound, so the tip normally carries the most visible curve. If a segment is left
        // unassigned — commonly the tip — fold its weight onto the last assigned bone so the rod
        // still bends through its full intended range. Without this the tip's share is silently
        // dropped, making the whole bend (reeling + struggle + side-extreme included) read as weak.
        float effStartWeight = startWeight;
        float effMidWeight = midWeight;
        float effTipWeight = tipWeight;
        if (rodTip == null)
        {
            if (rodMid != null) effMidWeight += tipWeight;
            else if (rodStart != null) effStartWeight += tipWeight;
            effTipWeight = 0f;
        }
        if (rodMid == null && rodStart != null)
        {
            effStartWeight += effMidWeight;
            effMidWeight = 0f;
        }

        ApplyBend(rodStart, startBind, total * effStartWeight, bendAxisWorld);
        ApplyBend(rodMid, midBind, total * effMidWeight, bendAxisWorld);
        ApplyBend(rodTip, tipBind, total * effTipWeight, bendAxisWorld);
    }

    private Vector3 ComputeBendAxisWorld()
    {
        Transform anchor = rodStart != null ? rodStart : (rodMid != null ? rodMid : rodTip);
        if (anchor == null) return Vector3.right;

        // Use the rod's rigid mount (parent of the first bend bone) for the along-rod direction.
        // The bend bones themselves rotate each frame; using one of them creates a feedback loop
        // where the bend axis chases the bone it's rotating.
        Transform alongRef = anchor.parent != null ? anchor.parent : anchor;
        Vector3 fallbackAxis = anchor.TransformDirection(Vector3.forward);

        Vector3 targetAxis = fallbackAxis;
        if (aimTowardBobber && activeBobber != null)
        {
            Vector3 rodAlong = alongRef.right;
            Vector3 toBobber = activeBobber.transform.position - alongRef.position;
            float dist = toBobber.magnitude;
            // Only steer the bend toward the bobber while it's far enough out AND still in front of the
            // rod. A fish reeled right up to (or past) the tip would otherwise flip the aim direction and
            // bow the rod backward toward the camera; inside the threshold we keep the default forward bend.
            if (dist > minAimDistance)
            {
                Vector3 dir = toBobber / dist;
                if (Vector3.Dot(rodAlong, dir) > aimForwardDotThreshold)
                {
                    Vector3 cross = Vector3.Cross(rodAlong, dir);
                    if (cross.magnitude >= axisStabilityThreshold)
                    {
                        targetAxis = cross.normalized;
                    }
                }
            }
        }

        if (!axisInitialized)
        {
            smoothedBendAxis = targetAxis;
            axisInitialized = true;
        }
        else
        {
            float t = axisSmoothTime > 0.0001f ? 1f - Mathf.Exp(-Time.deltaTime / axisSmoothTime) : 1f;
            smoothedBendAxis = Vector3.Slerp(smoothedBendAxis, targetAxis, t).normalized;
        }
        return smoothedBendAxis;
    }

    private float UpdateNibbleEnvelope(float dt)
    {
        if (nibbleTimer < 0f) return 0f;
        nibbleTimer += dt;

        float attack = Mathf.Max(0.0001f, nibbleAttackTime);
        float hold = Mathf.Max(0f, nibbleHoldTime);
        float release = Mathf.Max(0.0001f, nibbleReleaseTime);
        float totalTime = attack + hold + release;

        if (nibbleTimer >= totalTime)
        {
            nibbleTimer = -1f;
            return 0f;
        }

        if (nibbleTimer < attack)
        {
            return Mathf.SmoothStep(0f, nibbleBendDeg, nibbleTimer / attack);
        }
        if (nibbleTimer < attack + hold)
        {
            return nibbleBendDeg;
        }
        float t = (nibbleTimer - attack - hold) / release;
        return Mathf.SmoothStep(nibbleBendDeg, 0f, t);
    }

    private static void ApplyBend(Transform bone, Quaternion bindRot, float angleDeg, Vector3 bendAxisWorld)
    {
        if (bone == null) return;
        Vector3 axisInParentLocal = bone.parent != null
            ? bone.parent.InverseTransformDirection(bendAxisWorld)
            : bendAxisWorld;
        bone.localRotation = Quaternion.AngleAxis(angleDeg, axisInParentLocal) * bindRot;
    }
}
