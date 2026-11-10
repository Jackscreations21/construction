using UnityEngine;

namespace Groundbreak
{
    /// <summary>Procedural looks for interior finish (trim paint, wood floors, LVP prints, tile, grout, thinset,
    /// countertops, cabinets). All registered in <see cref="FinishMaterials"/> under "fin-*" keys; textures are seamless and
    /// tile in meters. Wood textures run their grain along texture U (MeshBuilder's uHint / extrusion axis).</summary>
    public static class InteriorMaterials
    {
        public const string Primed = "fin-primed", OakTrim = "fin-oak-trim", Cedar = "fin-cedar";
        public const string LvpPrefix = "fin-lvp-", HardwoodPrefix = "fin-hw-";
        public const string Porcelain = "fin-porcelain", Subway = "fin-subway", Grout = "fin-grout", Thinset = "fin-thinset";
        public const string CementBoard = "fin-cement-board", Rosin = "fin-rosin", Underlay = "fin-underlay", Spacer = "fin-spacer";
        public const string Granite = "fin-granite", Quartz = "fin-quartz", Laminate = "fin-laminate", Butcher = "fin-butcher";
        public const string CabinetBox = "fin-cab-box", WallFace = "fin-wall-paint";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // Semi-gloss primed + painted trim (MDF / finger-joint pine): very smooth, a touch of brush texture along U.
            FinishMaterials.Register(Primed, new FinishMaterials.Spec { Gen = () => Painted(FinishNoise.C(242, 241, 236), 4101), Tile = 0.6f, Gloss = 0.62f, Bump = 0.15f, NormalStrength = 1f });
            FinishMaterials.Register(OakTrim, new FinishMaterials.Spec { Gen = () => Wood(FinishNoise.C(196, 146, 92), FinishNoise.C(132, 84, 46), 4111, 7f, 0.55f, true), Tile = 0.6f, Gloss = 0.45f, Bump = 0.5f });
            FinishMaterials.Register(Cedar, new FinishMaterials.Spec { Gen = () => Wood(FinishNoise.C(214, 160, 118), FinishNoise.C(160, 96, 66), 4121, 5f, 0.25f, false), Tile = 0.3f, Gloss = 0.1f, Bump = 0.7f });
            // LVP prints (wood-look vinyl with a satin wear layer).
            FinishMaterials.Register(LvpPrefix + "oak", new FinishMaterials.Spec { Gen = () => Wood(FinishNoise.C(196, 160, 118), FinishNoise.C(128, 94, 62), 4131, 5.5f, 0.35f, false), Tile = 1.2192f, Gloss = 0.42f, Bump = 0.35f });
            FinishMaterials.Register(LvpPrefix + "gray", new FinishMaterials.Spec { Gen = () => Wood(FinishNoise.C(170, 162, 150), FinishNoise.C(104, 98, 90), 4141, 5.5f, 0.35f, false), Tile = 1.2192f, Gloss = 0.4f, Bump = 0.35f });
            // Solid red oak strip, natural with satin poly.
            FinishMaterials.Register(HardwoodPrefix + "redoak", new FinishMaterials.Spec { Gen = () => Wood(FinishNoise.C(160, 116, 84), FinishNoise.C(112, 72, 48), 4151, 9f, 0.7f, true), Tile = 0.9144f, Gloss = 0.4f, Bump = 0.45f });
            FinishMaterials.Register(Porcelain, new FinishMaterials.Spec { Gen = PorcelainGen, Tile = 0.6096f, Gloss = 0.7f, Bump = 0.15f, NormalStrength = 1f });
            FinishMaterials.Register(Subway, new FinishMaterials.Spec { Gen = () => Painted(FinishNoise.C(244, 244, 240), 4161), Tile = 0.3f, Gloss = 0.9f, Bump = 0.25f, NormalStrength = 1.5f });
            FinishMaterials.Register(Grout, new FinishMaterials.Spec { Gen = () => Sanded(FinishNoise.C(255, 255, 255), 4171, 0.22f), Tile = 0.15f, Gloss = 0.08f, Bump = 0.8f });
            FinishMaterials.Register(Thinset, new FinishMaterials.Spec { Gen = ThinsetGen, Tile = 0.1016f, Gloss = 0.12f, Bump = 1.2f, NormalStrength = 3f });
            FinishMaterials.Register(CementBoard, new FinishMaterials.Spec { Gen = CementBoardGen, Tile = 0.3f, Gloss = 0.06f, Bump = 0.9f });
            FinishMaterials.Register(Rosin, new FinishMaterials.Spec { Gen = () => Sanded(FinishNoise.C(176, 86, 78), 4181, 0.12f), Tile = 0.5f, Gloss = 0.08f, Bump = 0.3f });
            FinishMaterials.Register(Underlay, new FinishMaterials.Spec { Gen = () => Sanded(FinishNoise.C(186, 206, 226), 4191, 0.08f), Tile = 0.3f, Gloss = 0.15f, Bump = 0.4f });
            FinishMaterials.Register(Spacer, new FinishMaterials.Spec { Gen = () => Painted(FinishNoise.C(250, 250, 250), 4201), Tile = 0.1f, Gloss = 0.5f, Bump = 0.1f });
            FinishMaterials.Register(Granite, new FinishMaterials.Spec { Gen = GraniteGen, Tile = 0.6f, Gloss = 0.85f, Bump = 0.08f, NormalStrength = 0.5f });
            FinishMaterials.Register(Quartz, new FinishMaterials.Spec { Gen = QuartzGen, Tile = 0.9f, Gloss = 0.8f, Bump = 0.05f, NormalStrength = 0.5f });
            FinishMaterials.Register(Laminate, new FinishMaterials.Spec { Gen = LaminateGen, Tile = 0.6f, Gloss = 0.35f, Bump = 0.1f });
            FinishMaterials.Register(Butcher, new FinishMaterials.Spec { Gen = ButcherGen, Tile = 0.6096f, Gloss = 0.4f, Bump = 0.35f });
            FinishMaterials.Register(CabinetBox, new FinishMaterials.Spec { Gen = () => Wood(FinishNoise.C(222, 196, 156), FinishNoise.C(186, 152, 108), 4211, 4f, 0.2f, false), Tile = 0.6f, Gloss = 0.3f, Bump = 0.2f });
            FinishMaterials.Register(WallFace, new FinishMaterials.Spec { Gen = () => Sanded(FinishNoise.C(232, 228, 218), 4221, 0.03f), Tile = 0.5f, Gloss = 0.12f, Bump = 0.25f });
        }

