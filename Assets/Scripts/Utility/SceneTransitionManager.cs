using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneTransitionManager : GenericSingleton<SceneTransitionManager>
{
    [Header("Transition Settings")]
    public Animator transition;
    public float transitionTime = 1f;

    [Header("Loading Screen Elements")]
    public GameObject loadingIcon;
    public Image progressBar;

    private bool isTransitioning = false;

    protected override void Awake()
    {
        base.Awake();
        if (Instance == this)
        {
            DontDestroyOnLoad(transform.root.gameObject);
        }
    }

    private void Start()
    {
        // Ensure everything is hidden when the game first boots up
        if (loadingIcon != null) loadingIcon.SetActive(false);
        if (progressBar != null) progressBar.fillAmount = 0f;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // This function fires automatically when the new scene is ready
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(OnSceneLoadedRoutine());
    }

    private IEnumerator OnSceneLoadedRoutine()
    {
        // 1. Trigger the fade OUT of black (back to clear)
        if (transition != null)
        {
            transition.SetTrigger("End");
            transition.ResetTrigger("Start");
        }

        // 2. WAIT here while the screen fades back to clear.
        // The icon is still visible during this time.
        yield return new WaitForSeconds(transitionTime);

        // 3. NOW hide the loading elements
        if (loadingIcon != null)
        {
            loadingIcon.SetActive(false);
        }

        if (progressBar != null)
        {
            progressBar.fillAmount = 0f;
        }

        // 4. Finally, unlock the system for the next transition
        isTransitioning = false;
    }

    public void LoadScene(string sceneName)
    {
        if (isTransitioning) return;

        isTransitioning = true;
        StartCoroutine(LoadSceneRoutine(sceneName));
    }

    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        // Turn on the icon immediately when transition starts
        if (loadingIcon != null)
        {
            loadingIcon.SetActive(true);
        }

        // Start fading to black
        if (transition != null)
        {
            transition.SetTrigger("Start");
        }

        // Wait for the screen to go fully black
        yield return new WaitForSeconds(transitionTime);

        // Load the new scene in the background
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        operation.allowSceneActivation = false;

        while (!operation.isDone)
        {
            float progress = Mathf.Clamp01(operation.progress / 0.9f);

            if (progressBar != null)
            {
                progressBar.fillAmount = progress;
            }

            // When the scene is ready (90%)
            if (operation.progress >= 0.9f)
            {
                // Optional: Short pause to let the user see the "100%" state
                yield return new WaitForSeconds(0.2f);

                // Finish the load (this might cause a brief frame freeze)
                operation.allowSceneActivation = true;
            }

            yield return null;
        }
    }
}