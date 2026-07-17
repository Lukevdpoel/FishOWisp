Shader "Fur/Shell/Lit Baked"
{

Properties
{
    [Header(Basic)][Space]
    [MainColor] _BaseColor("Color", Color) = (0.5, 0.5, 0.5, 1)
    _AmbientColor("Ambient Color", Color) = (0.0, 0.0, 0.0, 1)
    _BaseMap("Albedo", 2D) = "white" {}
    [Gamma] _Metallic("Metallic", Range(0.0, 1.0)) = 0.5
    _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5

    [Header(Fur)][Space]
    _FurMap("Fur", 2D) = "white" {}
    [Normal] _NormalMap("Normal", 2D) = "bump" {}
    _NormalScale("Normal Scale", Range(0.0, 2.0)) = 1.0

    [Header(Underlying Surface (soft edge blend))][Space]
    _UnderMap("Underlying Albedo (the surface below)", 2D) = "black" {}
    _UnderColor("Underlying Tint", Color) = (1, 1, 1, 1)
    // NOTE: shell COUNT is baked into the mesh (ShellMeshBaker). _ShellStep still controls spacing.
    _ShellStep("Shell Step", Range(0.0, 0.05)) = 0.001
    _AlphaCutout("Alpha Cutout", Range(0.0, 1.0)) = 0.2
    _FurScale("Fur Scale", Range(0.0, 10.0)) = 1.0
    _Occlusion("Occlusion", Range(0.0, 1.0)) = 0.5
    _BaseMove("Base Move", Vector) = (0.0, -0.0, 0.0, 3.0)
    _WindFreq("Wind Freq", Vector) = (0.5, 0.7, 0.9, 1.0)
    _WindMove("Wind Move", Vector) = (0.2, 0.3, 0.2, 1.0)

    [Header(Moss Mask (Polybrush Vertex Color))][Space]
    _MossMaskChannel("Vertex Color Channel (RGBA weights)", Vector) = (1, 0, 0, 0)
    [ToggleUI] _MossMaskInvert("Invert Mask", Float) = 0
    _MossMaskSoftness("Mask Softness", Range(0.0, 1.0)) = 0.5
    _BaseSpread("Base Moss Spread (flat past shells)", Range(0.0, 0.5)) = 0.1
    _BaseLift("Base Lift (z-fight fix)", Range(0.0, 0.02)) = 0.002
    _BaseFeather("Base Edge Fade Width", Range(0.0, 0.5)) = 0.15
    _BaseNoiseMap("Base Edge Noise Map (grayscale)", 2D) = "gray" {}
    _BaseEdgeNoise("Base Edge Noise Strength", Range(0.0, 1.0)) = 0.6
    _BaseEdgeNoiseScale("Base Edge Noise Scale", Float) = 2.0
    _BaseEdgeNoiseWidth("Base Edge Noise Width", Range(0.02, 1.0)) = 0.4

    [Header(Lighting)][Space]
    _RimLightPower("Rim Light Power", Range(1.0, 20.0)) = 6.0
    _RimLightIntensity("Rim Light Intensity", Range(0.0, 1.0)) = 0.5
    _ShadowExtraBias("Shadow Extra Bias", Range(-1.0, 1.0)) = 0.0
}

SubShader
{
    Tags
    {
        // OPAQUE (alpha-test range): the soft edge is a colour blend into _UnderMap, not transparency,
        // so decals (drawn before the transparent queue) land on the moss like on any opaque surface.
        "RenderType" = "Opaque"
        "Queue" = "AlphaTest"
        "RenderPipeline" = "UniversalPipeline"
        "UniversalMaterialType" = "Lit"
        "IgnoreProjector" = "True"
    }

    LOD 100

    ZWrite On
    Cull Back

    Pass
    {
        Name "ForwardLit"
        Tags { "LightMode" = "UniversalForward" }

        HLSLPROGRAM
#if (UNITY_VERSION >= 202111)
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
        #pragma multi_compile_fragment _ _LIGHT_LAYERS
#else
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
#endif
        #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
        #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
        #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
        #pragma multi_compile _ _SHADOWS_SOFT
        #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
        #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
        #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3

        #pragma multi_compile _ DIRLIGHTMAP_COMBINED
        #pragma multi_compile _ LIGHTMAP_ON
        #pragma multi_compile_fog

        #pragma prefer_hlslcc gles
        #pragma exclude_renderers d3d11_9x
        #pragma target 3.5
        #pragma vertex vert
        #pragma fragment frag
        #include "./LitBaked.hlsl"
        ENDHLSL
    }

    Pass
    {
        Name "DepthOnly"
        Tags { "LightMode" = "DepthOnly" }

        ZWrite On
        ColorMask 0

        HLSLPROGRAM
        #pragma target 3.5
        #pragma vertex vert
        #pragma fragment frag
        #include "./DepthBaked.hlsl"
        ENDHLSL
    }

    Pass
    {
        Name "DepthNormals"
        Tags { "LightMode" = "DepthNormals" }

        ZWrite On

        HLSLPROGRAM
        #pragma target 3.5
        #pragma vertex vert
        #pragma fragment frag
        #include "./DepthNormalsBaked.hlsl"
        ENDHLSL
    }

    // NOTE: no ShadowCaster pass by design — see the geometry-shader Lit.shader for the same
    // perf decision (short moss/fur casting shadows isn't worth the cost). Moss still RECEIVES
    // shadows via the ForwardLit pass.
}

}
