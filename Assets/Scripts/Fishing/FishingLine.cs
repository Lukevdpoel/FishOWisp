using UnityEngine;

[RequireComponent(typeof(VerletRope))]
public class FishingLine : MonoBehaviour
{
    [Header("References")]
    public Transform rodTip;

    [Tooltip("A stable point (not on an animated bone) used as the cast origin. Falls back to rodTip if unset.")]
    public Transform stableThrowOrigin;

    [Tooltip("Local-space offset from rodTip where the joint anchor sits. Default zero puts the anchor exactly at rodTip; gravity then dangles the bobber by dangleLength below. Use (0, -0.1, 0) etc. to push the anchor below the rod tip.")]
    public Vector3 anchorLocalOffset = Vector3.zero;

    [Header("Bobber")]
    [Tooltip("The bobber the player currently has equipped. A persistent instance is spawned from this and tethered to a ConfigurableJoint anchor that follows rodTip.")]
    public BobberItem currentBobber;

    [Header("Launch Angle")]
    [Tooltip("Upward launch angle in degrees. Higher values produce a taller arc.")]
    public float launchAngle = 20f;

    [Header("Line Tether")]
    [Tooltip("Hard cap on bobber distance from the rod tip while dangling. The bobber cannot exceed this distance — no spring sag, no overshoot.")]
    public float dangleLength = 0.4f;

    [Tooltip("Hard cap on bobber distance while cast — the line running taut. Bobber flies freely below this.")]
    public float castMaxDistance = 30f;

    [Header("Dangle Stability")]
    [Tooltip("Linear damping applied to the bobber while dangling. Higher = less pendulum swing when walking, but more visible trail-behind. Reset to the prefab's damping on launch.")]
    public float dangleLinearDamping = 4f;

    [Tooltip("Angular damping while dangling. Kills residual spin from cast/reel so the bobber settles still on the rod.")]
    public float dangleAngularDamping = 5f;

    [Tooltip("Strength of the corrective torque that rotates the bobber so its LineAttachPoint faces the anchor while dangling. 0 = no orientation (tumbles freely). Higher = snappier alignment to upright.")]
    public float dangleOrientStrength = 30f;

    [Header("Diagnostics")]
    [Tooltip("Enable to print rodTip / anchor / bobber positions each FixedUpdate so we can see where things actually are at runtime.")]
    public bool logDiagnostics = false;
    [Tooltip("Print every N-th FixedUpdate instead of every one (50/sec). 10 = roughly 5 times per second.")]
    public int logEveryNFixed = 10;


    private VerletRope verletRope;
    private LineRenderer lineRenderer;
    private BobberController activeBobber;
    private Rigidbody activeBobberRb;
    private Rigidbody anchorRigidbody;
    private Transform anchorTransform;
    private ConfigurableJoint activeJoint;
    private Collider[] bobberColliders;
    private bool isDangling;

    void Awake()
    {
        verletRope = GetComponent<VerletRope>();
        lineRenderer = GetComponent<LineRenderer>();
        verletRope.DeactivateRope();
        EnsureAnchorRigidbody();
    }

