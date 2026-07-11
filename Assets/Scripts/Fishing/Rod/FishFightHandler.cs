using UnityEngine;

// The "you ARE the fish" fight: from the hook-set until the catch, the controls drive the
// hooked fish itself (the bobber rigidbody carrying the HookedFish visual). Tank controls
// (A/D, left stick X) steer the fish's swim heading; it always swims forward along that
// heading; holding reel cranks in line, which drags the fish head-first WHEREVER IT IS
// FACING — toward the bank when the player has it aligned, but just as happily away from
// the player when it isn't. In short, frequent fight bursts the fish wrenches its heading
// toward a random escape angle on the far side (an arc away from the player) at a turn rate
// that overpowers the player's steering, so the tank controls are constantly challenged by
// the fish's own realignment. Reeling while it faces the wrong way feeds it line — keep it
// up and the line snaps.
//
// Win/lose is purely spatial: the catch closes when the fish reaches the waterbank (within
// catchDistance of the reel target), and the line snaps (fish escapes) once the fish gets
// further from the player than the hook distance plus the allowed slack. The live tension
// toward that snap is broadcast via FishingEvents.OnLineTensionChanged so the rope can
// telegraph it (VerletRope's snap flicker).
//
// Pure logic class owned and ticked (FixedUpdate) by FishingRodController — same pattern as
// LureReelHandler. It drives the rigidbody's horizontal velocity directly (buoyancy keeps
// owning Y) and never touches the old BobberController struggle forces, which stay dormant
// for the whole fight.
public class FishFightHandler
{
    public enum Outcome { None, Caught, Escaped }

    // How far ahead of the fish (m, along its horizontal motion) the water's edge is probed.
    // The drive stops before the fish can push its collider onto the bank slope.
    private const float EdgeProbeDistance = 0.6f;
    // The fish's body must stay at least this far under the surface — any upward velocity
    // (slope contacts, solver pops) is cancelled above this line, so the fight can never
    // ride the fish up and out of the water.
    private const float MinSubmergence = 0.35f;

    [System.Serializable]
    public struct Settings
    {
        [Tooltip("Swim speed (m/s) of the fish along its heading while it is calm.")]
        public float swimSpeed;
        [Tooltip("Swim speed (m/s) during a fight burst — the wrench toward its escape angle carries real power, so misaligned moments cost line fast.")]
        public float fightSwimSpeed;
        [Tooltip("How fast (deg/s) full tank-steer input turns the fish's heading while it is calm.")]
        public float steerTurnRate;
        [Tooltip("How fast (deg/s) the fish turns its own heading toward its escape angle during a fight burst. Kept above steerTurnRate so the burst genuinely overrides the player.")]
        public float fightTurnRate;
        [Tooltip("Fraction of the player's steering that still works DURING a fight burst. 0 = the fish fully hijacks the controls; ~0.3 lets skilled counter-steering soften how far the burst turns it away.")]
        [Range(0f, 1f)] public float fightSteerAuthority;
        [Tooltip("Max random deviation (deg) to either side of dead-away-from-player for the escape angle a fight burst steers toward. Bigger = the fish curves off in wider arcs instead of powering straight away.")]
        [Range(0f, 120f)] public float fightArcDegrees;
        [Tooltip("Calm stretch length between fight bursts, picked at random in this [min, max] range (seconds). Big fish shorten it (see Size Intensity).")]
        public Vector2 calmDurationRange;
        [Tooltip("Fight burst length, picked at random in this [min, max] range (seconds). Deliberately short — the challenge is the constant re-steering, not enduring long hijacks. Big fish stretch it (see Size Intensity).")]
        public Vector2 fightDurationRange;
        [Tooltip("How much harder the LARGEST fish fights: burst length, fight turn rate and fight swim speed are scaled by (1 + this), and calm stretches are divided by it. 0 = size doesn't matter.")]
        [Range(0f, 2f)] public float sizeIntensity;
        [Tooltip("The catch cannot close before the fight has run this long (seconds), so a fish hooked right at the bank still puts up a token struggle.")]
        public float minFightSeconds;
    }

