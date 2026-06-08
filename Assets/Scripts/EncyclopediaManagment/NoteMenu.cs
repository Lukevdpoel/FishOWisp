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

    private string debugInfo = "NoteMenu: waiting...";

    void Start()
    {
        isOpenAnimHash = Animator.StringToHash("IsOpen");

        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, false);

        // EnterBackdrop assumes the BackdropCamera is off at rest. If a scene authored it as active
        // (e.g. HUTBUILT did), it would render the captureRT quad over every other camera (depth 1,
        // Depth-Only clear) and paint over things like the shop interior view. Enforce the invariant
        // here so the rest of the codebase doesn't have to coordinate with the notebook.
        if (backdropCamera != null)
        {
            backdropCamera.enabled = false;
            backdropCamera.gameObject.SetActive(false);
        }

        debugInfo = "NoteMenu: Start() ran";
    }

    void Update()
    {
        if (Keyboard.current == null)
            debugInfo = "NoteMenu: Keyboard.current is NULL";
        else
            debugInfo = "NoteMenu: Keyboard OK, isOpen=" + isNoteOpen + ", tab=" + Keyboard.current.tabKey.isPressed;

        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
        {
            if (isNoteOpen)
            {
                CloseNotebook();
            }
            else
            {
                OpenNotebook();
            }
        }
    }

    void OpenNotebook()
    {
        isNoteOpen = true;

        EnterBackdrop();

        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (PauseManager.Instance != null)
            PauseManager.Instance.SetPaused(true);
    }

    public void CloseNotebook()
    {
        isNoteOpen = false;

        if (noteAnimator != null) noteAnimator.SetBool(isOpenAnimHash, false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (PauseManager.Instance != null)
            PauseManager.Instance.SetPaused(false);

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

        Debug.Log($"NoteMenu: disabling main camera '{mainCamera.name}' for backdrop");

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

        mainCamera.targetTexture = captureRT;
        mainCamera.cullingMask = prevMask & ~(1 << LayerMask.NameToLayer("UI"));
        mainCamera.Render();
        mainCamera.cullingMask = prevMask;
        mainCamera.targetTexture = prevTarget;

        if (prevStack != null)
            camData.cameraStack.AddRange(prevStack);

        mainCamera.enabled = false;
        // Toggle the GameObject (not just the component) — the BackdropCamera is
        // disabled at the GameObject level in the scene so it costs nothing at rest.
        backdropCamera.gameObject.SetActive(true);
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