    private void EnsureAnchorRigidbody()
    {
        if (rodTip == null)
        {
            Debug.LogError("[FishingLine] rodTip is not assigned — cannot create joint anchor.");
            return;
        }

        if (anchorTransform != null) return;

        // Top-level GameObject — NO parenting. A kinematic Rigidbody parented to a moving transform
        // pins its own world position and parent updates stop propagating to it. The reliable way
        // to move a kinematic body is MovePosition each FixedUpdate, which is what we do below.
        GameObject anchorGO = new GameObject("__FishingLineAnchor");
        anchorTransform = anchorGO.transform;
        anchorTransform.position = rodTip.TransformPoint(anchorLocalOffset);
        anchorTransform.rotation = rodTip.rotation;

        anchorRigidbody = anchorGO.AddComponent<Rigidbody>();
        anchorRigidbody.isKinematic = true;
        anchorRigidbody.useGravity = false;
        anchorRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void Start()
    {
        SpawnPersistentBobber(currentBobber);
    }

    private int diagnosticTick;

    private void FixedUpdate()
    {
        // Drive the kinematic anchor body to follow rodTip + anchorLocalOffset each physics step.
        // Top-level kinematic body + MovePosition is the only reliable way to make it track an
        // animated reference; parenting alone doesn't work for kinematic Rigidbodies.
        if (anchorRigidbody != null && rodTip != null)
        {
            Vector3 target = rodTip.TransformPoint(anchorLocalOffset);
            anchorRigidbody.MovePosition(target);
            anchorRigidbody.MoveRotation(rodTip.rotation);
        }

        ApplyDangleOrientation();

        if (!logDiagnostics) return;
        diagnosticTick++;
        if (diagnosticTick % Mathf.Max(1, logEveryNFixed) != 0) return;

        string rodTipStr = rodTip != null ? rodTip.position.ToString("F3") : "<null>";
        string anchorTStr = anchorTransform != null ? anchorTransform.position.ToString("F3") : "<null>";
        string anchorLocalStr = anchorTransform != null ? anchorTransform.localPosition.ToString("F3") : "<null>";
        string anchorParentStr = (anchorTransform != null && anchorTransform.parent != null) ? anchorTransform.parent.name : "<null>";
        string anchorRbStr = anchorRigidbody != null ? anchorRigidbody.position.ToString("F3") : "<null>";
        string bobberStr = activeBobber != null ? activeBobber.transform.position.ToString("F3") : "<null>";

        float rodToAnchor = (rodTip != null && anchorTransform != null) ? Vector3.Distance(rodTip.position, anchorTransform.position) : -1f;
        float anchorToBobber = (anchorTransform != null && activeBobber != null) ? Vector3.Distance(anchorTransform.position, activeBobber.transform.position) : -1f;

        Debug.Log(
            $"[FishingLine DIAG] rodTip={rodTipStr} | anchor(tf)={anchorTStr} local={anchorLocalStr} parent={anchorParentStr} | anchor(rb)={anchorRbStr} | bobber={bobberStr} | rodTip→anchor={rodToAnchor:F3} anchor→bobber={anchorToBobber:F3}");
    }

    private void OnEnable()
    {
        FishingEvents.OnThrowBobber += LaunchBobber;
        FishingEvents.OnCancelFishing += ReturnBobberToRod;
        FishingEvents.OnReelingCompleted += ReturnBobberToRod;
        PlayerController.BallJumpVisibilityChanged += OnBallJumpVisibilityChanged;
    }

    private void OnDisable()
    {
        FishingEvents.OnThrowBobber -= LaunchBobber;
        FishingEvents.OnCancelFishing -= ReturnBobberToRod;
        FishingEvents.OnReelingCompleted -= ReturnBobberToRod;
        PlayerController.BallJumpVisibilityChanged -= OnBallJumpVisibilityChanged;
    }

    // Hide the line + spawned bobber while the player morphs into / flies as the ball, and show
    // them again when the morph-out begins on the bounce (PlayerController decides the timing).
    // Rendering is toggled; physics/joints stay live so the bobber is right back on the rod. Fishing
    // can't begin mid-jump (needs grounded, unlocked controls), so the only bobber here is the dangling one.
    private void OnBallJumpVisibilityChanged(bool hidden)
    {
        if (lineRenderer != null) lineRenderer.enabled = !hidden;
        if (activeBobber != null)
            foreach (var r in activeBobber.GetComponentsInChildren<Renderer>(true))
                r.enabled = !hidden;
    }

    public void SetBobber(BobberItem item)
    {
        if (item == currentBobber && activeBobber != null) return;
        currentBobber = item;
        SpawnPersistentBobber(item);
    }

    private void SpawnPersistentBobber(BobberItem item)
    {
        if (activeBobber != null)
        {
            Destroy(activeBobber.gameObject);
            activeBobber = null;
            activeBobberRb = null;
            activeJoint = null;
        }

        if (item == null || item.bobberPrefab == null)
        {
            Debug.LogWarning("[FishingLine] No BobberItem with bobberPrefab assigned — no bobber will be shown on the rod.");
            return;
        }

        if (anchorTransform == null) EnsureAnchorRigidbody();
        if (anchorTransform == null) return;

        // Spawn at the dangle rest position (dangleLength straight below the anchor) instead of at
        // the anchor itself, so the bobber doesn't visibly drop into place on the first frame.
        // autoConfigureConnectedAnchor=false below pins the joint anchors explicitly, so the spawn
        // position doesn't affect joint setup or accumulate drift when swapping bobber types.
        // Spawn already upright (LineAttachPoint up so the lure hangs vertically, line attachment
        // toward the rod) by computing the rest rotation from the PREFAB before instantiating. The
        // attach-point geometry is identical on prefab and instance, so this is exact. Instantiating
        // at the correct rotation — rather than at identity (the flat in-water pose) and rotating
        // afterward — is what kills the one-frame flat flicker: the prefab uses RigidbodyInterpolation,
        // so an identity spawn records the flat pose in the interpolation buffer and the first rendered
        // frame blends out of it. Born upright, the buffer's first pose is already correct.
        BobberController prefabBobber = item.bobberPrefab.GetComponent<BobberController>();
        Quaternion spawnRotation = prefabBobber != null ? DangleRestRotation(prefabBobber) : Quaternion.identity;

        GameObject instance = Instantiate(item.bobberPrefab, DangleRestPosition(), spawnRotation);
        activeBobber = instance.GetComponent<BobberController>();
        if (activeBobber == null)
        {
            Debug.LogError("[FishingLine] BobberItem.bobberPrefab is missing the BobberController script!");
            Destroy(instance);
            return;
        }

        Rigidbody rb = activeBobber.GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("[FishingLine] BobberItem.bobberPrefab is missing a Rigidbody!");
            Destroy(instance);
            activeBobber = null;
            return;
        }
        activeBobberRb = rb;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        ConfigureJoint(instance, rb);

        bobberColliders = instance.GetComponentsInChildren<Collider>(includeInactive: true);

        // Park visually — set up the rope, clear leftover state, and disable the colliders so the
        // dangling bobber doesn't snag on terrain/walls as the player walks around.
        activeBobber.ResetForCast();
        ApplyDangleDamping(rb);
        SetBobberCollidersEnabled(false);
        isDangling = true;
        if (rodTip != null) verletRope.SetupRope(rodTip, activeBobber.LineAttachPoint);
    }

