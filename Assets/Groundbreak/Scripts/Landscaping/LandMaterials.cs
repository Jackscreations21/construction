using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Landscaping surfaces. Kept separate from the shared SurfaceKind enum so this feature does not edit
    /// ProceduralTextures/Materials. UVs in meters, tiling = 1 / TileMeters.</summary>
    public enum LandSurface { Mulch, Topsoil, Sand, Paver, Straw, Bark, Foliage, Cedar, RetainingBlock, Tarp, Sod, Asphalt }

    public static class LandMaterials
    {
        static readonly Dictionary<LandSurface, Material> _cache = new Dictionary<LandSurface, Material>();
        static readonly Dictionary<(LandSurface, Color), Material> _tint = new Dictionary<(LandSurface, Color), Material>();

        public static float TileMeters(LandSurface k)
        {
            switch (k)
            {
                case LandSurface.Mulch: return 1.0f;
                case LandSurface.Topsoil: return 1.5f;
                case LandSurface.Sand: return 0.6f;
                case LandSurface.Paver: return 0.4f;
                case LandSurface.Straw: return 1.0f;
                case LandSurface.Bark: return 0.6f;
                case LandSurface.Foliage: return 0.7f;
                case LandSurface.Cedar: return 1.0f;
                case LandSurface.RetainingBlock: return 0.5f;
                case LandSurface.Tarp: return 0.5f;
                case LandSurface.Sod: return 1.6f;
                case LandSurface.Asphalt: return 1.5f;
            }
            return 1f;
        }

        static float Gloss(LandSurface k)
        {
            switch (k)
            {
                case LandSurface.Tarp: return 0.45f;
                case LandSurface.Foliage: return 0.3f;
                case LandSurface.Sod: return 0.22f;
                case LandSurface.Paver: return 0.15f;
                case LandSurface.Asphalt: return 0.2f;
                default: return 0.08f;
            }
        }

        public static Material Get(LandSurface k)
        {
            if (_cache.TryGetValue(k, out var m) && m != null) return m;
            if (k == LandSurface.Topsoil || k == LandSurface.Sod || k == LandSurface.Sand || k == LandSurface.Straw)
            {
                // broad ground covers: terrain shader (anti-tiling + large-scale patch variation)
                LandTextures.Generate(k, out var alb, out var nrm, out float bmp);
                m = SiteLook.MakeTerrain("GBL_" + k, alb, nrm, bmp, Gloss(k), 1f / TileMeters(k));
                m.SetFloat("_AlphaGloss", 0f); // these textures carry no smoothness in alpha
                if (k == LandSurface.Sod) SiteLook.Style(m, new Color(1.06f, 1.02f, 0.8f), new Color(0.82f, 0.94f, 0.82f), 0.4f, Color.white);
                else SiteLook.Style(m, new Color(1.05f, 1.0f, 0.94f), new Color(0.84f, 0.80f, 0.78f), 0.35f, Color.white);
                _cache[k] = m;
                return m;
            }
            var shader = Shader.Find("Standard");
            m = new Material(shader) { name = "GBL_" + k, hideFlags = HideFlags.DontUnloadUnusedAsset };
            LandTextures.Generate(k, out var albedo, out var normal, out float bump);
            m.mainTexture = albedo;
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", bump);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_Glossiness", Gloss(k));
            m.SetFloat("_Metallic", 0f);
            m.mainTextureScale = Vector2.one / TileMeters(k);
            m.enableInstancing = true;
            _cache[k] = m;
            return m;
        }

        public static Material Get(LandSurface k, Color tint)
        {
            if (_tint.TryGetValue((k, tint), out var m) && m != null) return m;
            m = new Material(Get(k)) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.color = tint;
            _tint[(k, tint)] = m;
            return m;
        }

        /// <summary>Copy of a material whose texture tiling is stretched for a mesh with UVs NOT in meters (e.g. ground plane).</summary>
        public static Material WithTiling(Material src, float tiles)
        {
            var m = new Material(src) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.mainTextureScale = Vector2.one * tiles;
            return m;
        }
    }

    /// <summary>Seamless procedural textures for <see cref="LandSurface"/> (periodic noise, wrapped stamping).</summary>
    public static class LandTextures
    {
        const int N = 256;

        // ------------------------------------------------------------------ noise

        static uint Hash(int x, int y, int seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
        static float H01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777215f;

        /// <summary>Periodic value noise; p = cells across the texture.</summary>
        static float Value(float x, float y, int p, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int xa = ((x0 % p) + p) % p, xb = (xa + 1) % p, ya = ((y0 % p) + p) % p, yb = (ya + 1) % p;
            float a = H01(xa, ya, seed), b = H01(xb, ya, seed), c = H01(xa, yb, seed), d = H01(xb, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Periodic fBm on u,v in 0..1.</summary>
        static float Fbm(float u, float v, int baseCells, int octaves, int seed, float gain = 0.5f)
        {
            float sum = 0, amp = 1, norm = 0; int cells = baseCells;
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(u * cells, v * cells, cells, seed + o * 101) * amp;
                norm += amp; amp *= gain; cells *= 2;
            }
            return sum / norm;
        }

        /// <summary>Periodic Worley: F1, F2 distances (in cell units) and id of nearest cell.</summary>
        static void Worley(float u, float v, int cells, int seed, out float f1, out float f2, out uint id)
        {
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            f1 = 9; f2 = 9; id = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j;
                    int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                    float px = gx + H01(wx, wy, seed), py = gy + H01(wx, wy, seed + 7);
                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < f1) { f2 = f1; f1 = d; id = Hash(wx, wy, seed + 3); }
                    else if (d < f2) f2 = d;
                }
        }

        static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        // ------------------------------------------------------------------ canvas

        sealed class Cv
        {
            public readonly Color[] Col = new Color[N * N];
            public readonly float[] H = new float[N * N];
            public void Stamp(float cu, float cv, float len, float wid, float ang, Color c, float hgt, float alpha = 1f)
            {
                // oriented soft rectangle (strand / blade / leaf), wrapped
                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                float ext = Mathf.Max(len, wid) * 0.5f + 1;
                int x0 = Mathf.FloorToInt((cu - ext / N) * N), x1 = Mathf.CeilToInt((cu + ext / N) * N);
                int y0 = Mathf.FloorToInt((cv - ext / N) * N), y1 = Mathf.CeilToInt((cv + ext / N) * N);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x - cu * N, dy = y - cv * N;
                        float a = dx * ca + dy * sa, b = -dx * sa + dy * ca;
                        float ta = Mathf.Abs(a) / (len * 0.5f), tb = Mathf.Abs(b) / (wid * 0.5f);
                        if (ta > 1 || tb > 1) continue;
                        float w = (1 - ta * ta) * (1 - tb * tb) * alpha;
                        int i = (((y % N) + N) % N) * N + (((x % N) + N) % N);
                        Col[i] = Color.Lerp(Col[i], c, Mathf.Clamp01(w * 1.6f));
                        H[i] = Mathf.Max(H[i], hgt * (1 - tb * tb) + H[i] * 0.2f);
                    }
            }
        }

        delegate void Pixel(float u, float v, out Color c, out float h);

        static Cv Fill(Pixel f)
        {
            var cv = new Cv();
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    f((x + 0.5f) / N, (y + 0.5f) / N, out var c, out var h);
                    cv.Col[y * N + x] = c; cv.H[y * N + x] = h;
                }
            return cv;
        }

        // ------------------------------------------------------------------ generators

        public static void Generate(LandSurface k, out Texture2D albedo, out Texture2D normal, out float bump)
        {
            Cv c; float strength;
            var rng = new System.Random(1000 + (int)k * 77);
            float R() => (float)rng.NextDouble();
            switch (k)
            {
                case LandSurface.Topsoil:
                    c = Fill((float u, float v, out Color col, out float h) =>
                    {
                        float n = Fbm(u, v, 6, 5, 11);
                        Worley(u, v, 40, 12, out float f1, out float f2, out _);
                        float clod = Mathf.Clamp01((f2 - f1) * 3f);
                        h = n * 0.6f + clod * 0.4f;
                        col = Color.Lerp(C(52, 38, 27), C(84, 64, 45), n) * (0.8f + 0.25f * clod);
                        col.a = 1;
                    });
                    for (int i = 0; i < 160; i++) c.Stamp(R(), R(), 2 + R() * 3, 2 + R() * 2, R() * 6.28f, C(120, 108, 92), 1.2f);
                    strength = 4f; break;

                case LandSurface.Mulch:
                    c = Fill((float u, float v, out Color col, out float h) => { col = C(48, 28, 18); h = 0; });
                    for (int i = 0; i < 2600; i++)
                    {
                        float t = R();
                        Color s = t < 0.45f ? C(98, 52, 30) : t < 0.8f ? C(122, 70, 40) : t < 0.95f ? C(70, 40, 24) : C(150, 104, 66);
                        s *= 0.85f + R() * 0.3f; s.a = 1;
                        c.Stamp(R(), R(), 10 + R() * 22, 2.5f + R() * 3, R() * 6.28f, s, 0.5f + R() * 0.8f);
                    }
                    strength = 5f; break;

                case LandSurface.Sand:
                    c = Fill((float u, float v, out Color col, out float h) =>
                    {
                        float n = Fbm(u, v, 8, 3, 21), g = H01((int)(u * N), (int)(v * N), 22);
                        h = n * 0.4f + g * 0.3f;
                        col = Color.Lerp(C(176, 150, 112), C(204, 182, 142), n * 0.6f + g * 0.4f);
                        if (g > 0.97f) col = C(110, 96, 84);
                    });
                    strength = 2f; break;

                case LandSurface.Paver:
                    c = Fill((float u, float v, out Color col, out float h) =>
                    {
                        float n = Fbm(u, v, 4, 4, 31), g = H01((int)(u * N), (int)(v * N), 32);
                        Worley(u, v, 24, 33, out float f1, out _, out uint id);
                        float agg = f1 < 0.28f ? 1 : 0;
                        h = n * 0.3f + g * 0.2f - agg * 0.15f;
                        col = Color.Lerp(C(158, 150, 138), C(186, 178, 166), n);
                        if (agg > 0) col = Color.Lerp(col, (id & 1) == 0 ? C(120, 112, 104) : C(208, 200, 188), 0.5f);
                        col *= 0.92f + g * 0.12f; col.a = 1;
                    });
                    strength = 2f; break;

                case LandSurface.Straw:
                    c = Fill((float u, float v, out Color col, out float h) =>
                    {
                        float n = Fbm(u, v, 6, 4, 41);
                        col = Color.Lerp(C(52, 38, 27), C(78, 60, 42), n); h = n * 0.2f;
                    });
                    for (int i = 0; i < 1500; i++)
                    {
                        Color s = Color.Lerp(C(196, 166, 92), C(232, 208, 140), R()) * (0.8f + R() * 0.25f); s.a = 1;
                        c.Stamp(R(), R(), 30 + R() * 50, 1.6f + R() * 1.2f, R() * 6.28f, s, 0.6f + R() * 0.6f);
                    }
                    strength = 4f; break;

                case LandSurface.Bark:
                    c = Fill((float u, float v, out Color col, out float h) =>
                    {
                        Worley(u, v * 0.25f + Fbm(u, v, 4, 2, 52) * 0.05f, 10, 51, out float f1, out float f2, out _);
                        float ridge = Mathf.Clamp01((f2 - f1) * 2.2f);
                        float n = Fbm(u, v, 8, 4, 53);
                        h = ridge * 0.8f + n * 0.3f;
                        col = Color.Lerp(C(40, 32, 26), C(104, 88, 72), ridge * 0.7f + n * 0.3f);
                    });
                    strength = 7f; break;

                case LandSurface.Foliage:
                    c = Fill((float u, float v, out Color col, out float h) => { col = C(18, 30, 12); h = 0; });
                    for (int i = 0; i < 1900; i++)
                    {
                        float t = R();
                        Color s = Color.Lerp(C(42, 78, 26), C(96, 138, 50), t) * (0.75f + R() * 0.4f); s.a = 1;
                        c.Stamp(R(), R(), 9 + R() * 7, 4 + R() * 3, R() * 6.28f, s, 0.4f + R() * 0.7f);
                    }
                    strength = 5f; break;

                case LandSurface.Cedar:
                    c = Fill((float u, float v, out Color col, out float h) =>
                    {
                        float warp = Fbm(u, v, 3, 3, 61) * 0.6f;
                        float grain = Mathf.Sin((u * 38f + warp * 9f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                        float fine = Value(u * 160, v * 4, 160, 62);
                        float n = Fbm(u, v, 5, 3, 63);
                        h = grain * 0.3f + fine * 0.3f;
                        col = Color.Lerp(C(150, 92, 58), C(192, 128, 84), n * 0.6f + fine * 0.2f) * (0.85f + grain * 0.15f);
                        col.a = 1;
                    });
                    strength = 2.5f; break;

                case LandSurface.RetainingBlock:
                    c = Fill((float u, float v, out Color col, out float h) =>
                    {
                        Worley(u, v, 9, 71, out float f1, out float f2, out uint id);
                        float facet = f1 * 0.6f + ((id & 255) / 255f) * 0.4f;
                        float n = Fbm(u, v, 12, 4, 72), g = H01((int)(u * N), (int)(v * N), 73);
                        h = -facet + n * 0.4f;
                        col = Color.Lerp(C(150, 138, 120), C(186, 174, 154), n * 0.7f + (id & 15) / 15f * 0.3f) * (0.9f + g * 0.12f);
                        if (f2 - f1 < 0.05f) col *= 0.8f;
                        col.a = 1;
                    });
                    strength = 5f; break;

                case LandSurface.Tarp:
                    c = Fill((float u, float v, out Color col, out float h) =>
                    {
                        float wx = Mathf.Sin(u * N * 0.5f) * 0.5f + 0.5f, wy = Mathf.Sin(v * N * 0.5f) * 0.5f + 0.5f;
                        float n = Fbm(u, v, 3, 3, 81);
                        h = (wx + wy) * 0.15f + n * 0.5f;
                        col = Color.Lerp(C(22, 72, 160), C(40, 98, 190), n) * (0.92f + 0.08f * wx * wy);
                        col.a = 1;
                    });
                    strength = 2f; break;

                case LandSurface.Sod:
                    c = Fill((float u, float v, out Color col, out float h) =>
                    {
                        float n = Fbm(u, v, 5, 4, 91);
                        col = Color.Lerp(C(28, 54, 19), C(50, 82, 30), n); h = 0;
                    });
                    for (int i = 0; i < 5200; i++)
                    {
                        Color s = Color.Lerp(C(46, 88, 29), C(88, 128, 50), R()) * (0.75f + R() * 0.3f); s.a = 1; // matched to the lawn
                        c.Stamp(R(), R(), 8 + R() * 8, 1.4f + R(), R() * 6.28f, s, 0.5f + R() * 0.6f);
                    }
                    strength = 4f; break;

                default: // Asphalt
                    c = Fill((float u, float v, out Color col, out float h) =>
                    {
                        float n = Fbm(u, v, 6, 3, 101), g = H01((int)(u * N), (int)(v * N), 102);
                        h = g * 0.5f;
                        col = Color.Lerp(C(38, 38, 40), C(62, 61, 60), n) * (0.85f + g * 0.3f);
                        col.a = 1;
                    });
                    strength = 3f; break;
            }

            albedo = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { name = "GBL_" + k + "_Albedo", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            for (int i = 0; i < c.Col.Length; i++) c.Col[i].a = 1f;
            albedo.SetPixels(c.Col); albedo.Apply(true, false);

            var px = new Color[N * N];
            float kk = strength * 0.125f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int xm = (x + N - 1) % N, xp = (x + 1) % N, ym = (y + N - 1) % N, yp = (y + 1) % N;
                    float dx = c.H[y * N + xp] - c.H[y * N + xm], dy = c.H[yp * N + x] - c.H[ym * N + x];
                    var n = new Vector3(-dx * kk * 4f, -dy * kk * 4f, 1f).normalized;
                    px[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            normal = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "GBL_" + k + "_Normal", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            normal.SetPixels(px); normal.Apply(true, false);
            bump = 1f;
        }
    }
}
