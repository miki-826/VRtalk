// Burning logs and coal bed: lit bark (URP Lit inputs, SRP Batcher compatible) with glowing cracks that
// pulse like live embers. _EmissionColor sets the glow colour/intensity; heat rises towards the flames.
Shader "QuestAI/Hearth Embers"
{
    Properties
    {
        [MainTexture] _BaseMap("Bark", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.3
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        _SpecGlossMap("Specular", 2D) = "white" {}
        _BumpScale("Normal Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}
        _Parallax("Scale", Range(0.005, 0.08)) = 0.005
        _ParallaxMap("Height Map", 2D) = "black" {}
        _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion (G)", 2D) = "white" {}
        [HDR] _EmissionColor("Ember glow", Color) = (4,1.1,.25)
        _EmissionMap("Emission", 2D) = "white" {}
        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailAlbedoMap("Detail Albedo x2", 2D) = "linearGrey" {}
        _DetailNormalMapScale("Scale", Range(0.0, 2.0)) = 1.0
        [Normal] _DetailNormalMap("Normal Map", 2D) = "bump" {}
        _ClearCoatMask("Clear Coat", Range(0.0, 1.0)) = 0.0
        _ClearCoatSmoothness("Clear Coat Smoothness", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _Surface("__surface", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitPassVertex
            #pragma fragment EmberFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #define _SCREENSPACEREFLECTIONS_OFF 1
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            float EmberHash(float3 p){return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
            float EmberNoise(float3 p)
            {
                float3 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(lerp(EmberHash(i),EmberHash(i+float3(1,0,0)),f.x),lerp(EmberHash(i+float3(0,1,0)),EmberHash(i+float3(1,1,0)),f.x),f.y),
                            lerp(lerp(EmberHash(i+float3(0,0,1)),EmberHash(i+float3(1,0,1)),f.x),lerp(EmberHash(i+float3(0,1,1)),EmberHash(i+1),f.x),f.y),f.z);
            }

            void EmberFragment(Varyings input, out half4 outColor : SV_Target0)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv, surfaceData);
                surfaceData.emission = 0;
                InputData inputData;
                InitializeInputData(input, surfaceData.normalTS, inputData);
                InitializeBakedGIData(input, inputData);

                float3 p = inputData.positionWS * 22;
                float t = _Time.y;
                // Cracks: ridges of noise; char: dark crust between them.
                float n = EmberNoise(p) * .6 + EmberNoise(p * 2.3 + 5) * .4;
                float crack = 1 - abs(n * 2 - 1);
                crack = smoothstep(.84, .98, crack);
                // Heat is highest at the bottom of the stack (hearth bed top ~0.3 m).
                float heat = saturate((0.5 - inputData.positionWS.y) * 3.0);
                float pulse = .55 + .45 * EmberNoise(float3(p.xz * .15, t * 1.3));
                surfaceData.albedo *= lerp(1, .12, saturate(heat * 1.8));   // charred where hot
                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                half glow = (crack * heat * pulse + heat * heat * .06 * pulse) * .6;
                color.rgb += _EmissionColor.rgb * glow;
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                outColor = half4(color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex UniversalVertexMeta
            #pragma fragment UniversalFragmentMetaLit
            #pragma shader_feature EDITOR_VISUALIZATION
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitMetaPass.hlsl"
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