    private void ApplyDangleDamping(Rigidbody rb)
    {
        if (rb == null) return;
        rb.linearDamping = dangleLinearDamping;
        rb.angularDamping = dangleAngularDamping;
    }

    private void ApplyDangleOrientation()
    {
        if (!isDangling || activeBobber == null || anchorRigidbody == null) return;
        if (dangleOrientStrength <= 0f) return;

        Rigidbody bobberRb = activeBobberRb;
        if (bobberRb == null || bobberRb.isKinematic) return;

        Transform attach = activeBobber.LineAttachPoint;
        if (attach == null || attach == activeBobber.transform) return;

        // Current world-space direction from bobber centre to its line-attach point.
        Vector3 currentDir = attach.position - activeBobber.transform.position;
        if (currentDir.sqrMagnitude < 0.0001f) return;
        currentDir.Normalize();

        // Desired direction: from bobber toward anchor. With this aligned to currentDir, the
        // attach point sits between the bobber centre and the anchor — the natural rest pose.
        Vector3 desiredDir = anchorRigidbody.position - activeBobber.transform.position;
        if (desiredDir.sqrMagnitude < 0.0001f) return;
        desiredDir.Normalize();

        // Rotation that maps current → desired. Convert to torque (axis × signed angle in rad).
        Quaternion delta = Quaternion.FromToRotation(currentDir, desiredDir);
        delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (axis.sqrMagnitude < 0.0001f) return;
        if (angleDeg > 180f) angleDeg -= 360f;

        Vector3 torque = axis.normalized * (angleDeg * Mathf.Deg2Rad * dangleOrientStrength);
        bobberRb.AddTorque(torque, ForceMode.Acceleration);
    }

