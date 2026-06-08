using System;
using UnityEngine;

// One spawned waterlily under the player's feet. Emerges from below the water surface,
// idles with a damped-spring press when the player stands close, then sinks and is
// returned to the spawner's pool. The transform's Y is driven directly here so a
// displacement-shader material isn't strictly required — but you'll want to assign a
// material variant on the prefab with the wave/displacement effect disabled so these
// don't fight the surface wave.
public class WaterLilyPad : MonoBehaviour
{
    [Header("Emerge")]
    [Tooltip("Y offset applied to the resting position. Negative = pad sits below the detected water surface. Use this to compensate for the prefab's pivot or to make the pad sit slightly submerged.")]
    [SerializeField] private float surfaceOffset = 0f;
    [Tooltip("How far below the resting position the pad starts before emerging.")]
    [SerializeField] private float emergeDepth = 0.6f;
    [SerializeField] private float emergeDuration = 0.45f;
    [SerializeField] private AnimationCurve emergeCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.7f, 1.15f),
        new Keyframe(1f, 1f));

    [Header("Footprint")]
    [Tooltip("XZ radius this pad reserves on the water — no other pad spawns within this distance of its center. Read by WaterLilyPadSpawner.")]
    [SerializeField] private float footprintRadius = 0.7f;

    [Header("Lifetime")]
    [Tooltip("Pad stays alive while the player is within this XZ distance. When the player moves out and stays out for retentionGrace seconds, the pad sinks.")]
    [SerializeField] private float retentionRadius = 2.5f;
    [SerializeField] private float retentionGrace = 1.2f;
    [SerializeField] private float sinkDuration = 0.6f;

    public float FootprintRadius => footprintRadius;

    [Header("Press Under Player")]
    [Tooltip("Player must be within this radius (XZ) to depress the pad.")]
    [SerializeField] private float pressRadius = 1.2f;
    [SerializeField] private float pressDepth = 0.12f;
    [Tooltip("Higher = snappier spring. Acts like 2*PI*frequency.")]
    [SerializeField] private float springStiffness = 90f;
    [Tooltip("0 = bouncy, 1 = critically damped. Below 1 overshoots and bobs.")]
    [Range(0f, 1.5f)]
    [SerializeField] private float springDamping = 0.35f;

    [Header("Visual Wobble")]
    [Tooltip("Small random rotation around Y when emerging so the cluster doesn't look stamped.")]
    [SerializeField] private float yawJitter = 60f;

    private enum State { Emerging, Idle, Sinking, Disabled }
    private State state = State.Disabled;

    private float surfaceY;
    private float restY;          // animated by emerge/sink phase (without spring)
    private float currentOffsetY; // spring offset from restY (negative when pressed)
    private float velocityY;
    private float phaseTimer;
    private float awayTimer;

    // Cached so we don't call Mathf.Sqrt(springStiffness) every Update on every pad.
    // springStiffness is a serialized constant at runtime, so the sqrt only has to happen
    // once on Activate (and OnValidate, for editor-time inspector changes).
    private float sqrtSpringStiffness;

    // Sleep epsilons. When the spring is below these AND the player isn't pressing the pad,
    // we skip the spring math + transform.position write entirely. Transform writes propagate
    // a dirty flag through all children, so skipping them when nothing has changed is the
    // single biggest per-pad win.
    private const float SleepVelocityEpsilon = 0.0005f;
    private const float SleepDisplacementEpsilon = 0.0005f;
    private const float TransformWriteEpsilon = 0.00005f;

    private Transform player;
    private Action<WaterLilyPad> returnToPool;

    public void Activate(Vector3 spawnPos, float waterSurfaceY, Transform playerTransform, Action<WaterLilyPad> onReturn)
    {
        // surfaceY here is the pad's resting Y, not the literal water plane — surfaceOffset lets
        // the prefab sit above/below the detected water surface (pivot compensation or visual sink).
        surfaceY = waterSurfaceY + surfaceOffset;
        player = playerTransform;
        returnToPool = onReturn;

        Vector3 startPos = new Vector3(spawnPos.x, surfaceY - emergeDepth, spawnPos.z);
        transform.position = startPos;
        transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(-yawJitter, yawJitter), 0f);

        restY = startPos.y;
        currentOffsetY = 0f;
        velocityY = 0f;
        phaseTimer = 0f;
        awayTimer = 0f;
        sqrtSpringStiffness = Mathf.Sqrt(Mathf.Max(0f, springStiffness));
        state = State.Emerging;

        gameObject.SetActive(true);
    }

