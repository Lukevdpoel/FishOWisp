using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using System.Collections;

public class PauseMenu : MonoBehaviour
{
    [Header("UI References")]
    public GameObject PauseMenuUI;
    public Image MenuImage;

    [Header("Post Processing")]
    public Volume PostProcessVolume;

    [Header("Fullscreen Shader")]
    public Material fullScreenMaterial;
    public string shaderIntensityName = "_Intensity";

    [Header("Settings")]
    public float transitionDuration = 0.5f;

    // Internal variables
    private Material menuMaterial;
    private bool isPaused = false;
    private Coroutine transitionCoroutine;

    void Start()
    {
        if (MenuImage != null)
        {
            menuMaterial = new Material(MenuImage.material);
            MenuImage.material = menuMaterial;
            menuMaterial.SetFloat("_DissolveAmount", 1f);
        }

        if (PostProcessVolume != null)
            PostProcessVolume.weight = 0f;

        if (fullScreenMaterial != null)
            fullScreenMaterial.SetFloat(shaderIntensityName, 0f);

        PauseMenuUI.SetActive(false);
        Resume();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused) Resume();
            else Pause();
        }
    }

    public void Resume()
    {
        isPaused = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Time.timeScale = 1f;

        if (PauseManager.Instance != null)
            PauseManager.Instance.SetPaused(false);

        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(AnimateMenu(false));
    }

    void Pause()
    {
        isPaused = true;
        PauseMenuUI.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0.5f;

        if (PauseManager.Instance != null)
            PauseManager.Instance.SetPaused(true);

        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(AnimateMenu(true));
    }

    IEnumerator AnimateMenu(bool show)
    {
        float timer = 0f;

        // Eraser Shader: 1 = Invisible, 0 = Visible
        float startDissolve = show ? 1f : 0f;
        float endDissolve = show ? 0f : 1f;

        // Effects (Volume & Fullscreen): 0 = Off, 1 = On
        float startEffect = show ? 0f : 1f;
        float endEffect = show ? 1f : 0f;

        while (timer < transitionDuration)
        {
            timer += Time.unscaledDeltaTime;
            float t = timer / transitionDuration;

            // SmoothStep
            t = t * t * (3f - 2f * t);

            float currentDissolve = Mathf.Lerp(startDissolve, endDissolve, t);
            float currentEffect = Mathf.Lerp(startEffect, endEffect, t);

            if (menuMaterial != null)
                menuMaterial.SetFloat("_DissolveAmount", currentDissolve);

            if (PostProcessVolume != null)
                PostProcessVolume.weight = currentEffect;

            if (fullScreenMaterial != null)
                fullScreenMaterial.SetFloat(shaderIntensityName, currentEffect);

            yield return null;
        }

        // Finalize values
        if (menuMaterial != null) menuMaterial.SetFloat("_DissolveAmount", endDissolve);
        if (PostProcessVolume != null) PostProcessVolume.weight = endEffect;
        if (fullScreenMaterial != null) fullScreenMaterial.SetFloat(shaderIntensityName, endEffect);

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