using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Part of FishRipple (partial class). Serialized fields live in FishRipple.cs.
public partial class FishRipple
{
    // Lure-brain strike: the fish has won its bite roll and commits — a fast 3D dash at the lure,
    // no weaving. onGrabStart fires when the dash connects and the fish clamps onto the lure (the
    // reaction window opens); onGrabReleased fires if the grip lapses with no player response (the
    // fish spits the lure and swims off). No bite is committed until the zone calls ConfirmGrab().
    public void StartLureStrike(System.Action<FishRipple> onGrabStart, System.Action<FishRipple> onGrabReleased)
    {
        // Already committed (Striking/Grabbing) or unavailable — don't restart a dash. Guarding
        // Striking also stops a re-roll firing while the fish is mid lure-nibble pass.
        if (currentState == FishState.Scared || currentState == FishState.Nibbling
            || currentState == FishState.Striking || currentState == FishState.Grabbing
            || currentState == FishState.Despawning) return;
        if (bobberTransform == null) return;

        onGrabStartCallback = onGrabStart;
        onGrabReleasedCallback = onGrabReleased;
        isFollower = false;
        dashDirection = GetFlatForward();
        strikeHeadY = transform.position.y; // start at current swim depth; the dash arcs it up
        // Progress-ramped rise, same as the bobber leap: the nose-up starts with the dash.
        strikeStartHoriz = GetHorizontalDistance(GetHeadPosition(), bobberTransform.position);
        strikeGiveUpTimer = StrikeGiveUpSeconds();
        currentState = FishState.Striking;
    }

    // Generous worst case for the dash: the run-up at dash speed plus slack for the rise and one
    // missed pass. A clean strike connects in a fraction of this.
    private float StrikeGiveUpSeconds() =>
        3f + strikeStartHoriz / Mathf.Max(0.5f, strikeSpeed);

    // A non-committing lure nibble: the fish darts THROUGH the lure (twitching it) and out to a
    // point past it, then drops back to hovering, still interested. The lure brain rolls this on a
    // response that isn't a full bite, so the player has to keep tugging to re-roll for a real bite.
    // isFollower is left untouched so the fish stays a hovering chaser when the pass ends (clearing
    // it would let UpdateAttracted mistake this lure chaser for a bait lead and start bobber-nibbling).
    public void StartLureNibble()
    {
        if (currentState == FishState.Scared || currentState == FishState.Striking
            || currentState == FishState.Grabbing || currentState == FishState.Despawning) return;
        if (bobberTransform == null) return;

        Vector3 toLure = bobberTransform.position - transform.position;
        toLure.y = 0f;
        if (toLure.sqrMagnitude < 0.0001f) toLure = GetFlatForward();
        toLure.Normalize();

        Vector3 lureFlat = new Vector3(bobberTransform.position.x, waterSurfaceY, bobberTransform.position.z);
        nibblePassTarget = lureFlat + toLure * Mathf.Max(0.3f, nibblePassDistance);
        nibblePassBrushed = false;
        nibblePassTimer = 2f;
        nibblePass = true;
        dashDirection = GetFlatForward();
        strikeHeadY = transform.position.y;
        // Same progress-ramped rise as a real strike, so the tease's nose-up begins with the dart.
        strikeStartHoriz = GetHorizontalDistance(GetHeadPosition(), bobberTransform.position);
        currentState = FishState.Striking;
    }

    // The dash connected: clamp onto the lure and start the hold/reaction window. The fish now
    // carries the lure (tows it to its mouth) and keeps swimming on in its dash heading. The zone
    // is told via onGrabStart so it can open the rod's window and freeze further bite rolls.
    private void BeginGrab()
    {
        dashDirection = GetFlatForward();
        grabStartHeadY = strikeHeadY;
        SizeClass size = preset != null ? preset.sizeClass : SizeClass.Medium;
        grabTimer = Mathf.Max(0.05f, grabHoldDuration * LureBiteBrain.ReactionWindowMultiplier(size));
        currentState = FishState.Grabbing;
        bobberCtrl?.BeginGrabTow();
        onGrabStartCallback?.Invoke(this);
    }

    // The hold lapsed with no response: spit the lure (hand it back to physics) and coast on along
    // the dash heading before normal wandering resumes — the fish "finishes its dash" instead of
    // teleporting away.
    private void ReleaseGrab()
    {
        var notify = onGrabReleasedCallback;
        onGrabStartCallback = null;
        onGrabReleasedCallback = null;
        bobberCtrl?.EndGrabTow();
        // A bobber bite that's missed (window lapsed) coasts off as a wandering fish; drop the
        // bite-hide hook so a LATER bite on this same bobber can't hide this now-innocent fish.
        // Harmless for the lure path (those fish never subscribed).
        FishingEvents.OnFishBite -= OnFishBiteHide;

        currentState = FishState.Wandering;
        Vector3 ahead = GetHeadPosition() + dashDirection * grabReleaseFollowThrough;
        ahead.y = waterSurfaceY;
        wanderTarget = ClampToBounds(ahead);
        BeginWanderLeg();
        reengageCooldown = grabReleaseAvoidTime;

        notify?.Invoke(this);
    }

