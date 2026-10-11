// FoliageFX leaves: alpha-tested foliage that casts and receives shadows, for
// the leaves of CustomUnityAsset trees and plants.
//
// Leaves exported on the Standard shader in Fade or Transparent mode, or on a
// legacy Transparent shader, draw in the Transparent queue. Unity's built-in
// pipeline never shadows that queue and leaves it out of the camera's depth
// texture, so those leaves stay fully lit inside a shadow and seem to glow.
// This shader draws them alpha tested in the AlphaTest queue instead: they
// write depth, cast and receive shadows from every light, and are lit like
// Standard.
//
// The plugin never edits the tree's own material. It copies it and swaps the
// shader on the copy; the property names are the Standard shader's, so the
// texture, tiling, colour, smoothness and metallic carry over. It turns on
// _NORMALMAP when the source has a normal map and writes everything else (the
// recolouring, cutoff, translucency, cull, soft edges, distance coverage and
// wind) as material values. None of those is a keyword, so every combination
// is already compiled into the bundle.
//
// Soft edges are alpha to coverage. With MSAA the lit passes write alpha as a
// ramp one pixel wide around the cutoff, and the hardware turns it into a
// share of each pixel's samples: a smooth outline that still writes depth.
// Hard edges write alpha 1, every sample. The ShadowCaster pass, which also
// draws the camera's depth texture, returns depth instead of alpha (zero for
// most lights), so with alpha to coverage on it would drop every sample; it
// turns it off and clips at the cutoff.
//
// Hue, saturation and brightness work in gamma space, the space the texture
// was painted in and VaM's colour pickers show, so the sliders move a leaf's
// colour the way a picker would. VaM renders in linear space, so the colour
// goes to gamma and back.
Shader "Vampix Collections/FoliageFX/Leaves"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Leaves (RGB) Alpha (A)", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0.01, 0.99)) = 0.5
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        _Glossiness ("Smoothness", Range(0, 1)) = 0.2
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _HueShift ("Hue Shift", Range(-180, 180)) = 0
        _Saturation ("Saturation", Range(0, 2)) = 1
        _Brightness ("Brightness", Range(0, 2)) = 1
        _Translucency ("Translucency", Range(0, 2)) = 0.0
        _TranslucencyDistortion ("Translucency Spread", Range(0, 1)) = 0.3
        _TranslucencyPower ("Translucency Focus", Range(1, 16)) = 4.0
        _SoftEdges ("Soft Edges", Range(0, 1)) = 0
        _MipCoverage ("Distance Coverage", Range(0, 1)) = 0.25
        _WindStrength ("Wind Strength", Range(0, 1)) = 0
        _WindSpeed ("Wind Speed", Range(0, 4)) = 1
        _WindDirection ("Wind Direction", Vector) = (0, 0, 1, 0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        LOD 300
        Cull [_Cull]
        AlphaToMask On

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        fixed4 _Color;
        half _Cutoff;
        half _MipCoverage;
        float _WindStrength;
        float _WindSpeed;
        float4 _WindDirection;

        // In world space, so a leaf moves the same distance whatever the
        // tree's scale and the flutter's phase changes from leaf to leaf, with
        // a slow sway along the wind on top: about ten centimetres at full
        // strength.
        void ApplyWind(inout float4 vertex)
        {
            if (_WindStrength <= 0) return;
            float3 wp = mul(unity_ObjectToWorld, vertex).xyz;
            float t = _Time.y * _WindSpeed;
            float phase = dot(wp, float3(1.7, 0.9, 1.3));
            float3 flutter = float3(sin(t * 3.1 + phase),
                                    sin(t * 2.3 + phase * 1.31) * 0.5,
                                    sin(t * 2.7 + phase * 0.77 + 1.3));
            float sway = sin(t * 0.8 + dot(wp.xz, float2(0.11, 0.07)));
            wp += (flutter * 0.6 + float3(_WindDirection.x, 0, _WindDirection.z) * sway * 0.4) * (_WindStrength * 0.08);
            vertex = mul(unity_WorldToObject, float4(wp, 1));
        }

        // Mipmaps average alpha, so a distant canopy thins out as its texels
        // fall under the cutoff. Alpha grows with the mip level to keep it
        // full; the plugin sets _MipCoverage to 0 on a texture without mips.
        half LeafAlpha(half a, float2 uv)
        {
            float2 texel = uv * _MainTex_TexelSize.zw;
            float2 dx = ddx(texel);
            float2 dy = ddy(texel);
            float mip = 0.5 * log2(max(dot(dx, dx), dot(dy, dy)));
            return a * (1 + max(0, mip) * _MipCoverage);
        }
        ENDCG

        CGPROGRAM
        // keepalpha: the lit passes' alpha is the coverage. Forward passes
        // only: VaM renders forward, and the translucency exists only in the
        // forward lighting function.
        #pragma surface surf Foliage vertex:vert fullforwardshadows keepalpha exclude_path:deferred exclude_path:prepass
        #pragma target 3.0
        #pragma multi_compile __ _NORMALMAP

        #include "UnityPBSLighting.cginc"

        sampler2D _BumpMap;
        half _BumpScale;
        half _Glossiness;
        half _Metallic;
        half _HueShift;
        half _Saturation;
        half _Brightness;
        half _Translucency;
        half _TranslucencyDistortion;
        half _TranslucencyPower;
        half _SoftEdges;

        struct Input
        {
            float2 uv_MainTex;
            fixed facing : VFACE;
        };

        void vert(inout appdata_full v)
        {
            ApplyWind(v.vertex);
        }

        float3 RGBToHSV(float3 c)
        {
            float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
            float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
            float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
            float d = q.x - min(q.w, q.y);
            float e = 1.0e-10;
            return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
        }

        float3 HSVToRGB(float3 c)
        {
            float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
            float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
            return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
        }

        half3 Recolour(half3 c)
        {
            // Untouched, the colour skips the trip: the two gamma conversions
            // are approximations and would shift it slightly.
            if (_HueShift == 0 && _Saturation == 1 && _Brightness == 1) return c;
        #ifndef UNITY_COLORSPACE_GAMMA
            c = LinearToGammaSpace(c);
        #endif
            float3 hsv = RGBToHSV(c);
            hsv.x = frac(hsv.x + _HueShift / 360.0);
            hsv.y = saturate(hsv.y * _Saturation);
            hsv.z *= _Brightness;
            c = HSVToRGB(hsv);
        #ifndef UNITY_COLORSPACE_GAMMA
            c = GammaToLinearSpace(c);
        #endif
            return c;
        }

        // Standard lighting, plus the light that comes through a thin leaf
        // from behind. gi.light.color already carries the shadow, so a leaf in
        // shade lets no light through either.
        inline half4 LightingFoliage(SurfaceOutputStandard s, half3 viewDir, UnityGI gi)
        {
            half4 c = LightingStandard(s, viewDir, gi);
            half3 h = normalize(gi.light.dir + s.Normal * _TranslucencyDistortion);
            half t = pow(saturate(dot(viewDir, -h)), _TranslucencyPower) * _Translucency;
            c.rgb += s.Albedo * gi.light.color * t;
            return c;
        }

        inline void LightingFoliage_GI(SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
        {
            LightingStandard_GI(s, data, gi);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 tex = tex2D(_MainTex, IN.uv_MainTex);
            half a = LeafAlpha(tex.a * _Color.a, IN.uv_MainTex);
            half width = max(fwidth(a), 0.0001);
            if (_SoftEdges > 0.5)
            {
                // Centred on the cutoff, so the soft outline sits where the
                // hard one would.
                o.Alpha = saturate((a - _Cutoff) / width + 0.5);
                clip(o.Alpha - 0.004);
            }
            else
            {
                clip(a - _Cutoff);
                o.Alpha = 1;
            }

            o.Albedo = Recolour(tex.rgb) * _Color.rgb;
        #if defined(_NORMALMAP)
            half3 n = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
        #else
            half3 n = half3(0, 0, 1);
        #endif
            // A double-sided leaf seen from the back is lit from the side the
            // camera sees.
            n.z *= IN.facing > 0 ? 1 : -1;
            o.Normal = n;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
        }
        ENDCG

        // Written by hand to turn alpha to coverage off. The same wind and the
        // same cutoff as the lit passes, so the shadow moves with the leaves
        // and has their outline.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            AlphaToMask Off
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma target 3.0
            #pragma multi_compile_shadowcaster

            // Declared here only: the surface shader above declares its own.
            float4 _MainTex_ST;

            struct v2fShadow
            {
                V2F_SHADOW_CASTER;
                float2 uv : TEXCOORD1;
            };

            v2fShadow vertShadow(appdata_full v)
            {
                v2fShadow o;
                ApplyWind(v.vertex);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            float4 fragShadow(v2fShadow i) : SV_Target
            {
                half a = LeafAlpha(tex2D(_MainTex, i.uv).a * _Color.a, i.uv);
                clip(a - _Cutoff);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    FallBack Off
}
