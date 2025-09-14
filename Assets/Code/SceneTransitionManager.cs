// File: SceneTransitionManager.cs
// REVISED SCRIPT

using System.Threading.Tasks; // Required for Task
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransitionManager : GenericSingleton<SceneTransitionManager>
{
    [Header("Transition Settings")]
    public Animator transition;
    [Tooltip("This should match the exact length of your fade-out animation clip.")]
    public float transitionTime = 1f;

    protected override void Awake()
    {
        base.Awake();
        if (Instance == this)
        {
            DontDestroyOnLoad(transform.root.gameObject);
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
            // You can simplify the fade-in logic
            transition.SetTrigger("End");
        }
        else
        {
            Debug.LogWarning("SceneTransitionManager: No animator assigned for transitions.");
        }
    }

    // REVISED: This method now calls the new async version.
    public void LoadScene(string sceneName)
    {
        if (transition != null)
        {
            // Fire and forget the async method.
            // The '_' discard tells the compiler we intentionally aren't awaiting the result here.
            _ = LoadSceneAsync(sceneName);
        }
        else
        {
            Debug.LogWarning("SceneTransitionManager: No animator assigned, loading instantly.");
            SceneManager.LoadScene(sceneName);
        }
    }

    // NEW: The async method that replaces the coroutine.
    private async Task LoadSceneAsync(string sceneName)
    {
        // Trigger the fade-out animation.
        transition.SetTrigger("Start");

        // Wait for the duration of the animation using Task.Delay.
        // Task.Delay expects milliseconds, so we multiply by 1000.
        await Task.Delay((int)(transitionTime * 1000));

        // Start loading the next scene in the background.
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        // You could do other things here while the scene loads.
        // By default, the scene will activate as soon as it's ready.
        // We await the operation to ensure the task completes.
        await asyncLoad;
    }
}