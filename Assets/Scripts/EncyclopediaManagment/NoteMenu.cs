using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

public class NoteMenu : MonoBehaviour
{
    [Header("Animation")]
    public Animator noteAnimator;
    private int isOpenAnimHash;

    [Header("Backdrop (perf: freeze world, fade Kuwahara over snapshot)")]
    [Tooltip("Main scene Base camera. Disabled while the notebook is open.")]
    public Camera mainCamera;
    [Tooltip("Lightweight Base camera that renders only the backdrop quad. Enabled while the notebook is open.")]
    public Camera backdropCamera;
    [Tooltip("RT that the main camera renders into for one frame; the backdrop quad's material samples it.")]
    public RenderTexture captureRT;
    [Tooltip("Material on the backdrop quad using the Custom/BackdropKuwahara shader. Its _Intensity is animated 0→1.")]
    public Material backdropMaterial;
    [Tooltip("Shader property name for the Kuwahara fade-in amount on the backdrop material.")]
    public string intensityProperty = "_Intensity";
    public float kuwaharaFadeDuration = 0.5f;
    [Range(0f, 1f)] public float kuwaharaMaxIntensity = 1f;

    private Coroutine kuwaharaRoutine;

    private bool isNoteOpen = false;
    public bool IsNoteOpen => isNoteOpen;

    // Static mirror of the open state so systems without a scene reference (PlayerController's
    // cursor locking, FishingRodController's click handling, PlayerCameraController) can stand
    // down while the notebook is open. The notebook also freezes Time.timeScale, but unscaled-
    // time code (camera, input) still runs, so input must be gated explicitly, like InventoryUI.
    public static bool IsNotebookOpen { get; private set; }

    // Time.timeScale as it was when the notebook opened, restored on close. Saving instead of
    // assuming 1 keeps us compatible with PauseMenu's 0.5 slow-mo Esc pause: opening the book
    // over it and closing again lands back on 0.5, not full speed.
    private float prevTimeScale = 1f;
    private bool didPause = false;