        /// <summary>Very smooth paint film with faint brush/roller streaks along U.</summary>
        public static FinishCanvas Painted(Color c, int seed)
        {
            const int n = 256; var cv = new FinishCanvas(n);
            var streak = FinishNoise.Field(n, 2, 48, 2, 0.5f, seed);
            var peel = FinishNoise.Field(n, 32, 32, 2, 0.5f, seed + 1);
            for (int i = 0; i < n * n; i++)
            {
                float s = streak[i] - 0.5f, p = peel[i] - 0.5f;
                cv.Set(i, c * (1f + s * 0.012f + p * 0.01f));
                cv.H[i] = s * 0.4f + p * 0.6f; cv.S[i] = 1f - Mathf.Abs(p) * 0.15f;
            }
            return cv;
        }

        /// <summary>Wood grain along U: growth-ring streaks (warped), pores, optional ray flecks (oak).</summary>
        public static FinishCanvas Wood(Color light, Color dark, int seed, float rings, float pores, bool rays)
        {
            const int n = 512; var cv = new FinishCanvas(n);
            var warp = FinishNoise.Field(n, 2, 4, 3, 0.5f, seed);
            var fine = FinishNoise.Field(n, 2, 160, 2, 0.6f, seed + 1);
            var pore = FinishNoise.Field(n, 8, 384, 1, 0.5f, seed + 2);
            var tone = FinishNoise.Field(n, 1, 6, 2, 0.5f, seed + 3);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float v = (float)y / n;
                    // growth rings as fine, nearly straight lines (flat-sawn boards seen edge-on), gentle drift
                    float ring = Mathf.Repeat(v * rings * 6f + (warp[i] - 0.5f) * 1.1f, 1f);
                    float late = FinishNoise.Smooth(0.6f, 0.82f, ring) * (1f - FinishNoise.Smooth(0.86f, 1f, ring));
                    float t = Mathf.Clamp01(late * 0.5f + (fine[i] - 0.5f) * 0.9f + (tone[i] - 0.5f) * 0.5f + 0.2f);
                    Color c = Color.Lerp(light, dark, t);
                    float pp = Mathf.Clamp01((pore[i] - 0.62f) * 4f) * pores;
                    c *= 1f - pp * 0.35f;
                    if (rays)
                    {
                        FinishNoise.Worley((float)x / n, v, 24, seed + 5, out float f1, out _, out int id);
                        if (FinishNoise.Hf(id, 3, seed) > 0.75f && f1 < 0.18f) c = Color.Lerp(c, light * 1.08f, 0.35f);
                    }
                    cv.Set(i, c);
                    cv.H[i] = -pp * 0.8f - late * 0.15f + (fine[i] - 0.5f) * 0.2f;
                    cv.S[i] = 1f - pp * 0.6f;
                }
            return cv;
        }

        /// <summary>Granular sanded surface (grout, paper, foam): base color with sand speckle.</summary>
        public static FinishCanvas Sanded(Color c, int seed, float amt)
        {
            const int n = 256; var cv = new FinishCanvas(n);
            var mac = FinishNoise.Field(n, 4, 4, 3, 0.5f, seed);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float h = FinishNoise.Hf(x, y, seed) - 0.5f;
                    cv.Set(i, c * (1f + h * amt + (mac[i] - 0.5f) * amt));
                    cv.H[i] = h; cv.S[i] = 0.6f + h * 0.4f;
                }
            return cv;
        }

        static FinishCanvas PorcelainGen()
        {
            const int n = 512; var cv = new FinishCanvas(n);
            var cloud = FinishNoise.Field(n, 3, 3, 5, 0.55f, 4301);
            var vein = FinishNoise.Field(n, 4, 4, 4, 0.5f, 4302);
            Color a = FinishNoise.C(206, 202, 194), b = FinishNoise.C(176, 170, 160);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float t = Mathf.Clamp01((cloud[i] - 0.35f) * 1.6f);
                    Color c = Color.Lerp(a, b, t);
                    float vv = Mathf.Abs(Mathf.Sin((vein[i] * 7f + (float)x / n * 2f) * Mathf.PI));
                    float vl = Mathf.Clamp01(1f - vv * 14f);
                    c = Color.Lerp(c, FinishNoise.C(150, 144, 136), vl * 0.35f);
                    float sp = FinishNoise.Hf(x, y, 4303) > 0.995f ? 0.85f : 1f;
                    cv.Set(i, c * sp);
                    cv.H[i] = (cloud[i] - 0.5f) * 0.1f; cv.S[i] = 1f;
                }
            return cv;
        }

        static FinishCanvas ThinsetGen()
        {
            const int n = 256; var cv = new FinishCanvas(n);
            var mac = FinishNoise.Field(n, 4, 4, 3, 0.5f, 4311);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    // 8 trowel ridges (1/4" ridge + 1/4" valley) per 4" tile, slightly wavy
                    float p = Mathf.Repeat((float)y / n * 8f + (mac[i] - 0.5f) * 0.3f, 1f);
                    float ridge = p < 0.5f ? 1f : 0f;
                    ridge = Mathf.Lerp(ridge, 0.5f, 0.15f);
                    float g = FinishNoise.Hf(x, y, 4312) - 0.5f;
                    cv.Set(i, FinishNoise.C(158, 158, 152) * (0.86f + ridge * 0.14f + g * 0.06f));
                    cv.H[i] = ridge + g * 0.1f; cv.S[i] = 0.7f;
                }
            return cv;
        }

        static FinishCanvas CementBoardGen()
        {
            const int n = 256; var cv = new FinishCanvas(n);
            var mac = FinishNoise.Field(n, 6, 6, 3, 0.5f, 4321);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float g = FinishNoise.Hf(x, y, 4322) - 0.5f;
                    bool mesh = (x % 16) < 1 || (y % 16) < 1;
                    cv.Set(i, FinishNoise.C(150, 150, 146) * (1f + g * 0.18f + (mac[i] - 0.5f) * 0.2f) * (mesh ? 0.93f : 1f));
                    cv.H[i] = g * 0.5f + (mesh ? 0.4f : 0f); cv.S[i] = 0.5f;
                }
            return cv;
        }

        static FinishCanvas GraniteGen()
        {
            const int n = 512; var cv = new FinishCanvas(n);
            Color[] pal = { FinishNoise.C(30, 30, 32), FinishNoise.C(92, 90, 88), FinishNoise.C(150, 146, 140), FinishNoise.C(214, 210, 204), FinishNoise.C(122, 78, 58) };
            float[] w = { 0.3f, 0.3f, 0.2f, 0.12f, 0.08f };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    FinishNoise.Worley((float)x / n, (float)y / n, 64, 4331, out float f1, out float f2, out int id);
                    float r = FinishNoise.Hf(id, 1, 4332); int k = 0; float acc = w[0];
                    while (r > acc && k < w.Length - 1) { k++; acc += w[k]; }
                    float edge = Mathf.Clamp01((f2 - f1) * 6f);
                    Color c = pal[k] * (0.9f + FinishNoise.Hf(id, 2, 4333) * 0.2f) * (0.85f + 0.15f * edge);
                    cv.Set(i, c); cv.H[i] = 0f; cv.S[i] = 0.9f + 0.1f * edge;
                }
            return cv;
        }

        static FinishCanvas QuartzGen()
        {
            const int n = 512; var cv = new FinishCanvas(n);
            var warp = FinishNoise.Field(n, 3, 3, 5, 0.55f, 4341);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float s = Mathf.Abs(Mathf.Sin(((float)(x + y) / n * 2f + warp[i] * 4f) * Mathf.PI));
                    float vein = Mathf.Clamp01(1f - s * 10f);
                    float sp = FinishNoise.Hf(x, y, 4342);
                    Color c = FinishNoise.C(240, 239, 236) * (sp > 0.985f ? 0.9f : 1f);
                    c = Color.Lerp(c, FinishNoise.C(160, 158, 156), vein * 0.5f);
                    cv.Set(i, c); cv.H[i] = 0f; cv.S[i] = 1f;
                }
            return cv;
        }

        static FinishCanvas LaminateGen()
        {
            const int n = 256; var cv = new FinishCanvas(n);
            var mac = FinishNoise.Field(n, 6, 6, 4, 0.55f, 4351);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float sp = FinishNoise.Hf(x, y, 4352);
                    Color c = Color.Lerp(FinishNoise.C(214, 204, 186), FinishNoise.C(160, 148, 128), Mathf.Clamp01((mac[i] - 0.3f) * 1.5f));
                    if (sp > 0.97f) c *= 0.7f;
                    cv.Set(i, c); cv.H[i] = sp * 0.2f; cv.S[i] = 1f;
                }
            return cv;
        }

        static FinishCanvas ButcherGen()
        {
            const int n = 512; var cv = new FinishCanvas(n);
            var wood = Wood(FinishNoise.C(226, 196, 150), FinishNoise.C(190, 150, 100), 4361, 6f, 0.2f, false);
            const int strips = 16; // 1-1/2" strips in a 24" tile
            for (int y = 0; y < n; y++)
            {
                int s = y * strips / n;
                float joint = FinishNoise.Hf(s, 0, 4362);
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float u = (float)x / n;
                    int seg = u < joint ? 0 : 1;
                    float tone = 0.86f + FinishNoise.Hf(s, seg, 4363) * 0.24f;
                    // sample the wood grain shifted per strip/segment so neighbours differ
                    int sy = (y + (int)(FinishNoise.Hf(s, seg, 4364) * n)) % n, sx = (x + (int)(FinishNoise.Hf(s, seg, 4365) * n)) % n;
                    int j = sy * n + sx;
                    bool line = (y * strips % n) < strips || Mathf.Abs(u - joint) * n < 1f;
                    cv.Set(i, wood.Get(j) * tone * (line ? 0.8f : 1f));
                    cv.H[i] = wood.H[j] - (line ? 0.5f : 0f); cv.S[i] = wood.S[j];
                }
            }
            return cv;
        }
    }
}
