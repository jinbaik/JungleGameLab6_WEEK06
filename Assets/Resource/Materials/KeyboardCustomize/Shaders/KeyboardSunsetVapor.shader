Shader "Keyboard/SunsetVapor"
{
    Properties
    {
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _SunColor ("Horizon Peach Gold", Color) = (1.0, 0.68, 0.28, 1)
        _CoralColor ("Sunset Coral Pink", Color) = (0.98, 0.34, 0.50, 1)
        _MagentaColor ("Dusk Orchid Magenta", Color) = (0.68, 0.18, 0.68, 1)
        _TwilightColor ("Night Indigo Violet", Color) = (0.20, 0.09, 0.44, 1)
        _RimColor ("Golden Hour Rim", Color) = (1.0, 0.58, 0.38, 1)
        _GradientScale ("Gradient Sweep Scale", Range(0.05, 1.5)) = 0.42
        _GradientAngle ("Gradient Angle", Range(-3.14, 3.14)) = 0.52
        _VerticalBlend ("Vertical Sculpt Influence", Range(0.0, 1.0)) = 0.35
        _DriftSpeed ("Color Flow Speed", Range(0.0, 2.0)) = 0.45
        _RimStrength ("Sunset Rim Glow", Range(0.0, 1.5)) = 0.45
        _RimPower ("Rim Falloff", Range(1.0, 6.0)) = 2.6
        _Smoothness ("Satin Smoothness", Range(0.0, 1.0)) = 0.65
        _ClearCoat ("Clear Coat Sheen", Range(0.0, 1.0)) = 0.38
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _SunColor;
                half4 _CoralColor;
                half4 _MagentaColor;
                half4 _TwilightColor;
                half4 _RimColor;
                float _GradientScale;
                float _GradientAngle;
                float _VerticalBlend;
                float _DriftSpeed;
                half _RimStrength;
                half _RimPower;
                half _Smoothness;
                half _ClearCoat;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half3 normalOS : TEXCOORD3;
            };

            // 4단계 황혼 컬러가 끊김 없이 순환(피치 골드 -> 코랄 핑크 -> 오키드 마젠타 -> 딥 바이올렛 -> 피치 골드)하며 흐르도록 보간한다.
            half3 EvaluateSunsetCycle(float phase)
            {
                float cycle = frac(phase) * 4.0;
                float t0 = smoothstep(0.0, 1.0, saturate(cycle));
                float t1 = smoothstep(0.0, 1.0, saturate(cycle - 1.0));
                float t2 = smoothstep(0.0, 1.0, saturate(cycle - 2.0));
                float t3 = smoothstep(0.0, 1.0, saturate(cycle - 3.0));

                half3 color = lerp(_SunColor.rgb, _CoralColor.rgb, t0);
                color = lerp(color, _MagentaColor.rgb, t1);
                color = lerp(color, _TwilightColor.rgb, t2);
                color = lerp(color, _SunColor.rgb, t3);
                return color;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                half3 normalOS = normalize(input.normalOS);
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half ndotv = saturate(dot(normal, view));

                // 시간에 따라 노을 그라데이션이 키보드 전체를 부드럽게 타고 흐르며 색상이 계속 전환되게 한다.
                float2 dir = float2(cos(_GradientAngle), sin(_GradientAngle));
                float3 combinedCoord = input.positionWS * 0.65 + input.positionOS * 0.08;
                float sweep = dot(combinedCoord.xz, dir) * _GradientScale;
                float sculptOffset = (normalOS.z * 0.12 - normalOS.y * 0.08) * _VerticalBlend;
                float phase = sweep * 0.35 + sculptOffset - _Time.y * (_DriftSpeed * 0.18);

                half3 sunsetAlbedo = EvaluateSunsetCycle(phase);
                half3 rimSunset = EvaluateSunsetCycle(phase + 0.18);

                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndotl = dot(normal, light.direction);
                half softDiffuse = saturate(ndotl * 0.55h + 0.45h);
                half shadow = lerp(0.65h, 1.0h, light.shadowAttenuation);

                // 새틴 PBT 질감의 부드러운 하이라이트와 얇은 클리어코트 윤기를 함께 계산한다.
                half3 halfway = normalize(light.direction + view);
                half ndoth = saturate(dot(normal, halfway));
                half satinPower = lerp(16.0h, 96.0h, _Smoothness);
                half coatPower = lerp(96.0h, 256.0h, _Smoothness);
                half satinSpec = pow(ndoth, satinPower) * lerp(0.10h, 0.35h, _Smoothness);
                half coatSpec = pow(ndoth, coatPower) * _ClearCoat * 0.45h;

                half fresnel = pow(1.0h - ndotv, _RimPower);
                half3 envReflection = GlossyEnvironmentReflection(reflect(-view, normal), 1.0h - _Smoothness * 0.8h, 1.0h);

                // 모서리 림 라이트도 시간 흐름에 맞춰 노을빛이 자연스럽게 변하도록 연결한다.
                half3 rimTint = lerp(_RimColor.rgb, rimSunset, 0.65h);
                half3 color = sunsetAlbedo * (0.42h + softDiffuse * 0.65h * shadow);
                color += rimTint * fresnel * _RimStrength;
                color += envReflection * (0.04h + fresnel * 0.18h) * _ClearCoat;
                color += light.color * (satinSpec * lerp(sunsetAlbedo, _SunColor.rgb, 0.5h) + coatSpec);

                return half4(color * _BaseColor.rgb, 1.0h);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Input { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 ShadowVert(Input input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction = normalize(_LightPosition - positionWS);
                #else
                    float3 direction = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, direction));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE * positionCS.w);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE * positionCS.w);
                #endif
                return positionCS;
            }
            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
