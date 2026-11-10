using UnityEngine;

namespace Groundbreak
{
    /// <summary>Procedural roofing materials (built once, cached by ShellMaterials). UVs are meters.</summary>
    public static class RoofTextures
    {
        static float H(int x, int y, int s) => ShellMaterials.Hash(x, y, s);
        static float N(float x, float y, int cell, int period, int s) => ShellMaterials.Noise(x, y, cell, period, s);

        // Weathered-wood blend: charcoal, warm gray-brown, tan, a few light flecks.
        static readonly Color[] WWLight = { new Color(0.36f, 0.33f, 0.29f), new Color(0.47f, 0.42f, 0.35f), new Color(0.29f, 0.26f, 0.22f), new Color(0.40f, 0.38f, 0.35f) };
        static readonly Color[] WWDark = { new Color(0.13f, 0.13f, 0.13f), new Color(0.19f, 0.18f, 0.17f), new Color(0.24f, 0.22f, 0.20f), new Color(0.10f, 0.10f, 0.105f) };

        /// <summary>Tile size of the shingle granule texture; per-tab UV offsets pick different blend patches inside it.</summary>
        public const float ShingleTile = 0.5f;

        /// <summary>Granule-surfaced asphalt shingle. darkness 0 = top laminate, 1 = shadow-band lower laminate.</summary>
        static Material Granules(string key, float darkness, int seed)
        {
            if (ShellMaterials.TryGet(key, out var m)) return m;
            const int n = 512;
            var col = new Color[n * n]; var h = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // large blend patches (a tab-sized area gets its own mix), medium mottling
                    float patch = N(x, y, 128, n / 128, seed) * 0.6f + N(x, y, 64, n / 64, seed + 1) * 0.4f;
                    float mott = N(x, y, 8, n / 8, seed + 2);
                    float dark = Mathf.Clamp01(0.32f + (patch - 0.5f) * 1.1f + darkness * 0.55f + (mott - 0.5f) * 0.12f);
                    // granules ~1-1.5 mm: 2x2 pixel clusters with per-granule color picks
                    int gx = x >> 1, gy = y >> 1;
                    float pick = H(gx + (y & 1) * 7, gy, seed + 3);
                    float r2 = H(gx, gy + 911, seed + 4);
                    Color c;
                    if (pick < dark) c = WWDark[(int)(r2 * WWDark.Length) % WWDark.Length];
                    else c = WWLight[(int)(r2 * WWLight.Length) % WWLight.Length];
                    float fleck = H(x, y, seed + 5);
                    if (fleck > 0.992f - darkness * 0.006f) c = new Color(0.62f, 0.60f, 0.56f);      // light ceramic granule
                    else if (fleck < 0.012f) c = new Color(0.40f, 0.30f, 0.20f);                       // rusty-brown granule
                    float lum = 0.86f + H(x, y, seed + 6) * 0.26f;
                    lum *= 1f - darkness * 0.30f;
                    c = new Color(c.r * lum, c.g * lum, c.b * lum, 0.35f + H(x, y, seed + 7) * 0.65f);
                    col[y * n + x] = c;
                    // height: rounded granules with pits between them
                    float g = H(gx, gy, seed + 8);
                    int lx = x & 1, ly = y & 1;
                    float bump = (lx == 0 && ly == 0) ? 1f : 0.75f;
                    h[y * n + x] = g * 0.55f * bump + H(x, y, seed + 9) * 0.25f + mott * 0.2f;
                }
            return ShellMaterials.Make(key, col, h, n, ShingleTile, 0.16f, 0.85f, 3.0f);
        }

        public static Material Shingle => Granules("roof-shingle-ww", 0f, 11);
        public static Material ShingleShadow => Granules("roof-shingle-ww-shadow", 1f, 23);

        /// <summary>Asphalt edge / butt / back of shingles: black with a few granules.</summary>
        public static Material AsphaltEdge
        {
            get
            {
                const string key = "roof-asphalt-edge";
                if (ShellMaterials.TryGet(key, out var m)) return m;
                const int n = 128;
                var col = new Color[n * n]; var h = new float[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float g = H(x, y, 41);
                        float v = 0.06f + g * 0.05f + (g > 0.93f ? 0.12f : 0f);
                        col[y * n + x] = new Color(v, v * 0.98f, v * 0.95f, 0.5f + g * 0.4f);
                        h[y * n + x] = g;
                    }
                return ShellMaterials.Make(key, col, h, n, 0.06f, 0.28f, 0.4f, 1.5f);
            }
        }

