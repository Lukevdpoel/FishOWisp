using System.Collections;
using System.Linq; // Required for finding the spawn point easily
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

    // Internal variable to remember where we are going
    private string currentSpawnID;

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

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(OnSceneLoadedRoutine());
    }

    private IEnumerator OnSceneLoadedRoutine()
    {
        // 1. CRITICAL WAIT: Wait 1 frame so the new scene can initialize first.
        // This prevents the Player Prefab's own Start() script from overwriting our teleport.
        yield return null;

        // --- POSITIONING LOGIC ---
        if (!string.IsNullOrEmpty(currentSpawnID))
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            SpawnPoint point = FindSpawnPoint(currentSpawnID);

            if (player != null && point != null)
            {
                // Disable CharacterController to prevent it from fighting the teleport
                CharacterController cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                // Teleport the player
                player.transform.position = point.transform.position;
                player.transform.rotation = point.transform.rotation;

                // Force Unity to acknowledge the move immediately
                Physics.SyncTransforms();

                if (cc != null) cc.enabled = true;

                Debug.Log($"Teleport successful: Player moved to {currentSpawnID}");
            }
            else
            {
                Debug.LogWarning($"Teleport Failed: Player found? {player != null}, SpawnPoint found? {point != null}");
            }
        }
        // -------------------------

        // 2. Start fading back in (Clear screen)
        if (transition != null)
        {
            transition.SetTrigger("End");
            transition.ResetTrigger("Start");
        }

        // 3. Keep loading icon visible while fading
        yield return new WaitForSeconds(transitionTime);

        // 4. Hide loading visuals
        if (loadingIcon != null) loadingIcon.SetActive(false);
        if (progressBar != null) progressBar.fillAmount = 0f;

        isTransitioning = false;
    }

    public void LoadScene(string sceneName, string spawnID = "")
    {
        if (isTransitioning) return;

        currentSpawnID = spawnID;
        isTransitioning = true;
        StartCoroutine(LoadSceneRoutine(sceneName));
    }

    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        if (loadingIcon != null) loadingIcon.SetActive(true);
        if (transition != null) transition.SetTrigger("Start");

        yield return new WaitForSeconds(transitionTime);

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        operation.allowSceneActivation = false;

        while (!operation.isDone)
        {
            float progress = Mathf.Clamp01(operation.progress / 0.9f);

            if (progressBar != null) progressBar.fillAmount = progress;

            if (operation.progress >= 0.9f)
            {
                yield return new WaitForSeconds(0.2f);
                operation.allowSceneActivation = true;
            }

            yield return null;
        }
    }

    private SpawnPoint FindSpawnPoint(string id)
    {
        SpawnPoint[] points = FindObjectsOfType<SpawnPoint>();
        return points.FirstOrDefault(p => p.spawnID == id);
    }
}