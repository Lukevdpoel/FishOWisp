using UnityEngine;
using System.Collections;

public class NoteMenu : MonoBehaviour
{
    [Header("Animation")]
    [Tooltip("The Animator component on the 3D notebook object.")]
    public Animator noteAnimator;
    private int isOpenAnimHash;

    [Header("UI Integration")]
    [Tooltip("Drag the object with EncyclopediaUIController here.")]
    public EncyclopediaUIController encyclopediaUI;
    [Tooltip("How long to wait (in real seconds) for the book to open before showing the grid.")]
    public float uiDelay = 0.5f;

    private bool isNoteOpen = false;

    void Start()
    {
        isOpenAnimHash = Animator.StringToHash("IsOpen");

        // Ensure we start closed
        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, false);
        if (encyclopediaUI != null) encyclopediaUI.SetUIState(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (isNoteOpen)
            {
                CloseNotebook();
            }
            else
            {
                OpenNotebook();
            }
        }
    }

    void OpenNotebook()
    {
        isNoteOpen = true;

        // 1. Animate the book
        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, true);

        // 2. Pause Logic 
        Cursor.lockState = CursorLockMode.None; // Unlock mouse
        Cursor.visible = true;                  // Show cursor

        if (PauseManager.Instance != null)
            PauseManager.Instance.SetPaused(true);

        // 3. Show the Encyclopedia UI
        StopAllCoroutines();
        StartCoroutine(ShowUIRoutine());
    }

    void CloseNotebook()
    {
        isNoteOpen = false;

        // 1. Animate the book closed
        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, false);

        // 2. Resume Logic
        Cursor.lockState = CursorLockMode.Locked; // Lock mouse
        Cursor.visible = false;                   // Hide cursor

        if (PauseManager.Instance != null)
            PauseManager.Instance.SetPaused(false);

        // 3. Hide UI Immediately
        StopAllCoroutines();
        if (encyclopediaUI != null) encyclopediaUI.SetUIState(false);
    }

    IEnumerator ShowUIRoutine()
    {
        // IMPORTANT: WaitForSecondsRealtime works even when Time.timeScale is 0
        yield return new WaitForSecondsRealtime(uiDelay);

        if (encyclopediaUI != null) encyclopediaUI.SetUIState(true);
    }
}