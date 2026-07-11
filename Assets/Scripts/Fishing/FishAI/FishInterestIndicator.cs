using UnityEngine;
using TMPro;

// Two small billboarded text indicators shown above a zone fish so the player can read its interest
// in the tackle without staring at the bobber:
//   NOTICE ("!")   — a one-shot text pop the instant a wandering fish first turns toward the
//                    bobber/lure, paired (by the owner) with a single sound cue. Holds for a beat,
//                    then fades.
//   INVESTIGATE    — text that cycles "." -> ".." -> "..." in code WHILE the fish is circling /
//                    working the tackle (approaching, hovering, nibbling the bobber, or teasing a
//                    lure), and hides once it commits to the bite or loses interest.
//
// Both are plain TextMeshPro for now — no art needed. Owned by FishRipple, which detects the notice
// edge and the per-frame "investigating" flag; this helper owns the objects, the camera
// billboarding, the fades and the dot clock. Both are created LAZILY — only for fish that actually
// engage — and torn down with the fish (Cleanup from FishRipple.OnDisable). The notice sits on top
// of the dots, and the dots stay suppressed while the notice is still on screen, so the two read as
// a sequence: "!" (I saw it) -> "..." (let me look).
public class FishInterestIndicator
{
    public struct Settings
    {
        public string noticeText;       // the notice symbol, e.g. "!"
        public Color noticeColor;       // tint of the notice text
        public float noticeFontSize;    // TMP font size for the notice
        public float noticeDuration;    // seconds the notice holds before fading out
        public float dotCyclesPerSecond;// how fast the "." -> ".." -> "..." text steps
        public Color dotColor;          // tint of the investigating dots
        public float dotFontSize;       // TMP font size for the dots
        public float fadeTime;          // seconds either indicator fades in/out (0 = instant)
        public float heightAboveFish;   // world units above the fish the indicators float
        public float scale;             // uniform world scale applied to the indicator objects
        public int sortingOrder;        // sort order (the notice draws one above the dots)
    }

    // The dot steps cycled through while investigating.
    private static readonly string[] DotSteps = { ".", "..", "..." };

    private TextMeshPro noticeText;
    private TextMeshPro investigateText;

    private bool noticeActive;
    private float noticeTimer;      // > 0 while the notice is in its hold
    private float noticeAlpha;
    private float dotAlpha;
    private float dotClock;         // advances only while the dots are actually showing

    private bool built;

    // Fire the one-shot notice pop. The owner plays the sound itself (it holds the AudioClip and
    // the world position), so this stays purely visual.
    public void TriggerNotice(in Settings s)
    {
        EnsureBuilt(s);
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
        }

        float dt = Time.deltaTime;

        // ----- Notice "!" -----
        if (noticeActive)
        {
            noticeTimer -= dt;
            if (noticeTimer <= 0f) noticeActive = false;
        }
        noticeAlpha = Fade(noticeAlpha, noticeActive ? 1f : 0f, s.fadeTime, dt);
        ApplyNotice(fishPos, s);

        // ----- Investigate "..." ----- (held back while the notice is still on screen)
        bool showDots = investigating && !noticeActive;
        dotAlpha = Fade(dotAlpha, showDots ? 1f : 0f, s.fadeTime, dt);
        if (showDots) dotClock += dt * Mathf.Max(0.01f, s.dotCyclesPerSecond);
        ApplyDots(fishPos, s);
    }

    // Destroy the indicator objects with the fish. Safe to call more than once.
    public void Cleanup()
    {
        if (noticeText != null) Object.Destroy(noticeText.gameObject);
        if (investigateText != null) Object.Destroy(investigateText.gameObject);
        noticeText = null;
        investigateText = null;
        built = false;
        noticeActive = false;
        noticeAlpha = 0f;
        dotAlpha = 0f;
    }

    private void EnsureBuilt(in Settings s)
    {
        if (built) return;
        noticeText = CreateText("FishNoticeIndicator", s.sortingOrder + 1);
        investigateText = CreateText("FishInvestigateIndicator", s.sortingOrder);
        built = true;
    }

    // A world-space TextMeshPro (MeshRenderer-based, no Canvas) that uses the TMP default font.
    private static TextMeshPro CreateText(string name, int sortingOrder)
    {
        GameObject go = new GameObject(name);
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        var mr = tmp.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = sortingOrder;
        go.SetActive(false);
        return tmp;
    }

    private void ApplyNotice(Vector3 fishPos, in Settings s)
    {
        if (noticeText == null) return;

        bool visible = noticeAlpha > 0.001f;
        if (noticeText.gameObject.activeSelf != visible) noticeText.gameObject.SetActive(visible);
        if (!visible) return;

        noticeText.text = s.noticeText;
        noticeText.fontSize = s.noticeFontSize;
        Color c = s.noticeColor; c.a *= noticeAlpha; noticeText.color = c;
        SetSortingOrder(noticeText, s.sortingOrder + 1);
        Place(noticeText.transform, fishPos, s);
    }

    private void ApplyDots(Vector3 fishPos, in Settings s)
    {
        if (investigateText == null) return;

        bool visible = dotAlpha > 0.001f;
        if (investigateText.gameObject.activeSelf != visible) investigateText.gameObject.SetActive(visible);
        if (!visible) return;

        investigateText.text = DotSteps[Mathf.Abs((int)dotClock) % DotSteps.Length];
        investigateText.fontSize = s.dotFontSize;
        Color c = s.dotColor; c.a *= dotAlpha; investigateText.color = c;
        SetSortingOrder(investigateText, s.sortingOrder);
        Place(investigateText.transform, fishPos, s);
    }

    private static void SetSortingOrder(TextMeshPro tmp, int order)
    {
        var mr = tmp.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = order;
    }

    // Float above the fish and billboard to the camera so the flat text always faces the player.
    private static void Place(Transform t, Vector3 fishPos, in Settings s)
    {
        t.position = fishPos + Vector3.up * s.heightAboveFish;
        t.localScale = Vector3.one * Mathf.Max(0.01f, s.scale);
        Camera cam = Camera.main;
        if (cam != null) t.rotation = cam.transform.rotation;
    }

    private static float Fade(float current, float target, float fadeTime, float dt)
    {
        if (fadeTime <= 0f) return target;
        return Mathf.MoveTowards(current, target, dt / Mathf.Max(0.0001f, fadeTime));
    }
}
