using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Part of FishRipple (partial class). Serialized fields live in FishRipple.cs.
public partial class FishRipple
{
    // Wandering fish that haven't moved meaningfully for several seconds are wedged in
    // geometry (spawned inside a rock, or terrain that isn't on the obstacle mask): swimming
    // can't free them because every heading is blocked, so relocate to a fresh valid point.
    // A fish sitting out a pause isn't stuck — full rests deliberately hold a dead stop for
    // longer than the rescue window, so the check only runs while the fish should be moving.
    private void UpdateStuckRescue()
    {
        if (currentState != FishState.Wandering || wanderPauseTimer > 0f)
        {
            stuckTimer = 0f;
            stuckAnchor = transform.position;
            return;
        }

        // NET displacement, not raw motion: a fish locked in a tight endless circle "moves" every
        // frame but goes nowhere, so a small threshold here let it reset the anchor forever and
        // dodge the rescue. 0.75m over the 4s window still resets instantly for any fish making
        // real progress (even the 0.5 m/s arrival glide covers 2m in that time), but catches
        // orbits up to ~0.75m across as stuck — they get a fresh wander target below.
        if (FishMovementHelpers.GetHorizontalDistance(transform.position, stuckAnchor) > 0.75f)
        {
            stuckTimer = 0f;
            stuckAnchor = transform.position;
            return;
        }

        stuckTimer += Time.deltaTime;
        if (stuckTimer < 4f) return;
        stuckTimer = 0f;

        // Only a fish that's genuinely wedged (no clear water around it) gets relocated.
        // A merely indecisive fish picks a fresh target instead of popping across the pond.
        if (FishMovementHelpers.HasClearanceAt(transform.position, 0.4f, obstacleLayers, waterSurfaceY, obstacleRayHeight))
        {
            PickNewWanderTarget();
            return;
        }

        Vector3 fresh = FishMovementHelpers.GetRandomPointInBounds(zoneBounds, transform.position, waterSurfaceY, obstacleLayers, obstacleRayHeight);
        fresh.y = waterSurfaceY;
        transform.position = fresh;
        stuckAnchor = fresh;
        PickNewWanderTarget();
    }

    private float GetSwayIntensity()
    {
        switch (currentState)
        {
            // A resting wanderer's tail winds down to a soft idle flap — clearly slower than
            // cruising but still visibly beating; restBlend is 0 outside a rest, so normal
            // cruising keeps the calm 1.
            case FishState.Wandering:  return Mathf.Lerp(1f, 0.35f, restBlend);
            case FishState.Attracted:  return 1.5f;
            // Calm tail while loitering between passes; mid-dash matches the Striking thrash (2.8) so a
            // tease dash and a real bite look identical in their buildup.
            case FishState.Nibbling:   return nibble != null && nibble.IsDashing ? 2.8f : 0.8f;
            case FishState.Scared:     return 2.5f;
            case FishState.Hunting:    return 2.2f;  // aggressive pursuit, just short of a lure strike
            case FishState.Striking:   return 2.8f;
            case FishState.Grabbing:   return 3f;   // hardest thrash — the fish is wrestling the lure
            case FishState.Despawning: return 2.6f; // same urgent tail-beat as the escape swim-off
            default:                   return 1f;
        }
    }

