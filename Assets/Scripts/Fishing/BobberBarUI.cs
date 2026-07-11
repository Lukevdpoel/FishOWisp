using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Twilight-Princess-style single selector for the equipped bobber/lure. Instead of a row of
// clickable slots, it shows ONLY the currently equipped tackle as a name with a description
// underneath, flanked by left/right arrows that imply you can "scroll" through your owned
// tackle. There is intentionally no icon/card: the dangling bobber model is already framed by
// the loadout camera, so the 3D model IS the picture — this is just its label.
//
// The UI is authored by you in the editor (build your own Canvas / panel / text / background and
// wire the slots below); this script only DRIVES those objects — it never creates any UI. If a
// slot is left unwired that piece simply does nothing.
//
// Cycling is driven by InventoryUI's loadout navigation (left/right stick / d-pad / A-D / arrows)
// which calls BobberInventory.SetSelectedBobber; this selector just reflects the live selection.
// The arrows are also clickable so mouse users (and the programmatic vendor / bait-missing opens,
// which aren't gamepad-steered) can still cycle.
public class BobberBarUI : MonoBehaviour
{
    [Header("Visibility")]
    [Tooltip("If true, the selector is only visible while the gear/inventory menu is open.")]
    public bool onlyShowWhileInventoryOpen = true;

    // ---- Authored layout (wire these to your own UI) ------------------------
    [Header("UI References")]
    [Tooltip("The selector's root object (holds your background + all the labels/arrows). Toggled " +
             "on/off with visibility.")]
    [SerializeField] private RectTransform blockRoot;
    [Tooltip("TMP label that shows the equipped tackle's name (the title).")]
    [SerializeField] private TextMeshProUGUI nameLabel;
    [Tooltip("TMP label that shows the equipped tackle's description.")]
    [SerializeField] private TextMeshProUGUI descriptionLabel;
    [Tooltip("TMP label for the 'press down for bait' navigation hint (shown/hidden automatically).")]
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
    [Tooltip("Optional CanvasGroup on the block, used to dim the selector on the bait screen. " +
             "Dimming is skipped if left empty.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Styling")]
    [Tooltip("Color of the inline {verb} button prompts in the description text. Only tints the " +
             "text-form prompts — Steam controller glyph art keeps its own colors.")]
    [SerializeField] private Color promptColor = new Color32(0xFF, 0xE0, 0x8A, 0xFF);

    private bool lastVisibleState = true;
    private bool isFishingActive;
    // The equipped tackle's description before {verb} prompt tokens are resolved. Kept so the
    // prompts can be re-rendered when the player swaps input device mid-menu.
    private string rawDescription = string.Empty;
    // True while this is the dimension left/right is steering (set by InventoryUI on the bobber
    // screen). Only cosmetic here — the bobber label is always visible while the menu is open;
    // the highlight just brightens it and animates the arrows.
    private bool isActiveDimension = true;

    private const float ArrowBobAmplitude = 6f;
    private const float ArrowBobSpeed = 4.5f;
    private const float DimAlpha = 0.45f;

    // --- Public API used by InventoryUI's loadout navigation (kept stable) ---
    /// <summary>Mark this as the dimension left/right is steering (brighten + animate arrows).</summary>
    public void FocusGamepad() { isActiveDimension = true; RefreshVisuals(); }
    /// <summary>This dimension is no longer steered (dim it).</summary>
    public void ClearGamepadFocus() { isActiveDimension = false; RefreshVisuals(); }

