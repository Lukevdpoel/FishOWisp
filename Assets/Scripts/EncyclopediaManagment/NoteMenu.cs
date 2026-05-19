using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class NoteMenu : MonoBehaviour
{
    [Header("Animation")]
    public Animator noteAnimator;
    private int isOpenAnimHash;

    [Header("UI Integration")]
    public EncyclopediaUIController encyclopediaUI;
    public float uiDelay = 0.5f;

    private bool isNoteOpen = false;
    public bool IsNoteOpen => isNoteOpen;

    private string debugInfo = "NoteMenu: waiting...";

    void Start()
    {
        isOpenAnimHash = Animator.StringToHash("IsOpen");

        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, false);
        if (encyclopediaUI != null) encyclopediaUI.SetUIState(false);
        debugInfo = "NoteMenu: Start() ran";
    }

    void Update()
    {
        if (Keyboard.current == null)
            debugInfo = "NoteMenu: Keyboard.current is NULL";
        else
            debugInfo = "NoteMenu: Keyboard OK, isOpen=" + isNoteOpen + ", tab=" + Keyboard.current.tabKey.isPressed;

        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
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

        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (PauseManager.Instance != null)
            PauseManager.Instance.SetPaused(true);

        StopAllCoroutines();
        StartCoroutine(ShowUIRoutine());
    }

    public void CloseNotebook()
    {
        isNoteOpen = false;

        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (PauseManager.Instance != null)
            PauseManager.Instance.SetPaused(false);

        StopAllCoroutines();
        if (encyclopediaUI != null) encyclopediaUI.SetUIState(false);

        // --- NEW CODE ADDED BELOW ---
        // Access the singleton and tell it to hide the viewer (which disables the camera)
        if (ModelViewer.Instance != null)
        {
            ModelViewer.Instance.HideViewer();
        }
        // ----------------------------
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 500, 30), debugInfo);
    }

    IEnumerator ShowUIRoutine()
    {
        yield return new WaitForSecondsRealtime(uiDelay);

        if (encyclopediaUI != null) encyclopediaUI.SetUIState(true);
    }
}