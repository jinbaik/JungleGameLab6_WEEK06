Shader "Keyboard/Aurora"
{
    Properties
    {
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _NightColor ("Polar Night Sky", Color) = (0.012, 0.024, 0.055, 1)
        _HorizonColor ("Horizon Glow", Color) = (0.02, 0.09, 0.16, 1)
        _AuroraGreen ("Aurora Emerald", Color) = (0.10, 0.92, 0.58, 1)
        _AuroraCyan ("Aurora Cyan", Color) = (0.12, 0.78, 0.96, 1)
        _AuroraViolet ("Aurora Magenta", Color) = (0.62, 0.24, 0.92, 1)
        _StarColor ("Star Light", Color) = (0.88, 0.96, 1.0, 1)
        _CurtainScale ("Wave Scale", Range(0.05, 2.0)) = 0.28
        _RibbonSharpness ("Band Softness", Range(0.5, 4.0)) = 1.35
        _StreakDensity ("Curtain Shimmer", Range(0.0, 2.0)) = 0.25
        _FlowSpeed ("Flow Speed", Range(0.0, 1.0)) = 0.10
        _StarDensity ("Star Density", Range(0.0, 15.0)) = 0.0
        _Glow ("Aurora Glow", Range(0.0, 2.0)) = 0.65
        _RimStrength ("Iridescent Rim", Range(0.0, 2.0)) = 0.32
        _ParallaxDepth ("Inner Depth", Range(0.0, 1.5)) = 0.28
        _DepthContrast ("Depth Contrast", Range(0.1, 2.5)) = 1.2
        _Smoothness ("Clear Coat Smoothness", Range(0.0, 1.0)) = 0.85
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
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _NightColor;
                half4 _HorizonColor;
                half4 _AuroraGreen;
                half4 _AuroraCyan;
                half4 _AuroraViolet;
                half4 _StarColor;
                float _CurtainScale;
                float _RibbonSharpness;
                float _StreakDensity;
                float _FlowSpeed;
                float _StarDensity;
                float _Glow;
                float _RimStrength;
                float _ParallaxDepth;
                float _DepthContrast;
                float _Smoothness;
            CBUFFER_END

            struct Input
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Output
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalOS : TEXCOORD3;
            };

            float Hash2(float2 p)
            {
                float3 q = frac(float3(p.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float Noise2(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(Hash2(i), Hash2(i + float2(1.0, 0.0)), f.x);
                float b = lerp(Hash2(i + float2(0.0, 1.0)), Hash2(i + float2(1.0, 1.0)), f.x);
                return lerp(a, b, f.y);
            }

            // 넓고 완만하게 흐르는 실크 형태의 오로라 그라데이션 파동을 계산한다.
            half3 EvaluateAuroraWave(float3 samplePos, float layerIndex, float time, out float waveMask)
            {
                float2 uv = samplePos.xz * _CurtainScale + float2(samplePos.y * 0.25, -samplePos.y * 0.2);
                float2 drift = float2(time * 0.35, -time * 0.22);

                // 자잘한 노이즈 대신 아주 완만한 저주파 곡선 한 줄기로 부드러운 흐름만 만든다.
                float gentleBend = (Noise2(uv * 0.6 + drift + layerIndex * 2.1) - 0.5) * 1.1;
                float phase = uv.x * 1.15 + uv.y * 0.75 + gentleBend - time * 0.6 + layerIndex * 1.2;

                // 날카로운 줄무늬가 생기지 않도록 넓고 부드러운 벨 커브 형태로 감쇠한다.
                float wave = sin(phase);
                float band = exp(-wave * wave * _RibbonSharpness);

                // 색상이 에메랄드 -> 시안 -> 바이올렛으로 넓고 매끄럽게 이어지도록 보간한다.
                float colorShift = saturate(0.5 + 0.5 * sin(phase * 0.65 - time * 0.3 + gentleBend));
                half3 lowerToMid = lerp(_AuroraGreen.rgb, _AuroraCyan.rgb, smoothstep(0.0, 0.55, colorShift));
                half3 auroraColor = lerp(lowerToMid, _AuroraViolet.rgb, smoothstep(0.45, 1.0, colorShift));

                // 은은한 밝기 숨쉬기(Shimmer)만 살짝 더해 단조로움을 피한다.
                float shimmer = 1.0 - _StreakDensity * 0.25 + _StreakDensity * 0.25 * sin(uv.x * 2.0 - uv.y * 1.5 + time * 1.1);
                waveMask = saturate(band * shimmer);
                return auroraColor * waveMask;
            }

            Output Vert(Input input)
            {
                Output output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                return output;
            }

            half4 Frag(Output input) : SV_Target
            {
                float time = _Time.y * _FlowSpeed;
                half3 normal = normalize(input.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 viewOS = normalize(mul((float3x3)GetWorldToObjectMatrix(), view));
                float3 normalOS = normalize(input.normalOS);

                float incidence = max(abs(dot(normalOS, viewOS)), 0.45);
                float3 baseSampleOS = input.positionOS * 0.35 + input.positionWS * 0.18;

                float horizonGrad = saturate(normalOS.y * 0.5 + 0.5);
                half3 sky = lerp(_HorizonColor.rgb, _NightColor.rgb, smoothstep(0.1, 0.9, horizonGrad));

                // 레이어 수를 2개로 줄여 겹침으로 인한 복잡한 무늬를 없애고 깊이감만 남긴다.
                [unroll]
                for (int layer = 0; layer < 2; layer++)
                {
                    float layerIdx = (float)layer;
                    float depth = (layerIdx + 1.0) * 0.5 * _ParallaxDepth;
                    float3 samplePosition = baseSampleOS - viewOS * (depth / incidence);

                    float waveMask;
                    half3 auroraBand = EvaluateAuroraWave(samplePosition, layerIdx, time, waveMask);

                    float layerWeight = layer == 0 ? 0.65 : 0.35;
                    sky += auroraBand * layerWeight * (0.55 + _Glow);
                }

                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float diffuse = saturate(dot(normal, light.direction));
                float ndotv = saturate(dot(normal, view));
                float fresnel = pow(1.0 - ndotv, 3.0);

                half3 iridescentRim = lerp(_AuroraGreen.rgb, _AuroraViolet.rgb, smoothstep(0.15, 0.85, 1.0 - ndotv));
                iridescentRim = lerp(iridescentRim, _AuroraCyan.rgb, 0.4);

                half3 halfway = normalize(light.direction + view);
                float specularPower = lerp(24.0, 160.0, _Smoothness);
                float specular = pow(saturate(dot(normal, halfway)), specularPower) * lerp(0.12, 0.4, _Smoothness);
                half3 envReflection = GlossyEnvironmentReflection(reflect(-view, normal), 1.0 - _Smoothness, 1.0);

                half3 color = sky * (0.85 + diffuse * 0.18 * light.shadowAttenuation);
                color += iridescentRim * fresnel * _RimStrength;
                color += envReflection * (0.05 + fresnel * 0.16) * _Smoothness;
                color += light.color * specular;

                return half4(color * _BaseColor.rgb, 1.0);
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