    private void Start()
    {
        WireArrowButtons();
        RefreshSelection();

        if (BobberInventory.Instance != null)
        {
            BobberInventory.Instance.OnOwnedBobbersChanged += RefreshSelection;
            BobberInventory.Instance.OnSelectedBobberChanged += HandleSelectedChanged;
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

        if (BobberInventory.Instance != null)
        {
            BobberInventory.Instance.OnOwnedBobbersChanged -= RefreshSelection;
            BobberInventory.Instance.OnSelectedBobberChanged -= HandleSelectedChanged;
        }
    }

    private void HandleThrow(Vector3 dir, float force) => isFishingActive = true;
    private void HandleFishingEnded() => isFishingActive = false;
    private void HandleSelectedChanged(BobberItem _) => RefreshSelection();

    private void HandleDeviceChanged(ActiveInputDevice _) => RefreshDescription();

    // Re-resolve the {verb} tokens in the current blurb to inline glyphs / text for the active device.
    private void RefreshDescription()
    {
        if (descriptionLabel != null) GlyphRichText.Apply(descriptionLabel, rawDescription, promptColor);
    }

    // Live-update the prompt tint while tweaking the color in the inspector during play mode.
    private void OnValidate()
    {
        if (Application.isPlaying && !string.IsNullOrEmpty(rawDescription)) RefreshDescription();
    }

    // Hook the cycle arrows to the selection stepper. Wiring your own arrow Buttons is all it takes.
    private void WireArrowButtons()
    {
        if (leftButton != null) leftButton.onClick.AddListener(() => CycleSelection(-1));
        if (rightButton != null) rightButton.onClick.AddListener(() => CycleSelection(1));
    }

    private void Update()
    {
        // The bobber label is shown the whole time the gear menu is open (active on the bobber
        // screen, dimmed on the bait screen). Visibility doesn't depend on the active-dimension
        // flag — only the brightness does.
        bool shouldShow = (onlyShowWhileInventoryOpen ? InventoryUI.IsInventoryOpen : true)
            && FishVendor.CurrentShoppingVendor == null; // never leak the selector into the 3D shop
        shouldShow &= HasAnyTackle();
        if (shouldShow != lastVisibleState) ApplyVisibility(shouldShow);

        if (lastVisibleState) AnimateArrows();
    }

    private bool HasAnyTackle()
    {
        if (BobberInventory.Instance == null) return false;
        IReadOnlyList<BobberItem> owned = BobberInventory.Instance.OwnedBobbers;
        return owned != null && owned.Count > 0;
    }

    private void ApplyVisibility(bool visible)
    {
        lastVisibleState = visible;
        if (blockRoot != null) blockRoot.gameObject.SetActive(visible);
        if (visible) RefreshVisuals();
    }

    // While not in loadout mode (programmatic vendor / bait-missing opens) there is no "active"
    // dimension, so show the label at full strength. In loadout mode, dim it on the bait screen.
    private bool Highlighted => !InventoryUI.IsLoadoutActive || isActiveDimension;

    // Step the equipped bobber/lure by delta through the owned list (wrapping). Same path the
    // left/right keys use, so clicking an arrow and pressing left/right are equivalent.
    private void CycleSelection(int delta)
    {
        if (isFishingActive) return;
        if (BobberInventory.Instance == null) return;
        IReadOnlyList<BobberItem> owned = BobberInventory.Instance.OwnedBobbers;
        if (owned == null || owned.Count == 0) return;

        int current = IndexOfSelected(owned);
        int next = current < 0 ? (delta > 0 ? 0 : owned.Count - 1)
                               : ((current + delta) % owned.Count + owned.Count) % owned.Count;
        if (owned[next] != null) BobberInventory.Instance.SetSelectedBobber(owned[next]);
    }

    private int IndexOfSelected(IReadOnlyList<BobberItem> owned)
    {
        BobberItem sel = BobberInventory.Instance.SelectedBobber;
        if (sel == null) return -1;
        for (int i = 0; i < owned.Count; i++) if (owned[i] == sel) return i;
        return -1;
    }

    // Update the name + description text to the equipped tackle, then refresh styling.
    private void RefreshSelection()
    {
        if (nameLabel == null) return;

        BobberItem sel = BobberInventory.Instance != null ? BobberInventory.Instance.SelectedBobber : null;
        if (sel == null)
        {
            nameLabel.text = "—"; // em dash
            rawDescription = string.Empty;
        }
        else
        {
            nameLabel.text = string.IsNullOrEmpty(sel.displayName) ? sel.name : sel.displayName;
            rawDescription = sel.description;
        }
        // Resolve {verb} tutorial tokens to inline glyphs / button prompts for the active device.
        RefreshDescription();
        RefreshVisuals();
    }

    private bool HasMultipleTackle()
    {
        if (BobberInventory.Instance == null) return false;
        IReadOnlyList<BobberItem> owned = BobberInventory.Instance.OwnedBobbers;
        return owned != null && owned.Count > 1;
    }

    private void RefreshVisuals()
    {
        if (blockRoot == null) return;

        // Arrows only make sense when there is more than one tackle to scroll between.
        bool showArrows = HasMultipleTackle();
        if (leftButton != null) leftButton.gameObject.SetActive(showArrows);
        if (rightButton != null) rightButton.gameObject.SetActive(showArrows);

        // The "press down for bait" hint shows only inside the loadout gear menu on the active
        // bobber screen, and only when stepping down would actually reach the bait screen
        // (a regular bobber — not a lure — is equipped and at least one bait is available).
        // Programmatic opens (vendor / bait-missing prompt) have no up/down navigation, so no hint.
        if (hintLabel != null)
            hintLabel.gameObject.SetActive(InventoryUI.IsLoadoutActive && isActiveDimension
                && !BobberInventory.IsLureEquipped && HasCyclableBait());

        if (canvasGroup != null) canvasGroup.alpha = Highlighted ? 1f : DimAlpha;
    }

    private static bool HasCyclableBait()
    {
        if (BaitInventory.Instance == null) return false;
        IReadOnlyList<BaitItem> registered = BaitInventory.Instance.RegisteredBaits;
        if (registered == null) return false;
        for (int i = 0; i < registered.Count; i++)
        {
            BaitItem b = registered[i];
            if (b == null || !b.isAvailable) continue;
            if (b.isAlwaysAvailable || BaitInventory.Instance.GetCount(b) > 0) return true;
        }
        return false;
    }

    private void AnimateArrows()
    {
        if (leftArrowGlyphRect == null || rightArrowGlyphRect == null) return;

        // Gentle in/out bob only while this dimension is the one being steered; parked otherwise.
        float bob = Highlighted ? Mathf.Sin(Time.unscaledTime * ArrowBobSpeed) * ArrowBobAmplitude : 0f;
        leftArrowGlyphRect.anchoredPosition = new Vector2(-bob, 0f);
        rightArrowGlyphRect.anchoredPosition = new Vector2(bob, 0f);
    }
}
