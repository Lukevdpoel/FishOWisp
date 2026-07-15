using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Part of BobberController (partial class). Serialized fields live in BobberController.cs.
public partial class BobberController
{
    // Builds a looping AudioSource that mirrors the main source's 3D/mixer settings so the extra
    // loops attenuate with distance the same way, without needing to be authored on the prefab.
    private AudioSource CreateLoopSource()
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = true;
        if (audioSource != null)
        {
            s.spatialBlend = audioSource.spatialBlend;
            s.rolloffMode = audioSource.rolloffMode;
            s.minDistance = audioSource.minDistance;
            s.maxDistance = audioSource.maxDistance;
            s.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
        }
        return s;
    }

    private void PlayCastFly()
    {
        if (castLoopSource == null || castFlySound == null) return;
        castLoopSource.clip = castFlySound;
        castLoopSource.volume = castFlyVolume;
        castLoopSource.Play();
    }

    private void StopCastFly()
    {
        if (castLoopSource != null && castLoopSource.isPlaying) castLoopSource.Stop();
    }

    // The catch is decided — silence the struggle/tension loops right away. This component is
    // disabled through the catch reel + hang showcase (so Update can't drive UpdateFightAudio),
    // and ResetForCast — which normally stops the loops — is deferred until the showcase ends,
    // which left the reeling/tension loop playing over the whole fish presentation.
    public void StopFightAudioLoops()
    {
        StopCastFly();
        if (fightLoopSource != null)
        {
            if (fightLoopSource.isPlaying) fightLoopSource.Stop();
            fightLoopSource.clip = null;
        }
    }

    // Keeps the fight loop in sync with the bobber's state: struggle clip while the hooked fish
    // fights, the calmer hooked clip while it's on the line but resting/being reeled, silence
    // otherwise. Driven every frame from Update so it follows the struggle toggling for free.
    private void UpdateFightAudio()
    {
        if (fightLoopSource == null) return;

        AudioClip desired = null;
        float volume = 1f;
        if (hookedFish != null)
        {
            if (isStruggling || fightBurst) { desired = fishStruggleSound; volume = fishStruggleVolume; }
            else { desired = fishHookedIdleSound; volume = fishHookedIdleVolume; }
        }

        if (desired == null)
        {
            if (fightLoopSource.isPlaying) fightLoopSource.Stop();
            fightLoopSource.clip = null;
            return;
        }

        fightLoopSource.volume = volume;
        if (fightLoopSource.clip != desired)
        {
            fightLoopSource.clip = desired;
            fightLoopSource.Play();
        }
        else if (!fightLoopSource.isPlaying)
        {
            fightLoopSource.Play();
        }
    }

}
