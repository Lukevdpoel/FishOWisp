using JetBrains.Annotations;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseMenu : MonoBehaviour
{
    public GameObject PauseMenuUI;

    // Start is called before the first frame update
    void Start()
    {
        // Ensure the game starts in the "Resumed" state 
        // (Cursor hidden, time running)
        Resume();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (PauseMenuUI.activeSelf)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }
    }

    public void Resume()
    {
        PauseMenuUI.SetActive(false);

        // 1. Lock the mouse to the center of the screen
        Cursor.lockState = CursorLockMode.Locked;
        // 2. Hide the cursor
        Cursor.visible = false;

        // 3. Unfreeze the game time
        Time.timeScale = 1f;

        // Call your manager if it exists
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.SetPaused(false);
        }
    }

    void Pause()
    {
        PauseMenuUI.SetActive(true);

        // 1. Unlock the mouse so it can move freely
        Cursor.lockState = CursorLockMode.None;
        // 2. Make the cursor visible
        Cursor.visible = true;

        // 3. Freeze the game time (stops physics and standard animations)
        Time.timeScale = 0f;

        // Call your manager if it exists
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.SetPaused(true);
        }
    }

    public void LoadMenu()
    {
        // IMPORTANT: Always unfreeze time before leaving the scene, 
        // otherwise the Main Menu will be stuck!
        Time.timeScale = 1f;

        Debug.Log("Loading menu...");
        // SceneManager.LoadScene("YourMenuSceneName");
    }

    public void QuitGame()
    {
        Debug.Log("Quitting game...");
        Application.Quit();
    }
}