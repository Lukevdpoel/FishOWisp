using UnityEngine;

public class WaterRippleBinder : MonoBehaviour
{
    [System.Serializable]
    public struct RippleSettings
    {
        [Tooltip("Peak ripple height at the source. Larger = stronger normal bend and SSR distortion.")]
        public float amplitude;
        [Tooltip("Spatial falloff radius in world units. The ripple fades out by this distance.")]
        public float radius;
        [Tooltip("How long the ripple is visible before it fully fades, in seconds.")]
        public float lifetime;
    }

    [Header("Per-Event Ripple Shape")]
    public RippleSettings impact = new RippleSettings { amplitude = 0.4f, radius = 2.0f, lifetime = 2.5f };
    public RippleSettings nibble = new RippleSettings { amplitude = 0.12f, radius = 0.6f, lifetime = 1.2f };
    public RippleSettings bite = new RippleSettings { amplitude = 0.25f, radius = 1.2f, lifetime = 1.8f };

    [Header("Global Wave Tuning")]
    [Tooltip("Wave count per unit distance. Higher = tighter concentric rings.")]
    public float frequency = 6f;
    [Tooltip("How fast wave fronts travel outward from the source.")]
    public float waveSpeed = 4f;

    void OnEnable()
    {
        FishingEvents.OnBobberLandedInWater += HandleLand;
        FishingEvents.OnFishNibble += HandleNibble;
        FishingEvents.OnFishBite += HandleBite;
        WaterRippleEmitter.SetGlobalParams(frequency, waveSpeed);
    }

    void OnDisable()
    {
        FishingEvents.OnBobberLandedInWater -= HandleLand;
        FishingEvents.OnFishNibble -= HandleNibble;
        FishingEvents.OnFishBite -= HandleBite;
    }

    void OnValidate()
    {
        if (Application.isPlaying)
        {
            WaterRippleEmitter.SetGlobalParams(frequency, waveSpeed);
        }
    }

    private void HandleLand(BobberController bobber) => Emit(bobber, impact);
    private void HandleNibble(BobberController bobber) => Emit(bobber, nibble);
    private void HandleBite(BobberController bobber) => Emit(bobber, bite);

    private void Emit(BobberController bobber, RippleSettings settings)
    {
        if (bobber == null) return;
        WaterRippleEmitter.Emit(bobber.transform.position, settings.amplitude, settings.radius, settings.lifetime);
    }
}
