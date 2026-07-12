using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Game actions that can appear as {tokens} in tutorial / description text. Authors write the verb
/// (e.g. "{reel}"), never a physical button, so one description is correct on keyboard, Xbox, PS,
/// Switch — and survives rebinding. <see cref="ButtonPrompts"/> resolves each verb to the button
/// for the active device + bindings at display time.
/// </summary>
public enum PromptVerb
{
    Cast,
    Reel,
    Attract,
    Aim,
    ResetCast,
    Hook,
    Inventory,
    Notebook,
    Jump,
    Walk,
    CycleLeftRight,
    Interact,
    // Confirm/advance a prompt (finish catch inspection). Keyboard: LMB, like Hook; gamepad: the
    // interact/confirm button (A) — kept distinct from Hook, which now rides the reel button.
    Confirm,
}

/// <summary>
/// Turns {verb} tokens in a string into device-aware button prompts, e.g.
///   "Hold {reel} to crank it in."  ->  "Hold <b><color=#FFE08A>RT</color></b> to crank it in."
///
/// Gamepad labels come from the active brand's rebindable table (<see cref="GamepadInput.ControlFor"/>)
/// so they track remaps and show RT / R2 / ZR for the right pad. Keyboard labels come from the
/// KeyboardMouseBindings table on the same asset — the table gameplay actually reads (via
/// <see cref="KeyInput"/>) — so they track keyboard remaps the same way.
///
/// Output is short text labels for now. To upgrade to glyph art, give the description TMP labels a
/// button-prompt TMP Sprite Asset and have <see cref="GamepadLabel"/> / <see cref="KeyboardLabel"/>
/// return "&lt;sprite name=...&gt;" tags instead — authored text and call sites don't change.
/// </summary>
public static class ButtonPrompts
{
    // Rich-text wrapper so a resolved prompt stands out as a "button" inside body text. The warm
    // default tint can be overridden per label via StyledLabel(v, colorHex) — see BobberBarUI's
    // inspector prompt color.
    private const string DefaultColorHex = "#FFE08A";
    private const string CloseStyle = "</color></b>";

    private static readonly Dictionary<string, PromptVerb> Tokens =
        new Dictionary<string, PromptVerb>(StringComparer.OrdinalIgnoreCase)
        {
            { "cast", PromptVerb.Cast },
            { "reel", PromptVerb.Reel },
            { "attract", PromptVerb.Attract },
            { "aim", PromptVerb.Aim },
            { "resetcast", PromptVerb.ResetCast },
            { "reset", PromptVerb.ResetCast },
            { "hook", PromptVerb.Hook },
            { "inventory", PromptVerb.Inventory },
            { "notebook", PromptVerb.Notebook },
            { "jump", PromptVerb.Jump },
            { "walk", PromptVerb.Walk },
            { "move", PromptVerb.Walk },
            { "cycle", PromptVerb.CycleLeftRight },
            { "interact", PromptVerb.Interact },
            { "confirm", PromptVerb.Confirm },
        };

    /// <summary>
    /// Replace every {verb} token in <paramref name="raw"/> with the styled prompt for the active
    /// device. Unknown tokens are left untouched (so a typo is visible, not silently dropped), and
    /// text with no '{' returns instantly.
    /// </summary>
    public static string Format(string raw) => Format(raw, StyledLabel);

    /// <summary>
    /// Same token replacement as <see cref="Format(string)"/>, but each recognized {verb} is turned
    /// into text by <paramref name="resolve"/> instead of the default styled label. This lets callers
    /// substitute richer output (e.g. inline TMP glyph sprites via <see cref="GlyphRichText"/>) while
    /// the token-parsing rules stay in one place. <paramref name="resolve"/> returns the full
    /// replacement string for a verb.
    /// </summary>
    public static string Format(string raw, Func<PromptVerb, string> resolve)
    {
        if (string.IsNullOrEmpty(raw) || raw.IndexOf('{') < 0) return raw;

        StringBuilder sb = new StringBuilder(raw.Length + 24);
        int i = 0;
        while (i < raw.Length)
        {
            int open = raw.IndexOf('{', i);
            if (open < 0) { sb.Append(raw, i, raw.Length - i); break; }
            int close = raw.IndexOf('}', open + 1);
            if (close < 0) { sb.Append(raw, i, raw.Length - i); break; }

            sb.Append(raw, i, open - i);
            string token = raw.Substring(open + 1, close - open - 1).Trim();
            if (Tokens.TryGetValue(token, out PromptVerb verb))
                sb.Append(resolve(verb));
            else
                sb.Append('{').Append(token).Append('}'); // unknown token: leave visible
            i = close + 1;
        }
        return sb.ToString();
    }

    /// <summary>The default inline prompt for a verb: the device-correct label wrapped in the warm
    /// "button" styling. This is what <see cref="Format(string)"/> substitutes for each token.</summary>
    public static string StyledLabel(PromptVerb v) => StyledLabel(v, null);

    /// <summary>Same styled prompt with a custom rich-text color (e.g. "#AABBCC" or "#AABBCCDD");
    /// null or empty falls back to the default warm tint.</summary>
    public static string StyledLabel(PromptVerb v, string colorHex) =>
        "<b><color=" + (string.IsNullOrEmpty(colorHex) ? DefaultColorHex : colorHex) + ">" + For(v) + CloseStyle;

