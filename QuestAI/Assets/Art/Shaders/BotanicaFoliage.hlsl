#ifndef QUESTAI_BOTANICA_FOLIAGE_INCLUDED
#define QUESTAI_BOTANICA_FOLIAGE_INCLUDED

// Indoor air movement: a few millimetres of flutter, larger towards the top of each plant.
// Kept as constants so the material stays SRP Batcher compatible with LitInput's UnityPerMaterial.
#define FOLIAGE_WIND_AMPLITUDE 0.0065
#define FOLIAGE_TRANSLUCENCY 0.55

float3 FoliageWindWS(float3 positionWS)
{
    float t = _Time.y;
    float phase = dot(positionWS, float3(1.7, 0.9, 1.3));
    float height = saturate((positionWS.y - 0.4) * 0.8);
    float3 offset;
    offset.x = sin(t * 1.3 + phase) * 0.6 + sin(t * 2.9 + phase * 2.1) * 0.25;
    offset.y = sin(t * 2.3 + phase * 1.7) * 0.3;
    offset.z = cos(t * 1.1 + phase * 1.3) * 0.6;
    return positionWS + offset * FOLIAGE_WIND_AMPLITUDE * (0.3 + height);
}

float3 FoliageWindOS(float3 positionOS)
{
    return TransformWorldToObject(FoliageWindWS(TransformObjectToWorld(positionOS)));
}

// Cheap per-leaf tint variation so a mass of identical leaves does not read as one flat colour.
half3 FoliageTint(half3 albedo, float3 positionWS)
{
    float n = frac(sin(dot(floor(positionWS * 6.0), float3(12.9898, 78.233, 37.719))) * 43758.5453);
    half3 yellowGreen = half3(albedo.r * 1.25, albedo.g * 1.15, albedo.b * 0.7);
    return lerp(albedo * 0.82, yellowGreen, n);
}

#endif
