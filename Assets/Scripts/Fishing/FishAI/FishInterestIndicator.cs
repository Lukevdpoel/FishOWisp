using UnityEngine;

// A single billboarded flame quad shown above a zone fish so the player can read its interest in
// the tackle without staring at the bobber. One object, two moods, told apart by COLOR rather than
// by swapping sprites:
//   NOTICE      — the instant a wandering fish first turns toward the bobber/lure, the flame pops
//                 up shifted to the warmer notice palette (paired by the owner with a single sound
//                 cue), holds for a beat, then crossfades back to...
//   INVESTIGATE — the flame's own authored material colors (no override — tune the look on the
//                 material itself), shown WHILE the fish is circling / working the tackle
//                 (approaching, hovering, nibbling the bobber, or teasing a lure), hidden once it
//                 commits to the bite or loses interest.
//
// The visual is an owner-authored prefab: a quad with the Flame_Texture_Wobble material, typically
// carrying BillboardSprite (camera facing) and FlameLag (tip trails the fish's motion). This class
// only positions it and drives the shader through a MaterialPropertyBlock: _CoreColor/_OuterColor/
// _GlowColor crossfade between palettes, and _Fade ramps 0..1 so the flame fades in and out
// instead of popping. Emission intensity rides in the HDR _GlowColor — the shader's _Glow is the
// glow TEXTURE, not a float, so there is no separate intensity scalar to drive. Owned by
// FishRipple, created LAZILY — only for fish that actually engage —
// and torn down with the fish (Cleanup from FishRipple.OnDisable).
public class FishInterestIndicator
{
    // The flame shader colors for one mood. Lerped, not swapped, when the mood changes.
    [System.Serializable]
    public struct FlamePalette
    {
        [ColorUsage(true, true)] public Color core;   // -> _CoreColor (inner flame)
        [ColorUsage(true, true)] public Color outer;  // -> _OuterColor (outer flame)
        [ColorUsage(true, true)] public Color glow;   // -> _GlowColor (emission tint + intensity, HDR)

        public static FlamePalette Lerp(in FlamePalette a, in FlamePalette b, float t)
        {
            return new FlamePalette
            {
                core = Color.Lerp(a.core, b.core, t),
                outer = Color.Lerp(a.outer, b.outer, t),
                glow = Color.Lerp(a.glow, b.glow, t),
            };
        }
    }

    public struct Settings
    {
        public GameObject flamePrefab;      // owner-authored flame quad; nothing shows while null
        public FlamePalette noticePalette;  // the warm shift; base colors come from the material
        public float paletteBlendTime;      // seconds a notice <-> base crossfade takes
        public float noticeDuration;        // seconds the notice palette holds before blending back
        public float fadeTime;              // seconds the flame fades in/out (0 = instant)
        public float heightAboveFish;       // world units above the fish the flame floats
        public float scale;                 // multiplier on top of the prefab's authored scale
        public int sortingOrder;            // renderer sorting order
    }

    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    private static readonly int OuterColorId = Shader.PropertyToID("_OuterColor");
    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");

    private GameObject flame;
    private Renderer flameRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Vector3 prefabScale = Vector3.one;
    private FlamePalette basePalette;   // the material's authored colors, sampled at build

    private bool built;
    private bool noticeActive;
    private float noticeTimer;      // > 0 while the notice is in its hold
    private float alpha;            // drives _Fade

    // Palette crossfade state: from -> to over blend 0..1.
    private FlamePalette fromPalette;
    private FlamePalette toPalette;
    private float blend = 1f;
    private bool showingNotice;
    private bool moodInitialized;

    // Fire the one-shot notice pop. The owner plays the sound itself (it holds the AudioClip and
    // the world position), so this stays purely visual.
    public void TriggerNotice(in Settings s)
    {
        EnsureBuilt(s);
        if (!built) return;
        noticeActive = true;
        noticeTimer = Mathf.Max(0.01f, s.noticeDuration);
    }