    /// <summary>The bare (unstyled) label for a verb on the active device. Handy for HUD prompts too.</summary>
    public static string For(PromptVerb v) =>
        GamepadInput.IsGamepadActive
            ? GamepadLabel(GamepadInput.ActiveGamepadKind, GamepadInput.ControlFor(v), v)
            : KeyboardLabel(v);

    // The keys gameplay actually reads: the KeyboardMouseBindings table (via KeyInput). Walk and
    // Cycle live on the fixed movement axes / menu-nav keys, so they stay literal.
    private static string KeyboardLabel(PromptVerb v)
    {
        KeyboardMouseBindings t = GamepadInput.Bindings.keyboardMouse;
        switch (v)
        {
            case PromptVerb.Cast: return KeyLabel(t.castAim);
            case PromptVerb.Reel: return KeyLabel(t.reel);
            case PromptVerb.Attract: return KeyLabel(t.attract);
            case PromptVerb.Hook: return KeyLabel(t.reel);  // hooking shares the reel key
            case PromptVerb.Confirm: return KeyLabel(t.confirm);
            case PromptVerb.Aim: return KeyLabel(t.aim);
            case PromptVerb.ResetCast: return KeyLabel(t.lineReset);
            case PromptVerb.Interact: return KeyLabel(t.interact);
            case PromptVerb.Inventory: return KeyLabel(t.inventoryToggle);
            case PromptVerb.Notebook: return KeyLabel(t.notebookToggle);
            case PromptVerb.Jump: return KeyLabel(t.jump);
            case PromptVerb.Walk: return "WASD";
            case PromptVerb.CycleLeftRight: return "A / D";
            default: return "?";
        }
    }

    /// <summary>Human-readable label for a KeyCode: mouse buttons and modifier/special keys get
    /// friendly names, everything else falls back to the enum name.</summary>
    public static string KeyLabel(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.Mouse0: return "Left Click";
            case KeyCode.Mouse1: return "Right Click";
            case KeyCode.Mouse2: return "Middle Click";
            case KeyCode.LeftShift: case KeyCode.RightShift: return "Shift";
            case KeyCode.LeftControl: case KeyCode.RightControl: return "Ctrl";
            case KeyCode.LeftAlt: case KeyCode.RightAlt: return "Alt";
            case KeyCode.Return: return "Enter";
            case KeyCode.Escape: return "Esc";
            case KeyCode.UpArrow: return "Up Arrow";
            case KeyCode.DownArrow: return "Down Arrow";
            case KeyCode.LeftArrow: return "Left Arrow";
            case KeyCode.RightArrow: return "Right Arrow";
            default:
                if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9)
                    return ((int)(k - KeyCode.Alpha0)).ToString();
                return k.ToString();
        }
    }

    private static string GamepadLabel(GamepadKind kind, GamepadControl c, PromptVerb v)
    {
        // Verbs that aren't on a rebindable control (movement / cycle live on the sticks & d-pad).
        if (c == GamepadControl.None)
            return v switch
            {
                PromptVerb.Walk => "Left Stick",
                PromptVerb.CycleLeftRight => "D-Pad",
                _ => "?",
            };

        switch (c)
        {
            case GamepadControl.LeftTrigger:   return kind == GamepadKind.PlayStation ? "L2" : kind == GamepadKind.Nintendo ? "ZL" : "LT";
            case GamepadControl.RightTrigger:  return kind == GamepadKind.PlayStation ? "R2" : kind == GamepadKind.Nintendo ? "ZR" : "RT";
            case GamepadControl.LeftShoulder:  return kind == GamepadKind.PlayStation ? "L1" : kind == GamepadKind.Nintendo ? "L"  : "LB";
            case GamepadControl.RightShoulder: return kind == GamepadKind.PlayStation ? "R1" : kind == GamepadKind.Nintendo ? "R"  : "RB";
            case GamepadControl.ButtonSouth:   return FaceLabel(kind, 0);
            case GamepadControl.ButtonEast:    return FaceLabel(kind, 1);
            case GamepadControl.ButtonWest:    return FaceLabel(kind, 2);
            case GamepadControl.ButtonNorth:   return FaceLabel(kind, 3);
            case GamepadControl.Start:         return kind == GamepadKind.PlayStation ? "Options" : "Start";
            case GamepadControl.Select:        return kind == GamepadKind.PlayStation ? "Create" : kind == GamepadKind.Nintendo ? "-" : "Back";
            case GamepadControl.LeftStickButton:  return "L3";
            case GamepadControl.RightStickButton: return "R3";
            case GamepadControl.DpadUp:    return "D-Pad Up";
            case GamepadControl.DpadDown:  return "D-Pad Down";
            case GamepadControl.DpadLeft:  return "D-Pad Left";
            case GamepadControl.DpadRight: return "D-Pad Right";
            default: return "?";
        }
    }

    // pos: 0=South, 1=East, 2=West, 3=North. Switch maps face buttons by physical POSITION, so its
    // labels are mirrored vs. Xbox (see GamepadInput.CurrentGamepadKind / InputBindings comments).
    private static string FaceLabel(GamepadKind kind, int pos) => kind switch
    {
        GamepadKind.PlayStation => pos switch { 0 => "Cross", 1 => "Circle", 2 => "Square", _ => "Triangle" },
        GamepadKind.Nintendo => pos switch { 0 => "B", 1 => "A", 2 => "Y", _ => "X" },
        _ => pos switch { 0 => "A", 1 => "B", 2 => "X", _ => "Y" },
    };
}
