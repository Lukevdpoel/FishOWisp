using System;
using UnityEngine;

// Marker-targeted casting. Holding the aim button (LMB / LT) drops a reticle into the world;
// WASD / the left stick steer it across land or water inside the cast range, and the player
// decides how far to throw by where they put it. The throw itself is an "About Fishing"-style
// whip: pull the mouse (or yank the stick) left/right/back/forward, then push it back the
// opposite way — the reversal speed is the power, and a full-speed whip lands exactly on the
// marker while a soft one drops short. Too slow a push is simply not a cast; keep aiming.
//
// Owns: the marker, the gesture (CastFlickGesture), and the ballistic solve that turns
// "land on this point" into the (direction, force) pair the rest of the fishing stack already
// consumes via FishingEvents.OnThrowBobber. FishingRodController still owns the state machine:
// it fires OnStartCharging when the aim button goes down and OnCancelCharging when it is
// released without a throw.
public class RodCasting : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The TIP of the rod. Used for the actual bobber spawn position.")]
    public Transform throwOrigin;

    [Tooltip("A stationary point (child of Player Root) used as the ballistic origin for the range solve. Prevents wobbling during animation; falls back to throwOrigin.")]
    public Transform predictionOrigin;

    [Tooltip("The visual model of the player to rotate.")]
    public Transform playerModel;

    [Header("Aim Marker")]
    [Tooltip("Optional reticle prefab (must carry a CastAimMarker). Leave empty for the built-in ring.")]
    public CastAimMarker markerPrefab;
    [Tooltip("Marker travel speed (m/s) at full input.")]
    public float markerMoveSpeed = 10f;
    [Tooltip("Closest the marker can sit to the player.")]
    public float minAimDistance = 2f;
    [Tooltip("Cast range: furthest the marker can be placed. Also capped by what maxThrowForce can physically reach at the line's launch angle.")]
    public float maxAimDistance = 12f;
    [Tooltip("Distance in front of the player the marker appears at when aiming begins.")]
    public float startAimDistance = 6f;
    [Tooltip("Surfaces the marker can sit on. Water layers are probed separately (triggers included), so keep this broad.")]
    public LayerMask aimSurfaceMask = ~0;
    [Tooltip("Layers that count as water for the marker (colors it and marks the spot fishable). Left empty it resolves 'WaterColliderFish' and 'Water' by name at Awake.")]
    public LayerMask waterMask;

    [Header("Whip Gesture")]
    public CastFlickGesture.Settings gestureSettings = new CastFlickGesture.Settings();

    [Header("Throw")]
    [Tooltip("Floor on the solved launch speed (m/s).")]
    public float minThrowForce = 5f;
    [Tooltip("Ceiling on the solved launch speed (m/s). Together with the launch angle this bounds the real reachable range.")]
    public float maxThrowForce = 25f;
    [Tooltip("The weakest whip that still casts lands this fraction of the way to the marker; a full-speed whip lands on it.")]
    [Range(0.1f, 1f)] public float minPowerDistanceFraction = 0.45f;
    [Tooltip("MUST MATCH the 'Extra Gravity' value in your BobberController script.")]
    public float bobberExtraGravity = 30f;
    [Tooltip("Used only if no FishingLine is found to read the real launch angle from.")]
    public float launchAngleFallback = 20f;

    [Header("Aiming")]
    [Tooltip("How fast the player model rotates to face the marker.")]
    public float playerAimRotationSpeed = 10f;

    /// <summary>True from aim-button-down until the throw or the cancel.</summary>
    public bool IsAiming => isCharging;

    /// <summary>Where the reticle currently sits (surface-snapped). The camera frames against this.</summary>
    public Vector3 AimMarkerPoint => marker != null && marker.gameObject.activeSelf ? marker.SurfacePoint : markerGoal;

    /// <summary>Current wind-up pull (x = right, y = away), magnitude ≤ 1. ProceduralCastArms poses the rod from this.</summary>
    public Vector2 PullNormalized => gesture.Pull;

    /// <summary>Fired the instant a whip succeeds: (power 0..1, push direction in gesture space). Drives the arm swing.</summary>
    public event Action<float, Vector2> ThrowGesture;

    private readonly CastFlickGesture gesture = new CastFlickGesture();
    private bool isCharging;
    private Camera mainCamera;
    private CastAimMarker marker;
    private Vector3 markerGoal;         // unsnapped aim point we integrate input into
    private FishingLine fishingLine;

    private float LaunchAngle => fishingLine != null ? fishingLine.launchAngle : launchAngleFallback;
    private float TotalGravity => Physics.gravity.magnitude + bobberExtraGravity;
    private Vector3 CastOrigin => predictionOrigin != null ? predictionOrigin.position
                                : throwOrigin != null ? throwOrigin.position
                                : transform.position;

    void Awake()
    {
        mainCamera = Camera.main;

        Transform root = transform.root != null ? transform.root : transform;
        fishingLine = root.GetComponentInChildren<FishingLine>(includeInactive: true);
        if (fishingLine == null) fishingLine = FindFirstObjectByType<FishingLine>(FindObjectsInactive.Include);

        if (waterMask.value == 0)
        {
            int mask = 0;
            int l = LayerMask.NameToLayer("WaterColliderFish");
            if (l >= 0) mask |= 1 << l;
            l = LayerMask.NameToLayer("Water");
            if (l >= 0) mask |= 1 << l;
            waterMask = mask;
        }
    }

    private void OnEnable()
    {
        FishingEvents.OnStartCharging += BeginCharging;
        FishingEvents.OnCancelCharging += Cancel;
        FishingEvents.OnCancelFishing += Cancel;

        // After a scene swap, ensure stale aim state from a previous scene can't leave the
        // marker or charge UI hanging in the new scene.
        isCharging = false;
        FishingEvents.OnToggleChargeUI?.Invoke(false);
        if (marker != null) marker.Hide();
    }

    private void OnDisable()
    {
        FishingEvents.OnStartCharging -= BeginCharging;
        FishingEvents.OnCancelCharging -= Cancel;
        FishingEvents.OnCancelFishing -= Cancel;
        if (marker != null) Destroy(marker.gameObject);
        marker = null;
    }

    void Update()
    {
        if (!isCharging || Time.timeScale == 0f) return;

        float dt = Time.deltaTime;
        Vector2 mouseDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        Vector2 stick = GamepadInput.Look;

        bool threw = gesture.Update(dt, mouseDelta, stick, gestureSettings, out float power, out Vector2 pushDir);

        UpdateMarker(dt);
        FacePlayerAtMarker(dt);

        // Deliberately NOT fed into OnChargeProgressNormalized per-frame: the charge camera
        // reaction pitches/zooms with that value, and the aim must not move the camera
        // vertically. Only the final throw power is sent (in Throw), for the flight spiral.
        if (threw) Throw(power, pushDir);
    }

    private void BeginCharging()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        isCharging = true;
        gesture.Reset();

        Vector3 fwd = CameraFlat(mainCamera != null ? mainCamera.transform.forward : (playerModel != null ? playerModel.forward : transform.forward));
        markerGoal = transform.position + fwd * Mathf.Clamp(startAimDistance, minAimDistance, EffectiveMaxAimDistance());
        markerGoal.y = transform.position.y;

        EnsureMarker();
        marker.Show();
        marker.Place(markerGoal, aimSurfaceMask, waterMask);

        FishingEvents.OnChargeProgressNormalized?.Invoke(0f);
    }

    private void Cancel()
    {
        if (!isCharging) return;
        isCharging = false;
        gesture.Reset();
        if (marker != null) marker.Hide();
    }

    // -------- marker --------

    private void EnsureMarker()
    {
        if (marker != null) return;
        marker = markerPrefab != null
            ? Instantiate(markerPrefab)
            : new GameObject("CastAimMarker").AddComponent<CastAimMarker>();
    }

    private void UpdateMarker(float dt)
    {
        // WASD / the LEFT stick steer the marker (walking is locked while aiming, so both are
        // free); the right stick belongs entirely to the whip gesture.
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"))
                      + GamepadInput.Move;
        input = Vector2.ClampMagnitude(input, 1f);

        if (input.sqrMagnitude > 0.0001f)
        {
            Vector3 camFwd = CameraFlat(mainCamera != null ? mainCamera.transform.forward : Vector3.forward);
            Vector3 camRight = Vector3.Cross(Vector3.up, camFwd);
            markerGoal += (camRight * input.x + camFwd * input.y) * markerMoveSpeed * dt;
        }

        // Clamp to the reachable ring around the player.
        Vector3 offset = markerGoal - transform.position;
        offset.y = 0f;
        float maxRange = EffectiveMaxAimDistance();
        float mag = offset.magnitude;
        if (mag > maxRange) offset *= maxRange / mag;
        else if (mag < minAimDistance && mag > 0.0001f) offset *= minAimDistance / mag;
        markerGoal = transform.position + offset;

        // Keep the goal's height loosely tied to the snapped surface so the probe never starts
        // underneath terrain when aiming up a slope.
        marker.Place(markerGoal, aimSurfaceMask, waterMask);
        markerGoal.y = marker.SurfacePoint.y;
    }

    private void FacePlayerAtMarker(float dt)
    {
        if (playerModel == null || marker == null) return;
        Vector3 toMarker = marker.SurfacePoint - playerModel.position;
        toMarker.y = 0f;
        if (toMarker.sqrMagnitude < 0.01f) return;
        Quaternion targetRot = Quaternion.LookRotation(toMarker.normalized);
        playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetRot, dt * playerAimRotationSpeed);
    }

    // The marker may not be placeable further than the strongest throw can physically reach.
    // Flat-ground range at the launch angle: R = v² · sin(2θ) / g.
    private float EffectiveMaxAimDistance()
    {
        float theta = LaunchAngle * Mathf.Deg2Rad;
        float flatRange = maxThrowForce * maxThrowForce * Mathf.Sin(2f * theta) / TotalGravity;
        return Mathf.Min(maxAimDistance, Mathf.Max(minAimDistance, flatRange));
    }

    // -------- throw --------

    private void Throw(float power, Vector2 pushDir)
    {
        isCharging = false;

        Vector3 origin = CastOrigin;
        Vector3 markerPoint = marker != null ? marker.SurfacePoint : markerGoal;

        // A soft whip drops the cast short of the marker along the throw line.
        float fraction = Mathf.Lerp(minPowerDistanceFraction, 1f, power);
        Vector3 target = Vector3.LerpUnclamped(origin, markerPoint, fraction);
        target.y = SampleSurfaceHeight(target, markerPoint.y);

        Vector3 flat = target - origin;
        flat.y = 0f;
        Vector3 direction = flat.sqrMagnitude > 0.0001f ? flat.normalized
                          : (playerModel != null ? playerModel.forward : transform.forward);
        float force = SolveLaunchSpeed(origin, target);

        // Cache the power as the "charge" for everything that scales with throw strength
        // (flight spiral, rope coil) before the launch consumes it.
        FishingEvents.OnChargeProgressNormalized?.Invoke(power);
        ThrowGesture?.Invoke(power, pushDir);

        if (marker != null) marker.Hide();
        FishingEvents.OnThrowBobber?.Invoke(direction, force);
    }

    // Height of whatever the shortened target point sits over, so undershoots onto a bank or a
    // lower terrace still get a correct arc. Falls back to the marker's height.
    private float SampleSurfaceHeight(Vector3 point, float fallbackY)
    {
        Vector3 from = new Vector3(point.x, transform.position.y + 30f, point.z);
        RaycastHit water = default;
        bool hasWater = waterMask.value != 0
                        && Physics.Raycast(from, Vector3.down, out water, 80f, waterMask, QueryTriggerInteraction.Collide);
        bool hasSolid = Physics.Raycast(from, Vector3.down, out RaycastHit solid, 80f,
                                        aimSurfaceMask & ~waterMask.value, QueryTriggerInteraction.Ignore);
        if (hasWater && (!hasSolid || water.point.y >= solid.point.y)) return water.point.y;
        if (hasSolid) return solid.point.y;
        return fallbackY;
    }

    // Launch speed that lands a projectile fired at the line's fixed launch angle on the target,
    // under gravity + the bobber's extra gravity: v² = g·d² / (2·cos²θ·(d·tanθ − Δh)).
    private float SolveLaunchSpeed(Vector3 origin, Vector3 target)
    {
        float theta = LaunchAngle * Mathf.Deg2Rad;
        Vector3 delta = target - origin;
        float h = delta.y;
        delta.y = 0f;
        float d = delta.magnitude;
        if (d < 0.01f) return minThrowForce;

        float cos = Mathf.Cos(theta);
        float denom = 2f * cos * cos * (d * Mathf.Tan(theta) - h);
        // Target above the reachable line-of-fire (steep cliff overhead) — just heave at max.
        if (denom <= 0.001f) return maxThrowForce;

        float v = Mathf.Sqrt(TotalGravity * d * d / denom);
        return Mathf.Clamp(v, minThrowForce, maxThrowForce);
    }

    private static Vector3 CameraFlat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }
}
