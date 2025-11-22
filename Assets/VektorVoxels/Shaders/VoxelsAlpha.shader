Shader "Voxels/AlphaBlended"
{
    Properties {
        [NoScaleOffset] _MainTex ("Voxel Texture Atlas", 2D) = "white" { }
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0

        _SunTint ("Sun Light Tint", Color) = (1, 1, 1, 1)
        _SunIntensity ("Sun Light Intensity", Range(0, 10)) = 1.0
        _BlockIntensity ("Block Light Intensity", Range(0, 10)) = 1.0

        _TileSize ("Atlas Tile Size", Float) = 0.0625
    }

    SubShader {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard vertex:vert alpha:fade
        #pragma target 3.5

        sampler2D _MainTex;

        struct Input {
            float2 uv_MainTex;
            float4 sunLight;
            float4 blockLight;
            float2 tileRepeat;
        };

        half _Glossiness;
        half _Metallic;

        half4 _SunTint;
        half _SunIntensity;
        half _BlockIntensity;
        half _TileSize;

        void vert(inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            o.sunLight = v.texcoord1 * _SunIntensity;
            o.sunLight.rgb *= _SunTint.rgb;
            o.blockLight = v.texcoord2 * _BlockIntensity;

            // Tile repeat counts stored in texcoord3 (xy = repeat count for U and V)
            o.tileRepeat = v.texcoord3.xy;
        }

        void surf (Input i, inout SurfaceOutputStandard o) {
            float2 uv = i.uv_MainTex;

            // Apply atlas-aware tiling if repeat counts are set (> 0)
            if (i.tileRepeat.x > 0 && i.tileRepeat.y > 0) {
                // Calculate the tile's base position (floor to tile boundary)
                float2 tileBase = floor(uv / _TileSize) * _TileSize;

                // Calculate local UV within the tile, then tile it
                float2 localUV = uv - tileBase;
                float2 tiledLocal = frac(localUV / _TileSize * i.tileRepeat) * _TileSize;

                uv = tileBase + tiledLocal;
            }

            fixed4 c = tex2D (_MainTex, uv);
            o.Albedo = c.rgb;

            // Voxel lighting is mapped to the emission channel for now.
            o.Emission = c.rgb * max(i.blockLight.rgb, i.sunLight.rgb);
            o.Smoothness = _Glossiness;
            o.Metallic = _Metallic;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