    // The player reacted in time. Returns true while the fish is still gripping (the zone then
    // turns this fish into the hooked fish); false if the grab already lapsed this frame. The tow
    // ends here so BobberController.HookFish takes the lure over cleanly.
    public bool ConfirmGrab()
    {
        if (currentState != FishState.Grabbing) return false;
        bobberCtrl?.EndGrabTow();
        grabTimer = 0f;
        onGrabStartCallback = null;
        onGrabReleasedCallback = null;
        return true;
    }

    public void CancelLureStrike()
    {
        if (currentState != FishState.Striking && currentState != FishState.Grabbing) return;
        if (currentState == FishState.Grabbing) onGrabReleasedCallback?.Invoke(this);
        onGrabStartCallback = null;
        onGrabReleasedCallback = null;
        nibblePass = false;
        bobberCtrl?.EndGrabTow();
        currentState = FishState.Wandering;
        PickNewWanderTarget();
    }

    // The dash: a fast, committed charge at the lure. As it closes it arcs UP toward the lure's
    // real height so the head rises to meet it (the body trails the arc as a 3D rope). On contact
    // (snout within reach of the lure) the fish clamps on and the grab begins.
    private void UpdateStriking()
    {
        if (bobberTransform == null)
        {
            CancelLureStrike();
            return;
        }

        if (nibblePass)
        {
            UpdateLureNibblePass();
            return;
        }

        Vector3 lure = bobberTransform.position;            // real 3D lure position
        Vector3 lureFlat = new Vector3(lure.x, waterSurfaceY, lure.z);

        // Contact is measured snout-to-lure in full 3D: the fish must actually rise to the lure,
        // not just be horizontally under it.
        Vector3 snout = modelVisual != null ? modelVisual.MouthWorldPosition : GetHeadPosition();
        if (Vector3.Distance(snout, lure) < Mathf.Max(strikeLeapRange, nibbleRange))
        {
            BeginGrab();
            return;
        }

        // Contact never landed in the worst-case dash time: the bobber is somewhere the dash can't
        // actually reach. Give up and swim off (with the post-grab re-engage cooldown, so the fish
        // doesn't immediately wheel back and repeat the doomed dash).
        strikeGiveUpTimer -= Time.deltaTime;
        if (strikeGiveUpTimer <= 0f)
        {
            reengageCooldown = grabReleaseAvoidTime;
            CancelLureStrike();
            return;
        }

        // Horizontal chase toward the lure.
        SteerToward(lureFlat, strikeSpeed, strikeTurnRate);
        dashDirection = GetFlatForward();

        // Vertical arc up to the lure, ramped by leap PROGRESS so the jump begins the instant the leap
        // launches (its run-up can be several metres). Reach full height over the first ~60% of the
        // leap, then hold it for the final approach, so the snout is already up at the lure when
        // contact lands instead of still climbing. Same for the bobber bite and the lure strike.
        float horiz = GetHorizontalDistance(GetHeadPosition(), lureFlat);
        float riseT = strikeStartHoriz > 0.01f ? 1f - Mathf.Clamp01(horiz / (strikeStartHoriz * 0.6f)) : 1f;
        float targetY = Mathf.Lerp(waterSurfaceY, HostYToPlaceSnoutAt(lure.y), riseT);
        strikeHeadY = Mathf.MoveTowards(strikeHeadY, targetY, strikeRiseSpeed * Time.deltaTime);
        ApplyStrikeHeadY();
    }