    private void SetBobberCollidersEnabled(bool enabled)
    {
        if (bobberColliders == null) return;
        for (int i = 0; i < bobberColliders.Length; i++)
        {
            if (bobberColliders[i] != null) bobberColliders[i].enabled = enabled;
        }
    }

    private void ConfigureJoint(GameObject bobberInstance, Rigidbody bobberRb)
    {
        activeJoint = bobberInstance.AddComponent<ConfigurableJoint>();
        activeJoint.connectedBody = anchorRigidbody;

        activeJoint.autoConfigureConnectedAnchor = false;
        // Anchor at the bobber's local origin (≈ centre of mass). Offsetting to LineAttachPoint
        // would produce torque from constraint impulses → endless spin. The visual rope still
        // renders to LineAttachPoint; that's a separate concern.
        activeJoint.anchor = Vector3.zero;
        activeJoint.connectedAnchor = Vector3.zero;

        activeJoint.xMotion = ConfigurableJointMotion.Limited;
        activeJoint.yMotion = ConfigurableJointMotion.Limited;
        activeJoint.zMotion = ConfigurableJointMotion.Limited;

        activeJoint.angularXMotion = ConfigurableJointMotion.Free;
        activeJoint.angularYMotion = ConfigurableJointMotion.Free;
        activeJoint.angularZMotion = ConfigurableJointMotion.Free;

        ApplyLinearLimit(dangleLength);

        activeJoint.enableCollision = false;
    }

    // Where the dangling bobber settles: its joint limits the centre to dangleLength from the
    // anchor, and gravity pulls it straight down. Spawn/park here so it starts at rest instead
    // of dropping into place. Doesn't have to be exact — the joint settles any small residual.
    private Vector3 DangleRestPosition()
    {
        Vector3 anchorPos = anchorTransform != null ? anchorTransform.position : rodTip.TransformPoint(anchorLocalOffset);
        return anchorPos + Vector3.down * dangleLength;
    }

    // The orientation a dangling bobber settles into: its LineAttachPoint axis pointing up at the
    // anchor, plus a locked heading. Bobbers lie flat in the water but hang vertically off the rod,
    // so spawning/parking with this avoids a visible rotate-into-place. The heading lock matters for
    // elongated lures: aligning only the attach axis leaves the spin about the vertical line free,
    // so the lure settled at a random facing (looked rotated on some spawns, fine on others). We
    // pin that spin relative to the rod so it's consistent from the player's view; the per-item
    // dangleFacingYawOffset turns which side shows. A round bobber doesn't care about the spin.
    private Quaternion DangleRestRotation(BobberController bobber)
    {
        if (bobber == null) return Quaternion.identity;

        Transform attach = bobber.LineAttachPoint;
        if (attach == null || attach == bobber.transform) return bobber.transform.rotation;

        // Direction from the bobber origin to its attach point, in the bobber's own local space
        // (rotation-invariant fixed geometry). The rest pose rotates that local axis to world up.
        Vector3 localAttachDir = bobber.transform.InverseTransformPoint(attach.position);
        if (localAttachDir.sqrMagnitude < 0.0001f) return bobber.transform.rotation;
        localAttachDir.Normalize();

        // 1) Point the attach axis straight up toward the anchor. Leaves the spin about up free.
        Quaternion rot = Quaternion.FromToRotation(localAttachDir, Vector3.up);

        // 2) Lock that free spin: aim a lure-side axis (local axis perpendicular to the attach
        //    axis) along the rod's horizontal heading, so the facing is the same every spawn.
        Vector3 localSide = Vector3.ProjectOnPlane(Vector3.up, localAttachDir);
        if (localSide.sqrMagnitude < 0.0001f) localSide = Vector3.ProjectOnPlane(Vector3.forward, localAttachDir);
        if (localSide.sqrMagnitude > 0.0001f)
        {
            Vector3 sideNow = Vector3.ProjectOnPlane(rot * localSide.normalized, Vector3.up);
            Vector3 rodHeading = rodTip != null ? Vector3.ProjectOnPlane(rodTip.forward, Vector3.up) : Vector3.forward;
            if (sideNow.sqrMagnitude > 0.0001f && rodHeading.sqrMagnitude > 0.0001f)
                rot = Quaternion.FromToRotation(sideNow.normalized, rodHeading.normalized) * rot;
        }

        // 3) Per-item nudge so each model can be turned to present its good side to the player.
        float yawOffset = currentBobber != null ? currentBobber.dangleFacingYawOffset : 0f;
        if (Mathf.Abs(yawOffset) > 0.001f)
            rot = Quaternion.AngleAxis(yawOffset, Vector3.up) * rot;

        return rot;
    }

