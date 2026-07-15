using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

// Part of FishingRodController (partial class). Serialized fields live in FishingRodController.cs.
public partial class FishingRodController
{
    private void HandleInput()
    {
        if (Time.timeScale == 0f) return;

        // The notebook owns the mouse while open (and the game does NOT pause — there is no
        // PauseManager in any scene). Without this gate, LMB starts charging and the no-bait
        // path pops the inventory over the notebook. A cast being aimed can't survive the
        // notebook taking the mouse either — abandon it cleanly instead of leaving the marker
        // aiming underneath.
        if (NoteMenu.IsNotebookOpen)
        {
            StopAiming();
            AbandonCharge();
            return;
        }

        // External locks (dialogue, shop, cutscene) block starting fishing — but once a flow is
        // in progress we hold the movement lock ourselves and must keep reading our own inputs.
        if (currentState == FishingState.Idle
            && playerController != null && playerController.areControlsLocked) return;

        // --- Aim Mode (RMB / LB) - works in any non-fight/non-inspection state ---
        if ((KeyInput.AimPressed || GamepadInput.AimPressed) && !isAiming)
        {
            isAiming = true;
            FishingEvents.OnStartAiming?.Invoke();
        }

        if ((KeyInput.AimReleased || GamepadInput.AimReleased) && isAiming)
        {
            StopAiming();
        }

        // --- Keyboard/mouse verbs, context-switched by fishing state. All four default to LMB
        // (one do-fishing button, like the old hardcoded Mouse0 switch) but each reads its own
        // binding so they can be split apart on the InputBindings asset. ---
        switch (currentState)
        {
            case FishingState.Idle:
                if (KeyInput.CastAimPressed) StartCharging();
                break;
            case FishingState.WaitingForBite:
                if (KeyInput.AttractPressed) FishingEvents.OnAttractFish?.Invoke();
                break;
            case FishingState.FishOnTheLine:
                if (KeyInput.HookPressed)
                {
                    if (isLureGrabActive) ConfirmLureHook();
                    else HookFishAndStartFight();
                }
                break;
            case FishingState.InspectingCatch:
                if (KeyInput.ConfirmPressed) TryFinishInspection();
                break;
        }

        // --- LT: hold to aim the cast marker, release to put the rod away — a mirror of holding
        // LMB on mouse/keyboard. The throw itself is the whip gesture (RodCasting): while the aim
        // button is held, yanking the right stick / mouse out and snapping it back the opposite
        // way fires the cast; releasing the aim button without a whip just abandons the aim (no
        // cooldown, nothing thrown). Only armed from Idle/Charging so the trigger keeps its
        // post-cast meanings without cross-firing here. ---
        bool castAimHeld = GamepadInput.CastAimHeld;
        if (currentState == FishingState.Idle && castAimHeld && !castAimHeldLast)
        {
            StartCharging();
        }
        else if (currentState == FishingState.Charging && castAimHeldLast && !castAimHeld)
        {
            AbandonCharge();
        }
        castAimHeldLast = castAimHeld;

        // --- RB: reset a cast already in the water (bobber or lure alike), or cut the line
        // mid-fight to give up the fish. ---
        if (GamepadInput.ResetCastPressed)
        {
            if (currentState == FishingState.WaitingForBite || currentState == FishingState.LureReeling)
                ReelLineBack();
            else if (currentState == FishingState.FightingFish)
                CutLineDuringFight();
        }

        // --- RT tap: attract fish toward the bobber while waiting for a bite. ---
        if (GamepadInput.AttractPressed && currentState == FishingState.WaitingForBite)
        {
            FishingEvents.OnAttractFish?.Invoke();
        }

        // --- A: confirm/finish catch inspection. (Casting is no longer on A — the whip gesture
        // throws while aiming.) ---
        if (GamepadInput.ConfirmPressed)
        {
            switch (currentState)
            {
                case FishingState.InspectingCatch:
                    TryFinishInspection();
                    break;
            }
        }

        // --- Reel button (default RT) press reacts to a biting fish — the SAME control the fight
        // reel uses (held), so hooking and reeling share one button on every scheme. Mirrors LMB on
        // keyboard/mouse, where the down-edge hooks the bite and the hold reels. ---
        if (GamepadInput.ReelPressed && currentState == FishingState.FishOnTheLine)
        {
            if (isLureGrabActive) ConfirmLureHook();
            else HookFishAndStartFight();
        }

        // --- Releasing the cast-aim key (LMB up) without a whip abandons the aim — nothing is
        // thrown; the whip gesture inside RodCasting is the only way to actually cast. ---
        if (KeyInput.CastAimReleased && currentState == FishingState.Charging)
        {
            AbandonCharge();
        }

        // --- Instant reset (default E) — keyboard mirror of the RB cast reset. The reel-in arc is
        // kinematic and flies over geometry, so this doubles as the escape hatch when the lure
        // snags behind terrain during the physics reel. Mid-fight it cuts the line instead. ---
        if (KeyInput.ResetCastPressed)
        {
            if (currentState == FishingState.WaitingForBite || currentState == FishingState.LureReeling)
                ReelLineBack();
            else if (currentState == FishingState.FightingFish)
                CutLineDuringFight();
        }
    }

