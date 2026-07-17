using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using State = FishingRodController.FishingState;

// A Twilight-Princess-style action-prompt bar pinned to the bottom of the screen during fishing.
// It shows a row of "button + action" hints (e.g. [RT] Reel In   [Left Stick] Steer Rod) that
// swaps automatically with the current FishingState AND the equipped tackle (bobber vs lure), so
// the player always sees the inputs that matter right now. Buttons are device-aware: the same row
// reads "Left Click" on keyboard/mouse and "RT / R2 / ZR" on the matching pad, and re-renders live
// when the player swaps device mid-cast.
//
// This is an ORIGINAL implementation inspired by the general idea of a context-sensitive prompt bar
// — it uses this game's own inputs (resolved through ButtonPrompts / GamepadInput), not any
// third-party assets. Drop this component on a GameObject under the Gameplay UI canvas; it builds
// its own UI at runtime (mirrors BobberBarUI) and finds the FishingRodController on its own.
public class FishingPromptBar : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Pixels between the prompt row and the bottom of the screen.")]
    public float bottomOffset = 40f;
    [Tooltip("Horizontal gap between adjacent prompts.")]
    public float promptSpacing = 22f;
    [Tooltip("Gap between a button chip and its action label.")]
    public float chipLabelSpacing = 8f;

    [Header("Style")]
    public int buttonFontSize = 22;
    public int actionFontSize = 22;
    [Tooltip("Pixel size of the Steam Input button glyph when one is shown instead of a text chip.")]
    public int glyphSize = 34;
    public Color buttonTextColor = new Color(1f, 0.88f, 0.54f); // warm gold, matches ButtonPrompts
    public Color actionTextColor = Color.white;
    public Color chipColor = new Color(0f, 0f, 0f, 0.55f);
    [Tooltip("Add a thin dark outline to the text so it stays readable over the 3D scene.")]
    public bool addTextOutline = true;

    [Header("Visibility")]
    [Tooltip("Also show the cast / aim / jump / interact / gear / notebook prompts while the rod " +
             "is idle (not yet cast). Turn off to only show the bar once a line is actually in the water.")]
    public bool showWhileIdle = true;

    private CanvasGroup canvasGroup;
    private FishingRodController controller;
    private float controllerRetryTimer;

    // One fully-built row per (state, lure, interact, glyph-era) signature, built on first need and
    // then just SetActive-swapped. Rebuilding the row from scratch on every signature change was the
    // game's top GC allocator (AddComponent + TMP mesh churn) and hitched the frame it happened on —
    // an interact hint flickering at range boundary could retrigger it every few frames.
    private readonly Dictionary<int, RectTransform> rowCache = new Dictionary<int, RectTransform>();
    private RectTransform activeRow;
    private RectTransform selfRect;

    // Signature of what's currently drawn, so we only rebuild the row when something changes.
    private State builtState = (State)(-1);
    private bool builtLure;
    private bool builtGamepad;
    private bool builtInteract;
    private bool builtValid;

    // A single hint: a device button (resolved at build time) and the action it performs. When the
    // player is on a pad Steam Input manages, 'control' lets the chip swap its text for the real
    // controller glyph; GamepadControl.None means "no glyph, text only" (keyboard, or steering).
    private readonly struct Prompt
    {
        public readonly string button;
        public readonly string action;
        public readonly GamepadControl control;
        public Prompt(string button, string action, GamepadControl control = GamepadControl.None)
        {
            this.button = button;
            this.action = action;
            this.control = control;
        }
    }

    private void Start()
    {
        EnsureCanvasParent();
        BuildRoot();
        GamepadInput.OnActiveDeviceChanged += HandleDeviceChanged;
        SteamInputGlyphs.OnGlyphsChanged += ForceRebuild;
        ApplyVisible(false);
    }

    private void OnDestroy()
    {
        GamepadInput.OnActiveDeviceChanged -= HandleDeviceChanged;
        SteamInputGlyphs.OnGlyphsChanged -= ForceRebuild;
    }

    // Force a rebuild on the next Update so the freshly active device's labels (or, once Steam
    // starts managing a pad, its glyphs) are drawn. Cached rows hold the OLD device's labels /
    // glyphs baked into their TMP texts, so they must go too. This is rare (device swap, Steam
    // glyph load) — the steady state never rebuilds.
    private void HandleDeviceChanged(ActiveInputDevice _) => ForceRebuild();
    private void ForceRebuild()
    {
        builtState = (State)(-1);
        foreach (RectTransform row in rowCache.Values)
            if (row != null) Destroy(row.gameObject);
        rowCache.Clear();
        activeRow = null;
    }

    private void Update()
    {
        // Resolve the rod lazily but retry on a slow clock: FindFirstObjectByType is a full scene
        // scan, and in scenes without a rod (hut, cave) a per-frame retry would pay that scan —
        // plus its allocation — every single frame, forever.
        if (controller == null)
        {
            controllerRetryTimer -= Time.unscaledDeltaTime;
            if (controllerRetryTimer > 0f) { ApplyVisible(false); return; }
            controllerRetryTimer = 1f;
            controller = ResolveController();
        }

        bool show = ShouldShow(out State state);
        ApplyVisible(show);
        if (!show) return;

        bool lure = BobberInventory.IsLureEquipped;
        bool gamepad = GamepadInput.IsGamepadActive;
        // Interact only exists on the idle row (from Charging onward the rod locks the player in
        // place, so nothing interactable can be in range).
        bool interact = state == State.Idle && InteractHint.Available;

        // Only tear down and rebuild the row when the phase, tackle, device, or a nearby
        // interactable actually changes.
        if (state != builtState || lure != builtLure || gamepad != builtGamepad
            || interact != builtInteract || !builtValid)
        {
            RebuildRow(state, lure, interact);
            builtState = state;
            builtLure = lure;
            builtGamepad = gamepad;
            builtInteract = interact;
            builtValid = true;
        }
    }

    private bool ShouldShow(out State state)
    {
        state = State.Idle;
        if (controller == null) return false;

        // The notebook, gear menu, and 3D shop own the screen (and their own prompts) while open;
        // the title sequence locks gameplay UI out entirely.
        if (NoteMenu.IsNotebookOpen || InventoryUI.IsInventoryOpen
            || FishVendor.CurrentShoppingVendor != null
            || MainMenuController.IsTitleSequenceActive)
            return false;

        state = controller.currentState;

        // Transient auto-played phases have no player action to prompt.
        if (state == State.Reeling || state == State.Cooldown) return false;
        if (state == State.Idle && !showWhileIdle) return false;

        return true;
    }

    // The prompt set for a phase. Bobber and lure differ only where the verbs differ; everything
    // funnels through the same verb vocabulary so it stays correct on every device and after rebinds.
    private List<Prompt> BuildPrompts(State state, bool lure, bool interact)
    {
        var list = new List<Prompt>(4);
        switch (state)
        {
            case State.Idle:
                AddHold(list, PromptVerb.Cast, "Aim Cast");
                Add(list, PromptVerb.Aim, "Inspect");
                Add(list, PromptVerb.Jump, "Jump");
                if (interact) Add(list, PromptVerb.Interact, "Interact");
                Add(list, PromptVerb.Inventory, "Gear");
                Add(list, PromptVerb.Notebook, "Notebook");
                break;

            // Aiming: WASD / left stick steer the marker; the throw is the whip gesture on the
            // mouse / right stick (pull out, snap back the other way), not a button.
            case State.Charging:
                AddRaw(list, "WASD", "Left Stick", "Move Marker");
                AddRaw(list, "Whip Mouse", "Whip Right Stick", "Cast");
                break;

            // Bobber waiting for a bite.
            case State.WaitingForBite:
                Add(list, PromptVerb.Attract, "Attract Fish");
                Add(list, PromptVerb.ResetCast, "Reel In");
                break;

            // Lure being worked across the water (TP-style retrieve).
            case State.LureReeling:
                AddHold(list, PromptVerb.Reel, "Reel In");
                AddRaw(list, "A / D", "Left Stick", "Steer Lure");
                Add(list, PromptVerb.ResetCast, "Recast");
                break;

            // A fish is on / gripping the lure — the brief hook window.
            case State.FishOnTheLine:
                Add(list, PromptVerb.Hook, "Hook It!");
                break;

            case State.FightingFish:
                AddHold(list, PromptVerb.Reel, "Reel In");
                // The fight is played AS the fish: tank-steer it with A/D or the left stick
                // (see FishingRodController.TickFishFight) — not a rebindable verb.
                AddRaw(list, "A / D", "Left Stick", "Steer Fish");
                Add(list, PromptVerb.ResetCast, "Cut Line");
                break;

            case State.InspectingCatch:
                // Finishing inspection is the confirm button (LMB / A) — distinct from Hook, which
                // now rides the reel button so it shares a button with reeling.
                Add(list, PromptVerb.Confirm, "Continue");
                break;
        }
        return list;
    }

    private void Add(List<Prompt> list, PromptVerb verb, string action)
        => list.Add(new Prompt(ButtonPrompts.For(verb), action, GamepadInput.ControlFor(verb)));

    private void AddHold(List<Prompt> list, PromptVerb verb, string action)
        => list.Add(new Prompt(ButtonPrompts.For(verb),
            "Hold to " + char.ToLower(action[0]) + action.Substring(1), GamepadInput.ControlFor(verb)));

    // A non-rebindable input that differs by device but isn't in the verb vocabulary (steering).
    private void AddRaw(List<Prompt> list, string keyboardLabel, string gamepadLabel, string action)
        => list.Add(new Prompt(GamepadInput.IsGamepadActive ? gamepadLabel : keyboardLabel, action));

    // ---------------- UI construction (runtime, mirrors BobberBarUI) ----------------

    private void RebuildRow(State state, bool lure, bool interact)
    {
        if (selfRect == null) return;
        if (activeRow != null) activeRow.gameObject.SetActive(false);

        bool gamepad = GamepadInput.IsGamepadActive;
        int key = (int)state | (lure ? 1 << 8 : 0) | (interact ? 1 << 9 : 0) | (gamepad ? 1 << 10 : 0);
        if (!rowCache.TryGetValue(key, out RectTransform row) || row == null)
        {
            row = CreateRow();
            foreach (Prompt p in BuildPrompts(state, lure, interact))
                BuildPrompt(row, p);
            rowCache[key] = row;
        }

        row.gameObject.SetActive(true);
        activeRow = row;
    }

    private void BuildPrompt(RectTransform rowRoot, Prompt p)
    {
        GameObject promptObj = new GameObject("Prompt", typeof(RectTransform));
        promptObj.transform.SetParent(rowRoot, false);
        var hlg = promptObj.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = chipLabelSpacing;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        AddFitter(promptObj);

        // Prefer the real controller glyph from Steam Input when the player is on a managed pad and
        // this prompt maps to a glyph-able control; otherwise draw the translucent text chip. The
        // glyph is null in the editor, on keyboard, and whenever Steam isn't managing the pad, so
        // the text path is the always-present fallback.
        Sprite glyph = null;
        if (GamepadInput.IsGamepadActive && p.control != GamepadControl.None)
            SteamInputGlyphs.TryGetGlyphSprite(p.control, out glyph);

        if (glyph != null)
            BuildGlyphChip(promptObj.transform, glyph);
        else
            BuildTextChip(promptObj.transform, p.button);

        MakeText(promptObj.transform, p.action, actionFontSize, actionTextColor, FontStyles.Normal);
    }

    // Button chip: a translucent box that hugs the button label.
    private void BuildTextChip(Transform parent, string label)
    {
        GameObject chip = new GameObject("Chip", typeof(RectTransform));
        chip.transform.SetParent(parent, false);
        Image bg = chip.AddComponent<Image>();
        bg.color = chipColor;
        var chipHlg = chip.AddComponent<HorizontalLayoutGroup>();
        chipHlg.padding = new RectOffset(12, 12, 5, 5);
        chipHlg.childAlignment = TextAnchor.MiddleCenter;
        chipHlg.childControlWidth = chipHlg.childControlHeight = true;
        chipHlg.childForceExpandWidth = chipHlg.childForceExpandHeight = false;
        AddFitter(chip);
        var chipLE = chip.AddComponent<LayoutElement>();
        chipLE.minWidth = 38f;
        chipLE.minHeight = 34f;

        MakeText(chip.transform, label, buttonFontSize, buttonTextColor, FontStyles.Bold);
    }

    // Glyph chip: the Steam Input artwork drawn at glyphSize, no box behind it (the glyph already
    // reads as a button). preserveAspect keeps non-square origins (triggers) from stretching.
    private void BuildGlyphChip(Transform parent, Sprite glyph)
    {
        GameObject g = new GameObject("Glyph", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        var img = g.AddComponent<Image>();
        img.sprite = glyph;
        img.preserveAspect = true;
        img.raycastTarget = false;
        var le = g.AddComponent<LayoutElement>();
        le.preferredWidth = glyphSize;
        le.preferredHeight = glyphSize;
    }

    private TextMeshProUGUI MakeText(Transform parent, string text, int size, Color color, FontStyles style)
    {
        GameObject o = new GameObject("Label", typeof(RectTransform));
        o.transform.SetParent(parent, false);
        var t = o.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        if (addTextOutline) ApplyOutline(t);
        return t;
    }

    private static void AddFitter(GameObject o)
    {
        var f = o.AddComponent<ContentSizeFitter>();
        f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private static void ApplyOutline(TextMeshProUGUI label)
    {
        if (label == null || label.fontSharedMaterial == null) return;
        Material mat = new Material(label.fontSharedMaterial);
        mat.EnableKeyword("OUTLINE_ON");
        mat.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 0.85f));
        mat.SetFloat("_OutlineWidth", 0.18f);
        label.fontMaterial = mat;
    }

    private void BuildRoot()
    {
        selfRect = GetComponent<RectTransform>();
        if (selfRect == null) selfRect = gameObject.AddComponent<RectTransform>();
        selfRect.anchorMin = new Vector2(0f, 0f);
        selfRect.anchorMax = new Vector2(1f, 0f);
        selfRect.pivot = new Vector2(0.5f, 0f);
        selfRect.anchoredPosition = new Vector2(0f, bottomOffset);
        selfRect.sizeDelta = new Vector2(0f, 60f);

        canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    // One row container per cached signature; all rows share the anchor spot, one active at a time.
    private RectTransform CreateRow()
    {
        GameObject row = new GameObject("PromptRow", typeof(RectTransform));
        RectTransform rect = row.GetComponent<RectTransform>();
        rect.SetParent(selfRect, false);
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = Vector2.zero;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = promptSpacing;
        hlg.childAlignment = TextAnchor.LowerCenter;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        AddFitter(row);
        return rect;
    }

    private void ApplyVisible(bool visible)
    {
        if (canvasGroup != null) canvasGroup.alpha = visible ? 1f : 0f;
        if (!visible) builtValid = false; // force a fresh build next time it reappears
    }

    private FishingRodController ResolveController()
        => FindFirstObjectByType<FishingRodController>(FindObjectsInactive.Include);

    private void EnsureCanvasParent()
    {
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null) return;

        Canvas existing = FindFirstObjectByType<Canvas>();
        if (existing == null)
        {
            GameObject canvasObj = new GameObject("FishingPromptBar_Canvas");
            Canvas c = canvasObj.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            existing = c;
        }
        transform.SetParent(existing.transform, false);
    }
}
