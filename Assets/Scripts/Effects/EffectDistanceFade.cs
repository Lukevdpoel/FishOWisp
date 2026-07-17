using UnityEngine;

// Fades an ambient effect in/out by camera distance, so the hard layer-cull never pops on screen.
// Fade factor runs 1 at fadeStart down to 0 at fadeEnd; set the camera's layer cull distance a
// little beyond fadeEnd so culling only ever removes something that is already invisible.
//
// Each effect type fades the cheap way:
//   - Particles: the emission rate is scaled down, so the existing particles die out naturally
//     over their lifetime — no shader or overdraw cost. At zero the system is stopped outright
//     (covers burst emitters, which ignore the rate multiplier).
//   - Lights: intensity is scaled, and the light is disabled entirely at zero.
//   - Mesh effects (sunshafts): a float material property (e.g. _Intensity) is scaled on a
//     per-renderer material instance, and the renderer is disabled at zero. Instances (not
//     MaterialPropertyBlocks) because the SRP Batcher sources UnityPerMaterial properties from
//     the material's own buffer and ignores per-renderer block overrides on Shader Graph shaders.
public class EffectDistanceFade : MonoBehaviour
{
    [Tooltip("Fully visible when the camera is closer than this (meters).")]
    public float fadeStart = 25f;

    [Tooltip("Fully faded out at this camera distance (meters). Keep the layer cull distance a bit beyond this.")]
    public float fadeEnd = 35f;

    [Header("What to fade (particle systems and lights auto-fill from children when left empty)")]
    public ParticleSystem[] particleSystems;
    public Light[] lights;

    [System.Serializable]
    public struct MaterialFade
    {
        [Tooltip("Renderer whose material gets faded (e.g. the sunshaft quad).")]
        public Renderer renderer;

        [Tooltip("Shader property to scale toward 0 (sunshafts use _Color; the shader is additive, so black = invisible).")]
        public string property;

        [Tooltip("Tick when the property is a Color (scales RGBA); untick for a plain float.")]
        public bool isColor;
    }

    [Tooltip("Mesh effects faded through a shader float. Not needed for particles/lights.")]
    public MaterialFade[] materialFades;

    private float[] baseEmissionRates;
    private float[] baseLightIntensities;
    private float[] basePropertyValues;
    private Color[] basePropertyColors;
    private Material[] fadeMaterials; // per-renderer instances owned (and destroyed) by this component
    private Transform cam;
    private float lastFade = -1f;

    void Awake()
    {
        if (particleSystems == null || particleSystems.Length == 0)
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        if (lights == null || lights.Length == 0)
            lights = GetComponentsInChildren<Light>(true);

        baseEmissionRates = new float[particleSystems.Length];
        for (int i = 0; i < particleSystems.Length; i++)
            baseEmissionRates[i] = particleSystems[i].emission.rateOverTimeMultiplier;

        baseLightIntensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
            baseLightIntensities[i] = lights[i].intensity;

        basePropertyValues = new float[materialFades.Length];
        basePropertyColors = new Color[materialFades.Length];
        fadeMaterials = new Material[materialFades.Length];
        for (int i = 0; i < materialFades.Length; i++)
        {
            fadeMaterials[i] = materialFades[i].renderer.material; // instantiates a private copy
            if (materialFades[i].isColor)
                basePropertyColors[i] = fadeMaterials[i].GetColor(materialFades[i].property);
            else
                basePropertyValues[i] = fadeMaterials[i].GetFloat(materialFades[i].property);
        }
    }

    void OnDestroy()
    {
        if (fadeMaterials == null) return;
        foreach (var mat in fadeMaterials)
            if (mat != null) Destroy(mat);
    }

    void Update()
    {
        if (cam == null)
        {
            var main = Camera.main;
            if (main == null) return;
            cam = main.transform;
        }

        float distance = Vector3.Distance(cam.position, transform.position);
        float fade = Mathf.Clamp01(Mathf.InverseLerp(fadeEnd, fadeStart, distance));
        if (Mathf.Approximately(fade, lastFade)) return; // nothing moved through the band, skip the writes
        lastFade = fade;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            var ps = particleSystems[i];
            var emission = ps.emission;
            emission.rateOverTimeMultiplier = baseEmissionRates[i] * fade;
            if (fade <= 0f && ps.isEmitting)
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting); // bursts ignore the rate multiplier
            else if (fade > 0f && !ps.isEmitting)
                ps.Play(false);
        }

        for (int i = 0; i < lights.Length; i++)
        {
            lights[i].intensity = baseLightIntensities[i] * fade;
            lights[i].enabled = fade > 0f;
        }

        for (int i = 0; i < materialFades.Length; i++)
        {
            if (materialFades[i].isColor)
                fadeMaterials[i].SetColor(materialFades[i].property, basePropertyColors[i] * fade);
            else
                fadeMaterials[i].SetFloat(materialFades[i].property, basePropertyValues[i] * fade);
            materialFades[i].renderer.enabled = fade > 0f;
        }
    }
}
