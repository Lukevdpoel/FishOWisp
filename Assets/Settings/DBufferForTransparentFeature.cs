using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// Keeps URP's DBuffer (decal) textures bound as globals through the transparent pass so that
/// custom transparent shaders — e.g. the baked moss overlay (Fur/Shell/Lit Baked) — can receive
/// DBuffer decals (like the LightDecal projector).
///
/// Why this is needed: URP renders decals into _DBufferTexture0/1/2 in a prepass and lets OPAQUE
/// geometry read them during the opaque forward pass. Under Render Graph the DBuffer resources are
/// then freed, because no later pass declares reading them — so by the time transparent objects
/// render, the textures are gone. This feature adds a no-op pass at BeforeRenderingTransparents that
/// re-declares a read on the DBuffer and re-binds it as a global, which extends its lifetime and
/// keeps it available to the transparent queue (the same trick URP uses for _CameraOpaqueTexture).
///
/// Setup: add this feature to your URP Renderer (PC_Renderer). No settings. The moss shader already
/// samples the DBuffer via the _DBUFFER_MRTx keywords + ApplyDecalToSurfaceData.
/// </summary>
public class DBufferForTransparentFeature : ScriptableRendererFeature
{
    private class KeepDBufferPass : ScriptableRenderPass
    {
        private static readonly string[] k_DBufferNames =
            { "_DBufferTexture0", "_DBufferTexture1", "_DBufferTexture2" };

        private class PassData { }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle[] dBuffer = resourceData.dBuffer;
            if (dBuffer == null || dBuffer.Length == 0)
                return; // no decals / DBuffer this frame

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                       "Keep DBuffer For Transparent", out _))
            {
                bool anyValid = false;
                for (int i = 0; i < dBuffer.Length && i < k_DBufferNames.Length; i++)
                {
                    if (!dBuffer[i].IsValid())
                        continue;

                    // Declaring a read extends the resource lifetime up to this pass, and
                    // re-binding the global keeps _DBufferTextureN pointing at it for the
                    // transparent passes that follow.
                    builder.UseTexture(dBuffer[i], AccessFlags.Read);
                    builder.SetGlobalTextureAfterPass(dBuffer[i], Shader.PropertyToID(k_DBufferNames[i]));
                    anyValid = true;
                }

                if (!anyValid)
                    return;

                // Dummy attachment so this is a valid raster pass; we draw nothing, so the color
                // target is loaded and stored unchanged.
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Read);

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData _, RasterGraphContext _) => { });
            }
        }
    }

    private KeepDBufferPass m_Pass;

    public override void Create()
    {
        m_Pass = new KeepDBufferPass
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents,
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_Pass);
    }
}
