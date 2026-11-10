using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Run materials with manufacturer print lines (PEX, PVC/ABS DWV, NM-B cable), plus purple primer daubs and
    /// crinkled foil tape. MepMesh sweep UVs: U = meters along the run, V = meters around the profile, so a print strip
    /// tiles along U and clamps in V (one line of print per run).</summary>
    public static class MepPrint
    {
        static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();

        const int StripW = 512, StripH = 16;
        const float StripU = 0.30f;                                   // print repeats every 30 cm
        const float StripV = StripU / StripW * StripH;                // ~9.4 mm band, 4 mm tall letters

        /// <summary>The material for a run's swept body (falls back to MepLook.Of for unprinted runs).</summary>
        public static Material RunMaterial(RunSpec s)
        {
            if (s == null) return MepLook.Get(SurfaceKind.PaintedWhite);
            string text = PrintFor(s, out float gloss, out Color ink);
            if (text == null) return MepLook.Of(s);
            string key = s.Id + "|" + text;
            if (_cache.TryGetValue(key, out var m) && m != null) return m;
            Color baseCol = s.Surface == SurfaceKind.DarkPlastic ? new Color(0.13f, 0.13f, 0.14f) : new Color(0.84f, 0.83f, 0.80f);
            m = new Material(Shader.Find("Standard")) { name = "GB_Print_" + s.Id, hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.mainTexture = Strip(text, ink, s.Id.GetHashCode());
            m.color = new Color(baseCol.r * s.Tint.r, baseCol.g * s.Tint.g, baseCol.b * s.Tint.b, 1f);
            m.SetFloat("_Glossiness", gloss);
            m.SetFloat("_Metallic", 0f);
            m.mainTextureScale = new Vector2(1f / StripU, 1f / StripV);
            m.mainTextureOffset = new Vector2(0f, -0.15f);
            m.enableInstancing = true;
            _cache[key] = m;
            return m;
        }

        static string Size(float nominalIn)
        {
            if (Mathf.Abs(nominalIn - 0.5f) < 0.01f) return "1/2 IN";
            if (Mathf.Abs(nominalIn - 0.75f) < 0.01f) return "3/4 IN";
            if (Mathf.Abs(nominalIn - 1.5f) < 0.01f) return "1-1/2 IN";
            if (Mathf.Abs(nominalIn - 1.25f) < 0.01f) return "1-1/4 IN";
            return Mathf.RoundToInt(nominalIn) + " IN";
        }

        /// <summary>Generic print legends (no brand names): what the stamp on real pipe/cable says.</summary>
        static string PrintFor(RunSpec s, out float gloss, out Color ink)
        {
            gloss = 0.45f; ink = new Color(0.08f, 0.08f, 0.09f);
            if (s.Trade == Trade.Plumbing && s.Join == JoinMethod.Crimp)
            {
                gloss = 0.55f;
                if (s.Tint.r > 0.95f && s.Tint.g > 0.95f) ink = new Color(0.12f, 0.20f, 0.55f);
                return "PEX-B " + Size(s.NominalIn) + " CTS SDR9  NSF-PW  ASTM F876/F877  100 PSI 180F  160 PSI 73F";
            }
            if (s.Join == JoinMethod.Solvent && (s.Kind == NetKind.Drain || s.Kind == NetKind.Vent))
            {
                bool abs = s.Surface == SurfaceKind.DarkPlastic;
                if (abs) { ink = new Color(0.85f, 0.85f, 0.82f); gloss = 0.35f; return "ABS-DWV SCH 40  " + Size(s.NominalIn) + "  ASTM D2661  NSF-DWV  CELLULAR CORE"; }
                gloss = 0.42f;
                return "PVC-DWV SCHEDULE 40  " + Size(s.NominalIn) + "  ASTM D2665  NSF-DWV  CELLULAR CORE";
            }
            if (s.Trade == Trade.Electrical && s.Kind == NetKind.Power && s.Awg > 0 && s.Shape == RunShape.Flat)
            {
                gloss = 0.32f;
                return "TYPE NM-B " + s.Awg + " AWG " + s.Conductors + " CONDUCTOR WITH GROUND 600V  (UL)  " + s.Awg + "-" + s.Conductors + " W/G";
            }
            return null;
        }

        static Texture2D Strip(string text, Color ink, int seed)
        {
            var px = new Color32[StripW * StripH];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            int x0 = 24;
            byte ir = (byte)(ink.r * 255), ig = (byte)(ink.g * 255), ib = (byte)(ink.b * 255);
            for (int ci = 0; ci < text.Length; ci++)
            {
                var g = LumberMarks.Glyph(text[ci]);
                if (g == null) continue;
                for (int gy = 0; gy < 7; gy++)
                    for (int gx = 0; gx < 5; gx++)
                    {
                        if (g[gy][gx] != '#') continue;
                        int x = x0 + ci * 6 + gx, y = StripH - 5 - gy;  // rows 4..10, blank margins top & bottom
                        if (x < 0 || x >= StripW) continue;
                        // ink-jet print: slightly uneven dots
                        float k = 0.75f + 0.25f * ShellMaterials.Hash(x, y, seed);
                        px[y * StripW + x] = Color32.Lerp(new Color32(255, 255, 255, 255), new Color32(ir, ig, ib, 255), k);
                    }
            }
            var t = new Texture2D(StripW, StripH, TextureFormat.RGBA32, true) { name = "GB_PrintStrip", filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.wrapModeU = TextureWrapMode.Repeat; t.wrapModeV = TextureWrapMode.Clamp;
            t.SetPixels32(px); t.Apply(true, true);
            return t;
        }

        /// <summary>Purple primer swabbed with a dauber: streaky, uneven density, a few thin spots.</summary>
        public static Material Primer
        {
            get
            {
                const string key = "mep-primer-purple";
                if (ShellMaterials.TryGet(key, out var m)) return m;
                const int n = 128;
                var col = new Color[n * n]; var h = new float[n * n];
                Color deep = new Color(0.36f, 0.12f, 0.52f), thin = new Color(0.62f, 0.46f, 0.72f);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        // dauber strokes run around the pipe (V); density changes along U in streaks
                        float streak = ShellMaterials.Noise(x, y, 4, n / 4, 301) * 0.4f + ShellMaterials.Noise(x * 4f, y * 0.25f, 16, n / 4, 302) * 0.6f;
                        float d = Mathf.Clamp01(0.25f + streak * 0.95f);
                        Color c = Color.Lerp(thin, deep, d);
                        col[y * n + x] = new Color(c.r, c.g, c.b, 0.35f + 0.4f * d);
                        h[y * n + x] = d * 0.4f;
                    }
                return ShellMaterials.Make(key, col, h, n, 0.05f, 0.35f, 0.3f, 1.2f);
            }
        }

        /// <summary>UL 181 aluminum foil tape: bright, slightly crinkled, smoothed down by hand.</summary>
        public static Material FoilTape
        {
            get
            {
                const string key = "mep-foil-tape";
                if (ShellMaterials.TryGet(key, out var m)) return m;
                const int n = 128;
                var col = new Color[n * n]; var h = new float[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float crinkle = ShellMaterials.Noise(x, y, 8, n / 8, 311) * 0.6f + ShellMaterials.Noise(x, y, 4, n / 4, 312) * 0.25f
                                        + Mathf.Abs(Mathf.Sin((x * 0.8f + y * 0.15f) * 0.35f)) * 0.15f;
                        float v = 0.84f + 0.08f * crinkle;
                        col[y * n + x] = new Color(v, v, v * 1.01f, 0.65f + 0.3f * crinkle);
                        h[y * n + x] = crinkle;
                    }
                return ShellMaterials.Make(key, col, h, n, 0.08f, 0.70f, 0.6f, 2.5f, 0.55f);
            }
        }
    }
}
