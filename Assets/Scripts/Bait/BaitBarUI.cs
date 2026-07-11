using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Twilight-Princess-style single selector for the equipped bait — the bait counterpart of
// BobberBarUI. It shows only the equipped bait as a name (with its stock count) and a
// description underneath, flanked by left/right arrows for "scrolling" through your baits.
//
// The UI is authored by you in the editor (build your own Canvas / panel / text / background and
// wire the slots below); this script only DRIVES those objects — it never creates any UI. If a
// slot is left unwired that piece simply does nothing.
//
// Bait is only relevant with a regular bobber (lures take no bait), so the selector is reachable
// by pressing DOWN from the bobber screen in the gear menu (InventoryUI handles that navigation
// and calls SetSelectedBait). The arrows are also clickable for mouse users and for the
// programmatic bait-missing prompt that pops the menu open when you cast with no bait equipped.
public class BaitBarUI : MonoBehaviour
{
    [Header("Visibility")]
    [Tooltip("If true, the selector is only visible while the gear/inventory menu is open.")]
    public bool onlyShowWhileInventoryOpen = true;

    // ---- Authored layout (wire these to your own UI) ------------------------
    [Header("UI References")]
    [Tooltip("The selector's root object (holds your background + all the labels/arrows). Toggled " +
             "on/off with visibility.")]
    [SerializeField] private RectTransform blockRoot;
    [Tooltip("TMP label that shows the equipped bait's name + count (the title).")]
    [SerializeField] private TextMeshProUGUI nameLabel;
    [Tooltip("TMP label that shows the equipped bait's description.")]
    [SerializeField] private TextMeshProUGUI descriptionLabel;
    [Tooltip("TMP label for the 'press up for tackle' navigation hint (shown/hidden automatically).")]
    [SerializeField] private TextMeshProUGUI hintLabel;
    [Tooltip("Left cycle-arrow button.")]
    [SerializeField] private Button leftButton;
    [Tooltip("Right cycle-arrow button.")]
    [SerializeField] private Button rightButton;
    [Tooltip("Optional: the glyph child rect inside the left arrow (only used for the gentle bob " +
             "animation — leave empty to skip the bob).")]
    [SerializeField] private RectTransform leftArrowGlyphRect;
    [Tooltip("Optional: the glyph child rect inside the right arrow (bob animation only).")]
    [SerializeField] private RectTransform rightArrowGlyphRect;
    [Tooltip("Optional CanvasGroup on the block, used to dim the selector when it isn't the active " +
             "screen. Dimming is skipped if left empty.")]
    [SerializeField] private CanvasGroup canvasGroup;

    private bool lastVisibleState = true;
    private bool isFishingActive;
    // The equipped bait's description before {verb} prompt tokens are resolved. Kept so the prompts
    // can be re-rendered when the player swaps input device mid-menu.
    private string rawDescription = string.Empty;
    // True while the bait screen is the one left/right is steering (set by InventoryUI when you
    // press down from the bobber screen). Unlike the bobber selector, bait visibility depends on
    // this in loadout mode: bait only appears once you've stepped down to it.
    private bool isActiveDimension;

    private readonly List<BaitItem> cyclable = new List<BaitItem>();
    private readonly List<BaitItem> cycleStops = new List<BaitItem>();

    private const float ArrowBobAmplitude = 6f;
    private const float ArrowBobSpeed = 4.5f;

    // --- Public API used by InventoryUI's loadout navigation (kept stable) ---
    /// <summary>Step onto the bait screen — reveal + brighten this selector.</summary>
    public void FocusGamepad() { isActiveDimension = true; RefreshVisuals(); }
    /// <summary>Leave the bait screen — hide this selector (in loadout mode).</summary>
    public void ClearGamepadFocus() { isActiveDimension = false; RefreshVisuals(); }

    private void Start()
    {
        WireArrowButtons();
        RefreshSelection();

        if (BaitInventory.Instance != null)
        {
            BaitInventory.Instance.OnBaitChanged += RefreshSelection;
            BaitInventory.Instance.OnSelectedBaitChanged += HandleSelectedChanged;
        }

        FishingEvents.OnThrowBobber += HandleThrow;
        FishingEvents.OnCancelFishing += HandleFishingEnded;
        FishingEvents.OnReelingCompleted += HandleFishingEnded;

        // Re-render the description's button prompts when the player swaps keyboard <-> gamepad, or
        // when Steam reports a new controller/config (so the inline glyphs swap to the right art).
        GamepadInput.OnActiveDeviceChanged += HandleDeviceChanged;
        SteamInputGlyphs.OnGlyphsChanged += RefreshDescription;

        ApplyVisibility(!onlyShowWhileInventoryOpen);
    }

    private void OnDestroy()
    {
        FishingEvents.OnThrowBobber -= HandleThrow;
        FishingEvents.OnCancelFishing -= HandleFishingEnded;
        FishingEvents.OnReelingCompleted -= HandleFishingEnded;
        GamepadInput.OnActiveDeviceChanged -= HandleDeviceChanged;
        SteamInputGlyphs.OnGlyphsChanged -= RefreshDescription;

        if (BaitInventory.Instance != null)
        {
            BaitInventory.Instance.OnBaitChanged -= RefreshSelection;
            BaitInventory.Instance.OnSelectedBaitChanged -= HandleSelectedChanged;
        }
    }

    private void HandleThrow(Vector3 dir, float force) => isFishingActive = true;
    private void HandleFishingEnded() => isFishingActive = false;
    private void HandleSelectedChanged(BaitItem _) => RefreshSelection();