    // The lure nibble pass: same fast dart as a strike, but it drives THROUGH the lure to an
    // overshoot point instead of clamping on. It rises to brush the lure once (twitching it), then
    // settles back to swim depth and, on reaching the far point, returns to hovering — still a
    // chaser, so the next tug can re-roll. No grab, no reaction window.
    private void UpdateLureNibblePass()
    {
        Vector3 lure = bobberTransform.position;
        Vector3 lureFlat = new Vector3(lure.x, waterSurfaceY, lure.z);

        // Brush the lure once on the way through — a quick twitch, the lure's "a fish bumped it".
        Vector3 snout = modelVisual != null ? modelVisual.MouthWorldPosition : GetHeadPosition();
        if (!nibblePassBrushed && Vector3.Distance(snout, lure) < Mathf.Max(strikeLeapRange, nibbleRange))
        {
            nibblePassBrushed = true;
            bobberCtrl?.PlayNibbleWobble(GetFlatForward());
        }

        // Dart through the lure toward the overshoot point past it.
        SteerToward(nibblePassTarget, strikeSpeed, strikeTurnRate);
        dashDirection = GetFlatForward();

        // Up-and-over arc tied to PROGRESS so the tease's rise begins with the dart and matches a real
        // lure strike's buildup: ramp up to the lure over the approach (full by ~60%, hold to it),
        // then ease back down over the overshoot — a clean forward arc, never a drop in place, and no
        // early tell the player could read.
        float horiz = GetHorizontalDistance(GetHeadPosition(), lureFlat);
        float riseT = !nibblePassBrushed
            ? (strikeStartHoriz > 0.01f ? 1f - Mathf.Clamp01(horiz / (strikeStartHoriz * 0.6f)) : 1f)
            : Mathf.Clamp01(GetHorizontalDistance(GetHeadPosition(), nibblePassTarget) / Mathf.Max(0.2f, nibblePassDistance));
        float targetY = Mathf.Lerp(waterSurfaceY, HostYToPlaceSnoutAt(lure.y), riseT);
        strikeHeadY = Mathf.MoveTowards(strikeHeadY, targetY, strikeRiseSpeed * Time.deltaTime);
        ApplyStrikeHeadY();

        // Out the far side (or the safety cap lapsed if the overshoot was blocked) — drop back to
        // hovering, ready to be teased into another roll.
        nibblePassTimer -= Time.deltaTime;
        if (GetHorizontalDistance(GetHeadPosition(), nibblePassTarget) <= 0.3f || nibblePassTimer <= 0f)
        {
            nibblePass = false;
            strikeHeadY = waterSurfaceY;
            currentState = FishState.Attracted;
            weaveTimer = 0f;
            weaveOffset = Vector3.zero;
            attractPauseTimer = 0f;
        }
    }

    // The grab: the fish has the lure in its mouth and keeps swimming on in its dash heading,
    // towing the lure along (and dragging it a little deeper over the hold). When the hold lapses
    // it spits the lure and coasts away; ConfirmGrab() (player reacted) ends it as a real bite.
    private void UpdateGrabbing()
    {
        if (bobberTransform == null)
        {
            ReleaseGrab();
            return;
        }

        grabTimer -= Time.deltaTime;

        // Keep swimming forward along the dash heading, carrying the lure. Unlike every other
        // moving state this never routed through SteerToward, so a grab towing the lure toward
        // shore/rocks had no obstacle check at all — hold position (still counting down the grab
        // timer toward ReleaseGrab/ConfirmGrab) rather than plow into terrain.
        Vector3 step = dashDirection * (grabForwardSpeed * Time.deltaTime);
        Vector3 newPos = ClampToBounds(transform.position + step);
        if (!IsObstacleAt(newPos))
        {
            transform.position = newPos;
        }
        transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(dashDirection.x, dashDirection.z) * Mathf.Rad2Deg, 0f);

        // Drag the lure down a touch as the hold runs out (TP fish pull the lure under).
        float held = grabHoldDuration > 0.001f ? 1f - Mathf.Clamp01(grabTimer / grabHoldDuration) : 1f;
        strikeHeadY = Mathf.MoveTowards(strikeHeadY, grabStartHeadY - grabPullDownDepth * held,
                                        strikeRiseSpeed * Time.deltaTime);
        ApplyStrikeHeadY();

        // Tow the lure to the fish's mouth so it travels with the fish.
        if (bobberCtrl != null && modelVisual != null)
            bobberCtrl.SetGrabTowTarget(modelVisual.MouthWorldPosition);

