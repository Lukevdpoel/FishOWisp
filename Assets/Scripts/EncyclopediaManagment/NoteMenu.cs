using UnityEngine;

public class NoteMenu : MonoBehaviour
{
    // Drag your 3D object that has the Animator component here
    [Tooltip("The Animator component on the object to be animated.")]
    public Animator noteAnimator;

    private bool isNoteOpen = false;

    // This is the name of the parameter you will create in the Animator window
    private int isOpenAnimHash;

    void Start()
    {
        // Set the hash for the "IsOpen" parameter
        isOpenAnimHash = Animator.StringToHash("IsOpen");

        // Ensure the animator starts in the closed state
        noteAnimator.SetBool(isOpenAnimHash, false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            // Toggle the boolean state
            isNoteOpen = !isNoteOpen;

            // Tell the animator to change state
            noteAnimator.SetBool(isOpenAnimHash, isNoteOpen);

            // Call your Pause/Resume logic
            // This will pause the game *as soon as* the open animation starts
            // and resume *as soon as* the close animation starts.
            if (isNoteOpen)
            {
                Pause();
            }
            else
            {
                Resume();
            }
        }
    }

    public void Resume()
    {
        // You no longer need to manually SetActive(false)
        PauseManager.Instance.SetPaused(false, true);
    }

    void Pause()
    {
        // You no longer need to manually SetActive(true)
        PauseManager.Instance.SetPaused(true, true);
    }
}