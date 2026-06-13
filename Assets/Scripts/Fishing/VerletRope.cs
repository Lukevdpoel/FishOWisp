using System.Collections.Generic;
using UnityEngine;

// Run after PlayerController.LateUpdate (squash/stretch, airborne tumble) and FishingRodBend.LateUpdate
// finish writing to the rod-tip transform chain. If we sampled rodTip.position before those wrote their
// LateUpdate changes, point 0 of the rope would lag a frame behind the rendered rod tip — visible as a
// disconnect during charge-squash and bounce impacts.
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(LineRenderer))]
public class VerletRope : MonoBehaviour
{
    public struct RopePoint
    {
        public Vector3 currentPosition;
        public Vector3 previousPosition;
        public bool isLocked;
    }

    private Transform rodTip;
    private Transform bobber;

    [Header("Rope Settings")]
    public int segmentCount = 35;
    public int constraintIterations = 20;

    [Header("Physics")]
    public Vector3 gravity = new Vector3(0f, -9.81f, 0f);

    [Header("Slack After Landing")]
    [Tooltip("Baseline slack reduction once the bobber is sitting in the water (0 = original full slack, 1 = fully straight). The nibble pulse is layered on top.")]
    [Range(0f, 1f)] public float landedTightenAmount = 0.25f;

    [Header("Nibble Tighten Pulse")]
    [Tooltip("How much the line tightens on each individual nibble (0 = no change, 1 = fully straight).")]
    [Range(0f, 1f)] public float nibbleTightenAmount = 0.6f;
    [Tooltip("Seconds for a nibble's tighten pulse to decay back to baseline.")]
    public float nibbleTightenDecay = 0.4f;

    [Header("Tug Tighten Pulse")]
    [Tooltip("How much the line straightens on each lure tug / yank (0 = no change, 1 = fully straight).")]
    [Range(0f, 1f)] public float tugTightenAmount = 0.85f;
    [Tooltip("Seconds for a tug's straighten pulse to slacken back to baseline.")]
    public float tugTightenDecay = 0.35f;

    [Header("Lure Reel Tension")]
    [Tooltip("How taut the line is held while the player cranks the reel on a lure (0 = no change, 1 = fully straight).")]
    [Range(0f, 1f)] public float reelTightenAmount = 0.9f;
    [Tooltip("Seconds for the line to pull taut once cranking starts.")]
    public float reelTightenAttack = 0.12f;
    [Tooltip("Seconds for the line to slacken back once cranking stops.")]
    public float reelTightenRelease = 0.4f;

    [Header("Fight Jump Slack")]
    [Tooltip("How slack the taut fight line goes while the hooked fish is airborne (0 = stays dead straight, 1 = fully simulated rope).")]
    [Range(0f, 1f)] public float jumpSlackAmount = 0.85f;
    [Tooltip("Seconds for the line to go slack when the fish leaves the water.")]
    public float jumpSlackAttack = 0.08f;
    [Tooltip("Seconds for the line to pull straight again after the fish lands.")]
    public float jumpSlackRelease = 0.6f;

