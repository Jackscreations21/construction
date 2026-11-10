using UnityEngine;

namespace Groundbreak
{
    /// <summary>Procedural looks for drywall: paper-fiber detail, gypsum core (cut edges), back paper, and the wall
    /// textures (orange peel, knockdown, smooth) used as tiling DETAIL maps over each surface's composite skin.</summary>
    public static class DrywallLooks
    {
        public const string PaperDetail = "dw-paper-detail", Core = "dw-core", Back = "dw-back", OrangePeel = "dw-orangepeel", Knockdown = "dw-knockdown",
            Smooth = "dw-smooth", Bead = "dw-bead", Screw = "dw-screw";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            FinishMaterials.Register(PaperDetail, new FinishMaterials.Spec { Gen = PaperFibers, Tile = 0.35f, NormalStrength = 2.5f });
            FinishMaterials.Register(Core, new FinishMaterials.Spec { Gen = GypsumCore, Tile = 0.12f, Gloss = 0.05f, Bump = 0.9f, NormalStrength = 4f });
            FinishMaterials.Register(Back, new FinishMaterials.Spec { Gen = BackPaper, Tile = 0.5f, Gloss = 0.15f, Bump = 0.4f, NormalStrength = 2f });
            FinishMaterials.Register(OrangePeel, new FinishMaterials.Spec { Gen = OrangePeelTex, Tile = 0.3f, NormalStrength = 2.2f });
            FinishMaterials.Register(Knockdown, new FinishMaterials.Spec { Gen = KnockdownTex, Tile = 0.6f, NormalStrength = 1.8f });
            FinishMaterials.Register(Smooth, new FinishMaterials.Spec { Gen = SmoothTex, Tile = 0.5f, NormalStrength = 1f });
            FinishMaterials.Register(Bead, new FinishMaterials.Spec { Gen = () => FinishMaterials.FineSurface(new Color(0.72f, 0.74f, 0.76f), 5101, 0.05f, 64), Tile = 0.2f, Gloss = 0.55f, Metal = 0.6f, Bump = 0.2f });
            FinishMaterials.Register(Screw, new FinishMaterials.Spec { Gen = null, Tint = new Color(0.13f, 0.13f, 0.14f), Gloss = 0.35f, Metal = 0.5f });
        }

        /// <summary>Neutral (0.5) detail with long paper fibers: multiplies the surface color by ~0.95-1.05.</summary>
        static FinishCanvas PaperFibers()
        {
            const int n = 512; var c = new FinishCanvas(n);
            var f1 = FinishNoise.Field(n, 72, 40, 2, 0.5f, 5001);
            var f2 = FinishNoise.Field(n, 24, 24, 3, 0.5f, 5002);
            var speck = FinishNoise.Field(n, 128, 128, 1, 0.5f, 5003);
            for (int i = 0; i < n * n; i++)
            {
                float a = f1[i] - 0.5f, b = f2[i] - 0.5f, s = speck[i] > 0.78f ? (speck[i] - 0.78f) * 0.5f : 0f;
                float g = 0.5f + a * 0.025f + b * 0.03f - s * 0.2f;
                c.Set(i, new Color(g, g, g));
                c.H[i] = a * 0.6f + b * 0.3f;
            }
            return c;
        }

        static FinishCanvas GypsumCore()
        {
            const int n = 256; var c = new FinishCanvas(n);
            var f = FinishNoise.Field(n, 16, 16, 3, 0.5f, 5011);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    FinishNoise.Worley((x + 0.5f) / n, (y + 0.5f) / n, 40, 5012, out float d1, out _, out _);
                    float pore = d1 < 0.18f ? 1f - d1 / 0.18f : 0f;
                    c.Set(i, new Color(0.91f, 0.90f, 0.85f) * (0.96f + (f[i] - 0.5f) * 0.12f - pore * 0.25f));
                    c.H[i] = f[i] * 0.4f - pore * 0.8f;
                }
            return c;
        }

        static FinishCanvas BackPaper()
        {
            const int n = 256; var c = new FinishCanvas(n);
            var f = FinishNoise.Field(n, 64, 10, 3, 0.5f, 5021);
            for (int i = 0; i < n * n; i++)
            {
                c.Set(i, new Color(0.56f, 0.52f, 0.45f) * (0.95f + (f[i] - 0.5f) * 0.16f));
                c.H[i] = f[i] * 0.4f;
            }
            return c;
        }

        /// <summary>Orange peel: dense small round bumps (2-5 mm) like the skin of an orange.</summary>
        static FinishCanvas OrangePeelTex()
        {
            const int n = 512; var c = new FinishCanvas(n);
            var macro = FinishNoise.Field(n, 6, 6, 2, 0.5f, 5031);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    FinishNoise.Worley((x + 0.5f) / n, (y + 0.5f) / n, 70, 5032, out float d1, out float d2, out int id);
                    float r = 0.35f + FinishNoise.Hf(id, 9, 5033) * 0.35f;
                    float bump = d1 < r ? Mathf.Sqrt(1f - (d1 / r) * (d1 / r)) : 0f;
                    FinishNoise.Worley((x + 0.5f) / n, (y + 0.5f) / n, 130, 5034, out float e1, out _, out _);
                    float small = e1 < 0.3f ? Mathf.Sqrt(1f - (e1 / 0.3f) * (e1 / 0.3f)) * 0.5f : 0f;
                    float g = 0.5f + (bump - 0.4f) * 0.03f + (macro[i] - 0.5f) * 0.02f;
                    c.Set(i, new Color(g, g, g));
                    c.H[i] = bump * 0.8f + small * 0.5f;
                }
            return c;
        }

        /// <summary>Knockdown: splattered mud blobs troweled flat — irregular flat-topped plateaus with soft edges.</summary>
        static FinishCanvas KnockdownTex()
        {
            const int n = 512; var c = new FinishCanvas(n);
            var warp = FinishNoise.Field(n, 8, 8, 3, 0.55f, 5041);
            var fine = FinishNoise.Field(n, 64, 64, 2, 0.5f, 5042);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float u = (x + 0.5f) / n + (warp[i] - 0.5f) * 0.05f, v = (y + 0.5f) / n + (fine[i] - 0.5f) * 0.02f;
                    FinishNoise.Worley(u - Mathf.Floor(u), v - Mathf.Floor(v), 14, 5043, out float d1, out _, out int id);
                    float r = 0.25f + FinishNoise.Hf(id, 4, 5044) * 0.3f;
                    float blob = Mathf.Clamp01((r - d1) / 0.14f);                     // flat top, soft edge
                    float rough = (fine[i] - 0.5f) * 0.15f;
                    float g = 0.5f + blob * 0.012f + rough * 0.05f;
                    c.Set(i, new Color(g, g, g));
                    c.H[i] = blob * 0.9f + rough * blob;
                }
            return c;
        }

        static FinishCanvas SmoothTex()
        {
            const int n = 256; var c = new FinishCanvas(n);
            var f = FinishNoise.Field(n, 4, 4, 2, 0.5f, 5051);
            for (int i = 0; i < n * n; i++) { float g = 0.5f + (f[i] - 0.5f) * 0.01f; c.Set(i, new Color(g, g, g)); c.H[i] = f[i] * 0.05f; }
            return c;
        }
    }
}
