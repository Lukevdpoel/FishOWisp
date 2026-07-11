using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Feeds the water ripple simulation (WaterRippleSimRenderer) from gameplay. Anchors the
/// sim map to the bobber while it's in the water, stamps one-shot impulses on fishing
/// events (splashdown, nibble, bite, lure tugs, hooked-fish jump splashes), and lays a
/// continuous trail of small impulses while the bobber — or a nearby surface fish — is
/// actually moving. Ripples trace movement and stop when it stops; a resting bobber
/// makes none.
/// </summary>
public class WaterRippleBinder : MonoBehaviour
{
    [System.Serializable]
    public struct Impulse
    {
        [Tooltip("Footprint radius of the push, in world meters.")]
        public float radius;
        [Tooltip("How hard the surface is pushed (sim height units). ~0.1 is a subtle nudge, ~1 a big splash. Negative pushes the surface down first, like a plunge.")]
        public float strength;
    }

    [Header("One-Shot Impulses")]
    [Tooltip("Bobber/lure splashdown after a cast.")]
    public Impulse impact = new Impulse { radius = 0.18f, strength = 0.75f };
    [Tooltip("A fish brushing/wobbling the bobber.")]
    public Impulse nibble = new Impulse { radius = 0.08f, strength = 0.25f };
    [Tooltip("The committed bite that submerges the bobber.")]
    public Impulse bite = new Impulse { radius = 0.12f, strength = 0.5f };
    [Tooltip("Each lure yank (A/D press).")]
    public Impulse lureTug = new Impulse { radius = 0.09f, strength = 0.3f };
    [Tooltip("A hooked fish splashing back down from a fight jump.")]
    public Impulse jumpSplash = new Impulse { radius = 0.18f, strength = 0.7f };

    [Header("Bobber Movement Trail")]
    [Tooltip("Meters of horizontal bobber travel between trail impulses. Smaller = denser, more continuous wake. Keep at or below trailRadius or the wake breaks into separate dots.")]
    public float trailSpacing = 0.08f;
    [Tooltip("Footprint radius of each trail impulse, in meters.")]
    public float trailRadius = 0.08f;
    [Tooltip("Trail impulse strength at (or above) the reference speed below. Scales down for slower drags.")]
    public float trailStrength = 0.2f;
    [Tooltip("Horizontal speed (m/s) at which the trail reaches full strength.")]
    public float trailRefSpeed = 1.5f;
    [Tooltip("Below this horizontal speed the bobber counts as resting and leaves no trail — buoyancy bobbing and pixel creep never ripple.")]
    public float trailMinSpeed = 0.15f;

    [Header("Fish Trails")]
    [Tooltip("Also let the swimming silhouette fish (FishRipple) leave wakes while the sim is running. They only register inside the map around the bobber.")]
    public bool fishTrails = true;
    [Tooltip("Meters of fish travel between trail impulses.")]
    public float fishTrailSpacing = 0.18f;
    [Tooltip("Footprint radius of each fish impulse, in meters.")]
    public float fishTrailRadius = 0.08f;
    [Tooltip("Strength of each fish impulse. Keep well below the bobber trail so fish read as gentle wakes.")]
    public float fishTrailStrength = 0.1f;
    [Tooltip("A fish slower than this (m/s) is drifting/resting and leaves no wake.")]
    public float fishTrailMinSpeed = 0.4f;

    BobberController trackedBobber;
    Vector3 lastTrailPos;
    float lastTrailTime;
    bool hasTrailAnchor;

    // Per-fish position+time of the last trail stamp: x,y,z = world pos, w = Time.time.
    readonly Dictionary<FishRipple, Vector4> fishLastStamp = new Dictionary<FishRipple, Vector4>();
    static readonly List<FishRipple> fishPruneScratch = new List<FishRipple>();
    float nextFishPruneTime;

    void OnEnable()
    {
        FishingEvents.OnBobberLandedInWater += HandleLand;
        FishingEvents.OnFishNibble += HandleNibble;
        FishingEvents.OnFishBite += HandleBite;
        FishingEvents.OnLureTugged += HandleLureTug;
        FishingEvents.OnHookedFishJumpChanged += HandleJumpChanged;
        FishingEvents.OnCancelFishing += HandleFishingOver;
        FishingEvents.OnReelingCompleted += HandleFishingOver;
    }

    void OnDisable()
    {
        FishingEvents.OnBobberLandedInWater -= HandleLand;
        FishingEvents.OnFishNibble -= HandleNibble;
        FishingEvents.OnFishBite -= HandleBite;
        FishingEvents.OnLureTugged -= HandleLureTug;
        FishingEvents.OnHookedFishJumpChanged -= HandleJumpChanged;
        FishingEvents.OnCancelFishing -= HandleFishingOver;
        FishingEvents.OnReelingCompleted -= HandleFishingOver;
        HandleFishingOver();
    }

