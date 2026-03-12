Shader "Skybox/ProceduralStarSkybox"
{
    Properties
    {
        _StarDensity ("Star Density", Float) = 80
        _StarBrightness ("Star Brightness", Float) = 1.5
        _StarSize ("Star Size", Float) = 0.015
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _StarDensity;
                float _StarBrightness;
                float _StarSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDir    : TEXCOORD0;
            };

            // Hash function for pseudo-random values
            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.viewDir = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.viewDir);

                // Convert direction to spherical UV for cell tiling
                float2 uv = float2(atan2(dir.z, dir.x), asin(dir.y));
                uv *= _StarDensity;

                float2 cell = floor(uv);
                float2 localUV = frac(uv) - 0.5;

                // Hash the cell to get star position and brightness
                float2 starOffset = Hash2(cell) - 0.5;
                float dist = length(localUV - starOffset * 0.8);

                // Star brightness based on hash
                float brightness = Hash2(cell + 100.0).x;
                brightness = pow(brightness, 3.0); // Make most stars dim, few bright

                // Sharp star dot
                float star = smoothstep(_StarSize, _StarSize * 0.3, dist);
                star *= brightness * _StarBrightness;

                return half4(star, star, star, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
