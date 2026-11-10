using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Runtime procedural surface textures. Every generator works on a periodic domain
    /// (wrapped lattice noise, wrapped Worley cells, wrapped stamping), so all textures tile seamlessly.
    /// Each kind yields an sRGB albedo (alpha = relative smoothness * 0.5, see Materials) and a
    /// linear tangent-space normal map baked from a height field with a wrapped Sobel filter.
    /// Grain / strands / brushing / rebar ribs run along U (texture X).
    /// Generation is lazy (first request per kind) and cached for the session.
    /// </summary>
    public static class ProceduralTextures
    {
        public struct TextureSet
        {
            public Texture2D Albedo;   // null for Glass
            public Texture2D Normal;   // null for Glass
        }

        static readonly Dictionary<SurfaceKind, TextureSet> _cache = new Dictionary<SurfaceKind, TextureSet>();

        /// <summary>Total wall-clock milliseconds spent generating textures so far (diagnostics).</summary>
        public static double TotalMilliseconds { get; private set; }

        public static Texture2D Albedo(SurfaceKind kind) => Get(kind).Albedo;
        public static Texture2D Normal(SurfaceKind kind) => Get(kind).Normal;

        public static TextureSet Get(SurfaceKind kind)
        {
            if (_cache.TryGetValue(kind, out var s) && (kind == SurfaceKind.Glass || (s.Albedo != null && s.Normal != null)))
                return s;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            s = Generate(kind);
            sw.Stop();
            TotalMilliseconds += sw.Elapsed.TotalMilliseconds;
            _cache[kind] = s;
            return s;
        }

        // =====================================================================================
        //  Dispatch
        // =====================================================================================

        static TextureSet Generate(SurfaceKind k)
        {
            Canvas c; float ns; // normal strength: how many pixels tall a height of 1.0 is
            switch (k)
            {
                case SurfaceKind.Glass: return new TextureSet();
                case SurfaceKind.Osb: c = Osb(); ns = 2.2f; break;
                case SurfaceKind.PineLumber: c = Wood(PineSpec()); ns = 2.0f; break;
                case SurfaceKind.TreatedLumber: c = Treated(); ns = 2.5f; break;
                case SurfaceKind.Plywood: c = Plywood(); ns = 1.8f; break;
                case SurfaceKind.Oak: c = Wood(OakSpec()); ns = 1.8f; break;
                case SurfaceKind.Walnut: c = Wood(WalnutSpec()); ns = 1.8f; break;
                case SurfaceKind.PineEndGrain: c = EndGrain(); ns = 2.5f; break;
                case SurfaceKind.Grass: c = Grass(); ns = 3.0f; break;
                case SurfaceKind.Dirt: c = Dirt(); ns = 3.0f; break;
                case SurfaceKind.Gravel: c = Gravel(); ns = 4.0f; break;
                case SurfaceKind.Concrete: c = Concrete(C(170, 168, 162), 601, 70, 0.25f, 0.10f); ns = 1.5f; break;
                case SurfaceKind.ConcreteBlock: c = Concrete(C(158, 156, 150), 1601, 46, 0.55f, 0.18f); ns = 3.0f; break;
                case SurfaceKind.Mortar: c = Mortar(); ns = 2.0f; break;
                case SurfaceKind.Brick: c = Brick(false); ns = 2.5f; break;
                case SurfaceKind.UsedBrick: c = Brick(true); ns = 3.0f; break;
                case SurfaceKind.Rebar: c = Rebar(); ns = 6.0f; break;
                case SurfaceKind.PaperBag: c = PaperBag(); ns = 2.0f; break;
                case SurfaceKind.PaintedWhite: c = Surface(C(214, 212, 205), 701, 0.025f, 96, 0.006f, 0.05f, 0.03f); ns = 1.0f; break;
                case SurfaceKind.PaintedSteel: c = Surface(C(212, 212, 208), 711, 0.02f, 80, 0.006f, 0.05f, 0.03f); ns = 1.0f; break;
                case SurfaceKind.TruckPaint: c = Surface(C(158, 20, 18), 721, 0.03f, 72, 0.004f, 0.04f, 0.0f); ns = 0.6f; break;
                case SurfaceKind.Porcelain: c = Surface(C(242, 241, 236), 731, 0.012f, 40, 0.004f, 0.03f, 0.0f); ns = 0.4f; break;
                case SurfaceKind.DarkPlastic: c = Surface(C(34, 34, 36), 741, 0.05f, 180, 0.04f, 0.15f, 0.2f); ns = 0.8f; break;
                case SurfaceKind.MatteBlack: c = Surface(C(30, 30, 31), 751, 0.04f, 150, 0.03f, 0.15f, 0.25f); ns = 1.2f; break;
                case SurfaceKind.Tire: c = Surface(C(30, 30, 32), 761, 0.06f, 128, 0.05f, 0.2f, 0.4f); ns = 1.5f; break;
                case SurfaceKind.Brass: c = Surface(C(225, 190, 112), 771, 0.03f, 64, 0.01f, 0.05f, 0.0f); ns = 0.4f; break;
                case SurfaceKind.BrushedSteel: c = Brushed(C(196, 198, 201), 801, 192, 0.07f); ns = 0.8f; break;
                case SurfaceKind.Nickel: c = Brushed(C(206, 204, 198), 811, 128, 0.05f); ns = 0.6f; break;
                case SurfaceKind.Galvanized: c = Galvanized(); ns = 0.6f; break;
                default: c = Surface(C(200, 200, 200), 999, 0.03f, 64, 0.01f, 0.05f, 0.0f); ns = 1f; break;
            }
            string name = "GB_" + k;
            return new TextureSet { Albedo = MakeAlbedo(c, name + "_Albedo"), Normal = MakeNormal(c.H, c.N, ns, name + "_Normal") };
        }

        // =====================================================================================
        //  Canvas + baking
        // =====================================================================================

        /// <summary>Float working buffers. Colors are sRGB 0..1, S = relative smoothness (1 = material nominal), H = height.</summary>
        sealed class Canvas
        {
            public readonly int N;
            public readonly float[] R, G, B, S, H;
            public Canvas(int n)
            {
                N = n; int len = n * n;
                R = new float[len]; G = new float[len]; B = new float[len]; S = new float[len]; H = new float[len];
                for (int i = 0; i < len; i++) S[i] = 1f;
            }
            public void Set(int i, Color c) { R[i] = c.r; G[i] = c.g; B[i] = c.b; }
            public void Mul(int i, float k) { R[i] *= k; G[i] *= k; B[i] *= k; }
            public void Blend(int i, Color c, float t)
            {
                R[i] += (c.r - R[i]) * t; G[i] += (c.g - G[i]) * t; B[i] += (c.b - B[i]) * t;
            }
        }

        static Texture2D MakeAlbedo(Canvas c, string name)
        {
            int len = c.N * c.N;
            var px = new Color32[len];
            for (int i = 0; i < len; i++)
                px[i] = new Color32(B8(c.R[i]), B8(c.G[i]), B8(c.B[i]), B8(c.S[i] * 0.5f));
            return Upload(px, c.N, false, name);
        }

        /// <summary>Wrapped Sobel on the height field. Packed as (x,y,z,1)*0.5+0.5 in a LINEAR RGBA32 texture:
        /// Standard's UnpackScaleNormalRGorAG does x *= a (a = 1 -> x stays), reads .xy, rebuilds z — works
        /// whether or not the platform uses DXT5nm decoding.</summary>
        static Texture2D MakeNormal(float[] h, int n, float strength, string name)
        {
            var px = new Color32[n * n];
            float k = strength * 0.125f;
            for (int y = 0; y < n; y++)
            {
                int ym = ((y - 1 + n) % n) * n, y0 = y * n, yp = ((y + 1) % n) * n;
                for (int x = 0; x < n; x++)
                {
                    int xm = (x - 1 + n) % n, xp = (x + 1) % n;
                    float dx = (h[ym + xp] + 2f * h[y0 + xp] + h[yp + xp]) - (h[ym + xm] + 2f * h[y0 + xm] + h[yp + xm]);
                    float dy = (h[yp + xm] + 2f * h[yp + x] + h[yp + xp]) - (h[ym + xm] + 2f * h[ym + x] + h[ym + xp]);
                    float nx = -dx * k, ny = -dy * k, nz = 1f;
                    float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                    nx *= inv; ny *= inv; nz *= inv;
                    px[y0 + x] = new Color32(B8(nx * 0.5f + 0.5f), B8(ny * 0.5f + 0.5f), B8(nz * 0.5f + 0.5f), 255);
                }
            }
            return Upload(px, n, true, name);
        }

        static Texture2D Upload(Color32[] px, int n, bool linear, string name)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, linear);
            t.name = name;
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 8;
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            t.SetPixels32(px);
            t.Apply(true, true); // build mips on CPU, then free the CPU copy
            return t;
        }

        static byte B8(float v) => (byte)(v <= 0f ? 0 : v >= 1f ? 255 : (int)(v * 255f + 0.5f));
        static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

        // =====================================================================================
        //  Noise toolkit (all periodic)
        // =====================================================================================

        static float[] _tbl;
        static void EnsureTables()
        {
            if (_tbl != null) return;
            var r = new System.Random(90210);
            _tbl = new float[65536];
            for (int i = 0; i < _tbl.Length; i++) _tbl[i] = (float)r.NextDouble();
        }

        static int FloorI(float x) { int i = (int)x; return x < i ? i - 1 : i; }
        static int Wrap(int i, int m) { i %= m; return i < 0 ? i + m : i; }
        static float Smooth(float a, float b, float x)
        {
            float t = (x - a) / (b - a);
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }
        static float Frac(float x) => x - FloorI(x);

        static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u + 0x9E3779B9u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                return h ^ (h >> 13);
            }
        }
        /// <summary>Hash to [0,1).</summary>
        static float Hf(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;

        /// <summary>Lattice value with period (px,py); lattice coords wrapped before lookup, so noise tiles exactly.</summary>
        static float Lat(int x, int y, int px, int py, int seed)
        {
            x %= px; if (x < 0) x += px;
            y %= py; if (y < 0) y += py;
            return _tbl[(((y + seed * 37) & 255) << 8) | ((x + seed * 101) & 255)];
        }

        static float VNoise(float x, float y, int px, int py, int seed)
        {
            int xi = FloorI(x), yi = FloorI(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Lat(xi, yi, px, py, seed), b = Lat(xi + 1, yi, px, py, seed);
            float c = Lat(xi, yi + 1, px, py, seed), d = Lat(xi + 1, yi + 1, px, py, seed);
            float ab = a + (b - a) * fx, cd = c + (d - c) * fx;
            return ab + (cd - ab) * fy;
        }

        /// <summary>1D value noise (not periodic; used in local/strand space).</summary>
        static float Noise1(float x, int seed)
        {
            int xi = FloorI(x); float f = x - xi; f = f * f * (3f - 2f * f);
            float a = Hf(xi, 0, seed), b = Hf(xi + 1, 0, seed);
            return a + (b - a) * f;
        }

        /// <summary>n x n tileable fBm, normalized to [0,1]. px/py = base cell counts across the tile in U/V
        /// (px &lt; py gives features stretched along U).</summary>
        static float[] Field(int n, int px, int py, int octaves, float gain, int seed)
        {
            EnsureTables();
            var f = new float[n * n];
            float inv = 1f / n, min = float.MaxValue, max = float.MinValue;
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) * inv;
                    float sum = 0f, amp = 1f, norm = 0f; int cx = px, cy = py;
                    for (int o = 0; o < octaves; o++)
                    {
                        sum += amp * VNoise(u * cx, v * cy, cx, cy, seed + o * 31);
                        norm += amp; amp *= gain; cx <<= 1; cy <<= 1;
                    }
                    float val = sum / norm;
                    f[y * n + x] = val;
                    if (val < min) min = val;
                    if (val > max) max = val;
                }
            }
            float k = max - min > 1e-6f ? 1f / (max - min) : 1f;
            for (int i = 0; i < f.Length; i++) f[i] = (f[i] - min) * k;
            return f;
        }

        /// <summary>Bilinear wrapped sample of an m x m field at tile coords (u,v) (any real value).</summary>
        static float Sample(float[] f, int m, float u, float v)
        {
            float x = u * m - 0.5f, y = v * m - 0.5f;
            int xi = FloorI(x), yi = FloorI(y);
            float fx = x - xi, fy = y - yi;
            int x0 = Wrap(xi, m), x1 = x0 + 1 == m ? 0 : x0 + 1;
            int y0 = Wrap(yi, m) * m, y1 = (Wrap(yi, m) + 1 == m ? 0 : Wrap(yi, m) + 1) * m;
            float a = f[y0 + x0], b = f[y0 + x1], c = f[y1 + x0], d = f[y1 + x1];
            float ab = a + (b - a) * fx, cd = c + (d - c) * fx;
            return ab + (cd - ab) * fy;
        }

        /// <summary>Wrapped Worley (cellular) noise on a g x g grid. Distances in cell units;
        /// (ndx,ndy) = pixel offset from the nearest feature point.</summary>
        static void Worley(float u, float v, int g, int seed, float jitter,
            out float f1, out float f2, out int id, out float ndx, out float ndy)
        {
            float x = u * g, y = v * g;
            int xi = FloorI(x), yi = FloorI(y);
            float d1 = 1e9f, d2 = 1e9f; id = 0; ndx = 0f; ndy = 0f;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int cx = xi + ox, cy = yi + oy;
                    int wx = Wrap(cx, g), wy = Wrap(cy, g);
                    float fpx = cx + 0.5f + (Hf(wx, wy, seed) - 0.5f) * jitter;
                    float fpy = cy + 0.5f + (Hf(wx, wy, seed + 1) - 0.5f) * jitter;
                    float dx = x - fpx, dy = y - fpy;
                    float d = dx * dx + dy * dy;
                    if (d < d1) { d2 = d1; d1 = d; id = wy * g + wx; ndx = dx; ndy = dy; }
                    else if (d < d2) d2 = d;
                }
            f1 = Mathf.Sqrt(d1); f2 = Mathf.Sqrt(d2);
        }

        static float[] BoxBlur(float[] src, int n, int r)
        {
            var tmp = new float[n * n];
            var dst = new float[n * n];
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < n; y++)
            {
                int row = y * n; float sum = 0f;
                for (int k = -r; k <= r; k++) sum += src[row + Wrap(k, n)];
                for (int x = 0; x < n; x++)
                {
                    tmp[row + x] = sum * inv;
                    sum += src[row + Wrap(x + r + 1, n)] - src[row + Wrap(x - r, n)];
                }
            }
            for (int x = 0; x < n; x++)
            {
                float sum = 0f;
                for (int k = -r; k <= r; k++) sum += tmp[Wrap(k, n) * n + x];
                for (int y = 0; y < n; y++)
                {
                    dst[y * n + x] = sum * inv;
                    sum += tmp[Wrap(y + r + 1, n) * n + x] - tmp[Wrap(y - r, n) * n + x];
                }
            }
            return dst;
        }

        /// <summary>Cheap cavity occlusion: darken pixels lower than their blurred neighbourhood.</summary>
        static void Cavity(Canvas c, int radius, float amount, float maxDark)
        {
            var blur = BoxBlur(c.H, c.N, radius);
            for (int i = 0; i < blur.Length; i++)
            {
                float d = blur[i] - c.H[i];
                if (d <= 0f) continue;
                c.Mul(i, 1f - Math.Min(maxDark, d * amount));
            }
        }

        static float Gauss(System.Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
        }
        static float Rnd(System.Random r) => (float)r.NextDouble();

        static void Speck(Canvas c, float cx, float cy, float rad, Color col, float opacity, float dh, float s)
        {
            int n = c.N, mask = n - 1, ri = (int)rad + 1;
            int x0 = FloorI(cx), y0 = FloorI(cy);
            for (int dy = -ri; dy <= ri; dy++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    float ddx = x0 + dx + 0.5f - cx, ddy = y0 + dy + 0.5f - cy;
                    float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                    if (d > rad + 0.5f) continue;
                    float a = opacity * (d < rad - 0.5f ? 1f : rad + 0.5f - d);
                    int i = ((y0 + dy) & mask) * n + ((x0 + dx) & mask);
                    c.Blend(i, col, a);
                    c.H[i] += dh * a;
                    if (s >= 0f) c.S[i] += (s - c.S[i]) * a;
                }
        }

        // =====================================================================================
        //  OSB — hero texture. 1024 px = 4' (1.2192 m) => ~1.2 mm / px.
        //  ~2100 overlapping wood strands (65-150 mm x 10-30 mm) stamped with wrap-around,
        //  aligned along U (sheet length) with ~12 deg scatter, each with ragged edges, angled/torn
        //  ends, internal fibre streaks, edge darkening, and its own tone. Under-layer shows through
        //  as darker voids; cavity occlusion + resin specks finish it.
        // =====================================================================================

        static Canvas Osb()
        {
            const int n = 1024, mask = n - 1;
            var c = new Canvas(n);
            var rng = new System.Random(4242);
            float inv = 1f / n;

            // Under-layer: deeper strands seen through gaps — dark, streaky along U.
            var under = Field(256, 6, 28, 3, 0.55f, 11);
            Color gapDark = C(140, 106, 64), gapMid = C(172, 132, 82); // voids read as shallow shade, not holes
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) * inv;
                    float t = Sample(under, 256, u, v);
                    int i = y * n + x;
                    c.Set(i, Color.Lerp(gapDark, gapMid, t));
                    c.H[i] = t * 0.15f - 0.2f;
                    c.S[i] = 0.6f;
                }
            }

            // Strand palette (sRGB): honey / tan / blond aspen / amber / brown / dark / reddish pine.
            Color[] pal =
            {
                // Real OSB reads as one honey-tan field: narrow tonal range, few dark flakes, no red.
                C(206, 168, 110), C(218, 184, 128), C(228, 198, 148), C(198, 154, 96),
                C(182, 138, 84)
            };
            float[] wts = { 0.34f, 0.26f, 0.14f, 0.18f, 0.08f };

            var edgeTop = new float[256]; var edgeBot = new float[256]; var along = new float[256];
            var endA = new float[256]; var endB = new float[256]; var fiber = new float[256];

            const int count = 2100;
            for (int k = 0; k < count; k++)
            {
                float cx = Rnd(rng) * n, cy = Rnd(rng) * n;
                float sigma = Rnd(rng) < 0.1f ? 0.55f : 0.21f;
                float ang = Mathf.Clamp(Gauss(rng) * sigma, -1.2f, 1.2f);
                float len = Mathf.Lerp(64f, 150f, Rnd(rng));
                float r2 = Rnd(rng);
                float wid = 10f + 20f * r2 * r2;
                if (Rnd(rng) < 0.07f) { len *= 0.42f; wid *= 0.8f; } // small chips/fines

                // tone
                float pick = Rnd(rng), acc = 0f; int pi = 0;
                for (int p = 0; p < wts.Length; p++) { acc += wts[p]; if (pick <= acc) { pi = p; break; } pi = p; }
                Color tone = pal[pi];
                float bright = 0.95f + 0.1f * Rnd(rng);
                float warm = (Rnd(rng) - 0.5f) * 0.05f;
                float tr = tone.r * bright * (1f + warm), tg = tone.g * bright, tb = tone.b * bright * (1f - warm * 1.5f);
                float hgt = 0.35f + 0.45f * (k / (float)count) + 0.2f * Rnd(rng);
                float grad = (Rnd(rng) - 0.5f) * 0.12f; // one end darker than the other
                int sd = rng.Next(1, 1 << 20);

                float hl = len * 0.5f, hw = wid * 0.5f;
                int Ls = Math.Min(255, (int)len + 4), Wt = Math.Min(255, (int)wid + 4);
                float rag = 0.08f + 0.16f * Rnd(rng);
                float taper = 0.5f * Rnd(rng);
                for (int s = 0; s < Ls; s++)
                {
                    float sn = Mathf.Abs((s - 1.5f - hl) / hl);
                    float tp = 1f - taper * Smooth(0.55f, 1.0f, sn);
                    edgeTop[s] = (1f - rag * Noise1(s * 0.22f, sd)) * tp;
                    edgeBot[s] = (1f - rag * Noise1(s * 0.22f, sd + 1)) * tp;
                    along[s] = (Noise1(s * 0.045f, sd + 2) - 0.5f) + grad * ((s - 1.5f - hl) / hl) * 4f;
                }
                float skewA = (Rnd(rng) - 0.5f) * 0.4f, skewB = (Rnd(rng) - 0.5f) * 0.4f;
                float endRag = 0.06f + 0.14f * Rnd(rng);
                bool band = Rnd(rng) < 0.35f;
                float bc = Rnd(rng) * wid, bw = 1.2f + 2.5f * Rnd(rng);
                for (int t = 0; t < Wt; t++)
                {
                    float tn = (t - 1.5f - hw) / hw;
                    endA[t] = 1f - endRag * Noise1(t * 0.35f, sd + 3) + skewA * tn;
                    endB[t] = 1f - endRag * Noise1(t * 0.35f, sd + 4) + skewB * tn;
                    float f = 0.65f * (Noise1(t * 0.55f, sd + 5) * 2f - 1f) + 0.35f * (Noise1(t * 1.7f, sd + 6) * 2f - 1f);
                    if (band) { float q = (t - 1.5f - bc) / bw; f -= 1.1f * Mathf.Exp(-q * q); }
                    fiber[t] = f;
                }

                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                float lim = hl * 1.25f;
                float ex = Mathf.Abs(ca) * lim + Mathf.Abs(sa) * hw + 2f;
                float ey = Mathf.Abs(sa) * lim + Mathf.Abs(ca) * hw + 2f;
                int x0 = FloorI(cx - ex), x1 = FloorI(cx + ex) + 1, y0 = FloorI(cy - ey), y1 = FloorI(cy + ey) + 1;
                for (int py = y0; py <= y1; py++)
                {
                    float dy = py + 0.5f - cy;
                    int row = (py & mask) * n;
                    for (int px = x0; px <= x1; px++)
                    {
                        float dx = px + 0.5f - cx;
                        float sA = dx * ca + dy * sa;
                        float tA = -dx * sa + dy * ca;
                        if (sA <= -lim || sA >= lim || tA <= -hw || tA >= hw) continue;
                        int ks = (int)(sA + hl + 1.5f); if (ks < 0) ks = 0; else if (ks >= Ls) ks = Ls - 1;
                        int kt = (int)(tA + hw + 1.5f); if (kt < 0) kt = 0; else if (kt >= Wt) kt = Wt - 1;
                        float tn = tA / hw, atn = tn < 0f ? -tn : tn;
                        float wl = tn >= 0f ? edgeTop[ks] : edgeBot[ks];
                        if (atn > wl) continue;
                        float sn = sA / hl, asn = sn < 0f ? -sn : sn;
                        float el = sn >= 0f ? endA[kt] : endB[kt];
                        if (asn > el) continue;

                        float edge = Math.Min((wl - atn) * hw, (el - asn) * hl); // px to nearest edge
                        float eShade = edge >= 2.5f ? 1f : 0.89f + 0.044f * edge;
                        float f = fiber[kt], av = along[ks];
                        float mul = (1f + 0.085f * f + 0.09f * av) * eShade;
                        int i = row + (px & mask);
                        c.R[i] = tr * mul; c.G[i] = tg * mul; c.B[i] = tb * mul;
                        c.H[i] = hgt + 0.05f * f + 0.03f * av - (1f - eShade) * 1.0f;
                        c.S[i] = 1f + 0.15f * f;
                    }
                }
            }

            Cavity(c, 3, 2.2f, 0.30f);

            // Board-scale tone drift (press/wax/moisture) and a faint warm/cool shift.
            var macro = Field(128, 4, 4, 4, 0.55f, 77);
            var hue = Field(128, 3, 3, 3, 0.5f, 78);
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) * inv;
                    int i = y * n + x;
                    float m = 0.92f + 0.16f * Sample(macro, 128, u, v);
                    float h = (Sample(hue, 128, u, v) - 0.5f) * 0.06f;
                    float micro = 0.97f + 0.06f * Hf(x, y, 79);
                    c.R[i] *= m * micro * (1f + h); c.G[i] *= m * micro; c.B[i] *= m * micro * (1f - h * 1.5f);
                }
            }

            // Resin / wax specks (dark, slightly glossy) and a few pale fines.
            Color resin = C(88, 56, 30);
            for (int k = 0; k < 1500; k++)
                Speck(c, Rnd(rng) * n, Rnd(rng) * n, 0.6f + 1.1f * Rnd(rng), resin, 0.55f + 0.4f * Rnd(rng), -0.05f, 1.8f);
            Color fines = C(226, 196, 146);
            for (int k = 0; k < 400; k++)
                Speck(c, Rnd(rng) * n, Rnd(rng) * n, 0.5f + 0.8f * Rnd(rng), fines, 0.5f, 0.05f, -1f);
            return c;
        }

        // =====================================================================================
        //  Wood (flat-sawn face): growth-ring contours of r = v*Rings + warp(u,v); latewood rises
        //  gradually within a ring and drops sharply at the boundary. Knots bulge the contours.
        // =====================================================================================

        sealed class WoodSpec
        {
            public int N = 512, Rings = 40, WarpPx = 2, WarpPy = 5, Seed = 1, Knots = 0;
            public float WarpAmp = 3f, WarpB = 0.35f, Late = 0.25f, Fiber = 0.04f, Blotch = 0.05f, Pores = 0f;
            public float KnotMin = 0.01f, KnotMax = 0.02f, KnotAspect = 1.4f, ContrastMin = 0.4f, LateGloss = 0.25f;
            public Color Early, LateC, KnotC;
        }

        static WoodSpec PineSpec() => new WoodSpec
        {
            N = 1024, Rings = 56, WarpPx = 2, WarpPy = 5, WarpAmp = 3.2f, Late = 0.22f, Fiber = 0.035f, Blotch = 0.05f,
            Knots = 4, KnotMin = 0.008f, KnotMax = 0.016f, ContrastMin = 0.35f, Seed = 101,
            Early = C(233, 210, 167), LateC = C(204, 164, 112), KnotC = C(168, 112, 60)
        };

        static WoodSpec OakSpec() => new WoodSpec
        {
            N = 512, Rings = 22, WarpPx = 2, WarpPy = 3, WarpAmp = 4.5f, WarpB = 0.4f, Late = 0.35f, Fiber = 0.06f, Blotch = 0.06f,
            Pores = 0.35f, ContrastMin = 0.5f, Seed = 301, LateGloss = 0.1f,
            Early = C(182, 130, 78), LateC = C(132, 86, 48), KnotC = C(90, 56, 30)
        };

        static WoodSpec WalnutSpec() => new WoodSpec
        {
            N = 512, Rings = 18, WarpPx = 2, WarpPy = 3, WarpAmp = 4f, WarpB = 0.5f, Late = 0.35f, Fiber = 0.07f, Blotch = 0.08f,
            Pores = 0.25f, ContrastMin = 0.5f, Seed = 401, LateGloss = 0.1f,
            Early = C(114, 77, 50), LateC = C(74, 48, 32), KnotC = C(50, 32, 22)
        };

        static Canvas Wood(WoodSpec w)
        {
            int n = w.N;
            var c = new Canvas(n);
            var rng = new System.Random(w.Seed * 7919 + 13);
            var warpA = Field(256, w.WarpPx, w.WarpPy, 3, 0.5f, w.Seed + 1);
            var warpB = Field(256, w.WarpPx * 3, w.WarpPy * 4, 2, 0.5f, w.Seed + 2);
            var blot = Field(128, 3, 4, 3, 0.55f, w.Seed + 3);
            var fib = Field(n, 4, n / 4, 2, 0.5f, w.Seed + 4);
            float[] pores = w.Pores > 0f ? Field(n, n / 16, n / 3, 1, 0.5f, w.Seed + 5) : null;

            int kc = w.Knots;
            var kx = new float[kc]; var ky = new float[kc]; var kr = new float[kc];
            for (int k = 0; k < kc; k++) { kx[k] = Rnd(rng); ky[k] = Rnd(rng); kr[k] = Mathf.Lerp(w.KnotMin, w.KnotMax, Rnd(rng)); }

            float inv = 1f / n;
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) * inv;
                    int i = y * n + x;
                    float wv = (Sample(warpA, 256, u, v) - 0.5f) + w.WarpB * (Sample(warpB, 256, u, v) - 0.5f);
                    float r = v * w.Rings + wv * w.WarpAmp;

                    float knot = 0f, knotD = 0f, halo = 0f;
                    for (int k = 0; k < kc; k++)
                    {
                        float dx = u - kx[k]; dx -= Mathf.Round(dx);
                        float dy = v - ky[k]; dy -= Mathf.Round(dy);
                        float ex = dx / (kr[k] * w.KnotAspect), ey = dy / kr[k];
                        float e = ex * ex + ey * ey;
                        if (e > 40f) continue;
                        r += 2.4f * Mathf.Exp(-e * 0.18f);
                        halo = Math.Max(halo, Mathf.Exp(-e * 0.5f));
                        float d = Mathf.Sqrt(e);
                        float km = 1f - Smooth(0.8f, 1.05f, d);
                        if (km > knot) { knot = km; knotD = d; }
                    }

                    int ri = FloorI(r);
                    float fr = r - ri;
                    float strength = w.ContrastMin + (1f - w.ContrastMin) * Hf(Wrap(ri, w.Rings), 0, w.Seed + 9);
                    float lw = Smooth(1f - w.Late * 1.7f, 1f - w.Late * 0.1f, fr) * strength;

                    float f = fib[i] - 0.5f;
                    float bl = Sample(blot, 128, u, v) - 0.5f;
                    Color col = Color.Lerp(w.Early, w.LateC, lw);
                    float mul = 1f + f * w.Fiber * 2f + bl * w.Blotch * 2f;
                    float pm = 0f;
                    if (pores != null)
                    {
                        pm = Smooth(0.66f, 0.82f, pores[i]) * (1f - 0.7f * lw);
                        mul *= 1f - w.Pores * pm;
                    }
                    mul *= 1f - 0.10f * halo;
                    col *= mul;
                    if (knot > 0f)
                    {
                        float ring = 0.78f + 0.22f * (0.5f + 0.5f * Mathf.Sin(knotD * 22f));
                        Color kcol = w.KnotC * (ring * (1f - 0.35f * Smooth(0.6f, 1f, knotD)));
                        col = Color.Lerp(col, kcol, knot);
                    }
                    c.R[i] = col.r; c.G[i] = col.g; c.B[i] = col.b;
                    c.H[i] = -lw * 0.35f + f * 0.25f - pm * 0.5f + knot * 0.15f;
                    c.S[i] = 1f + lw * w.LateGloss - pm * 0.3f + knot * 0.2f;
                }
            }
            return c;
        }

        // Pressure-treated: greener, blotchy, with staggered incising slits. 512 px = 0.5 m.
        static Canvas Treated()
        {
            var w = new WoodSpec
            {
                N = 512, Rings = 28, WarpPx = 2, WarpPy = 4, WarpAmp = 2.5f, Late = 0.25f, Fiber = 0.04f, Blotch = 0.10f,
                Knots = 3, KnotMin = 0.018f, KnotMax = 0.034f, ContrastMin = 0.4f, Seed = 201,
                Early = C(170, 165, 112), LateC = C(128, 122, 78), KnotC = C(96, 84, 48)
            };
            var c = Wood(w);
            int n = c.N, mask = n - 1;
            float inv = 1f / n;
            var hue = Field(128, 4, 4, 3, 0.5f, 211);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float hb = Sample(hue, 128, (x + 0.5f) * inv, (y + 0.5f) * inv) - 0.5f;
                    c.R[i] *= 1f + 0.12f * hb; c.G[i] *= 1f - 0.02f * hb; c.B[i] *= 1f - 0.14f * hb;
                    c.S[i] *= 0.9f;
                }
            // Incising: 12 px (~12 mm) slits along the grain in a staggered grid (rows even => tiles).
            const int cols = 24, rows = 40;
            float sx = n / (float)cols, sy = n / (float)rows;
            for (int j = 0; j < rows; j++)
                for (int k = 0; k < cols; k++)
                {
                    float cx = (k + ((j & 1) == 1 ? 0.5f : 0f)) * sx + (Hf(k, j, 77) - 0.5f) * 2f;
                    float cy = (j + 0.5f) * sy + (Hf(k, j, 78) - 0.5f) * 1.5f;
                    int x0 = FloorI(cx), y0 = FloorI(cy);
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        float a = Mathf.Abs(dx) / 6.5f, depth = 1f - a * a;
                        for (int dy = 0; dy <= 1; dy++)
                        {
                            int i = ((y0 + dy) & mask) * n + ((x0 + dx) & mask);
                            c.Mul(i, 1f - 0.45f * depth);
                            c.H[i] -= 0.7f * depth;
                            c.S[i] = 0.6f;
                        }
                    }
                }
            return c;
        }

        // Plywood face: rotary-cut veneer = very wide, wild flowing bands, plus "football" patches.
        static Canvas Plywood()
        {
            var w = new WoodSpec
            {
                N = 1024, Rings = 9, WarpPx = 2, WarpPy = 2, WarpAmp = 3.5f, WarpB = 0.6f, Late = 0.32f, Fiber = 0.04f, Blotch = 0.07f,
                Knots = 2, KnotMin = 0.008f, KnotMax = 0.013f, KnotAspect = 1.15f, ContrastMin = 0.5f, Seed = 501,
                Early = C(233, 207, 158), LateC = C(204, 154, 95), KnotC = C(140, 90, 48)
            };
            var c = Wood(w);
            var rng = new System.Random(502);
            for (int p = 0; p < 2; p++)
                FootballPatch(c, Rnd(rng) * c.N, Rnd(rng) * c.N, 0.035f * c.N, 0.017f * c.N, w, 510 + p);
            return c;
        }

        static void FootballPatch(Canvas c, float cx, float cy, float hl, float hw, WoodSpec w, int seed)
        {
            int n = c.N, mask = n - 1;
            int ix = FloorI(cx), iy = FloorI(cy);
            for (int py = -(int)hw - 2; py <= (int)hw + 2; py++)
                for (int px = -(int)hl - 2; px <= (int)hl + 2; px++)
                {
                    float s = px / hl;
                    if (s <= -1f || s >= 1f) continue;
                    float lim = hw * (1f - s * s);
                    float edgeDist = lim - Mathf.Abs(py);
                    if (edgeDist < -1.2f) continue;
                    int i = ((iy + py) & mask) * n + ((ix + px) & mask);
                    if (edgeDist < 0.8f) { c.Mul(i, 0.72f); c.H[i] -= 0.25f; continue; }
                    float rr = (iy + py) / (float)n * w.Rings * 2.5f + Noise1((ix + px) * 0.01f, seed) * 0.8f;
                    float lw = Smooth(0.72f, 0.97f, Frac(rr)) * 0.6f;
                    Color col = Color.Lerp(w.Early, w.LateC, lw) * (1.03f + 0.03f * (Hf(px, py, seed) - 0.5f));
                    c.Set(i, col);
                    c.H[i] = -lw * 0.3f;
                    c.S[i] = 1f;
                }
        }

        // End grain: concentric growth rings around a pith just outside the board region.
        // 512 px = 0.4 m. Torus-wrapped distance keeps it tileable (crease far from typical end faces).
        static Canvas EndGrain()
        {
            const int n = 512;
            var c = new Canvas(n);
            var warp = Field(256, 4, 4, 3, 0.5f, 151);
            var saw = Field(n, 3, 160, 2, 0.5f, 152);
            const float pu = 0.92f, pv = 0.88f;
            Color early = C(206, 174, 124), late = C(152, 108, 64);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) * inv;
                    int i = y * n + x;
                    float dx = u - pu; dx -= Mathf.Round(dx);
                    float dy = v - pv; dy -= Mathf.Round(dy);
                    float rho = Mathf.Sqrt(dx * dx + dy * dy);
                    float r = rho * 90f + (Sample(warp, 256, u, v) - 0.5f) * 2.2f;
                    int ri = FloorI(r);
                    float strength = 0.45f + 0.55f * Hf(ri, 3, 153);
                    float lw = Smooth(0.62f, 0.95f, r - ri) * strength;
                    float sm = saw[i] - 0.5f;
                    float speck = Hf(x, y, 154) - 0.5f;
                    Color col = Color.Lerp(early, late, lw) * (1f + sm * 0.12f + speck * 0.08f);
                    c.Set(i, col);
                    c.H[i] = -lw * 0.4f + sm * 0.5f + speck * 0.1f;
                    c.S[i] = 0.75f;
                }
            }
            return c;
        }

        // =====================================================================================
        //  Ground
        // =====================================================================================

        // Grass: 1024 px = 2 m. ~120k stamped blades over a dark soil/shadow base; colour from
        // multi-scale fields (lush vs yellow-green vs dry straw patches), tips lighter than roots.
        static Canvas Grass()
        {
            const int n = 1024, mask = n - 1;
            var c = new Canvas(n);
            var rng = new System.Random(3131);
            var macro = Field(128, 3, 3, 4, 0.55f, 301);
            var mid = Field(256, 14, 14, 3, 0.5f, 302);
            var dry = Field(128, 5, 5, 3, 0.5f, 303);
            Color soil = C(58, 50, 30), shadowG = C(38, 54, 24);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float m = Sample(mid, 256, (x + 0.5f) * inv, (y + 0.5f) * inv);
                    c.Set(i, Color.Lerp(soil, shadowG, m) * (0.85f + 0.3f * Hf(x, y, 9)));
                    c.H[i] = 0f; c.S[i] = 0.6f;
                }
            Color g1 = C(70, 102, 36), g2 = C(108, 140, 56), g3 = C(112, 124, 44), straw = C(158, 146, 88);
            const int blades = 120000;
            for (int b = 0; b < blades; b++)
            {
                float x0 = Rnd(rng) * n, y0 = Rnd(rng) * n;
                float u = x0 * inv, v = y0 * inv;
                float mv = Sample(macro, 128, u, v), md = Sample(mid, 256, u, v), dr = Sample(dry, 128, u, v);
                Color gc = Color.Lerp(g1, g2, md);
                gc = Color.Lerp(gc, g3, mv * 0.6f);
                float dryAmt = Smooth(0.62f, 0.9f, dr) * 0.28f; // subtle: strong patches show the tile grid at distance
                if (Rnd(rng) < 0.035f) dryAmt = 0.85f;
                gc = Color.Lerp(gc, straw, dryAmt);
                float bright = 0.78f + 0.4f * Rnd(rng);
                float ang = Rnd(rng) * Mathf.PI * 2f;
                float len = 7f + 14f * Rnd(rng);
                float dx = Mathf.Cos(ang), dy = Mathf.Sin(ang);
                int wx = Mathf.RoundToInt(-dy), wy = Mathf.RoundToInt(dx);
                float hgt = 0.3f + 0.7f * Rnd(rng);
                int steps = (int)len;
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / len;
                    int ix = FloorI(x0 + dx * s) & mask, iy = FloorI(y0 + dy * s) & mask;
                    Color col = gc * (bright * (0.62f + 0.5f * t));
                    int i = iy * n + ix;
                    c.Set(i, col); c.H[i] = hgt * (0.4f + 0.6f * t); c.S[i] = 1f;
                    if (t < 0.6f)
                    {
                        int j = ((iy + wy) & mask) * n + ((ix + wx) & mask);
                        c.Set(j, col * 0.9f); c.H[j] = hgt * (0.35f + 0.6f * t); c.S[j] = 1f;
                    }
                }
            }
            Cavity(c, 2, 3f, 0.5f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    c.Mul(i, 0.9f + 0.2f * Sample(macro, 128, (x + 0.5f) * inv, (y + 0.5f) * inv));
                }
            return c;
        }

        // Dirt: 512 px = 1.5 m. Multi-scale fBm, damp patches, small clods (Worley) and pebbles.
        static Canvas Dirt()
        {
            const int n = 512;
            var c = new Canvas(n);
            var macro = Field(128, 4, 4, 5, 0.55f, 401);
            var mid = Field(256, 16, 16, 3, 0.5f, 402);
            var damp = Field(128, 3, 3, 3, 0.5f, 403);
            Color dryC = C(128, 100, 72), darkC = C(86, 64, 44), pebbleC = C(150, 140, 124);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) * inv;
                    int i = y * n + x;
                    float m = Sample(macro, 128, u, v), md = Sample(mid, 256, u, v), dp = Sample(damp, 128, u, v);
                    Worley(u, v, 60, 404, 0.9f, out float cf1, out float cf2, out int _, out float _, out float _);
                    float clod = Smooth(0f, 0.4f, cf2 - cf1);
                    Worley(u, v, 22, 405, 0.8f, out float pf1, out float _, out int pid, out float _, out float _);
                    float isPeb = Hf(pid, 0, 406) < 0.3f ? 1f : 0f;
                    float prad = 0.14f + 0.12f * Hf(pid, 1, 406);
                    float peb = isPeb * (1f - Smooth(prad * 0.7f, prad, pf1));
                    Color col = Color.Lerp(dryC, darkC, m * 0.7f + md * 0.3f);
                    col *= 1f - 0.25f * Smooth(0.55f, 0.8f, dp);
                    col *= 0.9f + 0.2f * Hf(x, y, 407) + 0.08f * (clod - 0.5f);
                    col = Color.Lerp(col, pebbleC * (0.8f + 0.4f * Hf(pid, 2, 406)), peb);
                    c.Set(i, col);
                    c.H[i] = m * 0.4f + md * 0.3f + clod * 0.25f + peb * 0.6f;
                    c.S[i] = 1f + 0.8f * Smooth(0.55f, 0.8f, dp) + peb * 0.5f;
                }
            }
            Cavity(c, 3, 2f, 0.35f);
            return c;
        }

        // Gravel: 512 px = 1 m, ~2.3 cm stones from domain-warped Worley cells, dark gaps.
        static Canvas Gravel()
        {
            const int n = 512;
            var c = new Canvas(n);
            var wxF = Field(128, 6, 6, 2, 0.5f, 501);
            var wyF = Field(128, 6, 6, 2, 0.5f, 502);
            Color[] pal = { C(132, 130, 124), C(152, 140, 118), C(96, 94, 90), C(140, 143, 147), C(118, 108, 96), C(170, 164, 152) };
            Color gap = C(40, 38, 35);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) * inv;
                    int i = y * n + x;
                    float uu = u + (Sample(wxF, 128, u, v) - 0.5f) * 0.03f;
                    float vv = v + (Sample(wyF, 128, u, v) - 0.5f) * 0.03f;
                    Worley(uu, vv, 44, 503, 0.9f, out float f1, out float f2, out int id, out float _, out float _);
                    float edge = f2 - f1;
                    float hc = Hf(id, 7, 504);
                    float dome = Smooth(0f, 0.35f, edge);
                    float stone = Smooth(0.03f, 0.10f, edge);
                    Color sc = pal[(int)(Hf(id, 3, 504) * pal.Length) % pal.Length] * (0.85f + 0.3f * Hf(id, 5, 504));
                    float sp = 0.88f + 0.24f * Hf(x, y, 505);
                    Color col = Color.Lerp(gap, sc * (sp * (0.75f + 0.25f * dome)), stone);
                    c.Set(i, col);
                    c.H[i] = dome * (0.6f + 0.4f * hc);
                    c.S[i] = 0.8f + 0.6f * hc * stone;
                }
            }
            Cavity(c, 3, 2f, 0.4f);
            return c;
        }

        // Concrete / CMU: blotchy cement paste, fine aggregate speckle, pits. 512 px.
        static Canvas Concrete(Color baseC, int seed, int poreGrid, float poreChance, float aggregate)
        {
            const int n = 512;
            var c = new Canvas(n);
            var macro = Field(128, 3, 3, 5, 0.55f, seed);
            var mid = Field(256, 12, 12, 3, 0.5f, seed + 1);
            var fine = Field(n, 96, 96, 2, 0.5f, seed + 2);
            Color aggDark = C(110, 108, 102), aggLight = C(196, 192, 182);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) * inv;
                    int i = y * n + x;
                    float m = Sample(macro, 128, u, v) - 0.5f, md = Sample(mid, 256, u, v) - 0.5f, fn = fine[i] - 0.5f;
                    float hs = Hf(x, y, seed + 3) - 0.5f;
                    Worley(u, v, poreGrid, seed + 4, 0.9f, out float f1, out float _, out int id, out float _, out float _);
                    float pore = 0f;
                    if (Hf(id, 0, seed + 5) < poreChance)
                    {
                        float rad = 0.10f + 0.14f * Hf(id, 1, seed + 5);
                        pore = 1f - Smooth(rad * 0.6f, rad, f1);
                    }
                    Color col = baseC * (1f + m * 0.16f + md * 0.06f + fn * 0.08f + hs * 0.05f);
                    // coarse aggregate grains: cells of a finer Worley tinted lighter/darker
                    if (aggregate > 0f)
                    {
                        float ag = Smooth(0.68f, 0.8f, fine[i]);
                        col = Color.Lerp(col, Hf(x >> 2, y >> 2, seed + 6) < 0.5f ? aggDark : aggLight, ag * aggregate * 2f);
                    }
                    col *= 1f - 0.45f * pore;
                    c.Set(i, col);
                    c.H[i] = (m + 0.5f) * 0.2f + fn * 0.3f + hs * aggregate * 0.4f - pore * 0.8f;
                    c.S[i] = 1f + m * 0.4f - pore * 0.4f;
                }
            }
            return c;
        }

        // Mortar: gray sand-cement, fine sand grains. 512 px = 0.5 m.
        static Canvas Mortar()
        {
            const int n = 512;
            var c = new Canvas(n);
            var macro = Field(128, 4, 4, 4, 0.55f, 1701);
            var fine = Field(n, 128, 128, 2, 0.5f, 1702);
            Color baseC = C(152, 150, 144), sandL = C(186, 178, 160), sandD = C(104, 100, 94);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float m = Sample(macro, 128, (x + 0.5f) * inv, (y + 0.5f) * inv) - 0.5f;
                    float hs = Hf(x, y, 1703);
                    Color col = baseC * (1f + m * 0.12f + (fine[i] - 0.5f) * 0.1f);
                    if (hs > 0.93f) col = Color.Lerp(col, sandL, 0.6f);
                    else if (hs < 0.06f) col = Color.Lerp(col, sandD, 0.6f);
                    c.Set(i, col);
                    c.H[i] = fine[i] * 0.4f + (hs - 0.5f) * 0.25f;
                    c.S[i] = 0.8f;
                }
            return c;
        }

        // =====================================================================================
        //  Masonry & steel
        // =====================================================================================

        // Brick: 512 px = 0.6 m. Sanded (sand-struck) face: fine grit, dark iron/manganese speckles,
        // fire-flash tone patches (~0.2 m scale so randomly offset bricks differ), small pits.
        // Used: darker, soot blotches, tumbled dents/chips, old mortar smears.
        static Canvas Brick(bool used)
        {
            const int n = 512;
            int seed = used ? 1201 : 1101;
            var c = new Canvas(n);
            var tone = Field(128, 3, 3, 3, 0.55f, seed);
            var flash = Field(128, 5, 5, 3, 0.5f, seed + 1);
            var grit = Field(n, 128, 128, 2, 0.5f, seed + 2);
            var soot = used ? Field(128, 4, 4, 4, 0.55f, seed + 3) : null;
            var dent = used ? Field(256, 10, 10, 3, 0.5f, seed + 4) : null;
            Color redA = used ? C(150, 74, 52) : C(168, 72, 48);
            Color redB = used ? C(118, 60, 44) : C(140, 56, 38);
            Color flashC = used ? C(92, 52, 40) : C(110, 52, 40);
            Color sootC = C(46, 38, 36), mortarC = C(170, 166, 156), speckC = C(54, 34, 28);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) * inv;
                    int i = y * n + x;
                    float t = Sample(tone, 128, u, v), fl = Sample(flash, 128, u, v), g = grit[i] - 0.5f;
                    float hs = Hf(x, y, seed + 5);
                    Color col = Color.Lerp(redA, redB, t);
                    col = Color.Lerp(col, flashC, Smooth(0.6f, 0.9f, fl) * 0.6f);
                    col *= 1f + g * 0.14f + (hs - 0.5f) * 0.1f;
                    float h = g * 0.5f + (hs - 0.5f) * 0.2f;
                    float s = 0.9f;
                    if (used)
                    {
                        float so = Sample(soot, 128, u, v);
                        col = Color.Lerp(col, sootC, Smooth(0.55f, 0.85f, so) * 0.65f);
                        float dn = Sample(dent, 256, u, v);
                        float chip = Smooth(0.72f, 0.82f, dn);
                        col = Color.Lerp(col, col * 1.12f, chip); // fresher clay in chips
                        h -= chip * 0.8f;
                        float mo = Smooth(0.78f, 0.9f, 1f - so) * Smooth(0.5f, 0.7f, fl);
                        col = Color.Lerp(col, mortarC * (0.9f + 0.2f * hs), mo * 0.7f);
                        h += mo * 0.2f;
                    }
                    c.Set(i, col); c.H[i] = h; c.S[i] = s;
                }
            }
            var rng = new System.Random(seed + 9);
            int specks = used ? 900 : 1400;
            for (int k = 0; k < specks; k++)
                Speck(c, Rnd(rng) * n, Rnd(rng) * n, 0.5f + 1.3f * Rnd(rng), speckC, 0.5f + 0.5f * Rnd(rng), 0.05f, -1f);
            int pits = used ? 500 : 250;
            for (int k = 0; k < pits; k++)
                Speck(c, Rnd(rng) * n, Rnd(rng) * n, 0.7f + 1.6f * Rnd(rng), redB * 0.6f, 0.6f, -0.6f, 0.6f);
            Cavity(c, 2, 2f, 0.3f);
            return c;
        }

        // Rebar: 512 px = 1' (0.3048 m) along U => exactly 8 transverse ribs at 1.5" pitch (64 px).
        // Rust: orange-brown with dark mill-scale patches; ribs carry fresher/darker rust on crests.
        static Canvas Rebar()
        {
            const int n = 512;
            var c = new Canvas(n);
            var rust = Field(128, 4, 6, 4, 0.55f, 1301);
            var scale = Field(128, 3, 5, 3, 0.5f, 1302);
            var fine = Field(n, 64, 64, 2, 0.5f, 1303);
            Color rustA = C(140, 78, 42), rustB = C(104, 58, 34), millScale = C(62, 56, 52), orange = C(170, 96, 46);
            float inv = 1f / n;
            const int ribs = 8;
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) * inv;
                    int i = y * n + x;
                    // rib profile: narrow raised band per pitch, slight slant around the bar
                    float p = Frac(u * ribs + v * 1f);
                    float d = Mathf.Abs(p - 0.5f) * 2f;        // 0 at rib centre
                    float rib = 1f - Smooth(0.10f, 0.22f, d);
                    float r = Sample(rust, 128, u, v), sc = Sample(scale, 128, u, v), f = fine[i] - 0.5f;
                    float hs = Hf(x, y, 1304) - 0.5f;
                    Color col = Color.Lerp(rustA, rustB, r);
                    col = Color.Lerp(col, millScale, Smooth(0.6f, 0.8f, sc) * 0.75f);
                    col = Color.Lerp(col, orange, Smooth(0.7f, 0.95f, fine[i]) * 0.5f);
                    col *= 1f + f * 0.15f + hs * 0.08f - rib * 0.06f;
                    c.Set(i, col);
                    c.H[i] = rib * 1.0f + f * 0.15f + hs * 0.05f;
                    c.S[i] = 0.8f + Smooth(0.6f, 0.8f, sc) * 0.6f;
                }
            }
            return c;
        }

        // Kraft paper bag: fibrous tan with faint creases. 512 px = 0.5 m.
        static Canvas PaperBag()
        {
            const int n = 512;
            var c = new Canvas(n);
            var macro = Field(128, 3, 4, 3, 0.5f, 1401);
            var fibers = Field(n, 24, 160, 2, 0.5f, 1402);
            var crease = Field(128, 3, 3, 3, 0.5f, 1403);
            Color kraft = C(182, 142, 96);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) * inv, v = (y + 0.5f) * inv;
                    int i = y * n + x;
                    float m = Sample(macro, 128, u, v) - 0.5f, f = fibers[i] - 0.5f;
                    float cr = Sample(crease, 128, u, v);
                    float ridge = 1f - Mathf.Abs(cr - 0.5f) * 2f; // ridge lines where field crosses 0.5
                    ridge = Smooth(0.9f, 1f, ridge);
                    float hs = Hf(x, y, 1404) - 0.5f;
                    Color col = kraft * (1f + m * 0.08f + f * 0.08f + hs * 0.06f - ridge * 0.06f);
                    c.Set(i, col);
                    c.H[i] = f * 0.3f + cr * 0.6f + ridge * 0.3f;
                    c.S[i] = 0.9f;
                }
            return c;
        }

        // Generic fine surface (paint/porcelain/plastic/rubber/brass): macro tone drift, orange-peel bumps.
        static Canvas Surface(Color baseC, int seed, float macroAmt, int bumpPeriod, float microAmt, float sVar, float microHeight)
        {
            const int n = 512;
            var c = new Canvas(n);
            var macro = Field(128, 3, 3, 3, 0.5f, seed);
            var bump = Field(n, bumpPeriod, bumpPeriod, 2, 0.45f, seed + 1);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float m = Sample(macro, 128, (x + 0.5f) * inv, (y + 0.5f) * inv) - 0.5f;
                    float b = bump[i] - 0.5f, h = Hf(x, y, seed + 2) - 0.5f;
                    c.Set(i, baseC * (1f + m * macroAmt * 2f + h * microAmt * 2f + b * microAmt));
                    c.H[i] = b + h * microHeight;
                    c.S[i] = 1f + m * sVar * 2f - Mathf.Abs(b) * sVar;
                }
            return c;
        }

        // Brushed metal: long streaks along U.
        static Canvas Brushed(Color baseC, int seed, int streakPy, float amt)
        {
            const int n = 512;
            var c = new Canvas(n);
            var s1 = Field(n, 2, streakPy, 3, 0.6f, seed);
            var s2 = Field(n, 8, 256, 2, 0.5f, seed + 1);
            var macro = Field(128, 3, 3, 3, 0.5f, seed + 2);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float st = (s1[i] - 0.5f) * 0.6f + (s2[i] - 0.5f) * 0.4f;
                    float m = Sample(macro, 128, (x + 0.5f) * inv, (y + 0.5f) * inv) - 0.5f;
                    c.Set(i, baseC * (1f + st * amt * 2f + m * 0.03f));
                    c.H[i] = st;
                    c.S[i] = 1f + st * 0.25f + m * 0.1f;
                }
            return c;
        }

        // Galvanized: Worley spangle crystals (~12 mm), each with its own brightness, feathered
        // dendrite stripes along a random direction, facet gradient, and its own smoothness.
        static Canvas Galvanized()
        {
            const int n = 512;
            var c = new Canvas(n);
            var wxF = Field(128, 5, 5, 2, 0.5f, 901);
            var wyF = Field(128, 5, 5, 2, 0.5f, 902);
            Color baseC = C(180, 184, 188);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) * inv;
                    int i = y * n + x;
                    float uu = u + (Sample(wxF, 128, u, v) - 0.5f) * 0.04f;
                    float vv = v + (Sample(wyF, 128, u, v) - 0.5f) * 0.04f;
                    Worley(uu, vv, 32, 903, 1f, out float f1, out float f2, out int id, out float ndx, out float ndy);
                    float th = Hf(id, 1, 904) * Mathf.PI;
                    float ct = Mathf.Cos(th), st = Mathf.Sin(th);
                    float tone = 0.86f + 0.12f * Hf(id, 2, 904);
                    float across = -ndx * st + ndy * ct;
                    float feather = Noise1(across * 14f + id * 3.1f, 905) - 0.5f;
                    float facet = ndx * ct + ndy * st;
                    float border = 1f - Smooth(0.0f, 0.05f, f2 - f1);
                    float mul = tone + 0.06f * feather + 0.06f * facet - 0.07f * border;
                    c.Set(i, baseC * mul);
                    c.H[i] = feather * 0.3f + facet * 0.1f - border * 0.3f;
                    c.S[i] = 0.75f + 0.5f * Hf(id, 4, 904);
                }
            }
            return c;
        }
    }
}
