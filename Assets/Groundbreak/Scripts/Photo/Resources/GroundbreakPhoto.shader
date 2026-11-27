// Photo mode post-processing for the built-in pipeline (loaded with Resources.Load so it ships in builds).
// Pass 0: exposure + colour filter + vignette + frame.  Pass 1: running-average accumulation (depth-of-field samples).
Shader "Hidden/Groundbreak/Photo"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Mode, _Exposure, _Vignette, _Frame, _Aspect;

            float3 Sat(float3 c, float s) { float l = dot(c, float3(0.299, 0.587, 0.114)); return lerp(l.xxx, c, s); }

            fixed4 frag (v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float3 c = tex2D(_MainTex, uv).rgb * _Exposure;
                float l = dot(c, float3(0.299, 0.587, 0.114));
                int mode = (int)round(_Mode);
                if (mode == 1) c = saturate((l - 0.5) * 1.15 + 0.5).xxx;                               // black & white
                else if (mode == 2) c = lerp(c, float3(l * 1.07, l * 0.86, l * 0.62), 0.9);           // sepia
                else if (mode == 3) c = c * float3(1.08, 1.0, 0.86);                                  // warm
                else if (mode == 4) c = c * float3(0.88, 0.98, 1.1);                                  // cool
                else if (mode == 5) c = saturate((Sat(c, 1.35) - 0.5) * 1.12 + 0.5);                  // vivid
                else if (mode == 6) c = Sat(c, 0.7) * float3(0.95, 0.97, 0.88) * 0.88 + float3(0.07, 0.07, 0.06); // faded film

                float2 d = (uv - 0.5) * float2(_Aspect, 1.0);
                float r = length(d) / length(float2(_Aspect, 1.0) * 0.5);
                c *= 1.0 - _Vignette * smoothstep(0.45, 1.05, r);

                int frame = (int)round(_Frame);
                float mx = 1.0 / _Aspect;   // converts a height fraction to a width fraction (equal pixel margins)
                if (frame == 1)             // instant-print: white border, deep bottom margin
                {
                    float m = 0.045;
                    if (uv.x < m * mx || uv.x > 1 - m * mx || uv.y > 1 - m || uv.y < 0.17) c = float3(0.95, 0.94, 0.91);
                }
                else if (frame == 2)        // gallery mat: dark moulding, white mat
                {
                    float o = 0.022, m = 0.085;
                    if (uv.x < o * mx || uv.x > 1 - o * mx || uv.y < o || uv.y > 1 - o) c = float3(0.09, 0.08, 0.07);
                    else if (uv.x < m * mx || uv.x > 1 - m * mx || uv.y < m || uv.y > 1 - m) c = float3(0.93, 0.92, 0.9);
                }
                else if (frame == 3)        // 2.39:1 letterbox
                {
                    if (abs(uv.y - 0.5) > 0.5 * _Aspect / 2.39) c = 0;
                }
                else if (frame == 4)        // job-site blueprint corners
                {
                    float2 e = min(uv, 1 - uv) * float2(_Aspect, 1.0);
                    bool inBox = e.x > 0.03 && e.y > 0.03 && e.x < 0.12 && e.y < 0.12;
                    if (inBox && (e.x < 0.037 || e.y < 0.037)) c = float3(0.98, 0.62, 0.15);
                }
                return fixed4(c, 1);
            }
            ENDCG
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Weight;
            fixed4 frag (v2f_img i) : SV_Target { return fixed4(tex2D(_MainTex, i.uv).rgb, _Weight); }
            ENDCG
        }
    }
}
