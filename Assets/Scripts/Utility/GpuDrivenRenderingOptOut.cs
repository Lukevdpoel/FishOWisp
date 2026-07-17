using System.Reflection;
using UnityEngine;

// Opts a renderer out of the GPU Resident Drawer. Renderers whose meshes are rewritten on the
// CPU every frame (SetVertices) get re-dispatched to the GRD batcher on every change —
// per-frame InstanceCullingBatcher.BuildBatch cost for zero batching benefit, since their
// instantiated meshes are unique anyway. Those renderers should call Apply() once at init.
//
// Renderer.allowGPUDrivenRendering exists but is still internal in Unity 6000.2, so it is set
// via reflection (safe: standalone builds are Mono, nothing strips it). If a future Unity
// drops the property, the fallback assigns a dummy MaterialPropertyBlock — GRD documentedly
// skips renderers that use property blocks (at the cost of SRP-batcher compatibility, which is
// why the property is preferred).
public static class GpuDrivenRenderingOptOut
{
    static readonly PropertyInfo allowProperty = typeof(Renderer).GetProperty(
        "allowGPUDrivenRendering", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    static MaterialPropertyBlock dummyBlock;

    public static void Apply(Renderer renderer)
    {
        if (renderer == null) return;

        if (allowProperty != null)
        {
            allowProperty.SetValue(renderer, false);
            return;
        }

        if (dummyBlock == null)
        {
            dummyBlock = new MaterialPropertyBlock();
            // A property no shader samples; only there so the block isn't empty.
            dummyBlock.SetFloat(Shader.PropertyToID("_GpuDrivenOptOut"), 1f);
        }
        renderer.SetPropertyBlock(dummyBlock);
    }
}
