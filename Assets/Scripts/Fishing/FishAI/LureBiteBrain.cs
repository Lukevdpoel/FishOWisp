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
        [Tooltip("How many fish may be interested in (hover around) the lure at once. The brain keeps the nearest this-many engaged and tells every other fish to keep clear, so the lure never gathers a crowd. Set 0 to leave hovering uncapped.")]
        public int maxHoverFish;
        [Tooltip("How many hovering fish actively roll for bites at once (TP caps chasers at 2). The rest just school around the lure. Keep ≤ maxHoverFish.")]
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
        [Tooltip("After a fish spits the lure on a missed reaction, NO chaser may roll a new strike for this long. Stops a second fish from instantly biting the moment the first one is missed — the whole school gets a beat. Set 0 to disable.")]
        public float postMissCooldown;
        [Tooltip("After ANY fish responds — a nibble OR a committed bite — NO chaser may roll again for " +
                 "this long, so the school doesn't take turns nibbling/biting the lure one after another. " +
                 "The lock is global (applies to every active fish, not just the one that responded). " +
                 "0/unset falls back to postMissCooldown.")]
        public float responseCooldown;

        [Header("Bite Chance")]
        [Tooltip("Strike probability per roll while the lure sits still (TP: 0.15).")]
        [Range(0f, 1f)] public float baseBiteChance;
        [Tooltip("Strike probability per roll while the lure recently moved (TP: 0.3, with the still roll as fallback — combined here).")]
        [Range(0f, 1f)] public float movingBiteChance;
        [Tooltip("Chance multiplier at Night (TP: ×1.5 outside midday hours).")]
        public float nightChanceMultiplier;
        [Tooltip("FishPreset.catchProbability equal to this value is neutral (×1). Above = more eager than average, below = warier (TP gave the catfish ×2).")]
        [Range(0.05f, 1f)] public float neutralCatchProbability;
        [Tooltip("When a roll succeeds (the fish responds to the lure), this is the chance it's a full " +
                 "BITE — a dash-grab that opens the reaction window. Otherwise it's a NIBBLE: the fish " +
                 "darts through the lure, twitches it, and stays interested, so the player has to keep " +
                 "tugging to re-roll for a real bite. A bite always overtakes a nibble rolled the same " +
                 "moment. 1 (or 0/unset, treated as 1) = every response is a bite, the old behaviour.")]
        [Range(0f, 1f)] public float biteCommitChance;

        [Header("Popper Preference")]
        [Tooltip("Awareness-radius multiplier applied to a popper-preferring fish (FishPreset.prefersPopper) " +
                 "while a Popper lure is equipped — they notice the splashing popper from farther and swim " +
                 "over. Stacks with the moving-lure bonus. 1 = no extra pull.")]
        public float popperPreferenceRadiusMultiplier;
        [Tooltip("Bite-chance multiplier applied to a popper-preferring fish while a Popper lure is equipped " +
                 "— they strike the popper more readily. 1 = no bonus, 2 = twice as likely per roll.")]
        public float popperPreferenceBiteMultiplier;
    }

    private enum Phase { Approaching, InRing, Striking }

    // Priority tiers for filling the capped hover set: a committed biter is never dropped, a fish
    // already hovering keeps its slot over a wandering newcomer, and free slots go to the nearest
    // wandering fish. This keeps the interested pair stable instead of churning as fish mill about.
    private enum Tier { Committed, Hovering, Wandering }

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
    // The fish currently allowed to be interested in the lure (rebuilt each tick by EnforceHoverCap).
    private readonly List<FishRipple> hoverSet = new List<FishRipple>();
    // Fish that rolled a NIBBLE this tick. Their tease passes are deferred until the whole chaser
    // sweep is done, so a bite committed by any chaser the same tick can overtake (drop) them.
    private readonly List<FishRipple> nibbleCandidates = new List<FishRipple>();

    private float movedTimer;

    // True while a Popper-style lure is equipped (set each Tick). Gates the popper-preference
    // awareness/bite bonuses so they only apply to popper-preferring fish with the popper out.
    private bool popperEquipped;

    // While > 0 no chaser may roll a new strike: set when a grab is missed (the school is briefly
    // startled) so a second fish can't instantly pounce on the lure the moment the first is spat out.
    private float biteLockTimer;

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
        // Splashdown happens outside the Tick loop, so refresh the popper flag here too — the
        // direct-hit strike below must respect the popper split (only popper fish can be struck
        // by a popper landing on them). The next Tick's scare sweep chases the rest off.
        popperEquipped = BobberInventory.IsPopperEquipped;
        movedTimer = Mathf.Max(movedTimer, s.movedDurationPerYank);

        if (fish == null || s.directHitRadius <= 0f) return;

        FishRipple best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < fish.Count; i++)
        {
            FishRipple candidate = fish[i];
            if (candidate == null || candidate.preset == null) continue;
            if (!RespondsToEquippedLure(candidate.preset)) continue;
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

    // Zone calls this when a grab lapsed with no player response (a missed bite). Locks out every
    // chaser's bite roll for postMissCooldown so the next fish can't instantly take its place.
    public void OnBiteMissed(in Settings s)
    {
        biteLockTimer = Mathf.Max(biteLockTimer, s.postMissCooldown);
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
        hoverSet.Clear();

        if (allFish != null)
        {
            for (int i = 0; i < allFish.Count; i++)
            {
                if (allFish[i] != null) allFish[i].SetAwarenessScale(1f);
            }
        }

        movedTimer = 0f;
        biteLockTimer = 0f;
    }

    public void Tick(float dt, List<FishRipple> fish, BobberController lure, bool crankActive, bool isPopper, in Settings s)
    {
        if (lure == null || fish == null) return;

        popperEquipped = isPopper;
        movedTimer = Mathf.Max(0f, movedTimer - dt);
        biteLockTimer = Mathf.Max(0f, biteLockTimer - dt);
        if (crankActive) movedTimer = Mathf.Max(movedTimer, s.crankMovedTopUp);

        bool lureMoved = movedTimer > 0f;

        // POPPER SCARE: while the popper is moving (reeling or wiggling — both feed movedTimer),
        // its aggressive pop chases off every fish that isn't a popper-lover, bobber species
        // included. Gated on lureMoved so a still popper is safe to drift past; already-fleeing
        // fish are skipped inside the sweep.
        if (popperEquipped && lureMoved) ScareNonPopperFish(fish);

        // NOTICE: a moving lure is visible from farther away, and a popper-preferring fish notices
        // the splashing popper from farther still (its bonus stacks on top of the moving bonus).
        float awareness = lureMoved ? s.movingRadiusMultiplier : 1f;
        for (int i = 0; i < fish.Count; i++)
        {
            if (fish[i] == null) continue;
            float a = awareness;
            // > 0 guard: a setting left at 0 (e.g. an old serialized zone before this field
            // existed) must read as "no bonus" (×1), never multiply awareness down to nothing.
            if (popperEquipped && fish[i].preset != null && fish[i].preset.prefersPopper
                && s.popperPreferenceRadiusMultiplier > 0f)
                a *= s.popperPreferenceRadiusMultiplier;
            fish[i].SetAwarenessScale(a);
        }

        TickBoredCooldowns(dt);
        EnforceHoverCap(fish, lure, in s);
        PruneChasers();
        RecruitChasers(fish, lure, in s);
        TickChasers(dt, lure, lureMoved, in s);
    }

    // Cap how many fish may be interested in the lure at once. Each tick we rebuild the allowed
    // "hover set" (nearest first, committed/already-hovering fish kept for stability) and tell
    // everyone outside it to keep clear — so the lure draws an interested pair, never a crowd.
    private void EnforceHoverCap(List<FishRipple> fish, BobberController lure, in Settings s)
    {
        if (s.maxHoverFish <= 0) return; // 0 = leave hovering uncapped (old behaviour)

        Vector3 lurePos = lure.transform.position;
        hoverSet.Clear();
        FillHoverTier(fish, lurePos, s.maxHoverFish, Tier.Committed);
        FillHoverTier(fish, lurePos, s.maxHoverFish, Tier.Hovering);
        FillHoverTier(fish, lurePos, s.maxHoverFish, Tier.Wandering);

        for (int i = 0; i < fish.Count; i++)
        {
            FishRipple f = fish[i];
            if (f == null || f.preset == null || !RespondsToEquippedLure(f.preset)) continue;
            // Bored fish keep clear on their own cooldown; scared/despawning fish are mid-exit.
            if (boredFish.Contains(f)) continue;
            FishRipple.FishState st = f.CurrentState;
            if (st == FishRipple.FishState.Scared || st == FishRipple.FishState.Despawning) continue;

            if (hoverSet.Contains(f))
            {
                f.SetAvoidBobber(false);
            }
            else
            {
                f.StopFollowing();      // drop it out of any hover it had snuck into
                f.SetAvoidBobber(true); // and keep it away so it can't re-attract
            }
        }
    }

    // Add fish matching the given tier to the hover set, nearest first, until the cap is reached.
    private void FillHoverTier(List<FishRipple> fish, Vector3 lurePos, int cap, Tier tier)
    {
        while (hoverSet.Count < cap)
        {
            FishRipple best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < fish.Count; i++)
            {
                FishRipple f = fish[i];
                if (f == null || f.preset == null || !RespondsToEquippedLure(f.preset)) continue;
                if (boredFish.Contains(f) || hoverSet.Contains(f)) continue;
                if (!TierMatches(f.CurrentState, tier)) continue;

                float d = FishMovementHelpers.GetHorizontalDistance(f.transform.position, lurePos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = f;
                }
            }
            if (best == null) return;
            hoverSet.Add(best);
        }
    }

    private static bool TierMatches(FishRipple.FishState st, Tier tier)
    {
        switch (tier)
        {
            case Tier.Committed: return st == FishRipple.FishState.Striking || st == FishRipple.FishState.Grabbing;
            case Tier.Hovering:  return st == FishRipple.FishState.Attracted;
            case Tier.Wandering: return st == FishRipple.FishState.Wandering;
            default:             return false;
        }
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
                    // An in-ring chaser is normally Attracted; while it's mid lure-nibble pass its
                    // fish briefly reads Striking, so keep it (it returns to hovering on its own).
                    : (c.fish.CurrentState == FishRipple.FishState.Attracted || c.fish.IsLureNibbling));
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
                if (!RespondsToEquippedLure(candidate.preset)) continue;
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

    // Whether a species engages the CURRENTLY equipped lure. The popper splits the lure fish:
    // with the popper out only popper-lovers respond; with the plain lure out only non-lovers do.
    // Mirrors BobberInventory.PresetRespondsToEquippedTackle for the lure path (popperEquipped is
    // refreshed each Tick and on splashdown).
    private bool RespondsToEquippedLure(FishPreset preset)
    {
        if (preset == null || !preset.RespondsToLure) return false;
        return popperEquipped ? preset.prefersPopper : !preset.prefersPopper;
    }

    // Scare every non-popper fish out of the water while the popper is moving. Popper-lovers stay;
    // fish already fleeing or despawning are mid-exit, so leave them be (Scare re-fleeing a fleer
    // would just re-jitter it in place).
    private void ScareNonPopperFish(List<FishRipple> fish)
    {
        for (int i = 0; i < fish.Count; i++)
        {
            FishRipple f = fish[i];
            if (f == null || f.preset == null || f.preset.prefersPopper) continue;
            FishRipple.FishState st = f.CurrentState;
            if (st == FishRipple.FishState.Scared || st == FishRipple.FishState.Despawning) continue;
            f.Scare();
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

    private bool AnyStriking()
    {
        for (int i = 0; i < chasers.Count; i++)
        {
            if (chasers[i].phase == Phase.Striking) return true;
        }
        return false;
    }

    private void TickChasers(float dt, BobberController lure, bool lureMoved, in Settings s)
    {
        Vector3 lurePos = lure.transform.position;
        nibbleCandidates.Clear();

        // Global lockout applied after ANY response (nibble or bite) so the school can't take turns
        // pecking the lure — every active fish waits this long before the next roll. Falls back to
        // the post-miss cooldown when unset.
        float responseCooldown = s.responseCooldown > 0f ? s.responseCooldown : s.postMissCooldown;

        // Only one fish may ever be committed to the lure at a time. A strike is blocked while
        // another chaser is already dashing in (AnyStriking) or while the post-miss lock is up —
        // and once we launch one this tick, the local flag stops a second firing the same frame.
        bool strikeBlocked = biteLockTimer > 0f || AnyStriking();

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

                // While another fish owns the bite (or the post-miss lock is up) the timer is
                // frozen — the chaser hovers, armed, and takes its turn once the lure is free. It's
                // also frozen while this fish is mid lure-nibble pass, so a re-tug can't fire a
                // second roll until the tease finishes and it's hovering again.
                if (c.timerArmed && !strikeBlocked && !c.fish.IsLureNibbling)
                {
                    c.biteTimer -= dt;
                    if (c.biteTimer <= 0f)
                    {
                        c.timerArmed = false;
                        if (Random.value < ComputeBiteChance(c.fish, lureMoved, in s))
                        {
                            // The fish responds. Decide bite vs. nibble. An unset (0) commit chance
                            // reads as 1 (always bite) so old serialized zones keep their behaviour.
                            float commit = s.biteCommitChance > 0f ? s.biteCommitChance : 1f;
                            if (Random.value < commit)
                            {
                                c.fish.StartLureStrike(onGrabStart, onGrabReleased);
                                if (c.fish.CurrentState == FishRipple.FishState.Striking)
                                {
                                    c.phase = Phase.Striking;
                                    strikeBlocked = true; // this fish now owns the lure for this tick onward
                                    // Lock the whole school after this response so no fish takes a turn.
                                    biteLockTimer = Mathf.Max(biteLockTimer, responseCooldown);
                                    continue;
                                }
                            }
                            else
                            {
                                // Nibble: stay interested (refresh patience) and defer the tease pass
                                // until the sweep is done, so a bite this tick can overtake it.
                                c.patience = Random.Range(s.patienceMin, s.patienceMax);
                                nibbleCandidates.Add(c.fish);
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

        // Bite priority: if any chaser committed a strike this tick (or one was already ongoing /
        // the post-miss lock is up), the bite overtakes — drop every nibble rolled this tick. Only
        // when the lure is free do the teasing passes actually fire.
        if (!strikeBlocked && nibbleCandidates.Count > 0)
        {
            for (int i = 0; i < nibbleCandidates.Count; i++)
                nibbleCandidates[i]?.StartLureNibble();
            // Same global lockout as a bite — after a nibble the whole school waits, so fish don't
            // peck the lure in turns.
            biteLockTimer = Mathf.Max(biteLockTimer, responseCooldown);
        }
        nibbleCandidates.Clear();
    }

    private float ComputeBiteChance(FishRipple fish, bool lureMoved, in Settings s)
    {
        // TP rolls the moving chance first and falls back to the still roll — two chances,
        // combined here into one equivalent probability.
        float p = lureMoved
            ? s.movingBiteChance + (1f - s.movingBiteChance) * s.baseBiteChance
            : s.baseBiteChance;

        if (WorldStateManager.Instance != null && WorldStateManager.Instance.IsNight)
        {
            p *= s.nightChanceMultiplier;
        }

        if (fish.preset != null && s.neutralCatchProbability > 0.001f)
        {
            p *= fish.preset.catchProbability / s.neutralCatchProbability;
        }

        // Popper preference: a fish that loves the popper strikes it more readily. The > 0 guard
        // keeps an unset (0) multiplier as "no bonus" (×1) instead of zeroing the bite chance.
        if (popperEquipped && fish.preset != null && fish.preset.prefersPopper
            && s.popperPreferenceBiteMultiplier > 0f)
        {
            p *= s.popperPreferenceBiteMultiplier;
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
