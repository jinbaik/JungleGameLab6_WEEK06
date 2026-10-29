Shader "Keyboard/PCB Light Spill"
{
    Properties
    {
        [HDR] _LEDColor ("LED Color", Color) = (0, 0.8, 1, 1)
        _SpillRange ("Spill Range", Range(0.05, 10)) = 0.85
        _SpillStrength ("Spill Strength", Range(0, 10)) = 0.12
        _SpillSoftness ("Spill Softness", Range(0.1, 32)) = 2
        _Intensity ("Intensity", Float) = 3
        _Pattern ("Pattern", Float) = 0
        _Speed ("Speed", Float) = 1
        _AnimationTime ("Animation Time", Float) = 0
        _KeyboardBounds ("Keyboard Bounds", Vector) = (0, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "LED"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _LEDColor;
                float _SpillRange;
                float _SpillStrength;
                float _SpillSoftness;
                float _Intensity;
                float _Pattern;
                float _Speed;
                float _AnimationTime;
                float4 _KeyboardBounds;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float keyboardX : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.keyboardX = (input.positionOS.x - _KeyboardBounds.x) / _KeyboardBounds.y;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 edge = abs(input.uv * 2 - 1);
                half glow = pow(saturate(1 - length(edge) / max(_SpillRange, 0.001)), _SpillSoftness) * _SpillStrength;
                half3 color = _LEDColor.rgb;
                float brightness = 1;
                if (_Pattern > 0.5 && _Pattern < 1.5)
                {
                    float hue = frac(input.keyboardX - _AnimationTime * _Speed * 0.2);
                    color = saturate(abs(frac(hue + float3(0, 2.0 / 3.0, 1.0 / 3.0)) * 6 - 3) - 1);
                }
                else if (_Pattern > 1.5)
                {
                    brightness = 0.5 + 0.5 * cos(_AnimationTime * _Speed * 2.0);
                }
                return half4(color * _Intensity * brightness, _LEDColor.a * glow * brightness);
            }
            ENDHLSL
        }
    }
}