    void HandleLand(BobberController bobber)
    {
        if (bobber == null) return;
        trackedBobber = bobber;
        hasTrailAnchor = false;
        WaterRippleSimRenderer.SetRippleAnchor(bobber.transform);
        Emit(bobber, impact);
    }

    void HandleNibble(BobberController bobber) => Emit(bobber, nibble);
    void HandleBite(BobberController bobber) => Emit(bobber, bite);

    void HandleLureTug()
    {
        if (trackedBobber != null && trackedBobber.IsInWater) Emit(trackedBobber, lureTug);
    }

    void HandleJumpChanged(bool airborne)
    {
        // The hooked fish rides the bobber, so the splash-back lands where the bobber is.
        if (!airborne) Emit(trackedBobber, jumpSplash);
    }

    void HandleFishingOver()
    {
        trackedBobber = null;
        hasTrailAnchor = false;
        fishLastStamp.Clear();
        WaterRippleSimRenderer.SetRippleAnchor(null);
    }

    static void Emit(BobberController bobber, Impulse impulse)
    {
        if (bobber == null) return;
        WaterRippleSimRenderer.AddImpulse(bobber.transform.position, impulse.radius, impulse.strength);
    }

    void Update()
    {
        // Destroyed bobber (reel-in despawn paths) reads as null — the sim renderer idles on
        // its own dead anchor too, so nothing else to unwind here.
        if (trackedBobber == null) return;

        UpdateBobberTrail();
        if (fishTrails) UpdateFishTrails();
    }

    void UpdateBobberTrail()
    {
        if (!trackedBobber.IsInWater)
        {
            hasTrailAnchor = false; // airborne (fight jump) — restart the trail on splashdown
            return;
        }

        Vector3 pos = trackedBobber.transform.position;
        if (!hasTrailAnchor)
        {
            lastTrailPos = pos;
            lastTrailTime = Time.time;
            hasTrailAnchor = true;
            return;
        }

        Vector3 flat = pos - lastTrailPos;
        flat.y = 0f;
        float dist = flat.magnitude;
        if (dist < trailSpacing) return;

        // Speed over the whole gap since the last stamp, so slow creep that takes seconds
        // to cover the spacing is correctly seen as resting, not as one fast hop.
        float speed = dist / Mathf.Max(0.0001f, Time.time - lastTrailTime);
        lastTrailPos = pos;
        lastTrailTime = Time.time;
        if (speed < trailMinSpeed) return;

        float strength = trailStrength * Mathf.Clamp01(speed / Mathf.Max(0.01f, trailRefSpeed));
        WaterRippleSimRenderer.AddImpulse(pos, trailRadius, strength);
    }

    void UpdateFishTrails()
    {
        for (int i = 0; i < FishRipple.Active.Count; i++)
        {
            FishRipple fish = FishRipple.Active[i];
            if (fish == null) continue;

            Vector3 pos = fish.transform.position;
            if (!fishLastStamp.TryGetValue(fish, out Vector4 last))
            {
                fishLastStamp[fish] = new Vector4(pos.x, pos.y, pos.z, Time.time);
                continue;
            }

            Vector3 flat = pos - new Vector3(last.x, last.y, last.z);
            flat.y = 0f;
            float dist = flat.magnitude;
            if (dist < fishTrailSpacing) continue;

            float speed = dist / Mathf.Max(0.0001f, Time.time - last.w);
            fishLastStamp[fish] = new Vector4(pos.x, pos.y, pos.z, Time.time);
            if (speed < fishTrailMinSpeed) continue;

            // The sim renderer drops stamps that fall outside the map around the bobber,
            // so far-away fish cost nothing beyond this loop.
            WaterRippleSimRenderer.AddImpulse(pos, fishTrailRadius, fishTrailStrength);
        }

        // Occasionally drop entries for despawned fish so the dictionary can't grow forever.
        if (Time.time >= nextFishPruneTime)
        {
            nextFishPruneTime = Time.time + 5f;
            fishPruneScratch.Clear();
            foreach (var kvp in fishLastStamp)
            {
                if (kvp.Key == null || !FishRipple.Active.Contains(kvp.Key)) fishPruneScratch.Add(kvp.Key);
            }
            for (int i = 0; i < fishPruneScratch.Count; i++) fishLastStamp.Remove(fishPruneScratch[i]);
            fishPruneScratch.Clear();
        }
    }
}
