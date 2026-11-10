// Ground surfaces (grass, dirt, gravel, topsoil, sod): Standard lighting plus anti-tiling.
// A second, rotated and rescaled sample of the same texture is blended in by world-space noise, and a large-scale
// (tens of meters) tint/brightness field adds the patchy variation a real lawn or site has, so the 1-2 m texture tile
// no longer reads as a checkerboard at distance. UVs stay in meters (_MainTex_ST tiling), so all existing meshes work.
Shader "Groundbreak/Terrain"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (A = smoothness)", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Bump scale", Float) = 1
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
        _GlossMapScale ("Smoothness map scale", Float) = 0.4
        _AlphaGloss ("Smoothness from albedo alpha", Range(0,1)) = 1
        _MacroTex ("Macro noise (RGB = 3 fields)", 2D) = "gray" {}
        _MacroScale ("Macro frequency (1/m)", Float) = 0.03
        _MacroStrength ("Macro strength", Range(0,1)) = 0.35
        _TintA ("Patch tint A", Color) = (1,1,1,1)
        _TintB ("Patch tint B", Color) = (1,1,1,1)
        _AntiTile ("Anti-tile blend", Range(0,1)) = 1
        _FarFade ("Distance (m) where detail flattens", Float) = 40
        _HighPass ("Remove tile-scale tone (0..1)", Range(0,1)) = 1
        _HPMip ("High-pass blur mip", Float) = 7.5
        _WorldTile ("World-projected tiling on steep faces (1/m, 0 = off)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex, _BumpMap, _MacroTex;
        half4 _Color, _TintA, _TintB;
        half _BumpScale, _Glossiness, _GlossMapScale, _AlphaGloss, _MacroScale, _MacroStrength, _AntiTile, _FarFade, _HighPass, _HPMip, _WorldTile;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal; INTERNAL_DATA
        };

        float2 Rot(float2 p, float a) { float c = cos(a), s = sin(a); return float2(p.x * c - p.y * s, p.x * s + p.y * c); }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 w = IN.worldPos.xz;
            // three octaves of the tileable noise at rotated, incommensurate scales so their repeats never line up
            float3 n1 = tex2D(_MacroTex, Rot(w, 0.41) * _MacroScale).rgb;               // ~33 m patches
            float3 n2 = tex2D(_MacroTex, Rot(w, 1.07) * _MacroScale * 4.7 + 0.37).rgb;  // ~7 m
            float3 n3 = tex2D(_MacroTex, Rot(w, -0.73) * _MacroScale * 0.213 + 0.71).rgb;// ~150 m

            // second sample: rotate 37 deg, scale 0.77 — breaks the repeat of the base tile
            float2 uvA = IN.uv_MainTex;
            const float cs = 0.7986, sn = 0.6018;
            float2 uvB = float2(uvA.x * cs - uvA.y * sn, uvA.x * sn + uvA.y * cs) * 0.77 + float2(0.31, 0.57);
            half4 a = tex2D(_MainTex, uvA);
            half4 b = tex2D(_MainTex, uvB);
            // high-pass: divide out the texture's own ~25-50 cm tone (it repeats every tile and reads as a checker at range),
            // keep the fine detail, and let the world-space macro field below supply the large-scale variation instead
            half3 mean = tex2Dlod(_MainTex, float4(0.5, 0.5, 0, 14)).rgb;
            half3 blurA = tex2Dlod(_MainTex, float4(uvA, 0, _HPMip)).rgb;
            half3 blurB = tex2Dlod(_MainTex, float4(uvB, 0, _HPMip)).rgb;
            a.rgb = lerp(a.rgb, a.rgb * mean / max(blurA, 0.02), _HighPass);
            b.rgb = lerp(b.rgb, b.rgb * mean / max(blurB, 0.02), _HighPass);
            half blend = saturate((n2.g - 0.5) * 4.0 + 0.5) * _AntiTile;
            half4 c = lerp(a, b, blend);

            // large-scale patchiness: tint between two colours, plus brightness waves
            half t = saturate((n1.r * 0.45 + n3.g * 0.55) * 2.2 - 0.6);
            half3 tint = lerp(_TintA.rgb, _TintB.rgb, t);
            half bright = 1.0 + ((n1.b - 0.5) * 0.45 + (n3.r - 0.5) * 0.6 + (n2.r - 0.5) * 0.4) * _MacroStrength;
            c.rgb *= lerp(half3(1,1,1), tint, _MacroStrength) * bright;

            // far away, average the texture toward its mean so the tile grid can't show
            float dist = distance(_WorldSpaceCameraPos, IN.worldPos);
            half far = saturate((dist - _FarFade) / (_FarFade * 3.0));
            c.rgb = lerp(c.rgb, mean * lerp(half3(1,1,1), tint, _MacroStrength) * bright, far * 0.8);

            half3 na = UnpackScaleNormal(tex2D(_BumpMap, uvA), _BumpScale);
            half3 nb = UnpackScaleNormal(tex2D(_BumpMap, uvB), _BumpScale);
            nb.xy = float2(nb.x * cs + nb.y * sn, -nb.x * sn + nb.y * cs); // undo the UV rotation
            half3 nrm = normalize(lerp(na, nb, blend) * half3(1, 1, 1 + far * 2));

            // steep faces (trench and hole walls): the mesh UVs are a top-down projection that smears there, so project the
            // texture onto the wall's own plane instead
            float3 wn = WorldNormalVector(IN, float3(0, 0, 1));
            half side = _WorldTile > 0 ? saturate((0.8 - abs(wn.y)) * 4.0) : 0;
            if (side > 0.001)
            {
                float2 uvS = (abs(wn.x) > abs(wn.z) ? IN.worldPos.zy : IN.worldPos.xy) * _WorldTile;
                half4 sc = tex2D(_MainTex, uvS);
                half3 blurS = tex2Dlod(_MainTex, float4(uvS, 0, _HPMip)).rgb;
                sc.rgb = lerp(sc.rgb, sc.rgb * mean / max(blurS, 0.02), _HighPass) * lerp(half3(1,1,1), tint, _MacroStrength) * 0.85; // walls sit in shade
                c = lerp(c, sc, side);
                nrm = normalize(lerp(nrm, half3(0, 0, 1), side * 0.7));
            }

            o.Albedo = c.rgb * _Color.rgb;
            o.Normal = nrm;
            o.Smoothness = lerp(_Glossiness, saturate(c.a * _GlossMapScale), _AlphaGloss);
            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
