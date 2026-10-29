Shader "Keyboard/ArtisanPassiveIcon"
{
    Properties { _Tint ("Pastel Color", Color) = (1,0.93,0.6,1) _Kind ("Kind", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            float _Kind;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            float Box(float2 p, float2 b) { float2 d = abs(p)-b; return length(max(d,0))+min(max(d.x,d.y),0); }
            float Ellipse(float2 p, float2 r) { return (length(p/r)-1)*min(r.x,r.y); }
            float Shape(float2 p)
            {
                if (_Kind < 0.5)
                {
                    float d = min(Box(p-float2(0,-0.34),float2(0.37,0.065)), Box(p-float2(0,-0.07),float2(0.22-0.09*p.y,0.24)));
                    d = min(d, Box(p-float2(0,0.25),float2(0.34,0.11)));
                    d = max(d,-Box(p-float2(-0.13,0.37),float2(0.055,0.075)));
                    return max(d,-Box(p-float2(0.13,0.37),float2(0.055,0.075)));
                }
                if (_Kind < 1.5)
                {
                    float d = min(Box(p-float2(0,-0.35),float2(0.34,0.06)),Ellipse(p-float2(0,-0.02),float2(0.25,0.32)));
                    d = min(d,Ellipse(p-float2(0,0.33),float2(0.09,0.08)));
                    float2 q = float2(p.x*0.86+p.y*0.5,-p.x*0.5+p.y*0.86);
                    return max(d,-Box(q-float2(0.13,0.22),float2(0.045,0.2)));
                }
                if (_Kind < 2.5)
                {
                    float d = Ellipse(p-float2(0,-0.14),float2(0.23,0.19));
                    d = min(d,Ellipse(p-float2(-0.16,-0.23),float2(0.15,0.12)));
                    d = min(d,Ellipse(p-float2(0.16,-0.23),float2(0.15,0.12)));
                    d = min(d,Ellipse(p-float2(-0.29,0.08),float2(0.095,0.14)));
                    d = min(d,Ellipse(p-float2(-0.11,0.26),float2(0.095,0.14)));
                    d = min(d,Ellipse(p-float2(0.11,0.26),float2(0.095,0.14)));
                    return min(d,Ellipse(p-float2(0.29,0.08),float2(0.095,0.14)));
                }
                if (_Kind < 3.5)
                {
                    float d = max(max(-p.y-0.3,p.y-0.32),max(-p.x-0.37,p.x+0.57*p.y-0.22));
                    d = max(d,-length(p-float2(-0.18,-0.1))+0.09);
                    d = max(d,-length(p-float2(-0.12,0.17))+0.055);
                    d = max(d,-length(p-float2(0.09,-0.19))+0.06);
                    return max(d,-length(p-float2(-0.37,0.13))+0.07);
                }
                float body = max(Ellipse(p-float2(0,-0.08),float2(0.34,0.32)),-p.y-0.34-0.035*cos(p.x*35));
                body = min(body,min(Ellipse(p-float2(-0.28,0.1),float2(0.11,0.16)),Ellipse(p-float2(0.28,0.1),float2(0.11,0.16))));
                body = max(body,-Ellipse(p-float2(-0.13,-0.01),float2(0.05,0.022)));
                body = max(body,-Ellipse(p-float2(0.13,-0.01),float2(0.05,0.022)));
                body = max(body,-Ellipse(p-float2(0,-0.13),float2(0.07,0.02)));
                float halo = abs(Ellipse(p-float2(0,0.37),float2(0.22,0.055)))-0.016;
                return min(body,halo);
            }
            Varyings Vert(Attributes input) { Varyings o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz); o.uv=input.uv; return o; }
            half4 Frag(Varyings input) : SV_Target
            {
                float d=Shape((input.uv-0.5)*1.1);
                float aa=max(fwidth(d),0.002);
                half alpha=1-smoothstep(0.018-aa,0.018+aa,d);
                half border=smoothstep(-0.012-aa,-0.012+aa,d);
                return half4(lerp(_Tint.rgb,_Tint.rgb*0.38,border),alpha*_Tint.a);
            }
            ENDHLSL
        }
    }
}