    private LineRenderer lineRenderer;
    private List<RopePoint> ropePoints = new List<RopePoint>();
    private float segmentLength;
    private bool isInitialized = false;
    private bool isLineTight = false;
    private bool hasLanded = false;
    private float nibbleTightness = 0f;
    private float tugTightness = 0f;
    private float reelTightness = 0f;
    private bool isLureReelHeld = false;
    private bool isFishAirborne = false;
    private float fightSlack = 0f;
    private Vector3[] positionsCache;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }

    private void OnEnable()
    {
        FishingEvents.OnBobberLandedInWater += HandleBobberLandedInWater;
        FishingEvents.OnFishNibble += HandleFishNibble;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnFishFightBegin += HandleFishFightBegin;
        FishingEvents.OnFishFightEnd += HandleFishFightEnd;
        FishingEvents.OnLureTugged += HandleLureTugged;
        FishingEvents.OnLureReelChanged += HandleLureReelChanged;
        FishingEvents.OnHookedFishJumpChanged += HandleHookedFishJumpChanged;
    }

    private void OnDisable()
    {
        FishingEvents.OnBobberLandedInWater -= HandleBobberLandedInWater;
        FishingEvents.OnFishNibble -= HandleFishNibble;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnFishFightBegin -= HandleFishFightBegin;
        FishingEvents.OnFishFightEnd -= HandleFishFightEnd;
        FishingEvents.OnLureTugged -= HandleLureTugged;
        FishingEvents.OnLureReelChanged -= HandleLureReelChanged;
        FishingEvents.OnHookedFishJumpChanged -= HandleHookedFishJumpChanged;
    }

    private void HandleHookedFishJumpChanged(bool airborne)
    {
        isFishAirborne = airborne;
    }

    private void HandleFishNibble(BobberController b)
    {
        nibbleTightness = nibbleTightenAmount;
    }

    private void HandleLureTugged()
    {
        tugTightness = tugTightenAmount;
    }

    private void HandleLureReelChanged(bool held)
    {
        isLureReelHeld = held;
    }

    private void HandleFishBite(BobberController b)
    {
        isLineTight = true;
        nibbleTightness = 0f;
    }

    private void HandleBobberLandedInWater(BobberController landed)
    {
        if (!isInitialized) return;
        hasLanded = true;
    }

    private void HandleFishFightBegin(FishPreset fish)
    {
        isLineTight = true;
    }

    private void HandleFishFightEnd(bool success)
    {
        isLineTight = false;
        isFishAirborne = false;
    }

    public void SetupRope(Transform rodTip, Transform bobber)
    {
        this.rodTip = rodTip;
        this.bobber = bobber;

        ropePoints.Clear();

        for (int i = 0; i <= segmentCount; i++)
        {
            ropePoints.Add(new RopePoint
            {
                currentPosition = rodTip.position,
                previousPosition = rodTip.position,
                isLocked = (i == 0)
            });
        }

        positionsCache = new Vector3[ropePoints.Count];
        isInitialized = true;
        hasLanded = false;

        // A re-setup is a fresh line (new cast or the bobber parked back on the rod) — clear
        // all transient tension state. Without this, a fight that ends by parking the bobber
        // (lost fight, missed hook) leaves isLineTight stuck on and the dangling rope renders
        // as a dead-straight 2-point line.
        isLineTight = false;
        nibbleTightness = 0f;
        tugTightness = 0f;
        reelTightness = 0f;
        isFishAirborne = false;
        fightSlack = 0f;
    }

    public void DeactivateRope()
    {
        isInitialized = false;
        isLineTight = false;
        hasLanded = false;
        nibbleTightness = 0f;
        tugTightness = 0f;
        reelTightness = 0f;
        isLureReelHeld = false;
        isFishAirborne = false;
        fightSlack = 0f;
        if (lineRenderer != null)
            lineRenderer.positionCount = 0;
    }

    void LateUpdate()
    {
        if (!isInitialized) return;

        // --- FIXED: Added Safety Check ---
        // If the bobber or rod has been destroyed, stop immediately to prevent MissingReferenceException.
        if (bobber == null || rodTip == null)
        {
            DeactivateRope();
            return;
        }
        // ---------------------------------

        if (nibbleTightness > 0f)
        {
            nibbleTightness = Mathf.Max(0f, nibbleTightness - Time.deltaTime / Mathf.Max(0.001f, nibbleTightenDecay));
        }
        if (tugTightness > 0f)
        {
            tugTightness = Mathf.Max(0f, tugTightness - Time.deltaTime / Mathf.Max(0.001f, tugTightenDecay));
        }

        // Sustained tension while cranking: ramp up fast on attack, ease off on release.
        float reelTarget = isLureReelHeld ? reelTightenAmount : 0f;
        float reelRampTime = reelTarget > reelTightness ? reelTightenAttack : reelTightenRelease;
        reelTightness = Mathf.MoveTowards(
            reelTightness, reelTarget, Time.deltaTime / Mathf.Max(0.001f, reelRampTime));

        // Fight-jump slack: snaps in when the hooked fish leaves the water, eases back out
        // after the splash-down so the line smoothly pulls straight again.
        float slackTarget = isFishAirborne ? jumpSlackAmount : 0f;
        float slackRampTime = slackTarget > fightSlack ? jumpSlackAttack : jumpSlackRelease;
        fightSlack = Mathf.MoveTowards(
            fightSlack, slackTarget, Time.deltaTime / Mathf.Max(0.001f, slackRampTime));

        // Simulate every frame — even while the fight line renders straight — so the rope
        // points keep tracking the endpoints. When jump slack kicks in mid-fight the simulated
        // rope is current instead of frozen at its pre-fight pose.
        Simulate();

        if (isLineTight)
        {
            if (fightSlack <= 0f) DrawTightLine();
            else DrawSimulatedRope(1f - fightSlack);
        }
        else
        {
            DrawSimulatedRope(0f);
        }
    }

    private void Simulate()
    {
        // Safety check is now handled in LateUpdate, so we can access .position safely here
        float currentRopeLength = Vector3.Distance(rodTip.position, bobber.position);
        segmentLength = currentRopeLength / segmentCount;

        float deltaTime = Time.deltaTime;

        for (int i = 0; i < ropePoints.Count; i++)
        {
            RopePoint point = ropePoints[i];
            if (point.isLocked) continue;

            Vector3 velocity = point.currentPosition - point.previousPosition;
            point.previousPosition = point.currentPosition;

            point.currentPosition += velocity + gravity * (deltaTime * deltaTime);
            ropePoints[i] = point;
        }

        for (int i = 0; i < constraintIterations; i++)
        {
            ApplyConstraints();
        }
    }

    private void ApplyConstraints()
    {
        RopePoint firstPoint = ropePoints[0];
        firstPoint.currentPosition = rodTip.position;
        ropePoints[0] = firstPoint;

        RopePoint lastPoint = ropePoints[ropePoints.Count - 1];
        lastPoint.currentPosition = bobber.position;
        ropePoints[ropePoints.Count - 1] = lastPoint;

        for (int i = 0; i < ropePoints.Count - 1; i++)
        {
            RopePoint point1 = ropePoints[i];
            RopePoint point2 = ropePoints[i + 1];

            Vector3 delta = point2.currentPosition - point1.currentPosition;
            float distance = delta.magnitude;

            if (distance == 0) continue;

            float error = distance - segmentLength;
            Vector3 correction = delta.normalized * error;

            if (!point1.isLocked)
                point1.currentPosition += correction * 0.5f;
            if (!point2.isLocked)
                point2.currentPosition -= correction * 0.5f;

            ropePoints[i] = point1;
            ropePoints[i + 1] = point2;
        }
    }

    // minTightness floors the straight-line blend — the fight line during a jump passes
    // (1 - fightSlack) here so the rope stays partially taut and re-tightens smoothly.
    private void DrawSimulatedRope(float minTightness)
    {
        int count = ropePoints.Count;
        lineRenderer.positionCount = count;
        int last = count - 1;

        float baseline = hasLanded ? landedTightenAmount : 0f;
        float effectiveTightness = Mathf.Max(
            Mathf.Max(Mathf.Max(baseline, reelTightness), minTightness),
            Mathf.Max(nibbleTightness, tugTightness));
        bool blend = effectiveTightness > 0f && last > 0;

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = ropePoints[i].currentPosition;
            if (blend)
            {
                float t = (float)i / last;
                Vector3 straight = Vector3.Lerp(rodTip.position, bobber.position, t);
                pos = Vector3.Lerp(pos, straight, effectiveTightness);
            }
            positionsCache[i] = pos;
        }
        lineRenderer.SetPositions(positionsCache);
    }

    private void DrawTightLine()
    {
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, rodTip.position);
        lineRenderer.SetPosition(1, bobber.position);
    }
}