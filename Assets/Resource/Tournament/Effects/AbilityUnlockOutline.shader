Shader "Keyboard/AbilityUnlockOutline"
{
    Properties
    {
        _OutlineColor ("Unlock Ivory", Color) = (1,0.93,0.65,1)
        _Width ("Width Pixels", Range(0,6)) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _OutlineColor;
            float _Width;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            // 표면 법선을 따라 화면에서 일정한 폭으로 확장한다.
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 normalVS = TransformWorldToViewDir(normalWS);
                float2 direction = normalVS.xy / max(length(normalVS.xy), 0.0001);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionCS.xy += direction * (_Width * 2.0 / _ScreenParams.xy) * output.positionCS.w;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }
    }
}
