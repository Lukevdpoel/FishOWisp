using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransitionManager : GenericSingleton<SceneTransitionManager>
{
    [Header("Transition Settings")]
    public Animator transition;
    public float transitionTime = 1f;

    private bool isTransitioning = false;

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
            Debug.Log("SceneTransitionManager: Setting trigger End");
            transition.SetTrigger("End");
            transition.ResetTrigger("Start");
        }
        else
        {
            Debug.LogWarning("SceneTransitionManager: No animator assigned for transitions.");
        }

        isTransitioning = false;
    }

    public void LoadScene(string sceneName)
    {
        if (isTransitioning)
        {
            return;
        }

        if (transition != null)
        {
            isTransitioning = true;
            _ = LoadSceneAsync(sceneName);
        }
        else
        {
            Debug.LogWarning("SceneTransitionManager: No animator assigned, loading instantly.");
            SceneManager.LoadScene(sceneName);
        }
    }

    private async Task LoadSceneAsync(string sceneName)
    {
        Debug.Log("SceneTransitionManager: Setting trigger Start");
        transition.SetTrigger("Start");

        await Task.Delay((int)(transitionTime * 1000));

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        await asyncLoad;
    }
}