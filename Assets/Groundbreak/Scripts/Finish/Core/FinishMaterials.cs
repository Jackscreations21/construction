using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>Float working buffers for a procedural texture. Colors are sRGB 0..1, S = relative smoothness
    /// (1 = material nominal gloss), H = height (for the normal map), A = alpha (only used by transparent/cutout specs).</summary>
    public sealed class FinishCanvas
    {
        public readonly int N;
        public readonly float[] R, G, B, S, H, A;
        public FinishCanvas(int n)
        {
            N = n; int len = n * n;
            R = new float[len]; G = new float[len]; B = new float[len]; S = new float[len]; H = new float[len]; A = new float[len];
            for (int i = 0; i < len; i++) { S[i] = 1f; A[i] = 1f; }
        }
        public void Set(int i, Color c) { R[i] = c.r; G[i] = c.g; B[i] = c.b; }
        public Color Get(int i) => new Color(R[i], G[i], B[i], A[i]);
        public void Mul(int i, float k) { R[i] *= k; G[i] *= k; B[i] *= k; }
        public void Blend(int i, Color c, float t) { R[i] += (c.r - R[i]) * t; G[i] += (c.g - G[i]) * t; B[i] += (c.b - B[i]) * t; }
    }

    /// <summary>Periodic (seamlessly tiling) noise toolkit for finish textures. u,v in 0..1 wrap.</summary>
    public static class FinishNoise
    {
        public static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)seed * 0xcb1ab31fu;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return h;
            }
        }
        /// <summary>Hash to 0..1.</summary>
        public static float Hf(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;
        static int Wrap(int i, int m) { i %= m; return i < 0 ? i + m : i; }
        static float SmoothT(float t) => t * t * (3f - 2f * t);
        public static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>Value noise periodic over a px × py lattice; x in [0,px), y in [0,py).</summary>
        public static float VNoise(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = SmoothT(x - x0), fy = SmoothT(y - y0);
            int ax = Wrap(x0, px), bx = Wrap(x0 + 1, px), ay = Wrap(y0, py), by = Wrap(y0 + 1, py);
            float a = Hf(ax, ay, seed), b = Hf(bx, ay, seed), c = Hf(ax, by, seed), d = Hf(bx, by, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Fractal field n×n (0..1, ~0.5 mean), base lattice px × py, octaves doubling.</summary>
        public static float[] Field(int n, int px, int py, int octaves, float gain, int seed)
        {
            var f = new float[n * n];
            float amp = 1f, total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                int lx = px << o, ly = py << o;
                for (int y = 0; y < n; y++)
                {
                    float fy = (y + 0.5f) / n * ly;
                    for (int x = 0; x < n; x++)
                        f[y * n + x] += amp * VNoise((x + 0.5f) / n * lx, fy, lx, ly, seed + o * 101);
                }
                total += amp; amp *= gain;
            }
            for (int i = 0; i < f.Length; i++) f[i] /= total;
            return f;
        }

        /// <summary>Bilinear sample of an m×m field at wrapped u,v (0..1).</summary>
        public static float Sample(float[] f, int m, float u, float v)
        {
            float x = u * m - 0.5f, y = v * m - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            int ax = Wrap(x0, m), bx = Wrap(x0 + 1, m), ay = Wrap(y0, m), by = Wrap(y0 + 1, m);
            return Mathf.Lerp(Mathf.Lerp(f[ay * m + ax], f[ay * m + bx], fx), Mathf.Lerp(f[by * m + ax], f[by * m + bx], fx), fy);
        }

        /// <summary>Wrapped box blur (radius in px).</summary>
        public static float[] Blur(float[] src, int n, int r)
        {
            if (r <= 0) return (float[])src.Clone();
            var tmp = new float[n * n]; var dst = new float[n * n];
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0; for (int k = -r; k <= r; k++) s += src[y * n + Wrap(x + k, n)];
                    tmp[y * n + x] = s * inv;
                }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0; for (int k = -r; k <= r; k++) s += tmp[Wrap(y + k, n) * n + x];
                    dst[y * n + x] = s * inv;
                }
            return dst;
        }

        /// <summary>Wrapped Worley (cellular) on a g×g grid: f1, f2 distances (in cell units) and the nearest cell id.</summary>
        public static void Worley(float u, float v, int g, int seed, out float f1, out float f2, out int id)
        {
            float x = u * g, y = v * g; int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            f1 = f2 = 9f; id = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = cx + dx, gy = cy + dy, wx = Wrap(gx, g), wy = Wrap(gy, g);
                    float px = gx + Hf(wx, wy, seed), py = gy + Hf(wx, wy, seed + 7);
                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < f1) { f2 = f1; f1 = d; id = wy * g + wx; } else if (d < f2) f2 = d;
                }
        }

        public static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);
    }

    /// <summary>
    /// Material registry for close-up/finish trades, keyed by string so every feature folder can add its own looks
    /// without touching the shared SurfaceKind enum. Textures are generated lazily from a <see cref="FinishCanvas"/>
    /// generator and tile in world meters (UVs from MeshBuilder are meters; tiling = 1 / Tile).
    /// Usage: FinishMaterials.Register("drywall-face", new FinishMaterials.Spec { Gen = MyGen, Tile = 1.2f, Gloss = 0.1f });
    ///        renderer.sharedMaterial = FinishMaterials.Get("drywall-face");
    /// </summary>
    public static class FinishMaterials
    {
        public enum Blend { Opaque, Cutout, Fade, Transparent }

        public class Spec
        {
            public Func<FinishCanvas> Gen;       // texture generator (seamless). null = flat color only
            public float Tile = 1f;              // meters per texture tile
            public float Gloss = 0.2f, Metal = 0f, Bump = 0.6f;
            public float NormalStrength = 2f;    // pixels of slope per unit height in the Sobel bake
            public Blend Mode = Blend.Opaque;
            public Color Tint = Color.white;     // main color multiplier
            public bool DoubleSided;             // render both faces (thin films like poly / house wrap): uses Cull Off via the Standard "_Cull" isn't available, so builders should add back faces
        }

        static readonly Dictionary<string, Spec> _specs = new Dictionary<string, Spec>();
        static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, (Texture2D a, Texture2D n)> _tex = new Dictionary<string, (Texture2D, Texture2D)>();

        public static void Register(string key, Spec spec) { _specs[key] = spec; _mats.Remove(key); }
        public static bool Has(string key) => _specs.ContainsKey(key);
        public static float Tile(string key) => _specs.TryGetValue(key, out var s) ? s.Tile : 1f;

        public static Texture2D Albedo(string key) { Textures(key, out var a, out _); return a; }
        public static Texture2D Normal(string key) { Textures(key, out _, out var n); return n; }

        public static void Textures(string key, out Texture2D albedo, out Texture2D normal)
        {
            albedo = normal = null;
            if (_tex.TryGetValue(key, out var t) && t.a != null) { albedo = t.a; normal = t.n; return; }
            if (!_specs.TryGetValue(key, out var s) || s.Gen == null) return;
            var c = s.Gen();
            albedo = BakeAlbedo(c, "FIN_" + key + "_A", s.Mode != Blend.Opaque);
            normal = BakeNormal(c, s.NormalStrength, "FIN_" + key + "_N");
            _tex[key] = (albedo, normal);
        }

        public static Material Get(string key)
        {
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            if (!_specs.TryGetValue(key, out var s)) { Debug.LogWarning("FinishMaterials: unknown key " + key); s = new Spec(); }
            m = Build(key, s);
            _mats[key] = m;
            return m;
        }

        /// <summary>Tinted copy (cached per key+color), e.g. vinyl siding colors or paint over a texture.</summary>
        public static Material Get(string key, Color tint)
        {
            string k = key + "#" + ColorUtility.ToHtmlStringRGBA(tint);
            if (_mats.TryGetValue(k, out var m) && m != null) return m;
            var b = Get(key);
            m = new Material(b) { name = b.name + "_" + ColorUtility.ToHtmlStringRGB(tint), hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.color = b.color * tint;
            _mats[k] = m;
            return m;
        }

        static Material Build(string key, Spec s)
        {
            var m = new Material(Shader.Find("Standard")) { name = "FIN_" + key, hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.SetFloat("_Glossiness", s.Gloss);
            m.SetFloat("_Metallic", s.Metal);
            m.color = s.Tint;
            Textures(key, out var a, out var n);
            if (a != null)
            {
                m.mainTexture = a;
                if (s.Mode == Blend.Opaque)
                {
                    m.SetFloat("_SmoothnessTextureChannel", 1f);
                    m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                    m.SetFloat("_GlossMapScale", Mathf.Min(2f, s.Gloss * 2f));
                }
            }
            if (n != null)
            {
                m.SetTexture("_BumpMap", n);
                m.SetFloat("_BumpScale", s.Bump);
                m.EnableKeyword("_NORMALMAP");
            }
            m.mainTextureScale = Vector2.one / Mathf.Max(0.001f, s.Tile);
            SetMode(m, s.Mode);
            if (s.Mode == Blend.Opaque) m.enableInstancing = true;
            return m;
        }

        /// <summary>Switches a Standard material between Opaque / Cutout / Fade / Transparent.</summary>
        public static void SetMode(Material m, Blend mode)
        {
            switch (mode)
            {
                case Blend.Opaque:
                    m.SetFloat("_Mode", 0); m.SetOverrideTag("RenderType", "");
                    m.SetInt("_SrcBlend", (int)BlendMode.One); m.SetInt("_DstBlend", (int)BlendMode.Zero); m.SetInt("_ZWrite", 1);
                    m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    m.renderQueue = -1; break;
                case Blend.Cutout:
                    m.SetFloat("_Mode", 1); m.SetOverrideTag("RenderType", "TransparentCutout");
                    m.SetInt("_SrcBlend", (int)BlendMode.One); m.SetInt("_DstBlend", (int)BlendMode.Zero); m.SetInt("_ZWrite", 1);
                    m.EnableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    m.SetFloat("_Cutoff", 0.5f);
                    m.renderQueue = (int)RenderQueue.AlphaTest; break;
                case Blend.Fade:
                    m.SetFloat("_Mode", 2); m.SetOverrideTag("RenderType", "Transparent");
                    m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
                    m.DisableKeyword("_ALPHATEST_ON"); m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    m.renderQueue = (int)RenderQueue.Transparent; break;
                case Blend.Transparent:
                    m.SetFloat("_Mode", 3); m.SetOverrideTag("RenderType", "Transparent");
                    m.SetInt("_SrcBlend", (int)BlendMode.One); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
                    m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    m.renderQueue = (int)RenderQueue.Transparent; break;
            }
        }

        static byte B8(float v) => (byte)(v <= 0f ? 0 : v >= 1f ? 255 : (int)(v * 255f + 0.5f));

        static Texture2D BakeAlbedo(FinishCanvas c, string name, bool alphaIsAlpha)
        {
            int len = c.N * c.N; var px = new Color32[len];
            for (int i = 0; i < len; i++)
                px[i] = new Color32(B8(c.R[i]), B8(c.G[i]), B8(c.B[i]), B8(alphaIsAlpha ? c.A[i] : c.S[i] * 0.5f));
            return Upload(px, c.N, false, name);
        }

        static Texture2D BakeNormal(FinishCanvas c, float strength, string name)
        {
            int n = c.N; var h = c.H; var px = new Color32[n * n];
            float k = strength * 0.125f;
            for (int y = 0; y < n; y++)
            {
                int ym = ((y - 1 + n) % n) * n, y0 = y * n, yp = ((y + 1) % n) * n;
                for (int x = 0; x < n; x++)
                {
                    int xm = (x - 1 + n) % n, xp = (x + 1) % n;
                    float dx = (h[ym + xp] + 2f * h[y0 + xp] + h[yp + xp]) - (h[ym + xm] + 2f * h[y0 + xm] + h[yp + xm]);
                    float dy = (h[yp + xm] + 2f * h[yp + x] + h[yp + xp]) - (h[ym + xm] + 2f * h[ym + x] + h[ym + xp]);
                    float nx = -dx * k, ny = -dy * k, inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + 1f);
                    px[y0 + x] = new Color32(B8(nx * inv * 0.5f + 0.5f), B8(ny * inv * 0.5f + 0.5f), B8(inv * 0.5f + 0.5f), 255);
                }
            }
            return Upload(px, n, true, name);
        }

        public static Texture2D Upload(Color32[] px, int n, bool linear, string name)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, linear)
            { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, hideFlags = HideFlags.DontUnloadUnusedAsset };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        // ------------------------------------------------------------- shared looks used by several trades

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterCommon()
        {
            // Smooth latex paint / caulk / painted aluminum: very fine orange peel.
            Register("paint-smooth", new Spec { Gen = () => FineSurface(FinishNoise.C(236, 235, 230), 3101, 0.015f, 96), Tile = 0.5f, Gloss = 0.25f, Bump = 0.2f, NormalStrength = 1f });
            Register("caulk-white", new Spec { Gen = () => FineSurface(FinishNoise.C(240, 240, 236), 3111, 0.02f, 64), Tile = 0.2f, Gloss = 0.55f, Bump = 0.3f });
            Register("aluminum-white", new Spec { Gen = () => FineSurface(FinishNoise.C(238, 238, 234), 3121, 0.012f, 128), Tile = 0.5f, Gloss = 0.6f, Bump = 0.15f });
        }

        /// <summary>Generic fine surface: subtle macro drift + orange peel (512 px).</summary>
        public static FinishCanvas FineSurface(Color baseC, int seed, float amt, int bumpPeriod)
        {
            const int n = 512; var c = new FinishCanvas(n);
            var macro = FinishNoise.Field(128, 3, 3, 3, 0.5f, seed);
            var bump = FinishNoise.Field(n, bumpPeriod, bumpPeriod, 2, 0.45f, seed + 1);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float m = FinishNoise.Sample(macro, 128, (x + 0.5f) / n, (y + 0.5f) / n) - 0.5f, b = bump[i] - 0.5f;
                    c.Set(i, baseC * (1f + m * amt * 2f + b * amt));
                    c.H[i] = b; c.S[i] = 1f - Mathf.Abs(b) * 0.1f;
                }
            return c;
        }
    }
}