    public bool InFightBurst => inFightBurst;

    private Rigidbody rb;
    private HookedFishController fishVisual;
    private float headingYaw;
    private bool inFightBurst;
    private float phaseTimer;
    private float burstArcOffset;    // this burst's escape angle, relative to dead-away (deg)
    private float fightScale = 1f;   // size-based difficulty multiplier (>= 1)
    private float hookDistance;      // horizontal player→fish distance at the hook-set
    private float slackLength;       // extra line beyond hookDistance before the snap
    private float pullCap;           // per-species leash: max line the fish itself can take (m)
    private float elapsed;
    private bool wasReeling;

    // Called at the hook-set. Seeds the heading pointing away from the player (the natural
    // pull-against-the-line pose the fish was already holding), sizes the difficulty, and
    // fixes the line-break distance at hook distance + slack so every fight has stakes
    // regardless of how long the cast was.
    public void Begin(BobberController bobber, FishPreset preset, Vector3 playerPos,
                      float lineSlackBeforeBreak, in Settings s)
    {
        rb = bobber != null ? bobber.GetComponent<Rigidbody>() : null;
        fishVisual = bobber != null && bobber.ActiveFishModel != null
            ? bobber.ActiveFishModel.GetComponent<HookedFishController>() : null;

        Vector3 away = bobber != null ? bobber.transform.position - playerPos : Vector3.forward;
        away.y = 0f;
        float awayYaw = away.sqrMagnitude > 0.001f
            ? Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg : 0f;

        // Seamless bite → fight: the steering heading starts as the fish's CURRENT facing, so
        // it follows through whatever direction the bite left it moving in. Seeding "away from
        // the player" here snapped the fish onto a new heading the instant the hook set. Its
        // own fight bursts realign it away soon enough.
        headingYaw = fishVisual != null ? fishVisual.CurrentBaseYaw : awayYaw;

        float sizeT = preset != null ? NormalizedSize(preset.sizeClass) : 0.5f;
        fightScale = 1f + s.sizeIntensity * sizeT;

        hookDistance = Mathf.Sqrt(away.sqrMagnitude);
        slackLength = Mathf.Max(1f, lineSlackBeforeBreak);

        // Per-species leash from the FishPreset asset; 0/unset falls back to a size-based
        // default so a new preset still fights sensibly without tuning.
        pullCap = preset != null && preset.fightPullDistance > 0f
            ? preset.fightPullDistance
            : Mathf.Lerp(1.5f, 6f, sizeT);
        elapsed = 0f;
        wasReeling = false;

        // Every fight opens calm, so the player gets a beat to find the controls and the fish
        // carries on from the pose the bite left it in — an instant opening burst yanked it
        // off in a new direction the moment the hook set, which read as a teleport. The stiff
        // body chain (BodyStraightenRate + the hook-set chain snap) keeps it straight without
        // needing to bolt.
        inFightBurst = false;
        phaseTimer = RandomCalmDuration(in s);
        if (bobber != null) bobber.fightBurst = false;
        FishingEvents.OnFishStruggleStateChanged?.Invoke(false);
        FishingEvents.OnLineTensionChanged?.Invoke(0f);
    }

