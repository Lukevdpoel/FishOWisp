using System.Collections.Generic;
using UnityEngine;

// Twilight-Princess-style lure attraction & bite brain, ported from the TP decompilation
// (zeldaret/tp: d_a_mg_fish.cpp search_lure / mf_lure_search). Owned and ticked by
// FishingZone while a lure sits in its water; the bait/bobber path is untouched.
//
// The TP loop, mapped onto this game's fish:
//   NOTICE   — fish already self-attract to the bobber within their actionRadius and hover
//              at followerHoverDistance (that hover ring IS TP's 50–100 unit stand-off).
//              While the lure recently moved, the brain scales every fish's awareness
//              radius up (TP: detection ×1.5 while the twitch countdown runs).
//   HOVER    — up to maxActiveChasers hovering fish become active chasers. Each lure
//              movement re-arms an idle bite timer (TP: 10–40 frames) and refreshes the
//              chaser's patience (TP: 100–300 frames). No movement = no new rolls, and
//              patience runs out — the fish gets bored and leaves for a while.
//   ROLL     — when an armed bite timer expires the fish rolls to strike: ~15% for a
//              still lure, ~40% for a moving one (TP rolls 30% then falls back to 15%),
//              scaled by weather (TP: rain ×1.75, outside midday ×1.5 → here Night) and
//              species eagerness (TP: catfish ×2 → here FishPreset.catchProbability).
//   DIRECT   — landing the lure within directHitRadius of a fish makes that fish strike
//              instantly, no roll (TP: casting onto a fish bites immediately). Precise by
//              design — anything outside the radius goes through the normal loop.
//   STRIKE   — the winner dashes the lure fast and thrashing (FishRipple.Striking), then on
//              contact clamps onto it (FishRipple.Grabbing) and holds it for a beat — the
//              player's reaction window. React in time and the zone commits the bite
//              (BobberController.HookFish); miss it and the fish spits the lure and swims off,
//              never disappearing. The grab start/release are surfaced via the two callbacks.
//
// Deliberately not ported (single lure type, no species special-casing): per-lure
// learning bitmask, the loach's frog-lure gate, and the second-chaser half-radius rule.
public class LureBiteBrain
{
    [System.Serializable]
    public struct Settings
    {
        [Header("Lure Movement Awareness")]
        [Tooltip("Awareness-radius multiplier applied to every fish while the lure recently moved. TP: ×1.5.")]
        public float movingRadiusMultiplier;
        [Tooltip("Seconds one yank counts as 'lure is moving' (TP: ~30 frames).")]
        public float movedDurationPerYank;
        [Tooltip("While cranking the reel the moved timer is continuously topped up to this (TP: spinner refreshed ~4 frames). Short, so the moving bonus dies almost immediately when the crank stops.")]
        public float crankMovedTopUp;
        [Tooltip("If the lure LANDS within this distance of a fish, that fish strikes instantly — no roll (TP: casting onto a fish bites immediately). Keep small so only precise casts are rewarded.")]
        public float directHitRadius;

        [Header("Chasers")]
        [Tooltip("How many hovering fish actively roll for bites at once (TP caps chasers at 2). The rest just school around the lure.")]
        public int maxActiveChasers;
        [Tooltip("Distance from the lure at which an approaching chaser counts as 'in the ring' and starts its bite/patience timers. Keep above followerHoverDistance.")]
        public float ringDistance;

        [Header("Bite Timers (seconds)")]
        [Tooltip("Bite-roll delay armed when a chaser first reaches the ring (TP: rnd(100) frames).")]
        public float arrivalBiteDelayMin;
        public float arrivalBiteDelayMax;
        [Tooltip("Bite-roll delay re-armed by lure movement while the timer is idle (TP: rnd(30)+10 frames). Each twitch literally buys one dice roll.")]
        public float rearmBiteDelayMin;
        public float rearmBiteDelayMax;
        [Tooltip("How long a chaser stays interested without lure movement (TP: rnd(200)+100 frames). Movement refreshes it.")]
        public float patienceMin;
        public float patienceMax;
        [Tooltip("After getting bored, the fish ignores the lure for this long (avoid flag), then may re-notice it.")]
        public float boredCooldown;

