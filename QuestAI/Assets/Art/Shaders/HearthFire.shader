// Fireplace flames on crossed ribbons. Keeps the note-article idea (height/centre gradient, transparent tip,
// layered noise distortion) and adds console-style detail: 4-octave turbulence, domain-warped tongues that
// split and detach, a blackbody colour ramp, a white-hot core and HDR output so bloom produces the glow.
Shader "QuestAI/Hearth Fire"
{
 Properties
 {
  [HDR] _RootColor("Root / center color",Color)=(1,.78,.06,1)
  [HDR] _OuterColor("Outer / tip color",Color)=(1,.09,.008,1)
  _GradientPower("Gradient power",Range(.1,5))=1.5
  _Distortion("Distortion strength",Range(0,.3))=.12
  _TipFade("Tip fade",Range(.2,3))=.85
  _ScrollA("Noise flow A",Vector)=(.13,-.85,0,0)
  _ScrollB("Noise flow B",Vector)=(-.19,-1.35,0,0)
  _Intensity("Intensity",Range(0,6))=2.4
  _Speed("Speed",Range(0,4))=1.1
  _Turbulence("Turbulence",Range(0,1))=.6
  _CoreHeat("White-hot core",Range(0,3))=1.4
 }
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
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _RootColor,_OuterColor;
   float4 _ScrollA,_ScrollB;
   float _Intensity,_Speed,_GradientPower,_Distortion,_TipFade,_Turbulence,_CoreHeat;
   CBUFFER_END
   struct A {float4 p:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float seed:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
   // Rising turbulence: each octave scrolls upward faster, like hot gas accelerating.
   float fbm(float2 p,float t)
   {
    float sum=0,amp=.5;
    [unroll] for(int o=0;o<4;o++){sum+=amp*noise(p+float2(0,-t*(1+o*.6)));p=p*2.03+float2(1.7,9.2);amp*=.5;}
    return sum;
   }
   // Approximate blackbody: deep red -> orange -> yellow -> white as temperature rises.
   half3 Blackbody(half heat)
   {
    half3 c=half3(saturate(heat*2.2),saturate(heat*2.2-.55),saturate(heat*2.2-1.35));
    return c*c;
   }
   V vert(A i)
   {
    V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 w=TransformObjectToWorld(i.p.xyz);o.seed=frac(w.x*7.31+w.z*3.17)*19;
    // Ribbons sway: tops lean with a slow draft, bases stay anchored to the logs.
    float sway=sin(_Time.y*_Speed*1.7+o.seed)*.5+sin(_Time.y*_Speed*3.1+o.seed*2.3)*.25;
    w.x+=sway*.055*i.uv.y*i.uv.y;w.z+=cos(_Time.y*_Speed*2.3+o.seed*1.7)*.02*i.uv.y*i.uv.y;
    o.p=TransformWorldToHClip(w);o.uv=i.uv;return o;
   }
   half4 frag(V i):SV_Target
   {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float t=_Time.y*_Speed,y=saturate(i.uv.y);
    float a=noise(i.uv*float2(3,5)+_ScrollA.xy*t+i.seed);
    float b=noise(i.uv*float2(6,8)+_ScrollB.xy*t-i.seed);
    float turb=fbm(i.uv*float2(4,3)+i.seed,t*1.6);
    float flow=lerp((a+b)*.5,turb,_Turbulence);
    // Domain warp grows with height so the base is steady and the tips lick and tear.
    float2 warped=i.uv+float2((a-b)*1.2+(turb-.5)*.9,flow-.5)*_Distortion*(.2+1.3*y);
    float x=(warped.x-.5)*2;
    float height=saturate(warped.y);
    float width=pow(saturate(1-height),.75)*.8;
    // Tongues: vertical streaks of noise carve the outline into separate licks of flame.
    float tongues=fbm(float2(x*2.2+i.seed,height*1.2),t*2.2);
    float silhouette=width-abs(x)+(tongues-.5)*.55*y+(flow-.5)*.25*y;
    float mask=smoothstep(-.03,.08,silhouette)*smoothstep(0,.06,y);
    // Detached wisps near the tip.
    float wisp=smoothstep(.62,.8,fbm(float2(x*3,height*4)+i.seed,t*3))*smoothstep(.35,.7,y)*(1-y);
    mask=saturate(mask+wisp*.6);
    float alpha=mask*pow(saturate(1-y),_TipFade)*_RootColor.a;
    // Temperature: hottest low and in the middle, cooling outward and upward.
    float inner=saturate(silhouette*3);
    float heat=saturate((1-pow(saturate(max(y,abs(x))),_GradientPower))*.75+inner*.45+(turb-.5)*.3);
    half3 color=lerp(_OuterColor.rgb,_RootColor.rgb,saturate(heat*1.3));
    color=lerp(color,Blackbody(heat),.55);
    color+=_CoreHeat*half3(1,.9,.7)*pow(saturate(inner*(1-y*1.4)),3);
    float flicker=.85+.15*noise(float2(t*6,i.seed));
    // Premultiplied: overlapping ribbons keep the yellow-to-red layering; HDR values feed the bloom.
    return half4(color*(.7+.6*flow)*_Intensity*flicker*alpha,alpha*.92);
   }
   ENDHLSL
  }
 }
}
