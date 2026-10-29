Shader "Keyboard/AtomicPurple"
{
    Properties
    {
        [MainColor] _BaseColor ("Core Purple", Color) = (0.26, 0.08, 0.48, 1)
        _SubsurfaceColor ("Backlight Scatter Violet", Color) = (0.54, 0.18, 0.88, 1)
        _RimColor ("Molded Edge Lavender", Color) = (0.74, 0.36, 1.0, 1)
        _CenterOpacity ("Center Opacity", Range(0.1, 0.95)) = 0.56
        _EdgeOpacity ("Edge Opacity", Range(0.2, 1.0)) = 0.88
        _RimPower ("Edge Thickness Curve", Range(1.0, 6.0)) = 2.4
        _FrostedRoughness ("Frosted Satin Spread", Range(0.05, 1.0)) = 0.38
        _Smoothness ("Surface Sheen", Range(0.0, 1.0)) = 0.76
        _SubsurfaceStrength ("Internal Glow", Range(0.0, 1.5)) = 0.45
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
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
                half4 _SubsurfaceColor;
                half4 _RimColor;
                half _CenterOpacity;
                half _EdgeOpacity;
                half _RimPower;
                half _FrostedRoughness;
                half _Smoothness;
                half _SubsurfaceStrength;
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
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half ndotv = saturate(dot(normal, view));

                // 사출 성형된 반투명 폴리카보네이트의 중앙(얇은 면)과 모서리(두꺼운 벽면) 두께 차이를 계산한다.
                half edgeThickness = pow(1.0h - ndotv, _RimPower);

                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndotl = dot(normal, light.direction);
                half wrappedDiffuse = saturate(ndotl * 0.65h + 0.35h);

                // 빛이 반투명 보라색 플라스틱 내부로 산란되는 서브서피스 효과를 표현한다.
                half backScatter = saturate(-ndotl * 0.5h + 0.5h) * (1.0h - edgeThickness * 0.5h);
                half3 plasticBody = lerp(_BaseColor.rgb, _SubsurfaceColor.rgb, backScatter * _SubsurfaceStrength);

                // 프로스티드(반무광) 아크릴/폴리카보네이트 특유의 넓은 새틴 하이라이트와 또렷한 외곽 광택을 결합한다.
                half3 halfway = normalize(light.direction + view);
                half ndoth = saturate(dot(normal, halfway));
                half frostedSpecPower = lerp(12.0h, 48.0h, 1.0h - _FrostedRoughness);
                half sharpSpecPower = lerp(48.0h, 220.0h, _Smoothness);
                half frostedSpec = pow(ndoth, frostedSpecPower) * 0.32h;
                half sharpSpec = pow(ndoth, sharpSpecPower) * 0.65h * _Smoothness;

                half3 reflection = GlossyEnvironmentReflection(reflect(-view, normal), _FrostedRoughness * 0.65h, 1.0h);

                half3 color = plasticBody * (0.42h + wrappedDiffuse * 0.58h * light.shadowAttenuation);
                color = lerp(color, _RimColor.rgb, edgeThickness * 0.62h);
                color += reflection * (0.10h + edgeThickness * 0.38h);
                color += light.color * (frostedSpec * _RimColor.rgb + sharpSpec);

                // 하이라이트가 맺히는 부분은 불투명도를 높여 플라스틱 표면의 입체감을 살린다.
                half alpha = saturate(lerp(_CenterOpacity, _EdgeOpacity, edgeThickness) + sharpSpec * 0.25h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
