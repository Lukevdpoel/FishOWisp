using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransitionManager : GenericSingleton<SceneTransitionManager>
{
    [Header("Transition Settings")]
    public Animator transition;
    public float transitionTime = 1f;

    private void Awake()
    {
        if (Instance == null || Instance == this)
        {
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (transition != null)
        {
            // Reset the fade-out trigger in case it’s still active
            transition.ResetTrigger("Start");

            // Force the fade-in animation to play from start
            transition.Play("FadeIdle", 0, 0f); // Replace "FadeIdle" with your default idle state
            transition.SetTrigger("End"); // fade-in
        }
        else
        {
            Debug.LogWarning("SceneTransitionManager: No animator assigned for transitions.");
        }
    }

    public void LoadScene(string sceneName)
    {
        if (transition != null)
        {
            StartCoroutine(LoadSceneRoutine(sceneName));
        }
        else
        {
            Debug.LogWarning("SceneTransitionManager: No animator assigned, loading instantly.");
            SceneManager.LoadScene(sceneName);
        }
    }

    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        if (transition != null)
        {
            // Reset fade-in trigger so fade-out will respond
            transition.ResetTrigger("End");

            // Trigger fade-out
            transition.SetTrigger("Start");

            yield return new WaitForSeconds(transitionTime);

            SceneManager.LoadScene(sceneName);
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }
}
