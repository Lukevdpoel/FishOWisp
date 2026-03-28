using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class KuwaharaRendererFeature : ScriptableRendererFeature
{
    private Material filterMaterial;
    private Material compositeMaterial;
    public RenderPassEvent renderPass = RenderPassEvent.BeforeRenderingPostProcessing;

    private KuwaharaPass kuwaharaPass;
    private CompositePass compositePass;

    public override void Create()
    {
        kuwaharaPass = new KuwaharaPass();
        kuwaharaPass.renderPassEvent = renderPass;

        compositePass = new CompositePass();
        compositePass.renderPassEvent = renderPass;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        KuwaharaVolumeComponent volume = VolumeManager.instance.stack.GetComponent<KuwaharaVolumeComponent>();

        if (volume == null || !volume.IsActive() || !renderingData.postProcessingEnabled)
            return;

        if (filterMaterial == null || compositeMaterial == null)
        {
            var shader = Shader.Find("Hidden/Kuwahara");
            if (shader == null)
            {
                Debug.LogWarning("[Kuwahara] Missing shader Hidden/Kuwahara");
                return;
            }
            if (filterMaterial == null)
                filterMaterial = CoreUtils.CreateEngineMaterial(shader);
            if (compositeMaterial == null)
                compositeMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        filterMaterial.SetInt("_KernelSize", volume.kernelSize.value);

        compositeMaterial.SetFloat("_FocalDistance", volume.focalDistance.value);
        compositeMaterial.SetFloat("_NearRange", volume.nearRange.value);
        compositeMaterial.SetFloat("_FarRange", volume.farRange.value);
        compositeMaterial.SetFloat("_MaxBlend", volume.maxBlend.value);

        kuwaharaPass.SetMaterial(filterMaterial);
        compositePass.SetMaterial(compositeMaterial);
        compositePass.SetKuwaharaPass(kuwaharaPass);

        renderer.EnqueuePass(kuwaharaPass);
        renderer.EnqueuePass(compositePass);
    }

    protected override void Dispose(bool disposing)
    {
        kuwaharaPass?.Dispose();
        compositePass?.Dispose();
    }

    class KuwaharaPass : ScriptableRenderPass
    {
        private Material m_Material;
        private TextureHandle m_KuwaharaTexHandle;

        private class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle destination;
        }

        public void SetMaterial(Material mat) => m_Material = mat;
        public TextureHandle KuwaharaTexHandle => m_KuwaharaTexHandle;

        public void Dispose() { }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            if (cameraData.isPreviewCamera) return;

            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.colorFormat = RenderTextureFormat.DefaultHDR;
            m_KuwaharaTexHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_KuwaharaTex", false);

            using (var builder = renderGraph.AddUnsafePass<PassData>("Kuwahara Filter", out var passData))
            {
                passData.material = m_Material;
                passData.source = resourceData.activeColorTexture;
                passData.destination = m_KuwaharaTexHandle;

                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.UseTexture(passData.destination, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    Blitter.BlitCameraTexture(cmd, data.source, data.destination,
                        RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                        data.material, 0);
                });
            }
        }
    }

    class CompositePass : ScriptableRenderPass
    {
        private Material m_Material;
        private KuwaharaPass m_KuwaharaPass;
        private static readonly int m_KuwaharaTexShaderID = Shader.PropertyToID("_KuwaharaTex");

        private class PassData
        {
            public Material material;
            public TextureHandle colorCopy;
            public TextureHandle kuwaharaTex;
            public TextureHandle cameraColor;
        }

        public void SetMaterial(Material mat) => m_Material = mat;
        public void SetKuwaharaPass(KuwaharaPass pass) => m_KuwaharaPass = pass;

        public void Dispose() { }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null || m_KuwaharaPass == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            if (cameraData.isPreviewCamera) return;

            // Copy camera color so we don't read and write the same texture
            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            var colorCopyHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_KuwaharaCompColorCopy", false);

            // Copy pass
            using (var builder = renderGraph.AddUnsafePass<PassData>("Kuwahara Copy Color", out var copyData))
            {
                copyData.cameraColor = resourceData.activeColorTexture;
                copyData.colorCopy = colorCopyHandle;

                builder.UseTexture(copyData.cameraColor, AccessFlags.Read);
                builder.UseTexture(copyData.colorCopy, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    Blitter.BlitCameraTexture(cmd, data.cameraColor, data.colorCopy);
                });
            }

            // Composite pass: blend color copy with kuwahara result, write to camera color
            using (var builder = renderGraph.AddUnsafePass<PassData>("Kuwahara Composite", out var passData))
            {
                passData.material = m_Material;
                passData.colorCopy = colorCopyHandle;
                passData.kuwaharaTex = m_KuwaharaPass.KuwaharaTexHandle;
                passData.cameraColor = resourceData.activeColorTexture;

                builder.UseTexture(passData.colorCopy, AccessFlags.Read);
                builder.UseTexture(passData.kuwaharaTex, AccessFlags.Read);
                builder.UseTexture(passData.cameraColor, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    // Set the kuwahara texture for the composite shader to sample
                    data.material.SetTexture(m_KuwaharaTexShaderID, (RTHandle)data.kuwaharaTex);
                    // Blitter sets _BlitTexture from colorCopy, draws to cameraColor with composite pass
                    Blitter.BlitCameraTexture(cmd, data.colorCopy, data.cameraColor,
                        RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                        data.material, 1);
                });
            }
        }
    }
}
