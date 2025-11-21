using Sirenix.OdinInspector;
using UnityEngine;

public class PauseManager : GenericSingleton<PauseManager>
{
    private bool isPaused = false;
    //private event onPausedStateChanged<bool>();
    [Button]
    public void SetPaused(bool paused, bool forceScale = false)
    {
        isPaused = paused;
        if (isPaused)
        {
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = 1f;
        }
        if(forceScale == true)
        {
            Time.timeScale = 1;
        }
    }

    public bool GetPaused()
    {
        return isPaused;
    }
}