        /// <summary>Synthetic underlayment: light gray woven polypropylene with printed lap lines and dashes.</summary>
        public static Material Underlayment
        {
            get
            {
                const string key = "roof-underlayment";
                if (ShellMaterials.TryGet(key, out var m)) return m;
                const int n = 512;                 // tile = 24" -> ~1.2 mm per pixel
                const float tile = 0.6096f;
                var col = new Color[n * n]; var h = new float[n * n];
                float px = tile / n;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        // woven tape: alternating over/under strands 3 px wide
                        int sx = x / 3, sy = y / 3;
                        bool warp = ((sx + sy) & 1) == 0;
                        float along = warp ? (x % 3) / 2f : (y % 3) / 2f;
                        float weave = 0.85f + 0.15f * Mathf.Sin(along * Mathf.PI);
                        float noise = 0.94f + N(x, y, 16, n / 16, 51) * 0.1f + H(x, y, 52) * 0.04f;
                        float g = 0.66f * weave * noise;
                        Color c = new Color(g, g * 1.01f, g * 1.04f, warp ? 0.55f : 0.4f);
                        float vIn = y * px / Units.Inch;     // inches up the roll within the tile
                        float uIn = x * px / Units.Inch;
                        // printed lap guide lines at 4" and 6" (dark blue-gray), plus a center print band with dashes
                        bool line = Mathf.Abs(vIn - 4f) < 0.06f || Mathf.Abs(vIn - 6f) < 0.04f || Mathf.Abs(vIn - 20f) < 0.04f;
                        bool dash = Mathf.Abs(vIn - 13f) < 0.55f && (uIn % 12f) < 5.5f && (uIn % 12f) > 0.5f;
                        bool dashText = dash && ((int)((uIn % 12f) * 3f) % 2 == 0) && Mathf.Abs(vIn - 13f) < 0.3f;
                        bool nailMark = (Mathf.Abs(vIn - 2f) < 0.18f || Mathf.Abs(vIn - 22f) < 0.18f) && Mathf.Abs((uIn % 12f) - 6f) < 0.18f;
                        if (line) c = new Color(0.22f, 0.30f, 0.42f, 0.4f);
                        else if (dashText) c = new Color(0.18f, 0.26f, 0.40f, 0.4f);
                        else if (dash) c = Color.Lerp(c, new Color(0.55f, 0.58f, 0.66f), 0.3f);
                        if (nailMark) c = new Color(0.85f, 0.85f, 0.88f, 0.4f);
                        col[y * n + x] = c;
                        h[y * n + x] = weave * 0.6f + H(x, y, 53) * 0.15f;
                    }
                return ShellMaterials.Make(key, col, h, n, tile, 0.35f, 0.5f, 1.5f);
            }
        }

        /// <summary>Ice &amp; water shield: black rubberized membrane with a fine granular / film top.</summary>
        public static Material IceWater
        {
            get
            {
                const string key = "roof-icewater";
                if (ShellMaterials.TryGet(key, out var m)) return m;
                const int n = 256;
                var col = new Color[n * n]; var h = new float[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float g = H(x, y, 61);
                        float v = 0.075f + g * 0.04f + N(x, y, 32, n / 32, 62) * 0.03f;
                        bool print = Mathf.Abs(y - n * 0.5f) < 1.2f && (x % 64) < 40;
                        if (print) v = 0.22f;
                        col[y * n + x] = new Color(v, v, v * 1.03f, 0.45f + g * 0.3f);
                        h[y * n + x] = g * 0.8f;
                    }
                return ShellMaterials.Make(key, col, h, n, 0.3f, 0.30f, 0.35f, 1.5f);
            }
        }

        /// <summary>Black plastic ridge vent (top: slight texture, louver faces get a slotted look).</summary>
        public static Material RidgeVent
        {
            get
            {
                const string key = "roof-ridgevent";
                if (ShellMaterials.TryGet(key, out var m)) return m;
                const int n = 128;
                var col = new Color[n * n]; var h = new float[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        bool slot = (x % 8) < 3;
                        float v = slot ? 0.03f : 0.11f + H(x, y, 71) * 0.03f;
                        col[y * n + x] = new Color(v, v, v, slot ? 0.1f : 0.6f);
                        h[y * n + x] = slot ? 0f : 1f;
                    }
                return ShellMaterials.Make(key, col, h, n, 0.1f, 0.35f, 0.6f, 2f);
            }
        }

        public static Material DripEdge => Materials.Get(SurfaceKind.PaintedWhite);
        public static Material Flashing => Materials.Get(SurfaceKind.Galvanized);
        public static Material CapStaple => ShellMaterials.Flat("roof-capstaple", new Color(0.86f, 0.86f, 0.84f), 0.4f, 0.03f, 0.1f);
        public static Material Sealant => ShellMaterials.Flat("roof-sealant", new Color(0.05f, 0.05f, 0.05f), 0.55f, 0.03f, 0.1f);
    }
}
