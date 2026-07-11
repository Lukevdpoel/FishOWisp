using UnityEngine;

[RequireComponent(typeof(AudioSource), typeof(CharacterController))]
public class FootStepLooper : MonoBehaviour
{
    public float movementThreshold = 0.1f;
    public float footstepPitch = 3.4f;

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
            audioSource.pitch = footstepPitch;

        }
        else if (!isMoving && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
    }
}
