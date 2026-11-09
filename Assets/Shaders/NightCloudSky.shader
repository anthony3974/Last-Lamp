// Night sky with drifting clouds, stars and a moon. No textures. Tune it on the Night_Sky material.
Shader "LastLamp/Night Cloud Sky"
{
    Properties
    {
        _SkyTop ("Sky Top", Color) = (0.04, 0.05, 0.09, 1)
        _SkyHorizon ("Sky Horizon", Color) = (0.12, 0.14, 0.2, 1)
        _CloudColor ("Cloud Color", Color) = (0.5, 0.52, 0.58, 1)
        _CloudCover ("Cloud Cover", Range(0, 1)) = 0.5
        _CloudSoftness ("Cloud Softness", Range(0.01, 0.5)) = 0.2
        _CloudScale ("Cloud Scale", Float) = 2.2
        _CloudSpeed ("Cloud Drift Speed", Float) = 0.012
        _StarAmount ("Star Amount", Range(0, 1)) = 0.35
        _MoonDirection ("Moon Direction", Vector) = (0.15, 0.55, 0.8, 0)
        _MoonSize ("Moon Size", Range(0.005, 0.15)) = 0.05
        _MoonGlow ("Moon Glow", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _SkyTop;
            fixed4 _SkyHorizon;
            fixed4 _CloudColor;
            float _CloudCover;
            float _CloudSoftness;
            float _CloudScale;
            float _CloudSpeed;
            float _StarAmount;
            float4 _MoonDirection;
            float _MoonSize;
            float _MoonGlow;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Hash(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(Hash(cell), Hash(cell + float2(1, 0)), f.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + float2(1, 1)), f.x),
                    f.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 5; i++)
                {
                    value += amplitude * Noise(p);
                    p = p * 2.02 + float2(17.3, 9.2);
                    amplitude *= 0.5;
                }
                return value;
            }

            float Stars(float3 direction)
            {
                const float grid = 420.0;
                float3 scaled = direction * grid;
                float3 cell = floor(scaled);
                if (Hash(cell) < 1.0 - 0.02 * _StarAmount) return 0.0;

                float fromCentre = length(frac(scaled) - 0.5);
                float twinkle = 0.7 + 0.3 * sin(_Time.y * 2.0 + Hash(cell + 7.0) * 40.0);
                return (1.0 - smoothstep(0.1, 0.5, fromCentre)) * twinkle;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 direction = normalize(i.direction);
                float height = saturate(direction.y);
                float3 color = lerp(_SkyHorizon.rgb, _SkyTop.rgb, sqrt(height));

                float angleToMoon = acos(saturate(dot(direction, normalize(_MoonDirection.xyz))));
                float moon = 1.0 - smoothstep(_MoonSize * 0.9, _MoonSize * 1.1, angleToMoon);
                float glow = exp(-angleToMoon * 4.0) * _MoonGlow;

                float2 cloudPlane = direction.xz / max(direction.y + 0.35, 0.05);
                float2 drift = float2(_Time.y * _CloudSpeed, _Time.y * _CloudSpeed * 0.4);
                float density = Fbm(cloudPlane * _CloudScale + drift);
                float aboveHorizon = saturate(direction.y * 5.0);
                float clouds = smoothstep(_CloudCover, _CloudCover + _CloudSoftness, density) * aboveHorizon;

                color += Stars(direction) * _StarAmount * aboveHorizon;
                color += glow * 0.25 + moon;

                float3 litCloud = _CloudColor.rgb * (0.35 + glow);
                color = lerp(color, litCloud, clouds);

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
