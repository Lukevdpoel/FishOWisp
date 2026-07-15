using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Part of InventoryUI (partial class). Serialized fields live in InventoryUI.cs.
public partial class InventoryUI
{
    private void HandleLoadoutNavigation()
    {
        int xStep = ReadHorizontalStep();
        if (xStep != 0) { Cycle(xStep); return; }

        int yStep = ReadVerticalStep();
        if (yStep < 0) EnterBaitScreen();
        else if (yStep > 0) EnterBobberScreen();
    }

    // D-pad / arrow keys / WASD are discrete; the left stick is debounced into single steps so a
    // held deflection doesn't spin through every option in a few frames. Player movement is
    // disabled while the gear menu is open (ToggleInputScripts), so WASD is free to reuse here.
    private int ReadHorizontalStep()
    {
        if (GamepadInput.DpadRightPressed || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) return 1;
        if (GamepadInput.DpadLeftPressed  || Input.GetKeyDown(KeyCode.LeftArrow)  || Input.GetKeyDown(KeyCode.A)) return -1;

        float x = GamepadInput.Move.x;
        if (stickXNeutral && Mathf.Abs(x) > StickStepOn) { stickXNeutral = false; return x > 0 ? 1 : -1; }
        if (Mathf.Abs(x) < StickStepOff) stickXNeutral = true;
        return 0;
    }

    private int ReadVerticalStep()
    {
        if (GamepadInput.DpadUpPressed   || Input.GetKeyDown(KeyCode.UpArrow)   || Input.GetKeyDown(KeyCode.W)) return 1;
        if (GamepadInput.DpadDownPressed || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) return -1;

        float y = GamepadInput.Move.y;
        if (stickYNeutral && Mathf.Abs(y) > StickStepOn) { stickYNeutral = false; return y > 0 ? 1 : -1; }
        if (Mathf.Abs(y) < StickStepOff) stickYNeutral = true;
        return 0;
    }

    private void Cycle(int delta)
    {
        if (loadoutScreen == LoadoutScreen.Bait) CycleBait(delta);
        else CycleBobber(delta);
        RefreshLoadoutFocus();
    }

    private void CycleBobber(int delta)
    {
        if (BobberInventory.Instance == null) return;
        IReadOnlyList<BobberItem> owned = BobberInventory.Instance.OwnedBobbers;
        if (owned == null || owned.Count == 0) return;

        int current = IndexOfSelectedBobber(owned);
        int next = current < 0 ? (delta > 0 ? 0 : owned.Count - 1)
                               : ((current + delta) % owned.Count + owned.Count) % owned.Count;
        if (owned[next] != null) BobberInventory.Instance.SetSelectedBobber(owned[next]);
    }

    private void CycleBait(int delta)
    {
        if (BaitInventory.Instance == null) return;

        // Cycle stops = "no bait" (null — an empty hook is always a valid choice) + each owned/
        // infinite bait, so the player can deliberately fish baitless from the gear menu. Mirrors
        // BaitBarUI.CycleSelection so the keyboard/gamepad path reaches the same stops as the arrows.
        cycleBaitStops.Clear();
        cycleBaitStops.Add(null);
        cycleBaitStops.AddRange(GetCyclableBaits());
        if (cycleBaitStops.Count <= 1) return; // only "no bait" available — nothing to cycle to

        int current = cycleBaitStops.IndexOf(BaitInventory.Instance.SelectedBait); // null resolves to 0
        if (current < 0) current = 0;
        int next = ((current + delta) % cycleBaitStops.Count + cycleBaitStops.Count) % cycleBaitStops.Count;
        BaitInventory.Instance.SetSelectedBait(cycleBaitStops[next]);
    }

    private int IndexOfSelectedBobber(IReadOnlyList<BobberItem> owned)
    {
        BobberItem sel = BobberInventory.Instance.SelectedBobber;
        if (sel == null) return -1;
        for (int i = 0; i < owned.Count; i++) if (owned[i] == sel) return i;
        return -1;
    }

    // Registered bait that is on the shelf and actually obtainable (in stock or infinite).
    private List<BaitItem> GetCyclableBaits()
    {
        cyclableBaits.Clear();
        if (BaitInventory.Instance == null) return cyclableBaits;
        IReadOnlyList<BaitItem> registered = BaitInventory.Instance.RegisteredBaits;
        if (registered == null) return cyclableBaits;
        for (int i = 0; i < registered.Count; i++)
        {
            BaitItem b = registered[i];
            if (b == null || !b.isAvailable) continue;
            if (b.isAlwaysAvailable || BaitInventory.Instance.GetCount(b) > 0) cyclableBaits.Add(b);
        }
        return cyclableBaits;
    }

    private void EnterBaitScreen()
    {
        if (loadoutScreen == LoadoutScreen.Bait) return;
        // Reachable only from a regular bobber — lures take no bait.
        if (BobberInventory.IsLureEquipped) return;
        if (GetCyclableBaits().Count == 0) return;
        loadoutScreen = LoadoutScreen.Bait;
        RefreshLoadoutFocus();
    }

    private void EnterBobberScreen()
    {
        if (loadoutScreen == LoadoutScreen.Bobber) return;
        loadoutScreen = LoadoutScreen.Bobber;
        RefreshLoadoutFocus();
    }

    // Tint the equipped slot on the active screen's bar (reusing the bars' gamepad-focus
    // highlight) so it's obvious which row left/right is steering. Always re-centres on the
    // equipped item so the focus tracks the live selection.
    private void RefreshLoadoutFocus()
    {
        if (loadoutScreen == LoadoutScreen.Bait)
        {
            if (bobberBar != null) bobberBar.ClearGamepadFocus();
            if (baitBar != null) { baitBar.ClearGamepadFocus(); baitBar.FocusGamepad(); }
        }
        else
        {
            if (baitBar != null) baitBar.ClearGamepadFocus();
            if (bobberBar != null) { bobberBar.ClearGamepadFocus(); bobberBar.FocusGamepad(); }
        }
    }

    private void ResetLoadoutNavigation()
    {
        loadoutScreen = LoadoutScreen.Bobber;
        stickXNeutral = true;
        stickYNeutral = true;
        if (bobberBar != null) bobberBar.ClearGamepadFocus();
        if (baitBar != null) baitBar.ClearGamepadFocus();
    }

}
