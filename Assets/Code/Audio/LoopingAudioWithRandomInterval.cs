using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class RandomAudioPlayerWithInterval : MonoBehaviour
{
    [Header("Audio Settings")]
    public List<AudioClip> audioClips = new List<AudioClip>();
    public bool playOnStart = true;

    [Header("Interval Settings (in seconds)")]
    public float minInterval = 1.0f;
    public float maxInterval = 5.0f;

    private AudioSource audioSource;
    private Coroutine playLoopCoroutine;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;

        if (playOnStart && audioClips.Count > 0)
        {
            StartPlayingLoop();
        }
    }

    public void StartPlayingLoop()
    {
        if (playLoopCoroutine == null)
            playLoopCoroutine = StartCoroutine(PlayLoopWithRandomAudioAndInterval());
    }

    public void StopPlayingLoop()
    {
        if (playLoopCoroutine != null)
        {
            StopCoroutine(playLoopCoroutine);
            playLoopCoroutine = null;
        }

        audioSource.Stop();
    }

    private IEnumerator PlayLoopWithRandomAudioAndInterval()
    {
        while (true)
        {
            if (audioClips.Count == 0)
                yield break;

            // Pick a random clip
            AudioClip selectedClip = audioClips[Random.Range(0, audioClips.Count)];
            audioSource.clip = selectedClip;
            audioSource.Play();

            // Wait for the clip to finish
            yield return new WaitForSeconds(selectedClip.length);

            // Wait a random interval before next clip
            float interval = Random.Range(minInterval, maxInterval);
            yield return new WaitForSeconds(interval);
        }
    }
}
