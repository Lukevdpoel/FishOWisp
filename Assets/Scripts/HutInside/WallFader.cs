using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class WallFader : MonoBehaviour
{
    [SerializeField] private float fadeDuration = 0.4f;
    [SerializeField, Range(0f, 1f)] private float opaqueAlpha = 1f;
    [SerializeField, Range(0f, 1f)] private float fadedAlpha = 0f;
    [SerializeField] private string colorProperty = "_BaseColor";

    private Renderer rend;
    private Material instanceMat;
    private int colorId;
    private Coroutine routine;

    private void Awake()
    {
        rend = GetComponent<Renderer>();
        instanceMat = rend.material;
        colorId = Shader.PropertyToID(colorProperty);
        SetAlpha(opaqueAlpha);
        rend.enabled = true;
    }

    public Coroutine FadeOut() => StartFade(fadedAlpha, disableOnComplete: true);
    public Coroutine FadeIn()
    {
        rend.enabled = true;
        return StartFade(opaqueAlpha, disableOnComplete: false);
    }

    private Coroutine StartFade(float targetAlpha, bool disableOnComplete)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(FadeRoutine(targetAlpha, disableOnComplete));
        return routine;
    }

    private IEnumerator FadeRoutine(float targetAlpha, bool disableOnComplete)
    {
        Color start = instanceMat.GetColor(colorId);
        float startA = start.a;
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            SetAlpha(Mathf.Lerp(startA, targetAlpha, k));
            yield return null;
        }
        SetAlpha(targetAlpha);
        if (disableOnComplete && Mathf.Approximately(targetAlpha, 0f))
            rend.enabled = false;
        routine = null;
    }

    private void SetAlpha(float a)
    {
        Color c = instanceMat.GetColor(colorId);
        c.a = a;
        instanceMat.SetColor(colorId, c);
    }

    private void OnDestroy()
    {
        if (instanceMat != null) Destroy(instanceMat);
    }
}
