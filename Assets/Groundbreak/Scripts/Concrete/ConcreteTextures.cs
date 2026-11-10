using UnityEngine;

namespace Groundbreak
{
    /// <summary>Procedural textures for the concrete trade's appended SurfaceKinds (wet concrete, troweled, broomed, poly).
    /// Periodic value noise so they tile; UVs are meters like everything else (tiling = 1 / Materials.TileMeters).</summary>
    public static class ConcreteTextures
    {
        const int N = 512;

        static uint Hash(int x, int y, int seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144665);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
        static float H01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;

        /// <summary>Periodic value noise with `cells` lattice cells across the texture.</summary>
        static float VNoise(float u, float v, int cells, int seed) => VNoise2(u, v, cells, cells, seed);

        static float VNoise2(float u, float v, int cu, int cv, int seed)
        {
            float x = u * cu, y = v * cv;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int W(int i) => ((i % cu) + cu) % cu;
            int Wv(int i) => ((i % cv) + cv) % cv;
            float a = H01(W(x0), Wv(y0), seed), b = H01(W(x0 + 1), Wv(y0), seed);
            float c = H01(W(x0), Wv(y0 + 1), seed), d = H01(W(x0 + 1), Wv(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int cells, int oct, int seed)
        {
            float s = 0, amp = 0.5f, tot = 0;
            for (int o = 0; o < oct; o++) { s += VNoise(u, v, cells << o, seed + o * 31) * amp; tot += amp; amp *= 0.5f; }
            return s / tot;
        }

        public static Material MakeMaterial(SurfaceKind kind)
        {
            var col = new Color32[N * N];
            var h = new float[N * N];
            float gloss, bump;
            float inv = 1f / N;
            switch (kind)
            {
                case SurfaceKind.WetConcrete: gloss = 0.78f; bump = 0.25f; break;
                case SurfaceKind.TroweledConcrete: gloss = 0.42f; bump = 0.35f; break;
                case SurfaceKind.BroomedConcrete: gloss = 0.12f; bump = 1.0f; break;
                default: gloss = 0.55f; bump = 0.5f; break; // poly
            }

            for (int y = 0; y < N; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) * inv;
                    int i = y * N + x;
                    float macro = Fbm(u, v, 3, 4, 11) - 0.5f;
                    float fine = H01(x, y, 7) - 0.5f;
                    Color c; float s = 1f, hh;
                    switch (kind)
                    {
                        case SurfaceKind.WetConcrete:
                        {
                            // dark paste with bleed-water sheen patches and sand grains at the surface
                            float sheen = Mathf.SmoothStep(0f, 1f, (Fbm(u, v, 4, 3, 21) - 0.45f) * 4f);
                            c = new Color(0.36f, 0.355f, 0.34f) * (1f + macro * 0.18f + fine * 0.06f);
                            c *= 1f - 0.08f * sheen;
                            s = 0.75f + 0.5f * sheen;
                            hh = macro * 0.2f + fine * 0.15f;
                            break;
                        }
                        case SurfaceKind.TroweledConcrete:
                        {
                            // hard-troweled: burnished light gray with darker trowel-swirl arcs and mottling
                            float swirl = 0f;
                            for (int k = 0; k < 3; k++)
                            {
                                float cx = (k * 0.37f + 0.1f), cy = (k * 0.53f + 0.3f);
                                float du = Mathf.Repeat(u - cx + 0.5f, 1f) - 0.5f, dv = Mathf.Repeat(v - cy + 0.5f, 1f) - 0.5f;
                                float r = Mathf.Sqrt(du * du + dv * dv);
                                swirl += Mathf.Pow(Mathf.Abs(Mathf.Sin(r * 60f + k)), 18f) * Mathf.Clamp01(1f - r * 2.2f);
                            }
                            float mott = Fbm(u, v, 6, 4, 31) - 0.5f;
                            c = new Color(0.56f, 0.555f, 0.535f) * (1f + macro * 0.14f + mott * 0.10f + fine * 0.025f - swirl * 0.07f);
                            s = 1f + mott * 0.5f + swirl * 0.4f;
                            hh = macro * 0.05f + fine * 0.03f;
                            break;
                        }
                        case SurfaceKind.BroomedConcrete:
                        {
                            // broom finish: fine parallel ridges along U (broom pulled across the slab), slightly wavy
                            float wv = v + (VNoise(u, v, 4, 41) - 0.5f) * 0.02f;
                            float line = VNoise2(u, wv, 5, 230, 43);          // varies across V (ridges run along U)
                            float streak = Mathf.Pow(line, 2.2f);
                            float mott = Fbm(u, v, 5, 3, 45) - 0.5f;
                            c = new Color(0.57f, 0.565f, 0.545f) * (1f + macro * 0.12f + mott * 0.08f - streak * 0.13f + fine * 0.05f);
                            hh = line * 0.9f + fine * 0.2f;
                            s = 0.8f + fine * 0.3f;
                            break;
                        }
                        default:
                        {
                            // 6 mil clear-ish poly: translucent milky sheet with wrinkles
                            float wr = Mathf.Abs(Fbm(u, v * 0.4f, 5, 4, 51) - 0.5f) * 2f;
                            c = new Color(0.60f, 0.64f, 0.67f) * (0.88f + 0.12f * wr + fine * 0.02f);
                            hh = (1f - wr) * 0.6f;
                            s = 1f;
                            break;
                        }
                    }
                    h[i] = hh;
                    col[i] = new Color32(B(c.r), B(c.g), B(c.b), B(s * 0.5f));
                }
            }

            var alb = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { name = "GB_" + kind + "_Albedo", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            alb.SetPixels32(col); alb.Apply(true, true);

            var np = new Color32[N * N];
            float k8 = 3f * 0.125f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int xm = (x - 1 + N) % N, xp = (x + 1) % N, ym = (y - 1 + N) % N, yp = (y + 1) % N;
                    float dx = h[y * N + xp] - h[y * N + xm], dy = h[yp * N + x] - h[ym * N + x];
                    var n = new Vector3(-dx * k8 * 4f, -dy * k8 * 4f, 1f).normalized;
                    np[y * N + x] = new Color32(B(n.x * 0.5f + 0.5f), B(n.y * 0.5f + 0.5f), B(n.z * 0.5f + 0.5f), 255);
                }
            var nor = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "GB_" + kind + "_Normal", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            nor.SetPixels32(np); nor.Apply(true, true);
            alb.hideFlags = nor.hideFlags = HideFlags.DontUnloadUnusedAsset;

            var m = new Material(Shader.Find("Standard")) { name = "GB_" + kind };
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            m.color = Color.white;
            m.mainTexture = alb;
            m.SetFloat("_Glossiness", gloss);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_SmoothnessTextureChannel", 1f);
            m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetFloat("_GlossMapScale", Mathf.Min(2f, gloss * 2f));
            m.SetTexture("_BumpMap", nor);
            m.SetFloat("_BumpScale", bump);
            m.EnableKeyword("_NORMALMAP");
            m.mainTextureScale = Vector2.one / Materials.TileMeters(kind);
            m.enableInstancing = true;
            return m;
        }

        static byte B(float v) => (byte)(v <= 0f ? 0 : v >= 1f ? 255 : (int)(v * 255f + 0.5f));
    }
}
