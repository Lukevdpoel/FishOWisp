using UnityEngine;

public class ParticleTrigger : MonoBehaviour
{
    public ParticleSystem particleEffect;
    public AudioSource audioSource;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (particleEffect != null && !particleEffect.isPlaying)
            {
                particleEffect.Play();
            }

            if (audioSource != null && !audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }
    }
}
