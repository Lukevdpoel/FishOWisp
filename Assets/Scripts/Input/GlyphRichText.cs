using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

/// <summary>
/// Renders {verb} tutorial tokens as inline Steam Input button GLYPHS inside a flowing
/// <see cref="TMP_Text"/> — the same artwork the <see cref="FishingPromptBar"/> shows as standalone
/// chips, but embedded in wrapping description prose (bait / bobber / lure blurbs).
///
/// TMP can only draw inline images through a <see cref="TMP_SpriteAsset"/>, and Steam hands us a
/// separate runtime PNG per controller button, so we build a tiny sprite asset per button on demand
/// (one glyph each) and chain them as fallbacks under a shared <see cref="root"/> asset. A description
/// then references them with standard <c>&lt;sprite name="ButtonSouth"&gt;</c> tags. When the player
/// isn't on a Steam-managed pad (keyboard, or Steam Input unavailable) each verb falls back to the
/// device-correct <see cref="ButtonPrompts"/> text, so prose is never left blank — same contract as
/// <see cref="GamepadGlyph"/>.
///
/// Call <see cref="Apply"/> whenever the text OR the active device / glyph set changes; it assigns the
/// sprite asset and the resolved string in one go. The glyph set is rebuilt lazily after Steam reports
/// a new controller/config or the active device flips.
/// </summary>
public static class GlyphRichText
{
    private static TMP_SpriteAsset root;
    private static readonly Dictionary<GamepadControl, TMP_SpriteAsset> byControl = new();
    private static bool dirty = true;

    static GlyphRichText()
    {
        // A (re)connect, Steam config change, or keyboard<->pad flip can change every glyph, so drop
        // the cached set and rebuild it on the next Apply.
        SteamInputGlyphs.OnGlyphsChanged += MarkDirty;
        GamepadInput.OnActiveDeviceChanged += _ => MarkDirty();
    }

    private static void MarkDirty() => dirty = true;

    /// <summary>
    /// Fill <paramref name="label"/> with <paramref name="raw"/>, replacing each {verb} token with an
    /// inline controller glyph when one is available and text otherwise. Also assigns the glyph sprite
    /// asset to the label. Safe to call every frame or on any input/selection change.
    /// </summary>
    public static void Apply(TMP_Text label, string raw) => Apply(label, raw, null);

    /// <summary>
    /// Same as <see cref="Apply(TMP_Text, string)"/> but tints the text-form prompts with
    /// <paramref name="promptColor"/> instead of the default warm yellow. Only affects the styled
    /// TEXT fallbacks — Steam glyph art is full-color button artwork and keeps its own colors.
    /// </summary>
    public static void Apply(TMP_Text label, string raw, Color? promptColor)
    {
        if (label == null) return;
        EnsureRoot();
        if (dirty) Rebuild();          // wipe stale per-device glyphs before we resolve tokens
        label.spriteAsset = root;      // tags below are looked up here + its fallbacks
        string hex = promptColor.HasValue ? "#" + ColorUtility.ToHtmlStringRGBA(promptColor.Value) : null;
        label.text = ButtonPrompts.Format(raw, v => Resolve(v, hex));
    }

    // Per-verb replacement: a glyph sprite tag on a managed pad, else the standard styled text label.
    private static string Resolve(PromptVerb v, string colorHex)
    {
        if (GamepadInput.IsGamepadActive)
        {
            GamepadControl c = GamepadInput.ControlFor(v);
            if (c != GamepadControl.None && EnsureGlyph(c))
                return $"<sprite name=\"{c}\">";
        }
        return ButtonPrompts.StyledLabel(v, colorHex);
    }

    // Build (once, cached) the single-glyph sprite asset for a control and register it as a fallback.
    // Returns false when Steam has no glyph for it, so the caller emits text instead.
    private static bool EnsureGlyph(GamepadControl c)
    {
        if (byControl.TryGetValue(c, out TMP_SpriteAsset cached))
            return cached != null;

        TMP_SpriteAsset asset = BuildGlyphAsset(c);
        byControl[c] = asset;                       // cache null too, so we don't retry every frame
        if (asset != null) root.fallbackSpriteAssets.Add(asset);
        return asset != null;
    }

    private static TMP_SpriteAsset BuildGlyphAsset(GamepadControl c)
    {
        if (!SteamInputGlyphs.TryGetGlyphSprite(c, out Sprite sprite) || sprite == null)
            return null;

        Texture2D tex = sprite.texture;
        int w = tex.width, h = tex.height;

        var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
        asset.name = "GlyphSA_" + c;
        asset.hideFlags = HideFlags.HideAndDontSave;
        asset.spriteSheet = tex;

        // faceInfo.pointSize is the reference size TMP scales the glyph against, so mapping it to the
        // texture height makes the glyph render at roughly one line (fontSize) tall regardless of the
        // PNG's native resolution. bearingY = h seats the glyph on the text baseline.
        asset.faceInfo = new FaceInfo
        {
            familyName = asset.name,
            pointSize  = h,
            scale      = 1f,
            lineHeight = h,
            ascentLine = h,
            capLine    = h,
            meanLine   = h * 0.5f,
            baseline   = 0f,
            descentLine = 0f,
        };

        var glyph = new TMP_SpriteGlyph(0u, new GlyphMetrics(w, h, 0f, h, w), new GlyphRect(0, 0, w, h), 1f, 0)
        {
            sprite = sprite
        };
        asset.spriteGlyphTable.Add(glyph);

        var character = new TMP_SpriteCharacter(0u, glyph) { name = c.ToString(), glyphIndex = 0u };
        asset.spriteCharacterTable.Add(character);

        // Build the lookups BEFORE assigning a material: TMP only runs its legacy-format upgrade
        // (which NREs on the null spriteInfoList of a runtime asset) when material != null AND the
        // version is empty. With material still null the upgrade is skipped, and the lookups it caches
        // here mean TMP never calls UpdateLookupTables again — so assigning the material now is safe.
        asset.UpdateLookupTables();
        asset.material = new Material(Shader.Find("TextMeshPro/Sprite")) { hideFlags = HideFlags.HideAndDontSave };
        asset.material.mainTexture = tex;
        return asset;
    }

    private static void EnsureRoot()
    {
        if (root != null) return;
        root = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
        root.name = "GlyphSA_Root";
        root.hideFlags = HideFlags.HideAndDontSave;
        // A valid (if empty) material/sheet keeps TMP happy; the actual glyphs live in the fallbacks.
        root.spriteSheet = Texture2D.whiteTexture;
        if (root.fallbackSpriteAssets == null) root.fallbackSpriteAssets = new List<TMP_SpriteAsset>();
        // Build lookups while material is null so TMP skips its (NRE-prone) legacy upgrade; see the
        // note in BuildGlyphAsset. Assign the material afterward.
        root.UpdateLookupTables();
        root.material = new Material(Shader.Find("TextMeshPro/Sprite")) { hideFlags = HideFlags.HideAndDontSave };
        root.material.mainTexture = Texture2D.whiteTexture;
    }

    // Drop every per-device glyph asset (the Sprites themselves are owned + freed by SteamInputGlyphs).
    private static void Rebuild()
    {
        EnsureRoot();
        foreach (TMP_SpriteAsset a in byControl.Values)
        {
            if (a == null) continue;
            if (a.material != null) Object.Destroy(a.material);
            Object.Destroy(a);
        }
        byControl.Clear();
        root.fallbackSpriteAssets.Clear();
        dirty = false;
    }
}
