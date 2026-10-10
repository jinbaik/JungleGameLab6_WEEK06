Shader "Environment/WindowLightBeam"
{
    Properties
    {
        [HDR] _Color("Light Color", Color) = (1, 0.9, 0.7, 0.025)
        _DustMode("Dust Mode", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _DustMode;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float edge = pow(saturate(1.0 - abs(input.uv.x * 2.0 - 1.0)), 1.4);
                float fade = smoothstep(0.0, 0.025, input.uv.y) * (1.0 - smoothstep(0.45, 1.0, input.uv.y));
                float dust = pow(saturate(1.0 - length(input.uv * 2.0 - 1.0)), 2.0);
                return half4(_Color.rgb, _Color.a * lerp(edge * fade, dust, _DustMode));
            }
            ENDHLSL
        }
    }
}