    // Reset statics before any Awake of a new play session — required with Reload Domain
    // disabled, otherwise the flag survives into the next play (see InventoryUI.ResetStatics).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsNotebookOpen = false;
    }

    private BackdropQuadFit backdropQuadFit;

    private string debugInfo = "NoteMenu: waiting...";

    void Awake()
    {
        isOpenAnimHash = Animator.StringToHash("IsOpen");

        if (noteAnimator != null)
        {
            noteAnimator.SetBool(isOpenAnimHash, false);
            // The book open/close animation must play while Time.timeScale is 0 —
            // opening the notebook is what pauses the game in the first place.
            noteAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }

        // Enforce the BackdropCamera-off invariant BEFORE the first frame renders.
        // Doing this in Start() (which runs after the first rendering pass can begin)
        // let the camera flash visible with stale captureRT content on some loads.
        // Awake() runs before any rendering, eliminating the race.
        EnforceBackdropOff();
        ClearCaptureRT();

        // Cache the quad-fit script so we can force a fit on the same frame we activate
        // the backdrop camera — otherwise [ExecuteAlways] LateUpdate can lose the race
        // against the very first render after activation.
        if (backdropCamera != null)
        {
            backdropQuadFit = backdropCamera.GetComponentInChildren<BackdropQuadFit>(includeInactive: true);
        }

        debugInfo = "NoteMenu: Awake() ran";
    }

    void OnEnable()
    {
        // Defensive: scene additive loads or runtime re-enables of the notebook root
        // shouldn't be able to leave the backdrop camera on.
        EnforceBackdropOff();
    }

    void OnDestroy()
    {
        // Scene unload while open must not leave the static flag stuck, or the next scene's
        // player would spawn with cursor/input gated forever.
        if (isNoteOpen) IsNotebookOpen = false;

        // Likewise it must not leave Time.timeScale frozen at 0 — timeScale survives
        // scene loads, so the next scene would start completely paused.
        if (didPause)
        {
            didPause = false;
            Time.timeScale = prevTimeScale;
        }
    }

    private void EnforceBackdropOff()
    {
        if (backdropCamera == null) return;
        backdropCamera.enabled = false;
        if (backdropCamera.gameObject.activeSelf) backdropCamera.gameObject.SetActive(false);
    }

    private void ClearCaptureRT()
    {
        if (captureRT == null) return;
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = captureRT;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = prev;
    }

    void Update()
    {
        if (Keyboard.current == null)
            debugInfo = "NoteMenu: Keyboard.current is NULL";
        else
            debugInfo = "NoteMenu: Keyboard OK, isOpen=" + isNoteOpen + ", tab=" + Keyboard.current.tabKey.isPressed;

        // Don't let the notebook open during the title flythrough / camera handoff — the player
        // isn't in control of the world yet. The fishing scripts are gated by MainMenuController
        // disabling them; the notebook lives in a different prefab, so it self-gates here.
        if (MainMenuController.IsTitleSequenceActive) return;

        bool togglePressed = (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                          || GamepadInput.NotebookTogglePressed;
        if (togglePressed)
        {
            if (isNoteOpen)
            {
                CloseNotebook();
            }
            else
            {
                OpenNotebook();
            }
            return;
        }

        // B / Circle backs out of the open notebook, matching the bounty board's cancel verb.
        if (isNoteOpen && GamepadInput.CancelPressed)
        {
            CloseNotebook();
        }
    }

    void OpenNotebook()
    {
        // The notebook and the inventory (with its bait bar) are mutually exclusive —
        // opening one closes the other. Routing through CloseInventory keeps all its
        // side effects (tooltip, dragged fish, vendor session, bait bar) consistent.
        if (InventoryUI.IsInventoryOpen && InventoryUI.Instance != null)
        {
            InventoryUI.Instance.CloseInventory();
        }

        isNoteOpen = true;
        IsNotebookOpen = true;

        // Open the UI first so the notebook always appears even if the backdrop step throws.
        // Previously EnterBackdrop ran first and any exception (e.g. URP camera-stack hiccup,
        // null mainCamera mid-scene-transition) would skip the animator/cursor/pause calls,
        // making Tab "do nothing" intermittently.
        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Freeze the world for real. (The old PauseManager.Instance.SetPaused call silently
        // no-opped — PauseManager exists in no scene — which is why gameplay kept advancing
        // behind the backdrop snapshot.) Everything the notebook itself animates is unscaled:
        // the book animator (set in Awake), the page-flip animator and its realtime waits,
        // the Kuwahara fade, and the encyclopedia fish's FishDisplaySway — so the book and
        // its fish stay alive while the world stops.
        if (!didPause)
        {
            prevTimeScale = Time.timeScale;
            didPause = true;
        }
        Time.timeScale = 0f;

        try
        {
            EnterBackdrop();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"NoteMenu: EnterBackdrop failed — notebook UI is still open but the world snapshot/blur is missing. {ex}");
        }
    }

    public void CloseNotebook()
    {
        isNoteOpen = false;
        IsNotebookOpen = false;

        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, false);

        // Don't steal the cursor back if another mouse-driven UI (inventory) is still open.
        if (!InventoryUI.IsInventoryOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (didPause)
        {
            didPause = false;
            Time.timeScale = prevTimeScale;
        }

        ExitBackdrop();

        // The encyclopedia content shuts itself off when its Canvas_UI parent deactivates
        // (driven by the page-flip / notebook-close system). We only handle the 3D model
        // viewer here as a belt-and-suspenders cleanup in case the lifecycle source is
        // disabled in a frame the controller doesn't observe.
        if (ModelViewer.Instance != null)
        {
            ModelViewer.Instance.HideViewer();
        }
    }

    void EnterBackdrop()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null)  { Debug.LogWarning("NoteMenu: mainCamera not assigned and Camera.main is null"); return; }
        if (backdropCamera == null) { Debug.LogWarning("NoteMenu: backdropCamera not assigned"); return; }
        if (captureRT == null)   { Debug.LogWarning("NoteMenu: captureRT not assigned"); return; }

        // Snapshot the live frame into the RT before disabling the main camera.
        // cam.Render() is synchronous and independent of Time.timeScale.
        // Strip the UI layer AND temporarily clear the URP overlay stack so the
        // notebook overlay camera doesn't render into the snapshot.
        RenderTexture prevTarget = mainCamera.targetTexture;
        int prevMask = mainCamera.cullingMask;
        var camData = mainCamera.GetUniversalAdditionalCameraData();
        List<Camera> prevStack = null;
        if (camData != null && camData.cameraStack != null && camData.cameraStack.Count > 0)
        {
            prevStack = new List<Camera>(camData.cameraStack);
            camData.cameraStack.Clear();
        }

        // Try/finally: if mainCamera.Render() throws, the targetTexture/cullingMask/cameraStack
        // mutations below would otherwise leak and break ALL subsequent rendering until the
        // scene reloads. The finally block guarantees we restore the main camera to a usable
        // state even on failure.
        try
        {
            mainCamera.targetTexture = captureRT;
            mainCamera.cullingMask = prevMask & ~(1 << LayerMask.NameToLayer("UI"));
            mainCamera.Render();
        }
        finally
        {
            mainCamera.cullingMask = prevMask;
            mainCamera.targetTexture = prevTarget;
            if (prevStack != null) camData.cameraStack.AddRange(prevStack);
        }

        mainCamera.enabled = false;
        // Toggle the GameObject (not just the component) — the BackdropCamera is
        // disabled at the GameObject level in the scene so it costs nothing at rest.
        backdropCamera.gameObject.SetActive(true);

        // Force-fit the quad NOW so the backdrop camera's first render after activation
        // samples a correctly sized/positioned quad. The [ExecuteAlways] LateUpdate on
        // BackdropQuadFit will keep it correct from this frame onward.
        if (backdropQuadFit != null) backdropQuadFit.Fit();

        backdropCamera.enabled = true;

        if (backdropMaterial != null)
        {
            if (kuwaharaRoutine != null) StopCoroutine(kuwaharaRoutine);
            float fromIntensity = backdropMaterial.GetFloat(intensityProperty);
            kuwaharaRoutine = StartCoroutine(FadeKuwahara(fromIntensity, kuwaharaMaxIntensity, null));
        }
    }

    void ExitBackdrop()
    {
        if (mainCamera == null || backdropCamera == null)
            return;

        if (kuwaharaRoutine != null) StopCoroutine(kuwaharaRoutine);

        if (backdropMaterial != null)
        {
            float fromIntensity = backdropMaterial.GetFloat(intensityProperty);
            kuwaharaRoutine = StartCoroutine(FadeKuwahara(fromIntensity, 0f, RestoreMainCamera));
        }
        else
        {
            RestoreMainCamera();
        }
    }

    void RestoreMainCamera()
    {
        if (backdropCamera != null)
        {
            backdropCamera.enabled = false;
            backdropCamera.gameObject.SetActive(false);
        }
        if (mainCamera != null) mainCamera.enabled = true;
    }

    IEnumerator FadeKuwahara(float from, float to, System.Action onComplete)
    {
        if (backdropMaterial == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        float span = Mathf.Max(0.0001f, kuwaharaMaxIntensity);
        float duration = kuwaharaFadeDuration * (Mathf.Abs(to - from) / span);

        if (duration <= 0f)
        {
            backdropMaterial.SetFloat(intensityProperty, to);
            onComplete?.Invoke();
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            backdropMaterial.SetFloat(intensityProperty, Mathf.Lerp(from, to, k));
            yield return null;
        }

        backdropMaterial.SetFloat(intensityProperty, to);
        onComplete?.Invoke();
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 500, 30), debugInfo);
    }
}
