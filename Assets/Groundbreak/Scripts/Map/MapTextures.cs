using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Procedural, seamless textures for <see cref="MapMaterials"/> (brick, R-panel steel, stucco, crop fields, water).
    /// Every texture is generated once on first use (albedo + normal from a height field, periodic noise so it tiles) and
    /// materials are cached per tint. UVs are metres: mainTextureScale = 1 / tile size. Rows / ribs / courses follow the
    /// MapMaterials conventions (crop rows and brick courses run along U, R-panel ribs along V).
    /// </summary>
    public static class MapTextures
    {
        public static bool Ready => true;

        /// <summary>Crop row spacing (30").</summary>
        public const float RowSpacing = 0.762f;
        /// <summary>48" square: 6 modular bricks (8" module) x 18 courses (3 courses = 8").</summary>
        public const float BrickTile = 1.2192f;
        /// <summary>36" square: 3 major R-panel ribs at 12" o.c.</summary>
        public const float MetalTile = 0.9144f;
        public const float StuccoTile = 1.0f;
        public const float WaterTile = 6f;
        public static float FieldTile(FieldKind k) => (k == FieldKind.Hay || k == FieldKind.Pasture) ? RowSpacing * 8f : RowSpacing * 4f;

        static readonly Dictionary<Color, Material> _brick = new Dictionary<Color, Material>();
        static readonly Dictionary<Color, Texture2D> _brickAlb = new Dictionary<Color, Texture2D>();
        static readonly Dictionary<Color, Material> _metal = new Dictionary<Color, Material>();
        static readonly Dictionary<Color, Material> _stucco = new Dictionary<Color, Material>();
        static readonly Dictionary<FieldKind, Material> _field = new Dictionary<FieldKind, Material>();
        static Material _water;
        static Texture2D _metalAlb, _metalNrm, _stuccoAlb, _stuccoNrm;

        /// <summary>Classic red face brick used when the tint is (near) white.</summary>
        public static readonly Color DefaultBrick = new Color(0.56f, 0.25f, 0.18f);

        // ------------------------------------------------------------------ public API

        /// <summary>Running-bond modular brick. tint ≈ white → classic red brick; any other colour is used as the brick
        /// body colour itself (mortar stays light grey). Mortar is recessed in the normal map.</summary>
        public static Material BrickWall(Color tint)
        {
            if (_brick.TryGetValue(tint, out var m) && m != null) return m;
            BrickBase();
            Color target = (tint.r > 0.96f && tint.g > 0.96f && tint.b > 0.96f) ? DefaultBrick * tint : tint;
            target.a = 1f;
            if (!_brickAlb.TryGetValue(target, out var alb) || alb == null)
            {
                alb = BrickAlbedo(target);
                _brickAlb[target] = alb;
            }
            m = Make("MapBrick", alb, _bNrm, 1f, 0.1f, 0f, BrickTile, Color.white);
            _brick[tint] = m;
            return m;
        }

        /// <summary>Painted R-panel (base near white, colour = paint).</summary>
        public static Material MetalPanel(Color c)
        {
            if (_metal.TryGetValue(c, out var m) && m != null) return m;
            if (_metalAlb == null) MetalBase();
            m = Make("MapMetalPanel", _metalAlb, _metalNrm, 1f, 0.5f, 0.15f, MetalTile, c);
            _metal[c] = m;
            return m;
        }

        /// <summary>Sand-finish stucco (base near white, colour = paint).</summary>
        public static Material Stucco(Color c)
        {
            if (_stucco.TryGetValue(c, out var m) && m != null) return m;
            if (_stuccoAlb == null) StuccoBase();
            m = Make("MapStucco", _stuccoAlb, _stuccoNrm, 1f, 0.06f, 0f, StuccoTile, c);
            _stucco[c] = m;
            return m;
        }

        public static Material Field(FieldKind k)
        {
            if (_field.TryGetValue(k, out var m) && m != null) return m;
            m = FieldMaterial(k);
            _field[k] = m;
            return m;
        }

        public static Material Water()
        {
            if (_water != null) return _water;
            _water = WaterMaterial();
            return _water;
        }

        // ------------------------------------------------------------------ noise (periodic)

        static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }
        static float H01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777215f;

        static int Wrap(int i, int p) { i %= p; return i < 0 ? i + p : i; }

        /// <summary>Periodic value noise, periods px / py cells.</summary>
        static float Value(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int xa = Wrap(x0, px), xb = Wrap(x0 + 1, px), ya = Wrap(y0, py), yb = Wrap(y0 + 1, py);
            float a = H01(xa, ya, seed), b = H01(xb, ya, seed), c = H01(xa, yb, seed), d = H01(xb, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Periodic fBm on u, v in 0..1 (tile).</summary>
        static float Fbm(float u, float v, int cells, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(u * cells, v * cells, cells, cells, seed + o * 101) * amp;
                norm += amp; amp *= 0.5f; cells *= 2;
            }
            return sum / norm;
        }

        /// <summary>Periodic Worley F1/F2 (cell units).</summary>
        static void Worley(float u, float v, int cells, int seed, out float f1, out float f2, out uint id)
        {
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            f1 = 9f; f2 = 9f; id = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j;
                    int wx = Wrap(gx, cells), wy = Wrap(gy, cells);
                    float px = gx + H01(wx, wy, seed), py = gy + H01(wx, wy, seed + 7);
                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < f1) { f2 = f1; f1 = d; id = Hash(wx, wy, seed + 3); }
                    else if (d < f2) f2 = d;
                }
        }

        static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);
        static float S(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        // ------------------------------------------------------------------ canvas (colour + height, wrapped stamping)

        sealed class Cv
        {
            public readonly int N;
            public readonly Color[] Col;
            public readonly float[] H;
            public Cv(int n) { N = n; Col = new Color[n * n]; H = new float[n * n]; }

            /// <summary>Oriented soft ellipse-ish stamp (leaf / strand / blade). Centre and sizes in pixels, wraps around.</summary>
            public void Stamp(float cx, float cy, float len, float wid, float ang, Color c, float hgt, float alpha = 1f)
            {
                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                float hl = Mathf.Max(0.6f, len * 0.5f), hw = Mathf.Max(0.6f, wid * 0.5f);
                float ext = Mathf.Max(hl, hw) + 1f;
                int x0 = Mathf.FloorToInt(cx - ext), x1 = Mathf.CeilToInt(cx + ext);
                int y0 = Mathf.FloorToInt(cy - ext), y1 = Mathf.CeilToInt(cy + ext);
                for (int y = y0; y <= y1; y++)
                {
                    float dy = y + 0.5f - cy;
                    int row = Wrap(y, N) * N;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - cx;
                        float a = dx * ca + dy * sa, b = -dx * sa + dy * ca;
                        float ta = Mathf.Abs(a) / hl, tb = Mathf.Abs(b) / hw;
                        if (ta >= 1f || tb >= 1f) continue;
                        float w = (1f - ta * ta) * (1f - tb * tb) * alpha;
                        int i = row + Wrap(x, N);
                        Col[i] = Color.Lerp(Col[i], c, Mathf.Clamp01(w * 1.8f));
                        H[i] = Mathf.Max(H[i], hgt * (1f - tb * tb) * (0.6f + 0.4f * (1f - ta * ta)));
                    }
                }
            }
        }

        // ------------------------------------------------------------------ texture / material assembly

        static Texture2D Albedo(string name, Color[] px, int n)
        {
            for (int i = 0; i < px.Length; i++) px[i].a = 1f;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, false)
            {
                name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8,
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>Tangent-space normal map from a wrapped height field. k = slope per height unit per 2 pixels.</summary>
        static Texture2D Normal(string name, float[] h, int n, float k)
        {
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                int ym = Wrap(y - 1, n) * n, yp = Wrap(y + 1, n) * n, yr = y * n;
                for (int x = 0; x < n; x++)
                {
                    int xm = Wrap(x - 1, n), xp = Wrap(x + 1, n);
                    float dx = (h[yr + xp] - h[yr + xm]) * k, dy = (h[yp + x] - h[ym + x]) * k;
                    var v = new Vector3(-dx, -dy, 1f).normalized;
                    px[yr + x] = new Color(v.x * 0.5f + 0.5f, v.y * 0.5f + 0.5f, v.z * 0.5f + 0.5f, 1f);
                }
            }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, true)
            {
                name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4,
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        static Material Make(string name, Texture2D alb, Texture2D nrm, float bump, float gloss, float metal, float tile, Color color)
        {
            var shader = Shader.Find("Standard");
            var m = new Material(shader) { name = name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.mainTexture = alb;
            if (nrm != null)
            {
                m.SetTexture("_BumpMap", nrm);
                m.SetFloat("_BumpScale", bump);
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetFloat("_Glossiness", gloss);
            m.SetFloat("_Metallic", metal);
            m.mainTextureScale = Vector2.one / tile;
            m.color = color;
            m.enableInstancing = true;
            return m;
        }

        // ------------------------------------------------------------------ brick

        const int BN = 512;
        static float[] _bMask, _bVar, _bHue, _bMor;
        static Texture2D _bNrm;

        /// <summary>Shared brick layout (mask, per-pixel variation) and normal map; albedo is made per brick colour.</summary>
        static void BrickBase()
        {
            if (_bMask != null && _bNrm != null) return;
            int n = BN;
            float ppi = n / 48f;                     // pixels per inch
            const float course = 8f / 3f;            // 2 5/8"-ish course module (3 courses = 8")
            const float brickH = 2.25f, brickL = 7.625f, module = 8f;
            _bMask = new float[n * n]; _bVar = new float[n * n]; _bHue = new float[n * n]; _bMor = new float[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                float Y = (y + 0.5f) / ppi;
                int crs = Mathf.FloorToInt(Y / course);
                float yc = Y - crs * course;
                float off = (crs & 1) == 1 ? module * 0.5f : 0f;
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float X = (x + 0.5f) / ppi + off;
                    int col = Mathf.FloorToInt(X / module);
                    float xc = X - col * module;
                    int colW = Wrap(col, 6);
                    float d = Mathf.Min(Mathf.Min(yc, brickH - yc), Mathf.Min(xc, brickL - xc)); // inches to nearest joint, <0 in mortar
                    float mask = S(-0.04f, 0.07f, d);
                    uint id = Hash(colW, crs, 7);
                    float r1 = (id & 1023) / 1023f, r2 = ((id >> 10) & 1023) / 1023f, r3 = ((id >> 20) & 1023) / 1023f;
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float grain = H01(x, y, 11);
                    float mott = Value(u * 48f, v * 48f, 48, 48, 12) * 0.6f + Value(u * 96f, v * 96f, 96, 96, 13) * 0.4f;
                    float bv = (0.82f + 0.3f * r1) * (0.9f + 0.18f * mott) * (0.93f + 0.12f * grain);
                    if (r3 > 0.9f) bv *= 0.7f;                     // dark-fired / flashed brick
                    else if (r3 < 0.06f) bv *= 1.12f;              // light one
                    if (H01(x, y, 14) > 0.986f) bv *= 0.55f;       // iron speckle
                    // darker near the arrises (sand-struck edges)
                    bv *= 0.9f + 0.1f * S(0f, 0.35f, d);
                    _bMask[i] = mask;
                    _bVar[i] = bv;
                    _bHue[i] = (r2 - 0.5f) * 0.14f + (mott - 0.5f) * 0.05f;
                    _bMor[i] = 0.86f + 0.2f * H01(x, y, 15) + 0.06f * mott;
                    // height (metres): brick face slightly uneven per brick, rounded arris, tooled concave mortar recess
                    float face = 0.0004f * grain + 0.0006f * mott + (r1 - 0.5f) * 0.0006f;
                    float jointDepth = -0.0045f + 0.0012f * S(-0.2f, 0f, d) + 0.0004f * H01(x, y, 16);
                    h[i] = Mathf.Lerp(jointDepth, face, mask);
                }
            }
            float pm = BrickTile / n;
            _bNrm = Normal("MapBrick_N", h, n, 0.55f / (2f * pm));
        }

        static Texture2D BrickAlbedo(Color T)
        {
            int n = BN;
            var px = new Color[n * n];
            var mortar = new Color(0.70f, 0.68f, 0.64f);
            for (int i = 0; i < px.Length; i++)
            {
                float v = _bVar[i], hue = _bHue[i];
                var brick = new Color(T.r * v * (1f + hue), T.g * v * (1f - hue * 0.4f), T.b * v * (1f - hue * 0.8f), 1f);
                var mo = mortar * _bMor[i];
                px[i] = Color.Lerp(mo, brick, _bMask[i]);
            }
            return Albedo("MapBrick_A", px, n);
        }

        // ------------------------------------------------------------------ R-panel

        static void MetalBase()
        {
            const int n = 256;
            float ppi = n / 36f;
            var prof = new float[n];      // height (m) across U
            var occl = new float[n];
            for (int x = 0; x < n; x++)
            {
                float X = (x + 0.5f) / ppi;
                float p = X % 12f;
                float d = Mathf.Min(p, 12f - p);           // inches from major rib centre
                float hMaj = d < 0.5f ? 1.25f : d < 1.5f ? 1.25f * (1.5f - d) : 0f;
                float dm = Mathf.Min(Mathf.Abs(p - 4f), Mathf.Abs(p - 8f));
                float hMin = dm < 0.15f ? 0.12f : dm < 0.45f ? 0.12f * (0.45f - dm) / 0.3f : 0f;
                prof[x] = (hMaj + hMin) * 0.0254f;
                occl[x] = (d > 1.2f && d < 2.0f) ? 0.93f : 1f;  // slight dirt line at the rib foot
            }
            var h = new float[n * n];
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float streak = Value(u * 64f, v * 4f, 64, 4, 41);   // faint vertical streaking
                    float n1 = Fbm(u, v, 4, 3, 42);
                    float g = H01(x, y, 43);
                    h[i] = prof[x] + (n1 - 0.5f) * 0.0004f;
                    float l = (0.9f + 0.05f * n1 + 0.03f * streak + 0.015f * g) * occl[x];
                    px[i] = new Color(l, l, l * 0.99f, 1f);
                }
            _metalAlb = Albedo("MapMetal_A", px, n);
            _metalNrm = Normal("MapMetal_N", h, n, 1f / (2f * (MetalTile / n)));
        }

        // ------------------------------------------------------------------ stucco

        static void StuccoBase()
        {
            const int n = 256;
            var h = new float[n * n];
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float sand = Value(u * 128f, v * 128f, 128, 128, 51);
                    float g = H01(x, y, 52);
                    float und = Fbm(u, v, 4, 3, 53);
                    float blot = Fbm(u, v, 3, 2, 54);
                    h[i] = sand * 0.0011f + g * 0.0006f + und * 0.002f;
                    float l = 0.86f + 0.05f * sand + 0.04f * g + 0.04f * (blot - 0.5f);
                    if (g > 0.993f) l *= 0.85f;
                    px[i] = new Color(l, l * 0.995f, l * 0.98f, 1f);
                }
            _stuccoAlb = Albedo("MapStucco_A", px, n);
            _stuccoNrm = Normal("MapStucco_N", h, n, 1f / (2f * (StuccoTile / n)));
        }

        // ------------------------------------------------------------------ fields

        static void Soil(Cv c, int seed, Color dark, Color light)
        {
            int n = c.N;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float f = Fbm(u, v, 8, 3, seed), g = H01(x, y, seed + 1);
                    c.Col[y * n + x] = Color.Lerp(dark, light, f * 0.75f + g * 0.25f);
                    c.H[y * n + x] = f * 0.2f + g * 0.08f;
                }
        }

        /// <summary>Darkens the ground near the plant rows (canopy shade).</summary>
        static void RowShade(Cv c, int rows, float sigma, float amount)
        {
            int n = c.N; float rp = n / (float)rows;
            for (int y = 0; y < n; y++)
            {
                float t = ((y + 0.5f) % rp) - rp * 0.5f;
                float k = 1f - amount * Mathf.Exp(-(t * t) / (sigma * sigma));
                for (int x = 0; x < n; x++) { int i = y * n + x; c.Col[i] *= k; c.Col[i].a = 1f; }
            }
        }

        static Material FieldMaterial(FieldKind k)
        {
            const int n = 256;
            var c = new Cv(n);
            var rng = new System.Random(500 + (int)k * 31);
            float R() => (float)rng.NextDouble();
            float strength = 1f, gloss = 0.12f;
            switch (k)
            {
                case FieldKind.Corn:
                {
                    // 4 rows x 10'; mid-summer: tall plants, arching leaves, dark soil still visible between rows
                    Soil(c, 301, C(52, 40, 30), C(84, 64, 46));
                    RowShade(c, 4, 22f, 0.45f);
                    float rp = n / 4f;
                    for (int r = 0; r < 4; r++)
                    {
                        float yc = (r + 0.5f) * rp;
                        const int plants = 17;                       // ~7" in-row spacing
                        for (int p = 0; p < plants; p++)
                        {
                            float xc = (p + 0.5f) * n / plants + (R() - 0.5f) * 3f, ycc = yc + (R() - 0.5f) * 2f;
                            int leaves = 9 + rng.Next(4);
                            for (int l = 0; l < leaves; l++)
                            {
                                float side = (l & 1) == 0 ? 1f : -1f;
                                float ang = side * Mathf.PI * 0.5f + (R() - 0.5f) * 1.7f;
                                float len = 12f + R() * 15f, wid = 3.2f + R() * 1.6f;
                                float sy = Mathf.Abs(Mathf.Sin(ang));
                                if (sy * len > 25f) len = 25f / sy;
                                var col = Color.Lerp(C(36, 82, 28), C(100, 150, 56), R()) * (0.8f + 0.3f * R()); col.a = 1f;
                                c.Stamp(xc + Mathf.Cos(ang) * len * 0.5f, ycc + Mathf.Sin(ang) * len * 0.5f, len, wid, ang, col, 0.55f + 0.45f * R());
                            }
                            var tas = C(178, 160, 98) * (0.85f + 0.2f * R()); tas.a = 1f;
                            c.Stamp(xc, ycc, 4.5f, 3f, R() * 6.28f, tas, 1f);   // tassel
                        }
                    }
                    strength = 1.4f; gloss = 0.18f;
                    break;
                }
                case FieldKind.Soybeans:
                {
                    Soil(c, 311, C(50, 38, 28), C(80, 60, 44));
                    RowShade(c, 4, 26f, 0.55f);
                    float rp = n / 4f;
                    for (int r = 0; r < 4; r++)
                    {
                        float yc = (r + 0.5f) * rp;
                        for (int i = 0; i < 640; i++)
                        {
                            float off = (R() + R() + R() - 1.5f) / 1.5f * 27f;
                            float closeness = 1f - Mathf.Abs(off) / 28f;
                            var col = Color.Lerp(C(28, 66, 22), C(70, 116, 40), R() * 0.7f + closeness * 0.3f) * (0.8f + 0.25f * R()); col.a = 1f;
                            c.Stamp(R() * n, yc + off, 5f + R() * 3f, 3.5f + R() * 2.5f, R() * 6.28f, col, 0.4f + 0.6f * closeness);
                        }
                    }
                    strength = 1.6f; gloss = 0.2f;
                    break;
                }
                case FieldKind.Wheat:
                {
                    // ripe wheat, 7.5" drill rows (16 per 10' tile) faintly visible
                    for (int y = 0; y < n; y++)
                    {
                        float ph = ((y + 0.5f) / n * 16f) % 1f;
                        float drill = 0.9f + 0.1f * (0.5f + 0.5f * Mathf.Cos(ph * Mathf.PI * 2f));
                        for (int x = 0; x < n; x++)
                        {
                            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                            float f = Fbm(u, v, 4, 3, 321), g = H01(x, y, 322);
                            var col = Color.Lerp(C(160, 124, 62), C(214, 180, 104), f * 0.6f + g * 0.4f) * drill; col.a = 1f;
                            c.Col[y * n + x] = col;
                            c.H[y * n + x] = g * 0.3f + f * 0.2f;
                        }
                    }
                    for (int i = 0; i < 5200; i++)
                    {
                        var col = Color.Lerp(C(178, 140, 70), C(232, 202, 128), R()) * (0.85f + 0.2f * R()); col.a = 1f;
                        float row = rng.Next(16), y = (row + 0.5f) * n / 16f + (R() - 0.5f) * 9f;
                        c.Stamp(R() * n, y, 3f + R() * 2.5f, 1.6f + R() * 0.8f, (R() - 0.5f) * 2.2f, col, 0.6f + 0.4f * R());
                    }
                    for (int i = 0; i < 90; i++)
                        c.Stamp(R() * n, R() * n, 3f, 2f, R() * 6.28f, C(96, 98, 52), 0.5f);   // the odd green weed
                    strength = 1.6f; gloss = 0.1f;
                    break;
                }
                case FieldKind.Plowed:
                {
                    // 15" moldboard furrows along U, cloddy soil, a little stalk residue
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                            float warp = (Fbm(u, v, 2, 2, 331) - 0.5f) * 0.03f;
                            float f = ((v + warp) * 8f) % 1f; if (f < 0f) f += 1f;
                            float fp = f < 0.65f ? f / 0.65f * 0.5f : 0.5f + (f - 0.65f) / 0.35f * 0.5f;   // asymmetric turned slice
                            float ridge = 0.5f - 0.5f * Mathf.Cos(fp * Mathf.PI * 2f);
                            Worley(u, v, 40, 332, out float f1, out float f2, out uint id);
                            float clod = Mathf.Clamp01((f2 - f1) * 3f);
                            float nn = Fbm(u, v, 8, 3, 333), g = H01(x, y, 334);
                            float h = ridge * 1.0f + clod * 0.35f + nn * 0.25f;
                            c.H[y * n + x] = h;
                            var col = Color.Lerp(C(58, 42, 30), C(124, 94, 66), Mathf.Clamp01(ridge * 0.55f + clod * 0.15f + nn * 0.3f)) * (0.9f + 0.15f * g);
                            if ((id & 15) == 0) col *= 0.88f;
                            col.a = 1f;
                            c.Col[y * n + x] = col;
                        }
                    for (int i = 0; i < 140; i++)
                    {
                        var col = Color.Lerp(C(150, 132, 96), C(196, 178, 136), R()); col.a = 1f;
                        c.Stamp(R() * n, R() * n, 6f + R() * 10f, 1.4f, R() * 6.28f, col, 1.1f, 0.8f);
                    }
                    strength = 1.2f; gloss = 0.08f;
                    break;
                }
                case FieldKind.Hay:
                {
                    // 20' tile: cut stubble with mower streaks along U, one raked windrow across the middle
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                            float streak = Value(u * 8f, v * 96f, 8, 96, 341);
                            float f = Fbm(u, v, 4, 3, 342), g = H01(x, y, 343);
                            var col = Color.Lerp(C(112, 128, 64), C(170, 168, 100), streak * 0.4f + f * 0.4f + g * 0.2f); col.a = 1f;
                            c.Col[y * n + x] = col;
                            c.H[y * n + x] = g * 0.25f + streak * 0.15f;
                        }
                    for (int i = 0; i < 3200; i++)
                    {
                        float off = (R() + R() - 1f) * 26f;                 // windrow ~1.2 m wide
                        float edge = (Fbm(R(), 0.5f, 3, 2, 344) - 0.5f) * 8f;
                        var col = Color.Lerp(C(136, 138, 76), C(200, 186, 118), R()) * (0.8f + 0.25f * R()); col.a = 1f;
                        c.Stamp(R() * n, n * 0.5f + off + edge, 9f + R() * 9f, 1.6f + R() * 0.8f, (R() - 0.5f) * 1.3f, col, 0.7f + 0.3f * R() - Mathf.Abs(off) / 60f);
                    }
                    strength = 1.4f; gloss = 0.1f;
                    break;
                }
                default: // Pasture
                {
                    // 20' tile: mottled olive grass, clover patches with flowers, worn dirt spots
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                            float f = Fbm(u, v, 3, 4, 351), g = H01(x, y, 352), fine = Value(u * 128f, v * 128f, 128, 128, 353);
                            var col = Color.Lerp(C(74, 98, 44), C(122, 132, 64), f * 0.7f + fine * 0.3f) * (0.88f + 0.18f * g);
                            Worley(u, v, 7, 354, out float f1, out _, out uint id);
                            if ((id & 3) == 0 && f1 < 0.32f)
                            {
                                float t = S(0.32f, 0.2f, f1);
                                col = Color.Lerp(col, C(62, 104, 58) * (0.9f + 0.2f * g), t * 0.8f);
                                if (g > 0.982f && t > 0.5f) col = C(232, 230, 214);
                            }
                            float dirt = Fbm(u, v, 4, 3, 355);
                            float dm = S(0.66f, 0.76f, dirt);
                            col = Color.Lerp(col, C(112, 92, 64) * (0.85f + 0.25f * g), dm * 0.85f);
                            col.a = 1f;
                            c.Col[y * n + x] = col;
                            c.H[y * n + x] = (1f - dm) * (g * 0.35f + fine * 0.3f);
                        }
                    for (int i = 0; i < 2600; i++)
                    {
                        var col = Color.Lerp(C(80, 104, 44), C(150, 152, 82), R()); col.a = 1f;
                        c.Stamp(R() * n, R() * n, 3f + R() * 3f, 1f, R() * 6.28f, col, 0.5f, 0.7f);
                    }
                    strength = 1.2f; gloss = 0.14f;
                    break;
                }
            }
            var alb = Albedo("MapField_" + k + "_A", c.Col, n);
            var nrm = Normal("MapField_" + k + "_N", c.H, n, strength);
            return Make("MapField_" + k, alb, nrm, 1f, gloss, 0f, FieldTile(k), Color.white);
        }

        // ------------------------------------------------------------------ water

        static Material WaterMaterial()
        {
            const int n = 256;
            var h = new float[n * n];
            var px = new Color[n * n];
            // integer wave vectors keep the ripple seamless
            int[] kx = { 3, -2, 5, 1, 7, -6, 9 }, ky = { 1, 4, -3, 6, 2, 5, -7 };
            float[] amp = { 0.010f, 0.008f, 0.006f, 0.005f, 0.003f, 0.003f, 0.002f };
            float[] phs = { 0.3f, 1.7f, 4.1f, 2.6f, 5.2f, 0.9f, 3.3f };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float hh = 0f;
                    for (int w = 0; w < kx.Length; w++) hh += amp[w] * Mathf.Sin((kx[w] * u + ky[w] * v) * Mathf.PI * 2f + phs[w]);
                    float f = Fbm(u, v, 8, 3, 361);
                    hh += (f - 0.5f) * 0.004f;
                    int i = y * n + x;
                    h[i] = hh;
                    float big = Fbm(u, v, 2, 2, 362);
                    var col = Color.Lerp(C(22, 38, 40), C(36, 56, 54), big * 0.7f + f * 0.3f);
                    px[i] = col;
                }
            var alb = Albedo("MapWater_A", px, n);
            var nrm = Normal("MapWater_N", h, n, 1f / (2f * (WaterTile / n)));
            return Make("MapWater", alb, nrm, 0.6f, 0.93f, 0f, WaterTile, Color.white);
        }
    }
}
