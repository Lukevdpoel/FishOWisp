using UnityEngine;
using UnityEngine.Rendering; // Needed for the Global Volume (Sepia effect)
using UnityEngine.UI;        // Needed to control the Image
using System.Collections;

public class PauseMenu : MonoBehaviour
{
    [Header("UI References")]
    public GameObject PauseMenuUI;
    public Image MenuImage;       // Assign the Image component with your "Eraser" material

    [Header("Post Processing")]
    public Volume PostProcessVolume; // Assign your Global Volume here

    [Header("Settings")]
    public float transitionDuration = 0.5f;

    // Internal variables
    private Material menuMaterial;
    private bool isPaused = false;
    private Coroutine transitionCoroutine;

    void Start()
    {
        // 1. Create a dynamic clone of the material so we don't edit the asset file permanently
        if (MenuImage != null)
        {
            menuMaterial = new Material(MenuImage.material);
            MenuImage.material = menuMaterial;
        }

        // 2. Ensure we start with the menu hidden and volume off
        if (PostProcessVolume != null) PostProcessVolume.weight = 0f;

        // Ensure the shader starts fully dissolved (invisible) just in case
        if (menuMaterial != null) menuMaterial.SetFloat("_DissolveAmount", 1f);

        PauseMenuUI.SetActive(false);
        Resume(); // Run standard resume logic to ensure cursor/time are correct
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused)
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
        isPaused = false;

        // Gameplay resume logic
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Time.timeScale = 1f;

        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.SetPaused(false);
        }

        // Stop any running animation and start fading OUT
        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(AnimateMenu(false));
    }

    void Pause()
    {
        isPaused = true;

        // Enable UI immediately so we can see the animation start
        PauseMenuUI.SetActive(true);

        // Gameplay pause logic
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0.5f; // Slow motion effect

        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.SetPaused(true);
        }

        // Stop any running animation and start fading IN
        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(AnimateMenu(true));
    }

    // This handles the animation for both the UI and the Volume
    IEnumerator AnimateMenu(bool show)
    {
        float timer = 0f;

        // --- THE FIX ---
        // SHADER: 1 = Invisible (Dissolved), 0 = Visible (Solid)
        // VOLUME: 0 = Normal Game, 1 = Sepia Filter

        // If Showing (Pause): Shader goes 1 -> 0 | Volume goes 0 -> 1
        // If Hiding (Resume): Shader goes 0 -> 1 | Volume goes 1 -> 0

        float startShader = show ? 1f : 0f;
        float endShader = show ? 0f : 1f;

        float startVol = show ? 0f : 1f;
        float endVol = show ? 1f : 0f;

        while (timer < transitionDuration)
        {
            // Use unscaledDeltaTime so animation works even if Time.timeScale is 0
            timer += Time.unscaledDeltaTime;

            float t = timer / transitionDuration;
            // SmoothStep math for a nicer, non-linear fade
            t = t * t * (3f - 2f * t);

            // Calculate current values
            float currentShaderVal = Mathf.Lerp(startShader, endShader, t);
            float currentVolVal = Mathf.Lerp(startVol, endVol, t);

            // Apply to Material
            if (menuMaterial != null)
                menuMaterial.SetFloat("_DissolveAmount", currentShaderVal);

            // Apply to Volume
            if (PostProcessVolume != null)
                PostProcessVolume.weight = currentVolVal;

            yield return null;
        }

        // Ensure values end exactly where they should
        if (menuMaterial != null) menuMaterial.SetFloat("_DissolveAmount", endShader);
        if (PostProcessVolume != null) PostProcessVolume.weight = endVol;

        // If we are resuming (hiding the menu), turn off the object now that it's invisible
        if (!show)
        {
            PauseMenuUI.SetActive(false);
        }
    }

    public void LoadMenu()
    {
        Time.timeScale = 1f;
        Debug.Log("Loading menu...");
    }

    public void QuitGame()
    {
        Debug.Log("Quitting game...");
        Application.Quit();
    }
}