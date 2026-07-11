// FishPreset.cs
// This ScriptableObject holds data for a single type of fish.
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NewFishPreset", menuName = "Fishing/Fish Preset")]
public class FishPreset : ScriptableObject
{
    [Header("Basic Info")]
    public string fishName;
    [TextArea]
    public string description;
    public Sprite fishImage;
    public GameObject fishPrefab;

    [Header("Size")]
    public SizeClass sizeClass;
    public bool isLegendary;
    public float minLengthCm;
    public float maxLengthCm;

    [Header("Behavior")]
    [Tooltip("Relative spawn weight during the DAY: a fish at 100 spawns twice as often as one at 50. " +
             "0 = never spawns during the day. Only fish with a non-zero weight for the current time of day compete for the roll.")]
    [Range(0f, 100f)]
    [FormerlySerializedAs("spawnChance")]
    public float daySpawnChance = 50f;
    [Tooltip("Relative spawn weight at NIGHT (same scale as Day). 0 = never spawns at night. " +
             "Night falls naturally on the in-game clock, or instantly via the vendor's Night Lantern.")]
    [Range(0f, 100f)]
    public float nightSpawnChance = 0f;
    [Range(0f, 1f)]
    public float catchProbability = 0.5f;
    [Tooltip("Which tackle this species responds to. Bobber fish care about the preferred-baits list below; " +
             "Lure fish ignore bait entirely. Both = responds to either.")]
    public TackleAttraction attractedTo = TackleAttraction.Both;
    [Tooltip("Radius (m) at which this species notices a bobber/lure in the water and swims over. " +
             "The lure brain multiplies this by its movingRadiusMultiplier (default 1.5) while the lure is moving. " +
             "Set 0 to fall back to the FishRipple prefab's actionRadius.")]
    public float awarenessRadius = 8f;

    [Tooltip("Full field-of-view angle (degrees), centred on the fish's current heading, that the " +
             "bobber/lure must fall within before this species notices it — aim matters, not just " +
             "distance. E.g. 120 = the fish notices anything within ±60° of dead ahead. Set 0 " +
             "for the old omnidirectional behavior (notices anywhere within awarenessRadius, " +
             "regardless of which way it's facing).")]
    public float awarenessConeAngle = 0f;

    [Tooltip("If checked, fish of THIS species gather into a loose shoal and swim in roughly the " +
             "same direction whenever more than one of them shares the water. A lone fish of a " +
             "schooling species just wanders. Cohesion/alignment radius and strength are tuned on " +
             "the FishRipple prefab (Schooling header); personal-space separation applies to every " +
             "fish regardless of this flag.")]
    public bool schoolsTogether = false;

    [Header("Predator")]
    [Tooltip("Species this fish hunts. When prey of one of these species shares the water, this fish " +
             "chases it down and scares it off. Leave empty for non-predators (most fish). " +
             "Chase tuning (sight range, speed, etc.) lives on the FishRipple prefab.")]
    public List<FishPreset> prey = new List<FishPreset>();

    // A fish with anything in its prey list actively hunts; everyone else just wanders/schools.
    public bool IsPredator => prey != null && prey.Count > 0;

    // True when this species gathers with its own kind (boids cohesion + alignment in FishRipple).
    public bool Schools => schoolsTogether;

    // True when the given species is on this predator's menu.
    public bool Hunts(FishPreset other) => other != null && prey != null && prey.Contains(other);

    [Header("Swim Animation")]
    [Tooltip("Tail-beat rate in beats/second while calmly swimming. Drives the procedural sway shader only — movement speed is unaffected.")]
    public float swimFrequency = 1.8f;
    [Tooltip("How many wave cycles fit along the body. Stiff swimmers (carp, pike) ~0.6, full-body undulators (eel, lamprey) ~1.8.")]
    public float swimBodyWaves = 0.7f;
    [Tooltip("How far the tail swings, as a fraction of body length.")]
    [Range(0f, 0.3f)]
    public float swimWaveAmplitude = 0.07f;
    [Tooltip("Whole-body side-to-side drift, as a fraction of body length.")]
    [Range(0f, 0.1f)]
    public float swimSideAmplitude = 0.015f;
    [Tooltip("Yaw wobble around the body center, in radians. High values read as hovering/wobbling (puffer, sunfish).")]
    [Range(0f, 0.5f)]
    public float swimPivotAmount = 0.06f;
    [Tooltip("Portion of the body (from the head) that stays rigid. 0 = the whole body flexes (eels), 0.5 = only the rear half moves.")]
    [Range(0f, 1f)]
    public float swimMaskStart = 0.35f;

