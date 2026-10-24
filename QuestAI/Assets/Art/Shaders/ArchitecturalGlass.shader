Shader "QuestAI/Architectural Glass"
{
 Properties { _Tint("Tint",Color)=(.78,.9,.86,.035) _Roughness("Roughness",Range(0,1))=.07 _Reflection("Reflection",Range(0,1))=.55 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Pass
  {
   Tags {"LightMode"="UniversalForward"}
   Blend One OneMinusSrcAlpha
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _Tint;half _Roughness,_Reflection;
   CBUFFER_END
   struct A {float4 p:POSITION;float3 n:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;half3 normal:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
   V vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.world=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.n);return o;}
   half4 frag(V i):SV_Target
   {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half3 view=GetWorldSpaceNormalizeViewDir(i.world),n=normalize(i.normal);
    half fresnel=.04+.96*pow(1-abs(dot(n,view)),5);
    half3 reflected=GlossyEnvironmentReflection(reflect(-view,n),i.world,_Roughness,1,GetNormalizedScreenSpaceUV(i.p));
    half strength=fresnel*_Reflection;
    half alpha=saturate(_Tint.a+strength);
    return half4(_Tint.rgb*_Tint.a+reflected*strength,alpha);
   }
   ENDHLSL
  }
 }
}