    // One FixedUpdate step of the fight. steer is the tank input (-1..1), reelHeld the level
    // state of the reel button. reelTarget is the waterline point in front of the player that
    // the catch measures to (FishingRodController.ComputeReelTarget); reelSpeed is the extra
    // speed cranking adds along the fish's FACING.
    public Outcome Tick(BobberController bobber, Vector3 reelTarget, Vector3 playerPos,
                        bool reelHeld, float steer, in Settings s,
                        float reelSpeed, float catchDistance, LayerMask waterMask)
    {
        if (bobber == null) return Outcome.None;

        float dt = Time.fixedDeltaTime;
        elapsed += dt;

        // Calm ↔ fight burst flips. The burst state doubles as the "struggle" the rod bend,
        // fight audio and the hooked visual's thrash all key off. Each burst rolls a fresh
        // escape angle somewhere in the away-from-player arc, so the fish curves off in a
        // different direction every time instead of powering straight away.
        phaseTimer -= dt;
        if (phaseTimer <= 0f)
        {
            inFightBurst = !inFightBurst;
            if (inFightBurst)
            {
                phaseTimer = Random.Range(s.fightDurationRange.x, s.fightDurationRange.y) * fightScale;
                burstArcOffset = Random.Range(-s.fightArcDegrees, s.fightArcDegrees);

                // Sporadic breach: at the top of a fight burst, roll a leap along the fish's
                // current heading. BobberController owns the chance (fightJumpChance), the
                // cooldown and the whole ballistic arc; while it is airborne the drive below is
                // skipped (jumping) so the leap plays out cleanly, then steering resumes on splash.
                Vector3 leapDir = Quaternion.Euler(0f, headingYaw, 0f) * Vector3.forward;
                bobber.TryFightJump(leapDir);
            }
            else
            {
                phaseTimer = RandomCalmDuration(in s);
            }
            bobber.fightBurst = inFightBurst;
            FishingEvents.OnFishStruggleStateChanged?.Invoke(inFightBurst);
        }

        // While the fish is mid-leap (a sporadic breach rolled at burst start, above) the
        // BobberController owns its ballistic arc — this handler must not overwrite the rigidbody
        // velocity or the depth guard would instantly cancel the upward launch. Read AFTER the
        // burst flip so a leap fired this very step is already seen as airborne.
        bool jumping = bobber.IsHookedFishJumping;

        // Tank steering — heading-relative, reduced (not zeroed) while the fish is fighting.
        float authority = inFightBurst ? s.fightSteerAuthority : 1f;
        headingYaw += steer * s.steerTurnRate * authority * dt;

        // The burst itself: the fish wrenches its heading toward this burst's escape angle
        // (dead-away from the player plus the rolled arc offset). This runs AFTER the
        // player's steer so at full deflection the net turn is the difference — the fish
        // wins, but counter-steering visibly slows it.
        Vector3 fromPlayer = bobber.transform.position - playerPos;
        fromPlayer.y = 0f;
        if (inFightBurst && fromPlayer.sqrMagnitude > 0.001f)
        {
            float awayYaw = Mathf.Atan2(fromPlayer.x, fromPlayer.z) * Mathf.Rad2Deg;
            headingYaw = Mathf.MoveTowardsAngle(headingYaw, awayYaw + burstArcOffset,
                                                s.fightTurnRate * fightScale * dt);
        }

        // Swim + reel: reeling cranks in line, which hauls the fish head-first along its
        // FACING — so it closes distance only when the player has it steered right, and
        // actively feeds a misaligned fish its escape. Horizontal velocity is overwritten
        // each step along the heading; Y is left to the bobber's own buoyancy.
        bool reelingNow = reelHeld && bobber.IsInWater && !jumping;
        if (rb != null && !rb.isKinematic && bobber.IsInWater && !jumping)
        {
            Vector3 fwd = Quaternion.Euler(0f, headingYaw, 0f) * Vector3.forward;
            float speed = (inFightBurst ? s.fightSwimSpeed * fightScale : s.swimSpeed)
                        + (reelingNow ? reelSpeed : 0f);
            Vector3 velocity = fwd * speed;

            // Per-species leash (FishPreset.fightPullDistance): past its pull limit the fish's
            // own swimming can take no more line — the outward part of its motion stalls, so it
            // hangs at the end of its pull, still free to swim sideways or back in. Only the
            // reel-fed portion may push it further out: that's the player cranking while the
            // fish faces the wrong way, which is the one route to the snap.
            float dist = fromPlayer.magnitude;
            if (dist >= hookDistance + pullCap && dist > 0.001f)
            {
                Vector3 outDir = fromPlayer / dist;
                float outward = Vector3.Dot(velocity, outDir);
                float allowedOutward = reelingNow
                    ? Mathf.Max(0f, Vector3.Dot(fwd, outDir) * reelSpeed) : 0f;
                if (outward > allowedOutward) velocity -= outDir * (outward - allowedOutward);
            }

            // Water-edge guard: a hooked fish never leaves the water. If the next step ahead of
            // the drive is over the bank (no water surface under the probe), the horizontal
            // drive stops — the fish noses the shore and holds until it (or the player) turns.
            // Without this, driving into a sloped bank pushed the collider up the slope and the
            // fish "swam" onto land.
            if (waterMask.value != 0)
            {
                Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
                if (horizontal.sqrMagnitude > 0.0001f)
                {
                    Vector3 probe = rb.position + horizontal.normalized * EdgeProbeDistance;
                    if (!FishingBobberPull.IsOverWater(probe, waterMask))
                    {
                        velocity.x = 0f;
                        velocity.z = 0f;
                    }
                }
            }

            // Depth guard: Y normally belongs to the bobber's buoyancy (it holds the fight
            // depth), but slope contacts can inject upward velocity — cancel any rise once the
            // body is near the surface so nothing can carry it out of the water vertically.
            float verticalVel = rb.linearVelocity.y;
            if (verticalVel > 0f && rb.position.y > bobber.WaterSurfaceY - MinSubmergence)
                verticalVel = 0f;

            rb.linearVelocity = new Vector3(velocity.x, verticalVel, velocity.z);
        }
        if (reelingNow != wasReeling)
        {
            if (reelingNow) FishingEvents.OnStartReelingDuringFight?.Invoke();
            else FishingEvents.OnStopReelingDuringFight?.Invoke();
            wasReeling = reelingNow;
        }

        // The hooked visual renders the driven heading truthfully (no away-pull, no sweep);
        // the steer input doubles as the rod-direction feedback for the player lean/rod bend.
        if (fishVisual != null) fishVisual.SetDrivenHeading(headingYaw);
        FishingEvents.OnRodDirectionUpdate?.Invoke(Mathf.Clamp(steer, -1f, 1f));

        // Live snap tension: 0 while the fish is at (or inside) its hook distance, 1 the
        // instant all the slack is out. The rope flickers on this (VerletRope), so the player
        // reads exactly how close they are to losing the fish.
        float tension = Mathf.Clamp01((fromPlayer.magnitude - hookDistance) / slackLength);
        FishingEvents.OnLineTensionChanged?.Invoke(tension);

        // Catch / escape are settled only while the fish is in the water — never mid-leap, so an
        // airborne fish can't be "landed" nor snap the line at the top of its arc.
        // Catch: the fish reached the waterbank in front of the player.
        if (!jumping && elapsed >= s.minFightSeconds && bobber.IsInWater
            && FishingBobberPull.IsWithinCatchRange(bobber, reelTarget, catchDistance))
        {
            return Outcome.Caught;
        }

        // Escape: the fish took all the slack — the line snaps (the rope is fully invisible
        // at this point; it flicker-fades back in on the rod via VerletRope's recover ramp).
        if (!jumping && tension >= 1f)
        {
            return Outcome.Escaped;
        }

        return Outcome.None;
    }

    // Close out the fight (win, escape or cancel): clear the burst flag and settle any
    // still-open reeling/struggle event edges so no listener is left mid-state.
    public void End(BobberController bobber)
    {
        if (bobber != null) bobber.fightBurst = false;
        if (fishVisual != null) fishVisual.ClearDrivenHeading();
        if (wasReeling)
        {
            FishingEvents.OnStopReelingDuringFight?.Invoke();
            wasReeling = false;
        }
        if (inFightBurst)
        {
            inFightBurst = false;
            FishingEvents.OnFishStruggleStateChanged?.Invoke(false);
        }
        rb = null;
        fishVisual = null;
    }

    private float RandomCalmDuration(in Settings s)
        => Random.Range(s.calmDurationRange.x, s.calmDurationRange.y) / fightScale;

    // Maps a SizeClass to 0 (smallest) .. 1 (largest) regardless of how many classes exist.
    private static float NormalizedSize(SizeClass size)
    {
        int max = System.Enum.GetValues(typeof(SizeClass)).Length - 1;
        return max > 0 ? (int)size / (float)max : 0f;
    }
}