        if (grabTimer <= 0f) ReleaseGrab();
    }

    // Applies the separately-integrated strike head height (SteerToward re-pins y to the surface,
    // so this runs last and owns the height during the dash/grab).
    private void ApplyStrikeHeadY()
    {
        Vector3 p = transform.position;
        p.y = strikeHeadY;
        transform.position = p;
    }

    // The host Y that places the rendered snout at worldY. The model is sunk below the host, so
    // we read the live host→snout vertical gap and offset by it (adapts to any species/depth).
    private float HostYToPlaceSnoutAt(float worldY)
    {
        float snoutY = modelVisual != null ? modelVisual.MouthWorldPosition.y : transform.position.y;
        float hostAboveSnout = transform.position.y - snoutY;
        return worldY + hostAboveSnout;
    }

    private void StartNibbling()
    {
        currentState = FishState.Nibbling;

        if (nibble == null) nibble = new FishNibbleBehavior(transform);
        nibble.Begin(bobberTransform, waterSurfaceY, preset, BuildNibbleSettings());

        // FishRipple also wants to hide visuals once the bite resolves. Subscribed here
        // (not in FishNibbleBehavior) because HideVisuals is a FishRipple concern. Remove first so
        // a fish that nibbles, misses, then nibbles again can't stack duplicate subscriptions.
        FishingEvents.OnFishBite -= OnFishBiteHide;
        FishingEvents.OnFishBite += OnFishBiteHide;
    }

    // After enough nibble passes the fish commits to the bite. The zone launches this with the
    // SAME grab callbacks the lure uses, so a bobber bite runs the identical dash → clamp → carry →
    // react-window flow: a fast 3D dash at the bobber, then it clamps on and tows the bobber along
    // (BeginGrabTow) so the bobber gets visibly dragged under and away — never a snap to a still
    // bobber. The reaction window is the lure's: react in time and the grab commits to a real bite
    // (ConfirmGrab → HookFish), miss it and the fish lets go and swims off (no cast-fail). State is
    // wired directly to bypass StartLureStrike's Nibbling guard.
    public void StartBobberBiteStrike(System.Action<FishRipple> onGrabStart, System.Action<FishRipple> onGrabReleased)
    {
        if (currentState != FishState.Nibbling) return;
        if (bobberTransform == null || bobberCtrl == null) return;
        nibble?.Stop();

        onGrabStartCallback = onGrabStart;
        onGrabReleasedCallback = onGrabReleased;
        isFollower = false;
        dashDirection = GetFlatForward();
        strikeHeadY = transform.position.y;
        // Ramp the leap's rise by progress from HERE (the run-up can be metres), so the jump starts
        // with the leap instead of waiting until the fish is almost on the bobber.
        strikeStartHoriz = GetHorizontalDistance(GetHeadPosition(), bobberTransform.position);
        strikeGiveUpTimer = StrikeGiveUpSeconds();
        currentState = FishState.Striking;
    }

    // True only on the nibbling lead once it has circled/dashed enough to commit. The zone polls
    // this to launch the bobber bite with the lure's grab-window callbacks.
    public bool NibbleReadyToBite =>
        currentState == FishState.Nibbling && nibble != null && nibble.ReadyToBite;

    // Bigger fish need a wider berth to carve the orbit and the dash; scale the orbit and the
    // ranges that ride with it (start/pass/touch) by size class. Speeds and the gap/count cadence
    // stay as authored — a big fish circling the same linear speed just laps lazier, which fits.
    private float NibbleSpaceScale =>
        SizeClassHelper.GetNibbleSpaceScale(preset != null ? preset.sizeClass : SizeClass.Medium);

    private FishNibbleBehavior.Settings BuildNibbleSettings()
    {
        float spaceScale = NibbleSpaceScale;
        return new FishNibbleBehavior.Settings
        {
            circleRadius = nibbleCircleRadius * spaceScale,
            circleSpeed = nibbleCircleSpeed,
            dashSpeed = nibbleDashSpeed,
            touchRadius = nibbleTouchRadius * spaceScale,
            passDistance = nibblePassDistance * spaceScale,
            nibbleGapRange = nibbleGapRange,
            nibblesBeforeBite = nibblesBeforeBite,
            turnRate = nibbleTurnRate,
            radiusJitter = nibbleRadiusJitter,
            wanderStrength = nibbleWanderStrength,
            dashRise = nibbleDashRise,
            // How far the snout sits below the host, so the dash can peak with the snout on the bobber
            // exactly like the bite leap (HostYToPlaceSnoutAt). Falls back to modelDepth if unmeasured.
            snoutDepth = modelVisual != null
                ? Mathf.Max(0f, transform.position.y - modelVisual.MouthWorldPosition.y)
                : modelDepth,
            obstacleLayers = obstacleLayers,
            obstacleRayHeight = obstacleRayHeight,
        };
    }

    private void OnFishBiteHide(BobberController bobber)
    {
        if (bobberTransform != null && bobber.transform == bobberTransform)
        {
            HideVisuals();
            FishingEvents.OnFishBite -= OnFishBiteHide;
            nibble?.Stop();
        }
    }

    private void HideVisuals()
    {
        // Disable all particle systems on this object and children
        var particles = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // Disable all renderers on this object and children
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = false;
        }

        glimpse.ForceHide();

        // Destroy wake
        if (activeWakeInstance != null)
        {
            Destroy(activeWakeInstance);
            activeWakeInstance = null;
        }
    }

}
