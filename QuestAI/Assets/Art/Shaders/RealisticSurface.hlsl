#ifndef QUESTAI_REALISTIC_SURFACE_INCLUDED
#define QUESTAI_REALISTIC_SURFACE_INCLUDED

// Shading extras layered on top of URP's mobile PBR, tuned for baked Quest rooms.
// Everything here is driven by keywords or globals so materials keep LitInput's UnityPerMaterial
// layout and stay SRP Batcher compatible.

// Scene key light (window / lamp direction) set by EnvironmentLighting per time of day.
// Static surfaces are fully lightmapped, so without this they would have no specular highlights at all.
half4 _QuestAIKeyLightDir;    // xyz: direction towards the light, w: unused
half4 _QuestAIKeyLightColor;  // rgb: colour * intensity

#define FABRIC_SHEEN 0.55h
#define SKIN_SCATTER half3(0.55h, 0.22h, 0.12h)
#define FOLIAGE_TRANSMISSION half3(1.6h, 1.9h, 0.8h)

// Charlie sheen distribution (Estevez & Kulla) with Neubelt's visibility, cheap enough for Quest.
half SheenTerm(half NoH, half NoL, half NoV, half roughness)
{
    half r = max(roughness, 0.07h);
    half invR = 1.0h / r;
    half sin2h = max(1.0h - NoH * NoH, 0.0078125h);
    half D = (2.0h + invR) * pow(sin2h, invR * 0.5h) / (2.0h * PI);
    half V = 1.0h / (4.0h * (NoL + NoV - NoL * NoV) + 1e-4h);
    return D * V;
}

// Removes reflections where light cannot reach: cavities (AO), dark lightmap areas, and normal-map
// directions that dip below the geometric surface (horizon occlusion).
half RealisticSpecularOcclusion(half occlusion, half3 bakedGI, half3 reflectWS, half3 vertexNormalWS, half perceptualRoughness)
{
    half lighting = saturate(Luminance(bakedGI) * 2.5h + 0.15h);
    half horizon = saturate(1.0h + dot(reflectWS, vertexNormalWS));
    half ao = saturate(pow(occlusion, lerp(4.0h, 1.0h, perceptualRoughness)));
    return ao * lighting * horizon * horizon;
}

half3 RealisticExtras(InputData inputData, SurfaceData surfaceData, half3 vertexNormalWS, half3 baseColor)
{
    BRDFData brdf;
    InitializeBRDFData(surfaceData, brdf);

    half3 N = inputData.normalWS;
    half3 V = inputData.viewDirectionWS;
    half NoV = saturate(dot(N, V));
    half3 R = reflect(-V, N);
    half3 extra = 0;

    // 1. Specular occlusion: take back the part of URP's probe reflection that would glow inside shadows.
#if !defined(_ENVIRONMENTREFLECTIONS_OFF)
    half fresnel = Pow4(1.0h - NoV);
    half3 env = GlossyEnvironmentReflection(R, inputData.positionWS, brdf.perceptualRoughness, 1.0h, inputData.normalizedScreenSpaceUV);
    half specOcc = RealisticSpecularOcclusion(surfaceData.occlusion, inputData.bakedGI, R, vertexNormalWS, brdf.perceptualRoughness);
    extra -= env * EnvironmentBRDFSpecular(brdf, fresnel) * surfaceData.occlusion * (1.0h - specOcc);
#endif

    // 2. Baked-light specular: a GGX highlight from the scene key light, masked by how lit the lightmap is.
#if defined(LIGHTMAP_ON)
    half3 L = normalize(_QuestAIKeyLightDir.xyz + half3(0, 1e-4h, 0));
    half NoL = saturate(dot(N, L));
    half lit = saturate(Luminance(inputData.bakedGI) * 1.5h);
    extra += brdf.specular * DirectBRDFSpecular(brdf, N, L, V) * NoL * _QuestAIKeyLightColor.rgb * lit * surfaceData.occlusion;
#endif

    half3 ambient = inputData.bakedGI;

#if defined(_QUESTAI_FABRIC)
    // Cloth: retro-reflective fuzz at grazing angles plus a soft sheen lobe from the key light.
    half3 sheenColor = sqrt(max(baseColor, 0.02h)) * FABRIC_SHEEN;
    half rim = Pow4(1.0h - NoV);
    extra += sheenColor * ambient * rim * surfaceData.occlusion * 1.4h;
    half3 Lf = normalize(_QuestAIKeyLightDir.xyz + half3(0, 1e-4h, 0));
    half3 H = SafeNormalize(Lf + V);
    half NoLf = saturate(dot(N, Lf));
    extra += sheenColor * SheenTerm(saturate(dot(N, H)), NoLf, NoV, 0.45h) * NoLf * _QuestAIKeyLightColor.rgb * saturate(Luminance(ambient) * 1.5h) * 0.6h;
#endif

#if defined(_QUESTAI_SKIN)
    // Soft vinyl/skin: light wrapping round the terminator and reddish scattering in the shadowed side.
    Light mainLight = GetMainLight();
    half NoLm = dot(N, mainLight.direction);
    half wrap = saturate((NoLm + 0.5h) / 2.25h);
    half scatter = wrap - saturate(NoLm) * 0.45h;
    extra += baseColor * SKIN_SCATTER * mainLight.color * max(scatter, 0) * 0.55h;
    extra += baseColor * SKIN_SCATTER * SampleSH(-N) * 0.25h;
    // Velvety rim so the silhouette separates from the room like a studio-lit figure.
    extra += Pow4(1.0h - NoV) * ambient * 0.35h;
#endif

#if defined(_QUESTAI_FOLIAGE)
    // Thin leaves let the bright room / window light through from behind.
    half3 behind = GlossyEnvironmentReflection(-N, inputData.positionWS, 1.0h, 1.0h, inputData.normalizedScreenSpaceUV);
    extra += baseColor * FOLIAGE_TRANSMISSION * behind * (0.25h + 0.35h * NoV) * surfaceData.occlusion;
#endif

    return extra;
}

#endif
