Shader "CubeSphere/ElevationColorRamp"
{
    Properties
    {
        _MainTex ("Elevation Map", 2D) = "black" {}
        _ElevMin ("Elevation Min", Float) = -100
        _ElevMax ("Elevation Max", Float) = 2000
        _ElevScale ("Elevation Scale", Float) = 1.0
        _MaxLandElev ("Max Land Elevation", Float) = 1500
        _MaxDepth ("Max Water Depth", Float) = 100
        _NightBrightness ("Night Side Brightness", Range(0, 1)) = 0.10
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _ElevMin;
                float _ElevMax;
                float _ElevScale;
                float _MaxLandElev;
                float _MaxDepth;
                float _NightBrightness;
            CBUFFER_END

            half3 WaterColor(float depth01)
            {
                half3 shallow = half3(0.15, 0.45, 0.75);
                half3 deep    = half3(0.03, 0.10, 0.35);
                return lerp(shallow, deep, saturate(depth01));
            }

            half3 LandColor(float height01)
            {
                half3 coast    = half3(0.65, 0.82, 0.50);
                half3 lowland  = half3(0.30, 0.65, 0.15);
                half3 midland  = half3(0.55, 0.60, 0.25);
                half3 highland = half3(0.60, 0.45, 0.25);
                half3 mountain = half3(0.50, 0.35, 0.25);
                half3 peak     = half3(0.95, 0.95, 0.95);

                float t = saturate(height01);
                if (t < 0.05) return lerp(coast, lowland, t / 0.05);
                if (t < 0.15) return lerp(lowland, midland, (t - 0.05) / 0.10);
                if (t < 0.35) return lerp(midland, highland, (t - 0.15) / 0.20);
                if (t < 0.65) return lerp(highland, mountain, (t - 0.35) / 0.30);
                return lerp(mountain, peak, (t - 0.65) / 0.35);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float elev = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).r;
                elev *= _ElevScale;

                half3 color;
                if (elev <= 0.0)
                {
                    float depth01 = saturate(-elev / _MaxDepth);
                    color = WaterColor(depth01);
                }
                else
                {
                    float height01 = saturate(elev / _MaxLandElev);
                    color = LandColor(height01);
                }

                // Lighting
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = half3(1.0, 1.0, 1.17) * _NightBrightness;
                color *= ambient + NdotL * (1.0 - _NightBrightness) * mainLight.color.rgb;

                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