    private void ApplyLinearLimit(float distance)
    {
        if (activeJoint == null) return;

        activeJoint.xMotion = ConfigurableJointMotion.Limited;
        activeJoint.yMotion = ConfigurableJointMotion.Limited;
        activeJoint.zMotion = ConfigurableJointMotion.Limited;

        var limit = activeJoint.linearLimit;
        limit.limit = Mathf.Max(0.0001f, distance);
        limit.bounciness = 0f;
        limit.contactDistance = 0.01f;
        activeJoint.linearLimit = limit;

        var limitSpring = activeJoint.linearLimitSpring;
        limitSpring.spring = 0f;
        limitSpring.damper = 0f;
        activeJoint.linearLimitSpring = limitSpring;
    }

    private void LaunchBobber(Vector3 direction, float force)
    {
        if (activeBobber == null)
        {
            Debug.LogError("[FishingLine] Tried to launch but there is no bobber instance.");
            return;
        }

        Rigidbody rb = activeBobberRb;
        if (rb == null) return;

        Transform spawnOrigin = stableThrowOrigin != null ? stableThrowOrigin : rodTip;

        ApplyLinearLimit(castMaxDistance);

        rb.isKinematic = false;
        rb.position = spawnOrigin.position;
        rb.rotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        activeBobber.ResetForCast();
        activeBobber.BeginFlight();
        isDangling = false;

        SetBobberCollidersEnabled(true);

        Vector3 launchDirection = Quaternion.AngleAxis(-launchAngle, Vector3.Cross(Vector3.up, direction)) * direction;
        rb.AddForce(launchDirection * force, ForceMode.VelocityChange);
        activeBobber.ApplyAirTumbleTorque();

        verletRope.SetupRope(rodTip, activeBobber.LineAttachPoint);
    }

    private void ReturnBobberToRod()
    {
        ParkBobberAtAnchor();
    }

    private void ParkBobberAtAnchor()
    {
        if (activeBobber == null || anchorTransform == null) return;

        ApplyLinearLimit(dangleLength);

        Rigidbody rb = activeBobberRb;
        if (rb != null)
        {
            // Kinematic-teleport-untoggle so the position move is clean and the joint solver picks
            // up the new state on the next FixedUpdate without fighting a stale dynamic position.
            // Velocities are zeroed only after going dynamic again — Unity 6 warns when you set
            // angularVelocity on a kinematic body.
            rb.isKinematic = true;
            rb.position = DangleRestPosition();
            rb.rotation = DangleRestRotation(activeBobber);
            Physics.SyncTransforms();
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        activeBobber.ResetForCast();
        ApplyDangleDamping(rb);
        SetBobberCollidersEnabled(false);
        isDangling = true;

        if (rodTip != null) verletRope.SetupRope(rodTip, activeBobber.LineAttachPoint);
    }
}
