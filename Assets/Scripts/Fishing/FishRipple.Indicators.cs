using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Part of FishRipple (partial class). Serialized fields live in FishRipple.cs.
public partial class FishRipple
{
    private void ShowIndicator()
    {
        if (enableBaitIndicator) glimpse.ShowOnAim(GetGlimpseSettings());
    }
    private void HideIndicator() => glimpse.HideOnAim();

    private FishGlimpseIndicator.Settings GetGlimpseSettings()
    {
        return new FishGlimpseIndicator.Settings
        {
            alwaysShowIndicator = alwaysShowIndicator,
            aimRevealRange = aimRevealRange,
            glimpseIntervalMin = glimpseIntervalMin,
            glimpseIntervalMax = glimpseIntervalMax,
            glimpseDurationMin = glimpseDurationMin,
            glimpseDurationMax = glimpseDurationMax,
            glimpseFadeIn = glimpseFadeIn,
            glimpseFadeOut = glimpseFadeOut,
        };
    }

    // Drives the above-fish interest flame. The warm notice palette fires once on the
    // Wandering -> Attracted "notice" edge (plus the one-shot sound), holds for a beat, then the
    // flame crossfades back to the material's own colors while the fish is investigating the
    // tackle — circling/hovering it (Attracted), working a bobber (Nibbling) or teasing a lure
    // (IsLureNibbling) — and fades out once it commits to the bite, gets scared or swims off.
    private void UpdateInterestIndicators()
    {
        if (!enableInterestIndicators)
        {
            prevIndicatorState = currentState;
            return;
        }

        FishInterestIndicator.Settings s = GetInterestSettings();

        if (prevIndicatorState == FishState.Wandering && currentState == FishState.Attracted)
        {
            interestIndicator.TriggerNotice(in s);
            PlayNoticeSound();
        }

        bool investigating = currentState == FishState.Attracted
                             || currentState == FishState.Nibbling
                             || IsLureNibbling;
        // Anchor over the fish's HEAD, not the body centre or the host pivot. The body centre sits
        // mid-fish (marker floats over the middle) and the pivot sits behind the mesh (marker swings
        // off to the side as the fish turns). Bias forward from the body centre toward the rendered
        // snout so the symbol rides above the head. Keep the host's Y (the water line) so
        // indicatorHeight still measures from the surface as tuned.
        Vector3 markerPos = transform.position;
        if (modelVisual != null)
        {
            Vector3 c = modelVisual.VisualCenterWorldPosition;
            Vector3 m = modelVisual.MouthWorldPosition;
            // ~60% of the way from centre to snout lands on the head rather than the nose tip.
            Vector3 head = Vector3.Lerp(c, m, 0.6f);
            markerPos = new Vector3(head.x, transform.position.y, head.z);
        }
        interestIndicator.Tick(markerPos, investigating, in s);

        prevIndicatorState = currentState;
    }

    private void PlayNoticeSound()
    {
        if (noticeSound == null) return;
        // Debounce globally so a school noticing together plays the cue once, not layered N times.
        if (Time.unscaledTime - lastNoticeSoundTime < NoticeSoundMinInterval) return;
        lastNoticeSoundTime = Time.unscaledTime;
        AudioSource.PlayClipAtPoint(noticeSound, transform.position, noticeSoundVolume);
    }

    private FishInterestIndicator.Settings GetInterestSettings()
    {
        return new FishInterestIndicator.Settings
        {
            flamePrefab = interestFlamePrefab,
            noticePalette = noticeFlame,
            paletteBlendTime = flameBlendTime,
            noticeDuration = noticeDuration,
            fadeTime = indicatorFadeTime,
            heightAboveFish = indicatorHeight,
            scale = indicatorScale,
            sortingOrder = indicatorSortingOrder,
        };
    }

}