    private void StopAiming()
    {
        if (!isAiming) return;
        isAiming = false;
        FishingEvents.OnStopAiming?.Invoke();
    }

    // The aim button was released without a whip: no cast, no cooldown — everything just returns
    // to idle. OnCancelCharging retires the marker/camera charge reaction; OnChargeCanceled snaps
    // the animator out of the windup and releases anything (inventory) locked for the charge.
    private void AbandonCharge()
    {
        if (currentState != FishingState.Charging) return;
        currentState = FishingState.Idle;
        FishingEvents.OnCancelCharging?.Invoke();
        FishingEvents.OnChargeCanceled?.Invoke();
        if (playerController != null) playerController.LockControls(false);
    }

    private void ReelLineBack()
    {
        Debug.Log("Reeling back the line.");
        currentState = FishingState.Reeling;
        StartCoroutine(ReelInBobberRoutine(null));
    }

    // Deliberately give up a fight: cut the line and let the fish go. Runs the exact same exit
    // as the line snapping (FishFightHandler.Outcome.Escaped) — the fish is released for good,
    // swims off, dives and fades out (HookedFishController.BeginEscape), and the cast resets
    // through the fail path.
    private void CutLineDuringFight()
    {
        Debug.Log("[FishFight] Line cut — the fish gets away.");
        fishFight.End(activeBobber);
        StartCoroutine(FailRoutine());
    }

    private InventoryUI GetInventoryUI()
    {
        if (cachedInventoryUI != null) return cachedInventoryUI;
        cachedInventoryUI = ResolveInventoryUI();
        return cachedInventoryUI;
    }

    private void StartCharging()
    {
        if (playerController != null && playerController.areControlsLocked)
        {
            Debug.Log("[FishFight] StartCharging BLOCKED — controls locked");
            return;
        }
        // Can't cast while jumping or otherwise airborne — must be planted on the ground.
        if (playerController != null && !playerController.IsGrounded)
        {
            Debug.Log("[FishFight] StartCharging BLOCKED — player is airborne");
            return;
        }
        if (bobberInWater != null || activeBobber != null)
        {
            Debug.Log($"[FishFight] StartCharging BLOCKED — bobberInWater: {(bobberInWater != null ? "exists" : "null")}, activeBobber: {(activeBobber != null ? "exists" : "null")}");
            return;
        }
        // Casting with an empty hook is allowed now (a baitless cast still draws bitesWithoutBait fish,
        // just slowly). This only drops a depleted bait selection back to "no bait"; it never pops the
        // gear menu. Lures use no bait, so it's a no-op for them.
        if (!BobberInventory.IsLureEquipped && BaitInventory.Instance != null)
        {
            BaitInventory.Instance.EnsureBaitSelected();
        }
        currentState = FishingState.Charging;
        if (playerController != null) playerController.LockControls(true);
        FishingEvents.OnStartCharging?.Invoke();
    }
    private void HandleThrow(Vector3 direction, float force)
    {
        danglingBobber?.SetActive(false);
        castOriginPosition = transform.position;
        castStartTime = Time.time;

        if (BobberInventory.IsLureEquipped)
        {
            lureReel.Reset();
            currentState = FishingState.LureReeling;
        }
        else
        {
            currentState = FishingState.WaitingForBite;
        }
    }
}