    [Header("Fish Fight")]
    [Tooltip("How far (m) this fish can drag the line beyond the spot it was hooked by its OWN " +
             "swimming during the fight. At this limit its pull stalls — it hangs at the end of " +
             "its leash, still free to swim sideways or back in — so it can never snap the line " +
             "by itself. Only the PLAYER reeling while it faces the wrong way pushes it past " +
             "this, toward the snap (the rod's Line Slack Before Break). Leave 0 to use an " +
             "automatic size-based default (~1.5 m for Tiny up to ~6 m for Huge).")]
    public float fightPullDistance = 0f;

    [Header("Economy & Conditions")]
    public int basePrice;
    public float pricePerCm;
    [Tooltip("Bait this fish is attracted to. Drag BaitItem assets from ScriptableObjects/Bait_Scriptable here. " +
             "Only used when the fish responds to bobbers — hidden for lure-only species.")]
    [ShowIf(nameof(RespondsToBobber))]
    public List<BaitItem> preferredBaits = new List<BaitItem>();

    [Tooltip("If checked, this species will bite an EMPTY HOOK (no bait equipped) — but far less " +
             "eagerly than when baited (the zone's No-Bait Interest multiplier slows it). Unchecked = " +
             "ignores a baitless hook entirely.")]
    [ShowIf(nameof(RespondsToBobber))]
    public bool bitesWithoutBait = false;

    [Tooltip("If checked, this species is extra drawn to the POPPER lure: it notices the popper " +
             "from farther away and strikes it more readily (tuned by the LureBiteBrain's popper " +
             "preference settings). No effect with the basic lure or a bobber.")]
    [ShowIf(nameof(RespondsToLure))]
    public bool prefersPopper = false;

    public bool RespondsToBobber => attractedTo != TackleAttraction.Lure;
    public bool RespondsToLure => attractedTo != TackleAttraction.Bobber;

    // Spawn weight for the current time of day. 0 = this fish never spawns then.
    public float GetSpawnWeight(bool isNight) => isNight ? nightSpawnChance : daySpawnChance;

    // Whether this fish can appear during the day / at night at all.
    public bool SpawnsByDay => daySpawnChance > 0f;
    public bool SpawnsByNight => nightSpawnChance > 0f;

    // Human-readable "when to find it" summary for the encyclopedia entry.
    public string TimeOfDayLabel =>
        SpawnsByDay && SpawnsByNight ? "Day & Night"
        : SpawnsByNight ? "Night"
        : SpawnsByDay ? "Day"
        : "—";
}

public enum TackleAttraction
{
    Bobber,
    Lure,
    Both
}

public enum SizeClass
{
    Tiny,
    Small,
    Medium,
    Large,
    Huge
}

public static class SizeClassHelper
{
    // Absolute length brackets in cm. Adjust as gameplay tuning evolves.
    public static SizeClass FromLengthCm(float lengthCm)
    {
        if (lengthCm < 10f) return SizeClass.Tiny;
        if (lengthCm < 30f) return SizeClass.Small;
        if (lengthCm < 60f) return SizeClass.Medium;
        if (lengthCm < 120f) return SizeClass.Large;
        return SizeClass.Huge;
    }

    // Camera view distance used by ModelViewer when displaying a caught fish.
    // Smaller value = camera closer to the fish.
    public static float GetCameraViewDistance(SizeClass sizeClass)
    {
        switch (sizeClass)
        {
            case SizeClass.Tiny:   return 0.5f;
            case SizeClass.Small:  return 0.8f;
            case SizeClass.Medium: return 1.2f;
            case SizeClass.Large:  return 2.0f;
            case SizeClass.Huge:   return 3.5f;
            default:               return 1f;
        }
    }

    // Multiplier on the bobber-nibble orbit (and the start / pass / touch ranges that scale with it)
    // so a bigger fish circles FARTHER out and has room to carve its turn, instead of folding its
    // long body around a tight ring. Medium is the baseline (1.0) — the nibble tunables on FishRipple
    // are authored for it. Kept moderate so even a Huge fish's start range stays inside commitDistance.
    public static float GetNibbleSpaceScale(SizeClass sizeClass)
    {
        switch (sizeClass)
        {
            case SizeClass.Tiny:   return 0.65f;
            case SizeClass.Small:  return 0.85f;
            case SizeClass.Medium: return 1f;
            case SizeClass.Large:  return 1.5f;
            case SizeClass.Huge:   return 2f;
            default:               return 1f;
        }
    }
}