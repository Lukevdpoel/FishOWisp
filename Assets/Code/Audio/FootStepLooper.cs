using UnityEngine;

[RequireComponent(typeof(AudioSource), typeof(CharacterController))]
public class FootstepLooper : MonoBehaviour
{
    public float movementThreshold = 0.1f;

    private AudioSource audioSource;
    private CharacterController controller;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        controller = GetComponent<CharacterController>();

        audioSource.loop = true;
        audioSource.playOnAwake = false;
    }

    void Update()
    {
        bool isMoving = controller.isGrounded && controller.velocity.magnitude > movementThreshold;

        if (isMoving && !audioSource.isPlaying)
        {
            audioSource.Play();
            audioSource.pitch = 3.4f; // Default is 1.0, increase for faster sound

        }
        else if (!isMoving && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
    }
}
