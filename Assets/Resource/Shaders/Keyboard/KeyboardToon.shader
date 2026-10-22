Shader "Keyboard/Toon"
{
    Properties
    {
        [MainColor] _BaseColor ("Surface Color", Color) = (0.25,0.76,0.81,1)
        _OutlineColor ("Outline Color", Color) = (0.015,0.02,0.035,1)
        _BorderWidth ("Border Width (Local Units)", Range(0,0.15)) = 0.045
        [PerRendererData] _ToonBoundsCenter ("Bounds Center", Vector) = (0,0,0,0)
        [PerRendererData] _ToonBoundsExtents ("Bounds Extents", Vector) = (0.5,0.5,0.5,0)
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.35
        _ShadowThreshold ("Shadow Threshold", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _OutlineColor;
            float _BorderWidth;
            float4 _ToonBoundsCenter;
            float4 _ToonBoundsExtents;
            float _ShadowStrength;
            float _ShadowThreshold;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD0;
            float3 normalOS : TEXCOORD1;
            float3 positionWS : TEXCOORD2;
            float3 positionOS : TEXCOORD3;
        };

        /// <summary>입력 정점과 법선으로 UV를 사용하지 않는 표면 표시 정보를 반환한다.</summary>
        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.normalOS = input.normalOS;
            output.positionOS = input.positionOS.xyz;
            return output;
        }
        ENDHLSL
        Pass
        {
            Name "ToonSurface"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            /// <summary>단색 표면에 조명 방향과 그림자를 사용한 두 단계 명암을 표시한다.</summary>
            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float diffuse = saturate(dot(normal, light.direction));
                float lit = step(_ShadowThreshold, diffuse * light.shadowAttenuation);
                float topFacing = saturate(abs(normalize(input.normalOS).y));
                float faceShade = lerp(0.72, 1.0, smoothstep(0.5, 0.9, topFacing));
                float shade = lerp(1.0 - _ShadowStrength, 1.0, lit) * faceShade;
                half3 color = _BaseColor.rgb * shade;
                float3 distanceToEdge = _ToonBoundsExtents.xyz - abs(input.positionOS - _ToonBoundsCenter.xyz);
                float3 axis = abs(normalize(input.normalOS));
                float edgeDistance = axis.y >= max(axis.x, axis.z) ? min(distanceToEdge.x, distanceToEdge.z) : (axis.x >= axis.z ? min(distanceToEdge.y, distanceToEdge.z) : min(distanceToEdge.x, distanceToEdge.y));
                float border = 1.0 - step(_BorderWidth, edgeDistance);
                return half4(lerp(color, _OutlineColor.rgb, border), 1);
            }
            ENDHLSL
        }
    }
}

