using UnityEngine;

/// <summary>
/// Makes a flame quad's tip trail behind world motion, like a real flame being carried.
/// Tracks this transform's velocity, runs it through a soft spring, and feeds the result
/// into the Flame_Texture_Wobble shader's _MotionOffset property. The graph multiplies
/// that offset by UV.y (the same mask as the flicker wobble), so the base stays anchored
/// to the fish while the top lags and settles with a little flick.
/// Runs after default LateUpdates so BillboardSprite has already oriented the quad
/// before the velocity is projected onto its axes.
/// </summary>
[DefaultExecutionOrder(100)]
public class FlameLag : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Renderer using the Flame_Texture_Wobble material. Defaults to the Renderer on this object.")]
    [SerializeField] private Renderer flameRenderer;

    [Header("Lag Feel")]
    [Tooltip("World units/sec of motion -> UV offset at the flame tip. Negative flips the trail direction.")]
    [SerializeField] private float velocityToOffset = 0.08f;
    [Tooltip("Spring stiffness. Higher = the flame straightens up faster after motion stops.")]
    [SerializeField] private float stiffness = 80f;
    [Tooltip("Spring damping. Critical damping is 2*sqrt(stiffness) (~18 at 80); lower values give a small overshoot flick.")]
    [SerializeField] private float damping = 12f;
    [Tooltip("Cap on the UV offset so the flame tip never slides off the quad's edge.")]
    [SerializeField] private float maxOffset = 0.3f;
    [Tooltip("Position jumps larger than this are treated as teleports and don't kick the flame.")]
    [SerializeField] private float teleportDistance = 3f;
    [Tooltip("Inspection UIs run on unscaled time (timeScale 0), so default on.")]
    [SerializeField] private bool useUnscaledTime = true;

    private static readonly int MotionOffsetId = Shader.PropertyToID("_MotionOffset");

    private MaterialPropertyBlock propertyBlock;
    private Vector3 lastPosition;
    private Vector2 offset;
    private Vector2 offsetVelocity;

    private void Awake()
    {
        if (flameRenderer == null)
            flameRenderer = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        lastPosition = transform.position;
        offset = Vector2.zero;
        offsetVelocity = Vector2.zero;
        Apply();
    }

    private void LateUpdate()
    {
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f)
            return;

        Vector3 worldDelta = transform.position - lastPosition;
        lastPosition = transform.position;

        if (worldDelta.magnitude > teleportDistance)
        {
            offset = Vector2.zero;
            offsetVelocity = Vector2.zero;
            Apply();
            return;
        }

        // Velocity in the quad's own axes so the lag stays correct while billboarding
        // (rotation only — scale must not distort it).
        Vector3 localVelocity = transform.InverseTransformDirection(worldDelta / dt);
        Vector2 target = new Vector2(localVelocity.x, localVelocity.y) * velocityToOffset;
        target = Vector2.ClampMagnitude(target, maxOffset);

        // Semi-implicit spring toward the velocity target; clamp the sim step so a
        // frame hitch can't make it explode.
        float simDt = Mathf.Min(dt, 1f / 30f);
        offsetVelocity += (stiffness * (target - offset) - damping * offsetVelocity) * simDt;
        offset += offsetVelocity * simDt;
        offset = Vector2.ClampMagnitude(offset, maxOffset);

        Apply();
    }

    private void Apply()
    {
        if (flameRenderer == null)
            return;
        flameRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetVector(MotionOffsetId, offset);
        flameRenderer.SetPropertyBlock(propertyBlock);
    }
}
