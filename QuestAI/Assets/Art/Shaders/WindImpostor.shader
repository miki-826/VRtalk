// Tree impostor (pre-rendered Poly Haven tree on crossed cards) swaying in the wind outside the windows.
// _QuestAIWind (AmbientLife) drives gusts; _QuestAIOutdoorTint (EnvironmentLighting) matches the panorama's
// time-of-day brightness. _Snow whitens the top-lit parts of the capture for the snowy rooms.
Shader "QuestAI/Wind Impostor"
{
 Properties
 {
  [MainTexture] _BaseMap("Impostor",2D)="white"{}
  _Tint("Tint",Color)=(1,1,1,1)
  _Cutoff("Alpha cutoff",Range(0,1))=.45
  _Base("Ground V in texture",Range(0,1))=0
  _Sway("Sway",Range(0,.3))=.1
  _Snow("Snow",Range(0,1))=0
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout"}
  Pass
  {
   Tags {"LightMode"="UniversalForward"}
   Cull Off
   AlphaToMask On
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseMap_ST;half4 _Tint;half _Cutoff,_Base,_Sway,_Snow;
   CBUFFER_END
   float _QuestAIWind;half4 _QuestAIOutdoorTint;
   struct A {float4 p:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half shade:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
   V vert(A i)
   {
    V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 w=TransformObjectToWorld(i.p.xyz);float3 origin=TransformObjectToWorld(float3(0,0,0));
    float phase=origin.x*.37+origin.z*.53;float h=saturate((i.uv.y-_Base)/(1-_Base));h*=h;
    float wind=max(_QuestAIWind,.12);
    // Whole-tree lean with the gust, plus branch-scale flutter that grows with wind strength.
    float lean=(sin(_Time.y*.9+phase)*.35+.65)*wind;
    float flutter=sin(_Time.y*4.3+phase*3+i.uv.x*6)*.12*wind;
    float scale=length(TransformObjectToWorld(float3(0,1,0))-origin);
    w.z+=(lean+flutter)*_Sway*scale*h;w.x+=sin(_Time.y*1.7+phase)*.25*wind*_Sway*scale*h;
    o.p=TransformWorldToHClip(w);o.uv=i.uv;o.shade=.85+.15*sin(phase*7);return o;
   }
   half4 frag(V i):SV_Target
   {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half4 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
    clip(c.a-_Cutoff);
    half lum=dot(c.rgb,half3(.3,.59,.11));
    half3 col=c.rgb*_Tint.rgb*i.shade;
    col=lerp(col,half3(.92,.95,1)*(.75+.25*lum),_Snow*smoothstep(.18,.42,lum));
    return half4(col*_QuestAIOutdoorTint.rgb,saturate((c.a-_Cutoff)/max(fwidth(c.a),1e-4)+.5));
   }
   ENDHLSL
  }
 }
}