        [Header("Bite Chance")]
        [Tooltip("Strike probability per roll while the lure sits still (TP: 0.15).")]
        [Range(0f, 1f)] public float baseBiteChance;
        [Tooltip("Strike probability per roll while the lure recently moved (TP: 0.3, with the still roll as fallback — combined here).")]
        [Range(0f, 1f)] public float movingBiteChance;
        [Tooltip("Chance multiplier at Night (TP: ×1.5 outside midday hours).")]
        public float nightChanceMultiplier;
        [Tooltip("Chance multiplier in Rainy/Stormy weather (TP: ×1.75 while raining).")]
        public float rainChanceMultiplier;
        [Tooltip("FishPreset.catchProbability equal to this value is neutral (×1). Above = more eager than average, below = warier (TP gave the catfish ×2).")]
        [Range(0.05f, 1f)] public float neutralCatchProbability;
    }

    private enum Phase { Approaching, InRing, Striking }

    private class Chaser
    {
        public FishRipple fish;
        public Phase phase;
        public float biteTimer;   // > 0 = armed and counting down; <= 0 = idle until re-armed
        public bool timerArmed;
        public float patience;
    }

    private readonly System.Action<FishRipple> onGrabStart;
    private readonly System.Action<FishRipple> onGrabReleased;
    private readonly List<Chaser> chasers = new List<Chaser>();
    private readonly List<FishRipple> boredFish = new List<FishRipple>();
    private readonly List<float> boredTimers = new List<float>();

    private float movedTimer;

    // onGrabStart fires when a striking fish clamps onto the lure (the reaction window opens);
    // onGrabReleased fires when it spits the lure on a missed bite. The zone wires both — the
    // grab itself, and the catch on a timely response, are committed there.
    public LureBiteBrain(System.Action<FishRipple> onGrabStart, System.Action<FishRipple> onGrabReleased)
    {
        this.onGrabStart = onGrabStart;
        this.onGrabReleased = onGrabReleased;
    }

    // Zone calls this the moment the lure splashes into its trigger (after wiring the bobber
    // transform onto the fish, before its splash-scare sweep). The splash counts as lure
    // movement, and a direct hit — landing within directHitRadius of a fish — makes the
    // nearest such fish strike instantly.
    public void OnSplashdown(in Settings s, List<FishRipple> fish, Vector3 splashPos)
    {
        movedTimer = Mathf.Max(movedTimer, s.movedDurationPerYank);

        if (fish == null || s.directHitRadius <= 0f) return;

        FishRipple best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < fish.Count; i++)
        {
            FishRipple candidate = fish[i];
            if (candidate == null || candidate.preset == null) continue;
            if (!candidate.preset.RespondsToLure) continue;
            if (candidate.CurrentState != FishRipple.FishState.Wandering
                && candidate.CurrentState != FishRipple.FishState.Attracted) continue;

            float d = FishMovementHelpers.GetHorizontalDistance(candidate.transform.position, splashPos);
            if (d <= s.directHitRadius && d < bestDist)
            {
                bestDist = d;
                best = candidate;
            }
        }

        if (best == null) return;