    private void HandleDeviceChanged(ActiveInputDevice _) => RefreshDescription();

    // Re-resolve the {verb} tokens in the current blurb to inline glyphs / text for the active device.
    private void RefreshDescription()
    {
        if (descriptionLabel != null) GlyphRichText.Apply(descriptionLabel, rawDescription);
    }

    // Hook the cycle arrows to the selection stepper. Wiring your own arrow Buttons is all it takes.
    private void WireArrowButtons()
    {
        if (leftButton != null) leftButton.onClick.AddListener(() => CycleSelection(-1));
        if (rightButton != null) rightButton.onClick.AddListener(() => CycleSelection(1));
    }

    private void Update()
    {
        bool menuOpen = (onlyShowWhileInventoryOpen ? InventoryUI.IsInventoryOpen : true)
            && FishVendor.CurrentShoppingVendor == null; // never leak the selector into the 3D shop

        // Bait only matters with a regular bobber, and only if you actually own/are-granted some.
        // In loadout mode it additionally appears only once you've stepped down onto it; while the
        // menu was opened programmatically (bait-missing prompt) there's no "down", so show it.
        bool applicable = menuOpen && !BobberInventory.IsLureEquipped && HasCyclableBait();
        bool shouldShow = applicable && (!InventoryUI.IsLoadoutActive || isActiveDimension);

        if (shouldShow != lastVisibleState) ApplyVisibility(shouldShow);
        if (lastVisibleState) AnimateArrows();
    }

    private void ApplyVisibility(bool visible)
    {
        lastVisibleState = visible;
        if (blockRoot != null) blockRoot.gameObject.SetActive(visible);
        if (visible) { RefreshSelection(); }
    }

    // Step the equipped bait by delta through the available baits (wrapping). Lures take no bait,
    // and bait can't be swapped while a bobber is in the water.
    private void CycleSelection(int delta)
    {
        if (isFishingActive) return;
        if (BobberInventory.IsLureEquipped) return;
        if (BaitInventory.Instance == null) return;

        // Cycle stops = "no bait" (null — an empty hook is always a valid choice now) + each owned/
        // infinite bait, so the player can deliberately fish baitless even while holding bait.
        cycleStops.Clear();
        cycleStops.Add(null);
        cycleStops.AddRange(GetCyclableBaits());
        if (cycleStops.Count <= 1) return; // only "no bait" available — nothing to cycle to

        int current = cycleStops.IndexOf(BaitInventory.Instance.SelectedBait); // null resolves to 0
        if (current < 0) current = 0;
        int next = ((current + delta) % cycleStops.Count + cycleStops.Count) % cycleStops.Count;
        BaitInventory.Instance.SetSelectedBait(cycleStops[next]);
    }

    private void RefreshSelection()
    {
        if (nameLabel == null) return;

        BaitItem sel = BaitInventory.Instance != null ? BaitInventory.Instance.SelectedBait : null;
        if (sel == null)
        {
            nameLabel.text = "no bait";
            rawDescription = "Scroll left / right to choose bait.";
        }
        else
        {
            string display = string.IsNullOrEmpty(sel.displayName) ? sel.name : sel.displayName;
            nameLabel.text = $"{display}   {CountSuffix(sel)}";
            rawDescription = sel.description;
        }
        // Resolve {verb} tutorial tokens to inline glyphs / button prompts for the active device.
        RefreshDescription();
        RefreshVisuals();
    }

    private static string CountSuffix(BaitItem bait)
    {
        if (bait.isAlwaysAvailable) return "∞";
        int count = BaitInventory.Instance != null ? BaitInventory.Instance.GetCount(bait) : 0;
        return "×" + count;
    }

    private void RefreshVisuals()
    {
        if (blockRoot == null) return;

        // "No bait" is always a cycle stop, so even a single owned bait gives a real choice (bait <-> none).
        bool showArrows = GetCyclableBaits().Count >= 1;
        if (leftButton != null) leftButton.gameObject.SetActive(showArrows);
        if (rightButton != null) rightButton.gameObject.SetActive(showArrows);

        // The "press up for tackle" hint only makes sense inside the loadout gear menu, where up/down
        // switches screens. Programmatic opens (bait-missing prompt) have no such navigation.
        if (hintLabel != null) hintLabel.gameObject.SetActive(InventoryUI.IsLoadoutActive);

        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    // Registered bait that's on the shelf and obtainable right now (in stock or infinite).
    private List<BaitItem> GetCyclableBaits()
    {
        cyclable.Clear();
        if (BaitInventory.Instance == null) return cyclable;
        IReadOnlyList<BaitItem> registered = BaitInventory.Instance.RegisteredBaits;
        if (registered == null) return cyclable;
        for (int i = 0; i < registered.Count; i++)
        {
            BaitItem b = registered[i];
            if (b == null || !b.isAvailable) continue;
            if (b.isAlwaysAvailable || BaitInventory.Instance.GetCount(b) > 0) cyclable.Add(b);
        }
        return cyclable;
    }

    private bool HasCyclableBait() => GetCyclableBaits().Count > 0;

    private void AnimateArrows()
    {
        if (leftArrowGlyphRect == null || rightArrowGlyphRect == null) return;
        float bob = Mathf.Sin(Time.unscaledTime * ArrowBobSpeed) * ArrowBobAmplitude;
        leftArrowGlyphRect.anchoredPosition = new Vector2(-bob, 0f);
        rightArrowGlyphRect.anchoredPosition = new Vector2(bob, 0f);
    }
}
