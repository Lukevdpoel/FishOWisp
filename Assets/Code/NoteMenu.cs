using JetBrains.Annotations;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NoteMenu : MonoBehaviour
{
    public static bool GameIsPaused = false;

    public GameObject NoteMenuUI;


    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (GameIsPaused)
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
            NoteMenuUI.SetActive(false);
            GameIsPaused = false;
        }

        void Pause()
        {
            NoteMenuUI.SetActive(true);
            GameIsPaused = true;
        }

 

}