Shader "Voxels/Opaque" {
    Properties {
        [NoScaleOffset] _MainTex ("Voxel Texture Atlas", 2D) = "white" { }
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        
        _SunTint ("Sun Light Tint", Color) = (1, 1, 1, 1)
        _SunIntensity ("Sun Light Intensity", Range(0, 10)) = 1.0
        _BlockIntensity ("Block Light Intensity", Range(0, 10)) = 1.0
    }
    
    SubShader {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.5

        sampler2D _MainTex;

        struct Input {
            float2 uv_MainTex;
            float4 sunLight;
            float4 blockLight;
        };

        half _Glossiness;
        half _Metallic;

        half4 _SunTint;
        half _SunIntensity;
        half _BlockIntensity;

        void vert(inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            
            o.sunLight = v.texcoord1 * _SunIntensity;
            o.sunLight.rgb *= _SunTint.rgb;
            o.blockLight = v.texcoord2 * _BlockIntensity;
        }

        void surf (Input i, inout SurfaceOutputStandard o) {
            //i.uv_MainTex.x = 1.0 - i.uv_MainTex.x;
            i.uv_MainTex.y = 1.0 - i.uv_MainTex.y;
            fixed4 c = tex2D (_MainTex, i.uv_MainTex);
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
