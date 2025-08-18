using JetBrains.Annotations;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NoteMenu : MonoBehaviour
{

    public GameObject NoteMenuUI;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (NoteMenuUI.activeSelf)
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
            PauseManager.Instance.SetPaused(false,true);
        }

        void Pause()
        {
            NoteMenuUI.SetActive(true);

          PauseManager.Instance.SetPaused(true,true);
        }

 

}