#if UNITY_EDITOR
    // Keep the cached sqrt in sync when designers tweak springStiffness in the inspector during play.
    private void OnValidate()
    {
        sqrtSpringStiffness = Mathf.Sqrt(Mathf.Max(0f, springStiffness));
    }
#endif

    void Update()
    {
        if (state == State.Disabled) return;

        float dt = Time.deltaTime;

        switch (state)
        {
            case State.Emerging:
                phaseTimer += dt;
                float et = Mathf.Clamp01(phaseTimer / Mathf.Max(0.01f, emergeDuration));
                float ek = emergeCurve.Evaluate(et);
                restY = Mathf.LerpUnclamped(surfaceY - emergeDepth, surfaceY, ek);
                if (phaseTimer >= emergeDuration)
                {
                    restY = surfaceY;
                    state = State.Idle;
                    phaseTimer = 0f;
                }
                break;

            case State.Idle:
                restY = surfaceY;
                if (IsPlayerWithinRetention())
                {
                    awayTimer = 0f;
                }
                else
                {
                    awayTimer += dt;
                    if (awayTimer >= retentionGrace)
                    {
                        state = State.Sinking;
                        phaseTimer = 0f;
                    }
                }
                break;

            case State.Sinking:
                phaseTimer += dt;
                float st = Mathf.Clamp01(phaseTimer / Mathf.Max(0.01f, sinkDuration));
                restY = Mathf.Lerp(surfaceY, surfaceY - emergeDepth, st);
                if (phaseTimer >= sinkDuration)
                {
                    Despawn();
                    return;
                }
                break;
        }

        // Damped-spring offset toward the press target (negative Y when player close).
        float pressTarget = ComputePressTarget();
        float displacement = currentOffsetY - pressTarget;

        // Sleep early-out. If the spring has settled (velocity ~0, displacement ~0) AND the player
        // isn't pressing the pad AND we're not in a forced-motion phase (Emerging/Sinking writes restY
        // every frame), we have no work to do — skip the spring step and the transform.position write.
        // This is the dominant per-pad cost when many pads are idling near the water surface, since each
        // transform.position write marks every child of the prefab as dirty.
        bool inMotionPhase = state == State.Emerging || state == State.Sinking;
        bool springAtRest = Mathf.Abs(velocityY) < SleepVelocityEpsilon
                         && Mathf.Abs(displacement) < SleepDisplacementEpsilon;
        if (springAtRest && pressTarget == 0f && !inMotionPhase)
        {
            // Snap exactly to rest so we don't accumulate drift across many sleep frames.
            velocityY = 0f;
            currentOffsetY = pressTarget;
            return;
        }

        // Critically-damped-ish spring: a = -k*(x - target) - 2*sqrt(k)*damping*v
        float accel = -springStiffness * displacement - 2f * sqrtSpringStiffness * springDamping * velocityY;
        velocityY += accel * dt;
        currentOffsetY += velocityY * dt;

        // Only touch transform.position when the Y actually moved. Transform writes propagate
        // a dirty flag through all children, so guarding the write is cheap and pays off for any
        // pad whose visual hierarchy has more than one renderer.
        float newY = restY + currentOffsetY;
        Vector3 p = transform.position;
        if (Mathf.Abs(p.y - newY) > TransformWriteEpsilon)
        {
            p.y = newY;
            transform.position = p;
        }
    }

    private float ComputePressTarget()
    {
        if (player == null) return 0f;
        if (state == State.Sinking) return 0f;

        Vector3 delta = player.position - transform.position;
        delta.y = 0f;
        float distSq = delta.sqrMagnitude;
        float r = pressRadius;
        if (distSq >= r * r) return 0f;

        float t = 1f - Mathf.Sqrt(distSq) / r; // 1 at center, 0 at edge
        // Smoothstep so the edge transition isn't a hard wall.
        t = t * t * (3f - 2f * t);
        return -pressDepth * t;
    }

    private bool IsPlayerWithinRetention()
    {
        if (player == null) return false;
        Vector3 delta = player.position - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude < retentionRadius * retentionRadius;
    }

    private void Despawn()
    {
        state = State.Disabled;
        gameObject.SetActive(false);
        returnToPool?.Invoke(this);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 c = transform.position;
        Gizmos.color = new Color(0.2f, 0.8f, 0.4f, 0.9f);
        DrawXZCircle(c, footprintRadius, 32);
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
        DrawXZCircle(c, retentionRadius, 32);
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
        DrawXZCircle(c, pressRadius, 24);
    }

    private static void DrawXZCircle(Vector3 center, float radius, int segments)
    {
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