    // Called every frame by FishRipple with the fish's world position and whether it is currently
    // investigating (circling/working the tackle, not yet committed to the bite).
    public void Tick(Vector3 fishPos, bool investigating, in Settings s)
    {
        // Stay cheap for the many fish that never engage: nothing is built until a notice fires or
        // investigation begins, and once idle again the fades still need to run to completion.
        if (!built)
        {
            if (!investigating && !noticeActive) return;
            EnsureBuilt(s);
            if (!built) return; // no prefab assigned
        }

        float dt = Time.deltaTime;

        if (noticeActive)
        {
            noticeTimer -= dt;
            if (noticeTimer <= 0f) noticeActive = false;
        }

        // While hidden, snap the palette so the flame never fades in mid-crossfade in stale colors.
        SetMood(noticeActive, in s, instant: alpha <= 0.001f);
        if (s.paletteBlendTime <= 0f) blend = 1f;
        else blend = Mathf.MoveTowards(blend, 1f, dt / s.paletteBlendTime);

        bool visible = noticeActive || investigating;
        alpha = Fade(alpha, visible ? 1f : 0f, s.fadeTime, dt);

        Apply(fishPos, in s);
    }

    // Destroy the flame with the fish. Safe to call more than once.
    public void Cleanup()
    {
        if (flame != null) Object.Destroy(flame);
        flame = null;
        flameRenderer = null;
        built = false;
        noticeActive = false;
        alpha = 0f;
        blend = 1f;
        moodInitialized = false;
    }

    private void EnsureBuilt(in Settings s)
    {
        if (built) return;
        if (s.flamePrefab == null) return; // owner hasn't wired the flame prefab yet

        flame = Object.Instantiate(s.flamePrefab);
        flame.name = "FishInterestFlame";
        flameRenderer = flame.GetComponentInChildren<Renderer>(true);
        if (flameRenderer == null)
        {
            Debug.LogWarning("FishInterestIndicator: flame prefab has no Renderer.", s.flamePrefab);
            Object.Destroy(flame);
            flame = null;
            return;
        }
        prefabScale = flame.transform.localScale;

        // The investigate look IS the material — sample its colors once so the notice shift can
        // crossfade from/to them.
        Material mat = flameRenderer.sharedMaterial;
        basePalette = new FlamePalette
        {
            core = mat != null && mat.HasProperty(CoreColorId) ? mat.GetColor(CoreColorId) : Color.white,
            outer = mat != null && mat.HasProperty(OuterColorId) ? mat.GetColor(OuterColorId) : Color.white,
            glow = mat != null && mat.HasProperty(GlowColorId) ? mat.GetColor(GlowColorId) : Color.white,
        };

        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
        flame.SetActive(false);
        built = true;
    }

    private void SetMood(bool notice, in Settings s, bool instant)
    {
        FlamePalette target = notice ? s.noticePalette : basePalette;

        if (!moodInitialized || instant)
        {
            fromPalette = target;
            toPalette = target;
            blend = 1f;
            showingNotice = notice;
            moodInitialized = true;
            return;
        }

        if (showingNotice != notice)
        {
            fromPalette = CurrentPalette();
            toPalette = target;
            blend = 0f;
            showingNotice = notice;
            return;
        }

        // Same mood: keep tracking the settings so inspector tuning shows up live.
        toPalette = target;
    }

    private FlamePalette CurrentPalette() => FlamePalette.Lerp(in fromPalette, in toPalette, blend);

    private void Apply(Vector3 fishPos, in Settings s)
    {
        if (flame == null) return;

        bool show = alpha > 0.001f;
        if (flame.activeSelf != show) flame.SetActive(show);
        if (!show) return;

        // Position and scale only — facing is BillboardSprite's job and the motion-trail is
        // FlameLag's, both living on the prefab.
        flame.transform.position = fishPos + Vector3.up * s.heightAboveFish;
        flame.transform.localScale = prefabScale * Mathf.Max(0.01f, s.scale);

        FlamePalette p = CurrentPalette();
        flameRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(FadeId, alpha);
        propertyBlock.SetColor(CoreColorId, p.core);
        propertyBlock.SetColor(OuterColorId, p.outer);
        propertyBlock.SetColor(GlowColorId, p.glow);
        flameRenderer.SetPropertyBlock(propertyBlock);

        if (flameRenderer.sortingOrder != s.sortingOrder)
            flameRenderer.sortingOrder = s.sortingOrder;
    }

    private static float Fade(float current, float target, float fadeTime, float dt)
    {
        if (fadeTime <= 0f) return target;
        return Mathf.MoveTowards(current, target, dt / Mathf.Max(0.0001f, fadeTime));
    }
}
