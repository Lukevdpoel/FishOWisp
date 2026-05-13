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
        // Wait two frames so the new scene's Awake AND Start have both run before we teleport.
        yield return null;
        yield return null;

        // --- POSITIONING LOGIC ---
        if (!string.IsNullOrEmpty(currentSpawnID))
        {
            SpawnPoint point = FindSpawnPoint(currentSpawnID);

            // The CharacterController is the GameObject that *is* the character physically.
            // In PlayerMaster the CC is on a nested child with a non-zero local offset relative to the
            // PlayerMaster root, so we must teleport the CC's transform directly (NOT transform.root) for
            // the visible character to land at the spawn point gizmo.
            CharacterController cc = FindFirstObjectByType<CharacterController>(FindObjectsInactive.Include);

            if (cc != null && point != null)
            {
                Transform characterTransform = cc.transform;

                cc.enabled = false;
                characterTransform.SetPositionAndRotation(point.transform.position, point.transform.rotation);
                Physics.SyncTransforms();
                cc.enabled = true;

                // The visible facing is controlled by PlayerController's playerModel transform, not the
                // CharacterController. Drive it explicitly so it doesn't slerp back to the pre-teleport facing.
                PlayerController playerController = cc.GetComponentInParent<PlayerController>();
                if (playerController == null) playerController = cc.GetComponentInChildren<PlayerController>(true);
                if (playerController != null) playerController.SetFacing(point.transform.rotation);

                Debug.Log($"[SceneTransition] Teleported '{characterTransform.name}' to spawn '{currentSpawnID}' at {point.transform.position}.");
            }
            else
            {
                string available = string.Join(", ", FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(p => p.spawnID));
                Debug.LogWarning($"[SceneTransition] Teleport failed (spawnID='{currentSpawnID}'). CharacterController found? {cc != null}. SpawnPoint found? {point != null}. Available spawn IDs in scene: [{available}].");
            }

            // Clear so a later scene load without an ID doesn't re-apply this teleport.
            currentSpawnID = "";
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
        // Include inactive in case the spawn point sits under a disabled grouping object.
        SpawnPoint[] points = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return points.FirstOrDefault(p => p.spawnID == id);
    }
}