    private void UpdateWandering()
    {
        // Passive auto-attract: if the bobber is sitting inside this fish's awareness radius
        // (and we're not being told to give the lead fish space), wake up and approach as a follower.
        // Bait mismatch is *not* gated here — wrong-species fish still gather visually so the pond
        // doesn't look empty. Only the bite/lead promotion in FishingZone is gated by bait.
        // Tackle mismatch IS gated: a lure-only species ignores a bait bobber entirely (and vice
        // versa) — it neither schools around it nor gets recruited by the lure brain. An EMPTY hook
        // (no bait, not a lure) is also gated for species that won't bite baitless, so the visual
        // gather matches the bite gate — a baitless hook doesn't draw a curious crowd it can't catch.
        if (bobberTransform != null && !shouldAvoidBobber && reengageCooldown <= 0f && BaseAwarenessRadius > 0f
            && BobberInventory.PresetRespondsToEquippedTackle(preset)
            && !BaitInventory.EmptyHookRejects(preset))
        {
            float bobberDist = GetHorizontalDistance(transform.position, bobberTransform.position);
            if (bobberDist <= EffectiveActionRadius && IsWithinAwarenessCone(bobberTransform.position))
            {
                AttractToBobber(false);         // passive curiosity — never spooks itself for drifting close
                if (currentState == FishState.Attracted)
                {
                    isFollower = true;          // self-attracted fish never nibble until promoted by FishingZone
                    return;
                }
            }
        }

        // Predators give chase. Bobber interaction always wins — the auto-attract above already
        // returned if the tackle pulled this fish in — and a predator told to give the bobber
        // space (mid-catch) stays calm. Otherwise a wandering predator that spots prey in its
        // school locks on and hunts. This even interrupts a wander rest, so a resting pike pounces.
        if (huntCooldownTimer <= 0f && !shouldAvoidBobber
            && preset != null && preset.IsPredator && TryAcquirePrey())
        {
            currentState = FishState.Hunting;
            return;
        }

        // Post-catch coast: the predator sails straight on along its lunge heading, shedding
        // speed exponentially, and hands over to normal wandering once it's back at cruise
        // pace. No steering — this is the follow-through of the charge, not a new decision.
        if (huntCoastSpeed > 0f)
        {
            huntCoastSpeed *= Mathf.Exp(-Mathf.Max(0.1f, huntCoastDecay) * Time.deltaTime);
            if (huntCoastSpeed <= swimSpeed)
            {
                huntCoastSpeed = 0f; // decayed to cruise pace — wandering takes over seamlessly
            }
            else
            {
                Vector3 coast = transform.position + GetFlatForward() * (huntCoastSpeed * Time.deltaTime);
                coast = ClampToBounds(coast);
                coast.y = waterSurfaceY;
                if (IsObstacleAt(coast))
                {
                    huntCoastSpeed = 0f; // shore dead ahead: drop the coast, steering resumes
                }
                else
                {
                    transform.position = coast;
                    return;
                }
            }
        }

        if (wanderPauseTimer > 0f)
        {
            wanderPauseTimer -= Time.deltaTime;

            if (isResting)
            {
                // Full rest: glide to a dead stop. restBlend ramps the speed down to zero (from
                // the arrive-glide speed, so the stop continues the arrival's deceleration
                // seamlessly) and softens the tail via GetSwayIntensity / the flap-amplitude
                // fade. NO steering here — the fish coasts dead straight along its heading and
                // then simply sits. Steering while nearly stationary (even toward a gentle drift
                // point) lets a sideways separation push rotate the fish in place, which reads
                // as tail-chasing; a resting fish holds its pose and only the sine wave moves.
                restBlend = Mathf.MoveTowards(restBlend, 1f, Time.deltaTime / Mathf.Max(0.05f, restEaseSeconds));
                float restSpeed = Mathf.Lerp(swimSpeed * 0.35f, 0f, restBlend);
                if (restSpeed > 0.01f)
                {
                    Vector3 coast = transform.position + GetFlatForward() * (restSpeed * Time.deltaTime);
                    coast = ClampToBounds(coast);
                    coast.y = waterSurfaceY;
                    if (!IsObstacleAt(coast)) transform.position = coast;
                }
                return;
            }

            // Drift pause: slow forward motion instead of a stop — and crowded pausers
            // gently spread apart.
            Vector3 driftTarget = GetHeadPosition() + GetFlatForward() * 2f
                                  + ComputeSeparation() * separationStrength
                                  + ComputeSchooling();
            SteerToward(driftTarget, pauseDriftSpeed, wanderTurnRate * 0.5f);
            return;
        }

        if (!hasWanderTarget)
        {
            PickNewWanderTarget();
        }

        // Arrival and reachability are judged from the HEAD — the steered agent. Judging
        // from the centre while the head seeks the point lets a big fish circle its target
        // with the head on it and the centre forever outside the arrive radius.
        Vector3 headPos = GetHeadPosition();
        float dist = GetHorizontalDistance(headPos, wanderTarget);

        if (dist < 0.45f)
        {
            hasWanderTarget = false;
            // Roll whether this pause is a full rest (glide to a halt, tail winds down) or the
            // usual slow drift. Rests draw from their own, longer duration range.
            isResting = Random.value < restChance;
            wanderPauseTimer = isResting
                ? Random.Range(restDurationRange.x, restDurationRange.y)
                : Random.Range(wanderPauseMin, wanderPauseMax);
            return;
        }

        // Progress watchdog (see the field comment): a cruising fish closes metres per second,
        // so several seconds without ANY gain on the target means it's trapped on an orbit or a
        // detour that will never converge. Re-rolling perturbs the equilibrium — and a target
        // rolled near the shoal is one cohesion agrees with, so the mill breaks up. Reads as the
        // fish changing its mind rather than looping.
        if (dist < wanderBestDist - 0.05f)
        {
            wanderBestDist = dist;
            wanderNoProgressTimer = 0f;
        }
        else
        {
            wanderNoProgressTimer += Time.deltaTime;
            if (wanderNoProgressTimer > 4f)
            {
                hasWanderTarget = false;
                return;
            }
        }

        // A target inside the turning radius at a bad angle can only be circled — drop it
        // for a fresh one. Reads as the fish changing its mind rather than looping.
        Vector3 toWanderTarget = wanderTarget - headPos;
        toWanderTarget.y = 0f;
        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;
        float turnRadius = swimSpeed / Mathf.Max(wanderTurnRate * Mathf.Deg2Rad, 0.01f);
        if (dist < turnRadius && Vector3.Angle(flatForward, toWanderTarget) > 45f)
        {
            hasWanderTarget = false;
            return;
        }

        // If another fish is being attracted, steer away from the bobber
        if (shouldAvoidBobber && bobberTransform != null)
        {
            float bobberDist = GetHorizontalDistance(transform.position, bobberTransform.position);
            if (bobberDist < bobberAvoidRadius)
            {
                Vector3 awayFromBobber = (transform.position - bobberTransform.position);
                awayFromBobber.y = 0;
                awayFromBobber.Normalize();
                SteerToward(transform.position + awayFromBobber * 2f, swimSpeed, attractTurnRate);
                hasWanderTarget = false;
                return;
            }
        }

        // Blend wander target toward bobber if nearby (natural curiosity) — skipped for fish
        // that don't respond to the equipped tackle; they shouldn't creep toward it either.
        Vector3 moveTarget = wanderTarget;
        if (!shouldAvoidBobber && bobberTransform != null && naturalAttraction > 0f
            && BobberInventory.PresetRespondsToEquippedTackle(preset)
            && !BaitInventory.EmptyHookRejects(preset))
        {
            float bobberDist = GetHorizontalDistance(transform.position, bobberTransform.position);
            if (bobberDist > naturalAttractionMinDist && IsWithinAwarenessCone(bobberTransform.position))
            {
                Vector3 bobberPos = bobberTransform.position;
                bobberPos.y = waterSurfaceY;
                moveTarget = Vector3.Lerp(wanderTarget, bobberPos, naturalAttraction);
            }
        }

        // Personal space: bend the path away from nearby schoolmates so fish don't phase
        // through each other while milling around.
        moveTarget += ComputeSeparation() * separationStrength;

        // Same-species shoaling: flagged species drift toward their own kind and fall into a
        // shared heading, so a pond holding several of one fish reads as a loose school.
        moveTarget += ComputeSchooling();

        // Lazy S-curves: bow the path sideways with a slow per-fish sine so cruising reads
        // as weaving rather than a beeline. Fades out near the target so arrival stays clean.
        if (wanderWeaveAmplitude > 0f)
        {
            Vector3 toMove = moveTarget - headPos;
            toMove.y = 0f;
            if (toMove.sqrMagnitude > 0.01f)
            {
                Vector3 perp = Vector3.Cross(Vector3.up, toMove.normalized);
                float fade = Mathf.Clamp01(dist / 1.5f);
                float weave = Mathf.Sin(Time.time * wanderWeaveFrequency * Mathf.PI * 2f + weavePhase);
                moveTarget += perp * (weave * wanderWeaveAmplitude * fade);
            }
        }

        // Waking from a rest: ease restBlend back down over the same ramp the glide-down used,
        // so the fish visibly picks its tail back up and accelerates off the stop instead of
        // launching at full cruise from a standstill.
        if (restBlend > 0f)
        {
            isResting = false;
            restBlend = Mathf.MoveTowards(restBlend, 0f, Time.deltaTime / Mathf.Max(0.05f, restEaseSeconds));
        }

        // Glide into the stop instead of swimming full tilt until the arrival check trips.
        float arriveGlide = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(dist / 0.8f));
        SteerToward(moveTarget, swimSpeed * arriveGlide * (1f - restBlend), wanderTurnRate);
    }

    private void UpdateAttracted()
    {
        if (bobberTransform == null)
        {
            currentState = FishState.Wandering;
            PickNewWanderTarget();
            return;
        }

        Vector3 bobberPos = bobberTransform.position;
        bobberPos.y = waterSurfaceY;

        float dist = GetHorizontalDistance(transform.position, bobberPos);

        // Self-attracted followers drift back to wandering once the bobber leaves the awareness
        // radius — or once an empty hook can no longer interest them (e.g. bait removed mid-cast),
        // so a fish that was hovering leaves cleanly instead of lingering on a hook it won't bite.
        if (BaseAwarenessRadius > 0f && isFollower
            && (dist > EffectiveActionRadius || BaitInventory.EmptyHookRejects(preset)))
        {
            StopFollowing();
            return;
        }

        // Head-based: the steering converges the head onto the bobber, so the hand-off must
        // measure from there too — the centre trails half a body behind and may never come
        // within range on bigger fish. The lead stops approaching at nibbleStartRange (room to
        // circle) and the dash-and-pass nibble behavior takes over from there. Scaled by size so a
        // big fish begins orbiting farther out, matching its wider (size-scaled) circle radius.
        if (!isFollower && GetHorizontalDistance(GetHeadPosition(), bobberPos) < nibbleStartRange * NibbleSpaceScale)
        {
            StartNibbling();
            return;
        }

        // Commit ramp: as the lead closes on the bobber, weave/pauses fade out and speed ramps up,
        // so the approach reads as a deliberate closing rather than indecisive hovering. The fish
        // never gives up on its own — only player misplay (scare/reel) can break the approach.
        float commitSpan = Mathf.Max(0.01f, commitDistance - nibbleRange);
        float commitFactor = isFollower ? 0f : Mathf.Clamp01(1f - (dist - nibbleRange) / commitSpan);
        float effectiveWeaveAmp = weaveAmplitude * (1f - commitFactor * commitWeaveDamping);
        float effectivePauseChance = attractPauseChance * (1f - commitFactor);
        float effectiveSpeed = attractSpeed * (1f + commitFactor * commitSpeedBoost);

        if (attractPauseTimer > 0f)
        {
            attractPauseTimer -= Time.deltaTime;
            // Hesitating fish hover with a slow drift rather than freezing mid-water — and keep
            // their personal space, so a crowd pausing around the bobber doesn't overlap.
            SteerToward(GetHeadPosition() + GetFlatForward() * 2f + ComputeSeparation() * separationStrength,
                        pauseDriftSpeed * 0.5f, attractTurnRate * 0.25f);
            return;
        }

        weaveTimer -= Time.deltaTime;
        if (weaveTimer <= 0f)
        {
            Vector3 toBobber = (bobberPos - transform.position);
            toBobber.y = 0;
            if (toBobber.sqrMagnitude > 0.01f)
            {
                Vector3 perp = Vector3.Cross(toBobber.normalized, Vector3.up);
                weaveOffset = perp * Random.Range(-effectiveWeaveAmp, effectiveWeaveAmp);
            }

            weaveTimer = Random.Range(weaveIntervalMin, weaveIntervalMax);

            if (Random.value < effectivePauseChance)
            {
                attractPauseTimer = Random.Range(attractPauseMin, attractPauseMax);
            }
        }

        // Followers hover at followerHoverDistance from the bobber rather than crowding the hook.
        Vector3 approachCenter = bobberPos;
        if (isFollower)
        {
            Vector3 fromBobber = transform.position - bobberPos;
            fromBobber.y = 0f;
            float fromBobberDist = fromBobber.magnitude;
            Vector3 outward = fromBobberDist > 0.01f
                ? fromBobber / fromBobberDist
                : new Vector3(Mathf.Cos(GetInstanceID()), 0f, Mathf.Sin(GetInstanceID()));
            approachCenter = bobberPos + outward * followerHoverDistance;
        }

        // Personal space around the tackle: followers ringing the bobber steer around each other
        // instead of phasing through. Fades out with the lead's commit ramp so its final run at
        // the bobber is never deflected by a schoolmate parked on the approach line.
        Vector3 targetPos = approachCenter + weaveOffset
                            + ComputeSeparation() * (separationStrength * (1f - commitFactor));
        targetPos.y = waterSurfaceY;

        SteerToward(targetPos, effectiveSpeed, attractTurnRate);
    }

    private float scareJinkTimer;

    private void UpdateScared()
    {
        scareTimer -= Time.deltaTime;

        float fleePhase = scareCooldown - 1.5f;
        if (scareTimer > fleePhase)
        {
            // Jink direction randomly every so often
            scareJinkTimer -= Time.deltaTime;
            if (scareJinkTimer <= 0f)
            {
                float jinkAngle = Random.Range(-60f, 60f);
                scareDirection = Quaternion.Euler(0, jinkAngle, 0) * scareDirection;
                scareDirection.y = 0;
                scareDirection.Normalize();
                scareJinkTimer = Random.Range(0.15f, 0.35f);
            }

            // Vary speed each frame for a panicked feel
            float burstSpeed = scareSpeed * Random.Range(0.6f, 1.3f);
            Vector3 probe = transform.position + scareDirection * (burstSpeed * Time.deltaTime);
            probe.y = waterSurfaceY;
            probe = ClampToBounds(probe);

            if (IsObstacleAt(probe))
            {
                scareDirection = Vector3.Cross(scareDirection, Vector3.up).normalized;
                if (scareDirection.sqrMagnitude < 0.01f)
                    scareDirection = -scareDirection;
            }

            // Always steer (even when the probe above just deflected scareDirection) — SteerToward
            // does its own obstacle lookahead and gracefully holds/slides when boxed in. Skipping
            // this call on a blocked frame used to freeze the transform's rotation entirely while
            // scareDirection kept getting reflected underneath it (and the forced jink-timer reset
            // below re-randomized it again next frame) — that mismatch between "what's decided" and
            // "what's visibly turned" is what read as the fish's head spasming near a corner.
            // Separation keeps a school that bolts together from darting through each other.
            SteerToward(transform.position + scareDirection * 2f + ComputeSeparation() * separationStrength,
                        burstSpeed, scareTurnRate);
        }
        else if (scareTimer > 0f)
        {
            // Recovery tail: catch breath with a slow drift instead of freezing in place.
            SteerToward(GetHeadPosition() + GetFlatForward() * 2f + ComputeSeparation() * separationStrength,
                        pauseDriftSpeed, wanderTurnRate);
        }

        if (scareTimer <= 0f)
        {
            currentState = FishState.Wandering;
            PickNewWanderTarget();
        }
    }

    // The chase: a predator charges the prey it locked onto. When it closes within huntCatchRange
    // the prey bolts directly away from this predator (a real flee, not a bobber/random scatter)
    // and the predator breaks off to rest. If the prey outruns the leash, the hunt is abandoned.
    private void UpdateHunting()
    {
        // Only ever chase a calm, wandering fish. The moment the prey gets pulled into anything
        // else — the player's bobber, another predator, leaving — the predator breaks off, so a
        // hunt never snatches a fish the player is actively working.
        if (preyTarget == null || preyTarget.currentState != FishState.Wandering)
        {
            EndHunt();
            return;
        }

        Vector3 preyPos = preyTarget.transform.position;
        float dist = GetHorizontalDistance(GetHeadPosition(), preyPos);

        // Caught up: the prey bolts straight away from this predator and vanishes — diving and
        // fading out (the same exit a lifetime despawn plays). The zone is notified the instant the
        // despawn begins, so a replacement fish spawns to take its place while the fade runs.
        if (dist < huntCatchRange)
        {
            preyTarget.BeginDespawn(transform.position);
            // Carry the lunge's momentum through the catch: EndHunt drops back to Wandering,
            // where the post-catch coast bleeds this off exponentially instead of the predator
            // grinding to cruise pace the frame it touches the prey.
            huntCoastSpeed = huntSpeed;
            EndHunt();
            return;
        }

        // Outran the leash — abandon the chase.
        if (dist > huntLeashRadius)
        {
            EndHunt();
            return;
        }

        preyPos.y = waterSurfaceY;
        // Personal space minus the prey itself, FADED OUT over the final approach. Exempting only
        // the prey isn't enough: prey often swims inside a school, and its schoolmates' pushes
        // deflect the chase target off the whole clump — the predator hangs just outside
        // huntCatchRange circling the school's edge forever. Far out it weaves around bystanders;
        // inside ~2× catch range the chase wins and it barrels through the school to the prey.
        float sepFade = Mathf.Clamp01((dist - huntCatchRange) / Mathf.Max(0.5f, huntCatchRange));
        SteerToward(preyPos + ComputeSeparation(preyTarget) * (separationStrength * sepFade),
                    huntSpeed, huntTurnRate);
    }

    // Scan the shared school for the nearest huntable prey within huntRadius. Only calm, wandering
    // prey are eligible — fish already fleeing, leaving, or drawn to the player's tackle are left
    // alone so a hunt picks a fresh target and never disrupts active fishing.
    private bool TryAcquirePrey()
    {
        if (school == null) return false;

        FishRipple closest = null;
        float bestSqr = huntRadius * huntRadius;
        for (int i = 0; i < school.Count; i++)
        {
            FishRipple other = school[i];
            if (other == null || other == this) continue;
            if (!preset.Hunts(other.preset)) continue;
            if (other.currentState != FishState.Wandering) continue;

            Vector3 diff = other.transform.position - transform.position;
            diff.y = 0f;
            float sqr = diff.sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                closest = other;
            }
        }

        preyTarget = closest;
        return closest != null;
    }

    private void EndHunt()
    {
        preyTarget = null;
        huntCooldownTimer = huntCooldown;
        currentState = FishState.Wandering;
        PickNewWanderTarget();
    }

    // This fish leaves for good, and the zone is told at once so the replacement spawns while the
    // fade plays. Two callers, two moods: calm lifetime expiry (fleeFrom == null) keeps the current
    // heading and cruises off — wheeling around to flee reads as unnatural for an unbothered fish;
    // a prey fish a predator just caught (fleeFrom == the predator's position) bursts directly away
    // from it, faster. Either way it dives into the murk and fades out.
    private void BeginDespawn(Vector3? fleeFrom = null)
    {
        currentState = FishState.Despawning;
        despawnTimer = 0f;
        isFollower = false;
        onGrabStartCallback = null;
        onGrabReleasedCallback = null;

        // Pick the swim-off heading, then prefer one with clear water: probe the mid-point of the
        // swim-off and yaw around until one fits. A blocked heading just means the fade plays
        // against the shore — acceptable worst case, so the heading stands if nothing better.
        Vector3 heading;
        if (fleeFrom.HasValue)
        {
            Vector3 away = transform.position - fleeFrom.Value;
            away.y = 0f;
            heading = away.sqrMagnitude > 0.0001f ? away.normalized : GetFlatForward();
            despawnSpeed = DespawnFleeSpeed;
        }
        else
        {
            heading = GetFlatForward();
            despawnSpeed = DespawnSpeed;
        }

        despawnDirection = heading;
        float probeDistance = despawnSpeed * DespawnDuration * 0.5f;
        float[] yawOffsets = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };
        for (int i = 0; i < yawOffsets.Length; i++)
        {
            Vector3 candidate = Quaternion.Euler(0f, yawOffsets[i], 0f) * heading;
            if (!IsObstacleAt(transform.position + candidate * probeDistance))
            {
                despawnDirection = candidate;
                break;
            }
        }

        // Only the diving body remains: the surface ripple, glimpse indicator and wake all
        // read as "fish here, catchable" and this one no longer is.
        var particles = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
        glimpse.ForceHide();
        if (activeWakeInstance != null)
        {
            Destroy(activeWakeInstance);
            activeWakeInstance = null;
        }

        OnDespawnStarted?.Invoke(this);
    }

    private void UpdateDespawning()
    {
        despawnTimer += Time.deltaTime;
        float t = despawnTimer / DespawnDuration;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(despawnDirection.x, despawnDirection.z) * Mathf.Rad2Deg, 0f);
        Vector3 pos = transform.position + despawnDirection * (despawnSpeed * Time.deltaTime);
        pos.y = waterSurfaceY - DespawnDiveDepth * t;
        transform.position = pos;

        modelVisual?.SetFadeAlpha(1f - t);
    }

}
