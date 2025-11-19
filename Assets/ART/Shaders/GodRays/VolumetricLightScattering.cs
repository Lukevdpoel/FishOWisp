using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumetricLightScattering : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Configuration")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        [Range(0.1f, 1f)] public float resolutionScale = 0.5f;
        [Range(0.0f, 1.0f)] public float intensity = 1.0f;
        [Range(0.0f, 1.0f)] public float blurWidth = 0.85f;
        [Range(4, 64)] public int numSamples = 16;

        [Header("References")]
        public Transform lightSource;
        public LayerMask occluderMask;
    }

    public Settings settings = new Settings();
    private VolumetricLightScatteringPass m_ScriptablePass;

    public override void Create()
    {
        m_ScriptablePass = new VolumetricLightScatteringPass(settings);
        m_ScriptablePass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.lightSource == null) return;

        // FIXED: We do NOT call Setup() here anymore. 
        // We just add the pass. The pass will grab the camera target itself later.
        renderer.EnqueuePass(m_ScriptablePass);
    }

    // ------------------------------------------------------------------------
    // THE RENDER PASS
    // ------------------------------------------------------------------------
    class VolumetricLightScatteringPass : ScriptableRenderPass
    {
        private readonly Settings m_Settings;
        private RTHandle m_OccludersTarget;

        // FIXED: We remove the manual "Setup" method.
        // We will access the camera target directly in Execute/OnCameraSetup.

        private readonly Material m_OccludersMaterial;
        private readonly Material m_RadialBlurMaterial;
        private readonly ShaderTagId m_ShaderTagId = new ShaderTagId("UniversalForward");

        private FilteringSettings m_FilteringSettings;

        public VolumetricLightScatteringPass(Settings settings)
        {
            m_Settings = settings;

            var occluderShader = Shader.Find("Hidden/UnlitBlack");
            if (occluderShader != null) m_OccludersMaterial = new Material(occluderShader);

            var blurShader = Shader.Find("Hidden/RadialBlur");
            if (blurShader != null) m_RadialBlurMaterial = new Material(blurShader);

            m_FilteringSettings = new FilteringSettings(RenderQueueRange.opaque, settings.occluderMask);
        }

        [System.Obsolete]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.width = (int)(descriptor.width * m_Settings.resolutionScale);
            descriptor.height = (int)(descriptor.height * m_Settings.resolutionScale);
            descriptor.colorFormat = RenderTextureFormat.Default;
            descriptor.depthBufferBits = 0;

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_OccludersTarget, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_OccludersMap");
        }

        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_OccludersMaterial == null || m_RadialBlurMaterial == null || m_Settings.lightSource == null) return;

            CommandBuffer cmd = CommandBufferPool.Get("Volumetric Light Scattering");
            Camera camera = renderingData.cameraData.camera;

            // FIXED: Access the Camera Target here, inside the scope
            RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

            Vector3 lightPos = camera.WorldToViewportPoint(m_Settings.lightSource.position);
            bool isLightInFront = lightPos.z > 0;

            if (isLightInFront)
            {
                m_RadialBlurMaterial.SetVector("_Center", new Vector4(lightPos.x, lightPos.y, 0, 0));
                m_RadialBlurMaterial.SetFloat("_Intensity", m_Settings.intensity);
                m_RadialBlurMaterial.SetFloat("_BlurWidth", m_Settings.blurWidth);
                m_RadialBlurMaterial.SetInt("_NumSamples", m_Settings.numSamples);

                // 1. Draw Occluders
                cmd.SetRenderTarget(m_OccludersTarget);
                cmd.ClearRenderTarget(false, true, Color.white);

                var drawingSettings = CreateDrawingSettings(m_ShaderTagId, ref renderingData, SortingCriteria.CommonOpaque);
                drawingSettings.overrideMaterial = m_OccludersMaterial;
                drawingSettings.overrideMaterialPassIndex = 0;

                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref m_FilteringSettings);

                // 2. Blur and Blit to Screen
                // Using the locally acquired cameraColorTarget
                cmd.Blit(m_OccludersTarget, cameraColorTarget, m_RadialBlurMaterial);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) { }

        public void Dispose()
        {
            m_OccludersTarget?.Release();
        }
    }
}