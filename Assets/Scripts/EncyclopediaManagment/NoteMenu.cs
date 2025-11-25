using UnityEngine;
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

    void Start()
    {
        isOpenAnimHash = Animator.StringToHash("IsOpen");

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

        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (PauseManager.Instance != null)
            PauseManager.Instance.SetPaused(true);

        StopAllCoroutines();
        StartCoroutine(ShowUIRoutine());
    }

    void CloseNotebook()
    {
        isNoteOpen = false;

        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (PauseManager.Instance != null)
            PauseManager.Instance.SetPaused(false);

        StopAllCoroutines();
        if (encyclopediaUI != null) encyclopediaUI.SetUIState(false);
    }

    IEnumerator ShowUIRoutine()
    {
        yield return new WaitForSecondsRealtime(uiDelay);

        if (encyclopediaUI != null) encyclopediaUI.SetUIState(true);
    }
}