using JetBrains.Annotations;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;


public class PauseMenu : MonoBehaviour
{

    public GameObject PauseMenuUI;


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
            PauseManager.Instance.SetPaused(false);
        }

        void Pause()
        {
            PauseMenuUI.SetActive(true);
        PauseManager.Instance.SetPaused(true);
    }

        public void LoadMenu()
    {
        Debug.Log("Loading menu...");
    }

        public void QuitGame()
    {
        Debug.Log("Quitting game...");
        Application.Quit();
    }

}