using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenuForRenderPipeline("GapperGames/Kuwahara DOF", typeof(UniversalRenderPipeline))]
    public sealed class KuwaharaVolumeComponent : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter enabled = new BoolParameter(false);
        public ClampedIntParameter kernelSize = new ClampedIntParameter(4, 2, 8);
        public ClampedFloatParameter focalDistance = new ClampedFloatParameter(10f, 0f, 200f);
        public ClampedFloatParameter nearRange = new ClampedFloatParameter(3f, 0f, 50f);
        public ClampedFloatParameter farRange = new ClampedFloatParameter(10f, 0f, 100f);
        public ClampedFloatParameter maxBlend = new ClampedFloatParameter(1f, 0f, 1f);

        public bool IsActive() => enabled.value;

        public bool IsTileCompatible() => false;
    }
}