        best.StartLureStrike(onGrabStart, onGrabReleased);
        if (best.CurrentState == FishRipple.FishState.Striking)
        {
            chasers.Add(new Chaser { fish = best, phase = Phase.Striking });
        }
    }

    // Zone forwards FishingEvents.OnLureTugged (one yank = one twitch).
    public void OnLureTugged(in Settings s)
    {
        movedTimer = Mathf.Max(movedTimer, s.movedDurationPerYank);
    }

    // Full reset on reel-in / cancel / lure leaving the zone. Striking fish drop back to
    // wandering; bored fish are freed (a fresh cast is a fresh start). The zone's own
    // scatter/scare sweeps handle the hovering school.
    public void ResetState(List<FishRipple> allFish)
    {
        for (int i = 0; i < chasers.Count; i++)
        {
            if (chasers[i].fish != null && chasers[i].phase == Phase.Striking)
                chasers[i].fish.CancelLureStrike();
        }
        chasers.Clear();

        for (int i = 0; i < boredFish.Count; i++)
        {
            if (boredFish[i] != null) boredFish[i].SetAvoidBobber(false);
        }
        boredFish.Clear();
        boredTimers.Clear();

        if (allFish != null)
        {
            for (int i = 0; i < allFish.Count; i++)
            {
                if (allFish[i] != null) allFish[i].SetAwarenessScale(1f);
            }
        }

        movedTimer = 0f;
    }

    public void Tick(float dt, List<FishRipple> fish, BobberController lure, bool crankActive, in Settings s)
    {
        if (lure == null || fish == null) return;

        movedTimer = Mathf.Max(0f, movedTimer - dt);
        if (crankActive) movedTimer = Mathf.Max(movedTimer, s.crankMovedTopUp);

        bool lureMoved = movedTimer > 0f;

        // NOTICE: a moving lure is visible from farther away.
        float awareness = lureMoved ? s.movingRadiusMultiplier : 1f;
        for (int i = 0; i < fish.Count; i++)
        {
            if (fish[i] != null) fish[i].SetAwarenessScale(awareness);
        }

        TickBoredCooldowns(dt);
        PruneChasers();
        RecruitChasers(fish, lure, in s);
        TickChasers(dt, lure, lureMoved, in s);
    }

    private void TickBoredCooldowns(float dt)
    {
        for (int i = boredFish.Count - 1; i >= 0; i--)
        {
            if (boredFish[i] == null)
            {
                boredFish.RemoveAt(i);
                boredTimers.RemoveAt(i);
                continue;
            }
            boredTimers[i] -= dt;
            if (boredTimers[i] <= 0f)
            {
                boredFish[i].SetAvoidBobber(false);
                boredFish.RemoveAt(i);
                boredTimers.RemoveAt(i);
            }
        }
    }

    // Drop chasers whose fish died, got scared, or otherwise left the expected state. The
    // FishRipple side already handles drift-out (followers stop following beyond the
    // awareness radius), so a state check covers TP's 2×-radius abandon rule too.
    private void PruneChasers()
    {
        for (int i = chasers.Count - 1; i >= 0; i--)
        {
            Chaser c = chasers[i];
            bool valid = c.fish != null
                && (c.phase == Phase.Striking
                    ? (c.fish.CurrentState == FishRipple.FishState.Striking
                       || c.fish.CurrentState == FishRipple.FishState.Grabbing)
                    : c.fish.CurrentState == FishRipple.FishState.Attracted);
            if (!valid) chasers.RemoveAt(i);
        }
    }

    private void RecruitChasers(List<FishRipple> fish, BobberController lure, in Settings s)
    {
        while (chasers.Count < s.maxActiveChasers)
        {
            FishRipple best = null;
            float bestDist = float.MaxValue;
            Vector3 lurePos = lure.transform.position;

            for (int i = 0; i < fish.Count; i++)
            {
                FishRipple candidate = fish[i];
                if (candidate == null || candidate.preset == null) continue;
                if (!candidate.preset.RespondsToLure) continue;
                if (candidate.CurrentState != FishRipple.FishState.Attracted) continue;
                if (IsChaser(candidate) || boredFish.Contains(candidate)) continue;

                float d = FishMovementHelpers.GetHorizontalDistance(candidate.transform.position, lurePos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = candidate;
                }
            }

            if (best == null) return;

            chasers.Add(new Chaser
            {
                fish = best,
                phase = Phase.Approaching,
                timerArmed = false,
                patience = Random.Range(s.patienceMin, s.patienceMax),
            });
        }
    }

    private bool IsChaser(FishRipple fish)
    {
        for (int i = 0; i < chasers.Count; i++)
        {
            if (chasers[i].fish == fish) return true;
        }
        return false;
    }

    private void TickChasers(float dt, BobberController lure, bool lureMoved, in Settings s)
    {
        Vector3 lurePos = lure.transform.position;

        for (int i = chasers.Count - 1; i >= 0; i--)
        {
            Chaser c = chasers[i];
            if (c.phase == Phase.Striking) continue; // FishRipple drives the charge; contact or scare resolves it

            float dist = FishMovementHelpers.GetHorizontalDistance(c.fish.transform.position, lurePos);

            if (c.phase == Phase.Approaching && dist <= s.ringDistance)
            {
                c.phase = Phase.InRing;
                c.biteTimer = Random.Range(s.arrivalBiteDelayMin, s.arrivalBiteDelayMax);
                c.timerArmed = true;
                c.patience = Random.Range(s.patienceMin, s.patienceMax);
            }

            // TP-faithful: patience doesn't drain during the swim-in (TP re-rolled it every
            // approach frame) — an interested fish always reaches the ring. An approach only
            // breaks via drift-out or a scare, both already handled by FishRipple + pruning.
            if (c.phase == Phase.Approaching) continue;

            if (c.phase == Phase.InRing)
            {
                // Each lure movement while the timer is idle re-arms one bite roll and buys
                // more patience — TP's "wiggle, pause, wiggle" rhythm in two lines.
                if (lureMoved && !c.timerArmed)
                {
                    c.biteTimer = Random.Range(s.rearmBiteDelayMin, s.rearmBiteDelayMax);
                    c.timerArmed = true;
                    c.patience = Random.Range(s.patienceMin, s.patienceMax);
                }

                if (c.timerArmed)
                {
                    c.biteTimer -= dt;
                    if (c.biteTimer <= 0f)
                    {
                        c.timerArmed = false;
                        if (Random.value < ComputeBiteChance(c.fish, lureMoved, in s))
                        {
                            c.fish.StartLureStrike(onGrabStart, onGrabReleased);
                            if (c.fish.CurrentState == FishRipple.FishState.Striking)
                            {
                                c.phase = Phase.Striking;
                                continue;
                            }
                        }
                    }
                }
            }

            c.patience -= dt;
            if (c.patience <= 0f)
            {
                c.fish.StopFollowing();
                c.fish.SetAvoidBobber(true);
                boredFish.Add(c.fish);
                boredTimers.Add(s.boredCooldown);
                chasers.RemoveAt(i);
            }
        }
    }

    private float ComputeBiteChance(FishRipple fish, bool lureMoved, in Settings s)
    {
        // TP rolls the moving chance first and falls back to the still roll — two chances,
        // combined here into one equivalent probability.
        float p = lureMoved
            ? s.movingBiteChance + (1f - s.movingBiteChance) * s.baseBiteChance
            : s.baseBiteChance;

        if (WorldStateManager.Instance != null)
        {
            switch (WorldStateManager.Instance.GetActiveFishingWeather())
            {
                case WeatherType.Night:
                    p *= s.nightChanceMultiplier;
                    break;
                case WeatherType.Rainy:
                case WeatherType.Stormy:
                    p *= s.rainChanceMultiplier;
                    break;
            }
        }

        if (fish.preset != null && s.neutralCatchProbability > 0.001f)
        {
            p *= fish.preset.catchProbability / s.neutralCatchProbability;
        }

        return p;
    }

    // TP holds the lure in the fish's mouth for a size-scaled window before spitting —
    // bigger fish spit faster (d_a_mg_fish.cpp hold-time table). Used by the rod to scale
    // its hook-set reaction window on lure bites.
    public static float ReactionWindowMultiplier(SizeClass size)
    {
        switch (size)
        {
            case SizeClass.Tiny:   return 1.25f;
            case SizeClass.Small:  return 1.1f;
            case SizeClass.Medium: return 1f;
            case SizeClass.Large:  return 0.8f;
            case SizeClass.Huge:   return 0.65f;
            default:               return 1f;
        }
    }
}
