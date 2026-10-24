Shader "QuestAI/Natural Leaves"
{
 Properties { _BaseColor("Leaf color",Color)=(.16,.32,.08,1) _Wind("Wind",Range(0,.2))=.035 _Needles("Needle clusters",Range(0,1))=0 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout"}
  Pass
  {
   Tags {"LightMode"="UniversalForward"}
   Cull Off
   ZWrite On
   AlphaToMask On
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;float _Wind,_Needles;
   CBUFFER_END
   struct A {float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;half3 normal:TEXCOORD1;float2 uv:TEXCOORD2;half3 tint:TEXCOORD3;UNITY_VERTEX_OUTPUT_STEREO};
   V vert(A i)
   {
    V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 world=TransformObjectToWorld(i.p.xyz);float phase=world.x*1.9+world.z*2.1;
    world.xz+=float2(sin(_Time.y*1.3+phase),cos(_Time.y*.9+phase))*_Wind*i.uv.y*i.uv.y;
    o.world=world;o.p=TransformWorldToHClip(world);o.normal=TransformObjectToWorldNormal(i.n);o.uv=i.uv;o.tint=i.color.rgb;return o;
   }
   half4 frag(V i):SV_Target
   {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float y=saturate(i.uv.y),x=abs(i.uv.x-.5)*2;
    float broad=pow(saturate(sin(y*3.14159)),.7);
    float envelope=pow(saturate(sin(y*3.14159)),.45);
    // Coarser branch clusters retain coverage at headset viewing distances.
    float needles=cos((y*12-x*4)*6.283);
    float needleEdge=max(.065-x,min(envelope-x,(needles+.25)*.35));
    float edge=lerp(broad-x,needleEdge,_Needles);clip(edge);
    float mid=1-smoothstep(.012,.04,x);
    float vein=pow(saturate(cos((y*13+x*3)*6.283)),18)*(1-x);
    half3 albedo=_BaseColor.rgb*i.tint*(.76+.28*y)+half3(.12,.17,.025)*(mid*.4+vein*.16);
    Light light=GetMainLight();half3 n=normalize(i.normal),view=GetWorldSpaceNormalizeViewDir(i.world);
    half diffuse=.3+.7*abs(dot(n,light.direction));
    half back=pow(saturate(dot(-light.direction,view)),3)*.3;
    half3 ambient=max(SampleSH(n),half3(.16,.18,.12));
    float coverage=saturate(edge/max(fwidth(x),.001)+.5);
    return half4(albedo*(ambient+light.color*diffuse)+albedo*back,coverage);
   }
   ENDHLSL
  }
 }
}
